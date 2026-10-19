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
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.ViewModels.Services;

/// <summary>
/// Wraps an OS-specific <see cref="IBrowserAdapter"/> so that if the user configures a specific
/// browser in Settings (Edge, Chrome, Brave, or custom path), it is prioritized.
/// </summary>
public sealed class ConfigurableBrowserAdapter : IBrowserAdapter
{
    private readonly IBrowserAdapter _inner;
    private readonly AppSettingsService _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurableBrowserAdapter"/> class.
    /// </summary>
    public ConfigurableBrowserAdapter(IBrowserAdapter inner, AppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(settings);
        this._inner = inner;
        this._settings = settings;
    }

    /// <inheritdoc />
    public string PlatformName => this._inner.PlatformName;

    /// <inheritdoc />
    public string? LocateExecutablePath() =>
        this._settings.ResolvePreferredBrowserExecutable() ?? this._inner.LocateExecutablePath();

    /// <inheritdoc />
    public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? preferredExe = this._settings.ResolvePreferredBrowserExecutable();
        BrowserLaunchOptions effectiveOptions = !string.IsNullOrWhiteSpace(preferredExe) && string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? options with { ExecutablePath = preferredExe, Headless = this._settings.Current.RunBrowserHeadless }
            : options with { Headless = this._settings.Current.RunBrowserHeadless };

        return this._inner.LaunchProcessAsync(effectiveOptions, ct);
    }
}

/// <summary>
/// Central UI application service context aggregating theme, localization, settings,
/// main SQLite store (<c>facebook_net.db</c>), dedicated Audit Log SQLite store (<c>facebook_audit.db</c>),
/// browser session manager, and Comet GraphQL client.
/// </summary>
public sealed class UiServiceContext : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UiServiceContext"/> class.
    /// </summary>
    /// <param name="platformBrowserAdapter">The OS-specific browser adapter.</param>
    /// <param name="customDataDir">Optional custom root data directory (for testing).</param>
    public UiServiceContext(IBrowserAdapter platformBrowserAdapter, string? customDataDir = null)
    {
        ArgumentNullException.ThrowIfNull(platformBrowserAdapter);

        string rootDir = string.IsNullOrWhiteSpace(customDataDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Facebook.Net")
            : customDataDir;

        _ = Directory.CreateDirectory(rootDir);
        this.DataDirectory = rootDir;

        this.ProfilesRootDirectory = Path.Combine(rootDir, "Profiles");
        _ = Directory.CreateDirectory(this.ProfilesRootDirectory);

        string settingsPath = Path.Combine(rootDir, "appsettings.json");
        this.Settings = new AppSettingsService(settingsPath);

        this.Theme = new ThemeService();
        this.Theme.SetTheme(this.Settings.Current.Theme);

        this.Localization = new LocalizationService();
        this.Localization.SetLanguage(this.Settings.Current.Language);

        string mainDbPath = Path.Combine(rootDir, "facebook_net.db");
        this.AccountStore = new SqliteAccountStore(mainDbPath);

        string auditDbPath = Path.Combine(rootDir, "facebook_audit.db");
        this.AuditStore = new SqliteAuditLogStore(auditDbPath);

        this.BrowserAdapter = new ConfigurableBrowserAdapter(platformBrowserAdapter, this.Settings);
        this.SessionManager = new BrowserSessionManager(this.BrowserAdapter);
        this.CometClient = new CometClient();
    }

    /// <summary>Gets the root data directory.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the isolated Chromium profiles directory.</summary>
    public string ProfilesRootDirectory { get; }

    /// <summary>Gets the application settings service.</summary>
    public AppSettingsService Settings { get; }

    /// <summary>Gets the Cloudflare Dark/Light theme service.</summary>
    public ThemeService Theme { get; }

    /// <summary>Gets the bilingual (`vi-VN` / `en-US`) localization service.</summary>
    public LocalizationService Localization { get; }

    /// <summary>Gets the main SQLite account, group, and campaign store (<c>facebook_net.db</c>).</summary>
    public SqliteAccountStore AccountStore { get; }

    /// <summary>Gets the dedicated SQLite audit log store (<c>facebook_audit.db</c>).</summary>
    public SqliteAuditLogStore AuditStore { get; }

    /// <summary>Gets the configurable browser adapter.</summary>
    public ConfigurableBrowserAdapter BrowserAdapter { get; }

    /// <summary>Gets the multi-profile browser session manager.</summary>
    public BrowserSessionManager SessionManager { get; }

    /// <summary>Gets the Comet GraphQL client.</summary>
    public CometClient CometClient { get; }

    /// <inheritdoc />
    public void Dispose() => this.SessionManager.Dispose();
}
