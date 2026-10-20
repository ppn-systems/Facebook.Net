// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone form input component factory (<c>CreateTextBox</c>, <c>CreateCheckBox</c>, <c>CreateComboBox</c>, <c>CreateListBox</c>, <c>CreateLabeled</c>).
/// </summary>
public static class FormField
{
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
        ThemeLifecycle.Bind(box, theme, ApplyColors);
        return box;
    }

    /// <summary>
    /// Creates a styled <see cref="CheckBox"/> whose label is automatically themed for Dark and Light palettes.
    /// </summary>
    public static CheckBox CreateCheckBox(
        ThemeService theme,
        string initialLabel,
        bool initialChecked,
        out TextBlock labelBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TextBlock lbl = SectionHeader.CreateLabel(theme, initialLabel, 12, FontWeight.Medium);
        CheckBox cb = new()
        {
            Content = lbl,
            IsChecked = initialChecked,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(cb, initialLabel);

        labelBlock = lbl;
        return cb;
    }

    /// <summary>
    /// Creates a styled <see cref="ComboBox"/> bound to <see cref="ThemeService"/> with a typed item template.
    /// </summary>
    public static ComboBox CreateComboBox<T>(
        ThemeService theme,
        IEnumerable itemsSource,
        Func<T, Control> itemBuilder,
        object? selectedItem = null,
        string? placeholderText = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(itemsSource);
        ArgumentNullException.ThrowIfNull(itemBuilder);

        ComboBox combo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            ItemsSource = itemsSource,
            PlaceholderText = placeholderText,
            ItemTemplate = new FuncDataTemplate<T>((item, _) =>
                item is null ? new Border() : itemBuilder(item))
        };

        if (selectedItem is not null)
        {
            combo.SelectedItem = selectedItem;
        }

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            combo.Background = p.SecondarySurface;
            combo.BorderBrush = p.InteractiveBorder;
            combo.Foreground = p.TextPrimary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(combo, theme, ApplyColors);
        return combo;
    }

    /// <summary>
    /// Creates a styled <see cref="ListBox"/> bound to <see cref="ThemeService"/> with a typed item template.
    /// </summary>
    public static ListBox CreateListBox<T>(
        ThemeService theme,
        IEnumerable itemsSource,
        Func<T, Control> itemBuilder,
        object? selectedItem = null,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(itemsSource);
        ArgumentNullException.ThrowIfNull(itemBuilder);

        ListBox listBox = new()
        {
            IsVisible = isVisible,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            ItemsSource = itemsSource,
            SelectedItem = selectedItem,
            ItemTemplate = new FuncDataTemplate<T>((item, _) =>
                item is null ? new Border() : itemBuilder(item))
        };

        listBox.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item)
            {
                item.Padding = new Thickness(4, 3);
                item.CornerRadius = new CornerRadius(6);
            }
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            listBox.Background = p.SecondarySurface;
            listBox.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(listBox, theme, ApplyColors);
        return listBox;
    }

    /// <summary>
    /// Wraps a control with a themed label header inside a vertical <see cref="StackPanel"/>.
    /// </summary>
    public static StackPanel CreateLabeled(
        ThemeService theme,
        string initialLabelText,
        Control fieldControl,
        out TextBlock labelBlock)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(fieldControl);

        TextBlock lbl = SectionHeader.CreateLabel(theme, initialLabelText, 12, FontWeight.SemiBold, isSecondary: true);
        StackPanel stack = new()
        {
            Spacing = 6
        };
        stack.Children.Add(lbl);
        stack.Children.Add(fieldControl);

        labelBlock = lbl;
        return stack;
    }
}
