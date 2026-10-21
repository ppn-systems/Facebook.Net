// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Leads;
using Facebook.Net.UI.Core.Threading;
using Facebook.Net.UI.ViewModels.AiAssistant;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests for <see cref="IAiMemoryStore"/>, <see cref="SqliteAiMemoryStore"/>,
/// <see cref="IAgyCliService"/>, <see cref="AgyCliService"/>, <see cref="AiAssistantViewModel"/>,
/// and <see cref="AppConfig.AiSystemPrompt"/>.
/// </summary>
public sealed class AiAssistantAndMemoryStoreTests : IDisposable
{
    private readonly string _tempDir;

    public AiAssistantAndMemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fb_ai_assistant_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_tempDir);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action?.Invoke();
    }

    private sealed class StubBrowserAdapter : IBrowserAdapter
    {
        public string PlatformName => "TestStub";

        public string? LocateExecutablePath() => "C:\\Stub\\chrome.exe";

        public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException("Stub browser adapter does not launch real processes.");
    }

    private sealed class FakeAgyCliService : IAgyCliService
    {
        public int CheckStatusCallCount { get; private set; }

        public int InstallCallCount { get; private set; }

        public int EnsureMcpCallCount { get; private set; }

        public int OAuthCallCount { get; private set; }

        public int SendPromptCallCount { get; private set; }

        public int WarmUpSessionCallCount { get; private set; }

        public int ResetWarmSessionCallCount { get; private set; }

        public PreferredBrowserKind? LastOAuthBrowserKind { get; private set; }

        public string? LastOAuthProfileDir { get; private set; }

        public string? LastUserPrompt { get; private set; }

        public string? LastSystemContextPrompt { get; private set; }

        public string? LastModel { get; private set; }

        public string? LastEffort { get; private set; }

        public string? LastAgyConversationId { get; private set; }

        public string? LastWarmUpAgyConversationId { get; private set; }

        public AgyStatusSnapshot StatusToReturn { get; set; } = new(
            IsInstalled: true,
            InstalledVersion: "0.2.3",
            ExecutablePath: @"C:\Users\Test\AppData\Local\agy\bin\agy.exe",
            IsAuthenticated: true,
            IsFacebookMcpRegistered: true);

        public (bool Success, string Message, string? PendingOAuthUrl) OAuthResultToReturn { get; set; } =
            (true, "Đăng nhập Google Antigravity thành công!", null);

        public AgyPromptResult PromptResultToReturn { get; set; } = new(
            IsSuccess: true,
            ResponseText: "Đã xử lý yêu cầu.");

        public Task<AgyStatusSnapshot> CheckStatusAsync(CancellationToken ct = default)
        {
            this.CheckStatusCallCount++;
            return Task.FromResult(this.StatusToReturn);
        }

        public Task<bool> InstallAgyCliAsync(
            IProgress<(int Percent, string StatusText)>? progress = null,
            CancellationToken ct = default)
        {
            this.InstallCallCount++;
            progress?.Report((100, "Đã cài đặt Google Antigravity CLI thành công!"));
            this.StatusToReturn = this.StatusToReturn with { IsInstalled = true, InstalledVersion = "0.2.3" };
            return Task.FromResult(true);
        }

        public Task<bool> EnsureFacebookMcpRegisteredAsync(string? customMcpExePath = null, CancellationToken ct = default)
        {
            this.EnsureMcpCallCount++;
            this.StatusToReturn = this.StatusToReturn with { IsFacebookMcpRegistered = true };
            return Task.FromResult(true);
        }

        public Task<(bool Success, string Message, string? PendingOAuthUrl)> RunCdpBrowserOAuthLoginAsync(
            IBrowserManager browserManager,
            PreferredBrowserKind browserKind,
            string? customProfileDir = null,
            IProgress<string>? statusProgress = null,
            CancellationToken ct = default)
        {
            this.OAuthCallCount++;
            this.LastOAuthBrowserKind = browserKind;
            this.LastOAuthProfileDir = customProfileDir;
            if (this.OAuthResultToReturn.Success)
            {
                this.StatusToReturn = this.StatusToReturn with { IsAuthenticated = true };
            }

            return Task.FromResult(this.OAuthResultToReturn);
        }

        public Task<bool> SubmitManualOAuthCodeAsync(string authCode, CancellationToken ct = default)
        {
            this.StatusToReturn = this.StatusToReturn with { IsAuthenticated = true };
            return Task.FromResult(true);
        }

        public Task WarmUpSessionAsync(
            string? model = null,
            string? effort = null,
            string? agyConversationId = null,
            CancellationToken ct = default)
        {
            this.WarmUpSessionCallCount++;
            this.LastWarmUpAgyConversationId = agyConversationId;
            return Task.CompletedTask;
        }

        public void ResetWarmSession() => this.ResetWarmSessionCallCount++;

        public Task<AgyPromptResult> SendPromptAsync(
            string userPrompt,
            string? systemContextPrompt = null,
            bool continueConversation = true,
            IProgress<string>? streamingProgress = null,
            string? model = null,
            string? effort = null,
            string? agyConversationId = null,
            CancellationToken ct = default)
        {
            this.SendPromptCallCount++;
            this.LastUserPrompt = userPrompt;
            this.LastSystemContextPrompt = systemContextPrompt;
            this.LastModel = model;
            this.LastEffort = effort;
            this.LastAgyConversationId = agyConversationId;
            streamingProgress?.Report(this.PromptResultToReturn.ResponseText);
            return Task.FromResult(this.PromptResultToReturn);
        }

        public string? ResolveFacebookMcpExecutablePath() => @"C:\Stub\Facebook.Net.Mcp.exe";
    }

    [Fact]
    public async Task AiMemoryStore_SaveQueryDeleteAndSystemContext_PersistsUpsertsAndPreloadsSystemPrompt()
    {
        string dbPath = Path.Combine(_tempDir, "facebook_ai_memory.db");
        SqliteAiMemoryStore store = new(dbPath);
        await store.InitializeAsync();

        AiMemoryRecord first = await store.SaveMemoryAsync(
            AiMemoryCategory.ProductKnowledge,
            "Sản Phẩm Chủ Lực",
            "Áo khoác kaki 1 lớp giá 250k",
            importance: 4,
            accountUid: "10001");

        // Upsert by (category, LOWER(key)) with different casing
        AiMemoryRecord upserted = await store.SaveMemoryAsync(
            AiMemoryCategory.ProductKnowledge,
            "sản phẩm chủ lực",
            "Áo khoác kaki 2 lớp chống nước giá 320k",
            importance: 5,
            accountUid: "10001");

        Assert.Equal(first.Id, upserted.Id);
        Assert.Equal("Áo khoác kaki 2 lớp chống nước giá 320k", upserted.Content);
        Assert.Equal(5, upserted.Importance);

        _ = await store.SaveMemoryAsync(
            AiMemoryCategory.WritingStyle,
            "Giọng văn thương hiệu",
            "Ngắn gọn, lịch sự, tập trung vào ưu đãi miễn phí vận chuyển",
            importance: 4,
            accountUid: null);

        IReadOnlyList<AiMemoryRecord> productMemories = await store.QueryMemoriesAsync(
            category: AiMemoryCategory.ProductKnowledge,
            accountUid: "10001");
        _ = Assert.Single(productMemories);

        string customSystemPrompt = "Chỉ dẫn hệ thống riêng cho Shop Thời Trang Nam";
        string contextPrompt = await store.BuildSystemContextPromptAsync(
            customSystemPrompt,
            "10001",
            "Alice");

        Assert.Contains(customSystemPrompt, contextPrompt, StringComparison.Ordinal);
        Assert.Contains("Alice", contextPrompt, StringComparison.Ordinal);
        Assert.Contains("10001", contextPrompt, StringComparison.Ordinal);
        Assert.Contains("Áo khoác kaki 2 lớp chống nước giá 320k", contextPrompt, StringComparison.Ordinal);
        Assert.Contains("Ngắn gọn, lịch sự, tập trung vào ưu đãi miễn phí vận chuyển", contextPrompt, StringComparison.Ordinal);

        // Verify chat history & action execution state
        AiChatMessageRecord msg = await store.AppendChatMessageAsync(
            "assistant",
            "Kế hoạch đăng 3 nhóm",
            "PUBLISH_GROUPS",
            "{\"actionKind\":\"PUBLISH_GROUPS\",\"targetGroupCount\":3}");
        Assert.False(msg.IsActionExecuted);

        await store.MarkChatActionExecutedAsync(msg.Id);
        IReadOnlyList<AiChatMessageRecord> chatHistory = await store.GetRecentChatHistoryAsync(10);
        _ = Assert.Single(chatHistory);
        Assert.True(chatHistory[0].IsActionExecuted);

        bool deleted = await store.DeleteMemoryAsync("sản phẩm chủ lực");
        Assert.True(deleted);

        int cleared = await store.ClearMemoriesAsync();
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task AiAssistantViewModel_SystemPrompt_LoadsFromSettingsAndPersistsOnSaveAndReset()
    {
        string workspaceDir = Path.Combine(_tempDir, "system_prompt_test");
        FakeAgyCliService fakeAgy = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);
        using ShellViewModel vm = new(context);

        Assert.Equal(string.Empty, vm.AiAssistant.SystemPromptText);
        Assert.Equal(string.Empty, context.Settings.Current.AiSystemPrompt);
        Assert.Equal("gemini-3.8-flash-low", vm.AiAssistant.SelectedAiModel);
        Assert.Equal("low", vm.AiAssistant.SelectedAiEffort);

        const string CustomPrompt = "Chỉ dẫn tùy chỉnh cho shop thời trang";
        vm.AiAssistant.SystemPromptText = CustomPrompt;
        await vm.AiAssistant.SaveSystemPromptCommand.ExecuteAsync(null);

        Assert.Equal(CustomPrompt, vm.AiAssistant.SystemPromptText);
        Assert.Equal(CustomPrompt, context.Settings.Current.AiSystemPrompt);

        await vm.AiAssistant.ResetSystemPromptCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.AiAssistant.SystemPromptText);
        Assert.Equal(string.Empty, context.Settings.Current.AiSystemPrompt);
    }

    [Fact]
    public async Task AiAssistantViewModel_LazyCheckRule_NeverChecksAgyOnStartup_OnlyWhenPanelOpened()
    {
        string workspaceDir = Path.Combine(_tempDir, "lazy_check_test");
        FakeAgyCliService fakeAgy = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);
        using ShellViewModel vm = new(context);

        Assert.Equal(0, fakeAgy.CheckStatusCallCount);
        Assert.Equal(AiAssistantSetupStage.NotCheckedYet, vm.AiAssistant.SetupStage);

        await vm.InitializeAsync();

        // Must NEVER check agy CLI during application startup
        Assert.Equal(0, fakeAgy.CheckStatusCallCount);
        Assert.Equal(AiAssistantSetupStage.NotCheckedYet, vm.AiAssistant.SetupStage);

        // Open AI Assistant panel on demand
        vm.ToggleAiAssistantCommand.Execute(null);
        await vm.AiAssistant.EnsureInitializedWhenOpenedAsync();

        Assert.True(vm.AiAssistant.IsPanelOpen);
        Assert.True(fakeAgy.CheckStatusCallCount >= 1);
    }

    [Fact]
    public async Task AiAssistantViewModel_WhenAlreadyInstalledAndAuthenticated_SkipsInstallAndLogin_GoesStraightToReady()
    {
        string workspaceDir = Path.Combine(_tempDir, "already_ready_test");
        FakeAgyCliService fakeAgy = new()
        {
            StatusToReturn = new AgyStatusSnapshot(
                IsInstalled: true,
                InstalledVersion: "0.2.3",
                ExecutablePath: @"C:\Users\Test\AppData\Local\agy\bin\agy.exe",
                IsAuthenticated: true,
                IsFacebookMcpRegistered: true)
        };

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);
        using ShellViewModel vm = new(context);

        vm.ToggleAiAssistantCommand.Execute(null);
        await vm.AiAssistant.EnsureInitializedWhenOpenedAsync();

        Assert.Equal(AiAssistantSetupStage.Ready, vm.AiAssistant.SetupStage);
        Assert.True(vm.AiAssistant.IsReadyStage);
        Assert.Equal(0, fakeAgy.InstallCallCount);
        Assert.Equal(0, fakeAgy.OAuthCallCount);
    }

    [Fact]
    public async Task AiAssistantViewModel_WhenInstalledButNotAuthenticated_RunsCdpBrowserOAuthWithSelectedBrowser()
    {
        string workspaceDir = Path.Combine(_tempDir, "cdp_oauth_test");
        FakeAgyCliService fakeAgy = new()
        {
            StatusToReturn = new AgyStatusSnapshot(
                IsInstalled: true,
                InstalledVersion: "0.2.3",
                ExecutablePath: @"C:\Users\Test\AppData\Local\agy\bin\agy.exe",
                IsAuthenticated: false,
                IsFacebookMcpRegistered: true)
        };

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);
        using ShellViewModel vm = new(context);

        await vm.AiAssistant.EnsureInitializedWhenOpenedAsync();
        Assert.Equal(AiAssistantSetupStage.NeedsAuth, vm.AiAssistant.SetupStage);
        Assert.True(vm.AiAssistant.IsNeedsAuthStage);

        vm.AiAssistant.SelectedAuthBrowser = PreferredBrowserKind.Chrome;
        await vm.AiAssistant.StartCdpOAuthLoginCommand.ExecuteAsync(null);

        Assert.Equal(1, fakeAgy.OAuthCallCount);
        Assert.Equal(PreferredBrowserKind.Chrome, fakeAgy.LastOAuthBrowserKind);
        Assert.Equal(AiAssistantSetupStage.Ready, vm.AiAssistant.SetupStage);
    }

    [Fact]
    public async Task AiAssistantViewModel_SendMessage_PreloadsSystemPromptAndMemoryContext_AndExecutesProposedAction()
    {
        string workspaceDir = Path.Combine(_tempDir, "send_message_action_test");
        FakeAgyCliService fakeAgy = new();

        string rawResponseWithActionTag =
            "Tôi đã chuẩn bị bài đăng khuyến mãi cho 3 nhóm mục tiêu.\n" +
            "[ACTION:PUBLISH_GROUPS|count=3|minDelay=20|maxDelay=45|content=Khuyến mãi đặc biệt giảm 30% hôm nay]";
        (string cleanText, AgyProposedAction? parsedAction) = AgyCliService.ParseResponseAndProposedAction(rawResponseWithActionTag);

        fakeAgy.PromptResultToReturn = new AgyPromptResult(
            IsSuccess: true,
            ResponseText: cleanText,
            ProposedAction: parsedAction);

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);
        using ShellViewModel vm = new(context);

        await vm.InitializeAsync();
        await vm.AiAssistant.EnsureInitializedWhenOpenedAsync();

        vm.AiAssistant.SystemPromptText = "Chỉ dẫn hệ thống chuyên biệt cho Bất Động Sản";
        await vm.AiAssistant.SaveSystemPromptCommand.ExecuteAsync(null);

        vm.AiAssistant.NewMemoryCategory = AiMemoryCategory.ProductKnowledge;
        vm.AiAssistant.NewMemoryKey = "Dự án trọng điểm";
        vm.AiAssistant.NewMemoryContent = "Căn hộ Vinhomes Grand Park chiết khấu 10%";
        await vm.AiAssistant.SaveMemoryCommand.ExecuteAsync(null);

        vm.AiAssistant.InputPromptText = "Soạn và chuẩn bị kế hoạch đăng bài vào 3 nhóm";
        await vm.AiAssistant.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(1, fakeAgy.SendPromptCallCount);
        Assert.NotNull(fakeAgy.LastSystemContextPrompt);
        Assert.Contains("Chỉ dẫn hệ thống chuyên biệt cho Bất Động Sản", fakeAgy.LastSystemContextPrompt, StringComparison.Ordinal);
        Assert.Contains("Căn hộ Vinhomes Grand Park chiết khấu 10%", fakeAgy.LastSystemContextPrompt, StringComparison.Ordinal);

        Assert.Equal(2, vm.AiAssistant.Messages.Count);
        AiChatBubbleItem assistantBubble = vm.AiAssistant.Messages[1];
        Assert.True(assistantBubble.IsAssistant);
        Assert.True(assistantBubble.HasProposedAction);
        Assert.NotNull(assistantBubble.ProposedAction);
        Assert.Equal("PUBLISH_GROUPS", assistantBubble.ProposedAction.ActionKind);
        Assert.Equal(3, assistantBubble.ProposedAction.TargetGroupCount);
        Assert.Equal("Khuyến mãi đặc biệt giảm 30% hôm nay", assistantBubble.ProposedAction.ContentText);

        await vm.AiAssistant.ExecuteProposedActionCommand.ExecuteAsync(assistantBubble);
        Assert.True(assistantBubble.IsActionExecuted);
        Assert.Equal(NavigationSection.Publisher, vm.CurrentSection);
        Assert.Equal("Khuyến mãi đặc biệt giảm 30% hôm nay", vm.PostSpintaxContent);
    }

    [Fact]
    public async Task AgyCliService_ResolveAgyExecutablePath_DetectsLocalAppDataAgyBinDirectory()
    {
        string agyWorkspace = Path.Combine(_tempDir, "agy_cli_service_test");
        using AgyCliService service = new(agyWorkspace);

        AgyStatusSnapshot status = await service.CheckStatusAsync();

        string localAppDataAgyExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy",
            "bin",
            "agy.exe");

        if (File.Exists(localAppDataAgyExe))
        {
            Assert.True(status.IsInstalled);
            Assert.False(string.IsNullOrWhiteSpace(status.ExecutablePath));
        }

        string? mcpPath = service.ResolveFacebookMcpExecutablePath();
        if (mcpPath is not null)
        {
            Assert.True(File.Exists(mcpPath));
        }
    }

    [Fact]
    public async Task AiMemoryStore_And_ViewModel_SupportMultiConversationHistoryAndResume()
    {
        string workspaceDir = Path.Combine(_tempDir, "multi_conversation_resume_test");
        FakeAgyCliService fakeAgy = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);

        await context.AiMemoryStore.InitializeAsync();

        _ = await context.AiMemoryStore.SaveConversationAsync(
            conversationId: "conv_1",
            title: "Chiến dịch Thời trang",
            agyConversationId: "agy_uuid_1");
        _ = await context.AiMemoryStore.AppendChatMessageAsync(
            role: "user",
            content: "Tư vấn áo khoác nam mùa đông",
            conversationId: "conv_1");
        _ = await context.AiMemoryStore.AppendChatMessageAsync(
            role: "assistant",
            content: "Đây là mẫu bài đăng áo khoác nam mùa đông",
            conversationId: "conv_1");

        _ = await context.AiMemoryStore.SaveConversationAsync(
            conversationId: "conv_2",
            title: "Chiến dịch Mỹ phẩm",
            agyConversationId: "agy_uuid_2");
        _ = await context.AiMemoryStore.AppendChatMessageAsync(
            role: "user",
            content: "Tư vấn son dưỡng môi",
            conversationId: "conv_2");

        IReadOnlyList<AiConversationThread> threads = await context.AiMemoryStore.GetConversationsAsync(50);
        Assert.Equal(2, threads.Count);

        AiConversationThread? conv1 = null;
        AiConversationThread? conv2 = null;
        for (int i = 0; i < threads.Count; i++)
        {
            if (string.Equals(threads[i].Id, "conv_1", StringComparison.Ordinal))
            {
                conv1 = threads[i];
            }
            else if (string.Equals(threads[i].Id, "conv_2", StringComparison.Ordinal))
            {
                conv2 = threads[i];
            }
        }

        Assert.NotNull(conv1);
        Assert.Equal("agy_uuid_1", conv1.AgyConversationId);
        Assert.Equal(2, conv1.MessageCount);

        Assert.NotNull(conv2);
        Assert.Equal("agy_uuid_2", conv2.AgyConversationId);
        Assert.Equal(1, conv2.MessageCount);

        using ShellViewModel vm = new(context);
        await vm.InitializeAsync();
        await vm.AiAssistant.EnsureInitializedWhenOpenedAsync();

        await vm.AiAssistant.SelectConversationCommand.ExecuteAsync(conv1);

        Assert.Equal("conv_1", vm.AiAssistant.ActiveConversationId);
        Assert.Equal("agy_uuid_1", vm.AiAssistant.ActiveAgyConversationId);
        Assert.Equal(2, vm.AiAssistant.Messages.Count);
        Assert.Equal("Tư vấn áo khoác nam mùa đông", vm.AiAssistant.Messages[0].Content);
        Assert.Equal("Đây là mẫu bài đăng áo khoác nam mùa đông", vm.AiAssistant.Messages[1].Content);

        fakeAgy.PromptResultToReturn = new AgyPromptResult(
            IsSuccess: true,
            ResponseText: "Đã chuẩn bị thêm 2 nhóm thời trang.",
            ConversationId: "agy_uuid_1");

        vm.AiAssistant.InputPromptText = "Tiếp tục đăng thêm 2 nhóm nữa";
        await vm.AiAssistant.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal("agy_uuid_1", fakeAgy.LastAgyConversationId);
        Assert.Equal(4, vm.AiAssistant.Messages.Count);

        bool deletedConv2 = await context.AiMemoryStore.DeleteConversationAsync("conv_2");
        Assert.True(deletedConv2);
    }

    [Fact]
    public void AgyCliService_TryParseStreamJsonLine_ExtractsConversationIdAndRealTimeTextDeltas()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));

        const string InitLine = "{\"event\":\"init\",\"conversation_id\":\"1e88be44-a038-4212-a969-8bfe06910ddb\",\"init\":{\"model\":\"gemini-3.8-flash-low\"}}";
        (bool initDone, string? initConvId, string? initDelta, string? initFinal, string? initErr) =
            AgyCliService.TryParseStreamJsonLine(InitLine);

        Assert.False(initDone);
        Assert.Equal("1e88be44-a038-4212-a969-8bfe06910ddb", initConvId);
        Assert.Null(initDelta);
        Assert.Null(initFinal);
        Assert.Null(initErr);

        const string DeltaLine = "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"1e88be44-a038-4212-a969-8bfe06910ddb\",\"step_index\":1,\"state\":\"ACTIVE\",\"step_type\":\"agent_response\",\"text_delta\":\"Xin chào bạn!\"}}";
        (bool deltaDone, string? deltaConvId, string? textDelta, string? deltaFinal, string? deltaErr) =
            AgyCliService.TryParseStreamJsonLine(DeltaLine);

        Assert.False(deltaDone);
        Assert.Equal("1e88be44-a038-4212-a969-8bfe06910ddb", deltaConvId);
        Assert.Equal("Xin chào bạn!", textDelta);
        Assert.Null(deltaFinal);
        Assert.Null(deltaErr);

        const string ResultLine = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"1e88be44-a038-4212-a969-8bfe06910ddb\",\"status\":\"SUCCESS\",\"response\":\"Xin chào bạn!\\n\"}}";
        (bool resultDone, string? resultConvId, string? resultDelta, string? finalResponse, string? resultErr) =
            AgyCliService.TryParseStreamJsonLine(ResultLine);

        Assert.True(resultDone);
        Assert.Equal("1e88be44-a038-4212-a969-8bfe06910ddb", resultConvId);
        Assert.Null(resultDelta);
        Assert.Equal("Xin chào bạn!\n", finalResponse);
        Assert.Null(resultErr);
    }

    [Fact]
    public void LeadScoringService_ExtractVietnamesePhoneNumbers_HandlesAllObfuscations_AndRejectsFalsePositives()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));
        LeadScoringService scorer = LeadScoringService.Instance;

        // 1. Dots, commas, spaces, hyphens, parentheses
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Liên hệ ngay: 098.765.4321 hoặc inbox"));
        Assert.Equal("0912345678", scorer.ExtractPrimaryPhoneNumber("SĐT: 0912,345,678"));
        Assert.Equal("0389998888", scorer.ExtractPrimaryPhoneNumber("Zalo (038) - 999 - 8888"));
        Assert.Equal("0776543210", scorer.ExtractPrimaryPhoneNumber("Gọi 0 7 7 6 5 4 3 2 1 0 nhé"));

        // 2. Emoji & icon separators between digit groups
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Chốt đơn 098🔥765🔥4321 giao ngay"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Hotline 098☎️765☎️4321"));
        Assert.Equal("0912345678", scorer.ExtractPrimaryPhoneNumber("Alo 0912👉345👉678"));

        // 3. Keycap emojis, circled digits, fullwidth & math bold digits
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Số zalo: 0️⃣9️⃣8️⃣7️⃣6️⃣5️⃣4️⃣3️⃣2️⃣1️⃣"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Gọi ngay: ⓪⑨⑧.⑦⑥⑤.④③②①"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Fullwidth: ０９８.７６５.４３２１"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Math bold: 𝟎𝟗𝟖𝟕𝟔𝟓𝟒𝟑𝟐𝟏"));

        // 4. Letter O/o lookalikes for zero
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Sđt O987.654.321"));
        Assert.Equal("0980123456", scorer.ExtractPrimaryPhoneNumber("Sđt 098O.123.456"));
        Assert.Equal("0389998888", scorer.ExtractPrimaryPhoneNumber("Zalo o38,999,8888"));

        // 5. Vietnamese spoken digit words
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Gọi số không chín tám 765 4321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Alo 0987 sáu năm bốn 321 nha"));

        // 6. +84 / 84 / 0084 country code normalization
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH +84 987 654 321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH (+84) 0987.654.321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH 0084 987 654 321"));

        // 7. False-positive rejection: 15-digit Facebook IDs and prices like 090.000.000 / 100.000.000
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("https://www.facebook.com/groups/100098765432100/posts/998877665544332"));
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("Giá bán: 090.000.000 VNĐ thương lượng"));
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("Giá chỉ 100.000.000 đồng"));

        // 8. Search keyword matching against obfuscated phone numbers
        Assert.True(scorer.MatchesBuyerKeywords("Liên hệ O98🔥765🔥4321", ["0987654321"]));
    }

    [Fact]
    public void ShellViewModel_MultiPhotoAndUpdateVersionParsing_WorkCorrectly()
    {
        string img1 = Path.Combine(_tempDir, "photo1.jpg");
        string img2 = Path.Combine(_tempDir, "photo2.png");
        string missingImg = Path.Combine(_tempDir, "missing.jpg");
        File.WriteAllBytes(img1, [0xFF, 0xD8, 0xFF]);
        File.WriteAllBytes(img2, [0x89, 0x50, 0x4E, 0x47]);

        string rawPaths = $"{img1} ; {img2} ; {missingImg} ; {img1}";
        List<string> existing = ShellViewModel.ParseExistingMediaFilePaths(rawPaths);

        Assert.Equal(2, existing.Count);
        Assert.Equal(img1, existing[0]);
        Assert.Equal(img2, existing[1]);

        Version v1 = ShellViewModel.TryParseReleaseVersion("v1.2.4");
        Version v2 = ShellViewModel.TryParseReleaseVersion("v1.3.0-preview.1");
        Assert.True(v2 > v1);
        Assert.Equal(new Version(1, 2, 4), v1);
        Assert.Equal(new Version(1, 3, 0), v2);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore locked temp files
        }
    }
}
