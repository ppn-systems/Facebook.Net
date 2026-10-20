// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
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

        Button backToSettingsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Audit.BtnBackToSettings"],
            this._vm.NavigateSettingsCommand,
            VectorIcons.Settings);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(countersRow, 1);
        Grid.SetColumn(backToSettingsBtn, 2);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(countersRow);
        topHeaderRow.Children.Add(backToSettingsBtn);
        Grid.SetRow(topHeaderRow, 0);
        pageGrid.Children.Add(topHeaderRow);

        // Filter Flyout Content inside [ ▽ Bộ lọc ] button: Category, TimeRange, Account
        ComboBox categoryCombo = FormField.CreateComboBox<object>(
            this._theme,
            new object[]
            {
                "ALL",
                AuditCategory.System,
                AuditCategory.Account,
                AuditCategory.Group,
                AuditCategory.Post,
                AuditCategory.SalesComment,
                AuditCategory.Scrape
            },
            obj =>
            {
                AuditCategory? cat = obj is AuditCategory c ? c : null;
                TextBlock tb = SectionHeader.CreateLabel(this._theme, this._i18n.FormatAuditCategory(cat), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditCategory(cat);
                return tb;
            },
            selectedItem: "ALL");
        categoryCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedCategory = categoryCombo.SelectedItem is AuditCategory cat ? cat : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };

        ComboBox timeRangeCombo = FormField.CreateComboBox<AuditTimeRange>(
            this._theme,
            new AuditTimeRange[]
            {
                AuditTimeRange.AllTime,
                AuditTimeRange.LastHour,
                AuditTimeRange.Today24Hours,
                AuditTimeRange.Last7Days
            },
            r =>
            {
                TextBlock tb = SectionHeader.CreateLabel(this._theme, this._i18n.FormatAuditTimeRange(r), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditTimeRange(r);
                return tb;
            },
            selectedItem: AuditTimeRange.AllTime);
        timeRangeCombo.SelectionChanged += async (_, _) =>
        {
            if (timeRangeCombo.SelectedItem is AuditTimeRange range)
            {
                drawerVm.SelectedTimeRange = range;
                await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
            }
        };

        ObservableCollection<object> accountFilterItems = new() { "ALL" };
        for (int i = 0; i < this._vm.Accounts.Count; i++)
        {
            accountFilterItems.Add(this._vm.Accounts[i]);
        }

        ComboBox accountFilterCombo = FormField.CreateComboBox<object>(
            this._theme,
            accountFilterItems,
            obj =>
            {
                if (obj is AccountProfile acc)
                {
                    return SectionHeader.CreateLabel(this._theme, acc.DisplayName, 12);
                }

                TextBlock allTb = SectionHeader.CreateLabel(this._theme, this._i18n["Audit.FilterAccountAll"], 12);
                this._i18n.LanguageChanged += (_, _) => allTb.Text = this._i18n["Audit.FilterAccountAll"];
                return allTb;
            },
            selectedItem: "ALL",
            placeholderText: this._i18n["Audit.FilterAccountAll"]);

        this._vm.Accounts.CollectionChanged += (_, _) =>
        {
            object? prev = accountFilterCombo.SelectedItem;
            accountFilterItems.Clear();
            accountFilterItems.Add("ALL");
            object? restored = "ALL";
            for (int i = 0; i < this._vm.Accounts.Count; i++)
            {
                AccountProfile item = this._vm.Accounts[i];
                accountFilterItems.Add(item);
                if (prev is AccountProfile prevAcc && string.Equals(prevAcc.Uid, item.Uid, StringComparison.Ordinal))
                {
                    restored = item;
                }
            }

            accountFilterCombo.SelectedItem = restored;
        };

        accountFilterCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedAccountUid = accountFilterCombo.SelectedItem is AccountProfile acc ? acc.Uid : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };

        StackPanel filterFlyoutStack = new() { Spacing = 8, Width = 280 };
        filterFlyoutStack.Children.Add(categoryCombo);
        filterFlyoutStack.Children.Add(timeRangeCombo);
        filterFlyoutStack.Children.Add(accountFilterCombo);
        Border filterFlyoutCard = SurfaceCard.Create(this._theme, filterFlyoutStack, new Thickness(12));

        // Row 1: Unified Search & Filter Bar ([ ▽ Bộ lọc | Tìm hoặc lọc | 🔍 ]) + Refresh + Clear
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
            filterFlyoutCard,
            drawerVm.RefreshFilterCommand);

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
            accountFilterCombo.PlaceholderText = this._i18n["Audit.FilterAccountAll"];
            searchFilterBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Audit.SearchPlaceholder"]);
            auditTable.UpdateHeadersAndEmptyText(
                [
                    this._i18n["Table.ColTime"],
                    this._i18n["Table.ColAccount"],
                    this._i18n["Table.ColCategory"],
                    this._i18n["Table.ColTarget"],
                    this._i18n["Table.ColMessage"]
                ],
                this._i18n["Empty.AuditLogs"]);
            AppButton.SetText(backToSettingsBtn, this._i18n["Audit.BtnBackToSettings"]);
            AppButton.SetText(refreshBtn, this._i18n["Audit.BtnRefresh"]);
            AppButton.SetText(clearBtn, this._i18n["Audit.BtnClear"]);
        };

        drawerVm.PropertyChanged += (_, _) =>
        {
            okCountTb.Text = drawerVm.SuccessCount.ToString(CultureInfo.InvariantCulture);
            warnCountTb.Text = drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture);
            errCountTb.Text = drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture);
        };

        return SurfaceCard.Create(this._theme, pageGrid);
    }
}
