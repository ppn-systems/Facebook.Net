// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meta.UI.Core.AiAssistant;
using Meta.UI.Core.Collections;
using Meta.UI.Core.Configuration;
using Meta.UI.ViewModels.Services;

namespace Meta.UI.ViewModels.AiAssistant;

/// <summary>
/// Represents the current on-demand setup stage of the AI Assistant panel.
/// </summary>
public enum AiAssistantSetupStage
{
    /// <summary>Panel has not been opened yet; no CLI check has been performed.</summary>
    NotCheckedYet = 0,

    /// <summary>Checking whether Google Antigravity CLI (<c>agy</c>) is installed and authenticated.</summary>
    Checking = 1,

    /// <summary><c>agy</c> is not installed; prompts the user with the 1-click install card.</summary>
    NeedsInstall = 2,

    /// <summary>Downloading and installing <c>agy</c> and registering <c>Facebook.Net.Mcp</c>.</summary>
    Installing = 3,

    /// <summary><c>agy</c> is installed, but Google OAuth authentication is required.</summary>
    NeedsAuth = 4,

    /// <summary>Running the automated CDP browser OAuth login flow.</summary>
    Authenticating = 5,

    /// <summary><c>agy</c> is installed, authenticated, and connected to MCP &amp; AI Memory store.</summary>
    Ready = 6
}

/// <summary>
/// Represents a single chat message bubble in the AI Assistant conversation stream,
/// with optional interactive confirmation actions (<c>[✅ Đăng luôn]</c> / <c>[✏️ Sửa lại]</c>).
/// </summary>
public sealed partial class AiChatBubbleItem : ObservableObject
{
    /// <summary>Gets or sets the database row identifier in <c>ai_chat_messages</c>.</summary>
    public long Id { get; set; }

    /// <summary>Gets the sender role (<c>user</c> or <c>assistant</c>).</summary>
    public string Role { get; init; } = "assistant";

    /// <summary>Gets a value indicating whether this bubble was sent by the user.</summary>
    public bool IsUser => string.Equals(this.Role, "user", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a value indicating whether this bubble was sent by the AI Assistant.</summary>
    public bool IsAssistant => !this.IsUser;

    /// <summary>Gets or sets the formatted local timestamp (<c>HH:mm</c>).</summary>
    [ObservableProperty]
    public partial string TimestampText { get; set; } = string.Empty;

    /// <summary>Gets or sets the markdown/plain text content of the message.</summary>
    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the assistant is currently streaming or generating this message.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Gets or sets the optional proposed batch action attached to this assistant message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProposedAction))]
    [NotifyPropertyChangedFor(nameof(CanExecuteProposedAction))]
    public partial AgyProposedAction? ProposedAction { get; set; }

    /// <summary>Gets a value indicating whether this bubble has a proposed batch action requiring confirmation.</summary>
    public bool HasProposedAction => this.ProposedAction is not null;

    /// <summary>Gets or sets a value indicating whether the proposed action has already been executed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteProposedAction))]
    public partial bool IsActionExecuted { get; set; }

    /// <summary>Gets a value indicating whether the confirmation buttons can still be clicked.</summary>
    public bool CanExecuteProposedAction => this.HasProposedAction && !this.IsActionExecuted;
}

/// <summary>
/// ViewModel for the right-hand slide-over AI Assistant panel and AI Memory &amp; Context settings.
/// Performs all <c>agy</c> CLI detection, installation, MCP registration, and CDP OAuth strictly on-demand
/// when the user opens the panel.
/// </summary>
public sealed partial class AiAssistantViewModel : ObservableObject
{
    private readonly UiServiceContext _context;
    private readonly Func<Task>? _onMcpDatabaseUpdated;
    private readonly Action<AgyProposedAction>? _onExecuteProposedActionInShell;
    private Action? _cancelActiveChat;
    private bool _hasActiveConversation;
    private bool _isMemoryStoreInitialized;
    private bool _suppressConversationSelectionChange;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiAssistantViewModel"/> class.
    /// Does NOT invoke <c>CheckStatusAsync</c> during construction; checks run lazily when the panel is opened.
    /// </summary>
    /// <param name="context">Shared UI service context.</param>
    /// <param name="onMcpDatabaseUpdated">Optional callback invoked after an AI turn to refresh main Shell collections.</param>
    /// <param name="onExecuteProposedActionInShell">Optional callback invoked when the user confirms a proposed batch action.</param>
    public AiAssistantViewModel(
        UiServiceContext context,
        Func<Task>? onMcpDatabaseUpdated = null,
        Action<AgyProposedAction>? onExecuteProposedActionInShell = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _onMcpDatabaseUpdated = onMcpDatabaseUpdated;
        _onExecuteProposedActionInShell = onExecuteProposedActionInShell;

        this.Messages = new BulkObservableCollection<AiChatBubbleItem>();
        this.Conversations = new BulkObservableCollection<AiConversationThread>();
        this.Memories = new BulkObservableCollection<AiMemoryRecord>();
        this.ActiveContextPacks = new BulkObservableCollection<string>();
        this.InstalledAuthBrowsers = new BulkObservableCollection<PreferredBrowserKind>();
        this.AvailableAuthProfiles = new BulkObservableCollection<BrowserProfileOption>();
        this.MemoryCategories = Enum.GetValues<AiMemoryCategory>();
        this.AvailableModels = AgyCliService.DefaultModelOptions;
        this.AvailableEffortLevels = AgyCliService.SupportedEffortLevels;

        this.SelectedAuthBrowser = context.Settings.Current.PreferredBrowser;
        this.SelectedAiModel = string.IsNullOrWhiteSpace(context.Settings.Current.AiModel)
            ? AppConfig.DefaultAiModel
            : context.Settings.Current.AiModel;
        this.SelectedAiEffort = string.IsNullOrWhiteSpace(context.Settings.Current.AiReasoningEffort)
            ? AppConfig.DefaultAiReasoningEffort
            : context.Settings.Current.AiReasoningEffort;
        this.SystemPromptText = context.Settings.Current.AiSystemPrompt ?? string.Empty;

        this.TogglePanelCommand = new RelayCommand(() => this.IsPanelOpen = !this.IsPanelOpen);
        this.OpenPanelCommand = new RelayCommand(() => this.IsPanelOpen = true);
        this.ClosePanelCommand = new RelayCommand(() => this.IsPanelOpen = false);
        this.SwitchToChatTabCommand = new RelayCommand(() => this.ActiveSubTabIndex = 0);
        this.SwitchToBrainTabCommand = new RelayCommand(() => this.ActiveSubTabIndex = 1);

        this.InstallAgyCommand = new AsyncRelayCommand(this.InstallAgyAsync);
        this.SkipInstallCommand = new RelayCommand(this.SkipInstall);
        this.StartCdpOAuthLoginCommand = new AsyncRelayCommand(this.StartCdpOAuthLoginAsync);
        this.SubmitManualOAuthCodeCommand = new AsyncRelayCommand(this.SubmitManualOAuthCodeAsync);
        this.VerifyAuthStateCommand = new AsyncRelayCommand(this.VerifyAuthStateAsync);
        this.SendMessageCommand = new AsyncRelayCommand(this.SendMessageAsync);
        this.StopGenerationCommand = new RelayCommand(this.StopGeneration);
        this.SendQuickPromptCommand = new AsyncRelayCommand<string>(this.SendQuickPromptAsync);
        this.ExecuteProposedActionCommand = new AsyncRelayCommand<AiChatBubbleItem>(this.ExecuteProposedActionAsync);
        this.EditProposedActionCommand = new RelayCommand<AiChatBubbleItem>(this.EditProposedAction);
        this.StartNewConversationCommand = new AsyncRelayCommand(this.StartNewConversationAsync);
        this.SelectConversationCommand = new AsyncRelayCommand<AiConversationThread>(this.SelectConversationAsync);
        this.DeleteConversationCommand = new AsyncRelayCommand<AiConversationThread>(this.DeleteConversationAsync);
        this.SaveMemoryCommand = new AsyncRelayCommand(this.SaveMemoryAsync);
        this.DeleteMemoryCommand = new AsyncRelayCommand<AiMemoryRecord>(this.DeleteMemoryAsync);
        this.ClearAllMemoriesCommand = new AsyncRelayCommand(this.ClearAllMemoriesAsync);
        this.SaveSystemPromptCommand = new AsyncRelayCommand(this.SaveSystemPromptAsync);
        this.ResetSystemPromptCommand = new AsyncRelayCommand(this.ResetSystemPromptAsync);
        this.ApplyPromptPresetCommand = new RelayCommand<string>(this.ApplySystemPromptPreset);
        this.ToggleContextPackCommand = new RelayCommand<string>(this.ToggleContextPack);
    }

    /// <summary>Gets or sets the optional owning <see cref="Shell.ShellViewModel"/> reference for deep workspace integration.</summary>
    internal Shell.ShellViewModel? Shell { get; set; }

    /// <summary>Gets or sets a value indicating whether the slide-over AI Assistant panel is open.</summary>
    [ObservableProperty]
    public partial bool IsPanelOpen { get; set; }

    /// <summary>Gets or sets the active sub-tab index (<c>0</c> = Chat, <c>1</c> = Second Brain).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabActive))]
    [NotifyPropertyChangedFor(nameof(IsSecondBrainTabActive))]
    public partial int ActiveSubTabIndex { get; set; }

    /// <summary>Gets a value indicating whether the Chat sub-tab is active.</summary>
    public bool IsChatTabActive => this.ActiveSubTabIndex == 0;

    /// <summary>Gets a value indicating whether the Second Brain sub-tab is active.</summary>
    public bool IsSecondBrainTabActive => this.ActiveSubTabIndex == 1;

    /// <summary>Gets or sets the current setup/readiness stage of the AI Assistant.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckingStage))]
    [NotifyPropertyChangedFor(nameof(IsNeedsInstallStage))]
    [NotifyPropertyChangedFor(nameof(IsInstallingStage))]
    [NotifyPropertyChangedFor(nameof(IsNeedsAuthStage))]
    [NotifyPropertyChangedFor(nameof(IsAuthenticatingStage))]
    [NotifyPropertyChangedFor(nameof(IsReadyStage))]
    public partial AiAssistantSetupStage SetupStage { get; set; } = AiAssistantSetupStage.NotCheckedYet;

    /// <summary>Gets a value indicating whether the assistant is currently checking CLI status.</summary>
    public bool IsCheckingStage => this.SetupStage == AiAssistantSetupStage.Checking;

    /// <summary>Gets a value indicating whether the 1-click install prompt card should be displayed.</summary>
    public bool IsNeedsInstallStage => this.SetupStage == AiAssistantSetupStage.NeedsInstall;

    /// <summary>Gets a value indicating whether <c>agy</c> installation is in progress.</summary>
    public bool IsInstallingStage => this.SetupStage == AiAssistantSetupStage.Installing;

    /// <summary>Gets a value indicating whether the Google OAuth browser login card should be displayed.</summary>
    public bool IsNeedsAuthStage => this.SetupStage == AiAssistantSetupStage.NeedsAuth;

    /// <summary>Gets a value indicating whether CDP OAuth authentication is currently running.</summary>
    public bool IsAuthenticatingStage => this.SetupStage == AiAssistantSetupStage.Authenticating;

    /// <summary>Gets a value indicating whether the assistant is ready for chatting.</summary>
    public bool IsReadyStage => this.SetupStage == AiAssistantSetupStage.Ready;

    /// <summary>Gets or sets a value indicating whether an AI operation is currently running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets the installation progress percentage (<c>0..100</c>).</summary>
    [ObservableProperty]
    public partial int InstallProgressPercent { get; set; }

    /// <summary>Gets or sets the status banner text displayed in the AI Assistant header or setup card.</summary>
    [ObservableProperty]
    public partial string StatusBannerText { get; set; } = string.Empty;

    /// <summary>Gets or sets the current user prompt text in the chat composer.</summary>
    [ObservableProperty]
    public partial string InputPromptText { get; set; } = string.Empty;

    /// <summary>Gets or sets the browser selected for CDP Google OAuth login.</summary>
    [ObservableProperty]
    public partial PreferredBrowserKind SelectedAuthBrowser { get; set; } = PreferredBrowserKind.AutoDetect;

    /// <summary>Gets or sets the browser profile selected for CDP Google OAuth login.</summary>
    [ObservableProperty]
    public partial BrowserProfileOption? SelectedAuthProfile { get; set; }

    /// <summary>Gets or sets the manual Google OAuth authorization code fallback input.</summary>
    [ObservableProperty]
    public partial string ManualOAuthCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's free-form unlimited custom context injected alongside the internal English System Prompt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomAiSystemPrompt))]
    public partial string SystemPromptText { get; set; } = string.Empty;

    /// <summary>Gets or sets the user's custom system prompt alias.</summary>
    public string CustomAiSystemPrompt
    {
        get => this.SystemPromptText;
        set => this.SystemPromptText = value;
    }

    /// <summary>Gets or sets the selected AI model ID (<c>agy --model</c>), defaulting to the fastest lightweight model (<c>gemini-3.8-flash-low</c>).</summary>
    [ObservableProperty]
    public partial string SelectedAiModel { get; set; } = AppConfig.DefaultAiModel;

    /// <summary>Gets or sets the selected reasoning effort (<c>agy --effort</c>), defaulting to <c>low</c>.</summary>
    [ObservableProperty]
    public partial string SelectedAiEffort { get; set; } = AppConfig.DefaultAiReasoningEffort;

    /// <summary>Gets the available AI models for <c>agy --model</c>.</summary>
    public IReadOnlyList<AgyModelOption> AvailableModels { get; }

    /// <summary>Gets the supported reasoning effort levels for <c>agy --effort</c>.</summary>
    public IReadOnlyList<string> AvailableEffortLevels { get; }

    /// <summary>Gets or sets the topic/key when adding a new memory entry to the AI Memory store.</summary>
    [ObservableProperty]
    public partial string NewMemoryKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the content when adding a new memory entry to the AI Memory store.</summary>
    [ObservableProperty]
    public partial string NewMemoryContent { get; set; } = string.Empty;

    /// <summary>Gets or sets the category when adding a new memory entry to the AI Memory store.</summary>
    [ObservableProperty]
    public partial AiMemoryCategory NewMemoryCategory { get; set; } = AiMemoryCategory.ProductKnowledge;

    /// <summary>Gets the collection of chat message bubbles in the active conversation.</summary>
    public BulkObservableCollection<AiChatBubbleItem> Messages { get; }

    /// <summary>Gets the collection of stored conversation threads ordered by most recently updated.</summary>
    public BulkObservableCollection<AiConversationThread> Conversations { get; }

    /// <summary>Gets or sets the currently selected conversation thread in the history selector.</summary>
    [ObservableProperty]
    public partial AiConversationThread? SelectedConversation { get; set; }

    /// <summary>Gets or sets the active local SQLite conversation thread ID.</summary>
    [ObservableProperty]
    public partial string ActiveConversationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the active <c>agy</c> CLI conversation session ID for resuming the exact AI session.</summary>
    [ObservableProperty]
    public partial string? ActiveAgyConversationId { get; set; }

    /// <summary>Gets the collection of persistent AI Memory records.</summary>
    public BulkObservableCollection<AiMemoryRecord> Memories { get; }

    /// <summary>Gets the collection of active workspace context pack IDs attached to the chat composer (<c>selected_lead</c>, <c>top_hot_leads</c>, <c>deep_analytics</c>, <c>all_groups_tags</c>, <c>templates_posts</c>).</summary>
    public BulkObservableCollection<string> ActiveContextPacks { get; }

    /// <summary>Gets the collection of installed Chromium browsers available for OAuth login.</summary>
    public BulkObservableCollection<PreferredBrowserKind> InstalledAuthBrowsers { get; }

    /// <summary>Gets the collection of browser profiles available for OAuth login.</summary>
    public BulkObservableCollection<BrowserProfileOption> AvailableAuthProfiles { get; }

    /// <summary>Gets all available <see cref="AiMemoryCategory"/> values for the AI Memory editor.</summary>
    public IReadOnlyList<AiMemoryCategory> MemoryCategories { get; }

    /// <summary>Gets the command to toggle the AI Assistant panel open or closed.</summary>
    public IRelayCommand TogglePanelCommand { get; }

    /// <summary>Gets the command to open the AI Assistant panel.</summary>
    public IRelayCommand OpenPanelCommand { get; }

    /// <summary>Gets the command to close the AI Assistant panel.</summary>
    public IRelayCommand ClosePanelCommand { get; }

    /// <summary>Gets the command to switch to the Chat sub-tab.</summary>
    public IRelayCommand SwitchToChatTabCommand { get; }

    /// <summary>Gets the command to switch to the AI Memory sub-tab.</summary>
    public IRelayCommand SwitchToBrainTabCommand { get; }

    /// <summary>Gets the command to install Google Antigravity CLI (<c>agy</c>) and register the Facebook MCP server.</summary>
    public IAsyncRelayCommand InstallAgyCommand { get; }

    /// <summary>Gets the command to skip installation and close the panel.</summary>
    public IRelayCommand SkipInstallCommand { get; }

    /// <summary>Gets the command to start automated CDP browser Google OAuth login.</summary>
    public IAsyncRelayCommand StartCdpOAuthLoginCommand { get; }

    /// <summary>Gets the command to submit a manually pasted Google OAuth authorization code.</summary>
    public IAsyncRelayCommand SubmitManualOAuthCodeCommand { get; }

    /// <summary>Gets the command to re-verify authentication state and transition to Ready if authenticated.</summary>
    public IAsyncRelayCommand VerifyAuthStateCommand { get; }

    /// <summary>Gets the command to send the current <see cref="InputPromptText"/> to the AI Assistant.</summary>
    public IAsyncRelayCommand SendMessageCommand { get; }

    /// <summary>Gets the command to send a quick suggestion chip prompt to the AI Assistant.</summary>
    public IAsyncRelayCommand<string> SendQuickPromptCommand { get; }

    /// <summary>Gets the command to confirm and execute a proposed batch action (<c>[✅ Đăng luôn]</c>).</summary>
    public IAsyncRelayCommand<AiChatBubbleItem> ExecuteProposedActionCommand { get; }

    /// <summary>Gets the command to edit a proposed batch action before running (<c>[✏️ Sửa lại]</c>).</summary>
    public IRelayCommand<AiChatBubbleItem> EditProposedActionCommand { get; }

    /// <summary>Gets the command to start a fresh conversation thread without deleting past conversations.</summary>
    public IAsyncRelayCommand StartNewConversationCommand { get; }

    /// <summary>Gets the command to switch to and resume a previous conversation thread.</summary>
    public IAsyncRelayCommand<AiConversationThread> SelectConversationCommand { get; }

    /// <summary>Gets the command to delete a conversation thread from history.</summary>
    public IAsyncRelayCommand<AiConversationThread> DeleteConversationCommand { get; }

    /// <summary>Gets the command to save a new memory entry into the AI Memory database.</summary>
    public IAsyncRelayCommand SaveMemoryCommand { get; }

    /// <summary>Gets the command to delete a memory entry from the AI Memory database.</summary>
    public IAsyncRelayCommand<AiMemoryRecord> DeleteMemoryCommand { get; }

    /// <summary>Gets the command to delete all memory entries from the AI Memory database.</summary>
    public IAsyncRelayCommand ClearAllMemoriesCommand { get; }

    /// <summary>Gets the command to save the custom System Prompt into application settings.</summary>
    public IAsyncRelayCommand SaveSystemPromptCommand { get; }

    /// <summary>Gets the command to reset the System Prompt to the default instructions.</summary>
    public IAsyncRelayCommand ResetSystemPromptCommand { get; }

    /// <summary>Gets the command to apply a built-in System Prompt preset.</summary>
    public IRelayCommand<string> ApplyPromptPresetCommand { get; }

    /// <summary>Gets the command to toggle a smart workspace context pack.</summary>
    public IRelayCommand<string> ToggleContextPackCommand { get; }

    /// <summary>Gets the command to stop the currently running AI generation stream.</summary>
    public IRelayCommand StopGenerationCommand { get; }

    /// <summary>
    /// Cancels the active AI prompt generation if one is currently running.
    /// </summary>
    public void StopGeneration() => _cancelActiveChat?.Invoke();

    /// <summary>
    /// Sends the specified assistant reply text into the Publisher Spintax composer and switches to the Publisher page.
    /// </summary>
    /// <param name="text">Text content to load into the Publisher composer.</param>
    public void SendTextToPublisherComposer(string? text)
    {
        if (this.Shell is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string? extracted = TryExtractQuotedText(text);
        this.Shell.PostSpintaxText = !string.IsNullOrWhiteSpace(extracted) ? extracted : text.Trim();
        this.Shell.GenerateSpintaxPreview();
        this.Shell.CurrentSection = Meta.UI.ViewModels.Shell.NavigationSection.Publisher;
    }

    /// <summary>
    /// Sends the specified assistant reply text into the Sales &amp; Leads Comment composer and switches to the Sales &amp; Leads page.
    /// </summary>
    /// <param name="text">Text content to load into the Sales Comment composer.</param>
    public void SendTextToSalesCommentComposer(string? text)
    {
        if (this.Shell is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string? extracted = TryExtractQuotedText(text);
        this.Shell.SalesCommentSpintaxText = !string.IsNullOrWhiteSpace(extracted) ? extracted : text.Trim();
        this.Shell.CurrentSection = Meta.UI.ViewModels.Shell.NavigationSection.SalesLeads;
    }

    /// <summary>
    /// Checks whether the specified context pack is currently enabled.
    /// </summary>
    public bool IsContextPackEnabled(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            return false;
        }

        for (int i = 0; i < this.ActiveContextPacks.Count; i++)
        {
            if (string.Equals(this.ActiveContextPacks[i], packId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Toggles the specified workspace context pack on or off.
    /// </summary>
    public void ToggleContextPack(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            return;
        }

        string trimmed = packId.Trim();
        for (int i = 0; i < this.ActiveContextPacks.Count; i++)
        {
            if (string.Equals(this.ActiveContextPacks[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                this.ActiveContextPacks.RemoveAt(i);
                return;
            }
        }

        this.ActiveContextPacks.Add(trimmed);
    }

    /// <summary>
    /// Ensures the specified workspace context pack is enabled.
    /// </summary>
    public void EnableContextPack(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId) || this.IsContextPackEnabled(packId))
        {
            return;
        }

        this.ActiveContextPacks.Add(packId.Trim());
    }

    partial void OnIsPanelOpenChanged(bool value)
    {
        if (value)
        {
            _ = this.EnsureInitializedWhenOpenedAsync();
        }
    }

    partial void OnSelectedAuthBrowserChanged(PreferredBrowserKind value) =>
        this.RefreshAuthBrowserProfiles(value);

    /// <summary>
    /// Initializes the SQLite AI Memory store and loads saved memories and the System Prompt
    /// without invoking <c>CheckStatusAsync</c> on <see cref="IAgyCliService"/>.
    /// </summary>
    public async Task EnsureMemoryLoadedAsync(CancellationToken ct = default)
    {
        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync(ct).ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        this.SelectedAiModel = string.IsNullOrWhiteSpace(_context.Settings.Current.AiModel)
            ? AppConfig.DefaultAiModel
            : _context.Settings.Current.AiModel;
        this.SelectedAiEffort = string.IsNullOrWhiteSpace(_context.Settings.Current.AiReasoningEffort)
            ? AppConfig.DefaultAiReasoningEffort
            : _context.Settings.Current.AiReasoningEffort;
        this.SystemPromptText = _context.Settings.Current.AiSystemPrompt ?? string.Empty;

        await this.ReloadMemoriesAsync(ct).ConfigureAwait(true);
    }

    partial void OnSelectedAiModelChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(_context.Settings.Current.AiModel, value, StringComparison.Ordinal))
        {
            _ = _context.Settings.SaveAsync(_context.Settings.Current with { AiModel = value.Trim() });
        }
    }

    partial void OnSelectedAiEffortChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(_context.Settings.Current.AiReasoningEffort, value, StringComparison.OrdinalIgnoreCase))
        {
            _ = _context.Settings.SaveAsync(_context.Settings.Current with { AiReasoningEffort = value.Trim().ToLowerInvariant() });
        }
    }

    partial void OnSelectedConversationChanged(AiConversationThread? value)
    {
        if (!_suppressConversationSelectionChange &&
            value is not null &&
            !string.Equals(this.ActiveConversationId, value.Id, StringComparison.Ordinal))
        {
            _ = this.SelectConversationAsync(value);
        }
    }

    /// <summary>
    /// Lazily initializes the AI Memory store, loads stored memories, conversation threads, and chat messages,
    /// refreshes installed browsers/profiles, checks <c>agy</c> installation and authentication state when the panel is opened,
    /// and pre-warms the <c>agy</c> daemon session.
    /// </summary>
    public async Task EnsureInitializedWhenOpenedAsync(CancellationToken ct = default)
    {
        await this.EnsureMemoryLoadedAsync(ct).ConfigureAwait(true);

        IReadOnlyList<AiConversationThread> threads = await _context.AiMemoryStore
            .GetConversationsAsync(50, ct)
            .ConfigureAwait(true);

        _suppressConversationSelectionChange = true;
        try
        {
            this.Conversations.ReplaceAll(threads);
            if (this.Conversations.Count > 0 && string.IsNullOrWhiteSpace(this.ActiveConversationId))
            {
                AiConversationThread recent = this.Conversations[0];
                this.SelectedConversation = recent;
                this.ActiveConversationId = recent.Id;
                this.ActiveAgyConversationId = recent.AgyConversationId;
            }
            else if (string.IsNullOrWhiteSpace(this.ActiveConversationId))
            {
                this.ActiveConversationId = CreateNewConversationId();
                this.SelectedConversation = null;
            }
            else
            {
                this.SelectedConversation = this.FindConversationById(this.ActiveConversationId);
            }
        }
        finally
        {
            _suppressConversationSelectionChange = false;
        }

        if (this.Messages.Count == 0)
        {
            string? filterConversationId = this.Conversations.Count > 0 ? this.ActiveConversationId : null;
            await this.LoadConversationMessagesAsync(filterConversationId, ct).ConfigureAwait(true);
        }

        this.RefreshInstalledAuthBrowsers();

        if (this.SetupStage != AiAssistantSetupStage.Ready)
        {
            await this.CheckAndTransitionSetupStageAsync(ct).ConfigureAwait(true);
        }

        if (this.SetupStage == AiAssistantSetupStage.Ready)
        {
            _ = _context.AgyCli.WarmUpSessionAsync(
                this.SelectedAiModel,
                this.SelectedAiEffort,
                this.ActiveAgyConversationId,
                ct);
        }
    }

    private void RefreshInstalledAuthBrowsers()
    {
        List<PreferredBrowserKind> browsers = _context.Settings.GetInstalledBrowsers();
        this.InstalledAuthBrowsers.ReplaceAll(browsers);
        if (!browsers.Contains(this.SelectedAuthBrowser) && browsers.Count > 0)
        {
            this.SelectedAuthBrowser = browsers[0];
        }

        this.RefreshAuthBrowserProfiles(this.SelectedAuthBrowser);
    }

    private void RefreshAuthBrowserProfiles(PreferredBrowserKind browserKind)
    {
        PreferredBrowserKind effectiveKind = browserKind == PreferredBrowserKind.AutoDetect
            ? _context.Settings.ResolveEffectiveBrowserKind()
            : browserKind;

        string isolatedOAuthDir = Path.Combine(_context.ProfilesRootDirectory, "agy_oauth_profile");
        BrowserProfileOption isolatedOption = new(
            _context.Localization["Ai.AuthProfileIsolated"],
            isolatedOAuthDir,
            IsManagedByApp: true);

        List<BrowserProfileOption> discovered = AppSettingsService.DiscoverBrowserProfiles(
            _context.ProfilesRootDirectory,
            effectiveKind);

        List<BrowserProfileOption> options = new(discovered.Count + 1) { isolatedOption };
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { isolatedOAuthDir };

        for (int i = 0; i < discovered.Count; i++)
        {
            if (seen.Add(discovered[i].DirectoryPath))
            {
                options.Add(discovered[i]);
            }
        }

        this.AvailableAuthProfiles.ReplaceAll(options);
        if (this.SelectedAuthProfile is null || !seen.Contains(this.SelectedAuthProfile.DirectoryPath))
        {
            this.SelectedAuthProfile = options[0];
        }
    }

    private async Task CheckAndTransitionSetupStageAsync(CancellationToken ct = default)
    {
        this.SetupStage = AiAssistantSetupStage.Checking;
        this.StatusBannerText = _context.Localization["Ai.StatusThinking"];

        AgyStatusSnapshot status = await _context.AgyCli.CheckStatusAsync(ct).ConfigureAwait(true);
        if (!status.IsInstalled)
        {
            this.SetupStage = AiAssistantSetupStage.NeedsInstall;
            this.StatusBannerText = _context.Localization["Ai.InstallTitle"];
            return;
        }

        _ = await _context.AgyCli.EnsureFacebookMcpRegisteredAsync(ct: ct).ConfigureAwait(true);

        if (!status.IsAuthenticated)
        {
            this.SetupStage = AiAssistantSetupStage.NeedsAuth;
            this.StatusBannerText = _context.Localization["Ai.AuthTitle"];
            return;
        }

        this.SetupStage = AiAssistantSetupStage.Ready;
        this.StatusBannerText = _context.Localization["Ai.StatusReady"];
    }

    private async Task InstallAgyAsync()
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        this.SetupStage = AiAssistantSetupStage.Installing;
        this.InstallProgressPercent = 5;
        this.StatusBannerText = _context.Localization["Ai.InstallingStatus"];

        try
        {
            Progress<(int Percent, string StatusText)> progress = new(report =>
            {
                _context.Dispatcher.Post(() =>
                {
                    this.InstallProgressPercent = Math.Clamp(report.Percent, 0, 100);
                    if (!string.IsNullOrWhiteSpace(report.StatusText))
                    {
                        this.StatusBannerText = report.StatusText;
                    }
                });
            });

            bool installed = await _context.AgyCli.InstallAgyCliAsync(progress).ConfigureAwait(true);
            AgyStatusSnapshot status = await _context.AgyCli.CheckStatusAsync().ConfigureAwait(true);

            if (!installed && !status.IsInstalled)
            {
                this.SetupStage = AiAssistantSetupStage.NeedsInstall;
                return;
            }

            _ = await _context.AgyCli.EnsureFacebookMcpRegisteredAsync().ConfigureAwait(true);

            if (!status.IsAuthenticated)
            {
                this.SetupStage = AiAssistantSetupStage.NeedsAuth;
                this.StatusBannerText = _context.Localization["Ai.AuthTitle"];
            }
            else
            {
                this.SetupStage = AiAssistantSetupStage.Ready;
                this.StatusBannerText = _context.Localization["Ai.StatusReady"];
            }
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private void SkipInstall()
    {
        this.IsPanelOpen = false;
        this.SetupStage = AiAssistantSetupStage.NotCheckedYet;
    }

    private async Task StartCdpOAuthLoginAsync()
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        this.SetupStage = AiAssistantSetupStage.Authenticating;
        this.StatusBannerText = _context.Localization["Ai.AuthTitle"];

        try
        {
            Progress<string> progress = new(msg =>
            {
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    _context.Dispatcher.Post(() => this.StatusBannerText = msg);
                }
            });

            (bool success, string message, _) = await _context.AgyCli
                .RunCdpBrowserOAuthLoginAsync(
                    _context.SessionManager,
                    this.SelectedAuthBrowser,
                    this.SelectedAuthProfile?.DirectoryPath,
                    progress)
                .ConfigureAwait(true);

            if (success)
            {
                this.SetupStage = AiAssistantSetupStage.Ready;
                this.StatusBannerText = _context.Localization["Ai.StatusReady"];
                return;
            }

            AgyStatusSnapshot status = await _context.AgyCli.CheckStatusAsync().ConfigureAwait(true);
            if (status.IsAuthenticated)
            {
                this.SetupStage = AiAssistantSetupStage.Ready;
                this.StatusBannerText = _context.Localization["Ai.StatusReady"];
            }
            else
            {
                this.SetupStage = AiAssistantSetupStage.NeedsAuth;
                this.StatusBannerText = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : _context.Localization["Ai.AuthTitle"];
            }
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task SubmitManualOAuthCodeAsync()
    {
        string code = this.ManualOAuthCode.Trim();
        if (string.IsNullOrWhiteSpace(code) || this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            bool submitted = await _context.AgyCli.SubmitManualOAuthCodeAsync(code).ConfigureAwait(true);
            this.ManualOAuthCode = string.Empty;

            AgyStatusSnapshot status = await _context.AgyCli.CheckStatusAsync().ConfigureAwait(true);
            if (submitted || status.IsAuthenticated)
            {
                this.SetupStage = AiAssistantSetupStage.Ready;
                this.StatusBannerText = _context.Localization["Ai.StatusReady"];
            }
            else
            {
                this.SetupStage = AiAssistantSetupStage.NeedsAuth;
            }
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task VerifyAuthStateAsync()
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            AgyStatusSnapshot status = await _context.AgyCli.CheckStatusAsync().ConfigureAwait(true);
            if (status.IsAuthenticated)
            {
                this.SetupStage = AiAssistantSetupStage.Ready;
                this.StatusBannerText = _context.Localization["Ai.StatusReady"];
            }
            else
            {
                this.SetupStage = AiAssistantSetupStage.NeedsAuth;
                this.StatusBannerText = _context.Localization["Ai.AuthTitle"];
            }
        }
        finally
        {
            this.IsBusy = false;
        }
    }
}
