// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone button component factory (Primary, Secondary, Danger, IconOnly, SidebarNav, and InfoFlyout).
/// All icons are loaded exclusively from <c>Assets/Icons/*.svg</c>.
/// </summary>
public static class AppButton
{
    /// <summary>
    /// Creates a primary action button accented with <see cref="ThemePalette.BrandPrimary"/>.
    /// </summary>
    public static Button CreatePrimary(
        ThemeService theme,
        string initialText,
        ICommand? command = null,
        string? iconSvg = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock label = new()
        {
            Text = initialText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Control? icon = !string.IsNullOrWhiteSpace(iconSvg)
            ? VectorIcons.Create(iconSvg, 15, Brushes.White, new Thickness(0))
            : null;

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (icon is not null)
        {
            row.Children.Add(icon);
        }

        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(btn, initialText);
        ToolTip.SetTip(btn, initialText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.BrandPrimary;
            btn.Foreground = Brushes.White;
        }

        ApplyColors();
        ThemeLifecycle.Bind(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a secondary action button with subtle border and SVG icon.
    /// </summary>
    public static Button CreateSecondary(
        ThemeService theme,
        string initialText,
        ICommand? command = null,
        string? iconSvg = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock label = new()
        {
            Text = initialText,
            FontWeight = FontWeight.Medium,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Control? icon = !string.IsNullOrWhiteSpace(iconSvg)
            ? VectorIcons.Create(iconSvg, 14, margin: new Thickness(0))
            : null;

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (icon is not null)
        {
            row.Children.Add(icon);
        }

        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(btn, initialText);
        ToolTip.SetTip(btn, initialText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.SecondarySurface;
            btn.BorderBrush = p.InteractiveBorder;
            label.Foreground = p.TextPrimary;
            if (icon is not null)
            {
                VectorIcons.SetForeground(icon, p.TextSecondary);
            }
        }

        ApplyColors();
        ThemeLifecycle.Bind(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a danger action button (delete / clear / stop) with a red outline and SVG icon.
    /// </summary>
    public static Button CreateDanger(
        ThemeService theme,
        string initialText,
        ICommand? command = null,
        string? iconSvg = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock label = new()
        {
            Text = initialText,
            FontWeight = FontWeight.Medium,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        string effectiveSvg = string.IsNullOrWhiteSpace(iconSvg) ? VectorIcons.Trash : iconSvg;
        Control icon = VectorIcons.Create(effectiveSvg, 14, margin: new Thickness(0));

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(btn, initialText);
        ToolTip.SetTip(btn, initialText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.DangerFill;
            btn.BorderBrush = p.DangerBorder;
            label.Foreground = p.DangerText;
            VectorIcons.SetForeground(icon, p.DangerText);
        }

        ApplyColors();
        ThemeLifecycle.Bind(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a compact square SVG icon button.
    /// </summary>
    public static Button CreateIcon(
        ThemeService theme,
        string iconSvg,
        string tooltipText,
        ICommand? command = null,
        double size = 32,
        double iconSize = 16)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Control icon = VectorIcons.Create(iconSvg, iconSize, margin: new Thickness(0));
        Button btn = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Command = command,
            Content = icon,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(btn, tooltipText);
        AutomationProperties.SetName(btn, tooltipText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.SecondarySurface;
            btn.BorderBrush = p.SubtleBorder;
            VectorIcons.SetForeground(icon, p.TextSecondary);
        }

        ApplyColors();
        ThemeLifecycle.Bind(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a sidebar navigation button with an SVG icon and text label that supports collapsed mode.
    /// </summary>
    public static Button CreateSidebarNav(
        string initialLabel,
        string iconSvg,
        ICommand command,
        out TextBlock labelBlock,
        out Control iconControl)
    {
        Control icon = VectorIcons.Create(iconSvg, 17, margin: new Thickness(0));
        TextBlock label = new()
        {
            Text = initialLabel,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 9),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(btn, initialLabel);
        ToolTip.SetTip(btn, initialLabel);

        labelBlock = label;
        iconControl = icon;
        return btn;
    }

    /// <summary>
    /// Creates a compact circular <c>i</c> (<see cref="VectorIcons.InfoCircle"/>) icon button that opens a
    /// themed <see cref="Flyout"/> popup when clicked.
    /// </summary>
    public static Button CreateInfoFlyout(
        ThemeService theme,
        string initialInfoText,
        out TextBlock infoTextBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock infoBody = new()
        {
            Text = initialInfoText,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
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

        Control infoIcon = VectorIcons.Create(VectorIcons.InfoCircle, 16, margin: new Thickness(0));

        Button btn = new()
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = infoIcon,
            Flyout = flyout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(btn, "Info");

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.BrandSubtleFill;
            VectorIcons.SetForeground(infoIcon, p.BrandPrimary);
            flyoutCard.Background = p.ElevatedSurface;
            flyoutCard.BorderBrush = p.InteractiveBorder;
            infoBody.Foreground = p.TextPrimary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(btn, theme, ApplyColors);

        infoTextBlock = infoBody;
        return btn;
    }

    /// <summary>
    /// Updates the text label inside a button created by <see cref="AppButton"/>.
    /// </summary>
    public static void SetText(Button btn, string newText)
    {
        ArgumentNullException.ThrowIfNull(btn);

        if (btn.Content is StackPanel sp)
        {
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] is TextBlock tb)
                {
                    tb.Text = newText;
                    break;
                }
            }
        }
        else if (btn.Content is TextBlock directTb)
        {
            directTb.Text = newText;
        }

        AutomationProperties.SetName(btn, newText);
        ToolTip.SetTip(btn, newText);
    }
}
