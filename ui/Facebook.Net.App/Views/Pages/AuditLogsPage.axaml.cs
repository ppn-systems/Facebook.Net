// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.ObjectModel;
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
/// Navigated to from the bottom of <see cref="SettingsPage"/> or the footer status ticker.
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

        Grid header = UiControls.CreateSectionHeader(
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

        Border okPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.CheckCircle,
            drawerVm.SuccessCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Success,
            out TextBlock okCountTb);

        Border warnPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.Warning,
            drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Warning,
            out TextBlock warnCountTb);

        Border errPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.ErrorCircle,
            drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Error,
            out TextBlock errCountTb);

        countersRow.Children.Add(okPill);
        countersRow.Children.Add(warnPill);
        countersRow.Children.Add(errPill);

        Button backToSettingsBtn = UiControls.CreateSecondaryButton(
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

        // Unified 1-Row Filter & Action Bar: Category, TimeRange, Account, Keyword Search, Refresh, Clear
        Grid filterBarRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1*,1.1*,1.4*,Auto,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        ComboBox categoryCombo = UiControls.CreateComboBox<object>(
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
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditCategory(cat), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditCategory(cat);
                return tb;
            },
            selectedItem: "ALL");
        categoryCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedCategory = categoryCombo.SelectedItem is AuditCategory cat ? cat : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(categoryCombo, 0);

        ComboBox timeRangeCombo = UiControls.CreateComboBox<AuditTimeRange>(
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
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditTimeRange(r), 12);
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
        Grid.SetColumn(timeRangeCombo, 1);

        ObservableCollection<object> accountFilterItems = new() { "ALL" };
        for (int i = 0; i < this._vm.Accounts.Count; i++)
        {
            accountFilterItems.Add(this._vm.Accounts[i]);
        }

        ComboBox accountFilterCombo = UiControls.CreateComboBox<object>(
            this._theme,
            accountFilterItems,
            obj =>
            {
                if (obj is AccountProfile acc)
                {
                    return UiControls.CreateThemedLabel(this._theme, acc.DisplayName, 12);
                }

                TextBlock allTb = UiControls.CreateThemedLabel(this._theme, this._i18n["Audit.FilterAccountAll"], 12);
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
        Grid.SetColumn(accountFilterCombo, 2);

        TextBox searchBox = UiControls.CreateTextBox(this._theme, this._i18n["Audit.SearchPlaceholder"]);
        searchBox.TextChanged += async (_, _) =>
        {
            drawerVm.SearchKeyword = searchBox.Text ?? string.Empty;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(searchBox, 3);

        Button refreshBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnRefresh"],
            drawerVm.RefreshFilterCommand,
            VectorIcons.Sync);
        Grid.SetColumn(refreshBtn, 4);

        Button clearBtn = UiControls.CreateDangerButton(
            this._theme,
            this._i18n["Audit.BtnClear"],
            drawerVm.ClearLogsCommand,
            VectorIcons.Trash);
        Grid.SetColumn(clearBtn, 5);

        filterBarRow.Children.Add(categoryCombo);
        filterBarRow.Children.Add(timeRangeCombo);
        filterBarRow.Children.Add(accountFilterCombo);
        filterBarRow.Children.Add(searchBox);
        filterBarRow.Children.Add(refreshBtn);
        filterBarRow.Children.Add(clearBtn);
        Grid.SetRow(filterBarRow, 1);
        pageGrid.Children.Add(filterBarRow);

        Border emptyLogsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.AuditLogs"],
            VectorIcons.AuditLog,
            out TextBlock emptyLogsMsg);
        emptyLogsBanner.IsVisible = drawerVm.FilteredLogs.Count == 0;

        ListBox logList = UiControls.CreateListBox<AuditLogRecord>(
            this._theme,
            drawerVm.FilteredLogs,
            rec => UiControls.CreateAuditLogItemRow(this._theme, this._i18n, rec),
            isVisible: drawerVm.FilteredLogs.Count > 0);

        drawerVm.FilteredLogs.CollectionChanged += (_, _) =>
        {
            bool isEmpty = drawerVm.FilteredLogs.Count == 0;
            emptyLogsBanner.IsVisible = isEmpty;
            logList.IsVisible = !isEmpty;
        };

        Grid logContentHost = UiControls.CreateListWithEmptyStateHost(logList, emptyLogsBanner);
        Grid.SetRow(logContentHost, 2);
        pageGrid.Children.Add(logContentHost);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Audit.TaskbarTitle"];
            infoTb.Text = this._i18n["Audit.SearchPlaceholder"];
            accountFilterCombo.PlaceholderText = this._i18n["Audit.FilterAccountAll"];
            searchBox.PlaceholderText = this._i18n["Audit.SearchPlaceholder"];
            emptyLogsMsg.Text = this._i18n["Empty.AuditLogs"];
            UiControls.SetButtonText(backToSettingsBtn, this._i18n["Audit.BtnBackToSettings"]);
            UiControls.SetButtonText(refreshBtn, this._i18n["Audit.BtnRefresh"]);
            UiControls.SetButtonText(clearBtn, this._i18n["Audit.BtnClear"]);
        };

        drawerVm.PropertyChanged += (_, _) =>
        {
            okCountTb.Text = drawerVm.SuccessCount.ToString(CultureInfo.InvariantCulture);
            warnCountTb.Text = drawerVm.WarningCount.ToString(CultureInfo.InvariantCulture);
            errCountTb.Text = drawerVm.ErrorCount.ToString(CultureInfo.InvariantCulture);
        };

        return UiControls.CreateCard(this._theme, pageGrid);
    }
}
