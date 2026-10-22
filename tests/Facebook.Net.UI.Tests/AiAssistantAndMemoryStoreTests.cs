// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.ViewModels.AiAssistant;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests for <see cref="SqliteAiMemoryStore"/>, <see cref="AgyCliService"/> stream/action parsing,
/// and <see cref="AiAssistantViewModel"/>.
/// </summary>
public sealed class AiAssistantAndMemoryStoreTests : IDisposable
{
    private readonly string _tempDir;

    public AiAssistantAndMemoryStoreTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_ai_assistant_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    [Fact]
    public async Task AiMemoryStore_SaveQueryDeleteAndSystemContext_PersistsUpsertsAndPreloadsSystemPrompt()
    {
        string dbPath = Path.Combine(this._tempDir, "facebook_ai_memory.db");
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

        // Verify accent-insensitive search
        IReadOnlyList<AiMemoryRecord> accentFreeHits = await store.QueryMemoriesAsync(keyword: "ao khoac kaki");
        _ = Assert.Single(accentFreeHits);

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
        string workspaceDir = Path.Combine(this._tempDir, "system_prompt_test");
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
        string workspaceDir = Path.Combine(this._tempDir, "lazy_check_test");
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
        string workspaceDir = Path.Combine(this._tempDir, "already_ready_test");
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
        string workspaceDir = Path.Combine(this._tempDir, "cdp_oauth_test");
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
        string workspaceDir = Path.Combine(this._tempDir, "send_message_action_test");
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
    public async Task AiMemoryStore_And_ViewModel_SupportMultiConversationHistoryAndResume()
    {
        string workspaceDir = Path.Combine(this._tempDir, "multi_conversation_resume_test");
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
    public void AgyCliService_ParseResponseActionAndMemories_StripsLearnTagsAndParsesAllNewActions()
    {
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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._tempDir))
            {
                Directory.Delete(this._tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore locked temp files
        }
    }
}
