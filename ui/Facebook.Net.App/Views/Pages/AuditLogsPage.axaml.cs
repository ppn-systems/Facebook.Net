// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Activity &amp; Audit Logs Page (<c>AuditLogsPage.axaml</c>).
/// Features the unified <see cref="SearchFilterBar"/> (<c>[ ▽ Bộ lọc | Tìm hoặc lọc | 🔍 ]</c>)
/// and the <see cref="DataTableView{T}"/> table component.
/// </summary>
public sealed partial class AuditLogsPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogsPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public AuditLogsPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public AuditLogsPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this._vm = viewModel;
        this._theme = viewModel.Context.Theme;
        this._i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.InitializeComponent();
        ContentControl root = this.FindControl<ContentControl>("PageRoot")!;
        root.Content = this.BuildLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private Control BuildLayout()
    {
        AuditLogDrawerViewModel drawerVm = this._vm.AuditDrawer;

        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 8
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Audit.TaskbarTitle"],
            this._i18n["Audit.SearchPlaceholder"],
            out TextBlock titleTb,
            out TextBlock infoTb);

        // SVG Icon Metric Pills for Success / Warning / Error counters
        StackPanel countersRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border okPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.CheckCircle,
            drawerVm.SuccessCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Success,
            out TextBlock okCountTb);

        Border warnPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.Warning,
            drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Warning,
            out TextBlock warnCountTb);

        Border errPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.ErrorCircle,
            drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Error,
            out TextBlock errCountTb);

        countersRow.Children.Add(okPill);
        countersRow.Children.Add(warnPill);
        countersRow.Children.Add(errPill);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(countersRow, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(countersRow);
        Grid.SetRow(topHeaderRow, 0);
        pageGrid.Children.Add(topHeaderRow);

        // Row 1: Unified 2-Level Search & Filter Bar ([ ▽ Bộ lọc (N) | [Chip ×] Tìm hoặc lọc | 🔍 ]) + Refresh + Clear
        Grid filterBarRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        SearchFilterBar searchFilterBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Audit.SearchPlaceholder"],
            drawerVm.SearchKeyword,
            searchCommand: drawerVm.RefreshFilterCommand);

        FilterCategoryDefinition catCategory = new(
            "category",
            this._i18n["Audit.FilterCatCategory"],
            VectorIcons.Filter,
            () =>
            [
                new FilterOptionItem(nameof(AuditCategory.System), this._i18n.FormatAuditCategory(AuditCategory.System)),
                new FilterOptionItem(nameof(AuditCategory.Account), this._i18n.FormatAuditCategory(AuditCategory.Account)),
                new FilterOptionItem(nameof(AuditCategory.Group), this._i18n.FormatAuditCategory(AuditCategory.Group)),
                new FilterOptionItem(nameof(AuditCategory.Post), this._i18n.FormatAuditCategory(AuditCategory.Post)),
                new FilterOptionItem(nameof(AuditCategory.SalesComment), this._i18n.FormatAuditCategory(AuditCategory.SalesComment)),
                new FilterOptionItem(nameof(AuditCategory.Scrape), this._i18n.FormatAuditCategory(AuditCategory.Scrape))
            ],
            selectedIds =>
            {
                drawerVm.SelectedCategory = selectedIds.Count > 0 && Enum.TryParse<AuditCategory>(selectedIds[0], out AuditCategory parsedCat)
                    ? parsedCat
                    : null;
                _ = drawerVm.RefreshLogsAsync();
            },
            allowMultiple: false,
            initialSelectedIds: drawerVm.SelectedCategory is { } initCat ? [initCat.ToString()] : null);

        FilterCategoryDefinition timeCategory = new(
            "time",
            this._i18n["Audit.FilterCatTimeRange"],
            VectorIcons.Sync,
            () =>
            [
                new FilterOptionItem(nameof(AuditTimeRange.LastHour), this._i18n["Audit.Time.LastHour"]),
                new FilterOptionItem(nameof(AuditTimeRange.Today24Hours), this._i18n["Audit.Time.Today24h"]),
                new FilterOptionItem(nameof(AuditTimeRange.Last7Days), this._i18n["Audit.Time.Last7Days"])
            ],
            selectedIds =>
            {
                drawerVm.SelectedTimeRange = selectedIds.Count > 0 && Enum.TryParse<AuditTimeRange>(selectedIds[0], out AuditTimeRange parsedTime)
                    ? parsedTime
                    : AuditTimeRange.AllTime;
                _ = drawerVm.RefreshLogsAsync();
            },
            allowMultiple: false,
            initialSelectedIds: drawerVm.SelectedTimeRange != AuditTimeRange.AllTime ? [drawerVm.SelectedTimeRange.ToString()] : null);

        FilterCategoryDefinition accountCategory = new(
            "account",
            this._i18n["Audit.FilterCatAccount"],
            VectorIcons.Accounts,
            () =>
            {
                List<FilterOptionItem> items = new(this._vm.Accounts.Count);
                for (int i = 0; i < this._vm.Accounts.Count; i++)
                {
                    AccountProfile acc = this._vm.Accounts[i];
                    items.Add(new FilterOptionItem(acc.Uid, acc.DisplayName));
                }

                return items;
            },
            selectedIds =>
            {
                drawerVm.SelectedAccountUid = selectedIds.Count > 0 ? selectedIds[0] : null;
                _ = drawerVm.RefreshLogsAsync();
            },
            allowMultiple: false,
            initialSelectedIds: !string.IsNullOrEmpty(drawerVm.SelectedAccountUid) ? [drawerVm.SelectedAccountUid] : null);

        searchFilterBar.SetFilterCategories(
            [catCategory, timeCategory, accountCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        this._vm.Accounts.CollectionChanged += (_, _) => searchFilterBar.RefreshFilterChips();

        searchFilterBar.SearchTextBox.TextChanged += async (_, _) =>
        {
            drawerVm.SearchKeyword = searchFilterBar.SearchTextBox.Text ?? string.Empty;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };

        Button refreshBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Audit.BtnRefresh"],
            drawerVm.RefreshFilterCommand,
            VectorIcons.Sync);

        Button clearBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Audit.BtnClear"],
            drawerVm.ClearLogsCommand,
            VectorIcons.Trash);
        clearBtn.Click += (_, _) =>
        {
            drawerVm.SelectedCategory = null;
            drawerVm.SelectedTimeRange = AuditTimeRange.AllTime;
            drawerVm.SelectedAccountUid = null;
            searchFilterBar.ClearAllFilters(invokeCallbacks: false);
        };

        Grid.SetColumn(searchFilterBar, 0);
        Grid.SetColumn(refreshBtn, 1);
        Grid.SetColumn(clearBtn, 2);
        filterBarRow.Children.Add(searchFilterBar);
        filterBarRow.Children.Add(refreshBtn);
        filterBarRow.Children.Add(clearBtn);
        Grid.SetRow(filterBarRow, 1);
        pageGrid.Children.Add(filterBarRow);

        // Row 2: DataTableView<AuditLogRecord> with column headers and empty state
        DataTableView<AuditLogRecord> auditTable = new(
            this._theme,
            [
                new DataTableColumn("66", this._i18n["Table.ColTime"]),
                new DataTableColumn("115", this._i18n["Table.ColAccount"]),
                new DataTableColumn("105", this._i18n["Table.ColCategory"]),
                new DataTableColumn("130", this._i18n["Table.ColTarget"]),
                new DataTableColumn("*", this._i18n["Table.ColMessage"])
            ],
            drawerVm.FilteredLogs,
            rec => AuditLogTableRow.Create(this._theme, this._i18n, rec),
            this._i18n["Empty.AuditLogs"],
            VectorIcons.AuditLog);

        Grid.SetRow(auditTable, 2);
        pageGrid.Children.Add(auditTable);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Audit.TaskbarTitle"];
            infoTb.Text = this._i18n["Audit.SearchPlaceholder"];
            catCategory.Title = this._i18n["Audit.FilterCatCategory"];
            timeCategory.Title = this._i18n["Audit.FilterCatTimeRange"];
            accountCategory.Title = this._i18n["Audit.FilterCatAccount"];
            searchFilterBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Audit.SearchPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            auditTable.UpdateHeadersAndEmptyText(
                [
                    this._i18n["Table.ColTime"],
                    this._i18n["Table.ColAccount"],
                    this._i18n["Table.ColCategory"],
                    this._i18n["Table.ColTarget"],
                    this._i18n["Table.ColMessage"]
                ],
                this._i18n["Empty.AuditLogs"]);
            AppButton.SetText(refreshBtn, this._i18n["Audit.BtnRefresh"]);
            AppButton.SetText(clearBtn, this._i18n["Audit.BtnClear"]);
        };

        drawerVm.PropertyChanged += (_, _) =>
        {
            okCountTb.Text = drawerVm.SuccessCount.ToString(CultureInfo.InvariantCulture);
            warnCountTb.Text = drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture);
            errCountTb.Text = drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture);
        };

        return pageGrid;
    }
}
