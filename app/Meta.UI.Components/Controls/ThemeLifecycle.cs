// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Manages automatic subscription and cleanup of <see cref="ThemeService.ThemeChanged"/> and
/// <see cref="LocalizationService.LanguageChanged"/> handlers when a <see cref="Control"/> attaches to
/// or detaches from the Avalonia visual tree, and provides a shared immutable brush cache.
/// </summary>
public static class ThemeLifecycle
{
    private static readonly ConcurrentDictionary<uint, IImmutableSolidColorBrush> s_brushCache = new();

    /// <summary>
    /// Returns a cached <see cref="IImmutableSolidColorBrush"/> for the specified <paramref name="color"/>,
    /// avoiding repeated per-row <see cref="SolidColorBrush"/> allocations during virtualized scrolling.
    /// </summary>
    public static IImmutableSolidColorBrush GetCachedBrush(Color color) =>
        s_brushCache.GetOrAdd(color.ToUInt32(), static argb => new ImmutableSolidColorBrush(Color.FromUInt32(argb)));

    /// <summary>
    /// Subscribes <paramref name="applyColors"/> to <see cref="ThemeService.ThemeChanged"/> while
    /// <paramref name="control"/> is attached to the visual tree.
    /// </summary>
    public static void Bind(Control control, ThemeService theme, Action applyColors)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(applyColors);

        void OnThemeChanged(object? sender, AppThemeMode mode) => applyColors();
        bool isSubscribed = true;
        theme.ThemeChanged += OnThemeChanged;

        control.AttachedToVisualTree += (_, _) =>
        {
            if (!isSubscribed)
            {
                theme.ThemeChanged += OnThemeChanged;
                isSubscribed = true;
                applyColors();
            }
        };

        control.DetachedFromVisualTree += (_, _) =>
        {
            if (isSubscribed)
            {
                theme.ThemeChanged -= OnThemeChanged;
                isSubscribed = false;
            }
        };
    }

    /// <summary>
    /// Subscribes <paramref name="applyLanguage"/> to <see cref="LocalizationService.LanguageChanged"/> while
    /// <paramref name="control"/> is attached to the visual tree, preventing event handler memory leaks on virtualized rows.
    /// </summary>
    public static void BindLanguage(Control control, LocalizationService i18n, Action applyLanguage)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(applyLanguage);

        void OnLanguageChanged(object? sender, AppLanguage language) => applyLanguage();
        bool isSubscribed = true;
        i18n.LanguageChanged += OnLanguageChanged;

        control.AttachedToVisualTree += (_, _) =>
        {
            if (!isSubscribed)
            {
                i18n.LanguageChanged += OnLanguageChanged;
                isSubscribed = true;
                applyLanguage();
            }
        };

        control.DetachedFromVisualTree += (_, _) =>
        {
            if (isSubscribed)
            {
                i18n.LanguageChanged -= OnLanguageChanged;
                isSubscribed = false;
            }
        };
    }
}
