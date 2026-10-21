// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.ViewModels.Services;

namespace Facebook.Net.UI.ViewModels.AiAssistant;

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
        this.SendQuickPromptCommand = new AsyncRelayCommand<string>(this.SendQuickPromptAsync);
        this.ExecuteProposedActionCommand = new AsyncRelayCommand<AiChatBubbleItem>(this.ExecuteProposedActionAsync);
        this.EditProposedActionCommand = new RelayCommand<AiChatBubbleItem>(this.EditProposedAction);
        this.StartNewConversationCommand = new AsyncRelayCommand(this.StartNewConversationAsync);
        this.SelectConversationCommand = new AsyncRelayCommand<AiConversationThread>(this.SelectConversationAsync);
        this.DeleteConversationCommand = new AsyncRelayCommand<AiConversationThread>(this.DeleteConversationAsync);
        this.SaveMemoryCommand = new AsyncRelayCommand(this.SaveMemoryAsync);
        this.DeleteMemoryCommand = new AsyncRelayCommand<AiMemoryRecord>(this.DeleteMemoryAsync);
        this.SaveSystemPromptCommand = new AsyncRelayCommand(this.SaveSystemPromptAsync);
        this.ResetSystemPromptCommand = new AsyncRelayCommand(this.ResetSystemPromptAsync);
    }

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
    public partial string SystemPromptText { get; set; } = string.Empty;

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

    /// <summary>Gets the command to save the custom System Prompt into application settings.</summary>
    public IAsyncRelayCommand SaveSystemPromptCommand { get; }

    /// <summary>Gets the command to reset the System Prompt to the default instructions.</summary>
    public IAsyncRelayCommand ResetSystemPromptCommand { get; }

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

            AiChatBubbleItem streamingBubble = new()
            {
                Id = 0,
                Role = "assistant",
                Content = "...",
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
                    agyConversationId: this.ActiveAgyConversationId)
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

            AgyProposedAction? fallbackAction = DetectFallbackProposedAction(
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
        finally
        {
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
                return FormatConversationTitle(this.Messages[i].Content);
            }
        }

        return FormatConversationTitle(currentPrompt);
    }

    private static string FormatConversationTitle(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "Hội thoại mới";
        }

        string oneLine = rawText.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length > 42 ? string.Concat(oneLine[..42].TrimEnd(), "...") : oneLine;
    }

    private static string CreateNewConversationId() =>
        $"conv_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";

    private async Task SaveSystemPromptAsync()
    {
        string normalized = this.SystemPromptText?.Trim() ?? string.Empty;
        this.SystemPromptText = normalized;
        await _context.Settings.SaveAsync(_context.Settings.Current with
        {
            AiSystemPrompt = normalized,
            AiModel = this.SelectedAiModel,
            AiReasoningEffort = this.SelectedAiEffort
        }).ConfigureAwait(true);
        this.StatusBannerText = _context.Localization["Ai.SystemPromptSavedStatus"];
    }

    private async Task ResetSystemPromptAsync()
    {
        this.SystemPromptText = string.Empty;
        await _context.Settings.SaveAsync(_context.Settings.Current with { AiSystemPrompt = string.Empty }).ConfigureAwait(true);
        this.StatusBannerText = _context.Localization["Ai.SystemPromptSavedStatus"];
    }

    private async Task SaveMemoryAsync()
    {
        string key = this.NewMemoryKey.Trim();
        string content = this.NewMemoryContent.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        string? activeUid = _context.Settings.Current.LastSelectedAccountUid;
        _ = await _context.AiMemoryStore
            .SaveMemoryAsync(this.NewMemoryCategory, key, content, importance: 4, accountUid: activeUid)
            .ConfigureAwait(true);

        this.NewMemoryKey = string.Empty;
        this.NewMemoryContent = string.Empty;
        await this.ReloadMemoriesAsync().ConfigureAwait(true);
    }

    private async Task DeleteMemoryAsync(AiMemoryRecord? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        _ = await _context.AiMemoryStore.DeleteMemoryAsync(entry.Id).ConfigureAwait(true);
        await this.ReloadMemoriesAsync().ConfigureAwait(true);
    }

    private async Task ReloadMemoriesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AiMemoryRecord> list = await _context.AiMemoryStore
            .QueryMemoriesAsync(limit: int.MaxValue, ct: ct)
            .ConfigureAwait(true);
        this.Memories.ReplaceAll(list);
    }

    private async Task TryExtractExplicitMemoryHintAsync(string prompt)
    {
        string? activeUid = _context.Settings.Current.LastSelectedAccountUid;

        string[] prefixes = ["hãy nhớ:", "ghi nhớ:", "nhớ rằng:", "remember:", "lưu vào bộ nhớ:"];
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (prompt.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                string body = prompt[prefixes[i].Length..].Trim();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    string key = body.Length > 36 ? body[..36].Trim() : body;
                    _ = await _context.AiMemoryStore.SaveMemoryAsync(
                        AiMemoryCategory.UserPreference,
                        key,
                        body,
                        importance: 4,
                        accountUid: activeUid).ConfigureAwait(true);
                }

                break;
            }
        }

        if (ContainsSafeDelayRule(prompt))
        {
            _ = await _context.AiMemoryStore.SaveMemoryAsync(
                AiMemoryCategory.CampaignRule,
                "Khoảng nghỉ an toàn",
                prompt,
                importance: 4,
                accountUid: activeUid).ConfigureAwait(true);
        }

        if (IsPublishPromptIntent(prompt))
        {
            string? quotedPost = TryExtractQuotedText(prompt);
            if (!string.IsNullOrWhiteSpace(quotedPost))
            {
                _ = await _context.AiMemoryStore.SaveMemoryAsync(
                    AiMemoryCategory.ProductKnowledge,
                    "Chiến dịch gần nhất",
                    quotedPost,
                    importance: 4,
                    accountUid: activeUid).ConfigureAwait(true);
            }
        }
    }

    private async Task<(string? ActiveUid, string? ActiveName, int EnabledTargetGroups, int MinDelay, int MaxDelay, string WorkspaceSnapshot)> BuildRichWorkspaceSnapshotAsync()
    {
        AppConfig cfg = _context.Settings.Current;
        string? activeUid = cfg.LastSelectedAccountUid;
        string? activeName = null;
        string activeStatus = "None";

        IReadOnlyList<AccountProfile> accounts = await _context.AccountStore.GetAllAccountsAsync().ConfigureAwait(true);
        for (int i = 0; i < accounts.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(activeUid) &&
                string.Equals(accounts[i].Uid, activeUid, StringComparison.Ordinal))
            {
                activeName = accounts[i].DisplayName;
                activeStatus = accounts[i].Status.ToString();
                break;
            }
        }

        if (activeName is null && accounts.Count > 0)
        {
            activeUid = accounts[0].Uid;
            activeName = accounts[0].DisplayName;
            activeStatus = accounts[0].Status.ToString();
        }

        IReadOnlyList<SavedTargetGroup> targets = await _context.AccountStore.GetAllTargetGroupsAsync().ConfigureAwait(true);
        int enabledCount = 0;
        List<string> sampleGroupNames = new(8);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].IsEnabled)
            {
                enabledCount++;
                if (sampleGroupNames.Count < 8 && !string.IsNullOrWhiteSpace(targets[i].GroupName))
                {
                    sampleGroupNames.Add(targets[i].GroupName);
                }
            }
        }

        IReadOnlyList<GroupTag> tags = await _context.AccountStore.GetAllGroupTagsAsync().ConfigureAwait(true);
        List<string> tagNames = new(tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(tags[i].Name))
            {
                tagNames.Add(tags[i].Name);
            }
        }

        IReadOnlyList<CampaignTemplate> templates = await _context.AccountStore.GetAllCampaignTemplatesAsync().ConfigureAwait(true);

        StringBuilder sb = new();
        _ = sb.AppendLine("[CURRENT WORKSPACE STATE]");
        _ = sb.Append(CultureInfo.InvariantCulture, $"- Active Facebook account: {activeName ?? "None"} (Status: {activeStatus})").AppendLine();
        _ = sb.Append(CultureInfo.InvariantCulture, $"- Enabled target groups count: {enabledCount}").AppendLine();
        if (sampleGroupNames.Count > 0)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"- Sample enabled target groups: {string.Join(", ", sampleGroupNames)}").AppendLine();
        }

        if (tagNames.Count > 0)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"- Group color tags: {string.Join(", ", tagNames)}").AppendLine();
        }

        _ = sb.Append(CultureInfo.InvariantCulture, $"- Safe anti-checkpoint delay: {cfg.MinDelaySeconds}s - {cfg.MaxDelaySeconds}s");

        if (templates.Count > 0)
        {
            _ = sb.AppendLine().AppendLine().AppendLine("[SAVED TEMPLATES]");
            int maxTemplates = Math.Min(6, templates.Count);
            for (int i = 0; i < maxTemplates; i++)
            {
                CampaignTemplate tpl = templates[i];
                string snippet = (tpl.SpintaxContent ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (snippet.Length > 140)
                {
                    snippet = string.Concat(snippet[..140], "...");
                }

                _ = sb.Append(CultureInfo.InvariantCulture, $"- {tpl.Title}: {snippet}");
                if (i + 1 < maxTemplates)
                {
                    _ = sb.AppendLine();
                }
            }
        }

        return (activeUid, activeName, enabledCount, cfg.MinDelaySeconds, cfg.MaxDelaySeconds, sb.ToString());
    }

    private static AgyProposedAction? DetectFallbackProposedAction(
        string userPrompt,
        string assistantResponse,
        int enabledTargetGroups,
        int minDelay,
        int maxDelay)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || string.IsNullOrWhiteSpace(assistantResponse))
        {
            return null;
        }

        if (IsPublishPromptIntent(userPrompt))
        {
            string? quotedContent = TryExtractQuotedText(userPrompt);
            bool hasConcreteContent = !string.IsNullOrWhiteSpace(quotedContent) ||
                                      (userPrompt.Trim().Length > 35 && !IsClarifyingQuestionResponse(assistantResponse));

            if (!hasConcreteContent)
            {
                return null;
            }

            int targetCount = TryExtractGroupCount(userPrompt, out int promptGroupCount)
                ? promptGroupCount
                : TryExtractGroupCount(assistantResponse, out int responseGroupCount)
                    ? responseGroupCount
                    : Math.Max(1, enabledTargetGroups);

            string contentText = !string.IsNullOrWhiteSpace(quotedContent)
                ? quotedContent
                : assistantResponse.Trim();

            return new AgyProposedAction(
                ActionKind: "PUBLISH_GROUPS",
                SummaryText: $"Đăng bài vào {targetCount} nhóm (delay {minDelay}s - {maxDelay}s)",
                ContentText: contentText,
                TargetGroupCount: targetCount,
                MinDelaySeconds: minDelay,
                MaxDelaySeconds: maxDelay,
                GroupKeyword: null);
        }

        return null;
    }

    private static bool IsClarifyingQuestionResponse(string assistantResponse) =>
        !string.IsNullOrWhiteSpace(assistantResponse) &&
        assistantResponse.TrimEnd().EndsWith('?');

    private static bool IsPublishPromptIntent(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("đăng bài", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("đăng vào", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("đăng lên", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("publish", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("post to group", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsSafeDelayRule(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool mentionsDelayKeyword =
            text.Contains("delay", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("nghỉ", StringComparison.OrdinalIgnoreCase);

        if (!mentionsDelayKeyword)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExtractQuotedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int firstStraight = text.IndexOf('"', StringComparison.Ordinal);
        if (firstStraight >= 0 && firstStraight + 1 < text.Length)
        {
            int secondStraight = text.IndexOf('"', firstStraight + 1);
            if (secondStraight > firstStraight + 1)
            {
                string extracted = text[(firstStraight + 1)..secondStraight].Trim();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
        }

        int firstCurly = text.IndexOf('\u201C', StringComparison.Ordinal);
        if (firstCurly >= 0 && firstCurly + 1 < text.Length)
        {
            int secondCurly = text.IndexOf('\u201D', firstCurly + 1);
            if (secondCurly > firstCurly + 1)
            {
                string extracted = text[(firstCurly + 1)..secondCurly].Trim();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
        }

        return null;
    }

    private static bool TryExtractGroupCount(string text, out int groupCount)
    {
        groupCount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsDigit(text[i]))
            {
                i++;
                continue;
            }

            int numStart = i;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            ReadOnlySpan<char> numberSpan = text.AsSpan(numStart, i - numStart);
            int afterNumber = i;
            while (afterNumber < text.Length && char.IsWhiteSpace(text[afterNumber]))
            {
                afterNumber++;
            }

            if (afterNumber > i && afterNumber < text.Length)
            {
                ReadOnlySpan<char> tail = text.AsSpan(afterNumber);
                if (tail.StartsWith("group", StringComparison.OrdinalIgnoreCase) ||
                    tail.StartsWith("nhóm", StringComparison.OrdinalIgnoreCase) ||
                    tail.StartsWith("hội nhóm", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(numberSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                        parsed > 0)
                    {
                        groupCount = parsed;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string SerializeProposedAction(AgyProposedAction action)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("actionKind", action.ActionKind);
            writer.WriteString("summaryText", action.SummaryText);
            if (action.ContentText is not null)
            {
                writer.WriteString("contentText", action.ContentText);
            }

            writer.WriteNumber("targetGroupCount", action.TargetGroupCount);
            writer.WriteNumber("minDelaySeconds", action.MinDelaySeconds);
            writer.WriteNumber("maxDelaySeconds", action.MaxDelaySeconds);
            if (action.GroupKeyword is not null)
            {
                writer.WriteString("groupKeyword", action.GroupKeyword);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static AgyProposedAction? DeserializeProposedAction(string? actionKind, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(actionKind) && string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new AgyProposedAction(actionKind!, actionKind!);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AgyProposedAction(actionKind ?? "PUBLISH_GROUPS", payloadJson);
            }

            string kind = root.TryGetProperty("actionKind", out JsonElement k) && k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? actionKind ?? "PUBLISH_GROUPS"
                : actionKind ?? "PUBLISH_GROUPS";
            string summary = root.TryGetProperty("summaryText", out JsonElement s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? kind
                : kind;
            string? content = root.TryGetProperty("contentText", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            int groupCount = root.TryGetProperty("targetGroupCount", out JsonElement gc) && gc.TryGetInt32(out int parsedCount)
                ? parsedCount
                : 0;
            int minDelay = root.TryGetProperty("minDelaySeconds", out JsonElement minD) && minD.TryGetInt32(out int parsedMin)
                ? parsedMin
                : 30;
            int maxDelay = root.TryGetProperty("maxDelaySeconds", out JsonElement maxD) && maxD.TryGetInt32(out int parsedMax)
                ? parsedMax
                : 60;
            string? keyword = root.TryGetProperty("groupKeyword", out JsonElement kw) && kw.ValueKind == JsonValueKind.String
                ? kw.GetString()
                : null;

            return new AgyProposedAction(kind, summary, content, groupCount, minDelay, maxDelay, keyword);
        }
        catch (JsonException)
        {
            return new AgyProposedAction(actionKind ?? "PUBLISH_GROUPS", payloadJson);
        }
    }
}
