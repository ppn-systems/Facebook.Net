// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tab 2 of <see cref="OverviewPage"/>: Active Campaign Target Groups &amp; Buyer Care Donut Chart.
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
}
