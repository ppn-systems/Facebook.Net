// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.UI.Core.Accounts;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Core.Configuration;

/// <summary>
/// Represents a discovered or managed Chromium browser profile folder on the local machine.
/// </summary>
/// <param name="DisplayName">Human-readable label for the profile.</param>
/// <param name="DirectoryPath">Full directory path on disk.</param>
/// <param name="IsManagedByApp">Whether the profile lives inside the application's isolated <c>Profiles</c> root.</param>
/// <param name="HasFacebookSession">Whether an authenticated Facebook session (<c>c_user</c> + <c>xs</c>) was detected in this profile's cookie store.</param>
public sealed record BrowserProfileOption(
    string DisplayName,
    string DirectoryPath,
    bool IsManagedByApp,
    bool HasFacebookSession = false);

/// <summary>
/// Telemetry snapshot explaining how <see cref="PreferredBrowserKind.AutoDetect"/> selected the best Chromium browser on the user's machine.
/// </summary>
/// <param name="BrowserKind">Resolved browser family (<see cref="PreferredBrowserKind.Edge"/>, <see cref="PreferredBrowserKind.Chrome"/>, or <see cref="PreferredBrowserKind.Brave"/>).</param>
/// <param name="ExecutablePath">Full path to the browser executable on disk.</param>
/// <param name="IsOsDefaultBrowser">Whether this browser is registered as the OS default HTTPS handler.</param>
/// <param name="IsCurrentlyRunning">Whether processes for this browser are currently running.</param>
/// <param name="ActiveUptime">How long the earliest running process of this browser has been open.</param>
/// <param name="RunningProcessCount">Number of running OS processes for this browser.</param>
/// <param name="LastProfileActivityUtc">Most recent write timestamp of the browser's user profile data.</param>
public sealed record SmartBrowserDetectionResult(
    PreferredBrowserKind BrowserKind,
    string? ExecutablePath,
    bool IsOsDefaultBrowser,
    bool IsCurrentlyRunning,
    TimeSpan ActiveUptime,
    int RunningProcessCount,
    DateTimeOffset LastProfileActivityUtc);

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
    /// When <see cref="PreferredBrowserKind.AutoDetect"/> is selected, resolves the executable of the smart-detected default/most-active browser.
    /// </summary>
    public string? ResolvePreferredBrowserExecutable()
    {
        if (!string.IsNullOrWhiteSpace(this.Current.CustomBrowserExecutablePath) &&
            File.Exists(this.Current.CustomBrowserExecutablePath))
        {
            return this.Current.CustomBrowserExecutablePath;
        }

        PreferredBrowserKind effectiveKind = this.ResolveEffectiveBrowserKind();
        return ResolveExecutableForKind(effectiveKind);
    }

    /// <summary>
    /// Evaluates installed Chromium browsers using OS Default Browser registration, currently running process uptime/window activity,
    /// and recent user profile write timestamps to select the user's true primary browser.
    /// </summary>
    public static SmartBrowserDetectionResult DetectBestBrowser()
    {
        PreferredBrowserKind? osDefault = TryGetOsDefaultBrowserKind();
        PreferredBrowserKind[] candidates =
        [
            PreferredBrowserKind.Edge,
            PreferredBrowserKind.Chrome,
            PreferredBrowserKind.Brave
        ];

        SmartBrowserDetectionResult? best = null;
        double bestScore = double.MinValue;
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        for (int i = 0; i < candidates.Length; i++)
        {
            PreferredBrowserKind kind = candidates[i];
            string? exePath = ResolveExecutableForKind(kind);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                continue;
            }

            bool isOsDefault = osDefault == kind;
            (int procCount, bool hasVisibleWindow, TimeSpan uptime) = GetBrowserProcessMetrics(kind);
            DateTimeOffset lastProfileWrite = GetLastProfileActivityUtc(kind);

            double score = 0.0;
            if (isOsDefault)
            {
                score += 55.0;
            }

            if (procCount > 0)
            {
                score += 25.0;
                if (hasVisibleWindow)
                {
                    score += 25.0;
                }

                // Reward the browser that has been running the longest (up to +45 points) and has active tabs/processes (up to +15 points)
                score += Math.Min(45.0, uptime.TotalMinutes / 8.0);
                score += Math.Min(15.0, procCount * 0.8);
            }

            if (lastProfileWrite > DateTimeOffset.MinValue)
            {
                double minutesSinceActive = Math.Max(0.0, (nowUtc - lastProfileWrite).TotalMinutes);
                if (minutesSinceActive <= 30.0)
                {
                    score += 45.0;
                }
                else if (minutesSinceActive <= 360.0)
                {
                    score += 35.0;
                }
                else if (minutesSinceActive <= 1440.0)
                {
                    score += 25.0;
                }
                else if (minutesSinceActive <= 10080.0)
                {
                    score += 12.0;
                }
            }

            SmartBrowserDetectionResult current = new(
                BrowserKind: kind,
                ExecutablePath: exePath,
                IsOsDefaultBrowser: isOsDefault,
                IsCurrentlyRunning: procCount > 0,
                ActiveUptime: uptime,
                RunningProcessCount: procCount,
                LastProfileActivityUtc: lastProfileWrite);

            if (best is null || score > bestScore)
            {
                best = current;
                bestScore = score;
            }
        }

        return best ?? new SmartBrowserDetectionResult(
            PreferredBrowserKind.Edge,
            ResolveExecutableForKind(PreferredBrowserKind.Edge),
            IsOsDefaultBrowser: false,
            IsCurrentlyRunning: false,
            ActiveUptime: TimeSpan.Zero,
            RunningProcessCount: 0,
            LastProfileActivityUtc: DateTimeOffset.MinValue);
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

        return DetectBestBrowser().BrowserKind;
    }

    /// <summary>
    /// Discovers all isolated browser profiles inside <paramref name="appProfilesRootDir"/> as well as
    /// existing local user profile folders strictly matching <paramref name="effectiveBrowserKind"/>,
    /// enriching system profiles with their real human-readable names from Chromium's <c>Local State</c>.
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
                        bool hasFb = AccountVaultDomainService.Instance.HasProfileAuthenticatedCookieStore(dir);
                        results.Add(new BrowserProfileOption($"One Client — {folderName}", dir, IsManagedByApp: true, HasFacebookSession: hasFb));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible directories
            }
        }

        (PreferredBrowserKind Kind, string BrowserLabel, string UserDataRoot)[] systemRoots = GetSystemUserDataRoots();

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

            Dictionary<string, string> localStateNames = ReadChromiumProfileFriendlyNames(rootPath);

            string defaultProfileDir = Path.Combine(rootPath, "Default");
            if (Directory.Exists(defaultProfileDir) && seenPaths.Add(defaultProfileDir))
            {
                bool hasFb = AccountVaultDomainService.Instance.HasProfileAuthenticatedCookieStore(defaultProfileDir);
                string friendlyLabel = FormatSystemProfileDisplayName(systemRoots[r].BrowserLabel, "Default", localStateNames);
                results.Add(new BrowserProfileOption(friendlyLabel, defaultProfileDir, IsManagedByApp: false, HasFacebookSession: hasFb));
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
                        bool hasFb = AccountVaultDomainService.Instance.HasProfileAuthenticatedCookieStore(pDir);
                        string friendlyLabel = FormatSystemProfileDisplayName(systemRoots[r].BrowserLabel, pName, localStateNames);
                        results.Add(new BrowserProfileOption(friendlyLabel, pDir, IsManagedByApp: false, HasFacebookSession: hasFb));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible system profile folders
            }
        }

        return results;
    }

    private static (PreferredBrowserKind Kind, string BrowserLabel, string UserDataRoot)[] GetSystemUserDataRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return
            [
                (PreferredBrowserKind.Edge, "Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
                (PreferredBrowserKind.Chrome, "Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
                (PreferredBrowserKind.Brave, "Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"))
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            string appSupport = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            return
            [
                (PreferredBrowserKind.Edge, "Microsoft Edge", Path.Combine(appSupport, "Microsoft Edge")),
                (PreferredBrowserKind.Chrome, "Google Chrome", Path.Combine(appSupport, "Google", "Chrome")),
                (PreferredBrowserKind.Brave, "Brave", Path.Combine(appSupport, "BraveSoftware", "Brave-Browser"))
            ];
        }

        return [];
    }

    private static string FormatSystemProfileDisplayName(
        string browserLabel,
        string folderKey,
        Dictionary<string, string> localStateNames)
    {
        if (localStateNames.TryGetValue(folderKey, out string? friendly) &&
            !string.IsNullOrWhiteSpace(friendly) &&
            !string.Equals(friendly, folderKey, StringComparison.OrdinalIgnoreCase))
        {
            return $"{browserLabel} — {friendly} · {folderKey}";
        }

        return $"{browserLabel} — {folderKey}";
    }

    private static Dictionary<string, string> ReadChromiumProfileFriendlyNames(string userDataRoot)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        string localStatePath = Path.Combine(userDataRoot, "Local State");
        if (!File.Exists(localStatePath))
        {
            return map;
        }

        try
        {
            using FileStream fs = new(localStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("profile", out JsonElement profileEl) ||
                !profileEl.TryGetProperty("info_cache", out JsonElement cacheEl) ||
                cacheEl.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            foreach (JsonProperty prop in cacheEl.EnumerateObject())
            {
                JsonElement val = prop.Value;
                if (val.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? shortcutName = val.TryGetProperty("shortcut_name", out JsonElement snEl) ? snEl.GetString() : null;
                string? name = val.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
                string? userName = val.TryGetProperty("user_name", out JsonElement unEl) ? unEl.GetString() : null;

                string chosen = !string.IsNullOrWhiteSpace(shortcutName) &&
                                !string.Equals(shortcutName, "Person 1", StringComparison.OrdinalIgnoreCase)
                    ? shortcutName.Trim()
                    : (!string.IsNullOrWhiteSpace(name) ? name.Trim() : string.Empty);

                // Strip any parentheses to keep UI labels clean
                chosen = chosen.Replace("(", string.Empty, StringComparison.Ordinal).Replace(")", string.Empty, StringComparison.Ordinal).Trim();

                if (!string.IsNullOrWhiteSpace(userName))
                {
                    string cleanUser = userName.Trim().Replace("(", string.Empty, StringComparison.Ordinal).Replace(")", string.Empty, StringComparison.Ordinal);
                    chosen = !string.IsNullOrWhiteSpace(chosen) &&
                             !string.Equals(chosen, "Person 1", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(chosen, cleanUser, StringComparison.OrdinalIgnoreCase)
                        ? $"{chosen} · {cleanUser}"
                        : cleanUser;
                }

                if (!string.IsNullOrWhiteSpace(chosen))
                {
                    map[prop.Name] = chosen;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Ignore unreadable Local State
        }

        return map;
    }

    private static PreferredBrowserKind? TryGetOsDefaultBrowserKind()
    {
        if (OperatingSystem.IsWindows())
        {
            return TryGetWindowsDefaultBrowserKind();
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static PreferredBrowserKind? TryGetWindowsDefaultBrowserKind()
    {
        string[] subKeys =
        [
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice",
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"
        ];

        for (int i = 0; i < subKeys.Length; i++)
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeys[i]);
                if (key?.GetValue("ProgId") is string progId && !string.IsNullOrWhiteSpace(progId))
                {
                    if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase))
                    {
                        return PreferredBrowserKind.Edge;
                    }

                    if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                    {
                        return PreferredBrowserKind.Chrome;
                    }

                    if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase))
                    {
                        return PreferredBrowserKind.Brave;
                    }
                }
            }
            catch
            {
                // Ignore registry access error
            }
        }

        return null;
    }

    private static (int ProcessCount, bool HasVisibleWindow, TimeSpan LongestUptime) GetBrowserProcessMetrics(PreferredBrowserKind kind)
    {
        string processName = kind switch
        {
            PreferredBrowserKind.Edge => "msedge",
            PreferredBrowserKind.Chrome => "chrome",
            PreferredBrowserKind.Brave => "brave",
            PreferredBrowserKind.AutoDetect or _ => string.Empty
        };

        if (string.IsNullOrEmpty(processName))
        {
            return (0, false, TimeSpan.Zero);
        }

        Process[]? processes = null;
        try
        {
            processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                return (0, false, TimeSpan.Zero);
            }

            bool hasWindow = false;
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            TimeSpan maxUptime = TimeSpan.Zero;

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        hasWindow = true;
                    }

                    DateTime startUtc = p.StartTime.ToUniversalTime();
                    TimeSpan uptime = nowUtc - startUtc;
                    if (uptime > maxUptime && uptime < TimeSpan.FromDays(60))
                    {
                        maxUptime = uptime;
                    }
                }
                catch
                {
                    // Ignore protected or exiting process handle
                }
            }

            return (processes.Length, hasWindow, maxUptime);
        }
        catch
        {
            return (0, false, TimeSpan.Zero);
        }
        finally
        {
            if (processes is not null)
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    processes[i].Dispose();
                }
            }
        }
    }

    private static DateTimeOffset GetLastProfileActivityUtc(PreferredBrowserKind kind)
    {
        (PreferredBrowserKind Kind, string _, string UserDataRoot)[] roots = GetSystemUserDataRoots();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].Kind != kind)
            {
                continue;
            }

            string root = roots[i].UserDataRoot;
            if (!Directory.Exists(root))
            {
                return DateTimeOffset.MinValue;
            }

            string[] candidateFiles =
            [
                Path.Combine(root, "Local State"),
                Path.Combine(root, "Default", "Preferences"),
                Path.Combine(root, "Default", "Network", "Cookies")
            ];

            DateTimeOffset latest = DateTimeOffset.MinValue;
            for (int f = 0; f < candidateFiles.Length; f++)
            {
                try
                {
                    if (File.Exists(candidateFiles[f]))
                    {
                        DateTimeOffset writeUtc = File.GetLastWriteTimeUtc(candidateFiles[f]);
                        if (writeUtc > latest)
                        {
                            latest = writeUtc;
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible file
                }
            }

            return latest;
        }

        return DateTimeOffset.MinValue;
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
