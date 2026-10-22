// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Facebook.Net.Browser.MacOS;
using Facebook.Net.Browser.Session;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Client;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Storage;
using Meta.UI.Core.Accounts;
using Meta.UI.Core.AiAssistant;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Groups;
using Meta.UI.Core.Leads;
using Meta.UI.Core.Publisher;
using Microsoft.Extensions.Logging;

namespace Facebook.Net.Mcp;

/// <summary>
/// Central state coordinator for the <c>Facebook.Net.Mcp</c> server.
/// Shares the SQLite database (<c>facebook_net.db</c>), audit log store (<c>facebook_audit.db</c>),
/// <c>appsettings.json</c>, and isolated Chromium <c>Profiles/</c> directory with the Facebook One Client desktop suite.
/// </summary>
public sealed class FacebookMcpSessionManager : IDisposable
{
    private readonly ILogger<FacebookMcpSessionManager>? _logger;
    private readonly TaskCompletionSource _bootstrapCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _bootstrapStarted;
    private string? _activeAccountUid;

    public FacebookMcpSessionManager(
        ILogger<FacebookMcpSessionManager>? logger = null,
        string? customDataDir = null)
    {
        _logger = logger;

        string envDir = Environment.GetEnvironmentVariable("FACEBOOK_MCP_DATA_DIR") ?? string.Empty;
        string rootDir = !string.IsNullOrWhiteSpace(customDataDir)
            ? customDataDir
            : (!string.IsNullOrWhiteSpace(envDir)
                ? envDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Facebook.Net"));

        _ = Directory.CreateDirectory(rootDir);
        this.DataDirectory = rootDir;

        this.ProfilesRootDirectory = Path.Combine(rootDir, "Profiles");
        _ = Directory.CreateDirectory(this.ProfilesRootDirectory);

        this.Settings = new AppSettingsService(Path.Combine(rootDir, "appsettings.json"));
        this.AccountStore = new SqliteAccountStore(Path.Combine(rootDir, "facebook_net.db"));
        this.AuditStore = new SqliteAuditLogStore(Path.Combine(rootDir, "facebook_audit.db"));
        this.AiMemoryStore = new SqliteAiMemoryStore(Path.Combine(rootDir, "facebook_ai_memory.db"));

        IBrowserAdapter platformAdapter = OperatingSystem.IsMacOS()
            ? new MacOsBrowserAdapter()
            : new WindowsBrowserAdapter();

        this.SessionManager = new BrowserSessionManager(platformAdapter);
        this.CometClient = new CometClient();
        this.LeadScoring = LeadScoringService.Instance;
        this.AccountVault = AccountVaultDomainService.Instance;
        this.GroupTagService = GroupTagDomainService.Instance;
        this.CampaignPublisher = CampaignPublisherDomainService.Instance;
        this.StorageUsage = StorageUsageService.Instance;
        this.AppRelease = GitHubAppReleaseService.Instance;
        _activeAccountUid = this.Settings.Current.LastSelectedAccountUid;
    }

    /// <summary>Gets the root data directory.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the isolated Chromium profiles directory.</summary>
    public string ProfilesRootDirectory { get; }

    /// <summary>Gets the application settings service.</summary>
    public IAppSettingsService Settings { get; }

    /// <summary>Gets the main SQLite account, group, template, and scraped post store.</summary>
    public IAccountStore AccountStore { get; }

    /// <summary>Gets the dedicated SQLite audit log store.</summary>
    public IAuditLogStore AuditStore { get; }

    /// <summary>Gets the shared SQLite AI context memory and chat history store (<c>facebook_ai_memory.db</c>).</summary>
    public IAiMemoryStore AiMemoryStore { get; }

    /// <summary>Gets the multi-profile browser session manager.</summary>
    public IBrowserManager SessionManager { get; }

    /// <summary>Gets the Comet GraphQL client.</summary>
    public ICometClient CometClient { get; }

    /// <summary>Gets the buyer-intent lead scoring domain service.</summary>
    public ILeadScoringService LeadScoring { get; }

    /// <summary>Gets the account vault domain service.</summary>
    public IAccountVaultDomainService AccountVault { get; }

    /// <summary>Gets the group tag domain service.</summary>
    public IGroupTagDomainService GroupTagService { get; }

    /// <summary>Gets the campaign publisher domain service.</summary>
    public ICampaignPublisherDomainService CampaignPublisher { get; }

    /// <summary>Gets the storage usage analysis domain service.</summary>
    public IStorageUsageService StorageUsage { get; }

    /// <summary>Gets the application release update check domain service.</summary>
    public IAppReleaseService AppRelease { get; }

    /// <summary>Gets the currently selected active account UID.</summary>
    public string? ActiveAccountUid => _activeAccountUid;

    /// <summary>
    /// Starts non-blocking background initialization of SQLite tables and saved accounts so the MCP
    /// <c>initialize</c> handshake over stdio responds immediately.
    /// </summary>
    public void StartBootstrapInBackground()
    {
        if (Interlocked.Exchange(ref _bootstrapStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Background Facebook MCP bootstrap encountered an error.");
            }
            finally
            {
                _ = _bootstrapCompletion.TrySetResult();
            }
        });
    }

    /// <summary>
    /// Initializes SQLite stores and normalizes stored accounts.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await this.AccountStore.InitializeAsync(ct).ConfigureAwait(false);
            await this.AuditStore.InitializeAsync(ct).ConfigureAwait(false);
            await this.AiMemoryStore.InitializeAsync(ct).ConfigureAwait(false);

            IReadOnlyList<AccountProfile> accounts = await this.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
            for (int i = 0; i < accounts.Count; i++)
            {
                (AccountProfile normalized, bool modified) = this.AccountVault.NormalizeAccountProfile(accounts[i]);
                if (modified)
                {
                    await this.AccountStore.UpsertAccountAsync(normalized, ct).ConfigureAwait(false);
                }
            }

            if (string.IsNullOrWhiteSpace(_activeAccountUid) && accounts.Count > 0)
            {
                _activeAccountUid = accounts[0].Uid;
            }
        }
        finally
        {
            _ = _bootstrapCompletion.TrySetResult();
        }
    }

    /// <summary>
    /// Waits briefly for background database initialization if it is still in flight.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_bootstrapStarted == 0)
        {
            await this.InitializeAsync(ct).ConfigureAwait(false);
        }
        else
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await _bootstrapCompletion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Continue even if bootstrap timed out
            }
        }

        AppConfig reloaded = this.Settings.Reload();
        if (!string.IsNullOrWhiteSpace(reloaded.LastSelectedAccountUid))
        {
            _activeAccountUid = reloaded.LastSelectedAccountUid;
        }
    }

    /// <summary>
    /// Switches the active Facebook account UID and persists the selection to <c>appsettings.json</c>.
    /// </summary>
    public async Task<AccountProfile?> SwitchActiveAccountAsync(string accountUid, CancellationToken ct = default)
    {
        await this.EnsureInitializedAsync(ct).ConfigureAwait(false);
        AccountProfile? account = await this.AccountStore.GetAccountByUidAsync(accountUid, ct).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        _activeAccountUid = account.Uid;
        this.Settings.Save(this.Settings.Current with { LastSelectedAccountUid = account.Uid });
        return account;
    }

    /// <summary>
    /// Resolves the target <see cref="AccountProfile"/> (either by explicit <paramref name="accountUid"/> or the active account).
    /// </summary>
    public async Task<AccountProfile> ResolveAccountAsync(string? accountUid = null, CancellationToken ct = default)
    {
        await this.EnsureInitializedAsync(ct).ConfigureAwait(false);
        string? targetUid = !string.IsNullOrWhiteSpace(accountUid) ? accountUid.Trim() : _activeAccountUid;

        if (!string.IsNullOrWhiteSpace(targetUid))
        {
            AccountProfile? found = await this.AccountStore.GetAccountByUidAsync(targetUid, ct).ConfigureAwait(false);
            if (found is not null)
            {
                _activeAccountUid ??= found.Uid;
                return found;
            }
        }

        IReadOnlyList<AccountProfile> all = await this.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
        if (all.Count > 0)
        {
            _activeAccountUid = all[0].Uid;
            return all[0];
        }

        throw new InvalidOperationException("No Facebook account found in the vault. Call 'fb_import_session' or 'fb_open_browser_login' first.");
    }

    /// <summary>
    /// Acquires or launches an isolated Chromium <see cref="IBrowserSession"/> for the target account.
    /// </summary>
    public async Task<(AccountProfile Account, IBrowserSession Session)> AcquireBrowserSessionAsync(
        string? accountUid = null,
        bool forceVisible = false,
        CancellationToken ct = default)
    {
        AccountProfile account = await this.ResolveAccountAsync(accountUid, ct).ConfigureAwait(false);

        IBrowserSession? existing = this.SessionManager.TryGetSession(account.Uid);
        if (existing is not null &&
            existing.State == BrowserState.Running &&
            (!forceVisible || existing.IsBrowserProcessAttached))
        {
            return (account, existing);
        }

        ProxyConfig? resolvedProxy = null;
        if (!string.IsNullOrWhiteSpace(account.ProxyId))
        {
            IReadOnlyList<ProxyConfig> proxies = await this.AccountStore.GetAllProxiesAsync(ct).ConfigureAwait(false);
            for (int i = 0; i < proxies.Count; i++)
            {
                if (string.Equals(proxies[i].Id, account.ProxyId, StringComparison.Ordinal))
                {
                    resolvedProxy = proxies[i];
                    break;
                }
            }
        }

        bool isHeadless = !forceVisible && this.Settings.Current.RunBrowserHeadless;
        string? preferredExe = this.Settings.ResolvePreferredBrowserExecutable();
        BrowserLaunchOptions options = new(
            AccountUid: account.Uid,
            UserDataDir: account.ProfileDirectory,
            UserAgent: account.UserAgent,
            Proxy: resolvedProxy,
            Headless: isHeadless,
            ExecutablePath: preferredExe,
            BlockStaticMedia: isHeadless,
            LowResourceMode: isHeadless,
            TerminateProcessAfterExtract: isHeadless,
            CleanDiskCache: true);

        IBrowserSession session = await this.SessionManager.AcquireSessionAsync(options, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(account.CookieHeader))
        {
            _ = await session.ImportCookiesAsync(account.CookieHeader, ct: ct).ConfigureAwait(false);
        }

        return (account, session);
    }

    /// <summary>
    /// Appends an entry to the shared SQLite audit log (<c>facebook_audit.db</c>).
    /// </summary>
    public Task<AuditLogRecord> LogAuditAsync(
        AuditCategory category,
        AuditSeverity severity,
        AccountProfile? account,
        string targetName,
        string message,
        string? permalinkUrl = null,
        CancellationToken ct = default)
    {
        string uid = account?.Uid ?? _activeAccountUid ?? "mcp";
        string displayName = account?.DisplayName ?? "Facebook.Net.Mcp";
        return this.AuditStore.AppendAsync(category, severity, uid, displayName, targetName, message, permalinkUrl, ct);
    }

    public void Dispose() => this.SessionManager.Dispose();
}
