// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
        okPill.Cursor = new Cursor(StandardCursorType.Hand);

        Border warnPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.Warning,
            drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Warning,
            out TextBlock warnCountTb);
        warnPill.Cursor = new Cursor(StandardCursorType.Hand);

        Border errPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.ErrorCircle,
            drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Error,
            out TextBlock errCountTb);
        errPill.Cursor = new Cursor(StandardCursorType.Hand);

        countersRow.Children.Add(okPill);
        countersRow.Children.Add(warnPill);
        countersRow.Children.Add(errPill);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(countersRow, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(countersRow);
        Grid.SetRow(topHeaderRow, 0);
        pageGrid.Children.Add(topHeaderRow);

        // Row 1: Unified 2-Level Search & Filter Bar ([ ▽ Bộ lọc (N) | [Chip ×] Tìm hoặc lọc | 🔍 ]) + Export CSV + Refresh + Clear
        Grid filterBarRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
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
                new FilterOptionItem(nameof(AuditCategory.Personal), this._i18n.FormatAuditCategory(AuditCategory.Personal)),
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

        FilterCategoryDefinition severityCategory = new(
            "severity",
            this._i18n["Audit.FilterCatSeverity"],
            VectorIcons.InfoCircle,
            () =>
            [
                new FilterOptionItem(nameof(AuditSeverity.Success), nameof(AuditSeverity.Success), "#10B981"),
                new FilterOptionItem(nameof(AuditSeverity.Warning), nameof(AuditSeverity.Warning), "#F59E0B"),
                new FilterOptionItem(nameof(AuditSeverity.Error), nameof(AuditSeverity.Error), "#EF4444"),
                new FilterOptionItem(nameof(AuditSeverity.Info), nameof(AuditSeverity.Info), "#3B82F6")
            ],
            selectedIds =>
            {
                drawerVm.SelectedSeverity = selectedIds.Count > 0 && Enum.TryParse<AuditSeverity>(selectedIds[0], out AuditSeverity parsedSev)
                    ? parsedSev
                    : null;
                _ = drawerVm.RefreshLogsAsync();
            },
            allowMultiple: false,
            initialSelectedIds: drawerVm.SelectedSeverity is { } initSev ? [initSev.ToString()] : null);

        void ToggleSeverityFilter(AuditSeverity targetSeverity)
        {
            drawerVm.SelectedSeverity = drawerVm.SelectedSeverity == targetSeverity ? null : targetSeverity;
            severityCategory.SelectedIds.Clear();
            if (drawerVm.SelectedSeverity is { } activeSev)
            {
                severityCategory.SelectedIds.Add(activeSev.ToString());
            }

            searchFilterBar.RefreshFilterChips();
            _ = drawerVm.RefreshLogsAsync();
        }

        okPill.PointerPressed += (_, _) => ToggleSeverityFilter(AuditSeverity.Success);
        warnPill.PointerPressed += (_, _) => ToggleSeverityFilter(AuditSeverity.Warning);
        errPill.PointerPressed += (_, _) => ToggleSeverityFilter(AuditSeverity.Error);

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
            [catCategory, severityCategory, timeCategory, accountCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        this._vm.Accounts.CollectionChanged += (_, _) => searchFilterBar.RefreshFilterChips();

        CancellationTokenSource? searchDebounceCts = null;
        searchFilterBar.SearchTextBox.TextChanged += async (_, _) =>
        {
            drawerVm.SearchKeyword = searchFilterBar.SearchTextBox.Text ?? string.Empty;
            if (searchDebounceCts is not null)
            {
                CancellationTokenSource prev = searchDebounceCts;
                searchDebounceCts = null;
                await prev.CancelAsync().ConfigureAwait(true);
            }

            using CancellationTokenSource cts = new();
            searchDebounceCts = cts;
            try
            {
                await Task.Delay(150, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(searchDebounceCts, cts))
                {
                    searchDebounceCts = null;
                }
            }

            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };

        Button exportCsvBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Audit.BtnExportCsv"],
            command: null,
            iconSvg: VectorIcons.Download);
        exportCsvBtn.Height = 36;
        exportCsvBtn.Click += async (_, _) =>
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null)
            {
                return;
            }

            string defaultFileName = $"audit_logs_{DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture)}.csv";
            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = this._i18n["Audit.BtnExportCsv"],
                SuggestedFileName = defaultFileName,
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV") { Patterns = ["*.csv"] }
                ]
            }).ConfigureAwait(true);

            if (file is not null)
            {
                string? localPath = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    _ = await drawerVm.ExportFilteredLogsToCsvAsync(localPath).ConfigureAwait(true);
                }
            }
        };

        Button refreshBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.Sync,
            this._i18n["Audit.BtnRefresh"],
            drawerVm.RefreshFilterCommand,
            size: 36,
            iconSize: 16);

        Button clearBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.Trash,
            this._i18n["Audit.BtnClear"],
            command: null,
            size: 36,
            iconSize: 16,
            tone: AuditSeverity.Error);
        clearBtn.Click += (_, _) =>
        {
            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Confirm.ClearAuditLogsTitle"],
                this._i18n["Confirm.ClearAuditLogsMessage"],
                this._i18n["Confirm.BtnConfirmDelete"],
                this._i18n["Groups.BtnCancelEditTag"],
                async () =>
                {
                    drawerVm.SelectedCategory = null;
                    drawerVm.SelectedSeverity = null;
                    drawerVm.SelectedTimeRange = AuditTimeRange.AllTime;
                    drawerVm.SelectedAccountUid = null;
                    drawerVm.SearchKeyword = string.Empty;
                    searchFilterBar.SearchTextBox.Text = string.Empty;
                    searchFilterBar.ClearAllFilters(invokeCallbacks: false);
                    if (drawerVm.ClearLogsCommand.CanExecute(null))
                    {
                        await drawerVm.ClearLogsCommand.ExecuteAsync(null).ConfigureAwait(true);
                    }
                });
        };

        Grid.SetColumn(searchFilterBar, 0);
        Grid.SetColumn(exportCsvBtn, 1);
        Grid.SetColumn(refreshBtn, 2);
        Grid.SetColumn(clearBtn, 3);
        filterBarRow.Children.Add(searchFilterBar);
        filterBarRow.Children.Add(exportCsvBtn);
        filterBarRow.Children.Add(refreshBtn);
        filterBarRow.Children.Add(clearBtn);
        Grid.SetRow(filterBarRow, 1);
        pageGrid.Children.Add(filterBarRow);

        // Row 2: DataTableView<AuditLogRecord> with column headers and empty state
        DataTableView<AuditLogRecord> auditTable = new(
            this._theme,
            [
                new DataTableColumn("96", this._i18n["Table.ColTime"]),
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
            severityCategory.Title = this._i18n["Audit.FilterCatSeverity"];
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
            AppButton.SetText(exportCsvBtn, this._i18n["Audit.BtnExportCsv"]);
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
