// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Manages automatic subscription and cleanup of <see cref="ThemeService.ThemeChanged"/> handlers
/// when a <see cref="Control"/> attaches to or detaches from the Avalonia visual tree.
/// </summary>
public static class ThemeLifecycle
{
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
}
