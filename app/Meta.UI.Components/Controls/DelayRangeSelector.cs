// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Standalone 4-column Min/Max Delay range selector row (<c>Auto,*,Auto,*</c>).
/// Supports fractional seconds (e.g., <c>0,2</c> in <c>vi-VN</c> and <c>0.2</c> in <c>en-US</c>)
/// and normalizes both <c>,</c> and <c>.</c> decimal separator keystrokes automatically.
/// </summary>
public static class DelayRangeSelector
{
    /// <summary>
    /// Creates a Min/Max delay seconds selector row.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        string initialMinLabel,
        double initialMinSeconds,
        string initialMaxLabel,
        double initialMaxSeconds,
        out TextBlock minLabelBlock,
        out NumericUpDown minBoxControl,
        out TextBlock maxLabelBlock,
        out NumericUpDown maxBoxControl,
        ILocalizationService? i18n = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        NumberFormatInfo initialNumberFormat = ResolveNumberFormat(i18n);

        TextBlock minPrefix = SectionHeader.CreateLabel(theme, initialMinLabel, 12);
        NumericUpDown minBox = new()
        {
            Minimum = 0m,
            Maximum = 600m,
            Increment = 0.2m,
            Value = (decimal)Math.Round(initialMinSeconds, 2),
            FormatString = "0.##",
            ParsingNumberStyle = NumberStyles.Float | NumberStyles.AllowThousands,
            NumberFormat = initialNumberFormat,
            Width = 130,
            CornerRadius = new Avalonia.CornerRadius(8)
        };

        TextBlock maxPrefix = SectionHeader.CreateLabel(theme, initialMaxLabel, 12);
        NumericUpDown maxBox = new()
        {
            Minimum = 0m,
            Maximum = 1200m,
            Increment = 0.2m,
            Value = (decimal)Math.Round(initialMaxSeconds, 2),
            FormatString = "0.##",
            ParsingNumberStyle = NumberStyles.Float | NumberStyles.AllowThousands,
            NumberFormat = initialNumberFormat,
            Width = 130,
            CornerRadius = new Avalonia.CornerRadius(8)
        };

        AttachDecimalSeparatorNormalizer(minBox);
        AttachDecimalSeparatorNormalizer(maxBox);

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

        if (i18n is not null)
        {
            ThemeLifecycle.BindLanguage(row, i18n, () =>
            {
                NumberFormatInfo nextFormat = ResolveNumberFormat(i18n);
                minBox.NumberFormat = nextFormat;
                maxBox.NumberFormat = nextFormat;

                // Refresh FormatString so the inner TextBox re-renders with the new decimal separator immediately
                minBox.FormatString = "0.###";
                minBox.FormatString = "0.##";
                maxBox.FormatString = "0.###";
                maxBox.FormatString = "0.##";
            });
        }

        minLabelBlock = minPrefix;
        minBoxControl = minBox;
        maxLabelBlock = maxPrefix;
        maxBoxControl = maxBox;
        return row;
    }

    private static NumberFormatInfo ResolveNumberFormat(ILocalizationService? i18n) =>
        (i18n?.CurrentLanguage ?? AppLanguage.ViVn) == AppLanguage.ViVn
            ? CultureInfo.GetCultureInfo("vi-VN").NumberFormat
            : CultureInfo.GetCultureInfo("en-US").NumberFormat;

    private static void AttachDecimalSeparatorNormalizer(NumericUpDown box)
    {
        box.AddHandler(
            InputElement.TextInputEvent,
            (_, e) =>
            {
                if (e.Text is "." or ",")
                {
                    string sep = box.NumberFormat?.NumberDecimalSeparator ?? ",";
                    if (!string.Equals(e.Text, sep, StringComparison.Ordinal))
                    {
                        e.Text = sep;
                    }
                }
            },
            RoutingStrategies.Tunnel);
    }
}
