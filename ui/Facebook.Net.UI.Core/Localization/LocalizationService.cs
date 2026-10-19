// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Facebook.Net.UI.Core.Localization;

/// <summary>
/// Supported UI languages for the desktop suite.
/// </summary>
public enum AppLanguage
{
    /// <summary>Vietnamese (Tiếng Việt - vi-VN).</summary>
    ViVn,

    /// <summary>English (United States - en-US).</summary>
    EnUs
}

/// <summary>
/// Reactive bilingual localization service (`vi-VN` and `en-US`).
/// Split into partial files for core dictionary resolution and language catalogs.
/// </summary>
public sealed partial class LocalizationService : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> s_viVn = CreateVietnameseCatalog();
    private static readonly Dictionary<string, string> s_enUs = CreateEnglishCatalog();

    private AppLanguage _currentLanguage = AppLanguage.ViVn;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs whenever the active UI language is switched.
    /// </summary>
    public event EventHandler<AppLanguage>? LanguageChanged;

    /// <summary>
    /// Gets the current language.
    /// </summary>
    public AppLanguage CurrentLanguage => this._currentLanguage;

    /// <summary>
    /// Gets the localized string for the specified key.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The localized text, or the key itself if not found.</returns>
    public string this[string key] => this.Get(key);

    /// <summary>
    /// Retrieves the localized string for the specified key in the active language.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The localized string.</returns>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        Dictionary<string, string> primary = this._currentLanguage == AppLanguage.EnUs ? s_enUs : s_viVn;
        if (primary.TryGetValue(key, out string? value))
        {
            return value;
        }

        return s_viVn.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    /// <summary>
    /// Switches the active UI language and notifies all listeners.
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
}
