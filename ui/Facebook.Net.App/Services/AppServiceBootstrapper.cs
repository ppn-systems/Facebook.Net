// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using Facebook.Net.Browser.MacOS;
using Facebook.Net.Browser.Session;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Client;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Storage;

namespace Facebook.Net.App.Services;

/// <summary>
/// Provides Native AOT-safe dependency resolution and OS-aware service wiring for the desktop application.
/// </summary>
public sealed class AppServiceBootstrapper : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppServiceBootstrapper"/> class.
    /// </summary>
    /// <param name="customDatabasePath">Optional custom SQLite database file path.</param>
    public AppServiceBootstrapper(string? customDatabasePath = null)
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Facebook.Net");
        _ = Directory.CreateDirectory(appDataDir);

        this.DataDirectory = appDataDir;
        this.ProfilesRootDirectory = Path.Combine(appDataDir, "Profiles");
        _ = Directory.CreateDirectory(this.ProfilesRootDirectory);

        string dbPath = string.IsNullOrWhiteSpace(customDatabasePath)
            ? Path.Combine(appDataDir, "facebook_net.db")
            : customDatabasePath;

        this.AccountStore = new SqliteAccountStore(dbPath);
        this.BrowserAdapter = CreatePlatformBrowserAdapter();
        this.SessionManager = new BrowserSessionManager(this.BrowserAdapter);
        this.CometClient = new CometClient();
    }

    /// <summary>
    /// Gets the root application data directory.
    /// </summary>
    public string DataDirectory { get; }

    /// <summary>
    /// Gets the root directory where isolated Chromium user profiles are stored.
    /// </summary>
    public string ProfilesRootDirectory { get; }

    /// <summary>
    /// Gets the SQLite account and proxy store.
    /// </summary>
    public SqliteAccountStore AccountStore { get; }

    /// <summary>
    /// Gets the OS-specific Chromium browser adapter (<see cref="WindowsBrowserAdapter"/> or <see cref="MacOsBrowserAdapter"/>).
    /// </summary>
    public IBrowserAdapter BrowserAdapter { get; }

    /// <summary>
    /// Gets the multi-profile browser session manager.
    /// </summary>
    public BrowserSessionManager SessionManager { get; }

    /// <summary>
    /// Gets the Facebook Comet GraphQL client.
    /// </summary>
    public CometClient CometClient { get; }

    /// <summary>
    /// Resolves the appropriate <see cref="IBrowserAdapter"/> for the current operating system.
    /// </summary>
    /// <returns>An instance of <see cref="IBrowserAdapter"/>.</returns>
    public static IBrowserAdapter CreatePlatformBrowserAdapter() =>
        OperatingSystem.IsMacOS()
            ? new MacOsBrowserAdapter()
            : new WindowsBrowserAdapter();

    /// <inheritdoc />
    public void Dispose() => this.SessionManager.Dispose();
}
