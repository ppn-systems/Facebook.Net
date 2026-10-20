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
/// Uses a top <see cref="SubTabBar"/> to cleanly separate:
/// 1) Main KPI Dashboard, Session Readiness &amp; Buyer Leads Queue,
/// 2) Active Campaign Target Groups &amp; Buyer Care Donut Chart.
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
        Control tab1View = this.BuildDashboardTab(out Action refreshTab1, out Action refreshTab1I18n);
        Control tab2View = this.BuildCampaignsAndChartTab(out Action refreshTab2, out Action refreshTab2I18n);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Overview.TabDashboard"], VectorIcons.BrandMark, tab1View),
                new SubTabItem(this._i18n["Overview.TabCampaigns"], VectorIcons.Groups, tab2View)
            ]);

        void RefreshAll()
        {
            refreshTab1();
            refreshTab2();
        }

        RefreshAll();
        this._theme.ThemeChanged += (_, _) => RefreshAll();
        this._vm.PropertyChanged += (_, _) => RefreshAll();
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshAll();
        this._vm.TargetGroups.CollectionChanged += (_, _) => RefreshAll();
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => RefreshAll();
        this._vm.AuditDrawer.PropertyChanged += (_, _) => RefreshAll();

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Overview.TabDashboard"],
                this._i18n["Overview.TabCampaigns"]
            ]);
            refreshTab1I18n();
            refreshTab2I18n();
            RefreshAll();
        };

        return subTabBar;
    }

    private Control BuildDashboardTab(out Action refreshState, out Action refreshI18n)
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
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
            ColumnSpacing = 10
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

        // Row 2: Spacious 2-Column Smart Dashboard Workspace
        Grid mainSplit = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1*,1.35*"),
            ColumnSpacing = 12
        };

        // Left Card: Active Session Status, Readiness Checklist & Quick Actions
        StackPanel leftStack = new() { Spacing = 12 };

        StackPanel sessionHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.SessionCardTitle"],
            VectorIcons.Shield,
            out TextBlock sessionTitleTb);
        leftStack.Children.Add(sessionHeader);

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
        TextBlock statusHeading = SectionHeader.CreateLabel(this._theme, this._i18n["Overview.StatusDisconnected"], 14, FontWeight.Bold);
        statusHeading.MaxLines = 1;
        statusHeading.TextTrimming = TextTrimming.CharacterEllipsis;
        TextBlock statusSub = SectionHeader.CreateLabel(this._theme, this._i18n["Overview.SubtextDisconnected"], 11.5, FontWeight.Normal, isSecondary: true);
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
        StackPanel checklistStack = new() { Spacing = 7 };
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
        Border delayReadyPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.Shield,
            $"{this._i18n["Overview.CheckDelayReady"]}: {this._vm.MinDelaySeconds}s – {this._vm.MaxDelaySeconds}s",
            AuditSeverity.Success,
            out TextBlock delayReadyTb);
        checklistStack.Children.Add(accReadyPill);
        checklistStack.Children.Add(grpReadyPill);
        checklistStack.Children.Add(delayReadyPill);
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
            this._i18n["Overview.QuickOpenBrowser"],
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

        Border leftCard = SurfaceCard.Create(this._theme, leftStack, new Thickness(16, 14));

        // Right Card: Full-Height Latest Buyer Leads Needing Reply
        Grid pendingLeadsCardGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid pendingLeadsHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel pendingLeadsHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.PendingLeadsTitle"],
            VectorIcons.Search,
            out TextBlock pendingLeadsTitleTb);

        Button quickScrapeBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Overview.BtnScrapeNow"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        Button goToLeadsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Overview.BtnGoToLeads"],
            this._vm.NavigateSalesLeadsCommand,
            VectorIcons.Search);

        Grid.SetColumn(pendingLeadsHeader, 0);
        Grid.SetColumn(quickScrapeBtn, 1);
        Grid.SetColumn(goToLeadsBtn, 2);
        pendingLeadsHeaderRow.Children.Add(pendingLeadsHeader);
        pendingLeadsHeaderRow.Children.Add(quickScrapeBtn);
        pendingLeadsHeaderRow.Children.Add(goToLeadsBtn);
        Grid.SetRow(pendingLeadsHeaderRow, 0);
        pendingLeadsCardGrid.Children.Add(pendingLeadsHeaderRow);

        ContentControl pendingLeadsHost = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(pendingLeadsHost, 1);
        pendingLeadsCardGrid.Children.Add(pendingLeadsHost);

        Border pendingLeadsCard = SurfaceCard.Create(this._theme, pendingLeadsCardGrid, new Thickness(16, 14));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(pendingLeadsCard, 1);
        mainSplit.Children.Add(leftCard);
        mainSplit.Children.Add(pendingLeadsCard);
        Grid.SetRow(mainSplit, 2);
        pageGrid.Children.Add(mainSplit);

        refreshState = () =>
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
                StatusPill.SetSeverity(liveStatusPill, this._theme, AuditSeverity.Info);
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
                StatusPill.SetSeverity(liveStatusPill, this._theme, AuditSeverity.Success);
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
                StatusPill.SetSeverity(liveStatusPill, this._theme, AuditSeverity.Warning);
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
            IconMetricPill.SetSeverity(accReadyPill, isConnected ? AuditSeverity.Success : AuditSeverity.Warning);

            grpReadyTb.Text = enabledTargets > 0
                ? $"{this._i18n["Overview.CheckGroupsReady"]}: {enabledTargets}/{this._vm.TargetGroups.Count}"
                : this._i18n["Overview.CheckGroupsMissing"];
            IconMetricPill.SetSeverity(grpReadyPill, enabledTargets > 0 ? AuditSeverity.Success : AuditSeverity.Info);

            delayReadyTb.Text = $"{this._i18n["Overview.CheckDelayReady"]}: {this._vm.MinDelaySeconds}s – {this._vm.MaxDelaySeconds}s";
            IconMetricPill.SetSeverity(delayReadyPill, AuditSeverity.Success);

            accValTb.Text = this._vm.Accounts.Count.ToString(CultureInfo.InvariantCulture);
            grpValTb.Text = $"{enabledTargets}/{this._vm.TargetGroups.Count}";
            leadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(CultureInfo.InvariantCulture);
            okValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);

            // Populate Latest Uncommented Buyer Leads Widget
            StackPanel leadsListStack = new() { Spacing = 8 };
            int shownLeads = 0;

            for (int i = 0; i < this._vm.ScrapedPosts.Count; i++)
            {
                PostSummary post = this._vm.ScrapedPosts[i];
                if (this._vm.IsPostCommented(post.PostId))
                {
                    continue;
                }

                if (shownLeads >= 8)
                {
                    break;
                }

                shownLeads++;
                PostSummary capturedPost = post;
                int score = ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords);
                string author = string.IsNullOrWhiteSpace(post.AuthorName) ? this._i18n["Leads.DefaultAuthor"] : post.AuthorName;
                string snippet = string.IsNullOrWhiteSpace(post.MessageText)
                    ? this._i18n["Leads.EmptyPostText"]
                    : post.MessageText.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');

                Grid itemGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Border avatar = AvatarBadge.Create(this._theme, author, 32, post.AuthorAvatarUrl);
                StackPanel textCol = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                TextBlock authorLine = SectionHeader.CreateLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.GroupName) ? author : $"{author}  •  {post.GroupName}",
                    12,
                    FontWeight.SemiBold);
                authorLine.MaxLines = 1;
                authorLine.TextTrimming = TextTrimming.CharacterEllipsis;

                TextBlock msgLine = SectionHeader.CreateLabel(this._theme, snippet, 11.5, FontWeight.Normal, isSecondary: true);
                msgLine.MaxLines = 1;
                msgLine.TextTrimming = TextTrimming.CharacterEllipsis;
                textCol.Children.Add(authorLine);
                textCol.Children.Add(msgLine);

                Border intentBadge = StatusPill.Create(
                    this._theme,
                    this._vm.FormatBuyerIntentBadge(post),
                    score >= 70 ? AuditSeverity.Success : AuditSeverity.Info);

                Grid.SetColumn(avatar, 0);
                Grid.SetColumn(textCol, 1);
                Grid.SetColumn(intentBadge, 2);
                itemGrid.Children.Add(avatar);
                itemGrid.Children.Add(textCol);
                itemGrid.Children.Add(intentBadge);

                Button leadRowBtn = new()
                {
                    Content = itemGrid,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 8),
                    CornerRadius = new CornerRadius(8),
                    Background = p.SecondarySurface,
                    BorderBrush = p.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                leadRowBtn.Click += (_, _) =>
                {
                    this._vm.SelectedScrapedPost = capturedPost;
                    this._vm.NavigateSalesLeadsCommand.Execute(null);
                };

                leadsListStack.Children.Add(leadRowBtn);
            }

            if (shownLeads == 0)
            {
                TextBlock emptyLeadsTb = SectionHeader.CreateLabel(
                    this._theme,
                    this._i18n["Empty.Leads"],
                    12,
                    FontWeight.Normal,
                    isSecondary: true);
                emptyLeadsTb.TextWrapping = TextWrapping.Wrap;
                emptyLeadsTb.HorizontalAlignment = HorizontalAlignment.Center;
                emptyLeadsTb.VerticalAlignment = VerticalAlignment.Center;
                pendingLeadsHost.Content = SurfaceCard.CreateInset(this._theme, emptyLeadsTb, new Thickness(16));
            }
            else
            {
                pendingLeadsHost.Content = new ScrollViewer { Content = leadsListStack };
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Nav.Overview"];
            infoTb.Text = this._i18n["Overview.InfoTip"];
            accTitleTb.Text = this._i18n["Nav.Accounts"];
            grpTitleTb.Text = this._i18n["Nav.Groups"];
            leadsTitleTb.Text = this._i18n["Nav.SalesLeads"];
            okTitleTb.Text = this._i18n["Nav.AuditLogs"];
            sessionTitleTb.Text = this._i18n["Overview.SessionCardTitle"];
            quickActionsTitleTb.Text = this._i18n["Overview.QuickActionsTitle"];
            pendingLeadsTitleTb.Text = this._i18n["Overview.PendingLeadsTitle"];
            AppButton.SetText(quickScrapeBtn, this._i18n["Overview.BtnScrapeNow"]);
            AppButton.SetText(goToLeadsBtn, this._i18n["Overview.BtnGoToLeads"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Overview.QuickOpenBrowser"]);
            AppButton.SetText(liveCheckBtn, this._i18n["Accounts.BtnCheckHealth"]);
            AppButton.SetText(syncGroupsBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(warmUpBtn, this._i18n["Accounts.BtnWarmUp"]);
        };

        return pageGrid;
    }
}
