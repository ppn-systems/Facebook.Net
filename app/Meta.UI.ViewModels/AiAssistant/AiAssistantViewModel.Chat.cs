// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Meta.UI.Core.AiAssistant;

namespace Meta.UI.ViewModels.AiAssistant;

public sealed partial class AiAssistantViewModel
{
    private async Task SendQuickPromptAsync(string? quickPrompt)
    {
        if (string.IsNullOrWhiteSpace(quickPrompt))
        {
            return;
        }

        this.InputPromptText = quickPrompt.Trim();
        await this.SendMessageAsync().ConfigureAwait(true);
    }

    private async Task SendMessageAsync()
    {
        string prompt = this.InputPromptText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || this.IsBusy)
        {
            return;
        }

        this.InputPromptText = string.Empty;
        this.IsBusy = true;
        this.StatusBannerText = _context.Localization["Ai.StatusThinking"];
        AiChatBubbleItem? streamingBubble = null;
        using CancellationTokenSource chatCts = new();
        _cancelActiveChat = () =>
        {
            try
            {
                chatCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed.
            }
        };

        try
        {
            if (!_isMemoryStoreInitialized)
            {
                await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
                _isMemoryStoreInitialized = true;
            }

            if (string.IsNullOrWhiteSpace(this.ActiveConversationId))
            {
                this.ActiveConversationId = CreateNewConversationId();
            }

            string conversationTitle = this.ResolveConversationTitle(prompt);

            AiChatMessageRecord userRec = await _context.AiMemoryStore
                .AppendChatMessageAsync(
                    "user",
                    prompt,
                    conversationId: this.ActiveConversationId)
                .ConfigureAwait(true);

            _ = await _context.AiMemoryStore
                .SaveConversationAsync(this.ActiveConversationId, conversationTitle, this.ActiveAgyConversationId)
                .ConfigureAwait(true);

            this.Messages.Add(new AiChatBubbleItem
            {
                Id = userRec.Id,
                Role = "user",
                Content = prompt,
                TimestampText = userRec.TimestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            });

            await this.TryExtractExplicitMemoryHintAsync(prompt).ConfigureAwait(true);

            (string? activeUid, string? activeName, int enabledTargetGroups, int minDelay, int maxDelay, string workspaceSnapshot) =
                await this.BuildRichWorkspaceSnapshotAsync().ConfigureAwait(true);

            string baseContext = await _context.AiMemoryStore
                .BuildSystemContextPromptAsync(this.SystemPromptText, activeUid, activeName)
                .ConfigureAwait(true);

            string systemContextPrompt = string.Create(
                CultureInfo.InvariantCulture,
                $"{baseContext}\n\n{workspaceSnapshot}");

            streamingBubble = new AiChatBubbleItem
            {
                Id = 0,
                Role = "assistant",
                Content = "...",
                IsLoading = true,
                TimestampText = DateTimeOffset.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
            };
            this.Messages.Add(streamingBubble);

            Progress<string> streamingProgress = new(partialText =>
            {
                if (!string.IsNullOrWhiteSpace(partialText))
                {
                    _context.Dispatcher.Post(() => streamingBubble.Content = partialText);
                }
            });

            AgyPromptResult result = await _context.AgyCli
                .SendPromptAsync(
                    prompt,
                    systemContextPrompt,
                    continueConversation: _hasActiveConversation,
                    streamingProgress: streamingProgress,
                    model: this.SelectedAiModel,
                    effort: this.SelectedAiEffort,
                    agyConversationId: this.ActiveAgyConversationId,
                    ct: chatCts.Token)
                .ConfigureAwait(true);

            string finalResponseText = result.IsSuccess && !string.IsNullOrWhiteSpace(result.ResponseText)
                ? result.ResponseText
                : !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage
                    : _context.Localization["Log.ConnectionInterrupted"];

            if (result.IsSuccess)
            {
                _hasActiveConversation = true;
            }

            if (!string.IsNullOrWhiteSpace(result.ConversationId))
            {
                this.ActiveAgyConversationId = result.ConversationId;
            }

            if (result.LearnedMemories is { Count: > 0 })
            {
                for (int i = 0; i < result.LearnedMemories.Count; i++)
                {
                    AgyLearnedMemoryItem learned = result.LearnedMemories[i];
                    if (!string.IsNullOrWhiteSpace(learned.Key) && !string.IsNullOrWhiteSpace(learned.Content))
                    {
                        _ = await _context.AiMemoryStore.SaveMemoryAsync(
                            learned.Category,
                            learned.Key.Trim(),
                            learned.Content.Trim(),
                            Math.Clamp(learned.Importance, 1, 5),
                            activeUid).ConfigureAwait(true);
                    }
                }
            }
            else
            {
                await this.TryExtractContactAndPricingHintAsync(prompt, activeUid).ConfigureAwait(true);
            }

            AgyProposedAction? fallbackAction = this.DetectFallbackProposedAction(
                prompt,
                finalResponseText,
                enabledTargetGroups,
                minDelay,
                maxDelay);

            AgyProposedAction? proposedAction = result.ProposedAction;
            if (proposedAction is null)
            {
                proposedAction = fallbackAction;
            }
            else if (fallbackAction is not null)
            {
                string? mergedContent = !string.IsNullOrWhiteSpace(TryExtractQuotedText(prompt))
                    ? fallbackAction.ContentText
                    : !string.IsNullOrWhiteSpace(proposedAction.ContentText)
                        ? proposedAction.ContentText
                        : fallbackAction.ContentText;

                int mergedGroupCount = TryExtractGroupCount(prompt, out int explicitGroupCount)
                    ? explicitGroupCount
                    : proposedAction.TargetGroupCount > 0
                        ? proposedAction.TargetGroupCount
                        : fallbackAction.TargetGroupCount;

                proposedAction = proposedAction with
                {
                    ContentText = mergedContent,
                    TargetGroupCount = mergedGroupCount
                };
            }

            if (proposedAction is not null &&
                string.Equals(proposedAction.ActionKind, "PUBLISH_GROUPS", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(proposedAction.ContentText) ||
                 (IsClarifyingQuestionResponse(finalResponseText) && string.IsNullOrWhiteSpace(TryExtractQuotedText(prompt)))))
            {
                proposedAction = null;
            }

            string? proposedPayloadJson = proposedAction is not null
                ? SerializeProposedAction(proposedAction)
                : null;

            AiChatMessageRecord assistantRec = await _context.AiMemoryStore
                .AppendChatMessageAsync(
                    "assistant",
                    finalResponseText,
                    proposedAction?.ActionKind,
                    proposedPayloadJson,
                    this.ActiveConversationId)
                .ConfigureAwait(true);

            _ = await _context.AiMemoryStore
                .SaveConversationAsync(this.ActiveConversationId, conversationTitle, this.ActiveAgyConversationId)
                .ConfigureAwait(true);

            streamingBubble.Id = assistantRec.Id;
            streamingBubble.TimestampText = assistantRec.TimestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            streamingBubble.ProposedAction = proposedAction;
            streamingBubble.IsActionExecuted = false;
            streamingBubble.IsLoading = false;
            streamingBubble.Content = finalResponseText;

            if (!this.Messages.Contains(streamingBubble))
            {
                this.Messages.Add(streamingBubble);
            }

            await this.RefreshConversationListAsync().ConfigureAwait(true);
            await this.ReloadMemoriesAsync().ConfigureAwait(true);

            if (_onMcpDatabaseUpdated is not null)
            {
                await _onMcpDatabaseUpdated().ConfigureAwait(true);
            }

            this.StatusBannerText = _context.Localization["Ai.StatusReady"];
        }
        catch (OperationCanceledException)
        {
            if (streamingBubble is not null)
            {
                streamingBubble.IsLoading = false;
                if (string.Equals(streamingBubble.Content, "...", StringComparison.Ordinal))
                {
                    _ = this.Messages.Remove(streamingBubble);
                }
            }

            this.StatusBannerText = _context.Localization["Log.TaskStoppedByUser"];
        }
        finally
        {
            _cancelActiveChat = null;
            this.IsBusy = false;
        }
    }

    private async Task ExecuteProposedActionAsync(AiChatBubbleItem? item)
    {
        if (item?.ProposedAction is null || item.IsActionExecuted)
        {
            return;
        }

        item.IsActionExecuted = true;
        if (item.Id > 0)
        {
            await _context.AiMemoryStore.MarkChatActionExecutedAsync(item.Id).ConfigureAwait(true);
        }

        _onExecuteProposedActionInShell?.Invoke(item.ProposedAction);
    }

    private void EditProposedAction(AiChatBubbleItem? item)
    {
        if (item?.ProposedAction is null)
        {
            return;
        }

        string draftText = !string.IsNullOrWhiteSpace(item.ProposedAction.ContentText)
            ? item.ProposedAction.ContentText
            : !string.IsNullOrWhiteSpace(item.ProposedAction.GroupKeyword)
                ? item.ProposedAction.GroupKeyword
                : item.Content;

        this.InputPromptText = draftText;

        if (!string.IsNullOrWhiteSpace(item.ProposedAction.ContentText))
        {
            _onExecuteProposedActionInShell?.Invoke(item.ProposedAction with { ActionKind = "EDIT_DRAFT" });
        }
    }

    private async Task StartNewConversationAsync()
    {
        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        _context.AgyCli.ResetWarmSession();
        _hasActiveConversation = false;
        this.ActiveAgyConversationId = null;
        this.ActiveConversationId = CreateNewConversationId();

        _suppressConversationSelectionChange = true;
        try
        {
            this.SelectedConversation = null;
        }
        finally
        {
            _suppressConversationSelectionChange = false;
        }

        this.Messages.Clear();

        if (this.SetupStage == AiAssistantSetupStage.Ready)
        {
            _ = _context.AgyCli.WarmUpSessionAsync(this.SelectedAiModel, this.SelectedAiEffort, null);
        }
    }

    private async Task SelectConversationAsync(AiConversationThread? thread)
    {
        if (thread is null || string.IsNullOrWhiteSpace(thread.Id))
        {
            return;
        }

        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        _suppressConversationSelectionChange = true;
        try
        {
            this.SelectedConversation = thread;
        }
        finally
        {
            _suppressConversationSelectionChange = false;
        }

        this.ActiveConversationId = thread.Id;
        this.ActiveAgyConversationId = thread.AgyConversationId;
        _hasActiveConversation = true;

        await this.LoadConversationMessagesAsync(thread.Id).ConfigureAwait(true);

        if (this.SetupStage == AiAssistantSetupStage.Ready)
        {
            _ = _context.AgyCli.WarmUpSessionAsync(
                this.SelectedAiModel,
                this.SelectedAiEffort,
                thread.AgyConversationId);
        }
    }

    private async Task DeleteConversationAsync(AiConversationThread? thread)
    {
        AiConversationThread? target = thread ?? this.SelectedConversation;
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
        {
            return;
        }

        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        string deletedId = target.Id;
        _ = await _context.AiMemoryStore.DeleteConversationAsync(deletedId).ConfigureAwait(true);

        IReadOnlyList<AiConversationThread> remaining = await _context.AiMemoryStore
            .GetConversationsAsync(50)
            .ConfigureAwait(true);

        _suppressConversationSelectionChange = true;
        try
        {
            this.Conversations.ReplaceAll(remaining);
        }
        finally
        {
            _suppressConversationSelectionChange = false;
        }

        if (string.Equals(deletedId, this.ActiveConversationId, StringComparison.Ordinal))
        {
            if (this.Conversations.Count > 0)
            {
                await this.SelectConversationAsync(this.Conversations[0]).ConfigureAwait(true);
            }
            else
            {
                await this.StartNewConversationAsync().ConfigureAwait(true);
            }
        }
        else
        {
            _suppressConversationSelectionChange = true;
            try
            {
                this.SelectedConversation = this.FindConversationById(this.ActiveConversationId);
            }
            finally
            {
                _suppressConversationSelectionChange = false;
            }
        }
    }

    private async Task LoadConversationMessagesAsync(string? conversationId, CancellationToken ct = default)
    {
        IReadOnlyList<AiChatMessageRecord> history = await _context.AiMemoryStore
            .GetRecentChatHistoryAsync(100, ct, conversationId)
            .ConfigureAwait(true);

        List<AiChatBubbleItem> loadedBubbles = new(history.Count);
        for (int i = 0; i < history.Count; i++)
        {
            AiChatMessageRecord rec = history[i];
            loadedBubbles.Add(new AiChatBubbleItem
            {
                Id = rec.Id,
                Role = rec.Role,
                Content = rec.Content,
                TimestampText = rec.TimestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                ProposedAction = DeserializeProposedAction(rec.ProposedActionKind, rec.ProposedActionPayloadJson),
                IsActionExecuted = rec.IsActionExecuted
            });
        }

        this.Messages.ReplaceAll(loadedBubbles);
        _hasActiveConversation = loadedBubbles.Count > 0;
    }

    private async Task RefreshConversationListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AiConversationThread> threads = await _context.AiMemoryStore
            .GetConversationsAsync(50, ct)
            .ConfigureAwait(true);

        _suppressConversationSelectionChange = true;
        try
        {
            this.Conversations.ReplaceAll(threads);
            this.SelectedConversation = this.FindConversationById(this.ActiveConversationId);
        }
        finally
        {
            _suppressConversationSelectionChange = false;
        }
    }

    private AiConversationThread? FindConversationById(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        for (int i = 0; i < this.Conversations.Count; i++)
        {
            if (string.Equals(this.Conversations[i].Id, conversationId, StringComparison.Ordinal))
            {
                return this.Conversations[i];
            }
        }

        return null;
    }

    private string ResolveConversationTitle(string currentPrompt)
    {
        AiConversationThread? existing = this.FindConversationById(this.ActiveConversationId);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Title))
        {
            return existing.Title;
        }

        for (int i = 0; i < this.Messages.Count; i++)
        {
            if (this.Messages[i].IsUser && !string.IsNullOrWhiteSpace(this.Messages[i].Content))
            {
                return this.FormatConversationTitle(this.Messages[i].Content);
            }
        }

        return this.FormatConversationTitle(currentPrompt);
    }

    private string FormatConversationTitle(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return _context.Localization["Ai.NewConversationDefaultTitle"];
        }

        string oneLine = rawText.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length > 42 ? string.Concat(oneLine[..42].TrimEnd(), "...") : oneLine;
    }

    private static string CreateNewConversationId() =>
        $"conv_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>
    /// Opens the AI Assistant panel, enables the <c>selected_lead</c> context pack, and sends a consultation request for <paramref name="post"/>.
    /// </summary>
    public async Task RequestLeadAnalysisAndConsultationAsync(PostSummary? post)
    {
        if (post is null)
        {
            return;
        }

        if (this.Shell is not null)
        {
            this.Shell.SelectedScrapedPost = post;
            this.Shell.IsAiAssistantOpen = true;
        }

        this.IsPanelOpen = true;
        this.ActiveSubTabIndex = 0;
        this.EnableContextPack("selected_lead");
        this.InputPromptText = _context.Localization["Ai.PromptAnalyzeSelectedLead"];

        if (!this.IsBusy)
        {
            await this.SendMessageAsync().ConfigureAwait(true);
        }
    }
}
