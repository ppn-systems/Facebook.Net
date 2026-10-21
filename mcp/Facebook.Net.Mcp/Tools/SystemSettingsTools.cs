// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Application Settings, Anti-Checkpoint Delay Configuration, Background Auto-Scrape Settings,
/// Storage Metrics, Chromium Cache Cleanup, Audit Log Management, and GitHub Release Update Checks.
/// </summary>
[McpServerToolType]
public sealed class SystemSettingsTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_get_app_settings")]
    [Description("Get the current application settings from appsettings.json, including anti-checkpoint campaign delays, group scraping delays, background auto-scrape schedule, browser engine preference, headless mode, theme, language, and AI model settings.")]
    public async Task<string> GetAppSettingsAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        AppConfig cfg = _manager.Settings.Current;

        return JsonSerializer.Serialize(new
        {
            theme = cfg.Theme.ToString(),
            language = cfg.Language.ToString(),
            preferred_browser = cfg.PreferredBrowser.ToString(),
            effective_browser = _manager.Settings.ResolveEffectiveBrowserKind().ToString(),
            custom_browser_executable_path = cfg.CustomBrowserExecutablePath,
            run_browser_headless = cfg.RunBrowserHeadless,
            min_delay_seconds = cfg.MinDelaySeconds,
            max_delay_seconds = cfg.MaxDelaySeconds,
            scrape_delay_min_seconds = cfg.ScrapeDelayMinSeconds,
            scrape_delay_max_seconds = cfg.ScrapeDelayMaxSeconds,
            scrape_posts_per_group = cfg.ScrapePostsPerGroup,
            scrape_max_groups_per_cycle = cfg.ScrapeMaxGroupsPerCycle,
            auto_background_scrape_enabled = cfg.AutoBackgroundScrapeEnabled,
            auto_scrape_interval_minutes = cfg.AutoScrapeIntervalMinutes,
            scrape_all_enabled_target_groups = cfg.ScrapeAllEnabledTargetGroups,
            scrape_filter_keywords = cfg.ScrapeFilterKeywords,
            ai_model = cfg.AiModel,
            ai_reasoning_effort = cfg.AiReasoningEffort
        });
    }

    [McpServerTool(Name = "fb_update_app_settings")]
    [Description("Update application configuration settings in appsettings.json (anti-checkpoint publishing delays, scraping delays, posts per group, background auto-scrape toggle/interval, filter keywords, browser headless mode, preferred browser, theme, or language).")]
    public async Task<string> UpdateAppSettingsAsync(
        [Description("Optional minimum anti-checkpoint delay in seconds between campaign posts/comments (0..3600)")] double? minDelaySeconds = null,
        [Description("Optional maximum anti-checkpoint delay in seconds between campaign posts/comments (0..3600)")] double? maxDelaySeconds = null,
        [Description("Optional minimum delay in seconds between group feed scrapes (0..600)")] double? scrapeDelayMinSeconds = null,
        [Description("Optional maximum delay in seconds between group feed scrapes (0..600)")] double? scrapeDelayMaxSeconds = null,
        [Description("Optional number of posts to scrape per group (5..100)")] int? scrapePostsPerGroup = null,
        [Description("Optional maximum number of groups to scrape per cycle (1..500)")] int? scrapeMaxGroupsPerCycle = null,
        [Description("Optional toggle for automatic background group feed scraping")] bool? autoBackgroundScrapeEnabled = null,
        [Description("Optional interval in minutes for automatic background scraping (1..120)")] int? autoScrapeIntervalMinutes = null,
        [Description("Optional comma-separated buyer filter keywords for group scraping")] string? scrapeFilterKeywords = null,
        [Description("Optional toggle to run automated Chromium browser sessions in headless mode")] bool? runBrowserHeadless = null,
        [Description("Optional preferred browser engine: 'AutoDetect', 'Edge', 'Chrome', or 'Brave'")] string? preferredBrowser = null,
        [Description("Optional UI theme mode: 'System', 'Light', or 'Dark'")] string? theme = null,
        [Description("Optional UI language: 'ViVn' or 'EnUs'")] string? language = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        AppConfig current = _manager.Settings.Current;

        PreferredBrowserKind resolvedBrowser = current.PreferredBrowser;
        if (!string.IsNullOrWhiteSpace(preferredBrowser) &&
            Enum.TryParse<PreferredBrowserKind>(preferredBrowser.Trim(), ignoreCase: true, out PreferredBrowserKind parsedBrowser))
        {
            resolvedBrowser = parsedBrowser;
        }

        AppThemeMode resolvedTheme = current.Theme;
        if (!string.IsNullOrWhiteSpace(theme) &&
            Enum.TryParse<AppThemeMode>(theme.Trim(), ignoreCase: true, out AppThemeMode parsedTheme))
        {
            resolvedTheme = parsedTheme;
        }

        AppLanguage resolvedLang = current.Language;
        if (!string.IsNullOrWhiteSpace(language))
        {
            string cleanLang = language.Trim();
            if (string.Equals(cleanLang, "vi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanLang, "vi-VN", StringComparison.OrdinalIgnoreCase))
            {
                resolvedLang = AppLanguage.ViVn;
            }
            else if (string.Equals(cleanLang, "en", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(cleanLang, "en-US", StringComparison.OrdinalIgnoreCase))
            {
                resolvedLang = AppLanguage.EnUs;
            }
            else if (Enum.TryParse<AppLanguage>(cleanLang, ignoreCase: true, out AppLanguage parsedLang))
            {
                resolvedLang = parsedLang;
            }
        }

        AppConfig updated = (current with
        {
            MinDelaySeconds = minDelaySeconds ?? current.MinDelaySeconds,
            MaxDelaySeconds = maxDelaySeconds ?? current.MaxDelaySeconds,
            ScrapeDelayMinSeconds = scrapeDelayMinSeconds ?? current.ScrapeDelayMinSeconds,
            ScrapeDelayMaxSeconds = scrapeDelayMaxSeconds ?? current.ScrapeDelayMaxSeconds,
            ScrapePostsPerGroup = scrapePostsPerGroup ?? current.ScrapePostsPerGroup,
            ScrapeMaxGroupsPerCycle = scrapeMaxGroupsPerCycle ?? current.ScrapeMaxGroupsPerCycle,
            AutoBackgroundScrapeEnabled = autoBackgroundScrapeEnabled ?? current.AutoBackgroundScrapeEnabled,
            AutoScrapeIntervalMinutes = autoScrapeIntervalMinutes ?? current.AutoScrapeIntervalMinutes,
            ScrapeFilterKeywords = scrapeFilterKeywords ?? current.ScrapeFilterKeywords,
            RunBrowserHeadless = runBrowserHeadless ?? current.RunBrowserHeadless,
            HasExplicitHeadlessPreference = true,
            PreferredBrowser = resolvedBrowser,
            Theme = resolvedTheme,
            Language = resolvedLang
        }).Normalize();

        await _manager.Settings.SaveAsync(updated, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Success,
            account: null,
            "App Settings",
            $"MCP updated application settings (delay={updated.MinDelaySeconds}s..{updated.MaxDelaySeconds}s, scrapeDelay={updated.ScrapeDelayMinSeconds}s..{updated.ScrapeDelayMaxSeconds}s, autoScrape={updated.AutoBackgroundScrapeEnabled}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            settings = new
            {
                theme = updated.Theme.ToString(),
                language = updated.Language.ToString(),
                preferred_browser = updated.PreferredBrowser.ToString(),
                run_browser_headless = updated.RunBrowserHeadless,
                min_delay_seconds = updated.MinDelaySeconds,
                max_delay_seconds = updated.MaxDelaySeconds,
                scrape_delay_min_seconds = updated.ScrapeDelayMinSeconds,
                scrape_delay_max_seconds = updated.ScrapeDelayMaxSeconds,
                scrape_posts_per_group = updated.ScrapePostsPerGroup,
                scrape_max_groups_per_cycle = updated.ScrapeMaxGroupsPerCycle,
                auto_background_scrape_enabled = updated.AutoBackgroundScrapeEnabled,
                auto_scrape_interval_minutes = updated.AutoScrapeIntervalMinutes,
                scrape_filter_keywords = updated.ScrapeFilterKeywords
            }
        });
    }

    [McpServerTool(Name = "fb_get_storage_usage")]
    [Description("Get real-time disk and memory storage breakdown for the Facebook.Net data directory and isolated Chromium profiles.")]
    public async Task<string> GetStorageUsageAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        StorageUsageSnapshot snap = _manager.StorageUsage.CalculateSnapshot(
            _manager.DataDirectory,
            _manager.ProfilesRootDirectory);

        return JsonSerializer.Serialize(new
        {
            total_used_formatted = _manager.StorageUsage.FormatByteSize(snap.TotalUsedBytes),
            total_used_bytes = snap.TotalUsedBytes,
            main_db_formatted = _manager.StorageUsage.FormatByteSize(snap.MainDatabaseBytes),
            audit_db_formatted = _manager.StorageUsage.FormatByteSize(snap.AuditDatabaseBytes),
            browser_profiles_formatted = _manager.StorageUsage.FormatByteSize(snap.BrowserProfilesBytes),
            process_ram_formatted = _manager.StorageUsage.FormatByteSize(snap.ProcessRamBytes),
            drive_free_formatted = _manager.StorageUsage.FormatByteSize(snap.DriveFreeBytes),
            profiles = snap.ProfileBreakdown.Select(p => new
            {
                profile_name = p.ProfileName,
                size_formatted = _manager.StorageUsage.FormatByteSize(p.SizeBytes),
                size_bytes = p.SizeBytes
            })
        });
    }

    [McpServerTool(Name = "fb_clean_browser_cache")]
    [Description("Clean transient Chromium disk caches (Cache, Code Cache, GPUCache, Service Worker CacheStorage) across all managed browser profiles while preserving login cookies and session state.")]
    public async Task<string> CleanBrowserCacheAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        StorageUsageSnapshot before = _manager.StorageUsage.CalculateSnapshot(
            _manager.DataDirectory,
            _manager.ProfilesRootDirectory);

        _manager.StorageUsage.CleanProfileCaches(_manager.ProfilesRootDirectory);

        StorageUsageSnapshot after = _manager.StorageUsage.CalculateSnapshot(
            _manager.DataDirectory,
            _manager.ProfilesRootDirectory);

        long freedBytes = Math.Max(0L, before.BrowserProfilesBytes - after.BrowserProfilesBytes);
        string freedFormatted = _manager.StorageUsage.FormatByteSize(freedBytes);

        _ = await _manager.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Success,
            account: null,
            "Browser Cache Cleanup",
            $"MCP cleaned Chromium profile caches (freed {freedFormatted}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            freed_bytes = freedBytes,
            freed_formatted = freedFormatted,
            profiles_bytes_before = before.BrowserProfilesBytes,
            profiles_bytes_after = after.BrowserProfilesBytes,
            profiles_formatted_after = _manager.StorageUsage.FormatByteSize(after.BrowserProfilesBytes)
        });
    }

    [McpServerTool(Name = "fb_get_audit_logs")]
    [Description("Query recent activity and Personal/Campaign audit logs from facebook_audit.db.")]
    public async Task<string> GetAuditLogsAsync(
        [Description("Optional category filter: 'System', 'Account', 'Group', 'Post', 'SalesComment', 'Scrape', 'Personal'")] string? category = null,
        [Description("Optional search keyword")] string? searchKeyword = null,
        [Description("Optional account UID filter")] string? accountUid = null,
        [Description("Maximum number of log entries to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        AuditCategory? parsedCat = !string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<AuditCategory>(category.Trim(), ignoreCase: true, out AuditCategory cat)
            ? cat
            : null;

        AuditLogFilter filter = new(
            Category: parsedCat,
            AccountUid: string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim(),
            SearchKeyword: string.IsNullOrWhiteSpace(searchKeyword) ? null : searchKeyword.Trim(),
            Limit: Math.Clamp(limit, 1, 500));

        IReadOnlyList<AuditLogRecord> logs = await _manager.AuditStore.QueryAsync(filter, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            count = logs.Count,
            logs = logs.Select(l => new
            {
                id = l.Id,
                timestamp = l.Timestamp.ToString("o"),
                category = l.Category.ToString(),
                severity = l.Severity.ToString(),
                account_uid = l.AccountUid,
                account_name = l.AccountDisplayName,
                target_name = l.TargetName,
                message = l.Message,
                permalink_url = l.PermalinkUrl
            })
        });
    }

    [McpServerTool(Name = "fb_clear_audit_logs")]
    [Description("Clear all stored audit log records in facebook_audit.db and reclaim SQLite disk space via VACUUM.")]
    public async Task<string> ClearAuditLogsAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        await _manager.AuditStore.ClearAllAsync(ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "cleared"
        });
    }

    [McpServerTool(Name = "fb_check_app_updates")]
    [Description("Check GitHub Releases (ppn-systems/Facebook.Net) to see if a newer version of Facebook One Client is available.")]
    public async Task<string> CheckAppUpdatesAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        AppReleaseCheckResult result = await _manager.AppRelease.CheckForUpdatesAsync(ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            is_update_available = result.IsUpdateAvailable,
            current_version = result.CurrentVersion,
            latest_tag = result.LatestTag,
            published_date = result.PublishedDateText,
            download_url = result.DownloadUrl,
            error_message = result.ErrorMessage
        });
    }
}
