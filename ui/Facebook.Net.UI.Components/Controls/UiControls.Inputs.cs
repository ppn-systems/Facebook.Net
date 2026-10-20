// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Form and input components of <see cref="UiControls"/>:
/// Styled TextBoxes, composite File Picker rows, labeled form fields, and numeric delay range selectors.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates a styled <see cref="TextBox"/> with 8px corner radius and subtle border.
    /// </summary>
    public static TextBox CreateTextBox(
        ThemeService theme,
        string placeholder,
        string initialText = "",
        bool multiline = false,
        double? height = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBox box = new()
        {
            PlaceholderText = placeholder,
            Text = initialText,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            FontSize = 13,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Focusable = true
        };
        AutomationProperties.SetName(box, placeholder);
        ToolTip.SetTip(box, placeholder);

        if (height.HasValue)
        {
            box.Height = height.Value;
        }

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            box.Background = p.SecondarySurface;
            box.BorderBrush = p.InteractiveBorder;
            box.Foreground = p.TextPrimary;
        }

        ApplyColors();
        BindThemeLifecycle(box, theme, ApplyColors);
        return box;
    }

    /// <summary>
    /// Creates a reusable 2-column File Picker input row (`*,Auto`) containing a styled <see cref="TextBox"/>
    /// and a secondary Browse button with the <see cref="VectorIcons.Folder"/> SVG icon.
    /// </summary>
    public static Grid CreateFilePickerRow(
        ThemeService theme,
        string placeholder,
        string initialPath,
        string browseButtonLabel,
        out TextBox pathTextBox,
        out Button browseButton)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBox box = CreateTextBox(theme, placeholder, initialPath);
        Button btn = CreateSecondaryButton(theme, browseButtonLabel, iconSvg: VectorIcons.Folder);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(box, 0);
        Grid.SetColumn(btn, 1);
        row.Children.Add(box);
        row.Children.Add(btn);

        pathTextBox = box;
        browseButton = btn;
        return row;
    }

    /// <summary>
    /// Wraps a control with a themed label header inside a vertical <see cref="StackPanel"/>.
    /// </summary>
    public static StackPanel CreateLabeledField(
        ThemeService theme,
        string initialLabelText,
        Control fieldControl,
        out TextBlock labelBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(fieldControl);

        TextBlock lbl = CreateThemedLabel(theme, initialLabelText, 12, FontWeight.SemiBold, isSecondary: true);
        StackPanel stack = new()
        {
            Spacing = 6
        };
        stack.Children.Add(lbl);
        stack.Children.Add(fieldControl);

        labelBlock = lbl;
        return stack;
    }

    /// <summary>
    /// Creates a reusable 4-column Min/Max Delay range selector row (`Auto,*,Auto,*`).
    /// </summary>
    public static Grid CreateDelayRangeRow(
        ThemeService theme,
        string initialMinLabel,
        int initialMinSeconds,
        string initialMaxLabel,
        int initialMaxSeconds,
        out TextBlock minLabelBlock,
        out NumericUpDown minBoxControl,
        out TextBlock maxLabelBlock,
        out NumericUpDown maxBoxControl)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock minPrefix = CreateThemedLabel(theme, initialMinLabel, 12);
        NumericUpDown minBox = new()
        {
            Minimum = 1,
            Maximum = 600,
            Increment = 5,
            Value = initialMinSeconds,
            FormatString = "0"
        };

        TextBlock maxPrefix = CreateThemedLabel(theme, initialMaxLabel, 12);
        NumericUpDown maxBox = new()
        {
            Minimum = 1,
            Maximum = 1200,
            Increment = 5,
            Value = initialMaxSeconds,
            FormatString = "0"
        };

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(minPrefix, 0);
        Grid.SetColumn(minBox, 1);
        Grid.SetColumn(maxPrefix, 2);
        Grid.SetColumn(maxBox, 3);
        row.Children.Add(minPrefix);
        row.Children.Add(minBox);
        row.Children.Add(maxPrefix);
        row.Children.Add(maxBox);

        minLabelBlock = minPrefix;
        minBoxControl = minBox;
        maxLabelBlock = maxPrefix;
        maxBoxControl = maxBox;
        return row;
    }
}
