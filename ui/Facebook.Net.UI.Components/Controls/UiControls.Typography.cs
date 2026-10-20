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
/// Typography and header components of <see cref="UiControls"/>:
/// Page section headers with SVG `i` flyouts, subsection headers with SVG icons, and theme-aware labels.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates a compact section header with a primary title and a clickable SVG Info (`i`) icon button
    /// that opens a clean popover Flyout when clicked instead of cluttering the page with multi-line explanations.
    /// </summary>
    public static Grid CreateSectionHeader(
        ThemeService theme,
        string initialTitle,
        string initialInfoText,
        out TextBlock titleBlock,
        out TextBlock infoFlyoutBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Button infoBtn = CreateInfoFlyoutButton(theme, initialInfoText, out TextBlock flyoutText);

        Grid headerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(infoBtn, 1);
        headerRow.Children.Add(title);
        headerRow.Children.Add(infoBtn);

        void ApplyColors() => title.Foreground = theme.Palette.TextPrimary;

        ApplyColors();
        BindThemeLifecycle(headerRow, theme, ApplyColors);

        titleBlock = title;
        infoFlyoutBlock = flyoutText;
        return headerRow;
    }

    /// <summary>
    /// Creates a compact subsection header with an SVG icon and a bold label.
    /// </summary>
    public static StackPanel CreateSubSectionHeader(
        ThemeService theme,
        string initialTitle,
        string iconSvg,
        out TextBlock titleBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        PathIcon icon = VectorIcons.Create(iconSvg, 15, margin: new Thickness(0));
        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(title);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            icon.Foreground = p.BrandPrimary;
            title.Foreground = p.TextPrimary;
        }

        ApplyColors();
        BindThemeLifecycle(row, theme, ApplyColors);

        titleBlock = title;
        return row;
    }

    /// <summary>
    /// Creates a <see cref="TextBlock"/> whose foreground color automatically updates when <see cref="ThemeService"/> switches between Dark and Light mode.
    /// </summary>
    public static TextBlock CreateThemedLabel(
        ThemeService theme,
        string initialText,
        double fontSize = 13,
        FontWeight fontWeight = FontWeight.Normal,
        bool isSecondary = false,
        bool isBrand = false)
    {
        ArgumentNullException.ThrowIfNull(theme);
        TextBlock tb = new()
        {
            Text = initialText,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            tb.Foreground = isBrand
                ? p.BrandPrimary
                : (isSecondary ? p.TextSecondary : p.TextPrimary);
        }

        ApplyColors();
        BindThemeLifecycle(tb, theme, ApplyColors);
        return tb;
    }
}
