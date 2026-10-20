// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tab 2 &amp; Tab 3 of <see cref="OverviewPage"/>: Active Campaign Target Groups and Operational Data Analytics Hub.
/// </summary>
public sealed partial class OverviewPage
{
    private Control BuildCampaignsAndChartTab(out Action refreshState, out Action refreshI18n)
    {
        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1*"),
            ColumnSpacing = 12
        };

        // Left Card: Full-Height Active Campaign Groups List & Quick Publish Action
        Grid activeGroupsCardGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid activeGroupsHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel activeGroupsHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Overview.ActiveGroupsTitle"],
            VectorIcons.Groups,
            out TextBlock activeGroupsTitleTb);

        Button goToPublisherBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Overview.BtnGoToPublisher"],
            this._vm.NavigatePublisherCommand,
            VectorIcons.Publish);

        Grid.SetColumn(activeGroupsHeader, 0);
        Grid.SetColumn(goToPublisherBtn, 1);
        activeGroupsHeaderRow.Children.Add(activeGroupsHeader);
        activeGroupsHeaderRow.Children.Add(goToPublisherBtn);
        Grid.SetRow(activeGroupsHeaderRow, 0);
        activeGroupsCardGrid.Children.Add(activeGroupsHeaderRow);

        ContentControl activeGroupsHost = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(activeGroupsHost, 1);
        activeGroupsCardGrid.Children.Add(activeGroupsHost);

        Border activeGroupsCard = SurfaceCard.Create(this._theme, activeGroupsCardGrid, new Thickness(16, 14));

        // Right Card: Full-Size Multi-Color Buyer Care SVG Donut Chart
        SvgDonutChart overviewLeadsChart = new(
            this._theme,
            this._i18n["Analytics.ChartLeadsTitle"],
            VectorIcons.BrandMark,
            this._i18n["Analytics.TotalLabel"],
            size: 164,
            strokeThickness: 24);

        Grid.SetColumn(activeGroupsCard, 0);
        Grid.SetColumn(overviewLeadsChart, 1);
        splitGrid.Children.Add(activeGroupsCard);
        splitGrid.Children.Add(overviewLeadsChart);

        refreshState = () =>
        {
            int highIntentPending = 0;
            int warmPending = 0;
            int commentedLeads = 0;

            for (int i = 0; i < this._vm.ScrapedPosts.Count; i++)
            {
                PostSummary post = this._vm.ScrapedPosts[i];
                if (this._vm.IsPostCommented(post.PostId))
                {
                    commentedLeads++;
                    continue;
                }

                int score = ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords);
                if (score >= 70)
                {
                    highIntentPending++;
                }
                else
                {
                    warmPending++;
                }
            }

            overviewLeadsChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendHighIntentPending"], highIntentPending, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendWarmPending"], warmPending, "#8B5CF6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendCommented"], commentedLeads, "#10B981")
            ]);

            // Populate Active Target Groups Widget
            StackPanel groupsStack = new() { Spacing = 8 };
            int shownGroups = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup grp = this._vm.TargetGroups[i];
                if (!grp.IsEnabled)
                {
                    continue;
                }

                shownGroups++;
                Grid grpRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Border grpAvatar = AvatarBadge.Create(this._theme, grp.GroupName, 28, grp.AvatarUrl);
                TextBlock grpNameTb = SectionHeader.CreateLabel(this._theme, grp.GroupName, 12.5, FontWeight.SemiBold);
                grpNameTb.MaxLines = 1;
                grpNameTb.TextTrimming = TextTrimming.CharacterEllipsis;
                Border activeBadge = StatusPill.Create(this._theme, this._i18n["Status.Group.Member"], AuditSeverity.Success);

                Grid.SetColumn(grpAvatar, 0);
                Grid.SetColumn(grpNameTb, 1);
                Grid.SetColumn(activeBadge, 2);
                grpRow.Children.Add(grpAvatar);
                grpRow.Children.Add(grpNameTb);
                grpRow.Children.Add(activeBadge);
                groupsStack.Children.Add(SurfaceCard.CreateInset(this._theme, grpRow, new Thickness(10, 7)));
            }

            if (shownGroups == 0)
            {
                TextBlock emptyGrpTb = SectionHeader.CreateLabel(
                    this._theme,
                    this._i18n["Empty.TargetGroups"],
                    12,
                    FontWeight.Normal,
                    isSecondary: true);
                emptyGrpTb.TextWrapping = TextWrapping.Wrap;
                emptyGrpTb.HorizontalAlignment = HorizontalAlignment.Center;
                emptyGrpTb.VerticalAlignment = VerticalAlignment.Center;
                activeGroupsHost.Content = SurfaceCard.CreateInset(this._theme, emptyGrpTb, new Thickness(16));
            }
            else
            {
                activeGroupsHost.Content = new ScrollViewer { Content = groupsStack };
            }
        };

        refreshI18n = () =>
        {
            activeGroupsTitleTb.Text = this._i18n["Overview.ActiveGroupsTitle"];
            overviewLeadsChart.UpdateLabels(this._i18n["Analytics.ChartLeadsTitle"], this._i18n["Analytics.TotalLabel"]);
            AppButton.SetText(goToPublisherBtn, this._i18n["Overview.BtnGoToPublisher"]);
        };

        return splitGrid;
    }

    private Control BuildDataAnalyticsTab(out Action refreshState, out Action refreshI18n)
    {
        Grid analyticsGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        Grid headerRow = SectionHeader.Create(
            this._theme,
            this._i18n["Analytics.Header"],
            this._i18n["Analytics.Desc"],
            out TextBlock headerTitleTb,
            out TextBlock headerDescTb);
        Grid.SetRow(headerRow, 0);
        analyticsGrid.Children.Add(headerRow);

        Grid kpiRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };

        Border kpiAcc = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.AccountsTitle"],
            "0",
            VectorIcons.Accounts,
            out TextBlock kpiAccTitleTb,
            out TextBlock kpiAccValTb,
            this._vm.NavigateAccountsCommand);

        Border kpiGrp = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.GroupsTitle"],
            "0",
            VectorIcons.Groups,
            out TextBlock kpiGrpTitleTb,
            out TextBlock kpiGrpValTb,
            this._vm.NavigateGroupsCommand);

        Border kpiLeads = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.LeadsTitle"],
            "0",
            VectorIcons.Search,
            out TextBlock kpiLeadsTitleTb,
            out TextBlock kpiLeadsValTb,
            this._vm.NavigateSalesLeadsCommand);

        Border kpiActions = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.TelemetryTitle"],
            "0",
            VectorIcons.CheckCircle,
            out TextBlock kpiActionsTitleTb,
            out TextBlock kpiActionsValTb);

        Grid.SetColumn(kpiAcc, 0);
        Grid.SetColumn(kpiGrp, 1);
        Grid.SetColumn(kpiLeads, 2);
        Grid.SetColumn(kpiActions, 3);
        kpiRow.Children.Add(kpiAcc);
        kpiRow.Children.Add(kpiGrp);
        kpiRow.Children.Add(kpiLeads);
        kpiRow.Children.Add(kpiActions);
        Grid.SetRow(kpiRow, 1);
        analyticsGrid.Children.Add(kpiRow);

        Grid chartsGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 12
        };

        SvgDonutChart accountsChart = new(
            this._theme,
            this._i18n["Analytics.ChartAccountsTitle"],
            VectorIcons.Accounts,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        SvgDonutChart leadsChart = new(
            this._theme,
            this._i18n["Analytics.ChartLeadsTitle"],
            VectorIcons.Search,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        SvgDonutChart activityChart = new(
            this._theme,
            this._i18n["Analytics.ChartActivityTitle"],
            VectorIcons.BrandMark,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        Grid.SetColumn(accountsChart, 0);
        Grid.SetColumn(leadsChart, 1);
        Grid.SetColumn(activityChart, 2);
        chartsGrid.Children.Add(accountsChart);
        chartsGrid.Children.Add(leadsChart);
        chartsGrid.Children.Add(activityChart);

        ScrollViewer chartsScroll = new()
        {
            Content = chartsGrid
        };
        Grid.SetRow(chartsScroll, 2);
        analyticsGrid.Children.Add(chartsScroll);

        refreshState = () =>
        {
            int activeAcc = 0;
            int idleAcc = 0;
            int expiredAcc = 0;
            int attentionAcc = 0;

            for (int i = 0; i < this._vm.Accounts.Count; i++)
            {
                AccountStatus st = this._vm.Accounts[i].Status;
                if (st is AccountStatus.Active or AccountStatus.Running)
                {
                    activeAcc++;
                }
                else if (st == AccountStatus.Idle)
                {
                    idleAcc++;
                }
                else if (st == AccountStatus.CookieExpired)
                {
                    expiredAcc++;
                }
                else
                {
                    attentionAcc++;
                }
            }

            accountsChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendActive"], activeAcc, "#10B981"),
                new SvgDonutSlice(this._i18n["Analytics.LegendIdle"], idleAcc, "#3B82F6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendExpired"], expiredAcc, "#F59E0B"),
                new SvgDonutSlice(this._i18n["Analytics.LegendAttention"], attentionAcc, "#EF4444")
            ]);

            int highIntentPending = 0;
            int warmPending = 0;
            int commentedLeads = 0;

            for (int i = 0; i < this._vm.ScrapedPosts.Count; i++)
            {
                PostSummary post = this._vm.ScrapedPosts[i];
                if (this._vm.IsPostCommented(post.PostId))
                {
                    commentedLeads++;
                }
                else
                {
                    int score = ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords);
                    if (score >= 70)
                    {
                        highIntentPending++;
                    }
                    else
                    {
                        warmPending++;
                    }
                }
            }

            leadsChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendHighIntentPending"], highIntentPending, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendWarmPending"], warmPending, "#8B5CF6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendCommented"], commentedLeads, "#10B981")
            ]);

            int publishOps = 0;
            int commentOps = 0;
            int scrapeOps = 0;
            int syncOps = 0;
            int alertOps = 0;

            for (int i = 0; i < this._vm.AuditDrawer.FilteredLogs.Count; i++)
            {
                AuditLogRecord rec = this._vm.AuditDrawer.FilteredLogs[i];
                if (rec.Severity is AuditSeverity.Warning or AuditSeverity.Error)
                {
                    alertOps++;
                }

                switch (rec.Category)
                {
                    case AuditCategory.Post:
                        publishOps++;
                        break;
                    case AuditCategory.SalesComment:
                        commentOps++;
                        break;
                    case AuditCategory.Scrape:
                        scrapeOps++;
                        break;
                    case AuditCategory.Account:
                    case AuditCategory.Group:
                    case AuditCategory.System:
                    default:
                        syncOps++;
                        break;
                }
            }

            activityChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendPublish"], publishOps, "#3B82F6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSalesComment"], commentOps, "#10B981"),
                new SvgDonutSlice(this._i18n["Analytics.LegendScrape"], scrapeOps, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSync"], syncOps, "#06B6D4"),
                new SvgDonutSlice(this._i18n["Analytics.LegendAlerts"], alertOps, "#EF4444")
            ]);

            int enabledTargets = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledTargets++;
                }
            }

            kpiAccValTb.Text = this._vm.Accounts.Count.ToString(CultureInfo.InvariantCulture);
            kpiGrpValTb.Text = $"{enabledTargets}/{this._vm.TargetGroups.Count}";
            kpiLeadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(CultureInfo.InvariantCulture);
            kpiActionsValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);
        };

        refreshI18n = () =>
        {
            headerTitleTb.Text = this._i18n["Analytics.Header"];
            headerDescTb.Text = this._i18n["Analytics.Desc"];
            kpiAccTitleTb.Text = this._i18n["Kpi.AccountsTitle"];
            kpiGrpTitleTb.Text = this._i18n["Kpi.GroupsTitle"];
            kpiLeadsTitleTb.Text = this._i18n["Kpi.LeadsTitle"];
            kpiActionsTitleTb.Text = this._i18n["Kpi.TelemetryTitle"];
            accountsChart.UpdateLabels(this._i18n["Analytics.ChartAccountsTitle"], this._i18n["Analytics.TotalLabel"]);
            leadsChart.UpdateLabels(this._i18n["Analytics.ChartLeadsTitle"], this._i18n["Analytics.TotalLabel"]);
            activityChart.UpdateLabels(this._i18n["Analytics.ChartActivityTitle"], this._i18n["Analytics.TotalLabel"]);
        };

        return analyticsGrid;
    }
}
