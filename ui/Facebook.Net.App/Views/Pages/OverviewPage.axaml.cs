// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Smart Overview / Home Dashboard Page (<c>OverviewPage.axaml</c>).
/// Displays 4 interactive KPI cards, an Active Session &amp; Campaign Readiness Checklist card with 1-click actions,
/// and a real-time Recent Activity <see cref="DataTableView{T}"/>.
/// </summary>
public sealed partial class OverviewPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverviewPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public OverviewPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverviewPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public OverviewPage(ShellViewModel viewModel)
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
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };

        Grid headerRow = SectionHeader.Create(
            this._theme,
            this._i18n["Nav.Overview"],
            this._i18n["Overview.InfoTip"],
            out TextBlock titleTb,
            out TextBlock infoTb);
        Grid.SetRow(headerRow, 0);
        pageGrid.Children.Add(headerRow);

        // Row 1: 4 Interactive KPI Stat Cards (click to navigate directly to each page)
        Grid statsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 8
        };

        Border cardAcc = KpiStatCard.Create(
            this._theme,
            this._i18n["Nav.Accounts"],
            "0",
            VectorIcons.Accounts,
            out TextBlock accTitleTb,
            out TextBlock accValTb,
            this._vm.NavigateAccountsCommand);

        Border cardGrp = KpiStatCard.Create(
            this._theme,
            this._i18n["Nav.Groups"],
            "0",
            VectorIcons.Groups,
            out TextBlock grpTitleTb,
            out TextBlock grpValTb,
            this._vm.NavigateGroupsCommand);

        Border cardLeads = KpiStatCard.Create(
            this._theme,
            this._i18n["Nav.SalesLeads"],
            "0",
            VectorIcons.Search,
            out TextBlock leadsTitleTb,
            out TextBlock leadsValTb,
            this._vm.NavigateSalesLeadsCommand);

        Border cardOk = KpiStatCard.Create(
            this._theme,
            this._i18n["Nav.AuditLogs"],
            "0",
            VectorIcons.CheckCircle,
            out TextBlock okTitleTb,
            out TextBlock okValTb,
            this._vm.NavigateAuditLogsCommand);

        Grid.SetColumn(cardAcc, 0);
        Grid.SetColumn(cardGrp, 1);
        Grid.SetColumn(cardLeads, 2);
        Grid.SetColumn(cardOk, 3);
        statsRow.Children.Add(cardAcc);
        statsRow.Children.Add(cardGrp);
        statsRow.Children.Add(cardLeads);
        statsRow.Children.Add(cardOk);
        Grid.SetRow(statsRow, 1);
        pageGrid.Children.Add(statsRow);

        // Row 2: 2-Column Smart Dashboard Workspace
        Grid mainSplit = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.05*,1.25*"),
            ColumnSpacing = 10
        };

        // Left Card: Active Session Status, Readiness Checklist & Quick Actions
        StackPanel leftStack = new() { Spacing = 10 };

        StackPanel sessionHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.SessionCardTitle"],
            VectorIcons.Shield,
            out TextBlock sessionTitleTb);
        leftStack.Children.Add(sessionHeader);

        // Active Account Banner inside Left Card
        Grid accountBannerGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control heroIcon = VectorIcons.Create(VectorIcons.CheckCircle, 24, margin: new Thickness(0));
        Border heroCircle = new()
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Child = heroIcon,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel statusTextStack = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        TextBlock statusHeading = SectionHeader.CreateLabel(this._theme, this._i18n["Overview.StatusDisconnected"], 15, FontWeight.Bold);
        TextBlock statusSub = SectionHeader.CreateLabel(this._theme, this._i18n["Overview.SubtextDisconnected"], 11, FontWeight.Normal, isSecondary: true);
        statusSub.MaxLines = 2;
        statusTextStack.Children.Add(statusHeading);
        statusTextStack.Children.Add(statusSub);

        Border liveStatusPill = StatusPill.Create(this._theme, this._i18n["Overview.StatusDisconnected"], AuditSeverity.Warning);

        Grid.SetColumn(heroCircle, 0);
        Grid.SetColumn(statusTextStack, 1);
        Grid.SetColumn(liveStatusPill, 2);
        accountBannerGrid.Children.Add(heroCircle);
        accountBannerGrid.Children.Add(statusTextStack);
        accountBannerGrid.Children.Add(liveStatusPill);

        Border accountBannerWell = SurfaceCard.CreateInset(this._theme, accountBannerGrid, new Thickness(12, 10));
        leftStack.Children.Add(accountBannerWell);

        // Campaign Readiness Checklist
        StackPanel checklistStack = new() { Spacing = 6 };
        Border accReadyPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.CheckCircle,
            this._i18n["Overview.CheckAccountMissing"],
            AuditSeverity.Warning,
            out TextBlock accReadyTb);
        Border grpReadyPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.Groups,
            this._i18n["Overview.CheckGroupsMissing"],
            AuditSeverity.Info,
            out TextBlock grpReadyTb);
        checklistStack.Children.Add(accReadyPill);
        checklistStack.Children.Add(grpReadyPill);
        leftStack.Children.Add(checklistStack);

        // Quick Actions 2x2 Grid
        StackPanel quickActionsHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.QuickActionsTitle"],
            VectorIcons.BrandMark,
            out TextBlock quickActionsTitleTb);
        leftStack.Children.Add(quickActionsHeader);

        Grid quickActionsGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8
        };

        Button openBrowserBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Button liveCheckBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnCheckHealth"],
            this._vm.CheckAccountHealthCommand,
            VectorIcons.CheckCircle);

        Button syncGroupsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Button warmUpBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnWarmUp"],
            this._vm.WarmUpAccountCommand,
            VectorIcons.ThumbUp);

        Grid.SetColumn(openBrowserBtn, 0);
        Grid.SetRow(openBrowserBtn, 0);
        Grid.SetColumn(liveCheckBtn, 1);
        Grid.SetRow(liveCheckBtn, 0);
        Grid.SetColumn(syncGroupsBtn, 0);
        Grid.SetRow(syncGroupsBtn, 1);
        Grid.SetColumn(warmUpBtn, 1);
        Grid.SetRow(warmUpBtn, 1);

        quickActionsGrid.Children.Add(openBrowserBtn);
        quickActionsGrid.Children.Add(liveCheckBtn);
        quickActionsGrid.Children.Add(syncGroupsBtn);
        quickActionsGrid.Children.Add(warmUpBtn);
        leftStack.Children.Add(quickActionsGrid);

        Border leftCard = SurfaceCard.Create(this._theme, leftStack, new Thickness(14));

        // Right Card: Live Recent Activity Table (DataTableView<AuditLogRecord>)
        Grid rightGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8
        };

        StackPanel recentLogsHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.RecentLogsTitle"],
            VectorIcons.AuditLog,
            out TextBlock recentLogsTitleTb);
        Grid.SetRow(recentLogsHeader, 0);
        rightGrid.Children.Add(recentLogsHeader);

        DataTableView<AuditLogRecord> recentLogsTable = new(
            this._theme,
            [
                new DataTableColumn("64", this._i18n["Table.ColTime"]),
                new DataTableColumn("95", this._i18n["Table.ColCategory"]),
                new DataTableColumn("*", this._i18n["Table.ColMessage"])
            ],
            this._vm.AuditDrawer.FilteredLogs,
            rec => AuditLogTableRow.CreateCompact(this._theme, this._i18n, rec),
            this._i18n["Empty.AuditLogs"],
            VectorIcons.AuditLog);
        Grid.SetRow(recentLogsTable, 1);
        rightGrid.Children.Add(recentLogsTable);

        Border rightCard = SurfaceCard.Create(this._theme, rightGrid, new Thickness(14));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        mainSplit.Children.Add(leftCard);
        mainSplit.Children.Add(rightCard);
        Grid.SetRow(mainSplit, 2);
        pageGrid.Children.Add(mainSplit);

        void RefreshOverviewState()
        {
            ThemePalette p = this._theme.Palette;
            AccountProfile? acc = this._vm.SelectedAccount;
            bool isConnected = acc is not null && acc.Status == AccountStatus.Active;

            if (this._vm.IsBusy)
            {
                statusHeading.Text = this._i18n["Overview.StatusRunning"];
                statusHeading.Foreground = p.BrandPrimary;
                heroCircle.Background = p.BrandSubtleFill;
                heroCircle.BorderBrush = p.BrandPrimary;
                VectorIcons.SetForeground(heroIcon, p.BrandPrimary);
                statusSub.Text = this._vm.AuditDrawer.LatestTickerText;
                StatusPill.SetText(liveStatusPill, this._i18n["Overview.StatusRunning"]);
            }
            else if (isConnected && acc is not null)
            {
                statusHeading.Text = acc.DisplayName;
                statusHeading.Foreground = p.SuccessText;
                heroCircle.Background = p.SuccessFill;
                heroCircle.BorderBrush = p.SuccessBorder;
                VectorIcons.SetForeground(heroIcon, p.SuccessText);
                statusSub.Text = this._i18n["Overview.SubtextConnected"];
                StatusPill.SetText(liveStatusPill, this._i18n["Overview.StatusConnected"]);
            }
            else
            {
                statusHeading.Text = acc is not null
                    ? acc.DisplayName
                    : this._i18n["Overview.StatusDisconnected"];
                statusHeading.Foreground = p.TextPrimary;
                heroCircle.Background = p.SecondarySurface;
                heroCircle.BorderBrush = p.SubtleBorder;
                VectorIcons.SetForeground(heroIcon, p.BrandPrimary);
                statusSub.Text = this._i18n["Overview.SubtextDisconnected"];
                StatusPill.SetText(
                    liveStatusPill,
                    acc is not null ? this._i18n.FormatAccountStatus(acc.Status) : this._i18n["Overview.StatusDisconnected"]);
            }

            int enabledTargets = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledTargets++;
                }
            }

            accReadyTb.Text = acc is not null
                ? $"{this._i18n["Overview.CheckAccountReady"]}: {acc.DisplayName}"
                : this._i18n["Overview.CheckAccountMissing"];

            grpReadyTb.Text = enabledTargets > 0
                ? $"{this._i18n["Overview.CheckGroupsReady"]}: {enabledTargets}/{this._vm.TargetGroups.Count}"
                : this._i18n["Overview.CheckGroupsMissing"];

            accValTb.Text = this._vm.Accounts.Count.ToString(CultureInfo.InvariantCulture);
            grpValTb.Text = $"{enabledTargets}/{this._vm.TargetGroups.Count}";
            leadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(CultureInfo.InvariantCulture);
            okValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);
        }

        RefreshOverviewState();
        this._theme.ThemeChanged += (_, _) => RefreshOverviewState();
        this._vm.PropertyChanged += (_, _) => RefreshOverviewState();
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.TargetGroups.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.AuditDrawer.PropertyChanged += (_, _) => RefreshOverviewState();

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Nav.Overview"];
            infoTb.Text = this._i18n["Overview.InfoTip"];
            accTitleTb.Text = this._i18n["Nav.Accounts"];
            grpTitleTb.Text = this._i18n["Nav.Groups"];
            leadsTitleTb.Text = this._i18n["Nav.SalesLeads"];
            okTitleTb.Text = this._i18n["Nav.AuditLogs"];
            sessionTitleTb.Text = this._i18n["Overview.SessionCardTitle"];
            quickActionsTitleTb.Text = this._i18n["Overview.QuickActionsTitle"];
            recentLogsTitleTb.Text = this._i18n["Overview.RecentLogsTitle"];
            recentLogsTable.UpdateHeadersAndEmptyText(
                [
                    this._i18n["Table.ColTime"],
                    this._i18n["Table.ColCategory"],
                    this._i18n["Table.ColMessage"]
                ],
                this._i18n["Empty.AuditLogs"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            AppButton.SetText(liveCheckBtn, this._i18n["Accounts.BtnCheckHealth"]);
            AppButton.SetText(syncGroupsBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(warmUpBtn, this._i18n["Accounts.BtnWarmUp"]);
            RefreshOverviewState();
        };

        return pageGrid;
    }
}
