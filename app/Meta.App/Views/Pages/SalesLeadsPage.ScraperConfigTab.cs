// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Collections;

namespace Meta.App.Views.Pages;

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
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
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

        // Quick Search & 2-Level Filter Bar for Target Groups
        BulkObservableCollection<SavedTargetGroup> filteredTargetGroups = new();
        int targetStateFilter = 2; // 0 = Enabled, 1 = Disabled, 2 = All
        string? selectedTagFilterId = null;
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

                if (!string.IsNullOrEmpty(selectedTagFilterId))
                {
                    if (string.Equals(selectedTagFilterId, "__untagged__", StringComparison.Ordinal))
                    {
                        if (this._vm.GetGroupTagsForGroup(tg.GroupId).Count > 0)
                        {
                            continue;
                        }
                    }
                    else if (!tg.HasTag(selectedTagFilterId))
                    {
                        continue;
                    }
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

        groupSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"]);

        FilterCategoryDefinition stateCategory = new(
            "state",
            this._i18n["Groups.FilterCatTargetState"],
            VectorIcons.CheckCircle,
            () =>
            [
                new FilterOptionItem("enabled", this._i18n["Groups.FilterSegEnabled"]),
                new FilterOptionItem("disabled", this._i18n["Groups.FilterSegDisabled"])
            ],
            selectedIds =>
            {
                string? sel = selectedIds.Count > 0 ? selectedIds[0] : null;
                targetStateFilter = sel switch
                {
                    "enabled" => 0,
                    "disabled" => 1,
                    _ => 2
                };
                RefreshFilteredGroups();
            },
            allowMultiple: false);

        FilterCategoryDefinition tagCategory = new(
            "tag",
            this._i18n["Groups.FilterCatColorTag"],
            VectorIcons.Bookmark,
            () =>
            {
                List<FilterOptionItem> options = new(this._vm.GroupTags.Count + 1);
                for (int i = 0; i < this._vm.GroupTags.Count; i++)
                {
                    GroupTag tag = this._vm.GroupTags[i];
                    options.Add(new FilterOptionItem(tag.Id, tag.Name, ColorHex: tag.ColorHex));
                }

                options.Add(new FilterOptionItem("__untagged__", this._i18n["Groups.FilterUntagged"]));
                return options;
            },
            selectedIds =>
            {
                selectedTagFilterId = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshFilteredGroups();
            },
            allowMultiple: false);

        groupSearchBar.SetFilterCategories(
            [stateCategory, tagCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

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
                nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState),
                assignedTags: this._vm.GetGroupTagsForGroup(tg.GroupId),
                allTags: this._vm.GroupTags,
                onToggleTag: async tagId =>
                {
                    await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, tagId).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(selectedTagFilterId))
                    {
                        RefreshFilteredGroups();
                    }
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"]),
            selectedItem: this._vm.SelectedTargetGroup,
            isVisible: filteredTargetGroups.Count > 0);

        Border emptyTargetsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.Groups,
            out TextBlock emptyTargetsTb);
        emptyTargetsBanner.IsVisible = filteredTargetGroups.Count == 0;

        this._vm.TargetGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
                targetStateFilter == 2 &&
                string.IsNullOrEmpty(selectedTagFilterId) &&
                e.NewItems is { Count: > 0 } &&
                e.NewItems[0] is SavedTargetGroup updatedTg)
            {
                for (int i = 0; i < filteredTargetGroups.Count; i++)
                {
                    if (string.Equals(filteredTargetGroups[i].GroupId, updatedTg.GroupId, StringComparison.Ordinal))
                    {
                        filteredTargetGroups[i] = updatedTg;
                        return;
                    }
                }
            }

            RefreshFilteredGroups();
            bool isEmpty = filteredTargetGroups.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetGroupsList.IsVisible = !isEmpty;
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            RefreshFilteredGroups();
            groupSearchBar.RefreshFilterChips();
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
            out NumericUpDown scrapeMaxDelayBox,
            this._i18n);

        scrapeMinDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(scrapeMinDelayBox.Value ?? 1m), 0.0, 600.0), 2);
            if (Math.Abs(this._vm.ScrapeDelayMinSeconds - next) > 0.001)
            {
                this._vm.ScrapeDelayMinSeconds = next;
            }
        };
        scrapeMaxDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(scrapeMaxDelayBox.Value ?? 2m), this._vm.ScrapeDelayMinSeconds, 600.0), 2);
            if (Math.Abs(this._vm.ScrapeDelayMaxSeconds - next) > 0.001)
            {
                this._vm.ScrapeDelayMaxSeconds = next;
            }
        };

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
            "30",
            this._vm.ScrapeMaxGroupsPerCycle.ToString(CultureInfo.InvariantCulture));
        maxGroupsCycleBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxGroupsCycleBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapeMaxGroupsPerCycle = Math.Clamp(val, 1, 500);
            }
        };

        StackPanel maxGroupsCycleField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigMaxGroupsPerCycleLabel"],
            maxGroupsCycleBox,
            out TextBlock maxGroupsCycleLbl);

        TextBox intervalMinBox = FormField.CreateTextBox(
            this._theme,
            "5",
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
            null,
            VectorIcons.Trash);
        clearDbBtn.Click += (_, _) =>
        {
            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Confirm.ClearScrapedDbTitle"],
                this._i18n["Confirm.ClearScrapedDbMessage"],
                this._i18n["Confirm.BtnConfirmDelete"],
                this._i18n["Groups.BtnCancelEditTag"],
                async () =>
                {
                    if (this._vm.ClearScrapedPostsDbCommand.CanExecute(null))
                    {
                        await this._vm.ClearScrapedPostsDbCommand.ExecuteAsync(null).ConfigureAwait(true);
                    }
                });
        };

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
            stateCategory.Title = this._i18n["Groups.FilterCatTargetState"];
            tagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            groupSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Groups.FilterPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            AppButton.SetText(enableAllBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(disableAllBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
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
            RefreshFilteredGroups();
        };

        return splitGrid;
    }
}
