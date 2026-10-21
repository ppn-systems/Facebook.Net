// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Mcp;
using Facebook.Net.Mcp.Tools;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Leads;
using Facebook.Net.UI.Core.Threading;
using Facebook.Net.UI.ViewModels.AiAssistant;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests for <see cref="IAiMemoryStore"/>, <see cref="SqliteAiMemoryStore"/>,
/// <see cref="IAgyCliService"/>, <see cref="AgyCliService"/>, <see cref="AiAssistantViewModel"/>,
/// and <see cref="AppConfig.AiSystemPrompt"/>.
/// </summary>
public sealed partial class AiAssistantAndMemoryStoreTests : IDisposable
{
    [GeneratedRegex(@"\bfb_[a-z0-9_]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex FbToolNameRegex();
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

    [Fact]
    public void AgyCliService_ParseResponseActionAndMemories_StripsLearnTagsAndParsesAllNewActions()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));

        string rawText =
            "Chào anh Minh, bên em có sẵn mẫu bàn ghế gỗ sồi giá xưởng.\n" +
            "[LEARN:ProductKnowledge|sản_phẩm_nội_thất|Bàn ghế gỗ sồi tự nhiên giá xưởng bảo hành 5 năm]\n" +
            "[LEARN:WritingStyle|phong_cách_tư_vấn|Lịch sự, xưng em gọi anh chị]\n" +
            "[ACTION:COMMENT_LEAD|targetId=post_998877|content=Chào {author_name}, em gửi báo giá gỗ sồi qua inbox nhé!]";

        (string cleanText, AgyProposedAction? action, IReadOnlyList<AgyLearnedMemoryItem> memories) =
            AgyCliService.ParseResponseActionAndMemories(rawText);

        Assert.Equal("Chào anh Minh, bên em có sẵn mẫu bàn ghế gỗ sồi giá xưởng.", cleanText);
        Assert.DoesNotContain("[LEARN:", cleanText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[ACTION:", cleanText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, memories.Count);
        Assert.Equal(AiMemoryCategory.ProductKnowledge, memories[0].Category);
        Assert.Equal("sản_phẩm_nội_thất", memories[0].Key);
        Assert.Equal("Bàn ghế gỗ sồi tự nhiên giá xưởng bảo hành 5 năm", memories[0].Content);

        Assert.NotNull(action);
        Assert.Equal("COMMENT_LEAD", action.ActionKind);
        Assert.Equal("post_998877", action.TargetId);
        Assert.Equal("Chào {author_name}, em gửi báo giá gỗ sồi qua inbox nhé!", action.ContentText);

        // Verify FILTER_LEADS and BUMP_POSTS action parsing
        (_, AgyProposedAction? filterAction, _) = AgyCliService.ParseResponseActionAndMemories(
            "Đã lọc danh sách khách hàng tiềm năng.\n[ACTION:FILTER_LEADS|keyword=cần mua gấp]");
        Assert.NotNull(filterAction);
        Assert.Equal("FILTER_LEADS", filterAction.ActionKind);
        Assert.Equal("cần mua gấp", filterAction.GroupKeyword);

        (_, AgyProposedAction? bumpAction, _) = AgyCliService.ParseResponseActionAndMemories(
            "Sẵn sàng đẩy tương tác bài viết.\n[ACTION:BUMP_POSTS|content={Up|Chấm} bài lên top]");
        Assert.NotNull(bumpAction);
        Assert.Equal("BUMP_POSTS", bumpAction.ActionKind);
        Assert.Equal("{Up|Chấm} bài lên top", bumpAction.ContentText);
    }

    [Fact]
    public void LeadScoringService_AnalyzeWorkspaceData_ComputesGroupRoiRepeatAuthorsAndPeakHours()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));
        LeadScoringService scorer = LeadScoringService.Instance;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SavedTargetGroup> groups =
        [
            new SavedTargetGroup("grp_1", "Chợ Nội Thất Sài Gòn", IsEnabled: true, TagsCsv: "tag_furniture"),
            new SavedTargetGroup("grp_2", "Hội Mua Bán Chung Cư", IsEnabled: true, TagsCsv: string.Empty)
        ];

        List<PostSummary> posts =
        [
            new PostSummary(
                PostId: "p1",
                AuthorUid: "u_buyer_1",
                AuthorName: "Nguyễn Văn An",
                MessageText: "Cần mua gấp sofa phòng khách, liên hệ 0987.654.321 báo giá giúp mình?",
                PermalinkUrl: "https://facebook.com/groups/grp_1/posts/p1",
                ReactionCount: 5,
                CommentCount: 0,
                CreatedAt: now.AddMinutes(-30),
                GroupId: "grp_1",
                GroupName: "Chợ Nội Thất Sài Gòn"),
            new PostSummary(
                PostId: "p2",
                AuthorUid: "u_seeder",
                AuthorName: "Trần Môi Giới",
                MessageText: "Xả kho sofa giá rẻ",
                PermalinkUrl: "https://facebook.com/groups/grp_1/posts/p2",
                ReactionCount: 1,
                CommentCount: 12,
                CreatedAt: now.AddHours(-2),
                GroupId: "grp_1",
                GroupName: "Chợ Nội Thất Sài Gòn"),
            new PostSummary(
                PostId: "p3",
                AuthorUid: "u_seeder",
                AuthorName: "Trần Môi Giới",
                MessageText: "Xả kho bàn ăn giá rẻ",
                PermalinkUrl: "https://facebook.com/groups/grp_2/posts/p3",
                ReactionCount: 2,
                CommentCount: 8,
                CreatedAt: now.AddHours(-3),
                GroupId: "grp_2",
                GroupName: "Hội Mua Bán Chung Cư")
        ];

        HashSet<string> commentedIds = new(StringComparer.Ordinal) { "p1" };

        WorkspaceDataAnalyticsReport report = scorer.AnalyzeWorkspaceData(
            posts,
            groups,
            commentedIds,
            "cần mua, báo giá, sofa");

        Assert.Equal(3, report.TotalScrapedPosts);
        Assert.Equal(1, report.TotalPostsWithPhone);
        Assert.Equal(1, report.TotalHighIntentPosts);
        Assert.Equal(1, report.TotalZeroCommentOpportunities);
        Assert.Equal(1, report.TotalCommentedPosts);
        Assert.Equal(2, report.TotalPendingPosts);
        Assert.NotEmpty(report.TopPerformingGroups);
        Assert.Equal("grp_1", report.TopPerformingGroups[0].GroupId);
        _ = Assert.Single(report.RepeatAuthors);
        Assert.Equal("Trần Môi Giới", report.RepeatAuthors[0].AuthorName);
        Assert.Equal(2, report.RepeatAuthors[0].PostCount);

        string formatted = scorer.FormatAnalyticsReportForAi(report);
        Assert.Contains("Chợ Nội Thất Sài Gòn", formatted, StringComparison.Ordinal);
        Assert.Contains("Trần Môi Giới", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerLeadTags_And_CrmNotes_PersistAndTriggerSilentAiLearning()
    {
        string workspaceDir = Path.Combine(_tempDir, "customer_tags_and_notes_test");
        FakeAgyCliService fakeAgy = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);

        PostSummary sampleLead = new(
            PostId: "lead_post_101",
            AuthorUid: "uid_khach_1",
            AuthorName: "Lê Hoàng Nam",
            MessageText: "Mình cần tìm căn hộ 2PN Quận 7 giá tầm 3 tỷ, sđt 0912345678",
            PermalinkUrl: "https://facebook.com/groups/grp_101/posts/lead_post_101",
            ReactionCount: 3,
            CommentCount: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            GroupId: "grp_101",
            GroupName: "Nhóm Mua Bán Nhà Đất");

        await context.AccountStore.InitializeAsync();
        await context.AccountStore.UpsertScrapedPostsAsync([sampleLead]);

        using ShellViewModel vm = new(context);
        await vm.InitializeAsync();

        // Default 5 seeded customer tags should be loaded
        Assert.True(vm.LeadTags.Count >= 5);

        GroupTag? createdLeadTag = await vm.CreateQuickLeadTagAsync("Khách VIP Quận 7", "#EC4899");
        Assert.NotNull(createdLeadTag);
        Assert.Equal("Khách VIP Quận 7", createdLeadTag.Name);

        await vm.ToggleLeadPostTagAsync(sampleLead, createdLeadTag.Id);
        await vm.UpdateLeadPostNotesAsync(sampleLead, "Đã gọi điện tư vấn căn hộ Sunrise City, hẹn thứ 7 xem nhà");

        IReadOnlyList<PostSummary> reloaded = await context.AccountStore.QueryScrapedPostsAsync(groupId: "grp_101");
        PostSummary updatedPost = Assert.Single(reloaded);
        Assert.True(updatedPost.HasTag(createdLeadTag.Id));
        Assert.Equal("Đã gọi điện tư vấn căn hộ Sunrise City, hẹn thứ 7 xem nhà", updatedPost.Notes);
    }

    [Fact]
    public void McpServerTools_And_SystemPrompts_Have100PercentSchemaParity()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));

        HashSet<string> registeredMcpTools = new(StringComparer.Ordinal);
        CollectTools<AccountTools>(registeredMcpTools);
        CollectTools<GroupTools>(registeredMcpTools);
        CollectTools<PublisherTools>(registeredMcpTools);
        CollectTools<SalesLeadsTools>(registeredMcpTools);
        CollectTools<AiMemoryTools>(registeredMcpTools);

        // Verify all 40 MCP tools are registered
        Assert.Equal(40, registeredMcpTools.Count);
        Assert.Contains("fb_analyze_workspace_data", registeredMcpTools);
        Assert.Contains("fb_auto_tag_groups_by_keywords", registeredMcpTools);
        Assert.Contains("fb_clean_and_classify_all_groups", registeredMcpTools);
        Assert.Contains("fb_delete_group_tag", registeredMcpTools);
        Assert.Contains("fb_delete_lead_tag", registeredMcpTools);
        Assert.Contains("fb_search_leads_advanced", registeredMcpTools);
        Assert.Contains("fb_create_lead_tags_batch", registeredMcpTools);

        // Verify every fb_* tool mentioned in System Prompts, Resources, and Prompts exists in the MCP Server
        string analyzePromptText = ((TextContentBlock)Prompts.AnalyzeGroupLeads("test").Messages[0].Content).Text;
        string composePromptText = ((TextContentBlock)Prompts.ComposeSpintaxCampaign("test").Messages[0].Content).Text;

        string[] promptSources =
        [
            AppConfig.DefaultAiSystemPrompt,
            Resources.Overview(),
            Resources.SalesAgentGuide(),
            analyzePromptText,
            composePromptText
        ];

        Regex toolRegex = FbToolNameRegex();
        foreach (string source in promptSources)
        {
            foreach (Match match in toolRegex.Matches(source))
            {
                string mentionedTool = match.Value;
                Assert.Contains(mentionedTool, registeredMcpTools);
            }
        }
    }

    private static void CollectTools<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        HashSet<string> target)
    {
        foreach (MethodInfo method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            McpServerToolAttribute? attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Name))
            {
                _ = target.Add(attr.Name);
            }
        }
    }

    [Fact]
    public void LeadScoringService_CalculateBuyerIntentScore_ScoresHighIntentEvenWhenKeywordsEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tempDir));
        LeadScoringService scorer = LeadScoringService.Instance;

        int scoreWithEmptyKeywords = scorer.CalculateBuyerIntentScore(
            "Mình cần mua bàn ghế văn phòng gấp, liên hệ báo giá giúp mình qua số 0987.654.321 nhé?",
            rawKeywords: string.Empty);

        Assert.True(scoreWithEmptyKeywords >= 75);
    }

    [Fact]
    public async Task SqliteAccountStore_InitializeAsync_RepairsMassTagBdsPollution_AndNormalizesLegacyTags()
    {
        string workspaceDir = Path.Combine(_tempDir, "tag_pollution_repair_test");
        FakeAgyCliService fakeAgy = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            agyCliService: fakeAgy);

        await context.AccountStore.InitializeAsync();

        // Simulate 12 groups where all 12 were accidentally mass-tagged with tag_bds + legacy tag_jobs/tag_sales
        string[] sampleNames =
        [
            "Cộng đồng C# .NET Việt Nam",
            "Hội lập trình viên Claude AI & ChatGPT",
            "Tuyển dụng IT Việc làm Phần mềm",
            "Chợ Mua Bán Đồ Cũ Sài Gòn",
            "Mua Bán Nhà Đất Bất Động Sản Quận 7",
            "Hội Sinh Viên Giao Lưu Kết Bạn",
            "Lập trình Python & Data Science",
            "Chợ Thanh Lý Nội Thất Gia Đình",
            "Tuyển Dụng Nhân Sự Freelance",
            "Cộng đồng ReactJS & Frontend Dev",
            "VOZ Góc Công Nghệ",
            "Hội Chung Cư Vinhomes Grand Park"
        ];

        for (int i = 0; i < sampleNames.Length; i++)
        {
            await context.AccountStore.UpsertTargetGroupAsync(new SavedTargetGroup(
                GroupId: $"grp_{i + 1}",
                GroupName: sampleNames[i],
                IsEnabled: true,
                TagsCsv: "tag_bds,tag_jobs,tag_sales"));
        }

        // Re-run InitializeAsync to trigger NormalizeAndCleanGroupTagsAsync
        await context.AccountStore.InitializeAsync();

        IReadOnlyList<SavedTargetGroup> cleaned = await context.AccountStore.GetAllTargetGroupsAsync();
        Assert.Equal(12, cleaned.Count);

        SavedTargetGroup? dotNetGroup = null;
        SavedTargetGroup? bdsGroup = null;
        for (int i = 0; i < cleaned.Count; i++)
        {
            if (string.Equals(cleaned[i].GroupId, "grp_1", StringComparison.Ordinal))
            {
                dotNetGroup = cleaned[i];
            }
            else if (string.Equals(cleaned[i].GroupId, "grp_5", StringComparison.Ordinal))
            {
                bdsGroup = cleaned[i];
            }
        }

        Assert.NotNull(dotNetGroup);
        Assert.False(dotNetGroup.HasTag("tag_bds"));
        Assert.True(dotNetGroup.HasTag("tag_tech"));
        Assert.True(dotNetGroup.HasTag("tag_job"));
        Assert.True(dotNetGroup.HasTag("tag_market"));

        Assert.NotNull(bdsGroup);
        Assert.True(bdsGroup.HasTag("tag_bds"));
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
