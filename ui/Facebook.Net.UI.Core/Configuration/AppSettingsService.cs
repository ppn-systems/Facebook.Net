// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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
/// Represents a discovered or managed Chromium browser profile folder on the local machine.
/// </summary>
/// <param name="DisplayName">Human-readable label for the profile.</param>
/// <param name="DirectoryPath">Full directory path on disk.</param>
/// <param name="IsManagedByApp">Whether the profile lives inside the application's isolated <c>Profiles</c> root.</param>
public sealed record BrowserProfileOption(
    string DisplayName,
    string DirectoryPath,
    bool IsManagedByApp);

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
    AppThemeMode Theme = AppThemeMode.System,
    AppLanguage Language = AppLanguage.ViVn,
    PreferredBrowserKind PreferredBrowser = PreferredBrowserKind.AutoDetect,
    string? CustomBrowserExecutablePath = null,
    bool RunBrowserHeadless = false,
    int MinDelaySeconds = 2,
    int MaxDelaySeconds = 5,
    bool IsAuditDrawerExpanded = false,
    bool IsSidebarCollapsed = false,
    string? LastSelectedAccountUid = null,
    string ScrapeFilterKeywords = "",
    int ScrapePostsPerGroup = 15,
    bool AutoBackgroundScrapeEnabled = false,
    int AutoScrapeIntervalMinutes = 5,
    bool ScrapeAllEnabledTargetGroups = true,
    int ScrapeDelayMinSeconds = 1,
    int ScrapeDelayMaxSeconds = 2,
    int ScrapeMaxGroupsPerCycle = 30,
    string AiSystemPrompt = "",
    string AiModel = AppConfig.DefaultAiModel,
    string AiReasoningEffort = AppConfig.DefaultAiReasoningEffort)
{
    /// <summary>
    /// Built-in immutable English system prompt always pre-loaded before every AI Assistant conversation.
    /// </summary>
    public const string DefaultAiSystemPrompt =
        "You are the built-in AI Marketing & Automation Copilot inside Facebook One Client. " +
        "Always reply in the exact language used by the user (defaulting to natural, professional Vietnamese when the user writes in Vietnamese). " +
        "Be sharp, proactive, and concise — never apologize for delays or explain internal tool mechanics. " +
        "CRITICAL PERFORMANCE & INTELLIGENCE RULES:\n" +
        "1. ZERO REDUNDANT TOOL CALLS: The current workspace snapshot (active Facebook account, enabled target groups count, group color tags, saved Spintax templates, anti-checkpoint delay settings, and all AI Context Memory entries) is ALREADY pre-loaded below under [CURRENT WORKSPACE STATE] and [AI CONTEXT MEMORY]. Do NOT call read-only MCP tools (`fb_memory_search`, `fb_list_templates`, `fb_list_target_groups`, `fb_list_accounts`, `fb_memory_get_context`) unless the user explicitly asks to query data not shown in the snapshot.\n" +
        "2. PROACTIVE CAMPAIGN DRAFTING: When the user asks to publish to groups without providing post text (e.g. 'Đăng bài vào nhóm'), immediately look at [AI CONTEXT MEMORY] and [SAVED TEMPLATES] in the snapshot. If templates or product info exist, proactively propose 1 ready-to-use Spintax post draft right away AND emit the `[ACTION:PUBLISH_GROUPS|...]` tag so they can click 1 button to publish, or ask if they want to customize it. Only ask for product details if both memory and templates are completely empty.\n" +
        "3. ACTION TAG PROTOCOL: Only append `[ACTION:PUBLISH_GROUPS|count=<int>|minDelay=<sec>|maxDelay=<sec>|content=<full_post_text>]` when you have a concrete post text (`content` must NOT be empty). Use `[ACTION:SEARCH_GROUPS|keyword=<text>]` when proposing group discovery, and `[ACTION:SCAN_LEADS|keyword=<text>]` when proposing lead scanning.\n" +
        "4. AUTOMATIC MEMORY LEARNING: Only call `fb_memory_save` when the user shares NEW product details, pricing, brand voice, or campaign rules.";

    /// <summary>
    /// Default lightweight, fast AI model ID for <c>agy --model</c>.
    /// </summary>
    public const string DefaultAiModel = "gemini-3.8-flash-low";

    /// <summary>
    /// Default lowest reasoning effort level for <c>agy --effort</c>.
    /// </summary>
    public const string DefaultAiReasoningEffort = "low";

    /// <summary>
    /// Returns a normalized copy of this configuration ensuring valid delay bounds (<c>0 &lt;= MinDelaySeconds &lt;= MaxDelaySeconds &lt;= 3600</c>).
    /// </summary>
    public AppConfig Normalize()
    {
        int clampedMin = Math.Clamp(this.MinDelaySeconds, 0, 3600);
        int clampedMax = Math.Clamp(this.MaxDelaySeconds, clampedMin, 3600);
        int clampedPerGroup = Math.Clamp(this.ScrapePostsPerGroup <= 0 ? 15 : this.ScrapePostsPerGroup, 5, 100);
        int clampedInterval = Math.Clamp(this.AutoScrapeIntervalMinutes <= 0 ? 5 : this.AutoScrapeIntervalMinutes, 1, 120);
        int clampedScrapeMin = Math.Clamp(this.ScrapeDelayMinSeconds, 0, 600);
        int clampedScrapeMax = Math.Clamp(this.ScrapeDelayMaxSeconds, clampedScrapeMin, 600);
        int clampedMaxGroups = Math.Clamp(this.ScrapeMaxGroupsPerCycle <= 0 ? 30 : this.ScrapeMaxGroupsPerCycle, 1, 500);
        string? cleanedPath = string.IsNullOrWhiteSpace(this.CustomBrowserExecutablePath)
            ? null
            : this.CustomBrowserExecutablePath.Trim();
        string? cleanedUid = string.IsNullOrWhiteSpace(this.LastSelectedAccountUid)
            ? null
            : this.LastSelectedAccountUid.Trim();
        string cleanedCustomContext = string.IsNullOrWhiteSpace(this.AiSystemPrompt) ||
                                      string.Equals(this.AiSystemPrompt.Trim(), DefaultAiSystemPrompt, StringComparison.Ordinal)
            ? string.Empty
            : this.AiSystemPrompt.Trim();
        string cleanedModel = string.IsNullOrWhiteSpace(this.AiModel)
            ? DefaultAiModel
            : this.AiModel.Trim();
        string cleanedEffort = string.IsNullOrWhiteSpace(this.AiReasoningEffort)
            ? DefaultAiReasoningEffort
            : this.AiReasoningEffort.Trim().ToLowerInvariant();

        return this with
        {
            MinDelaySeconds = clampedMin,
            MaxDelaySeconds = clampedMax,
            CustomBrowserExecutablePath = cleanedPath,
            LastSelectedAccountUid = cleanedUid,
            ScrapeFilterKeywords = this.ScrapeFilterKeywords?.Trim() ?? string.Empty,
            ScrapePostsPerGroup = clampedPerGroup,
            AutoBackgroundScrapeEnabled = this.AutoBackgroundScrapeEnabled,
            AutoScrapeIntervalMinutes = clampedInterval,
            ScrapeAllEnabledTargetGroups = this.ScrapeAllEnabledTargetGroups,
            ScrapeDelayMinSeconds = clampedScrapeMin,
            ScrapeDelayMaxSeconds = clampedScrapeMax,
            ScrapeMaxGroupsPerCycle = clampedMaxGroups,
            AiSystemPrompt = cleanedCustomContext,
            AiModel = cleanedModel,
            AiReasoningEffort = cleanedEffort
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
/// Contract for loading, normalizing, and persisting <see cref="AppConfig"/> as well as resolving
/// installed Chromium browser engines and profiles.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>Gets the current active configuration.</summary>
    AppConfig Current { get; }

    /// <summary>Synchronously saves the updated <see cref="AppConfig"/> to disk using an atomic temporary file swap.</summary>
    void Save(AppConfig updated);

    /// <summary>Asynchronously saves the updated <see cref="AppConfig"/> to disk using an atomic temporary file swap.</summary>
    Task SaveAsync(AppConfig updated, CancellationToken ct = default);

    /// <summary>Resolves the browser executable path based on the user's <see cref="PreferredBrowserKind"/> or custom path.</summary>
    string? ResolvePreferredBrowserExecutable();

    /// <summary>Scans the machine for installed Chromium browsers.</summary>
    List<PreferredBrowserKind> GetInstalledBrowsers();

    /// <summary>Determines the effective <see cref="PreferredBrowserKind"/> currently in use.</summary>
    PreferredBrowserKind ResolveEffectiveBrowserKind();
}

/// <summary>
/// Loads and saves <see cref="AppConfig"/> atomically and resolves the user's preferred Chromium executable path.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string _settingsFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettingsService"/> class.
    /// </summary>
    /// <param name="settingsFilePath">Path to <c>appsettings.json</c>.</param>
    public AppSettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = settingsFilePath;
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

        string? dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(normalized, UiCoreJsonContext.Default.AppConfig);
        string tempPath = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    /// <summary>
    /// Asynchronously saves the updated <see cref="AppConfig"/> to disk using an atomic temporary file swap without blocking the UI thread.
    /// </summary>
    public async Task SaveAsync(AppConfig updated, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        AppConfig normalized = updated.Normalize();
        this.Current = normalized;

        string? dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(normalized, UiCoreJsonContext.Default.AppConfig);
        string tempPath = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
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

        return ResolveExecutableForKind(this.Current.PreferredBrowser);
    }

    /// <summary>
    /// Resolves the executable path on disk for a specific <see cref="PreferredBrowserKind"/>, or <see langword="null"/> if not installed.
    /// </summary>
    public static string? ResolveExecutableForKind(PreferredBrowserKind kind)
    {
        if (OperatingSystem.IsWindows())
        {
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            switch (kind)
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
            switch (kind)
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
        else if (OperatingSystem.IsLinux())
        {
            switch (kind)
            {
                case PreferredBrowserKind.Edge:
                    return FirstExisting("/usr/bin/microsoft-edge", "/usr/bin/microsoft-edge-stable");
                case PreferredBrowserKind.Chrome:
                    return FirstExisting("/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/usr/bin/chromium", "/usr/bin/chromium-browser");
                case PreferredBrowserKind.Brave:
                    return FirstExisting("/usr/bin/brave-browser", "/usr/bin/brave");
                case PreferredBrowserKind.AutoDetect:
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Scans the machine for installed Chromium browsers and returns <see cref="PreferredBrowserKind.AutoDetect"/>
    /// followed only by the browsers actually installed on disk (plus the currently configured kind if set).
    /// </summary>
    public List<PreferredBrowserKind> GetInstalledBrowsers()
    {
        List<PreferredBrowserKind> installed = [PreferredBrowserKind.AutoDetect];
        PreferredBrowserKind[] candidates =
        [
            PreferredBrowserKind.Edge,
            PreferredBrowserKind.Chrome,
            PreferredBrowserKind.Brave
        ];

        for (int i = 0; i < candidates.Length; i++)
        {
            PreferredBrowserKind candidate = candidates[i];
            if (!string.IsNullOrWhiteSpace(ResolveExecutableForKind(candidate)))
            {
                installed.Add(candidate);
            }
        }

        if (!installed.Contains(this.Current.PreferredBrowser))
        {
            installed.Add(this.Current.PreferredBrowser);
        }

        return installed;
    }

    /// <summary>
    /// Determines the effective <see cref="PreferredBrowserKind"/> currently in use (resolving <see cref="PreferredBrowserKind.AutoDetect"/>
    /// or custom executable paths to the concrete browser family: <see cref="PreferredBrowserKind.Edge"/>, <see cref="PreferredBrowserKind.Chrome"/>, or <see cref="PreferredBrowserKind.Brave"/>).
    /// </summary>
    public PreferredBrowserKind ResolveEffectiveBrowserKind()
    {
        if (!string.IsNullOrWhiteSpace(this.Current.CustomBrowserExecutablePath))
        {
            string exeName = Path.GetFileName(this.Current.CustomBrowserExecutablePath);
            if (exeName.Contains("brave", StringComparison.OrdinalIgnoreCase))
            {
                return PreferredBrowserKind.Brave;
            }

            if (exeName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
            {
                return PreferredBrowserKind.Chrome;
            }

            if (exeName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
                exeName.Contains("edge", StringComparison.OrdinalIgnoreCase))
            {
                return PreferredBrowserKind.Edge;
            }
        }

        if (this.Current.PreferredBrowser != PreferredBrowserKind.AutoDetect)
        {
            return this.Current.PreferredBrowser;
        }

        if (!string.IsNullOrWhiteSpace(ResolveExecutableForKind(PreferredBrowserKind.Edge)))
        {
            return PreferredBrowserKind.Edge;
        }

        if (!string.IsNullOrWhiteSpace(ResolveExecutableForKind(PreferredBrowserKind.Chrome)))
        {
            return PreferredBrowserKind.Chrome;
        }

        if (!string.IsNullOrWhiteSpace(ResolveExecutableForKind(PreferredBrowserKind.Brave)))
        {
            return PreferredBrowserKind.Brave;
        }

        return PreferredBrowserKind.Edge;
    }

    /// <summary>
    /// Discovers all isolated browser profiles inside <paramref name="appProfilesRootDir"/> as well as
    /// existing local user profile folders strictly matching <paramref name="effectiveBrowserKind"/>.
    /// </summary>
    public static List<BrowserProfileOption> DiscoverBrowserProfiles(
        string appProfilesRootDir,
        PreferredBrowserKind effectiveBrowserKind = PreferredBrowserKind.AutoDetect)
    {
        List<BrowserProfileOption> results = new();
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(appProfilesRootDir) && Directory.Exists(appProfilesRootDir))
        {
            try
            {
                string[] appDirs = Directory.GetDirectories(appProfilesRootDir);
                Array.Sort(appDirs, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < appDirs.Length; i++)
                {
                    string dir = appDirs[i];
                    string folderName = Path.GetFileName(dir);
                    if (!string.IsNullOrWhiteSpace(folderName) && seenPaths.Add(dir))
                    {
                        results.Add(new BrowserProfileOption($"One Client — {folderName}", dir, IsManagedByApp: true));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible directories
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            (PreferredBrowserKind Kind, string BrowserLabel, string UserDataRoot)[] systemRoots =
            [
                (PreferredBrowserKind.Edge, "Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
                (PreferredBrowserKind.Chrome, "Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
                (PreferredBrowserKind.Brave, "Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"))
            ];

            for (int r = 0; r < systemRoots.Length; r++)
            {
                if (effectiveBrowserKind != PreferredBrowserKind.AutoDetect &&
                    systemRoots[r].Kind != effectiveBrowserKind)
                {
                    continue;
                }

                string rootPath = systemRoots[r].UserDataRoot;
                if (!Directory.Exists(rootPath))
                {
                    continue;
                }

                string defaultProfileDir = Path.Combine(rootPath, "Default");
                if (Directory.Exists(defaultProfileDir) && seenPaths.Add(defaultProfileDir))
                {
                    results.Add(new BrowserProfileOption($"{systemRoots[r].BrowserLabel} — Default", defaultProfileDir, IsManagedByApp: false));
                }

                try
                {
                    string[] profileDirs = Directory.GetDirectories(rootPath, "Profile *");
                    Array.Sort(profileDirs, StringComparer.OrdinalIgnoreCase);
                    for (int p = 0; p < profileDirs.Length; p++)
                    {
                        string pDir = profileDirs[p];
                        string pName = Path.GetFileName(pDir);
                        if (!string.IsNullOrWhiteSpace(pName) && seenPaths.Add(pDir))
                        {
                            results.Add(new BrowserProfileOption($"{systemRoots[r].BrowserLabel} — {pName}", pDir, IsManagedByApp: false));
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore inaccessible system profile folders
                }
            }
        }

        return results;
    }

    private AppConfig LoadOrDefault()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                AppConfig? loaded = JsonSerializer.Deserialize(json, UiCoreJsonContext.Default.AppConfig);
                if (loaded is not null)
                {
                    if (loaded.MinDelaySeconds == 15 && loaded.MaxDelaySeconds == 45)
                    {
                        loaded = loaded with { MinDelaySeconds = 2, MaxDelaySeconds = 5 };
                    }

                    if (loaded.ScrapeDelayMinSeconds == 8 && loaded.ScrapeDelayMaxSeconds == 18)
                    {
                        loaded = loaded with { ScrapeDelayMinSeconds = 1, ScrapeDelayMaxSeconds = 2 };
                    }

                    if (loaded.ScrapeMaxGroupsPerCycle == 5)
                    {
                        loaded = loaded with { ScrapeMaxGroupsPerCycle = 30 };
                    }

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
