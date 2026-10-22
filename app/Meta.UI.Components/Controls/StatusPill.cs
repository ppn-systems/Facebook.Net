// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Standalone semantic status pill badge (<c>Success</c>, <c>Warning</c>, <c>Error</c>, <c>Info</c>).
/// </summary>
public static class StatusPill
{
    /// <summary>
    /// Creates a status pill badge for account/group/log status without technical enum names.
    /// </summary>
    public static Border Create(ThemeService theme, string text, AuditSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock label = new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Border pill = new()
        {
            CornerRadius = new CornerRadius(8),
            MinHeight = 26,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };

        AuditSeverity currentSeverity = severity;

        void ApplyColors()
        {
            if (pill.Tag is AuditSeverity taggedSeverity)
            {
                currentSeverity = taggedSeverity;
            }

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
            label.Foreground = fg;
        }

        pill.Tag = currentSeverity;
        ApplyColors();
        ThemeLifecycle.Bind(pill, theme, ApplyColors);
        return pill;
    }

    /// <summary>
    /// Updates the severity and refreshes colors on a pill created by <see cref="Create"/>.
    /// </summary>
    public static void SetSeverity(Border pill, ThemeService theme, AuditSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(pill);
        ArgumentNullException.ThrowIfNull(theme);
        pill.Tag = severity;

        ThemePalette p = theme.Palette;
        (IBrush fill, IBrush border, IBrush fg) = severity switch
        {
            AuditSeverity.Success => (p.SuccessFill, p.SuccessBorder, p.SuccessText),
            AuditSeverity.Warning => (p.WarningFill, p.WarningBorder, p.WarningText),
            AuditSeverity.Error => (p.DangerFill, p.DangerBorder, p.DangerText),
            AuditSeverity.Info => (p.InfoFill, p.InfoBorder, p.InfoText),
            _ => (p.InfoFill, p.InfoBorder, p.InfoText)
        };

        pill.Background = fill;
        pill.BorderBrush = border;
        if (pill.Child is TextBlock tb)
        {
            tb.Foreground = fg;
        }
    }

    /// <summary>
    /// Updates the text inside a pill created by <see cref="Create"/> or <see cref="IconMetricPill.Create"/>.
    /// </summary>
    public static void SetText(Border pill, string newText)
    {
        ArgumentNullException.ThrowIfNull(pill);

        if (pill.Child is TextBlock tb)
        {
            tb.Text = newText;
        }
        else if (pill.Child is StackPanel sp)
        {
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] is TextBlock childTb)
                {
                    childTb.Text = newText;
                    break;
                }
            }
        }
    }
}
