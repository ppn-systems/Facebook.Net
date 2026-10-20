// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Badges, pills, avatars, KPI cards, and empty-state banners of <see cref="UiControls"/>.
/// All icons are rendered strictly from `.svg` files via <see cref="VectorIcons"/>.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates a semantic Status Pill (`Success`, `Warning`, `Error`, `Info`) with a 6px dot and 1px tinted border.
    /// </summary>
    public static Border CreateStatusPill(ThemeService theme, string text, AuditSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Ellipse dot = new()
        {
            Width = 6,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock label = new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(dot);
        row.Children.Add(label);

        Border pill = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = severity,
            Child = row
        };
        AutomationProperties.SetName(pill, text);
        ToolTip.SetTip(pill, text);

        void ApplyColors()
        {
            AuditSeverity activeSeverity = pill.Tag is AuditSeverity s ? s : severity;
            ApplyPillColors(pill, dot, label, theme.Palette, activeSeverity);
        }

        ApplyColors();
        BindThemeLifecycle(pill, theme, ApplyColors);
        return pill;
    }

    /// <summary>
    /// Creates a semantic pill featuring an SVG icon (loaded from <c>Assets/Icons/*.svg</c>) and a metric/label string.
    /// Replaces raw Unicode symbols (`✔`, `⚠`, `✖`, `👍`, `💬`) with crisp vector SVG icons.
    /// </summary>
    public static Border CreateIconMetricPill(
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
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(label);

        Border pill = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row
        };
        AutomationProperties.SetName(pill, initialText);
        ToolTip.SetTip(pill, initialText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            switch (severity)
            {
                case AuditSeverity.Success:
                    pill.Background = p.SuccessFill;
                    pill.BorderBrush = p.SuccessBorder;
                    label.Foreground = p.SuccessText;
                    VectorIcons.SetForeground(icon, p.SuccessText);
                    break;
                case AuditSeverity.Warning:
                    pill.Background = p.WarningFill;
                    pill.BorderBrush = p.WarningBorder;
                    label.Foreground = p.WarningText;
                    VectorIcons.SetForeground(icon, p.WarningText);
                    break;
                case AuditSeverity.Error:
                    pill.Background = p.DangerFill;
                    pill.BorderBrush = p.DangerBorder;
                    label.Foreground = p.DangerText;
                    VectorIcons.SetForeground(icon, p.DangerText);
                    break;
                case AuditSeverity.Info:
                default:
                    pill.Background = p.InfoFill;
                    pill.BorderBrush = p.InfoBorder;
                    label.Foreground = p.InfoText;
                    VectorIcons.SetForeground(icon, p.InfoText);
                    break;
            }
        }

        ApplyColors();
        BindThemeLifecycle(pill, theme, ApplyColors);
        valueTextBlock = label;
        return pill;
    }

    private static void ApplyPillColors(Border pill, Ellipse dot, TextBlock label, ThemePalette p, AuditSeverity severity)
    {
        switch (severity)
        {
            case AuditSeverity.Success:
                pill.Background = p.SuccessFill;
                pill.BorderBrush = p.SuccessBorder;
                label.Foreground = p.SuccessText;
                dot.Fill = p.SuccessText;
                break;
            case AuditSeverity.Warning:
                pill.Background = p.WarningFill;
                pill.BorderBrush = p.WarningBorder;
                label.Foreground = p.WarningText;
                dot.Fill = p.WarningText;
                break;
            case AuditSeverity.Error:
                pill.Background = p.DangerFill;
                pill.BorderBrush = p.DangerBorder;
                label.Foreground = p.DangerText;
                dot.Fill = p.DangerText;
                break;
            case AuditSeverity.Info:
            default:
                pill.Background = p.InfoFill;
                pill.BorderBrush = p.InfoBorder;
                label.Foreground = p.InfoText;
                dot.Fill = p.InfoText;
                break;
        }
    }

    /// <summary>
    /// Updates the text and optionally the severity inside a status pill created by <see cref="CreateStatusPill"/>.
    /// </summary>
    public static void SetPillText(Border pill, string text, ThemeService? theme = null, AuditSeverity? newSeverity = null)
    {
        ArgumentNullException.ThrowIfNull(pill);
        AutomationProperties.SetName(pill, text);
        ToolTip.SetTip(pill, text);
        if (newSeverity.HasValue)
        {
            pill.Tag = newSeverity.Value;
        }

        if (pill.Child is StackPanel row)
        {
            Ellipse? dot = null;
            TextBlock? label = null;
            for (int i = 0; i < row.Children.Count; i++)
            {
                if (row.Children[i] is Ellipse el)
                {
                    dot = el;
                }
                else if (row.Children[i] is TextBlock tb)
                {
                    tb.Text = text;
                    label = tb;
                }
            }

            if (theme is not null && dot is not null && label is not null && pill.Tag is AuditSeverity sev)
            {
                ApplyPillColors(pill, dot, label, theme.Palette, sev);
            }
        }
    }

    /// <summary>
    /// Creates a circular avatar monogram badge (e.g., first 2 letters of Account or Group name)
    /// so entities are visually identifiable without showing raw numeric UIDs.
    /// </summary>
    public static Border CreateAvatarBadge(ThemeService theme, string displayName, double size = 32)
    {
        ArgumentNullException.ThrowIfNull(theme);

        string initials = ExtractInitials(displayName);
        TextBlock text = new()
        {
            Text = initials,
            FontSize = size * 0.38,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border circle = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1),
            Child = text
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            circle.Background = p.BrandSubtleFill;
            circle.BorderBrush = p.BrandSubtleBorder;
            text.Foreground = p.BrandPrimary;
        }

        ApplyColors();
        BindThemeLifecycle(circle, theme, ApplyColors);
        return circle;
    }

    /// <summary>
    /// Creates a KPI Stat Card displaying a label, a large monospace/tabular metric value,
    /// and a vector icon badge.
    /// </summary>
    public static Border CreateKpiStatCard(
        ThemeService theme,
        string initialTitle,
        string initialValue,
        string iconSvg,
        out TextBlock titleBlock,
        out TextBlock valueBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock title = new()
        {
            Text = initialTitle,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock val = new()
        {
            Text = initialValue,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace")
        };

        Control icon = VectorIcons.Create(iconSvg, 16, margin: new Thickness(0));
        Border iconBadge = new()
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon
        };

        StackPanel textStack = new()
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(title);
        textStack.Children.Add(val);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(iconBadge, 1);
        row.Children.Add(textStack);
        row.Children.Add(iconBadge);

        Border card = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12),
            Child = row
        };
        AutomationProperties.SetName(card, initialTitle);
        ToolTip.SetTip(card, initialTitle);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            card.Background = p.ElevatedSurface;
            card.BorderBrush = p.SubtleBorder;
            title.Foreground = p.TextSecondary;
            val.Foreground = p.TextPrimary;
            iconBadge.Background = p.BrandSubtleFill;
            iconBadge.BorderBrush = p.BrandSubtleBorder;
            VectorIcons.SetForeground(icon, p.BrandPrimary);
        }

        ApplyColors();
        BindThemeLifecycle(card, theme, ApplyColors);
        titleBlock = title;
        valueBlock = val;
        return card;
    }

    /// <summary>
    /// Creates a subtle empty-state banner shown when a list or collection is empty.
    /// </summary>
    public static Border CreateEmptyState(
        ThemeService theme,
        string initialMessage,
        string iconSvg,
        out TextBlock messageBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Control icon = VectorIcons.Create(iconSvg, 18, margin: new Thickness(0));
        TextBlock msg = new()
        {
            Text = initialMessage,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(msg);

        Border box = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10),
            Child = row
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
        BindThemeLifecycle(box, theme, ApplyColors);
        messageBlock = msg;
        return box;
    }

    private static string ExtractInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "FB";
        }

        string trimmed = name.Trim();
        string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0]));
        }

        return trimmed.Length >= 2
            ? trimmed[..2].ToUpperInvariant()
            : trimmed.ToUpperInvariant();
    }
}
