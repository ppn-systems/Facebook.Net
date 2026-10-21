// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> managing System/Dark/Light theme switching,
/// bilingual (`vi-VN` / `en-US`) language selection, sidebar collapse state, browser engine preferences,
/// isolated browser profile orchestration, and anti-checkpoint delays.
/// Storage and directory metrics are delegated to <see cref="StorageUsageAnalyzer"/>.
/// </summary>
public sealed partial class ShellViewModel
{
    private bool _isLoadingSettings;

    /// <summary>Gets or sets a value indicating whether the left navigation sidebar is collapsed to icon-only mode.</summary>
    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    /// <summary>Gets or sets the selected theme mode in Settings.</summary>
    [ObservableProperty]
    public partial AppThemeMode SelectedThemeMode { get; set; } = AppThemeMode.System;

    /// <summary>Gets or sets the selected UI language in Settings.</summary>
    [ObservableProperty]
    public partial AppLanguage SelectedLanguage { get; set; } = AppLanguage.ViVn;

    /// <summary>Gets or sets the preferred Chromium browser family.</summary>
    [ObservableProperty]
    public partial PreferredBrowserKind SelectedBrowserKind { get; set; } = PreferredBrowserKind.AutoDetect;

    /// <summary>Gets or sets an optional custom browser executable file path.</summary>
    [ObservableProperty]
    public partial string CustomBrowserExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether browsers should launch in headless mode.</summary>
    [ObservableProperty]
    public partial bool RunBrowserHeadless { get; set; }

    /// <summary>Gets or sets the minimum safe human delay in seconds between actions.</summary>
    [ObservableProperty]
    public partial int MinDelaySeconds { get; set; } = 2;

    /// <summary>Gets or sets the maximum safe human delay in seconds between actions.</summary>
    [ObservableProperty]
    public partial int MaxDelaySeconds { get; set; } = 5;

    /// <summary>Gets or sets the current application version string (e.g., <c>v1.0.0</c>).</summary>
    [ObservableProperty]
    public partial string CurrentAppVersion { get; set; } = GetCurrentAppVersionString();

    /// <summary>Gets or sets the human-readable status message of the in-app update checker.</summary>
    [ObservableProperty]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether an update check against GitHub Releases is in progress.</summary>
    [ObservableProperty]
    public partial bool IsCheckingForUpdate { get; set; }

    /// <summary>Gets or sets a value indicating whether a newer release is available on GitHub Releases.</summary>
    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    /// <summary>Gets or sets the tag name of the latest GitHub release (e.g., <c>v1.1.0</c>).</summary>
    [ObservableProperty]
    public partial string LatestReleaseTag { get; set; } = string.Empty;

    /// <summary>Gets or sets the direct installer or release page download URL for the latest GitHub release.</summary>
    [ObservableProperty]
    public partial string LatestReleaseDownloadUrl { get; set; } = string.Empty;

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnSelectedThemeModeChanged(AppThemeMode value)
    {
        this._context.Theme.SetTheme(value);
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
            _ = this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                this._context.Localization["Nav.Settings"],
                this._context.Localization.Format("Log.Personal.ThemeChanged", this._context.Localization.FormatThemeMode(value)));
        }
    }

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        this._context.Localization.SetLanguage(value);
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
            _ = this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                this._context.Localization["Nav.Settings"],
                this._context.Localization.Format("Log.Personal.LanguageChanged", LocalizationService.FormatLanguage(value)));
        }
    }

    partial void OnSelectedBrowserKindChanged(PreferredBrowserKind value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
            _ = this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                this._context.Localization["Nav.Settings"],
                this._context.Localization.Format("Log.Personal.BrowserChanged", this._context.Localization.FormatBrowserKind(value)));
        }
    }

    partial void OnCustomBrowserExecutablePathChanged(string value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnRunBrowserHeadlessChanged(bool value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnMinDelaySecondsChanged(int value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnMaxDelaySecondsChanged(int value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    /// <summary>
    /// Synchronously writes all current setting properties to <c>appsettings.json</c> so configuration is never lost on exit.
    /// </summary>
    public void FlushSettingsToDisk()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        string? selectedUid = this.SelectedAccount?.Uid ?? _context.Settings.Current.LastSelectedAccountUid;

        AppConfig updated = new AppConfig(
            Theme: this.SelectedThemeMode,
            Language: this.SelectedLanguage,
            PreferredBrowser: this.SelectedBrowserKind,
            CustomBrowserExecutablePath: string.IsNullOrWhiteSpace(this.CustomBrowserExecutablePath)
                ? null
                : this.CustomBrowserExecutablePath.Trim(),
            RunBrowserHeadless: this.RunBrowserHeadless,
            MinDelaySeconds: this.MinDelaySeconds,
            MaxDelaySeconds: this.MaxDelaySeconds,
            IsAuditDrawerExpanded: this.AuditDrawer.IsExpanded,
            IsSidebarCollapsed: this.IsSidebarCollapsed,
            LastSelectedAccountUid: selectedUid,
            ScrapeFilterKeywords: this.ScrapeFilterKeywords,
            ScrapePostsPerGroup: this.ScrapePostsPerGroup,
            AutoBackgroundScrapeEnabled: this.AutoBackgroundScrapeEnabled,
            AutoScrapeIntervalMinutes: this.AutoScrapeIntervalMinutes,
            ScrapeAllEnabledTargetGroups: this.ScrapeAllEnabledTargetGroups,
            ScrapeDelayMinSeconds: this.ScrapeDelayMinSeconds,
            ScrapeDelayMaxSeconds: this.ScrapeDelayMaxSeconds,
            ScrapeMaxGroupsPerCycle: this.ScrapeMaxGroupsPerCycle).Normalize();

        _context.Settings.Save(updated);
    }

    private void LoadSettingsIntoProperties()
    {
        _isLoadingSettings = true;
        try
        {
            AppConfig cfg = _context.Settings.Current;
            this.IsSidebarCollapsed = cfg.IsSidebarCollapsed;
            this.SelectedThemeMode = cfg.Theme;
            this.SelectedLanguage = cfg.Language;
            this.SelectedBrowserKind = cfg.PreferredBrowser;
            this.CustomBrowserExecutablePath = cfg.CustomBrowserExecutablePath ?? string.Empty;
            this.RunBrowserHeadless = cfg.RunBrowserHeadless;
            this.MinDelaySeconds = cfg.MinDelaySeconds;
            this.MaxDelaySeconds = cfg.MaxDelaySeconds;
            this.ScrapeFilterKeywords = cfg.ScrapeFilterKeywords ?? string.Empty;
            this.ScrapePostsPerGroup = cfg.ScrapePostsPerGroup;
            this.AutoBackgroundScrapeEnabled = cfg.AutoBackgroundScrapeEnabled;
            this.AutoScrapeIntervalMinutes = cfg.AutoScrapeIntervalMinutes;
            this.ScrapeAllEnabledTargetGroups = cfg.ScrapeAllEnabledTargetGroups;
            this.ScrapeDelayMinSeconds = cfg.ScrapeDelayMinSeconds;
            this.ScrapeDelayMaxSeconds = cfg.ScrapeDelayMaxSeconds;
            this.ScrapeMaxGroupsPerCycle = cfg.ScrapeMaxGroupsPerCycle;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void ToggleThemeQuick()
    {
        _context.Theme.ToggleTheme();
        this.SelectedThemeMode = _context.Theme.CurrentMode;
    }

    private void ToggleLanguageQuick()
    {
        AppLanguage next = _context.Localization.CurrentLanguage == AppLanguage.ViVn
            ? AppLanguage.EnUs
            : AppLanguage.ViVn;

        this.SelectedLanguage = next;
    }

    private async Task SaveSettingsAsync()
    {
        AppConfig previous = _context.Settings.Current;
        _context.Theme.SetTheme(this.SelectedThemeMode);
        _context.Localization.SetLanguage(this.SelectedLanguage);

        this.FlushSettingsToDisk();
        AppConfig updated = _context.Settings.Current;

        _isLoadingSettings = true;
        try
        {
            this.MinDelaySeconds = updated.MinDelaySeconds;
            this.MaxDelaySeconds = updated.MaxDelaySeconds;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        // If browser engine or headless mode changed, recycle active browser sessions so the next launch uses the new browser
        if (previous.PreferredBrowser != updated.PreferredBrowser ||
            previous.RunBrowserHeadless != updated.RunBrowserHeadless ||
            !string.Equals(previous.CustomBrowserExecutablePath, updated.CustomBrowserExecutablePath, StringComparison.Ordinal))
        {
            for (int i = 0; i < this.Accounts.Count; i++)
            {
                await _context.SessionManager.CloseSessionAsync(this.Accounts[i].Uid).ConfigureAwait(true);
            }
        }

        string friendlyTheme = _context.Localization.FormatThemeMode(updated.Theme);
        string friendlyLang = LocalizationService.FormatLanguage(updated.Language);
        string friendlyBrowser = _context.Localization.FormatBrowserKind(updated.PreferredBrowser);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Nav.Settings"],
            _context.Localization.Format("Log.SettingsSaved", friendlyTheme, friendlyLang, friendlyBrowser, updated.MinDelaySeconds, updated.MaxDelaySeconds)).ConfigureAwait(true);
    }

    /// <summary>
    /// Formats a byte count into a concise human-readable string (<c>KB</c>, <c>MB</c>, or <c>GB</c>).
    /// </summary>
    public static string FormatByteSize(long bytes) =>
        StorageUsageAnalyzer.FormatByteSize(bytes);

    /// <summary>
    /// Scans the application's local data directory and isolated Chromium profile folders to compute real-time storage usage.
    /// </summary>
    public StorageUsageSnapshot CalculateStorageUsageSnapshot() =>
        _context.StorageUsage.CalculateSnapshot(
            _context.DataDirectory,
            _context.ProfilesRootDirectory,
            this.ResolveFriendlyProfileLabel);

    /// <summary>
    /// Cleans transient Chromium disk caches across all managed browser profiles while preserving login cookies and session state.
    /// </summary>
    public async Task<long> CleanBrowserProfilesCacheAsync()
    {
        StorageUsageSnapshot before = this.CalculateStorageUsageSnapshot();
        _context.StorageUsage.CleanProfileCaches(_context.ProfilesRootDirectory);
        StorageUsageSnapshot after = this.CalculateStorageUsageSnapshot();
        long freedBytes = Math.Max(0, before.TotalUsedBytes - after.TotalUsedBytes);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Nav.Settings"],
            _context.Localization.Format("Storage.LogCacheCleaned", FormatByteSize(freedBytes), FormatByteSize(after.TotalUsedBytes))).ConfigureAwait(true);

        return freedBytes;
    }

    /// <summary>
    /// Returns all available browser profiles matching the currently selected browser family
    /// (managed profiles in <c>Profiles/</c> plus discovered system browser profiles for the active browser).
    /// Automatically cleans up unauthenticated orphan placeholder folders in <c>Profiles/</c>.
    /// </summary>
    public List<BrowserProfileOption> GetAvailableBrowserProfiles()
    {
        this.PruneOrphanPlaceholderProfiles();

        PreferredBrowserKind effectiveKind = _context.Settings.ResolveEffectiveBrowserKind();
        List<BrowserProfileOption> discovered = AppSettingsService.DiscoverBrowserProfiles(
            _context.ProfilesRootDirectory,
            effectiveKind);
        List<BrowserProfileOption> list = new(discovered.Count + 2);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < discovered.Count; i++)
        {
            BrowserProfileOption item = discovered[i];
            if (seen.Add(item.DirectoryPath))
            {
                string folderName = Path.GetFileName(item.DirectoryPath);
                bool isManaged = this.IsProfileInsideManagedRoot(item.DirectoryPath);
                string resolvedLabel = isManaged
                    ? $"One Client — {this.ResolveFriendlyProfileLabel(folderName, item.DirectoryPath)}"
                    : item.DisplayName;
                list.Add(new BrowserProfileOption(
                    resolvedLabel,
                    item.DirectoryPath,
                    IsManagedByApp: isManaged,
                    HasFacebookSession: item.HasFacebookSession));
            }
        }

        AccountProfile? acc = this.SelectedAccount;
        if (acc is not null &&
            !string.IsNullOrWhiteSpace(acc.ProfileDirectory) &&
            Directory.Exists(acc.ProfileDirectory) &&
            seen.Add(acc.ProfileDirectory))
        {
            string folder = Path.GetFileName(acc.ProfileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            bool isManaged = this.IsProfileInsideManagedRoot(acc.ProfileDirectory);
            string prefix = isManaged ? "One Client" : _context.Localization.FormatBrowserKind(effectiveKind);
            bool hasFb = this.HasProfileAuthenticatedCookieStore(acc.ProfileDirectory);
            string friendly = isManaged ? this.ResolveFriendlyProfileLabel(folder, acc.ProfileDirectory) : folder;
            list.Insert(0, new BrowserProfileOption($"{prefix} — {friendly}", acc.ProfileDirectory, IsManagedByApp: isManaged, HasFacebookSession: hasFb));
        }

        return list;
    }

    /// <summary>
    /// Opens the specified browser profile directory in the OS file manager (Windows Explorer / macOS Finder).
    /// </summary>
    public static void OpenProfileFolderInExplorer(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(directoryPath)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch error
        }
    }

    private void PruneOrphanPlaceholderProfiles()
    {
        if (string.IsNullOrWhiteSpace(_context.ProfilesRootDirectory) || !Directory.Exists(_context.ProfilesRootDirectory))
        {
            return;
        }

        try
        {
            string[] managedDirs = Directory.GetDirectories(_context.ProfilesRootDirectory);
            for (int i = 0; i < managedDirs.Length; i++)
            {
                string dir = managedDirs[i];
                string folderName = Path.GetFileName(dir);
                if (!folderName.StartsWith("login_", StringComparison.OrdinalIgnoreCase) &&
                    !folderName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isAssignedToAnyAccount = false;
                for (int a = 0; a < this.Accounts.Count; a++)
                {
                    AccountProfile acc = this.Accounts[a];
                    if (string.Equals(acc.ProfileDirectory, dir, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        isAssignedToAnyAccount = true;
                        break;
                    }
                }

                if (!isAssignedToAnyAccount && !this.HasProfileAuthenticatedCookieStore(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        _context.StorageUsage.InvalidateCache();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Ignore folder locked by an active browser process
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore inaccessible directory
        }
    }

    /// <summary>
    /// Checks whether the specified <paramref name="directoryPath"/> is strictly inside the application's managed <c>Profiles</c> root directory.
    /// </summary>
    public bool IsProfileInsideManagedRoot(string? directoryPath) =>
        _context.StorageUsage.IsProfileInsideManagedRoot(directoryPath, _context.ProfilesRootDirectory);

    /// <summary>
    /// Computes the total disk size in bytes of a specific profile folder using the fast TTL cache.
    /// </summary>
    public static long GetProfileDirectorySizeBytes(string? directoryPath) =>
        StorageUsageAnalyzer.GetProfileDirectorySizeBytes(directoryPath);

    /// <summary>
    /// Deletes an application-created browser profile directory from disk.
    /// Strictly refuses to delete any folder outside <c>ProfilesRootDirectory</c>.
    /// </summary>
    public async Task<bool> DeleteManagedBrowserProfileAsync(BrowserProfileOption? profile)
    {
        if (profile is null || !profile.IsManagedByApp || !this.IsProfileInsideManagedRoot(profile.DirectoryPath))
        {
            return false;
        }

        string targetDir = profile.DirectoryPath;
        for (int i = 0; i < this.Accounts.Count; i++)
        {
            AccountProfile acc = this.Accounts[i];
            if (string.Equals(acc.ProfileDirectory, targetDir, StringComparison.OrdinalIgnoreCase))
            {
                await _context.SessionManager.CloseSessionAsync(acc.Uid).ConfigureAwait(true);
            }
        }

        if (Directory.Exists(targetDir))
        {
            try
            {
                Directory.Delete(targetDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        _context.StorageUsage.InvalidateCache();

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            profile.DisplayName,
            _context.Localization.Format("Settings.LogProfileDeleted", profile.DisplayName)).ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// Creates a new isolated browser profile folder under <c>ProfilesRootDirectory</c> and assigns it to the currently selected account
    /// (or creates a ready-to-use account entry if no account exists yet).
    /// </summary>
    public async Task<string?> CreateAndAssignBrowserProfileAsync(string rawProfileName)
    {
        if (string.IsNullOrWhiteSpace(rawProfileName))
        {
            return null;
        }

        string trimmed = rawProfileName.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        System.Text.StringBuilder safeBuilder = new(trimmed.Length);
        for (int i = 0; i < trimmed.Length; i++)
        {
            char ch = trimmed[i];
            if (Array.IndexOf(invalidChars, ch) < 0 && ch is not '(' and not ')')
            {
                _ = safeBuilder.Append(ch);
            }
        }

        string safeFolderName = safeBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(safeFolderName))
        {
            safeFolderName = $"Profile_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        }

        string newProfileDir = Path.Combine(_context.ProfilesRootDirectory, safeFolderName);
        _ = Directory.CreateDirectory(newProfileDir);
        _context.StorageUsage.InvalidateCache();

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            trimmed,
            _context.Localization.Format("Log.Personal.ProfileCreated", trimmed)).ConfigureAwait(true);

        await this.AssignBrowserProfileToSelectedAccountAsync(newProfileDir, trimmed).ConfigureAwait(true);
        return newProfileDir;
    }

    /// <summary>
    /// Assigns an existing or newly created browser profile directory to the active account (or creates an account bound to that profile if none exists).
    /// When assigning a system browser profile (outside <c>ProfilesRootDirectory</c>), automatically syncs its cookies and preferences into an isolated
    /// <c>One Client</c> profile directory so it works even while the main browser is running and complies with Chromium's CDP isolation requirements.
    /// </summary>
    public async Task AssignBrowserProfileToSelectedAccountAsync(string profileDirectoryPath, string? friendlyProfileName = null)
    {
        if (string.IsNullOrWhiteSpace(profileDirectoryPath))
        {
            return;
        }

        string fullPath = profileDirectoryPath.Trim();
        string rawLeafName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string folderName = !string.IsNullOrWhiteSpace(friendlyProfileName)
            ? friendlyProfileName.Trim()
            : rawLeafName;

        if (!this.IsProfileInsideManagedRoot(fullPath) && Directory.Exists(fullPath))
        {
            string safeSlug = SanitizeFolderSlug($"{folderName}_{rawLeafName}");
            string isolatedDir = Path.Combine(_context.ProfilesRootDirectory, safeSlug);
            if (_context.AccountVault.TrySyncSystemProfileToIsolatedRoot(fullPath, isolatedDir))
            {
                fullPath = isolatedDir;
                _context.StorageUsage.InvalidateCache();
            }
        }

        _ = Directory.CreateDirectory(fullPath);
        bool hasCookies = this.HasProfileAuthenticatedCookieStore(fullPath);

        if (this.SelectedAccount is not null)
        {
            AccountProfile current = this.SelectedAccount;
            if (!string.Equals(current.ProfileDirectory, fullPath, StringComparison.OrdinalIgnoreCase) ||
                (hasCookies && current.Status != AccountStatus.Active))
            {
                await _context.SessionManager.CloseSessionAsync(current.Uid).ConfigureAwait(true);
                AccountProfile updated = current with
                {
                    ProfileDirectory = fullPath,
                    Status = hasCookies ? AccountStatus.Active : current.Status
                };
                await _context.AccountStore.UpsertAccountAsync(updated).ConfigureAwait(true);
                await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                current.DisplayName,
                _context.Localization.Format("Settings.LogProfileAssigned", folderName, current.DisplayName)).ConfigureAwait(true);
        }
        else
        {
            string newUid = $"profile_{Guid.NewGuid():N}"[..16];
            string ua = OperatingSystem.IsMacOS()
                ? ProtocolConstants.Protocol.DefaultMacOsUserAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent;

            AccountProfile created = new(
                Uid: newUid,
                DisplayName: folderName,
                CookieHeader: string.Empty,
                UserAgent: ua,
                ProfileDirectory: fullPath,
                ProxyId: null,
                TwoFactorSecret: null,
                Status: hasCookies ? AccountStatus.Active : AccountStatus.Idle,
                LastActiveAt: DateTimeOffset.UtcNow);

            await _context.AccountStore.UpsertAccountAsync(created).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(created.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                created.DisplayName,
                _context.Localization.Format("Settings.LogProfileAssigned", folderName, created.DisplayName)).ConfigureAwait(true);
        }
    }

    private static string SanitizeFolderSlug(string raw)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        System.Text.StringBuilder sb = new(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (Array.IndexOf(invalid, c) < 0 && c is not '(' and not ')' and not '—' and not '·')
            {
                _ = sb.Append(c == ' ' ? '_' : c);
            }
        }

        string slug = sb.ToString().Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? $"Imported_{DateTimeOffset.UtcNow:yyyyMMdd}" : slug;
    }

    private string ResolveFriendlyProfileLabel(string folderName, string fullDirPath)
    {
        for (int i = 0; i < this.Accounts.Count; i++)
        {
            AccountProfile acc = this.Accounts[i];
            if (string.Equals(acc.ProfileDirectory, fullDirPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase))
            {
                bool isAutoGeneratedId =
                    folderName.StartsWith("login_", StringComparison.OrdinalIgnoreCase) ||
                    folderName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(acc.DisplayName, folderName, StringComparison.OrdinalIgnoreCase);

                return isAutoGeneratedId
                    ? acc.DisplayName
                    : $"{acc.DisplayName} · {folderName}";
            }
        }

        return folderName;
    }

    private static string GetCurrentAppVersionString()
    {
        Version? asmVersion = typeof(ShellViewModel).Assembly.GetName().Version;
        if (asmVersion is null)
        {
            return "v1.0.0";
        }

        int build = asmVersion.Build >= 0 ? asmVersion.Build : 0;
        return $"v{asmVersion.Major}.{asmVersion.Minor}.{build}";
    }

    /// <summary>
    /// Normalizes a release tag or version string (such as <c>v1.2.3-beta.1</c>) into a comparable 3-part <see cref="Version"/>.
    /// </summary>
    public static Version TryParseReleaseVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return new Version(1, 0, 0);
        }

        string cleaned = rawVersion.Trim().TrimStart('v', 'V');
        int dashIdx = cleaned.IndexOfAny(['-', '+']);
        if (dashIdx > 0)
        {
            cleaned = cleaned[..dashIdx];
        }

        if (Version.TryParse(cleaned, out Version? parsed) && parsed is not null)
        {
            int build = parsed.Build >= 0 ? parsed.Build : 0;
            return new Version(parsed.Major, parsed.Minor, build);
        }

        return new Version(1, 0, 0);
    }

    /// <summary>
    /// Queries GitHub Releases (<c>ppn-systems/Facebook.Net</c>) to check if a newer application version is available.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (this.IsCheckingForUpdate)
        {
            return;
        }

        this.IsCheckingForUpdate = true;
        this.UpdateStatusMessage = _context.Localization["Settings.UpdateStatusChecking"];

        try
        {
            using HttpClient http = new()
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FacebookOneClient-UpdateChecker/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            const string ReleasesPageFallback = "https://github.com/ppn-systems/Facebook.Net/releases";
            string json = await http
                .GetStringAsync(new Uri("https://api.github.com/repos/ppn-systems/Facebook.Net/releases?per_page=20"))
                .ConfigureAwait(true);

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                this.IsUpdateAvailable = false;
                this.LatestReleaseTag = this.CurrentAppVersion;
                this.LatestReleaseDownloadUrl = ReleasesPageFallback;
                this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusUpToDate", this.CurrentAppVersion);
                return;
            }

            // Pick the highest semantic version among all non-draft releases (tie-breaking by published_at timestamp)
            JsonElement? chosenRelease = null;
            Version? highestVersion = null;
            DateTimeOffset highestPublishedAt = DateTimeOffset.MinValue;

            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                bool isDraft = item.TryGetProperty("draft", out JsonElement draftProp) && draftProp.ValueKind == JsonValueKind.True;
                if (isDraft || !item.TryGetProperty("tag_name", out JsonElement tagCheck))
                {
                    continue;
                }

                string? rawTag = tagCheck.GetString();
                if (string.IsNullOrWhiteSpace(rawTag))
                {
                    continue;
                }

                Version candidateVer = TryParseReleaseVersion(rawTag);
                DateTimeOffset candidateDate = DateTimeOffset.MinValue;
                if (item.TryGetProperty("published_at", out JsonElement pubProp) &&
                    DateTimeOffset.TryParse(pubProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedPub))
                {
                    candidateDate = parsedPub;
                }

                if (highestVersion is null ||
                    candidateVer > highestVersion ||
                    (candidateVer == highestVersion && candidateDate > highestPublishedAt))
                {
                    highestVersion = candidateVer;
                    highestPublishedAt = candidateDate;
                    chosenRelease = item;
                }
            }

            chosenRelease ??= doc.RootElement[0];
            JsonElement rel = chosenRelease.Value;

            string tagName = rel.TryGetProperty("tag_name", out JsonElement tagEl) && !string.IsNullOrWhiteSpace(tagEl.GetString())
                ? tagEl.GetString()!.Trim()
                : this.CurrentAppVersion;

            string htmlUrl = rel.TryGetProperty("html_url", out JsonElement htmlEl) && !string.IsNullOrWhiteSpace(htmlEl.GetString())
                ? htmlEl.GetString()!.Trim()
                : ReleasesPageFallback;

            string rawDate = string.Empty;
            if (rel.TryGetProperty("published_at", out JsonElement pubEl) && !string.IsNullOrWhiteSpace(pubEl.GetString()))
            {
                rawDate = pubEl.GetString()!;
            }
            else if (rel.TryGetProperty("created_at", out JsonElement createdEl) && !string.IsNullOrWhiteSpace(createdEl.GetString()))
            {
                rawDate = createdEl.GetString()!;
            }

            string dateText = DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)
                ? dto.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "—";

            string preferredExt = OperatingSystem.IsMacOS() ? ".dmg" : ".msi";
            string? assetUrl = null;
            if (rel.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsEl.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out JsonElement nameEl) ? (nameEl.GetString() ?? string.Empty) : string.Empty;
                    string dlUrl = asset.TryGetProperty("browser_download_url", out JsonElement dlEl) ? (dlEl.GetString() ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(dlUrl) && assetName.EndsWith(preferredExt, StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = dlUrl;
                        break;
                    }
                }
            }

            Version remoteVersion = TryParseReleaseVersion(tagName);
            Version currentVersion = TryParseReleaseVersion(this.CurrentAppVersion);

            if (remoteVersion > currentVersion)
            {
                this.IsUpdateAvailable = true;
                this.LatestReleaseTag = tagName;
                this.LatestReleaseDownloadUrl = assetUrl ?? htmlUrl;
                this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusAvailable", tagName, dateText);
            }
            else
            {
                this.IsUpdateAvailable = false;
                this.LatestReleaseTag = tagName;
                this.LatestReleaseDownloadUrl = htmlUrl;
                this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusUpToDate", this.CurrentAppVersion);
            }
        }
        catch (Exception ex)
        {
            this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusFailed", ex.Message);
        }
        finally
        {
            this.IsCheckingForUpdate = false;
        }
    }

    /// <summary>
    /// Downloads the latest installer package (<c>.msi</c> on Windows, <c>.dmg</c> on macOS) directly into <c>%TEMP%</c>,
    /// runs the silent/passive upgrade installer, and automatically relaunches the updated application.
    /// Falls back to opening the release download URL in the system browser if direct binary installation is not applicable.
    /// </summary>
    public async Task DownloadAndApplyUpdateAsync()
    {
        if (this.IsCheckingForUpdate)
        {
            return;
        }

        string url = !string.IsNullOrWhiteSpace(this.LatestReleaseDownloadUrl)
            ? this.LatestReleaseDownloadUrl.Trim()
            : "https://github.com/ppn-systems/Facebook.Net/releases";

        bool isMsi = OperatingSystem.IsWindows() && url.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        bool isDmg = OperatingSystem.IsMacOS() && url.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase);

        if (!isMsi && !isDmg)
        {
            this.OpenUpdateDownloadPage();
            return;
        }

        this.IsCheckingForUpdate = true;
        string tag = !string.IsNullOrWhiteSpace(this.LatestReleaseTag) ? this.LatestReleaseTag : "latest";

        try
        {
            string ext = isMsi ? ".msi" : ".dmg";
            string safeTag = tag.Replace('/', '_').Replace('\\', '_');
            string tempInstallerPath = Path.Combine(Path.GetTempPath(), $"FacebookOneClient-Update-{safeTag}{ext}");

            using HttpClient http = new()
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FacebookOneClient-UpdateChecker/1.0");

            using HttpResponseMessage response = await http
                .GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(true);
            _ = response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using (Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(true))
            await using (FileStream fileStream = new(tempInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int lastReportedPct = -1;
                int read;
                while ((read = await contentStream.ReadAsync(buffer).ConfigureAwait(true)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
                    totalRead += read;
                    if (totalBytes is > 0)
                    {
                        int pct = (int)Math.Clamp(totalRead * 100L / totalBytes.Value, 0, 100);
                        if (pct != lastReportedPct)
                        {
                            lastReportedPct = pct;
                            this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusDownloading", tag, pct);
                        }
                    }
                }
            }

            this.UpdateStatusMessage = _context.Localization.Format("Settings.UpdateStatusInstalling", tag);
            this.FlushSettingsToDisk();

            if (isMsi)
            {
                string currentExe = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "FacebookOneClient.exe");
                const string InstalledProgramFilesExe = @"C:\Program Files\Facebook One Client\FacebookOneClient.exe";
                string batchPath = Path.Combine(Path.GetTempPath(), $"fb_one_update_{Guid.NewGuid():N}.cmd");
                string batchScript =
                    "@echo off\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"msiexec.exe /i \"{tempInstallerPath}\" /passive /norestart\r\n" +
                    $"if exist \"{InstalledProgramFilesExe}\" (\r\n" +
                    $"    start \"\" \"{InstalledProgramFilesExe}\"\r\n" +
                    ") else (\r\n" +
                    $"    start \"\" \"{currentExe}\"\r\n" +
                    ")\r\n" +
                    "del \"%~f0\"\r\n";

                await File.WriteAllTextAsync(batchPath, batchScript).ConfigureAwait(true);
                _ = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                Environment.Exit(0);
            }
            else if (isDmg)
            {
                string shPath = Path.Combine(Path.GetTempPath(), $"fb_one_update_{Guid.NewGuid():N}.sh");
                string mountPoint = $"/Volumes/FBOneUpdate_{Guid.NewGuid():N}"[..28];
                string shScript =
                    "#!/bin/bash\n" +
                    "sleep 2\n" +
                    $"hdiutil attach -nobrowse -mountpoint \"{mountPoint}\" \"{tempInstallerPath}\" >/dev/null 2>&1\n" +
                    $"if [ -d \"{mountPoint}/Facebook One Client.app\" ]; then\n" +
                    "  rm -rf \"/Applications/Facebook One Client.app\"\n" +
                    $"  cp -R \"{mountPoint}/Facebook One Client.app\" \"/Applications/\"\n" +
                    "fi\n" +
                    $"hdiutil detach \"{mountPoint}\" -force >/dev/null 2>&1 || true\n" +
                    "open -a \"/Applications/Facebook One Client.app\"\n" +
                    "rm -f \"$0\"\n";

                await File.WriteAllTextAsync(shPath, shScript).ConfigureAwait(true);
                _ = Process.Start(new ProcessStartInfo("/bin/bash", $"\"{shPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                Environment.Exit(0);
            }
        }
        catch
        {
            this.OpenUpdateDownloadPage();
        }
        finally
        {
            this.IsCheckingForUpdate = false;
        }
    }

    /// <summary>
    /// Opens the latest release download URL or GitHub Releases page in the system default browser.
    /// </summary>
    public void OpenUpdateDownloadPage()
    {
        string url = !string.IsNullOrWhiteSpace(this.LatestReleaseDownloadUrl)
            ? this.LatestReleaseDownloadUrl
            : "https://github.com/ppn-systems/Facebook.Net/releases";

        try
        {
            _ = Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch failures in headless environments
        }
    }
}
