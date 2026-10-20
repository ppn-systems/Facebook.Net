// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Core container and lifecycle primitives of the shared <see cref="UiControls"/> component library.
/// Divided into modular partial files:
/// <list type="bullet">
///   <item><description><c>UiControls.cs</c> — Surface cards, inset wells, list/empty-state hosts, and theme lifecycle binding.</description></item>
///   <item><description><c>UiControls.Buttons.cs</c> — Primary, Secondary, Danger, Sidebar Navigation, Icon, and Info Flyout buttons.</description></item>
///   <item><description><c>UiControls.Typography.cs</c> — Section headers, subsection headers, and themed labels.</description></item>
///   <item><description><c>UiControls.Inputs.cs</c> — TextBoxes, composite File Picker rows, labeled form fields, and numeric delay rows.</description></item>
///   <item><description><c>UiControls.Badges.cs</c> — Status pills, SVG icon metric pills, monogram avatars, KPI stat cards, and empty-state banners.</description></item>
///   <item><description><c>UiControls.Cards.cs</c> — Reusable entity item cards for Accounts, Group Catalog, Target Groups, Lead Posts, and Audit Logs.</description></item>
/// </list>
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
            Padding = padding ?? new Thickness(14),
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
    /// Wraps a <see cref="ListBox"/> and an empty-state <see cref="Border"/> into a unified host <see cref="Grid"/>.
    /// </summary>
    public static Grid CreateListWithEmptyStateHost(ListBox listBox, Border emptyStateBanner)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(emptyStateBanner);

        Grid host = new();
        host.Children.Add(listBox);
        host.Children.Add(emptyStateBanner);
        return host;
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
}
