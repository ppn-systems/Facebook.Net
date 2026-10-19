// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Audit;
using Facebook.Net.UI.ViewModels.Services;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Navigation tabs in the Cloudflare-styled sidebar.
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
    Settings = 4
}

/// <summary>
/// Root Shell ViewModel coordinating multi-screen navigation, active account selection,
/// visual target groups, scraped group posts, and the collapsible bottom Audit Log drawer.
/// Split into partial files by domain screen:
/// - <c>ShellViewModel.cs</c> (Core navigation &amp; state)
/// - <c>ShellViewModel.Accounts.cs</c> (Zero-UID account vault, J2Team import &amp; 1-click browser login)
/// - <c>ShellViewModel.Groups.cs</c> (Auto group sync, visual selection &amp; group search/join)
/// - <c>ShellViewModel.PublisherAndLeads.cs</c> (Group/Timeline publishing, keyword lead scanner &amp; sales commenting)
/// - <c>ShellViewModel.Settings.cs</c> (Cloudflare Dark/Light theme, vi-VN/en-US language &amp; browser selection)
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly UiServiceContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellViewModel"/> class.
    /// </summary>
    public ShellViewModel(UiServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this._context = context;
        this.AuditDrawer = new AuditLogDrawerViewModel(context);

        this.Accounts = new ObservableCollection<AccountProfile>();
        this.JoinedGroups = new ObservableCollection<GroupSummary>();
        this.TargetGroups = new ObservableCollection<SavedTargetGroup>();
        this.SavedTemplates = new ObservableCollection<CampaignTemplate>();
        this.ScrapedPosts = new ObservableCollection<PostSummary>();

        // Navigation commands
        this.NavigateAccountsCommand = new RelayCommand(() => this.CurrentSection = NavigationSection.Accounts);
        this.NavigateGroupsCommand = new RelayCommand(() => this.CurrentSection = NavigationSection.Groups);
        this.NavigatePublisherCommand = new RelayCommand(() => this.CurrentSection = NavigationSection.Publisher);
        this.NavigateSalesLeadsCommand = new RelayCommand(() => this.CurrentSection = NavigationSection.SalesLeads);
        this.NavigateSettingsCommand = new RelayCommand(() => this.CurrentSection = NavigationSection.Settings);
        this.ToggleThemeQuickCommand = new RelayCommand(this.ToggleThemeQuick);
        this.ToggleLanguageQuickCommand = new RelayCommand(this.ToggleLanguageQuick);

        // Account commands
        this.QuickImportSessionCommand = new AsyncRelayCommand(this.QuickImportSessionAsync);
        this.OpenBrowserAndSyncCommand = new AsyncRelayCommand(this.OpenBrowserAndSyncAsync);
        this.DeleteSelectedAccountCommand = new AsyncRelayCommand(this.DeleteSelectedAccountAsync);

        // Group commands
        this.SyncJoinedGroupsCommand = new AsyncRelayCommand(this.SyncJoinedGroupsAsync);
        this.SearchGroupsByKeywordCommand = new AsyncRelayCommand(this.SearchGroupsByKeywordAsync);
        this.AddSelectedGroupToTargetsCommand = new AsyncRelayCommand(this.AddSelectedGroupToTargetsAsync);
        this.RemoveSelectedTargetGroupCommand = new AsyncRelayCommand(this.RemoveSelectedTargetGroupAsync);
        this.JoinSelectedGroupCommand = new AsyncRelayCommand(this.JoinSelectedGroupAsync);

        // Publisher & Sales Leads commands
        this.SaveTemplateCommand = new AsyncRelayCommand(this.SaveTemplateAsync);
        this.PublishCampaignCommand = new AsyncRelayCommand(this.PublishCampaignAsync);
        this.ScanGroupLeadsCommand = new AsyncRelayCommand(this.ScanGroupLeadsAsync);
        this.SendSalesCommentOnSelectedPostCommand = new AsyncRelayCommand(this.SendSalesCommentOnSelectedPostAsync);
        this.AutoCommentAllMatchedPostsCommand = new AsyncRelayCommand(this.AutoCommentAllMatchedPostsAsync);

        // Settings commands
        this.SaveSettingsCommand = new AsyncRelayCommand(this.SaveSettingsAsync);
    }

    /// <summary>Gets the shared UI service context.</summary>
    public UiServiceContext Context => this._context;

    /// <summary>Gets the collapsible bottom Audit Log drawer ViewModel.</summary>
    public AuditLogDrawerViewModel AuditDrawer { get; }

    /// <summary>Gets or sets the currently active navigation screen.</summary>
    [ObservableProperty]
    public partial NavigationSection CurrentSection { get; set; } = NavigationSection.Accounts;

    /// <summary>Gets or sets a value indicating whether an automation task is currently running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets the collection of managed Facebook accounts (displayed visually without raw UIDs).</summary>
    public ObservableCollection<AccountProfile> Accounts { get; }

    /// <summary>Gets the collection of synced/discovered Facebook groups.</summary>
    public ObservableCollection<GroupSummary> JoinedGroups { get; }

    /// <summary>Gets the collection of saved target groups selected for publishing and lead scanning.</summary>
    public ObservableCollection<SavedTargetGroup> TargetGroups { get; }

    /// <summary>Gets the collection of saved Spintax post/comment templates.</summary>
    public ObservableCollection<CampaignTemplate> SavedTemplates { get; }

    /// <summary>Gets the collection of scraped group posts (selected visually as cards).</summary>
    public ObservableCollection<PostSummary> ScrapedPosts { get; }

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

    /// <summary>Gets the command to toggle Dark/Light theme from the top bar.</summary>
    public IRelayCommand ToggleThemeQuickCommand { get; }

    /// <summary>Gets the command to toggle `vi-VN` / `en-US` language from the top bar.</summary>
    public IRelayCommand ToggleLanguageQuickCommand { get; }

    /// <summary>Gets the command to import a J2Team / Cookie session without typing a UID.</summary>
    public IAsyncRelayCommand QuickImportSessionCommand { get; }

    /// <summary>Gets the command to open the browser for interactive login and automatic group sync.</summary>
    public IAsyncRelayCommand OpenBrowserAndSyncCommand { get; }

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

    /// <summary>Gets the command to save the current Spintax content as a reusable template.</summary>
    public IAsyncRelayCommand SaveTemplateCommand { get; }

    /// <summary>Gets the command to publish a post to selected target groups or personal timeline.</summary>
    public IAsyncRelayCommand PublishCampaignCommand { get; }

    /// <summary>Gets the command to scan recent posts inside the selected group and filter by buyer keywords.</summary>
    public IAsyncRelayCommand ScanGroupLeadsCommand { get; }

    /// <summary>Gets the command to post a sales comment on the visually selected post card.</summary>
    public IAsyncRelayCommand SendSalesCommentOnSelectedPostCommand { get; }

    /// <summary>Gets the command to auto-comment on all scraped posts matching the buyer keyword filter.</summary>
    public IAsyncRelayCommand AutoCommentAllMatchedPostsCommand { get; }

    /// <summary>Gets the command to persist system settings.</summary>
    public IAsyncRelayCommand SaveSettingsCommand { get; }

    /// <summary>
    /// Initializes both SQLite databases (<c>facebook_net.db</c> and <c>facebook_audit.db</c>) and loads stored state.
    /// </summary>
    public async Task InitializeAsync()
    {
        await this._context.AccountStore.InitializeAsync().ConfigureAwait(true);
        await this._context.AuditStore.InitializeAsync().ConfigureAwait(true);

        this.LoadSettingsIntoProperties();
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);
        await this.AuditDrawer.RefreshLogsAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Info,
            targetName: "Hệ thống",
            message: $"Khởi động hoàn tất ({this.Accounts.Count} tài khoản, {this.TargetGroups.Count} nhóm mục tiêu).").ConfigureAwait(true);
    }

    private Task<AuditLogRecord> LogAuditAsync(
        AuditCategory category,
        AuditSeverity severity,
        string targetName,
        string message,
        string? permalinkUrl = null)
    {
        string uid = this.SelectedAccount?.Uid ?? "system";
        string name = this.SelectedAccount?.DisplayName ?? "Hệ thống";
        return this._context.AuditStore.AppendAsync(category, severity, uid, name, targetName, message, permalinkUrl);
    }

    private async Task<IBrowserSession?> TryGetOrLaunchActiveSessionAsync()
    {
        if (this.SelectedAccount is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                "Tài khoản",
                "Vui lòng chọn hoặc thêm một tài khoản trước khi thực hiện tác vụ.").ConfigureAwait(true);
            return null;
        }

        IBrowserSession? existing = this._context.SessionManager.TryGetSession(this.SelectedAccount.Uid);
        if (existing is not null)
        {
            return existing;
        }

        BrowserLaunchOptions options = new(
            AccountUid: this.SelectedAccount.Uid,
            UserDataDir: this.SelectedAccount.ProfileDirectory,
            UserAgent: this.SelectedAccount.UserAgent,
            Headless: this._context.Settings.Current.RunBrowserHeadless);

        IBrowserSession session = await this._context.SessionManager.AcquireSessionAsync(options).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(this.SelectedAccount.CookieHeader))
        {
            _ = await session.ImportCookiesAsync(this.SelectedAccount.CookieHeader).ConfigureAwait(true);
        }

        return session;
    }
}
