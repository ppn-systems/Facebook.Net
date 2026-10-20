// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tab 2 of <see cref="SalesLeadsPage"/>: Multi-Group Selection &amp; Anti-Ban Scraper Configuration.
/// </summary>
public sealed partial class SalesLeadsPage
{
    private Control BuildMultiGroupAndScraperConfigTab(out Action refreshI18n)
    {
        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1*"),
            ColumnSpacing = 14
        };

        // Left Column: Multi-Group Selection for Scraping with Quick Group Filter
        Grid leftPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10
        };

        Grid leftHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Leads.ConfigMultiGroupTitle"],
            this._i18n["Leads.ConfigMultiGroupDesc"],
            out TextBlock multiGroupTitleTb,
            out TextBlock multiGroupDescTb);
        Grid.SetRow(leftHeader, 0);
        leftPane.Children.Add(leftHeader);

        Grid bulkGroupBtnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,Auto"),
            ColumnSpacing = 8
        };

        Button enableAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button disableAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand,
            VectorIcons.StopCircle);

        Button syncJoinedBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(enableAllBtn, 0);
        Grid.SetColumn(disableAllBtn, 1);
        Grid.SetColumn(syncJoinedBtn, 2);
        bulkGroupBtnRow.Children.Add(enableAllBtn);
        bulkGroupBtnRow.Children.Add(disableAllBtn);
        bulkGroupBtnRow.Children.Add(syncJoinedBtn);
        Grid.SetRow(bulkGroupBtnRow, 1);
        leftPane.Children.Add(bulkGroupBtnRow);

        // Quick Search & Filter Bar for Target Groups
        BulkObservableCollection<SavedTargetGroup> filteredTargetGroups = new();
        int targetStateFilter = 2; // 0 = Enabled, 1 = Disabled, 2 = All
        SearchFilterBar groupSearchBar = null!;

        void RefreshFilteredGroups()
        {
            string q = (groupSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<SavedTargetGroup> matches = new(this._vm.TargetGroups.Count);
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup tg = this._vm.TargetGroups[i];
                if (targetStateFilter == 0 && !tg.IsEnabled)
                {
                    continue;
                }

                if (targetStateFilter == 1 && tg.IsEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(q) &&
                    !tg.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(tg);
            }

            filteredTargetGroups.ReplaceAll(matches);
        }

        SegmentedFilterPill groupStatePill = new(
            this._theme,
            [
                this._i18n["Groups.FilterSegEnabled"],
                this._i18n["Groups.FilterSegDisabled"],
                this._i18n["Groups.FilterSegAll"]
            ],
            initialIndex: 2,
            onSelected: idx =>
            {
                targetStateFilter = idx;
                groupSearchBar?.SetFilterActive(idx != 2);
                RefreshFilteredGroups();
            });

        Border groupFilterFlyoutCard = SurfaceCard.Create(this._theme, groupStatePill, new Thickness(12));
        groupSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"],
            filterFlyoutContent: groupFilterFlyoutCard);

        groupSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredGroups();
        groupSearchBar.SearchButton.Click += (_, _) => RefreshFilteredGroups();
        Grid.SetRow(groupSearchBar, 2);
        leftPane.Children.Add(groupSearchBar);

        RefreshFilteredGroups();

        ListBox targetGroupsList = FormField.CreateListBox<SavedTargetGroup>(
            this._theme,
            filteredTargetGroups,
            tg => GroupRowCard.CreateTargetRow(
                this._theme,
                tg,
                nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState)),
            selectedItem: this._vm.SelectedTargetGroup,
            isVisible: filteredTargetGroups.Count > 0);

        Border emptyTargetsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.Groups,
            out TextBlock emptyTargetsTb);
        emptyTargetsBanner.IsVisible = filteredTargetGroups.Count == 0;

        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            RefreshFilteredGroups();
            bool isEmpty = filteredTargetGroups.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetGroupsList.IsVisible = !isEmpty;
        };
        filteredTargetGroups.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredTargetGroups.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetGroupsList.IsVisible = !isEmpty;
        };

        Grid targetListHost = EmptyStateBanner.CreateHost(targetGroupsList, emptyTargetsBanner);
        Grid.SetRow(targetListHost, 3);
        leftPane.Children.Add(targetListHost);

        // Quick Add from Joined Groups row at bottom of Left Card
        Grid quickAddJoinedRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        ComboBox joinedGroupPicker = FormField.CreateComboBox<GroupSummary>(
            this._theme,
            this._vm.JoinedGroups,
            g => SectionHeader.CreateLabel(this._theme, g.Name, 12, FontWeight.Medium),
            this._vm.SelectedGroup,
            this._i18n["Groups.Header"]);
        joinedGroupPicker.SelectionChanged += (_, _) =>
        {
            if (joinedGroupPicker.SelectedItem is GroupSummary g)
            {
                this._vm.SelectedGroup = g;
            }
        };

        Button addJoinedToTargetsBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand,
            VectorIcons.Plus);

        Grid.SetColumn(joinedGroupPicker, 0);
        Grid.SetColumn(addJoinedToTargetsBtn, 1);
        quickAddJoinedRow.Children.Add(joinedGroupPicker);
        quickAddJoinedRow.Children.Add(addJoinedToTargetsBtn);
        Grid.SetRow(quickAddJoinedRow, 4);
        leftPane.Children.Add(quickAddJoinedRow);

        Border leftCard = SurfaceCard.Create(this._theme, leftPane, new Thickness(16));

        // Right Column: Selective Scraper & Anti-Ban Rate-Limit Configuration
        StackPanel rightStack = new() { Spacing = 12 };

        StackPanel configHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Leads.ConfigTitle"],
            VectorIcons.Settings,
            out TextBlock configTitleTb);
        rightStack.Children.Add(configHeader);

        Border chronoPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.CheckCircle,
            this._i18n["Leads.ConfigChronologicalBadge"],
            AuditSeverity.Success,
            out TextBlock chronoBadgeTb);
        rightStack.Children.Add(chronoPill);

        TextBox scrapeKeywordsBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Leads.ConfigKeywordsPlaceholder"],
            this._vm.ScrapeFilterKeywords);
        scrapeKeywordsBox.TextChanged += (_, _) =>
            this._vm.ScrapeFilterKeywords = scrapeKeywordsBox.Text ?? string.Empty;

        StackPanel scrapeKeywordsField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigKeywordsLabel"],
            scrapeKeywordsBox,
            out TextBlock scrapeKeywordsLbl);
        rightStack.Children.Add(scrapeKeywordsField);

        // Anti-Ban Rate-Limit Delay between Groups (Min..Max seconds)
        Grid scrapeDelayRow = DelayRangeSelector.Create(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            this._vm.ScrapeDelayMinSeconds,
            this._i18n["Settings.MaxDelayPrefix"],
            this._vm.ScrapeDelayMaxSeconds,
            out TextBlock scrapeMinDelayPrefix,
            out NumericUpDown scrapeMinDelayBox,
            out TextBlock scrapeMaxDelayPrefix,
            out NumericUpDown scrapeMaxDelayBox);

        scrapeMinDelayBox.ValueChanged += (_, _) =>
            this._vm.ScrapeDelayMinSeconds = Math.Clamp((int)(scrapeMinDelayBox.Value ?? 8), 2, 600);
        scrapeMaxDelayBox.ValueChanged += (_, _) =>
            this._vm.ScrapeDelayMaxSeconds = Math.Clamp((int)(scrapeMaxDelayBox.Value ?? 18), this._vm.ScrapeDelayMinSeconds, 600);

        StackPanel scrapeDelayField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigRateLimitLabel"],
            scrapeDelayRow,
            out TextBlock scrapeDelayLbl);
        rightStack.Children.Add(scrapeDelayField);

        // 3-Column Numbers: Posts/Group, Max Groups/Cycle (Round-Robin), Background Interval (min)
        Grid numbersGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
        };

        TextBox postsPerGroupBox = FormField.CreateTextBox(
            this._theme,
            "15",
            this._vm.ScrapePostsPerGroup.ToString(CultureInfo.InvariantCulture));
        postsPerGroupBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(postsPerGroupBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapePostsPerGroup = Math.Clamp(val, 5, 100);
            }
        };

        StackPanel postsPerGroupField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigPostsPerGroupLabel"],
            postsPerGroupBox,
            out TextBlock postsPerGroupLbl);

        TextBox maxGroupsCycleBox = FormField.CreateTextBox(
            this._theme,
            "5",
            this._vm.ScrapeMaxGroupsPerCycle.ToString(CultureInfo.InvariantCulture));
        maxGroupsCycleBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxGroupsCycleBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapeMaxGroupsPerCycle = Math.Clamp(val, 1, 50);
            }
        };

        StackPanel maxGroupsCycleField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigMaxGroupsPerCycleLabel"],
            maxGroupsCycleBox,
            out TextBlock maxGroupsCycleLbl);

        TextBox intervalMinBox = FormField.CreateTextBox(
            this._theme,
            "10",
            this._vm.AutoScrapeIntervalMinutes.ToString(CultureInfo.InvariantCulture));
        intervalMinBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(intervalMinBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.AutoScrapeIntervalMinutes = Math.Clamp(val, 1, 120);
            }
        };

        StackPanel intervalMinField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigIntervalMinutesLabel"],
            intervalMinBox,
            out TextBlock intervalMinLbl);

        Grid.SetColumn(postsPerGroupField, 0);
        Grid.SetColumn(maxGroupsCycleField, 1);
        Grid.SetColumn(intervalMinField, 2);
        numbersGrid.Children.Add(postsPerGroupField);
        numbersGrid.Children.Add(maxGroupsCycleField);
        numbersGrid.Children.Add(intervalMinField);
        rightStack.Children.Add(numbersGrid);

        CheckBox scrapeAllGroupsCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAllGroupsToggle"],
            this._vm.ScrapeAllEnabledTargetGroups,
            out TextBlock scrapeAllGroupsLbl);
        scrapeAllGroupsCheck.IsCheckedChanged += (_, _) =>
            this._vm.ScrapeAllEnabledTargetGroups = scrapeAllGroupsCheck.IsChecked ?? false;
        rightStack.Children.Add(scrapeAllGroupsCheck);

        CheckBox autoBgScrapeCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAutoBackgroundToggle"],
            this._vm.AutoBackgroundScrapeEnabled,
            out TextBlock autoBgScrapeLbl);
        autoBgScrapeCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoBackgroundScrapeEnabled = autoBgScrapeCheck.IsChecked ?? false;
        rightStack.Children.Add(autoBgScrapeCheck);

        Grid configActionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };

        Button runScrapeBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnScanPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        Button clearDbBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Leads.BtnClearScrapedDb"],
            this._vm.ClearScrapedPostsDbCommand,
            VectorIcons.Trash);

        Grid.SetColumn(runScrapeBtn, 0);
        Grid.SetColumn(clearDbBtn, 1);
        configActionRow.Children.Add(runScrapeBtn);
        configActionRow.Children.Add(clearDbBtn);
        rightStack.Children.Add(configActionRow);

        Border rightCard = SurfaceCard.Create(this._theme, new ScrollViewer { Content = rightStack }, new Thickness(16));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightCard);

        refreshI18n = () =>
        {
            multiGroupTitleTb.Text = this._i18n["Leads.ConfigMultiGroupTitle"];
            multiGroupDescTb.Text = this._i18n["Leads.ConfigMultiGroupDesc"];
            groupSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Groups.FilterPlaceholder"]);
            groupStatePill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegEnabled"],
                this._i18n["Groups.FilterSegDisabled"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            AppButton.SetText(enableAllBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(disableAllBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            joinedGroupPicker.PlaceholderText = this._i18n["Groups.Header"];
            AppButton.SetText(addJoinedToTargetsBtn, this._i18n["Groups.BtnSaveTarget"]);
            configTitleTb.Text = this._i18n["Leads.ConfigTitle"];
            chronoBadgeTb.Text = this._i18n["Leads.ConfigChronologicalBadge"];
            scrapeKeywordsLbl.Text = this._i18n["Leads.ConfigKeywordsLabel"];
            scrapeKeywordsBox.PlaceholderText = this._i18n["Leads.ConfigKeywordsPlaceholder"];
            scrapeDelayLbl.Text = this._i18n["Leads.ConfigRateLimitLabel"];
            scrapeMinDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            scrapeMaxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            postsPerGroupLbl.Text = this._i18n["Leads.ConfigPostsPerGroupLabel"];
            maxGroupsCycleLbl.Text = this._i18n["Leads.ConfigMaxGroupsPerCycleLabel"];
            intervalMinLbl.Text = this._i18n["Leads.ConfigIntervalMinutesLabel"];
            scrapeAllGroupsLbl.Text = this._i18n["Leads.ConfigAllGroupsToggle"];
            autoBgScrapeLbl.Text = this._i18n["Leads.ConfigAutoBackgroundToggle"];
            AppButton.SetText(runScrapeBtn, this._i18n["Leads.BtnScanPosts"]);
            AppButton.SetText(clearDbBtn, this._i18n["Leads.BtnClearScrapedDb"]);
        };

        return splitGrid;
    }
}
