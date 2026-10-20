// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tab 2 of <see cref="OverviewPage"/>: Operational Data Analytics Hub with 3 interactive SVG Donut Charts.
/// </summary>
public sealed partial class OverviewPage
{
    private Control BuildDataAnalyticsTab(out Action refreshState, out Action refreshI18n)
    {
        Grid analyticsGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 14
        };

        Grid headerRow = SectionHeader.Create(
            this._theme,
            this._i18n["Analytics.Header"],
            this._i18n["Analytics.Desc"],
            out TextBlock headerTitleTb,
            out TextBlock headerDescTb);
        Grid.SetRow(headerRow, 0);
        analyticsGrid.Children.Add(headerRow);

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
            size: 164,
            strokeThickness: 24);

        SvgDonutChart leadsChart = new(
            this._theme,
            this._i18n["Analytics.ChartLeadsTitle"],
            VectorIcons.Search,
            this._i18n["Analytics.TotalLabel"],
            size: 164,
            strokeThickness: 24);

        SvgDonutChart activityChart = new(
            this._theme,
            this._i18n["Analytics.ChartActivityTitle"],
            VectorIcons.BrandMark,
            this._i18n["Analytics.TotalLabel"],
            size: 164,
            strokeThickness: 24);

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
        Grid.SetRow(chartsScroll, 1);
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
            int personalOps = 0;
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
                    case AuditCategory.Personal:
                        personalOps++;
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
                new SvgDonutSlice(this._i18n["Analytics.LegendPersonal"], personalOps, "#8B5CF6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendPublish"], publishOps, "#3B82F6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSalesComment"], commentOps, "#10B981"),
                new SvgDonutSlice(this._i18n["Analytics.LegendScrape"], scrapeOps, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSync"], syncOps, "#06B6D4"),
                new SvgDonutSlice(this._i18n["Analytics.LegendAlerts"], alertOps, "#EF4444")
            ]);
        };

        refreshI18n = () =>
        {
            headerTitleTb.Text = this._i18n["Analytics.Header"];
            headerDescTb.Text = this._i18n["Analytics.Desc"];
            accountsChart.UpdateLabels(this._i18n["Analytics.ChartAccountsTitle"], this._i18n["Analytics.TotalLabel"]);
            leadsChart.UpdateLabels(this._i18n["Analytics.ChartLeadsTitle"], this._i18n["Analytics.TotalLabel"]);
            activityChart.UpdateLabels(this._i18n["Analytics.ChartActivityTitle"], this._i18n["Analytics.TotalLabel"]);
        };

        return analyticsGrid;
    }
}
