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
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Segmented status pill selector (<c>[ Đang mở | Đã đóng | Tất cả ]</c>) placed in the top-right header of a Filter Card.
/// </summary>
public sealed class SegmentedControl : Border
{
    private readonly ThemeService _theme;
    private readonly List<(Button Button, TextBlock Label)> _segments = new();
    private int _selectedIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentedControl"/> class.
    /// </summary>
    public SegmentedControl(
        ThemeService theme,
        IReadOnlyList<string> labels,
        int initialIndex = 0,
        Action<int>? onSelected = null,
        bool borderless = false)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
        {
            throw new ArgumentException("At least one segment label is required.", nameof(labels));
        }

        this._theme = theme;
        this._selectedIndex = Math.Clamp(initialIndex, 0, labels.Count - 1);

        this.CornerRadius = new CornerRadius(8);
        this.BorderThickness = new Thickness(borderless ? 0 : 1);
        this.Padding = new Thickness(4);
        this.VerticalAlignment = VerticalAlignment.Center;

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2
        };

        for (int i = 0; i < labels.Count; i++)
        {
            int capturedIndex = i;
            TextBlock lbl = new()
            {
                Text = labels[i],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Button btn = new()
            {
                Content = lbl,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            AutomationProperties.SetName(btn, labels[i]);

            btn.Click += (_, _) =>
            {
                if (this._selectedIndex != capturedIndex)
                {
                    this._selectedIndex = capturedIndex;
                    this.RefreshSegmentVisuals();
                    onSelected?.Invoke(capturedIndex);
                }
            };

            this._segments.Add((btn, lbl));
            row.Children.Add(btn);
        }

        this.Child = row;

        void ApplyColors()
        {
            ThemePalette p = this._theme.Palette;
            this.Background = borderless ? p.SecondarySurface : p.ElevatedSurface;
            this.BorderBrush = borderless ? Brushes.Transparent : p.InteractiveBorder;
            this.RefreshSegmentVisuals();
        }

        ApplyColors();
        ThemeLifecycle.Bind(this, theme, ApplyColors);
    }

    /// <summary>
    /// Updates the localized labels of the segments.
    /// </summary>
    public void UpdateLabels(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        int count = Math.Min(this._segments.Count, labels.Count);
        for (int i = 0; i < count; i++)
        {
            this._segments[i].Label.Text = labels[i];
            AutomationProperties.SetName(this._segments[i].Button, labels[i]);
        }
    }

    /// <summary>
    /// Sets the active segment index programmatically without invoking the selection callback.
    /// </summary>
    public void SetSelectedIndex(int index)
    {
        if (this._segments.Count == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, this._segments.Count - 1);
        if (this._selectedIndex != clamped)
        {
            this._selectedIndex = clamped;
            this.RefreshSegmentVisuals();
        }
    }

    private void RefreshSegmentVisuals()
    {
        ThemePalette p = this._theme.Palette;
        for (int i = 0; i < this._segments.Count; i++)
        {
            (Button btn, TextBlock lbl) = this._segments[i];
            bool active = i == this._selectedIndex;

            btn.Background = active ? p.BrandPrimary : Brushes.Transparent;
            lbl.Foreground = active ? Brushes.White : p.TextSecondary;
            lbl.FontWeight = active ? FontWeight.SemiBold : FontWeight.Medium;
        }
    }
}
