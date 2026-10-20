// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Button components of <see cref="UiControls"/>: Primary CTA, Secondary Outlined, Danger/Destructive,
/// Sidebar Navigation Item, Icon Button, and SVG Info (`i`) Flyout Button.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates a primary Call-To-Action button in brand orange (`#F6821F`) with an SVG icon.
    /// </summary>
    public static Button CreatePrimaryButton(
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
            FontSize = 12.5,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Control buttonContent = label;
        if (!string.IsNullOrWhiteSpace(iconSvg))
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6
            };
            row.Children.Add(VectorIcons.Create(iconSvg, 14, Brushes.White, new Thickness(0)));
            row.Children.Add(label);
            buttonContent = row;
        }

        Button btn = new()
        {
            Content = buttonContent,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Focusable = true
        };
        AutomationProperties.SetName(btn, initialText);
        ToolTip.SetTip(btn, initialText);

        void ApplyColors() => btn.Background = theme.Palette.BrandPrimary;

        ApplyColors();
        BindThemeLifecycle(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a secondary outlined button with an SVG icon that adapts to Dark and Light mode.
    /// </summary>
    public static Button CreateSecondaryButton(
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
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Control? icon = !string.IsNullOrWhiteSpace(iconSvg)
            ? VectorIcons.Create(iconSvg, 14, margin: new Thickness(0))
            : null;

        Control buttonContent = label;
        if (icon is not null)
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6
            };
            row.Children.Add(icon);
            row.Children.Add(label);
            buttonContent = row;
        }

        Button btn = new()
        {
            Content = buttonContent,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1),
            Focusable = true
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
        BindThemeLifecycle(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a subtle destructive / remove action button with danger border and SVG icon.
    /// </summary>
    public static Button CreateDangerButton(
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
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        string effectiveSvg = string.IsNullOrWhiteSpace(iconSvg) ? VectorIcons.Trash : iconSvg;
        Control icon = VectorIcons.Create(effectiveSvg, 14, margin: new Thickness(0));

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6
        };
        row.Children.Add(icon);
        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1),
            Focusable = true
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
        BindThemeLifecycle(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a compact square SVG icon button (used for sidebar collapse toggle or toolbar actions).
    /// </summary>
    public static Button CreateIconButton(
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
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Command = command,
            Content = icon,
            Focusable = true
        };
        AutomationProperties.SetName(btn, tooltipText);
        ToolTip.SetTip(btn, tooltipText);

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            btn.Background = p.SecondarySurface;
            btn.BorderBrush = p.SubtleBorder;
            VectorIcons.SetForeground(icon, p.TextSecondary);
        }

        ApplyColors();
        BindThemeLifecycle(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Creates a reusable Sidebar Navigation Button with an SVG icon and text label.
    /// </summary>
    public static Button CreateSidebarNavButton(
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
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(label);

        Button btn = new()
        {
            Content = row,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            Focusable = true
        };
        AutomationProperties.SetName(btn, initialLabel);
        ToolTip.SetTip(btn, initialLabel);

        labelBlock = label;
        iconControl = icon;
        return btn;
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

        Control infoIcon = VectorIcons.Create(VectorIcons.InfoCircle, 15, margin: new Thickness(0));

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
            VectorIcons.SetForeground(infoIcon, p.BrandPrimary);
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
    /// Updates the text inside a button created by <see cref="CreatePrimaryButton"/>, <see cref="CreateSecondaryButton"/>, or <see cref="CreateDangerButton"/>.
    /// </summary>
    public static void SetButtonText(Button button, string text)
    {
        ArgumentNullException.ThrowIfNull(button);
        AutomationProperties.SetName(button, text);
        ToolTip.SetTip(button, text);
        if (button.Content is TextBlock tb)
        {
            tb.Text = text;
        }
        else if (button.Content is StackPanel sp)
        {
            for (int i = 0; i < sp.Children.Count; i++)
            {
                if (sp.Children[i] is TextBlock innerTb)
                {
                    innerTb.Text = text;
                    break;
                }
            }
        }
        else
        {
            button.Content = text;
        }
    }
}
