// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone themed card and inset container component with 1px subtle borders and automatic Dark/Light adaptation.
/// </summary>
public static class SurfaceCard
{
    /// <summary>
    /// Creates an elevated surface card container with 1px subtle border and 10px corner radius.
    /// </summary>
    public static Border Create(ThemeService theme, Control content, Thickness? padding = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);

        Border card = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(16),
            ClipToBounds = true,
            Child = content
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            card.Background = p.ElevatedSurface;
            card.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(card, theme, ApplyColors);
        return card;
    }

    /// <summary>
    /// Creates a secondary inset container well with 1px subtle border and 8px corner radius.
    /// </summary>
    public static Border CreateInset(ThemeService theme, Control content, Thickness? padding = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);

        Border well = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(12),
            ClipToBounds = true,
            Child = content
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            well.Background = p.SecondarySurface;
            well.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(well, theme, ApplyColors);
        return well;
    }
}
