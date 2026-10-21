// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Browser.Session;
using Facebook.Net.Client;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Storage;
using Facebook.Net.UI.Core.Accounts;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Groups;
using Facebook.Net.UI.Core.Leads;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Publisher;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.Core.Threading;

namespace Facebook.Net.UI.ViewModels.Services;

/// <summary>
/// Wraps an OS-specific <see cref="IBrowserAdapter"/> so that if the user configures a specific
/// browser in Settings (Edge, Chrome, Brave, or custom path), it is prioritized.
/// </summary>
public sealed class ConfigurableBrowserAdapter : IBrowserAdapter
{
    private readonly IBrowserAdapter _inner;
    private readonly IAppSettingsService _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurableBrowserAdapter"/> class.
    /// </summary>
    public ConfigurableBrowserAdapter(IBrowserAdapter inner, IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(settings);
        _inner = inner;
        _settings = settings;
    }

    /// <inheritdoc />
    public string PlatformName => _inner.PlatformName;

    /// <inheritdoc />
    public string? LocateExecutablePath() =>
        _settings.ResolvePreferredBrowserExecutable() ?? _inner.LocateExecutablePath();

    /// <inheritdoc />
    public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? preferredExe = _settings.ResolvePreferredBrowserExecutable();
        BrowserLaunchOptions effectiveOptions = !string.IsNullOrWhiteSpace(preferredExe) && string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? options with { ExecutablePath = preferredExe }
            : options;

        return _inner.LaunchProcessAsync(effectiveOptions, ct);
    }
}

/// <summary>
/// Central UI application service context aggregating theme, localization, settings,
/// main SQLite store (<c>facebook_net.db</c>), dedicated Audit Log SQLite store (<c>facebook_audit.db</c>),
/// browser session manager, and Comet GraphQL client.
/// Exposes contract interfaces (<see cref="IAccountStore"/>, <see cref="IAuditLogStore"/>, <see cref="IBrowserManager"/>, <see cref="ICometClient"/>)
/// and accepts optional test doubles for full unit testability.
/// </summary>
public sealed class UiServiceContext : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiServiceContext"/> class.
    /// </summary>
    /// <param name="platformBrowserAdapter">The OS-specific browser adapter.</param>
    /// <param name="customDataDir">Optional custom root data directory (for testing).</param>
    /// <param name="dispatcher">Optional UI thread dispatcher abstraction.</param>
    /// <param name="accountStore">Optional custom account store implementation (for testing).</param>
    /// <param name="auditStore">Optional custom audit log store implementation (for testing).</param>
    /// <param name="sessionManager">Optional custom browser session manager implementation (for testing).</param>
    /// <param name="cometClient">Optional custom Comet GraphQL client implementation (for testing).</param>
    /// <param name="storageUsage"></param>
    /// <param name="leadScoring"></param>
    /// <param name="accountVault"></param>
    /// <param name="groupTagService"></param>
    /// <param name="aiMemoryStore">Optional custom AI Memory SQLite store implementation (for testing).</param>
    /// <param name="agyCliService">Optional custom Google Antigravity CLI service implementation (for testing).</param>
    /// <param name="campaignPublisher">Optional custom campaign publisher domain service (for testing).</param>
    /// <param name="appRelease">Optional custom application release service (for testing).</param>
    public UiServiceContext(
        IBrowserAdapter platformBrowserAdapter,
        string? customDataDir = null,
        IUiDispatcher? dispatcher = null,
        IAccountStore? accountStore = null,
        IAuditLogStore? auditStore = null,
        IBrowserManager? sessionManager = null,
        ICometClient? cometClient = null,
        IStorageUsageService? storageUsage = null,
        ILeadScoringService? leadScoring = null,
        IAccountVaultDomainService? accountVault = null,
        IGroupTagDomainService? groupTagService = null,
        IAiMemoryStore? aiMemoryStore = null,
        IAgyCliService? agyCliService = null,
        ICampaignPublisherDomainService? campaignPublisher = null,
        IAppReleaseService? appRelease = null)
    {
        ArgumentNullException.ThrowIfNull(platformBrowserAdapter);

        this.Dispatcher = dispatcher ?? AvaloniaUiDispatcher.Instance;

        string rootDir = string.IsNullOrWhiteSpace(customDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Facebook.Net")
            : customDataDir;

        _ = Directory.CreateDirectory(rootDir);
        this.DataDirectory = rootDir;

        this.ProfilesRootDirectory = Path.Combine(rootDir, "Profiles");
        _ = Directory.CreateDirectory(this.ProfilesRootDirectory);

        string settingsPath = Path.Combine(rootDir, "appsettings.json");
        this.Settings = new AppSettingsService(settingsPath);

        this.Theme = new ThemeService(this.Dispatcher);
        this.Theme.SetTheme(this.Settings.Current.Theme);

        this.Localization = new LocalizationService();
        this.Localization.SetLanguage(this.Settings.Current.Language);

        string mainDbPath = Path.Combine(rootDir, "facebook_net.db");
        this.AccountStore = accountStore ?? new SqliteAccountStore(mainDbPath);

        string auditDbPath = Path.Combine(rootDir, "facebook_audit.db");
        this.AuditStore = auditStore ?? new SqliteAuditLogStore(auditDbPath);

        string aiMemoryDbPath = Path.Combine(rootDir, "facebook_ai_memory.db");
        this.AiMemoryStore = aiMemoryStore ?? new SqliteAiMemoryStore(aiMemoryDbPath);
        this.AgyCli = agyCliService ?? new AgyCliService(rootDir);

        this.StorageUsage = storageUsage ?? StorageUsageService.Instance;
        this.LeadScoring = leadScoring ?? LeadScoringService.Instance;
        this.AccountVault = accountVault ?? AccountVaultDomainService.Instance;
        this.GroupTagService = groupTagService ?? GroupTagDomainService.Instance;
        this.CampaignPublisher = campaignPublisher ?? CampaignPublisherDomainService.Instance;
        this.AppRelease = appRelease ?? GitHubAppReleaseService.Instance;

        this.BrowserAdapter = new ConfigurableBrowserAdapter(platformBrowserAdapter, this.Settings);
        this.SessionManager = sessionManager ?? new BrowserSessionManager(this.BrowserAdapter);
        this.CometClient = cometClient ?? new CometClient();
    }

    /// <summary>Gets the UI thread dispatcher abstraction.</summary>
    public IUiDispatcher Dispatcher { get; }

    /// <summary>Gets the root data directory.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the isolated Chromium profiles directory.</summary>
    public string ProfilesRootDirectory { get; }

    /// <summary>Gets the application settings service.</summary>
    public AppSettingsService Settings { get; }

    /// <summary>Gets the application settings service contract.</summary>
    public IAppSettingsService SettingsService => this.Settings;

    /// <summary>Gets the Dark/Light theme service.</summary>
    public ThemeService Theme { get; }

    /// <summary>Gets the bilingual (`vi-VN` / `en-US`) localization service.</summary>
    public LocalizationService Localization { get; }

    /// <summary>Gets the main account, group, and campaign store (<c>facebook_net.db</c>).</summary>
    public IAccountStore AccountStore { get; }

    /// <summary>Gets the dedicated audit log store (<c>facebook_audit.db</c>).</summary>
    public IAuditLogStore AuditStore { get; }

    /// <summary>Gets the shared AI Assistant persistent memory &amp; chat store (<c>facebook_ai_memory.db</c>).</summary>
    public IAiMemoryStore AiMemoryStore { get; }

    /// <summary>Gets the Google Antigravity CLI (<c>agy</c>) and CDP OAuth orchestration service.</summary>
    public IAgyCliService AgyCli { get; }

    /// <summary>Gets the disk and profile storage usage domain service.</summary>
    public IStorageUsageService StorageUsage { get; }

    /// <summary>Gets the buyer-intent lead scoring and Vietnamese diacritic normalization domain service.</summary>
    public ILeadScoringService LeadScoring { get; }

    /// <summary>Gets the account vault and cookie store authentication domain service.</summary>
    public IAccountVaultDomainService AccountVault { get; }

    /// <summary>Gets the group color tag and anti-spam group selection domain service.</summary>
    public IGroupTagDomainService GroupTagService { get; }

    /// <summary>Gets the campaign publisher domain service.</summary>
    public ICampaignPublisherDomainService CampaignPublisher { get; }

    /// <summary>Gets the application release and update check service.</summary>
    public IAppReleaseService AppRelease { get; }

    /// <summary>Gets the configurable browser adapter.</summary>
    public IBrowserAdapter BrowserAdapter { get; }

    /// <summary>Gets the multi-profile browser session manager.</summary>
    public IBrowserManager SessionManager { get; }

    /// <summary>Gets the Comet GraphQL client.</summary>
    public ICometClient CometClient { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.AgyCli is IDisposable disposableAgy)
        {
            disposableAgy.Dispose();
        }

        this.SessionManager.Dispose();
    }
}
