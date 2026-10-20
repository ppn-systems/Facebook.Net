// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone SVG icon + counter badge pill component.
/// </summary>
public static class IconMetricPill
{
    /// <summary>
    /// Creates a status/metric pill combining an SVG icon from <c>Assets/Icons/</c> and a text/number label.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        string iconSvg,
        string initialText,
        AuditSeverity severity,
        out TextBlock valueTextBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Control icon = VectorIcons.Create(iconSvg, 12, margin: new Thickness(0));
        TextBlock label = new()
        {
            Text = initialText,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(label, 1);
        row.Children.Add(icon);
        row.Children.Add(label);

        Border pill = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = row
        };

        AuditSeverity currentSeverity = severity;

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            (IBrush fill, IBrush border, IBrush fg) = currentSeverity switch
            {
                AuditSeverity.Success => (p.SuccessFill, p.SuccessBorder, p.SuccessText),
                AuditSeverity.Warning => (p.WarningFill, p.WarningBorder, p.WarningText),
                AuditSeverity.Error => (p.DangerFill, p.DangerBorder, p.DangerText),
                AuditSeverity.Info => (p.InfoFill, p.InfoBorder, p.InfoText),
                _ => (p.InfoFill, p.InfoBorder, p.InfoText)
            };

            pill.Background = fill;
            pill.BorderBrush = border;
            VectorIcons.SetForeground(icon, fg);
            label.Foreground = fg;
        }

        pill.Tag = (Action<AuditSeverity>)(nextSeverity =>
        {
            currentSeverity = nextSeverity;
            ApplyColors();
        });

        ApplyColors();
        ThemeLifecycle.Bind(pill, theme, ApplyColors);

        valueTextBlock = label;
        return pill;
    }

    /// <summary>
    /// Dynamically updates the semantic severity color of a pill created by <see cref="Create"/>.
    /// </summary>
    public static void SetSeverity(Border pill, AuditSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(pill);
        if (pill.Tag is Action<AuditSeverity> setter)
        {
            setter(severity);
        }
    }
}
