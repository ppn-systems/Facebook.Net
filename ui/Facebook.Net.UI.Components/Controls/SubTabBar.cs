// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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
/// Descriptor for a single tab item in <see cref="SubTabBar"/>.
/// Supports both eager <see cref="Control"/> instances and lazy <see cref="Func{TResult}"/> factories.
/// </summary>
public sealed class SubTabItem
{
    private Control? _content;
    private readonly Func<Control>? _contentFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubTabItem"/> class with an eagerly constructed view.
    /// </summary>
    public SubTabItem(string title, string iconSvg, Control content)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(iconSvg);
        ArgumentNullException.ThrowIfNull(content);
        this.Title = title;
        this.IconSvg = iconSvg;
        this._content = content;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubTabItem"/> class with a lazy factory invoked only when the tab is first selected.
    /// </summary>
    public SubTabItem(string title, string iconSvg, Func<Control> contentFactory)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(iconSvg);
        ArgumentNullException.ThrowIfNull(contentFactory);
        this.Title = title;
        this.IconSvg = iconSvg;
        this._contentFactory = contentFactory;
    }

    /// <summary>Gets the initial localized tab title.</summary>
    public string Title { get; }

    /// <summary>Gets the SVG icon filename from <see cref="VectorIcons"/>.</summary>
    public string IconSvg { get; }

    /// <summary>Gets or lazily creates the <see cref="Control"/> view displayed when this sub-tab is active.</summary>
    public Control Content => this._content ??= this._contentFactory!();
}

/// <summary>
/// Horizontal Top Pill Sub-Navigation Bar (<c>[ 🗪 Tab 1 ]   ✉ Tab 2   🔔 Tab 3</c>)
/// that cleanly separates multiple sub-features of a page into independent views.
/// </summary>
public sealed class SubTabBar : Grid
{
    private readonly ThemeService _theme;
    private readonly List<(Button Button, TextBlock Label, Control Icon, SubTabItem Spec)> _tabs = new();
    private readonly ContentControl _viewHost;
    private int _selectedIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubTabBar"/> class.
    /// </summary>
    public SubTabBar(ThemeService theme, IReadOnlyList<SubTabItem> items, int initialIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one sub-tab item is required.", nameof(items));
        }

        this._theme = theme;
        this.RowDefinitions = new RowDefinitions("Auto,*");
        this.RowSpacing = 12;

        StackPanel tabStripStack = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (int i = 0; i < items.Count; i++)
        {
            int capturedIndex = i;
            SubTabItem spec = items[i];

            Control icon = VectorIcons.Create(spec.IconSvg, 14, margin: new Thickness(0));
            TextBlock lbl = new()
            {
                Text = spec.Title,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel btnContent = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, lbl }
            };

            Button tabBtn = new()
            {
                Content = btnContent,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(tabBtn, spec.Title);
            ToolTip.SetTip(tabBtn, spec.Title);

            tabBtn.Click += (_, _) => this.SelectTab(capturedIndex);

            this._tabs.Add((tabBtn, lbl, icon, spec));
            tabStripStack.Children.Add(tabBtn);
        }

        ScrollViewer tabScroll = new()
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = tabStripStack
        };

        Grid topStripGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,330")
        };
        SetColumn(tabScroll, 0);
        topStripGrid.Children.Add(tabScroll);

        Border topStripBorder = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2, 0, 2, 10),
            Child = topStripGrid
        };

        this._viewHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        SetRow(topStripBorder, 0);
        SetRow(this._viewHost, 1);
        this.Children.Add(topStripBorder);
        this.Children.Add(this._viewHost);

        void ApplyColors()
        {
            ThemePalette p = this._theme.Palette;
            topStripBorder.BorderBrush = p.SubtleBorder;
            this.RefreshTabVisuals();
        }

        this.SelectTab(Math.Clamp(initialIndex, 0, items.Count - 1));
        ApplyColors();
        ThemeLifecycle.Bind(this, theme, ApplyColors);
    }

    /// <summary>
    /// Occurs when the active sub-tab index changes.
    /// </summary>
    public event EventHandler<int>? SelectedIndexChanged;

    /// <summary>Gets the zero-based index of the currently active sub-tab.</summary>
    public int SelectedIndex => this._selectedIndex;

    /// <summary>
    /// Switches the active sub-tab to <paramref name="index"/>.
    /// </summary>
    public void SelectTab(int index)
    {
        if (index < 0 || index >= this._tabs.Count)
        {
            return;
        }

        this._selectedIndex = index;
        this._viewHost.Content = this._tabs[index].Spec.Content;
        this.RefreshTabVisuals();
        this.SelectedIndexChanged?.Invoke(this, index);
    }

    /// <summary>
    /// Updates the localized titles of the sub-tabs.
    /// </summary>
    public void UpdateTabTitles(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        int count = Math.Min(this._tabs.Count, titles.Count);
        for (int i = 0; i < count; i++)
        {
            this._tabs[i].Label.Text = titles[i];
            AutomationProperties.SetName(this._tabs[i].Button, titles[i]);
            ToolTip.SetTip(this._tabs[i].Button, titles[i]);
        }
    }

    private void RefreshTabVisuals()
    {
        ThemePalette p = this._theme.Palette;
        for (int i = 0; i < this._tabs.Count; i++)
        {
            (Button btn, TextBlock lbl, Control icon, _) = this._tabs[i];
            bool active = i == this._selectedIndex;

            btn.Background = active ? p.BrandPrimary : p.SecondarySurface;
            btn.BorderBrush = active ? p.BrandPrimary : p.SubtleBorder;
            lbl.Foreground = active ? Brushes.White : p.TextPrimary;
            lbl.FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium;
            VectorIcons.SetForeground(icon, active ? Brushes.White : p.TextSecondary);
        }
    }
}
