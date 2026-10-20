// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Core.Localization;

/// <summary>
/// Supported UI languages for the desktop suite.
/// Adding a new language only requires adding an enum member here and a corresponding <c>Assets/Locales/&lt;code&gt;.json</c> file.
/// </summary>
public enum AppLanguage
{
    /// <summary>Vietnamese (Tiếng Việt - vi-VN).</summary>
    ViVn,

    /// <summary>English (United States - en-US).</summary>
    EnUs
}

/// <summary>
/// Centralized, Native-AOT-safe i18n localization service that loads language catalogs from
/// embedded JSON locale files (<c>Assets/Locales/vi-VN.json</c>, <c>Assets/Locales/en-US.json</c>).
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Dictionary<AppLanguage, Dictionary<string, string>> s_catalogs = new()
    {
        [AppLanguage.ViVn] = LoadEmbeddedLocale("Assets.Locales.vi-VN.json"),
        [AppLanguage.EnUs] = LoadEmbeddedLocale("Assets.Locales.en-US.json")
    };

    private AppLanguage _currentLanguage = AppLanguage.ViVn;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs whenever the active UI language is switched.
    /// </summary>
    public event EventHandler<AppLanguage>? LanguageChanged;

    /// <summary>
    /// Gets the current active UI language.
    /// </summary>
    public AppLanguage CurrentLanguage => this._currentLanguage;

    /// <summary>
    /// Gets the localized string for the specified key.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The localized text, or the key itself if not found.</returns>
    public string this[string key] => this.Get(key);

    /// <summary>
    /// Retrieves the localized string for the specified key in the active language, falling back to <see cref="AppLanguage.ViVn"/> if missing.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The localized string.</returns>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (s_catalogs.TryGetValue(this._currentLanguage, out Dictionary<string, string>? primary) &&
            primary.TryGetValue(key, out string? value))
        {
            return value;
        }

        if (s_catalogs.TryGetValue(AppLanguage.ViVn, out Dictionary<string, string>? fallbackDict) &&
            fallbackDict.TryGetValue(key, out string? fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>
    /// Formats a parameterized localized string (<c>{0}</c>, <c>{1}</c>, ...) in the active language.
    /// </summary>
    /// <param name="key">The translation key in the locale JSON file.</param>
    /// <param name="args">Arguments to substitute into the localized template.</param>
    /// <returns>The formatted localized string.</returns>
    public string Format(string key, params object?[] args)
    {
        string template = this.Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        return string.Format(CultureInfo.InvariantCulture, template, args);
    }

    /// <summary>
    /// Switches the active UI language and notifies all bound controls and listeners.
    /// </summary>
    /// <param name="language">The target language.</param>
    public void SetLanguage(AppLanguage language)
    {
        if (this._currentLanguage == language)
        {
            return;
        }

        this._currentLanguage = language;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CurrentLanguage)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        this.LanguageChanged?.Invoke(this, language);
    }

    /// <summary>
    /// Returns all registered localization keys for the specified language catalog.
    /// </summary>
    public static Dictionary<string, string>.KeyCollection GetKeys(AppLanguage language) =>
        s_catalogs[language].Keys;

    /// <summary>
    /// Formats an <see cref="AccountStatus"/> into a friendly, localized user label.
    /// </summary>
    public string FormatAccountStatus(AccountStatus status) =>
        status switch
        {
            AccountStatus.Active => this["Status.Account.Active"],
            AccountStatus.Running => this["Status.Account.Running"],
            AccountStatus.CookieExpired => this["Status.Account.Expired"],
            AccountStatus.Checkpoint => this["Status.Account.Checkpoint"],
            AccountStatus.Disabled => this["Status.Account.Disabled"],
            AccountStatus.Idle => this["Status.Account.Idle"],
            _ => this["Status.Account.Idle"]
        };

    /// <summary>
    /// Formats a <see cref="GroupMembershipState"/> into a friendly, localized user label.
    /// </summary>
    public string FormatGroupMembership(GroupMembershipState state) =>
        state switch
        {
            GroupMembershipState.Member => this["Status.Group.Member"],
            GroupMembershipState.Requested => this["Status.Group.Requested"],
            GroupMembershipState.QuestionnaireCompleted => this["Status.Group.QuestionsDone"],
            GroupMembershipState.CanJoin => this["Status.Group.CanJoin"],
            GroupMembershipState.Unknown => this["Status.Group.CanJoin"],
            _ => this["Status.Group.CanJoin"]
        };

    /// <summary>
    /// Formats an <see cref="AuditCategory"/> (or <c>null</c> for "All") into a friendly, localized label.
    /// </summary>
    public string FormatAuditCategory(AuditCategory? category) =>
        category switch
        {
            null => this["Audit.FilterCategoryAll"],
            AuditCategory.System => this["Audit.Cat.System"],
            AuditCategory.Account => this["Audit.Cat.Account"],
            AuditCategory.Group => this["Audit.Cat.Group"],
            AuditCategory.Post => this["Audit.Cat.Post"],
            AuditCategory.SalesComment => this["Audit.Cat.SalesComment"],
            AuditCategory.Scrape => this["Audit.Cat.Scrape"],
            _ => this["Audit.Cat.System"]
        };

    /// <summary>
    /// Formats an <see cref="AuditTimeRange"/> into a friendly, localized label.
    /// </summary>
    public string FormatAuditTimeRange(AuditTimeRange range) =>
        range switch
        {
            AuditTimeRange.AllTime => this["Audit.Time.All"],
            AuditTimeRange.LastHour => this["Audit.Time.LastHour"],
            AuditTimeRange.Today24Hours => this["Audit.Time.Today24h"],
            AuditTimeRange.Last7Days => this["Audit.Time.Last7Days"],
            _ => this["Audit.Time.All"]
        };

    /// <summary>
    /// Formats a <see cref="PreferredBrowserKind"/> into a friendly, localized label.
    /// </summary>
    public string FormatBrowserKind(PreferredBrowserKind kind) =>
        kind switch
        {
            PreferredBrowserKind.AutoDetect => this["Settings.BrowserAuto"],
            PreferredBrowserKind.Edge => this["Settings.BrowserEdge"],
            PreferredBrowserKind.Chrome => this["Settings.BrowserChrome"],
            PreferredBrowserKind.Brave => this["Settings.BrowserBrave"],
            _ => this["Settings.BrowserAuto"]
        };

    /// <summary>
    /// Formats an <see cref="AppThemeMode"/> into a friendly, localized label.
    /// </summary>
    public string FormatThemeMode(AppThemeMode mode) =>
        mode switch
        {
            AppThemeMode.System => this["Settings.ThemeSystem"],
            AppThemeMode.Light => this["Settings.ThemeLight"],
            AppThemeMode.Dark => this["Settings.ThemeDark"],
            _ => this["Settings.ThemeSystem"]
        };

    /// <summary>
    /// Formats an <see cref="AppLanguage"/> into a friendly display label without locale code parentheses.
    /// </summary>
    public static string FormatLanguage(AppLanguage lang) =>
        lang == AppLanguage.EnUs ? "English" : "Tiếng Việt";

    private static Dictionary<string, string> LoadEmbeddedLocale(string resourceName)
    {
        Assembly assembly = typeof(LocalizationService).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded locale resource '{resourceName}'.");

        using JsonDocument doc = JsonDocument.Parse(stream);
        Dictionary<string, string> dict = new(StringComparer.Ordinal);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        return dict;
    }
}
