// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone 4-column Min/Max Delay range selector row (<c>Auto,*,Auto,*</c>).
/// </summary>
public static class DelayRangeSelector
{
    /// <summary>
    /// Creates a Min/Max delay seconds selector row.
    /// </summary>
    public static Grid Create(
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

        TextBlock minPrefix = SectionHeader.CreateLabel(theme, initialMinLabel, 12);
        NumericUpDown minBox = new()
        {
            Minimum = 1,
            Maximum = 600,
            Increment = 5,
            Value = initialMinSeconds,
            FormatString = "0",
            Width = 130,
            CornerRadius = new Avalonia.CornerRadius(8)
        };

        TextBlock maxPrefix = SectionHeader.CreateLabel(theme, initialMaxLabel, 12);
        NumericUpDown maxBox = new()
        {
            Minimum = 1,
            Maximum = 1200,
            Increment = 5,
            Value = initialMaxSeconds,
            FormatString = "0",
            Width = 130,
            CornerRadius = new Avalonia.CornerRadius(8)
        };

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*"),
            ColumnSpacing = 12
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
