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
/// Partial implementation of <see cref="UiControls"/> providing status pills, avatar chips,
/// compact section headers with click-to-view SVG Info (`i`) flyouts, and styled text inputs.
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
    /// Creates a compact circular SVG `i` (Info) button that displays an explanatory Flyout card when clicked.
    /// </summary>
    public static Button CreateInfoFlyoutButton(
        ThemeService theme,
        string initialInfoText,
        out TextBlock flyoutTextBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock infoBody = new()
        {
            Text = initialInfoText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            LineHeight = 18
        };

        Border flyoutCard = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10),
            Child = infoBody
        };

        Flyout flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = flyoutCard
        };

        PathIcon infoIcon = VectorIcons.Create(VectorIcons.InfoCircle, 15, margin: new Thickness(0));

        Button btn = new()
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = infoIcon,
            Flyout = flyout,
            Focusable = true
        };
        AutomationProperties.SetName(btn, initialInfoText);
        ToolTip.SetTip(btn, initialInfoText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.SecondarySurface;
            btn.BorderBrush = p.SubtleBorder;
            infoIcon.Foreground = p.BrandPrimary;
            flyoutCard.Background = p.ElevatedSurface;
            flyoutCard.BorderBrush = p.InteractiveBorder;
            infoBody.Foreground = p.TextPrimary;
        }

        ApplyColors();
        BindThemeLifecycle(btn, theme, ApplyColors);
        flyoutTextBlock = infoBody;
        return btn;
    }

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

        PathIcon icon = VectorIcons.Create(iconSvg, 16, margin: new Thickness(0));
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
            icon.Foreground = p.BrandPrimary;
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

        PathIcon icon = VectorIcons.Create(iconSvg, 18, margin: new Thickness(0));
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
            icon.Foreground = p.BrandPrimary;
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
