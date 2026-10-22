// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Column definition descriptor for <see cref="DataTableView{T}"/>.
/// </summary>
/// <param name="Width">Avalonia column width string (e.g., <c>"72"</c>, <c>"1.2*"</c>, <c>"Auto"</c>).</param>
/// <param name="HeaderText">Initial localized column header label.</param>
public sealed record DataTableColumn(string Width, string HeaderText);

/// <summary>
/// Standalone Data Table component with a themed header bar, column alignment, scrollable row list,
/// and integrated empty-state banner.
/// </summary>
/// <typeparam name="T">Type of data record rendered in each table row.</typeparam>
public sealed class DataTableView<T> : Border where T : class
{
    private readonly List<TextBlock> _headerLabels = new();
    private readonly TextBlock _emptyStateLabel;
    private readonly TextBlock? _tableTitleLabel;
    private readonly IEnumerable<T> _itemsSource;
    private string? _tableTitlePrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTableView{T}"/> class.
    /// </summary>
    public DataTableView(
        ThemeService theme,
        IReadOnlyList<DataTableColumn> columns,
        IEnumerable<T> itemsSource,
        Func<T, Control> rowBuilder,
        string emptyMessage,
        string emptyIconSvg,
        string? tableTitlePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(itemsSource);
        ArgumentNullException.ThrowIfNull(rowBuilder);

        this._itemsSource = itemsSource;
        this._tableTitlePrefix = tableTitlePrefix;

        this.CornerRadius = new CornerRadius(10);
        this.BorderThickness = new Thickness(1);
        this.ClipToBounds = true;

        Grid rootGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*")
        };

        // 0. Optional Table Card Title Bar
        Border? titleBarBorder = null;
        if (!string.IsNullOrWhiteSpace(tableTitlePrefix))
        {
            TextBlock titleTb = SectionHeader.CreateLabel(
                theme,
                $"{tableTitlePrefix}: {CountItems(itemsSource)}",
                14,
                FontWeight.Bold);
            this._tableTitleLabel = titleTb;

            titleBarBorder = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12),
                Child = titleTb
            };
            Grid.SetRow(titleBarBorder, 0);
            rootGrid.Children.Add(titleBarBorder);
        }

        // 1. Table Column Header Bar
        string colWidths = string.Join(",", ExtractWidths(columns));
        Grid headerRowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(colWidths),
            ColumnSpacing = 12,
            Margin = new Thickness(16, 10)
        };

        for (int i = 0; i < columns.Count; i++)
        {
            TextBlock colTb = SectionHeader.CreateLabel(
                theme,
                columns[i].HeaderText,
                11,
                FontWeight.Bold,
                isSecondary: true);
            colTb.TextTrimming = TextTrimming.CharacterEllipsis;
            colTb.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(colTb, i);
            headerRowGrid.Children.Add(colTb);
            this._headerLabels.Add(colTb);
        }

        Border headerBar = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerRowGrid
        };
        Grid.SetRow(headerBar, 1);
        rootGrid.Children.Add(headerBar);

        // 2. Table Rows ListBox & Empty State Banner
        bool hasItems = HasAnyItem(itemsSource);

        this.RowsListBox = new ListBox
        {
            IsVisible = hasItems,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            ItemsSource = itemsSource,
            ItemTemplate = new FuncDataTemplate<T>((item, _) =>
                item is null ? new Border() : rowBuilder(item))
        };

        this.RowsListBox.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item)
            {
                item.Padding = new Thickness(10, 8);
                item.Margin = new Thickness(0, 1);
                item.CornerRadius = new CornerRadius(6);
            }
        };

        Border emptyBanner = EmptyStateBanner.Create(
            theme,
            emptyMessage,
            emptyIconSvg,
            out TextBlock emptyTb);
        this._emptyStateLabel = emptyTb;
        emptyBanner.BorderThickness = new Thickness(0);
        emptyBanner.IsVisible = !hasItems;

        if (itemsSource is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += (_, _) =>
            {
                bool any = HasAnyItem(itemsSource);
                this.RowsListBox.IsVisible = any;
                emptyBanner.IsVisible = !any;
                if (this._tableTitleLabel is not null && !string.IsNullOrWhiteSpace(this._tableTitlePrefix))
                {
                    this._tableTitleLabel.Text = $"{this._tableTitlePrefix}: {CountItems(itemsSource)}";
                }
            };
        }

        Grid bodyHost = EmptyStateBanner.CreateHost(this.RowsListBox, emptyBanner);
        Grid.SetRow(bodyHost, 2);
        rootGrid.Children.Add(bodyHost);

        this.Child = rootGrid;

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.SubtleBorder;
            if (titleBarBorder is not null)
            {
                titleBarBorder.Background = p.ElevatedSurface;
                titleBarBorder.BorderBrush = p.SubtleBorder;
            }

            headerBar.Background = p.SecondarySurface;
            headerBar.BorderBrush = p.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(this, theme, ApplyColors);
    }

    /// <summary>Gets the underlying <see cref="ListBox"/> rendering the table rows.</summary>
    public ListBox RowsListBox { get; }

    /// <summary>
    /// Updates localized column header texts, empty-state message, and optional table title prefix.
    /// </summary>
    public void UpdateHeadersAndEmptyText(IReadOnlyList<string> headerTexts, string emptyMessage, string? tableTitlePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(headerTexts);

        int count = Math.Min(this._headerLabels.Count, headerTexts.Count);
        for (int i = 0; i < count; i++)
        {
            this._headerLabels[i].Text = headerTexts[i];
        }

        this._emptyStateLabel.Text = emptyMessage;
        if (!string.IsNullOrWhiteSpace(tableTitlePrefix))
        {
            this._tableTitlePrefix = tableTitlePrefix;
            this._tableTitleLabel?.Text = $"{tableTitlePrefix}: {CountItems(this._itemsSource)}";
        }
    }

    private static string[] ExtractWidths(IReadOnlyList<DataTableColumn> columns)
    {
        string[] result = new string[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            result[i] = columns[i].Width;
        }

        return result;
    }

    private static int CountItems(IEnumerable<T> source)
    {
        if (source is ICollection<T> col)
        {
            return col.Count;
        }

        int n = 0;
        using IEnumerator<T> enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            n++;
        }

        return n;
    }

    private static bool HasAnyItem(IEnumerable<T> source)
    {
        if (source is ICollection<T> col)
        {
            return col.Count > 0;
        }

        using IEnumerator<T> enumerator = source.GetEnumerator();
        return enumerator.MoveNext();
    }
}
