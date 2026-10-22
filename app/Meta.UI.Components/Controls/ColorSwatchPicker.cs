// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable circular color swatch selector for <c>GroupTag</c> and <c>LeadTag</c> creation/editing.
/// </summary>
public static class ColorSwatchPicker
{
    /// <summary>
    /// Creates a <see cref="WrapPanel"/> of circular color swatches with active border and optional checkmark.
    /// </summary>
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI checkmark symbol only.")]
    public static WrapPanel Create(
        ThemeService theme,
        IReadOnlyList<string> hexColors,
        Func<string> getSelectedHex,
        Action<string> onColorSelected,
        out Action rebuildSwatches,
        double size = 32,
        double spacing = 10,
        bool showCheckmark = true)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(hexColors);
        ArgumentNullException.ThrowIfNull(getSelectedHex);
        ArgumentNullException.ThrowIfNull(onColorSelected);

        WrapPanel swatchesWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = spacing,
            LineSpacing = spacing,
            VerticalAlignment = VerticalAlignment.Center
        };

        void RebuildSwatches()
        {
            string currentHex = getSelectedHex();
            swatchesWrap.Children.Clear();
            for (int i = 0; i < hexColors.Count; i++)
            {
                string hex = hexColors[i];
                bool isChosen = string.Equals(currentHex, hex, StringComparison.OrdinalIgnoreCase);
                Color c = Color.TryParse(hex, out Color parsed) ? parsed : Color.FromRgb(59, 130, 246);

                Border swatch = new()
                {
                    Width = size,
                    Height = size,
                    CornerRadius = new CornerRadius(999),
                    Background = ThemeLifecycle.GetCachedBrush(c),
                    BorderBrush = isChosen ? theme.Palette.TextPrimary : Brushes.Transparent,
                    BorderThickness = new Thickness(isChosen ? (size >= 26 ? 2.5 : 2.0) : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = isChosen && showCheckmark
                        ? new TextBlock
                        {
                            Text = "✓",
                            FontSize = size >= 26 ? 13 : 10,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                        : null
                };

                swatch.PointerPressed += (_, _) =>
                {
                    onColorSelected(hex);
                    RebuildSwatches();
                };

                swatchesWrap.Children.Add(swatch);
            }
        }

        RebuildSwatches();
        ThemeLifecycle.Bind(swatchesWrap, theme, RebuildSwatches);
        rebuildSwatches = RebuildSwatches;

        return swatchesWrap;
    }
}
