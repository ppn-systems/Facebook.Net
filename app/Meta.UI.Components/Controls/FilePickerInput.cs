// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Standalone 2-column File Picker input row (<c>*,Auto</c>) containing a styled <see cref="TextBox"/>
/// and a secondary Browse button with the <see cref="VectorIcons.Folder"/> SVG icon.
/// </summary>
public static class FilePickerInput
{
    /// <summary>
    /// Creates a composite file path textbox and browse button row.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        string placeholder,
        string initialPath,
        string browseButtonLabel,
        out TextBox pathTextBox,
        out Button browseButton)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBox box = FormField.CreateTextBox(theme, placeholder, initialPath);
        Button btn = AppButton.CreateSecondary(theme, browseButtonLabel, iconSvg: VectorIcons.Folder);

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
}
