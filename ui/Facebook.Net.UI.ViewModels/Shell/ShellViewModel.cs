// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.ViewModels.AiAssistant;
using Facebook.Net.UI.ViewModels.Audit;
using Facebook.Net.UI.ViewModels.Navigation;
using Facebook.Net.UI.ViewModels.Services;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Navigation pages in the collapsible left sidebar.
/// </summary>
public enum NavigationSection
{
    /// <summary>Zero-UID Account Vault &amp; 1-Click Session/Browser Login.</summary>
    Accounts = 0,

    /// <summary>Visual Joined Groups Catalog &amp; Group Discovery.</summary>
    Groups = 1,

    /// <summary>Automated Group/Timeline Publisher with Spintax &amp; Media.</summary>
    Publisher = 2,

    /// <summary>Group Lead Scanner &amp; Auto Sales Commenter.</summary>
    SalesLeads = 3,

    /// <summary>Theme, i18n Language, Browser Engine, and Anti-Checkpoint Settings.</summary>
    Settings = 4,

    /// <summary>Home / Overview status card and quick session controls.</summary>
    Overview = 5,

    /// <summary>Full-page Activity &amp; Audit Logs viewer.</summary>
    AuditLogs = 6
}

/// <summary>
/// Root Shell ViewModel coordinating multi-screen navigation, active account selection,
/// visual target groups, scraped group posts, and activity audit logs.
/// Split into partial files by domain screen:
/// - <c>ShellViewModel.cs</c> (Core navigation &amp; state)
/// - <c>ShellViewModel.Accounts.cs</c> (Zero-UID account vault, J2Team import &amp; 1-click browser login)
/// - <c>ShellViewModel.Groups.cs</c> (Auto group sync, visual selection &amp; group search/join)
/// - <c>ShellViewModel.PublisherAndLeads.cs</c> (Group/Timeline publishing, keyword lead scanner &amp; sales commenting)
/// - <c>ShellViewModel.Settings.cs</c> (Dark/Light theme, vi-VN/en-US language &amp; browser selection)
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly UiServiceContext _context;
    private readonly HashSet<string> _headlessSessionUids = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeTaskCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellViewModel"/> class.
    /// </summary>
    public ShellViewModel(UiServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        this.Navigation = new NavigationService(NavigationSection.Overview);
        this.Navigation.Navigated += (_, section) =>
        {
            if (this.CurrentSection != section)
            {
                this.CurrentSection = section;
            }
        };
        this.AuditDrawer = new AuditLogDrawerViewModel(context);
        this.AiAssistant = new AiAssistantViewModel(
            context,
            onMcpDatabaseUpdated: async () =>
            {
                await this.ReloadAllStoredDataAsync().ConfigureAwait(true);
                await this.QueryLocalScrapedPostsAsync().ConfigureAwait(true);
                await this.AuditDrawer.RefreshLogsAsync().ConfigureAwait(true);
            },
            onExecuteProposedActionInShell: this.HandleAiProposedAction);
        this.ToggleAiAssistantCommand = this.AiAssistant.TogglePanelCommand;

        this.Accounts = new BulkObservableCollection<AccountProfile>();
        this.JoinedGroups = new BulkObservableCollection<GroupSummary>();
        this.TargetGroups = new BulkObservableCollection<SavedTargetGroup>();
        this.GroupTags = new BulkObservableCollection<GroupTag>();
        this.SavedTemplates = new BulkObservableCollection<CampaignTemplate>();
        this.PublishedPosts = new BulkObservableCollection<PublishedPostRecord>();
        this.ScrapedPosts = new BulkObservableCollection<PostSummary>();

        // Load persisted settings synchronously BEFORE the UI window constructs controls
        this.LoadSettingsIntoProperties();

        // Navigation & global commands
        this.NavigateOverviewCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.Overview));
        this.NavigateAccountsCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.Accounts));
        this.NavigateGroupsCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.Groups));
        this.NavigatePublisherCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.Publisher));
        this.NavigateSalesLeadsCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.SalesLeads));
        this.NavigateSettingsCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.Settings));
        this.NavigateAuditLogsCommand = new RelayCommand(() => this.Navigation.NavigateTo(NavigationSection.AuditLogs));
        this.NavigateBackCommand = new RelayCommand(() => _ = this.Navigation.GoBack());
        this.ToggleSidebarCommand = new RelayCommand(() => this.IsSidebarCollapsed = !this.IsSidebarCollapsed);
        this.ToggleThemeQuickCommand = new RelayCommand(this.ToggleThemeQuick);
        this.ToggleLanguageQuickCommand = new RelayCommand(this.ToggleLanguageQuick);
        this.StopActiveTaskCommand = new RelayCommand(this.StopActiveTask);

        // Account commands
        this.QuickImportSessionCommand = new AsyncRelayCommand(this.QuickImportSessionAsync);
        this.OpenBrowserAndSyncCommand = new AsyncRelayCommand(this.OpenBrowserAndSyncAsync);
        this.CheckAccountHealthCommand = new AsyncRelayCommand(this.CheckAccountHealthAsync);
        this.WarmUpAccountCommand = new AsyncRelayCommand(this.WarmUpSelectedAccountAsync);
        this.SaveAccountProxyCommand = new AsyncRelayCommand(this.SaveAccountProxyAsync);
        this.DeleteSelectedAccountCommand = new AsyncRelayCommand(this.DeleteSelectedAccountAsync);

        // Group commands
        this.SyncJoinedGroupsCommand = new AsyncRelayCommand(this.SyncJoinedGroupsAsync);
        this.SearchGroupsByKeywordCommand = new AsyncRelayCommand(this.SearchGroupsByKeywordAsync);
        this.AddSelectedGroupToTargetsCommand = new AsyncRelayCommand(this.AddSelectedGroupToTargetsAsync);
        this.RemoveSelectedTargetGroupCommand = new AsyncRelayCommand(this.RemoveSelectedTargetGroupAsync);
        this.JoinSelectedGroupCommand = new AsyncRelayCommand(this.JoinSelectedGroupAsync);
        this.AutoJoinDiscoveredGroupsCommand = new AsyncRelayCommand(this.AutoJoinDiscoveredGroupsAsync);
        this.EnableAllTargetGroupsCommand = new AsyncRelayCommand(() => this.SetAllTargetGroupsEnabledAsync(true));
        this.DisableAllTargetGroupsCommand = new AsyncRelayCommand(() => this.SetAllTargetGroupsEnabledAsync(false));
        this.CreateGroupTagCommand = new AsyncRelayCommand(this.CreateGroupTagAsync);
        this.DeleteGroupTagCommand = new AsyncRelayCommand<GroupTag>(this.DeleteGroupTagAsync);

        // Publisher & Sales Leads commands
        this.PreviewSpintaxCommand = new RelayCommand(this.GenerateSpintaxPreview);
        this.SaveTemplateCommand = new AsyncRelayCommand(this.SaveTemplateAsync);
        this.SaveBumpTemplateCommand = new AsyncRelayCommand(this.SaveBumpTemplateAsync);
        this.DeleteSelectedTemplateCommand = new AsyncRelayCommand(this.DeleteSelectedTemplateAsync);
        this.PublishCampaignCommand = new AsyncRelayCommand(this.PublishCampaignAsync);
        this.SyncPersonalTimelinePostsCommand = new AsyncRelayCommand(this.SyncPersonalTimelinePostsAsync);
        this.RepostSelectedPostToGroupsCommand = new AsyncRelayCommand(this.RepostSelectedPostToGroupsAsync);
        this.DeletePublishedPostCommand = new AsyncRelayCommand(this.DeleteSelectedPublishedPostAsync);
        this.OpenPublishedPostInBrowserCommand = new AsyncRelayCommand(this.OpenSelectedPublishedPostInBrowserAsync);
        this.BumpRecentPostsCommand = new AsyncRelayCommand(this.BumpRecentPostsAsync);
        this.ScanGroupLeadsCommand = new AsyncRelayCommand(this.ScanGroupLeadsAsync);
        this.QueryLocalScrapedPostsCommand = new AsyncRelayCommand(() => this.QueryLocalScrapedPostsAsync());
        this.ClearScrapedPostsDbCommand = new AsyncRelayCommand(this.ClearScrapedPostsDbAsync);
        this.SendSalesCommentOnSelectedPostCommand = new AsyncRelayCommand(this.SendSalesCommentOnSelectedPostAsync);
        this.AutoCommentAllMatchedPostsCommand = new AsyncRelayCommand(this.AutoCommentAllMatchedPostsAsync);
        this.OpenSelectedPostInBrowserCommand = new AsyncRelayCommand(this.OpenSelectedPostInBrowserAsync);

        // Settings commands
        this.SaveSettingsCommand = new AsyncRelayCommand(this.SaveSettingsAsync);
        this.CheckForUpdatesCommand = new AsyncRelayCommand(this.CheckForUpdatesAsync);
        this.OpenUpdateDownloadCommand = new AsyncRelayCommand(this.DownloadAndApplyUpdateAsync);

        this.UpdateStatusMessage = _context.Localization["Settings.UpdateStatusIdle"];
        this.SyncLocalizedDefaultPrompts();
        _context.Localization.LanguageChanged += this.OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, AppLanguage e) =>
        this.SyncLocalizedDefaultPrompts();

    /// <summary>Gets the shared UI service context.</summary>
    public UiServiceContext Context => _context;

    /// <summary>Gets the page navigation service managing view transitions and back stack.</summary>
    public NavigationService Navigation { get; }

    /// <summary>Gets the collapsible bottom Audit Log drawer ViewModel.</summary>
    public AuditLogDrawerViewModel AuditDrawer { get; }

    /// <summary>Gets the right-hand slide-over AI Assistant ViewModel.</summary>
    public AiAssistantViewModel AiAssistant { get; }

    /// <summary>Gets the command to toggle the right-hand AI Assistant panel.</summary>
    public IRelayCommand ToggleAiAssistantCommand { get; }

    /// <summary>Gets or sets the Spintax post content alias used by AI Assistant actions.</summary>
    public string PostSpintaxContent
    {
        get => this.PostSpintaxText;
        set => this.PostSpintaxText = value;
    }

    /// <summary>Gets or sets the lead filter keywords alias used by AI Assistant actions.</summary>
    public string LeadFilterKeywords
    {
        get => this.BuyerIntentKeywords;
        set
        {
            this.BuyerIntentKeywords = value;
            this.ScrapeFilterKeywords = value;
        }
    }

    /// <summary>Gets or sets the currently active navigation screen.</summary>
    [ObservableProperty]
    public partial NavigationSection CurrentSection { get; set; } = NavigationSection.Overview;

    partial void OnCurrentSectionChanged(NavigationSection value)
    {
        if (this.Navigation.CurrentSection != value)
        {
            this.Navigation.NavigateTo(value);
        }
    }

    /// <summary>Gets or sets a value indicating whether an automation task is currently running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets the collection of managed Facebook accounts (displayed visually without raw UIDs).</summary>
    public BulkObservableCollection<AccountProfile> Accounts { get; }

    /// <summary>Gets the collection of synced/discovered Facebook groups.</summary>
    public BulkObservableCollection<GroupSummary> JoinedGroups { get; }

    /// <summary>Gets the collection of saved target groups selected for publishing and lead scanning.</summary>
    public BulkObservableCollection<SavedTargetGroup> TargetGroups { get; }

    /// <summary>Gets the collection of user-defined color-coded group tags.</summary>
    public BulkObservableCollection<GroupTag> GroupTags { get; }

    /// <summary>Gets the collection of saved Spintax post/comment templates.</summary>
    public BulkObservableCollection<CampaignTemplate> SavedTemplates { get; }

    /// <summary>Gets the collection of posts published from the app or synced from the personal profile timeline.</summary>
    public BulkObservableCollection<PublishedPostRecord> PublishedPosts { get; }

    /// <summary>Gets the collection of scraped group posts (selected visually as cards).</summary>
    public BulkObservableCollection<PostSummary> ScrapedPosts { get; }

    /// <summary>Gets the command to navigate to the Home / Overview screen.</summary>
    public IRelayCommand NavigateOverviewCommand { get; }

    /// <summary>Gets the command to navigate to Accounts screen.</summary>
    public IRelayCommand NavigateAccountsCommand { get; }

    /// <summary>Gets the command to navigate to Groups screen.</summary>
    public IRelayCommand NavigateGroupsCommand { get; }

    /// <summary>Gets the command to navigate to Publisher screen.</summary>
    public IRelayCommand NavigatePublisherCommand { get; }

    /// <summary>Gets the command to navigate to Sales Leads screen.</summary>
    public IRelayCommand NavigateSalesLeadsCommand { get; }

    /// <summary>Gets the command to navigate to Settings screen.</summary>
    public IRelayCommand NavigateSettingsCommand { get; }

    /// <summary>Gets the command to navigate to Activity / Audit Logs screen.</summary>
    public IRelayCommand NavigateAuditLogsCommand { get; }

    /// <summary>Gets the command to navigate back to the previous screen in the navigation stack.</summary>
    public IRelayCommand NavigateBackCommand { get; }

    /// <summary>Gets the command to collapse or expand the left navigation sidebar.</summary>
    public IRelayCommand ToggleSidebarCommand { get; }

    /// <summary>Gets the command to toggle Dark/Light theme from the top bar.</summary>
    public IRelayCommand ToggleThemeQuickCommand { get; }

    /// <summary>Gets the command to toggle `vi-VN` / `en-US` language from the top bar.</summary>
    public IRelayCommand ToggleLanguageQuickCommand { get; }

    /// <summary>Gets the command to immediately cancel any running automation campaign or delay timer.</summary>
    public IRelayCommand StopActiveTaskCommand { get; }

    /// <summary>Gets the command to import a J2Team / Cookie session without typing a UID.</summary>
    public IAsyncRelayCommand QuickImportSessionCommand { get; }

    /// <summary>Gets the command to open the browser for interactive login and automatic group sync.</summary>
    public IAsyncRelayCommand OpenBrowserAndSyncCommand { get; }

    /// <summary>Gets the command to verify live session health of the selected account.</summary>
    public IAsyncRelayCommand CheckAccountHealthCommand { get; }

    /// <summary>Gets the command to run an organic Newsfeed scroll and reaction warm-up for the selected account.</summary>
    public IAsyncRelayCommand WarmUpAccountCommand { get; }

    /// <summary>Gets the command to assign or update a dedicated HTTP/SOCKS5 proxy on the selected account.</summary>
    public IAsyncRelayCommand SaveAccountProxyCommand { get; }

    /// <summary>Gets the command to delete the selected account.</summary>
    public IAsyncRelayCommand DeleteSelectedAccountCommand { get; }

    /// <summary>Gets the command to sync all joined groups from the active account.</summary>
    public IAsyncRelayCommand SyncJoinedGroupsCommand { get; }

    /// <summary>Gets the command to search new groups by keyword.</summary>
    public IAsyncRelayCommand SearchGroupsByKeywordCommand { get; }

    /// <summary>Gets the command to add the selected group to the target campaign list.</summary>
    public IAsyncRelayCommand AddSelectedGroupToTargetsCommand { get; }

    /// <summary>Gets the command to remove the selected target group.</summary>
    public IAsyncRelayCommand RemoveSelectedTargetGroupCommand { get; }

    /// <summary>Gets the command to request joining the selected group.</summary>
    public IAsyncRelayCommand JoinSelectedGroupCommand { get; }

    /// <summary>Gets the command to batch auto-join discovered groups with anti-checkpoint delays.</summary>
    public IAsyncRelayCommand AutoJoinDiscoveredGroupsCommand { get; }

    /// <summary>Gets the command to enable all target groups for campaigns.</summary>
    public IAsyncRelayCommand EnableAllTargetGroupsCommand { get; }

    /// <summary>Gets the command to disable all target groups for campaigns.</summary>
    public IAsyncRelayCommand DisableAllTargetGroupsCommand { get; }

    /// <summary>Gets the command to create a new color-coded group tag.</summary>
    public IAsyncRelayCommand CreateGroupTagCommand { get; }

    /// <summary>Gets the command to delete a color-coded group tag.</summary>
    public IAsyncRelayCommand<GroupTag> DeleteGroupTagCommand { get; }

    /// <summary>Gets the command to generate a live randomized Spintax preview.</summary>
    public IRelayCommand PreviewSpintaxCommand { get; }

    /// <summary>Gets the command to save the current Spintax content as a reusable template.</summary>
    public IAsyncRelayCommand SaveTemplateCommand { get; }

    /// <summary>Gets the command to save the current Bump comment Spintax content as a reusable comment template.</summary>
    public IAsyncRelayCommand SaveBumpTemplateCommand { get; }

    /// <summary>Gets the command to delete the currently selected template from the library.</summary>
    public IAsyncRelayCommand DeleteSelectedTemplateCommand { get; }

    /// <summary>Gets the command to publish a post to selected target groups or personal timeline.</summary>
    public IAsyncRelayCommand PublishCampaignCommand { get; }

    /// <summary>Gets the command to sync recent posts from the active account's personal timeline.</summary>
    public IAsyncRelayCommand SyncPersonalTimelinePostsCommand { get; }

    /// <summary>Gets the command to repost the selected personal/published post into selected target groups.</summary>
    public IAsyncRelayCommand RepostSelectedPostToGroupsCommand { get; }

    /// <summary>Gets the command to delete the selected published post record from local history.</summary>
    public IAsyncRelayCommand DeletePublishedPostCommand { get; }

    /// <summary>Gets the command to open the selected published post in the browser.</summary>
    public IAsyncRelayCommand OpenPublishedPostInBrowserCommand { get; }

    /// <summary>Gets the command to bump (Up-Top) recently published group posts with a comment.</summary>
    public IAsyncRelayCommand BumpRecentPostsCommand { get; }

    /// <summary>Gets the command to selectively scrape recent group posts and store them into the local SQLite database.</summary>
    public IAsyncRelayCommand ScanGroupLeadsCommand { get; }

    /// <summary>Gets the command to search and filter scraped posts directly from the local SQLite database without calling Facebook.</summary>
    public IAsyncRelayCommand QueryLocalScrapedPostsCommand { get; }

    /// <summary>Gets the command to clear stored scraped posts from the local SQLite database.</summary>
    public IAsyncRelayCommand ClearScrapedPostsDbCommand { get; }

    /// <summary>Gets the command to post a sales comment on the visually selected post card.</summary>
    public IAsyncRelayCommand SendSalesCommentOnSelectedPostCommand { get; }

    /// <summary>Gets the command to auto-comment on all scraped posts matching the buyer keyword filter.</summary>
    public IAsyncRelayCommand AutoCommentAllMatchedPostsCommand { get; }

    /// <summary>Gets the command to open the selected scraped post directly in the active browser session.</summary>
    public IAsyncRelayCommand OpenSelectedPostInBrowserCommand { get; }

    /// <summary>Gets the command to persist system settings.</summary>
    public IAsyncRelayCommand SaveSettingsCommand { get; }

    /// <summary>Gets the command to check GitHub Releases for a newer application version.</summary>
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>Gets the command to download and install the latest release update (or open it in the browser).</summary>
    public IAsyncRelayCommand OpenUpdateDownloadCommand { get; }

    /// <summary>
    /// Initializes SQLite databases (<c>facebook_net.db</c>, <c>facebook_audit.db</c>, and <c>facebook_ai_memory.db</c>) and loads stored state.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _context.AccountStore.InitializeAsync().ConfigureAwait(true);
        await _context.AuditStore.InitializeAsync().ConfigureAwait(true);
        await this.AiAssistant.EnsureMemoryLoadedAsync().ConfigureAwait(true);

        this.LoadSettingsIntoProperties();
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);
        await this.QueryLocalScrapedPostsAsync().ConfigureAwait(true);
        await this.AuditDrawer.RefreshLogsAsync().ConfigureAwait(true);
        this.SyncBackgroundScraperLoop();

        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Info,
            targetName: _context.Localization["Audit.Cat.System"],
            message: _context.Localization.Format("Log.SystemInitialized", this.Accounts.Count, this.TargetGroups.Count)).ConfigureAwait(true);
    }

    /// <summary>
    /// Immediately cancels any running automation task or anti-checkpoint delay.
    /// </summary>
    public void StopActiveTask()
    {
        if (_activeTaskCts is not null && !_activeTaskCts.IsCancellationRequested)
        {
            _activeTaskCts.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.StopBackgroundScraperLoop();
        this.FlushSettingsToDisk();
        _context.Localization.LanguageChanged -= this.OnLanguageChanged;
        _activeTaskCts?.Cancel();
        _activeTaskCts?.Dispose();
        _activeTaskCts = null;
        this.AuditDrawer.Dispose();
    }

    private CancellationToken BeginTaskScope()
    {
        _activeTaskCts?.Cancel();
        _activeTaskCts?.Dispose();
        _activeTaskCts = new CancellationTokenSource();
        this.IsBusy = true;
        return _activeTaskCts.Token;
    }

    private void EndTaskScope() => this.IsBusy = false;

    private Task<AuditLogRecord> LogAuditAsync(
        AuditCategory category,
        AuditSeverity severity,
        string targetName,
        string message,
        string? permalinkUrl = null)
    {
        string uid = this.SelectedAccount?.Uid ?? "system";
        string name = this.SelectedAccount?.DisplayName ?? _context.Localization["Audit.Cat.System"];
        return _context.AuditStore.AppendAsync(category, severity, uid, name, targetName, message, permalinkUrl);
    }

    private string SanitizeErrorMessage(Exception ex)
    {
        string raw = ex.Message;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return _context.Localization["Log.ConnectionInterrupted"];
        }

        if (_context.Localization.CurrentLanguage == AppLanguage.EnUs)
        {
            return raw
                .Replace("UID", "account profile", StringComparison.OrdinalIgnoreCase)
                .Replace("fb_dtsg", "session security token", StringComparison.OrdinalIgnoreCase)
                .Replace("c_user", "login session", StringComparison.OrdinalIgnoreCase);
        }

        return raw
            .Replace("UID", "mã định danh", StringComparison.OrdinalIgnoreCase)
            .Replace("fb_dtsg", "mã bảo mật phiên", StringComparison.OrdinalIgnoreCase)
            .Replace("c_user", "phiên đăng nhập", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IBrowserSession?> TryGetOrLaunchActiveSessionAsync(CancellationToken ct = default, bool forceVisible = false)
    {
        if (this.SelectedAccount is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Account"],
                _context.Localization["Log.SelectAccountFirst"]).ConfigureAwait(true);
            return null;
        }

        bool needsInteractiveLogin =
            forceVisible ||
            (string.IsNullOrWhiteSpace(this.SelectedAccount.CookieHeader) &&
             !this.HasProfileAuthenticatedCookieStore(this.SelectedAccount.ProfileDirectory));
        bool isAutomatedHeadless = !needsInteractiveLogin && _context.Settings.Current.RunBrowserHeadless;

        IBrowserSession? existing = _context.SessionManager.TryGetSession(this.SelectedAccount.Uid);
        if (existing is not null && existing.State == Contracts.Primitives.BrowserState.Running)
        {
            if (needsInteractiveLogin && _headlessSessionUids.Contains(this.SelectedAccount.Uid))
            {
                await _context.SessionManager.CloseSessionAsync(this.SelectedAccount.Uid, ct).ConfigureAwait(true);
                _ = _headlessSessionUids.Remove(this.SelectedAccount.Uid);
            }
            else if (!needsInteractiveLogin || existing.IsBrowserProcessAttached)
            {
                return existing;
            }
        }

        ProxyConfig? resolvedProxy = null;
        if (!string.IsNullOrWhiteSpace(this.SelectedAccount.ProxyId))
        {
            IReadOnlyList<ProxyConfig> proxies =
                await _context.AccountStore.GetAllProxiesAsync(ct).ConfigureAwait(true);
            for (int i = 0; i < proxies.Count; i++)
            {
                if (string.Equals(proxies[i].Id, this.SelectedAccount.ProxyId, StringComparison.Ordinal))
                {
                    resolvedProxy = proxies[i];
                    break;
                }
            }
        }

        string? preferredExe = _context.Settings.ResolvePreferredBrowserExecutable();
        BrowserLaunchOptions options = new(
            AccountUid: this.SelectedAccount.Uid,
            UserDataDir: this.SelectedAccount.ProfileDirectory,
            UserAgent: this.SelectedAccount.UserAgent,
            Proxy: resolvedProxy,
            Headless: isAutomatedHeadless,
            ExecutablePath: preferredExe,
            BlockStaticMedia: isAutomatedHeadless,
            LowResourceMode: isAutomatedHeadless,
            TerminateProcessAfterExtract: isAutomatedHeadless,
            CleanDiskCache: true);

        IBrowserSession session = await _context.SessionManager.AcquireSessionAsync(options, ct).ConfigureAwait(true);
        if (isAutomatedHeadless)
        {
            _ = _headlessSessionUids.Add(this.SelectedAccount.Uid);
        }
        else
        {
            _ = _headlessSessionUids.Remove(this.SelectedAccount.Uid);
        }

        if (!string.IsNullOrWhiteSpace(this.SelectedAccount.CookieHeader))
        {
            _ = await session.ImportCookiesAsync(this.SelectedAccount.CookieHeader, ct: ct).ConfigureAwait(true);
        }

        return session;
    }

    private void HandleAiProposedAction(AgyProposedAction action)
    {
        if (action is null)
        {
            return;
        }

        if (string.Equals(action.ActionKind, "PUBLISH_GROUPS", StringComparison.OrdinalIgnoreCase))
        {
            this.PostSpintaxContent = action.ContentText ?? this.PostSpintaxContent;
            this.Navigation.NavigateTo(NavigationSection.Publisher);
            _ = this.PublishCampaignAsync();
        }
        else if (string.Equals(action.ActionKind, "EDIT_DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            this.PostSpintaxContent = action.ContentText ?? this.PostSpintaxContent;
            this.Navigation.NavigateTo(NavigationSection.Publisher);
        }
        else if (string.Equals(action.ActionKind, "SEARCH_GROUPS", StringComparison.OrdinalIgnoreCase))
        {
            this.GroupSearchKeyword = action.GroupKeyword ?? string.Empty;
            this.Navigation.NavigateTo(NavigationSection.Groups);
            _ = this.SearchGroupsByKeywordAsync();
        }
        else if (string.Equals(action.ActionKind, "SCAN_LEADS", StringComparison.OrdinalIgnoreCase))
        {
            this.LeadFilterKeywords = action.GroupKeyword ?? this.LeadFilterKeywords;
            this.Navigation.NavigateTo(NavigationSection.SalesLeads);
            _ = this.ScanGroupLeadsAsync();
        }
    }
}
