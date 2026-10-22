// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Represents a selectable option item within a filter category of <see cref="SearchFilterBar"/>.
/// </summary>
/// <param name="Id">Unique option identifier.</param>
/// <param name="Label">Localized display label for the option.</param>
/// <param name="ColorHex">Optional hex color string (e.g., <c>"#3B82F6"</c>) rendered as a circular color dot.</param>
public sealed record FilterOptionItem(string Id, string Label, string? ColorHex = null);

/// <summary>
/// Defines a filter category displayed in the 2-level drill-down filter popover of <see cref="SearchFilterBar"/>.
/// </summary>
public sealed class FilterCategoryDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilterCategoryDefinition"/> class.
    /// </summary>
    /// <param name="key">Unique category key (e.g., <c>"group"</c>, <c>"tag"</c>, <c>"status"</c>).</param>
    /// <param name="title">Localized category display title.</param>
    /// <param name="iconSvg">SVG icon asset file name from <see cref="VectorIcons"/>.</param>
    /// <param name="optionsProvider">Callback returning the available options for this category.</param>
    /// <param name="onApplied">Callback invoked with the selected option IDs when applied or cleared.</param>
    /// <param name="allowMultiple">Whether multiple options can be selected simultaneously.</param>
    /// <param name="initialSelectedIds">Optional initial selected option IDs.</param>
    public FilterCategoryDefinition(
        string key,
        string title,
        string iconSvg,
        Func<IReadOnlyList<FilterOptionItem>> optionsProvider,
        Action<IReadOnlyList<string>> onApplied,
        bool allowMultiple = false,
        IReadOnlyList<string>? initialSelectedIds = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(iconSvg);
        ArgumentNullException.ThrowIfNull(optionsProvider);
        ArgumentNullException.ThrowIfNull(onApplied);

        this.Key = key;
        this.Title = title;
        this.IconSvg = iconSvg;
        this.OptionsProvider = optionsProvider;
        this.OnApplied = onApplied;
        this.AllowMultiple = allowMultiple;
        this.SelectedIds = initialSelectedIds is not null ? [.. initialSelectedIds] : [];
    }

    /// <summary>Gets the unique key identifying this filter category.</summary>
    public string Key { get; }

    /// <summary>Gets or sets the localized display title of this category.</summary>
    public string Title { get; set; }

    /// <summary>Gets the SVG icon asset file name for this category.</summary>
    public string IconSvg { get; }

    /// <summary>Gets a value indicating whether multiple options can be selected simultaneously.</summary>
    public bool AllowMultiple { get; }

    /// <summary>Gets the provider callback that returns the current options for this category.</summary>
    public Func<IReadOnlyList<FilterOptionItem>> OptionsProvider { get; }

    /// <summary>Gets the callback invoked when the selection for this category is applied or cleared.</summary>
    public Action<IReadOnlyList<string>> OnApplied { get; }

    /// <summary>Gets the mutable list of currently active selected option IDs for this category.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Mutable selection list by design.")]
    public List<string> SelectedIds { get; }
}

/// <summary>
/// Standalone unified Search &amp; Filter Bar component (<c>[ ▽ Bộ lọc (N) | [Chip ×] Tìm hoặc lọc... × | 🔍 ]</c>).
/// Combines a left filter button (with a built-in 2-level drill-down Flyout popover or custom Flyout),
/// inline removable filter chips, a seamless center search box, a clear-all button, and a right search trigger button.
/// </summary>
public sealed class SearchFilterBar : Border
{
    private readonly ThemeService _theme;
    private readonly TextBlock _filterLabel;
    private readonly Control _funnelIcon;
    private readonly Border _countBadge;
    private readonly TextBlock _countBadgeText;
    private readonly ScrollViewer _chipsScrollViewer;
    private readonly StackPanel _chipsPanel;
    private readonly Button _clearAllButton;
    private readonly TextBlock _clearAllIcon;
    private readonly List<FilterCategoryDefinition> _categories = new();
    private readonly Border _popoverCard;
    private Flyout? _filterFlyout;
    private FilterCategoryDefinition? _activeDrillDownCategory;
    private string _searchInListPlaceholder = "Tìm trong danh sách";
    private string _applyButtonText = "Áp dụng";
    private bool _isFilterActive;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilterBar"/> class.
    /// </summary>
    /// <param name="theme">Active theme service.</param>
    /// <param name="filterButtonText">Localized text for the left filter button (e.g., <c>"Bộ lọc"</c>).</param>
    /// <param name="placeholderText">Localized placeholder for the center input box (e.g., <c>"Tìm hoặc lọc"</c>).</param>
    /// <param name="initialSearchText">Initial search text.</param>
    /// <param name="filterFlyoutContent">Optional control displayed in a popover flyout when the left filter button is clicked.</param>
    /// <param name="searchCommand">Optional command invoked when the right magnifier button is clicked or Enter is pressed.</param>
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI symbols only.")]
    public SearchFilterBar(
        ThemeService theme,
        string filterButtonText,
        string placeholderText,
        string initialSearchText = "",
        Control? filterFlyoutContent = null,
        ICommand? searchCommand = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        this._theme = theme;

        this.CornerRadius = new CornerRadius(8);
        this.BorderThickness = new Thickness(1);
        this.Height = 36;
        this.ClipToBounds = true;
        this.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid barGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,1,*,Auto,1,Auto"),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Column 0: Left Segment [ ▽ Bộ lọc (N) ]
        this._funnelIcon = VectorIcons.Create(VectorIcons.Filter, 14, margin: new Thickness(0));
        this._filterLabel = new TextBlock
        {
            Text = filterButtonText,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        this._countBadgeText = new TextBlock
        {
            Text = "0",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        this._countBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            MinWidth = 18,
            Height = 18,
            Padding = new Thickness(5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Child = this._countBadgeText
        };

        StackPanel filterBtnContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        filterBtnContent.Children.Add(this._funnelIcon);
        filterBtnContent.Children.Add(this._filterLabel);
        filterBtnContent.Children.Add(this._countBadge);

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

        this._popoverCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6)
        };

        if (filterFlyoutContent is not null)
        {
            this.FilterButton.Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                Content = filterFlyoutContent
            };
        }

        // Column 1: Left 1px Divider
        Border leftDivider = new()
        {
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Column 2: Center Grid (Sub-Col 0: Inline Filter Chips, Sub-Col 1: SearchTextBox)
        this._chipsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        this._chipsScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Content = this._chipsPanel
        };

        this.SearchTextBox = new TextBox
        {
            PlaceholderText = placeholderText,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
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

        Grid centerGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(this._chipsScrollViewer, 0);
        Grid.SetColumn(this.SearchTextBox, 1);
        centerGrid.Children.Add(this._chipsScrollViewer);
        centerGrid.Children.Add(this.SearchTextBox);

        // Column 3: Clear-All ("×") Button
        this._clearAllIcon = new TextBlock
        {
            Text = "×",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        this._clearAllButton = new Button
        {
            Content = this._clearAllIcon,
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 4, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = !string.IsNullOrEmpty(initialSearchText),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        this._clearAllButton.Click += (_, _) =>
        {
            this.ClearAllFilters(invokeCallbacks: true);
            if (!string.IsNullOrEmpty(this.SearchTextBox.Text))
            {
                this.SearchTextBox.Text = string.Empty;
            }

            this.UpdateClearAllButtonVisibility();
        };

        this.SearchTextBox.TextChanged += (_, _) => this.UpdateClearAllButtonVisibility();

        // Column 4: Right 1px Divider
        Border rightDivider = new()
        {
            Width = 1,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Column 5: Right Segment [ 🔍 ] Search Button
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
        Grid.SetColumn(centerGrid, 2);
        Grid.SetColumn(this._clearAllButton, 3);
        Grid.SetColumn(rightDivider, 4);
        Grid.SetColumn(this.SearchButton, 5);

        barGrid.Children.Add(this.FilterButton);
        barGrid.Children.Add(leftDivider);
        barGrid.Children.Add(centerGrid);
        barGrid.Children.Add(this._clearAllButton);
        barGrid.Children.Add(rightDivider);
        barGrid.Children.Add(this.SearchButton);

        this.Child = barGrid;

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.InteractiveBorder;

            this._countBadge.Background = p.BrandPrimary;
            this.ApplyFilterButtonStateColors();

            leftDivider.Background = p.InteractiveBorder;
            rightDivider.Background = p.InteractiveBorder;

            this.SearchTextBox.Background = Brushes.Transparent;
            this.SearchTextBox.Foreground = p.TextPrimary;

            this._clearAllIcon.Foreground = p.TextSecondary;

            this.SearchButton.Background = p.SecondarySurface;
            VectorIcons.SetForeground(searchIcon, p.TextPrimary);

            this._popoverCard.Background = p.ElevatedSurface;
            this._popoverCard.BorderBrush = p.SubtleBorder;

            if (this._categories.Count > 0)
            {
                this.RefreshFilterChips();
                if (this._filterFlyout is { IsOpen: true })
                {
                    if (this._activeDrillDownCategory is not null)
                    {
                        this.ShowLevel2DrillDown(this._activeDrillDownCategory);
                    }
                    else
                    {
                        this.ShowLevel1Menu();
                    }
                }
            }
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
    /// Attaches a debounced <see cref="TextBox.TextChanged"/> handler to <see cref="SearchTextBox"/> so rapid typing
    /// does not rebuild virtualized lists on every keystroke, while clearing (<c>""</c>) executes immediately.
    /// </summary>
    public void BindDebouncedSearch(Action<string?> onSearch, int delayMs = 150)
    {
        ArgumentNullException.ThrowIfNull(onSearch);

        System.Threading.CancellationTokenSource? debounceCts = null;
        this.SearchTextBox.TextChanged += async (_, _) =>
        {
            string? text = this.SearchTextBox.Text;
            if (debounceCts is not null)
            {
                System.Threading.CancellationTokenSource prev = debounceCts;
                debounceCts = null;
                await prev.CancelAsync().ConfigureAwait(true);
                prev.Dispose();
            }

            if (string.IsNullOrEmpty(text))
            {
                onSearch(string.Empty);
                return;
            }

            System.Threading.CancellationTokenSource cts = new();
            debounceCts = cts;
            try
            {
                await System.Threading.Tasks.Task.Delay(delayMs, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(debounceCts, cts))
                {
                    debounceCts = null;
                    cts.Dispose();
                }
            }

            onSearch(this.SearchTextBox.Text);
        };
    }

    /// <summary>
    /// Configures the built-in 2-level drill-down filter categories for the popover menu and inline filter chips.
    /// </summary>
    /// <param name="categories">Category definitions to display in the root filter popover.</param>
    /// <param name="searchInListPlaceholder">Localized placeholder for the Level 2 option search box.</param>
    /// <param name="applyButtonText">Localized text for the Level 2 Apply button.</param>
    public void SetFilterCategories(
        IReadOnlyList<FilterCategoryDefinition> categories,
        string searchInListPlaceholder = "Tìm trong danh sách",
        string applyButtonText = "Áp dụng")
    {
        ArgumentNullException.ThrowIfNull(categories);

        this._categories.Clear();
        this._categories.AddRange(categories);
        this._searchInListPlaceholder = searchInListPlaceholder;
        this._applyButtonText = applyButtonText;

        if (this._filterFlyout is null)
        {
            this._filterFlyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                Content = this._popoverCard
            };
            this._filterFlyout.Opened += (_, _) => this.ShowLevel1Menu();
        }

        this.FilterButton.Flyout = this._filterFlyout;
        this.RefreshFilterChips();
    }

    /// <summary>
    /// Programmatically updates the selected option IDs for a specific filter category and refreshes the inline chips.
    /// </summary>
    /// <param name="categoryKey">Unique category key.</param>
    /// <param name="selectedIds">New selected option IDs.</param>
    /// <param name="invokeCallback">Whether to invoke the category's <see cref="FilterCategoryDefinition.OnApplied"/> callback.</param>
    public void SetCategorySelection(string categoryKey, IReadOnlyList<string> selectedIds, bool invokeCallback = false)
    {
        ArgumentNullException.ThrowIfNull(categoryKey);
        ArgumentNullException.ThrowIfNull(selectedIds);

        FilterCategoryDefinition? category = this._categories.Find(
            c => string.Equals(c.Key, categoryKey, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return;
        }

        category.SelectedIds.Clear();
        category.SelectedIds.AddRange(selectedIds);

        if (invokeCallback)
        {
            category.OnApplied(category.SelectedIds);
        }

        this.RefreshFilterChips();
    }

    /// <summary>
    /// Clears all active category filter selections and refreshes the inline chips and badge count.
    /// </summary>
    /// <param name="invokeCallbacks">Whether to invoke each category's <see cref="FilterCategoryDefinition.OnApplied"/> callback with an empty list.</param>
    public void ClearAllFilters(bool invokeCallbacks = true)
    {
        foreach (FilterCategoryDefinition category in this._categories)
        {
            bool hadSelection = category.SelectedIds.Count > 0;
            category.SelectedIds.Clear();
            if (invokeCallbacks && hadSelection)
            {
                category.OnApplied(Array.Empty<string>());
            }
        }

        this.RefreshFilterChips();
    }

    /// <summary>
    /// Rebuilds the inline filter chips and active filter count badge from the current category selections.
    /// </summary>
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI chip remove symbol.")]
    public void RefreshFilterChips()
    {
        ThemePalette p = this._theme.Palette;
        this._chipsPanel.Children.Clear();

        int activeCount = 0;
        foreach (FilterCategoryDefinition cat in this._categories)
        {
            if (cat.SelectedIds.Count == 0)
            {
                continue;
            }

            activeCount++;
            IReadOnlyList<FilterOptionItem> options = cat.OptionsProvider();
            List<string> labels = new(cat.SelectedIds.Count);
            string? firstColorHex = null;

            foreach (string selectedId in cat.SelectedIds)
            {
                FilterOptionItem? matched = null;
                for (int i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = options[i];
                        break;
                    }
                }

                if (matched is not null)
                {
                    labels.Add(matched.Label);
                    if (firstColorHex is null && !string.IsNullOrWhiteSpace(matched.ColorHex))
                    {
                        firstColorHex = matched.ColorHex;
                    }
                }
                else
                {
                    labels.Add(selectedId);
                }
            }

            StackPanel chipRow = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrWhiteSpace(firstColorHex) && Color.TryParse(firstColorHex, out Color dotColor))
            {
                chipRow.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(dotColor),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            chipRow.Children.Add(new TextBlock
            {
                Text = $"{cat.Title}: {string.Join(", ", labels)}",
                FontSize = 11.5,
                FontWeight = FontWeight.Medium,
                Foreground = p.TextPrimary,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            FilterCategoryDefinition capturedCat = cat;
            Button removeChipBtn = new()
            {
                Content = "×",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(3, 0),
                MinWidth = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Foreground = p.TextSecondary,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            removeChipBtn.Click += (_, _) =>
            {
                capturedCat.SelectedIds.Clear();
                capturedCat.OnApplied(Array.Empty<string>());
                this.RefreshFilterChips();
                if (this._activeDrillDownCategory is null && this._categories.Count > 0)
                {
                    this.ShowLevel1Menu();
                }
            };

            chipRow.Children.Add(removeChipBtn);

            Border chipBorder = new()
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 4, 2),
                Background = p.SecondarySurface,
                BorderBrush = p.SubtleBorder,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = chipRow
            };

            this._chipsPanel.Children.Add(chipBorder);
        }

        this._chipsScrollViewer.IsVisible = this._chipsPanel.Children.Count > 0;
        this._countBadge.IsVisible = activeCount > 0;
        this._countBadgeText.Text = activeCount.ToString(CultureInfo.InvariantCulture);

        if (this._categories.Count > 0)
        {
            this._isFilterActive = activeCount > 0;
            this.ApplyFilterButtonStateColors();
        }

        this.UpdateClearAllButtonVisibility();
    }

    /// <summary>
    /// Attaches or replaces the popover <see cref="Flyout"/> content displayed when clicking the left filter button.
    /// </summary>
    public void SetFilterFlyout(Control filterFlyoutContent)
    {
        ArgumentNullException.ThrowIfNull(filterFlyoutContent);
        this.FilterButton.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = filterFlyoutContent
        };
    }

    /// <summary>
    /// Updates the visual active indicator on the left filter button when non-default filters are applied.
    /// </summary>
    public void SetFilterActive(bool isActive)
    {
        this._isFilterActive = isActive;
        this.ApplyFilterButtonStateColors();
    }

    /// <summary>
    /// Updates localized text on the filter button, search box placeholder, and drill-down popover controls.
    /// </summary>
    public void UpdateLocalization(
        string filterButtonText,
        string placeholderText,
        string? searchInListPlaceholder = null,
        string? applyButtonText = null)
    {
        this._filterLabel.Text = filterButtonText;
        this.SearchTextBox.PlaceholderText = placeholderText;

        if (!string.IsNullOrWhiteSpace(searchInListPlaceholder))
        {
            this._searchInListPlaceholder = searchInListPlaceholder;
        }

        if (!string.IsNullOrWhiteSpace(applyButtonText))
        {
            this._applyButtonText = applyButtonText;
        }

        AutomationProperties.SetName(this.FilterButton, filterButtonText);
        ToolTip.SetTip(this.FilterButton, filterButtonText);
        AutomationProperties.SetName(this.SearchTextBox, placeholderText);
        AutomationProperties.SetName(this.SearchButton, placeholderText);
        ToolTip.SetTip(this.SearchButton, placeholderText);

        if (this._categories.Count > 0)
        {
            this.RefreshFilterChips();
            this.ShowLevel1Menu();
        }
    }

    private void ApplyFilterButtonStateColors()
    {
        ThemePalette p = this._theme.Palette;
        this.FilterButton.Background = this._isFilterActive ? p.BrandSubtleFill : p.SecondarySurface;
        this._filterLabel.Foreground = this._isFilterActive ? p.BrandPrimary : p.TextPrimary;
        VectorIcons.SetForeground(this._funnelIcon, this._isFilterActive ? p.BrandPrimary : p.TextSecondary);
    }

    private void UpdateClearAllButtonVisibility()
    {
        bool hasSearchText = !string.IsNullOrEmpty(this.SearchTextBox.Text);
        bool hasActiveCategory = false;
        for (int i = 0; i < this._categories.Count; i++)
        {
            if (this._categories[i].SelectedIds.Count > 0)
            {
                hasActiveCategory = true;
                break;
            }
        }

        this._clearAllButton.IsVisible = hasSearchText || hasActiveCategory;
    }

    private void ShowLevel1Menu()
    {
        this._activeDrillDownCategory = null;
        ThemePalette p = this._theme.Palette;

        this._popoverCard.Width = 230;
        this._popoverCard.Padding = new Thickness(6);

        StackPanel menuStack = new()
        {
            Spacing = 2
        };

        foreach (FilterCategoryDefinition cat in this._categories)
        {
            Grid rowGrid = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            Control catIcon = VectorIcons.Create(cat.IconSvg, 16, foreground: p.TextSecondary);
            TextBlock catTitle = new()
            {
                Text = cat.Title,
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                Foreground = p.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border catBadge = new()
            {
                CornerRadius = new CornerRadius(9),
                MinWidth = 18,
                Height = 18,
                Padding = new Thickness(5, 0),
                Background = p.BrandPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = cat.SelectedIds.Count > 0,
                Child = new TextBlock
                {
                    Text = cat.SelectedIds.Count.ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Grid.SetColumn(catIcon, 0);
            Grid.SetColumn(catTitle, 1);
            Grid.SetColumn(catBadge, 2);
            rowGrid.Children.Add(catIcon);
            rowGrid.Children.Add(catTitle);
            rowGrid.Children.Add(catBadge);

            FilterCategoryDefinition capturedCat = cat;
            Button catBtn = new()
            {
                Content = rowGrid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            catBtn.PointerEntered += (_, _) => catBtn.Background = p.SecondarySurface;
            catBtn.PointerExited += (_, _) => catBtn.Background = Brushes.Transparent;
            catBtn.Click += (_, _) => this.ShowLevel2DrillDown(capturedCat);

            menuStack.Children.Add(catBtn);
        }

        this._popoverCard.Child = menuStack;
    }

    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI back chevron symbol.")]
    private void ShowLevel2DrillDown(FilterCategoryDefinition cat)
    {
        this._activeDrillDownCategory = cat;
        ThemePalette p = this._theme.Palette;

        this._popoverCard.Width = 260;
        this._popoverCard.Padding = new Thickness(10);

        List<string> pendingIds = [.. cat.SelectedIds];

        // 1. Back button header: [ ‹  {cat.Title} ]
        StackPanel backHeaderContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        backHeaderContent.Children.Add(new TextBlock
        {
            Text = "‹",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = p.TextSecondary,
            Margin = new Thickness(0, -2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        backHeaderContent.Children.Add(new TextBlock
        {
            Text = cat.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = p.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });

        Button backBtn = new()
        {
            Content = backHeaderContent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        backBtn.PointerEntered += (_, _) => backBtn.Background = p.SecondarySurface;
        backBtn.PointerExited += (_, _) => backBtn.Background = Brushes.Transparent;
        backBtn.Click += (_, _) => this.ShowLevel1Menu();

        // 2. In-popover search TextBox
        TextBox inListSearchBox = new()
        {
            PlaceholderText = this._searchInListPlaceholder,
            CornerRadius = new CornerRadius(8),
            BorderBrush = p.BrandPrimary,
            BorderThickness = new Thickness(1.5),
            Background = p.SecondarySurface,
            Foreground = p.TextPrimary,
            FontSize = 12.5,
            Padding = new Thickness(10, 6)
        };

        // 3. Scrollable CheckBox list
        StackPanel optionsStack = new()
        {
            Spacing = 2
        };

        ScrollViewer optionsScroll = new()
        {
            MaxHeight = 200,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = optionsStack
        };

        IReadOnlyList<FilterOptionItem> allOptions = cat.OptionsProvider();

        void PopulateCheckBoxes(string? filterQuery)
        {
            optionsStack.Children.Clear();
            string trimmedQuery = filterQuery?.Trim() ?? string.Empty;
            List<(FilterOptionItem Option, CheckBox Box)> checkBoxes = new();
            bool isUpdatingChecks = false;

            foreach (FilterOptionItem opt in allOptions)
            {
                if (!string.IsNullOrEmpty(trimmedQuery) &&
                    !opt.Label.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Control labelContent;
                if (!string.IsNullOrWhiteSpace(opt.ColorHex) && Color.TryParse(opt.ColorHex, out Color dotColor))
                {
                    StackPanel row = new()
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    row.Children.Add(new Border
                    {
                        Width = 10,
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(dotColor),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = opt.Label,
                        FontSize = 12.5,
                        Foreground = p.TextPrimary,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    labelContent = row;
                }
                else
                {
                    labelContent = new TextBlock
                    {
                        Text = opt.Label,
                        FontSize = 12.5,
                        Foreground = p.TextPrimary,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }

                bool isInitiallyChecked = false;
                for (int i = 0; i < pendingIds.Count; i++)
                {
                    if (string.Equals(pendingIds[i], opt.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        isInitiallyChecked = true;
                        break;
                    }
                }

                CheckBox cb = new()
                {
                    Content = labelContent,
                    IsChecked = isInitiallyChecked,
                    Padding = new Thickness(6, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                FilterOptionItem capturedOpt = opt;
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (isUpdatingChecks)
                    {
                        return;
                    }

                    bool isChecked = cb.IsChecked == true;
                    if (isChecked)
                    {
                        if (!cat.AllowMultiple)
                        {
                            isUpdatingChecks = true;
                            pendingIds.Clear();
                            pendingIds.Add(capturedOpt.Id);
                            for (int i = 0; i < checkBoxes.Count; i++)
                            {
                                if (!string.Equals(checkBoxes[i].Option.Id, capturedOpt.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    checkBoxes[i].Box.IsChecked = false;
                                }
                            }

                            isUpdatingChecks = false;
                        }
                        else
                        {
                            bool exists = false;
                            for (int i = 0; i < pendingIds.Count; i++)
                            {
                                if (string.Equals(pendingIds[i], capturedOpt.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    exists = true;
                                    break;
                                }
                            }

                            if (!exists)
                            {
                                pendingIds.Add(capturedOpt.Id);
                            }
                        }
                    }
                    else
                    {
                        _ = pendingIds.RemoveAll(id => string.Equals(id, capturedOpt.Id, StringComparison.OrdinalIgnoreCase));
                    }
                };

                checkBoxes.Add((opt, cb));
                optionsStack.Children.Add(cb);
            }
        }

        PopulateCheckBoxes(null);
        inListSearchBox.TextChanged += (_, _) => PopulateCheckBoxes(inListSearchBox.Text);

        // 4. Bottom-right primary button: [ Áp dụng ]
        Button applyBtn = new()
        {
            Content = this._applyButtonText,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 6),
            CornerRadius = new CornerRadius(6),
            Background = p.BrandPrimary,
            Foreground = Brushes.White,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        applyBtn.Click += (_, _) =>
        {
            cat.SelectedIds.Clear();
            cat.SelectedIds.AddRange(pendingIds);
            cat.OnApplied(cat.SelectedIds);
            this.RefreshFilterChips();
            this._filterFlyout?.Hide();
        };

        StackPanel drillDownStack = new()
        {
            Spacing = 8
        };
        drillDownStack.Children.Add(backBtn);
        drillDownStack.Children.Add(inListSearchBox);
        drillDownStack.Children.Add(optionsScroll);
        drillDownStack.Children.Add(applyBtn);

        this._popoverCard.Child = drillDownStack;
    }
}
