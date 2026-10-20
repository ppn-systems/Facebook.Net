// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> managing Dark/Light theme switching,
/// bilingual (`vi-VN` / `en-US`) language selection, sidebar collapse state, browser engine preferences,
/// and anti-checkpoint delays with automatic synchronous persistence to <c>appsettings.json</c>.
/// </summary>
public sealed partial class ShellViewModel
{
    private bool _isLoadingSettings;

    /// <summary>Gets or sets a value indicating whether the left navigation sidebar is collapsed to icon-only mode.</summary>
    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    /// <summary>Gets or sets the selected theme mode in Settings.</summary>
    [ObservableProperty]
    public partial AppThemeMode SelectedThemeMode { get; set; } = AppThemeMode.Dark;

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
    public partial int MinDelaySeconds { get; set; } = 15;

    /// <summary>Gets or sets the maximum safe human delay in seconds between actions.</summary>
    [ObservableProperty]
    public partial int MaxDelaySeconds { get; set; } = 45;

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
        }
    }

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        this._context.Localization.SetLanguage(value);
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnSelectedBrowserKindChanged(PreferredBrowserKind value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
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
        if (this._isLoadingSettings)
        {
            return;
        }

        string? selectedUid = this.SelectedAccount?.Uid ?? this._context.Settings.Current.LastSelectedAccountUid;

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

        this._context.Settings.Save(updated);
    }

    private void LoadSettingsIntoProperties()
    {
        this._isLoadingSettings = true;
        try
        {
            AppConfig cfg = this._context.Settings.Current;
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
            this._isLoadingSettings = false;
        }
    }

    private void ToggleThemeQuick()
    {
        this._context.Theme.ToggleTheme();
        this.SelectedThemeMode = this._context.Theme.CurrentMode;
    }

    private void ToggleLanguageQuick()
    {
        AppLanguage next = this._context.Localization.CurrentLanguage == AppLanguage.ViVn
            ? AppLanguage.EnUs
            : AppLanguage.ViVn;

        this.SelectedLanguage = next;
    }

    private async Task SaveSettingsAsync()
    {
        AppConfig previous = this._context.Settings.Current;
        this._context.Theme.SetTheme(this.SelectedThemeMode);
        this._context.Localization.SetLanguage(this.SelectedLanguage);

        this.FlushSettingsToDisk();
        AppConfig updated = this._context.Settings.Current;

        this._isLoadingSettings = true;
        try
        {
            this.MinDelaySeconds = updated.MinDelaySeconds;
            this.MaxDelaySeconds = updated.MaxDelaySeconds;
        }
        finally
        {
            this._isLoadingSettings = false;
        }

        // If browser engine or headless mode changed, recycle active browser sessions so the next launch uses the new browser
        if (previous.PreferredBrowser != updated.PreferredBrowser ||
            previous.RunBrowserHeadless != updated.RunBrowserHeadless ||
            !string.Equals(previous.CustomBrowserExecutablePath, updated.CustomBrowserExecutablePath, StringComparison.Ordinal))
        {
            for (int i = 0; i < this.Accounts.Count; i++)
            {
                await this._context.SessionManager.CloseSessionAsync(this.Accounts[i].Uid).ConfigureAwait(true);
            }
        }

        string friendlyTheme = this._context.Localization.FormatThemeMode(updated.Theme);
        string friendlyLang = LocalizationService.FormatLanguage(updated.Language);
        string friendlyBrowser = this._context.Localization.FormatBrowserKind(updated.PreferredBrowser);

        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Success,
            this._context.Localization["Nav.Settings"],
            this._context.Localization.Format("Log.SettingsSaved", friendlyTheme, friendlyLang, friendlyBrowser, updated.MinDelaySeconds, updated.MaxDelaySeconds)).ConfigureAwait(true);
    }
}
