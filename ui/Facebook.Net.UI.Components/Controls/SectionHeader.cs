// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone section header, sub-header, and themed typography component.
/// </summary>
public static class SectionHeader
{
    /// <summary>
    /// Creates a compact single-line page header with a title and a clickable <c>i</c> (<see cref="VectorIcons.InfoCircle"/>)
    /// button that reveals explanatory text in a popover flyout.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        string initialTitle,
        string initialSubtitle,
        out TextBlock titleBlock,
        out TextBlock subtitleBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Grid headerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button infoBtn = AppButton.CreateInfoFlyout(theme, initialSubtitle, out TextBlock subtitle);

        void ApplyColors() => title.Foreground = theme.Palette.TextPrimary;

        ApplyColors();
        ThemeLifecycle.Bind(headerRow, theme, ApplyColors);

        Grid.SetColumn(title, 0);
        Grid.SetColumn(infoBtn, 1);
        headerRow.Children.Add(title);
        headerRow.Children.Add(infoBtn);

        titleBlock = title;
        subtitleBlock = subtitle;
        return headerRow;
    }

    /// <summary>
    /// Creates a sub-section header row with a leading SVG icon and bold title text.
    /// </summary>
    public static StackPanel CreateSubHeader(
        ThemeService theme,
        string initialTitle,
        string iconSvg,
        out TextBlock titleBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Control icon = VectorIcons.Create(iconSvg, 15, margin: new Thickness(0));
        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(title);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            VectorIcons.SetForeground(icon, p.BrandPrimary);
            title.Foreground = p.TextPrimary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(row, theme, ApplyColors);

        titleBlock = title;
        return row;
    }

    /// <summary>
    /// Creates a reactive <see cref="TextBlock"/> that automatically updates its foreground brush on theme changes.
    /// </summary>
    public static TextBlock CreateLabel(
        ThemeService theme,
        string text,
        double fontSize = 13,
        FontWeight? weight = null,
        bool isSecondary = false)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock tb = new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        void ApplyColors() =>
            tb.Foreground = isSecondary ? theme.Palette.TextSecondary : theme.Palette.TextPrimary;

        ApplyColors();
        ThemeLifecycle.Bind(tb, theme, ApplyColors);
        return tb;
    }
}
