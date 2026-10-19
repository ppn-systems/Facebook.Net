// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Core.Configuration;

/// <summary>
/// Preferred Chromium browser family for launching isolated account sessions.
/// </summary>
public enum PreferredBrowserKind
{
    /// <summary>Automatically detect installed browser (Edge, Chrome, Brave).</summary>
    AutoDetect = 0,

    /// <summary>Microsoft Edge.</summary>
    Edge = 1,

    /// <summary>Google Chrome.</summary>
    Chrome = 2,

    /// <summary>Brave Browser.</summary>
    Brave = 3
}

/// <summary>
/// Strongly-typed application configuration persisted to <c>appsettings.json</c>.
/// </summary>
public sealed record AppConfig(
    AppThemeMode Theme = AppThemeMode.Dark,
    AppLanguage Language = AppLanguage.ViVn,
    PreferredBrowserKind PreferredBrowser = PreferredBrowserKind.AutoDetect,
    string? CustomBrowserExecutablePath = null,
    bool RunBrowserHeadless = false,
    int MinDelaySeconds = 15,
    int MaxDelaySeconds = 45,
    bool IsAuditDrawerExpanded = false);

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for Native AOT serialization of <see cref="AppConfig"/>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
public partial class UiCoreJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Loads and saves <see cref="AppConfig"/> and resolves the user's preferred Chromium executable path.
/// </summary>
public sealed class AppSettingsService
{
    private readonly string _settingsFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettingsService"/> class.
    /// </summary>
    /// <param name="settingsFilePath">Path to <c>appsettings.json</c>.</param>
    public AppSettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        this._settingsFilePath = settingsFilePath;
        this.Current = this.LoadOrDefault();
    }

    /// <summary>
    /// Gets the current active configuration.
    /// </summary>
    public AppConfig Current { get; private set; }

    /// <summary>
    /// Saves the updated <see cref="AppConfig"/> to disk.
    /// </summary>
    public void Save(AppConfig updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        this.Current = updated;

        string? dir = Path.GetDirectoryName(this._settingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(updated, UiCoreJsonContext.Default.AppConfig);
        File.WriteAllText(this._settingsFilePath, json);
    }

    /// <summary>
    /// Resolves the browser executable path based on the user's <see cref="PreferredBrowserKind"/> or custom path.
    /// Returns <see langword="null"/> to fall back to the default OS adapter discovery.
    /// </summary>
    public string? ResolvePreferredBrowserExecutable()
    {
        if (!string.IsNullOrWhiteSpace(this.Current.CustomBrowserExecutablePath) &&
            File.Exists(this.Current.CustomBrowserExecutablePath))
        {
            return this.Current.CustomBrowserExecutablePath;
        }

        if (OperatingSystem.IsWindows())
        {
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            switch (this.Current.PreferredBrowser)
            {
                case PreferredBrowserKind.Edge:
                    return FirstExisting(
                        Path.Combine(progFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                        Path.Combine(progFiles, "Microsoft", "Edge", "Application", "msedge.exe"));

                case PreferredBrowserKind.Chrome:
                    return FirstExisting(
                        Path.Combine(progFiles, "Google", "Chrome", "Application", "chrome.exe"),
                        Path.Combine(progFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                        Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"));

                case PreferredBrowserKind.Brave:
                    return FirstExisting(
                        Path.Combine(progFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                        Path.Combine(progFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                        Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));

                case PreferredBrowserKind.AutoDetect:
                default:
                    break;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            switch (this.Current.PreferredBrowser)
            {
                case PreferredBrowserKind.Edge:
                    return FirstExisting("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
                case PreferredBrowserKind.Chrome:
                    return FirstExisting("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
                case PreferredBrowserKind.Brave:
                    return FirstExisting("/Applications/Brave Browser.app/Contents/MacOS/Brave Browser");
                case PreferredBrowserKind.AutoDetect:
                default:
                    break;
            }
        }

        return null;
    }

    private AppConfig LoadOrDefault()
    {
        try
        {
            if (File.Exists(this._settingsFilePath))
            {
                string json = File.ReadAllText(this._settingsFilePath);
                AppConfig? loaded = JsonSerializer.Deserialize(json, UiCoreJsonContext.Default.AppConfig);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to default config on corrupt file
        }

        return new AppConfig();
    }

    private static string? FirstExisting(params string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(paths[i]) && File.Exists(paths[i]))
            {
                return paths[i];
            }
        }

        return null;
    }
}
