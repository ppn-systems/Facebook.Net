// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone KPI metric stat card component with SVG icon badge, metric value, and optional click navigation.
/// </summary>
public static class KpiStatCard
{
    /// <summary>
    /// Creates a KPI metric card with an SVG icon badge, uppercase label, large numeric metric value,
    /// and optional click command to navigate to the corresponding page.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        string initialTitle,
        string initialValue,
        string iconSvg,
        out TextBlock titleBlock,
        out TextBlock valueBlock,
        ICommand? clickCommand = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
        };

        Control icon = VectorIcons.Create(iconSvg, 18, margin: new Thickness(0));
        Border iconBadge = new()
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = icon,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel stack = new()
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock val = new()
        {
            Text = initialValue,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        stack.Children.Add(title);
        stack.Children.Add(val);

        Grid.SetColumn(iconBadge, 0);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(iconBadge);
        grid.Children.Add(stack);

        Border card = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10),
            Child = grid
        };

        if (clickCommand is not null)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerPressed += (_, _) =>
            {
                if (clickCommand.CanExecute(null))
                {
                    clickCommand.Execute(null);
                }
            };
        }

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            card.Background = p.ElevatedSurface;
            card.BorderBrush = p.SubtleBorder;
            iconBadge.Background = p.BrandSubtleFill;
            iconBadge.BorderBrush = p.BrandPrimary;
            VectorIcons.SetForeground(icon, p.BrandPrimary);
            title.Foreground = p.TextSecondary;
            val.Foreground = p.TextPrimary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(card, theme, ApplyColors);

        titleBlock = title;
        valueBlock = val;
        return card;
    }
}
