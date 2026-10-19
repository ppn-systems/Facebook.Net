// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
    bool IsAuditDrawerExpanded = false)
{
    /// <summary>
    /// Returns a normalized copy of this configuration ensuring valid delay bounds (<c>1 &lt;= MinDelaySeconds &lt;= MaxDelaySeconds &lt;= 3600</c>).
    /// </summary>
    public AppConfig Normalize()
    {
        int clampedMin = Math.Clamp(this.MinDelaySeconds, 1, 3600);
        int clampedMax = Math.Clamp(this.MaxDelaySeconds, clampedMin, 3600);
        string? cleanedPath = string.IsNullOrWhiteSpace(this.CustomBrowserExecutablePath)
            ? null
            : this.CustomBrowserExecutablePath.Trim();

        return this with
        {
            MinDelaySeconds = clampedMin,
            MaxDelaySeconds = clampedMax,
            CustomBrowserExecutablePath = cleanedPath
        };
    }
}

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
/// Loads and saves <see cref="AppConfig"/> atomically and resolves the user's preferred Chromium executable path.
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
    /// Synchronously saves the updated <see cref="AppConfig"/> to disk using an atomic temporary file swap.
    /// </summary>
    public void Save(AppConfig updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        AppConfig normalized = updated.Normalize();
        this.Current = normalized;

        string? dir = Path.GetDirectoryName(this._settingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(normalized, UiCoreJsonContext.Default.AppConfig);
        string tempPath = $"{this._settingsFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, this._settingsFilePath, overwrite: true);
    }

    /// <summary>
    /// Asynchronously saves the updated <see cref="AppConfig"/> to disk using an atomic temporary file swap without blocking the UI thread.
    /// </summary>
    public async Task SaveAsync(AppConfig updated, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        AppConfig normalized = updated.Normalize();
        this.Current = normalized;

        string? dir = Path.GetDirectoryName(this._settingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(normalized, UiCoreJsonContext.Default.AppConfig);
        string tempPath = $"{this._settingsFilePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(tempPath, this._settingsFilePath, overwrite: true);
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
                        Path.Combine(progFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                        Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"));

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
            string userApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
            switch (this.Current.PreferredBrowser)
            {
                case PreferredBrowserKind.Edge:
                    return FirstExisting(
                        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                        Path.Combine(userApps, "Microsoft Edge.app/Contents/MacOS/Microsoft Edge"));
                case PreferredBrowserKind.Chrome:
                    return FirstExisting(
                        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                        Path.Combine(userApps, "Google Chrome.app/Contents/MacOS/Google Chrome"));
                case PreferredBrowserKind.Brave:
                    return FirstExisting(
                        "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
                        Path.Combine(userApps, "Brave Browser.app/Contents/MacOS/Brave Browser"));
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
                    return loaded.Normalize();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall back to default normalized config when file is unreadable or contains invalid JSON
        }

        return new AppConfig().Normalize();
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
