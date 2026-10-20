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
/// Reusable UI component factory (`CreateCard`, `CreatePrimaryButton`, `CreateStatusPill`, `CreateTextBox`, `CreateSectionHeader`)
/// that automatically reacts to Dark/Light theme changes via <see cref="ThemeService"/>.
/// Split into partial files for core containers/buttons and entity cards/pills.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates an elevated surface card with a crisp 1px border (`#E2E8F0` light / `#27272A` dark)
    /// and 10px rounded corners. Automatically updates colors when <see cref="ThemeService"/> toggles.
    /// </summary>
    public static Border CreateCard(ThemeService theme, Control content, Thickness? padding = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);

        Border card = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(18),
            Child = content
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            card.Background = p.ElevatedSurface;
            card.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        BindThemeLifecycle(card, theme, ApplyColors);
        return card;
    }

    /// <summary>
    /// Creates a secondary inset container (well) for lists, code snippets, or filter bars.
    /// </summary>
    public static Border CreateInsetWell(ThemeService theme, Control content, Thickness? padding = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(content);

        Border well = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(12),
            Child = content
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            well.Background = p.SecondarySurface;
            well.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        BindThemeLifecycle(well, theme, ApplyColors);
        return well;
    }

    /// <summary>
    /// Creates a primary Call-To-Action button in brand orange (`#F6821F`) with an optional vector icon.
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
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
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
            row.Children.Add(VectorIcons.Create(iconSvg, 15, Brushes.White, new Thickness(0)));
            row.Children.Add(label);
            buttonContent = row;
        }

        Button btn = new()
        {
            Content = buttonContent,
            Command = command,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 9),
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
    /// Creates a secondary outlined button that adapts to Dark and Light mode.
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
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };

        PathIcon? icon = !string.IsNullOrWhiteSpace(iconSvg)
            ? VectorIcons.Create(iconSvg, 15, margin: new Thickness(0))
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
            Padding = new Thickness(12, 8),
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
            icon?.Foreground = p.TextSecondary;
        }

        ApplyColors();
        BindThemeLifecycle(btn, theme, ApplyColors);
        return btn;
    }

    /// <summary>
    /// Attaches a theme change listener to a control and automatically unsubscribes when the control detaches
    /// from the visual tree (and re-subscribes if re-attached), eliminating event handler memory leaks.
    /// </summary>
    public static void BindThemeLifecycle(Visual control, ThemeService theme, Action applyColors)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(applyColors);

        bool subscribed = false;
        void OnThemeChanged(object? sender, AppThemeMode mode) => applyColors();

        void Subscribe()
        {
            if (!subscribed)
            {
                theme.ThemeChanged += OnThemeChanged;
                subscribed = true;
            }
        }

        void Unsubscribe()
        {
            if (subscribed)
            {
                theme.ThemeChanged -= OnThemeChanged;
                subscribed = false;
            }
        }

        Subscribe();
        control.AttachedToVisualTree += (_, _) =>
        {
            Subscribe();
            applyColors();
        };
        control.DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    /// <summary>
    /// Updates the text inside a button created by <see cref="CreatePrimaryButton"/> or <see cref="CreateSecondaryButton"/>.
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
