// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Standalone empty-state banner component with SVG icon and localized guidance text.
/// </summary>
public static class EmptyStateBanner
{
    /// <summary>
    /// Creates an empty-state placeholder banner when a list has 0 items.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        string initialMessage,
        string iconSvg,
        out TextBlock messageBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Control icon = VectorIcons.Create(iconSvg, 22, margin: new Thickness(0));
        TextBlock msg = new()
        {
            Text = initialMessage,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        StackPanel stack = new()
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(icon);
        stack.Children.Add(msg);

        Border box = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 18),
            Child = stack
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            box.Background = p.SecondarySurface;
            box.BorderBrush = p.SubtleBorder;
            VectorIcons.SetForeground(icon, p.BrandPrimary);
            msg.Foreground = p.TextSecondary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(box, theme, ApplyColors);

        messageBlock = msg;
        return box;
    }

    /// <summary>
    /// Wraps a <see cref="ListBox"/> and an <see cref="EmptyStateBanner"/> inside a single host <see cref="Grid"/>.
    /// </summary>
    public static Grid CreateHost(ListBox listBox, Border emptyStateBanner)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(emptyStateBanner);

        Grid host = new();
        host.Children.Add(emptyStateBanner);
        host.Children.Add(listBox);
        return host;
    }
}
