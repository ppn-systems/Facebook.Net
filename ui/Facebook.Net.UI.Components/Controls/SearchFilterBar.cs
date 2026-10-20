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
/// Standalone unified Search &amp; Filter Bar component (<c>[ ▽ Bộ lọc | Tìm hoặc lọc... | 🔍 ]</c>).
/// Combines a left filter button (with optional Flyout popover), a seamless center search box,
/// and a right search trigger button inside a single bordered pill container.
/// </summary>
public sealed class SearchFilterBar : Border
{
    private readonly TextBlock _filterLabel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilterBar"/> class.
    /// </summary>
    /// <param name="theme">Active theme service.</param>
    /// <param name="filterButtonText">Localized text for the left filter button (e.g., <c>"Bộ lọc"</c>).</param>
    /// <param name="placeholderText">Localized placeholder for the center input box (e.g., <c>"Tìm hoặc lọc"</c>).</param>
    /// <param name="initialSearchText">Initial search text.</param>
    /// <param name="filterFlyoutContent">Optional control displayed in a popover flyout when the left filter button is clicked.</param>
    /// <param name="searchCommand">Optional command invoked when the right magnifier button is clicked or Enter is pressed.</param>
    public SearchFilterBar(
        ThemeService theme,
        string filterButtonText,
        string placeholderText,
        string initialSearchText = "",
        Control? filterFlyoutContent = null,
        ICommand? searchCommand = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        this.CornerRadius = new CornerRadius(8);
        this.BorderThickness = new Thickness(1);
        this.Height = 36;
        this.ClipToBounds = true;
        this.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid barGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,1,*,1,Auto"),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 1. Left Segment: [ ▽ Bộ lọc ]
        Control funnelIcon = VectorIcons.Create(VectorIcons.Filter, 14, margin: new Thickness(0));
        this._filterLabel = new TextBlock
        {
            Text = filterButtonText,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel filterBtnContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        filterBtnContent.Children.Add(funnelIcon);
        filterBtnContent.Children.Add(this._filterLabel);

        this.FilterButton = new Button
        {
            Content = filterBtnContent,
            Padding = new Thickness(12, 0),
            CornerRadius = new CornerRadius(7, 0, 0, 7),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(this.FilterButton, filterButtonText);
        ToolTip.SetTip(this.FilterButton, filterButtonText);

        if (filterFlyoutContent is not null)
        {
            this.FilterButton.Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                Content = filterFlyoutContent
            };
        }

        // 2. Left 1px Divider
        Border leftDivider = new()
        {
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 3. Center Segment: Borderless Search TextBox
        this.SearchTextBox = new TextBox
        {
            PlaceholderText = placeholderText,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(10, 6),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(initialSearchText))
        {
            this.SearchTextBox.Text = initialSearchText;
        }

        AutomationProperties.SetName(this.SearchTextBox, placeholderText);

        // 4. Right 1px Divider
        Border rightDivider = new()
        {
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 5. Right Segment: [ 🔍 ] Search Button
        Control searchIcon = VectorIcons.Create(VectorIcons.Search, 15, margin: new Thickness(0));
        this.SearchButton = new Button
        {
            Content = searchIcon,
            Command = searchCommand,
            Width = 38,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0, 7, 7, 0),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        AutomationProperties.SetName(this.SearchButton, placeholderText);
        ToolTip.SetTip(this.SearchButton, placeholderText);

        this.SearchTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && searchCommand is not null && searchCommand.CanExecute(null))
            {
                searchCommand.Execute(null);
            }
        };

        Grid.SetColumn(this.FilterButton, 0);
        Grid.SetColumn(leftDivider, 1);
        Grid.SetColumn(this.SearchTextBox, 2);
        Grid.SetColumn(rightDivider, 3);
        Grid.SetColumn(this.SearchButton, 4);

        barGrid.Children.Add(this.FilterButton);
        barGrid.Children.Add(leftDivider);
        barGrid.Children.Add(this.SearchTextBox);
        barGrid.Children.Add(rightDivider);
        barGrid.Children.Add(this.SearchButton);

        this.Child = barGrid;

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.InteractiveBorder;

            this.FilterButton.Background = p.SecondarySurface;
            this._filterLabel.Foreground = p.TextPrimary;
            VectorIcons.SetForeground(funnelIcon, p.TextSecondary);

            leftDivider.Background = p.InteractiveBorder;
            rightDivider.Background = p.InteractiveBorder;

            this.SearchTextBox.Background = Brushes.Transparent;
            this.SearchTextBox.Foreground = p.TextPrimary;

            this.SearchButton.Background = p.SecondarySurface;
            VectorIcons.SetForeground(searchIcon, p.TextPrimary);
        }

        ApplyColors();
        ThemeLifecycle.Bind(this, theme, ApplyColors);
    }

    /// <summary>Gets the left <c>Bộ lọc</c> button.</summary>
    public Button FilterButton { get; }

    /// <summary>Gets the center borderless search <see cref="TextBox"/>.</summary>
    public TextBox SearchTextBox { get; }

    /// <summary>Gets the right magnifier search <see cref="Button"/>.</summary>
    public Button SearchButton { get; }

    /// <summary>
    /// Updates localized text on the filter button and search box placeholder.
    /// </summary>
    public void UpdateLocalization(string filterButtonText, string placeholderText)
    {
        this._filterLabel.Text = filterButtonText;
        this.SearchTextBox.PlaceholderText = placeholderText;
        AutomationProperties.SetName(this.FilterButton, filterButtonText);
        ToolTip.SetTip(this.FilterButton, filterButtonText);
        AutomationProperties.SetName(this.SearchTextBox, placeholderText);
        AutomationProperties.SetName(this.SearchButton, placeholderText);
        ToolTip.SetTip(this.SearchButton, placeholderText);
    }
}
