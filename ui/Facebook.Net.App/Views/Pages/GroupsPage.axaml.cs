// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;
using Avalonia.Markup.Xaml;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Groups Catalog, Color-Coded Tag Manager &amp; Target Campaign Groups Page (<c>GroupsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Target Campaign Groups Management with 2-level <see cref="SearchFilterBar"/> and contextual row actions,
/// 2) Group Discovery, Keyword Search on Facebook &amp; Auto-Join with 2-level <see cref="SearchFilterBar"/>,
/// 3) Dedicated Color-Coded Tag Manager &amp; Batch Tag Assignment tab.
/// </summary>
public sealed partial class GroupsPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public GroupsPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public GroupsPage(ShellViewModel viewModel)
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
        Control tab1View = this.BuildTargetsTab(out Action refreshTab1);
        Control tab2View = this.BuildDiscoverTab(out Action refreshTab2);
        Control tab3View = this.BuildTagManagerTab(out Action refreshTab3);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Groups.TabTargets"], VectorIcons.CheckCircle, tab1View),
                new SubTabItem(this._i18n["Groups.TabDiscover"], VectorIcons.Groups, tab2View),
                new SubTabItem(this._i18n["Groups.TabTagManager"], VectorIcons.Bookmark, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Groups.TabTargets"],
                this._i18n["Groups.TabDiscover"],
                this._i18n["Groups.TabTagManager"]
            ]);
            refreshTab1();
            refreshTab2();
            refreshTab3();
        };

        return subTabBar;
    }

    private Control BuildTargetsTab(out Action refreshI18n)
    {
        Grid targetsPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.TargetListTitle"],
            this._i18n["Groups.Desc"],
            out TextBlock targetListTitle,
            out TextBlock targetListDesc);

        Button syncAllJoinedBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Button selectAllTargetsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button deselectAllTargetsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand,
            VectorIcons.StopCircle);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncAllJoinedBtn, 1);
        Grid.SetColumn(selectAllTargetsBtn, 2);
        Grid.SetColumn(deselectAllTargetsBtn, 3);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncAllJoinedBtn);
        topHeaderRow.Children.Add(selectAllTargetsBtn);
        topHeaderRow.Children.Add(deselectAllTargetsBtn);
        Grid.SetRow(topHeaderRow, 0);
        targetsPane.Children.Add(topHeaderRow);

        BulkObservableCollection<SavedTargetGroup> filteredTargets = new();
        string? targetStateFilter = null; // "enabled", "disabled", or null
        string? selectedTagFilterId = null; // tag.Id, "__untagged__", or null
        SearchFilterBar targetSearchBar = null!;

        void UpdateHeaderStats()
        {
            int enabledCount = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledCount++;
                }
            }

            targetListTitle.Text = $"{this._i18n["Groups.TargetListTitle"]} — {enabledCount} / {this._vm.TargetGroups.Count}";
        }

        void RefreshFilteredTargets()
        {
            UpdateHeaderStats();
            string q = (targetSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<SavedTargetGroup> matches = new(this._vm.TargetGroups.Count);
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup tg = this._vm.TargetGroups[i];
                if (string.Equals(targetStateFilter, "enabled", StringComparison.OrdinalIgnoreCase) && !tg.IsEnabled)
                {
                    continue;
                }

                if (string.Equals(targetStateFilter, "disabled", StringComparison.OrdinalIgnoreCase) && tg.IsEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(selectedTagFilterId))
                {
                    if (string.Equals(selectedTagFilterId, "__untagged__", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tg.GetTagIds().Count > 0)
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

            filteredTargets.ReplaceAll(matches);
        }

        targetSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"]);

        FilterCategoryDefinition stateCategory = new(
            key: "state",
            title: this._i18n["Groups.FilterCatTargetState"],
            iconSvg: VectorIcons.CheckCircle,
            optionsProvider: () =>
            [
                new FilterOptionItem("enabled", this._i18n["Groups.FilterSegEnabled"]),
                new FilterOptionItem("disabled", this._i18n["Groups.FilterSegDisabled"])
            ],
            onApplied: selectedIds =>
            {
                targetStateFilter = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshFilteredTargets();
            },
            allowMultiple: false);

        FilterCategoryDefinition tagCategory = new(
            key: "tag",
            title: this._i18n["Groups.FilterCatColorTag"],
            iconSvg: VectorIcons.Bookmark,
            optionsProvider: () =>
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
            onApplied: selectedIds =>
            {
                selectedTagFilterId = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshFilteredTargets();
            },
            allowMultiple: false);

        targetSearchBar.SetFilterCategories(
            [stateCategory, tagCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        targetSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredTargets();
        targetSearchBar.SearchButton.Click += (_, _) => RefreshFilteredTargets();
        Grid.SetRow(targetSearchBar, 1);
        targetsPane.Children.Add(targetSearchBar);

        RefreshFilteredTargets();

        ListBox targetList = FormField.CreateListBox<SavedTargetGroup>(
            this._theme,
            filteredTargets,
            tg => GroupRowCard.CreateTargetRow(
                this._theme,
                tg,
                nextState =>
                {
                    _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState);
                    UpdateHeaderStats();
                },
                assignedTags: this._vm.GetGroupTagsForGroup(tg.GroupId),
                allTags: this._vm.GroupTags,
                onToggleTag: async tagId =>
                {
                    await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, tagId).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(selectedTagFilterId))
                    {
                        RefreshFilteredTargets();
                    }
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"],
                onRemove: () =>
                {
                    ConfirmDialogModal.ShowAsync(
                        this,
                        this._theme,
                        this._i18n["Confirm.RemoveTargetGroupTitle"],
                        this._i18n.Format("Confirm.RemoveTargetGroupMessage", tg.GroupName),
                        this._i18n["Groups.BtnRemoveTargetShort"],
                        this._i18n["Groups.BtnCancelEditTag"],
                        () => this._vm.RemoveTargetGroupAsync(tg));
                },
                removeLabel: this._i18n["Groups.BtnRemoveTargetShort"]),
            selectedItem: this._vm.SelectedTargetGroup,
            isVisible: filteredTargets.Count > 0);

        targetList.SelectionChanged += (_, _) =>
        {
            if (targetList.SelectedItem is SavedTargetGroup stg)
            {
                this._vm.SelectedTargetGroup = stg;
            }
        };

        Border emptyTargetsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.CheckCircle,
            out TextBlock emptyTargetsTb);
        emptyTargetsBanner.IsVisible = filteredTargets.Count == 0;

        this._vm.TargetGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
                string.IsNullOrEmpty(targetStateFilter) &&
                string.IsNullOrEmpty(selectedTagFilterId) &&
                e.NewItems is { Count: > 0 } &&
                e.NewItems[0] is SavedTargetGroup updatedTg)
            {
                for (int i = 0; i < filteredTargets.Count; i++)
                {
                    if (string.Equals(filteredTargets[i].GroupId, updatedTg.GroupId, StringComparison.Ordinal))
                    {
                        filteredTargets[i] = updatedTg;
                        UpdateHeaderStats();
                        return;
                    }
                }
            }

            RefreshFilteredTargets();
            bool isEmpty = filteredTargets.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            RefreshFilteredTargets();
            targetSearchBar.RefreshFilterChips();
        };
        filteredTargets.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredTargets.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
        };

        Grid targetListHost = EmptyStateBanner.CreateHost(targetList, emptyTargetsBanner);
        Grid.SetRow(targetListHost, 2);
        targetsPane.Children.Add(targetListHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedTargetGroup), StringComparison.Ordinal) &&
                !ReferenceEquals(targetList.SelectedItem, this._vm.SelectedTargetGroup))
            {
                targetList.SelectedItem = this._vm.SelectedTargetGroup;
            }
        };

        refreshI18n = () =>
        {
            stateCategory.Title = this._i18n["Groups.FilterCatTargetState"];
            tagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            targetSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Groups.FilterPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            RefreshFilteredTargets();
            targetListDesc.Text = this._i18n["Groups.Desc"];
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            AppButton.SetText(syncAllJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
        };

        return targetsPane;
    }

    private Control BuildDiscoverTab(out Action refreshI18n)
    {
        Grid catalogPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button autoJoinHeaderBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnAutoJoinDiscovered"],
            this._vm.AutoJoinDiscoveredGroupsCommand,
            VectorIcons.Groups);
        autoJoinHeaderBtn.IsVisible = false;

        Button searchFbBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSearchOnFacebook"],
            this._vm.SearchGroupsByKeywordCommand,
            VectorIcons.Search);

        Button syncJoinedBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(autoJoinHeaderBtn, 1);
        Grid.SetColumn(searchFbBtn, 2);
        Grid.SetColumn(syncJoinedBtn, 3);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(autoJoinHeaderBtn);
        topHeaderRow.Children.Add(searchFbBtn);
        topHeaderRow.Children.Add(syncJoinedBtn);
        Grid.SetRow(topHeaderRow, 0);
        catalogPane.Children.Add(topHeaderRow);

        BulkObservableCollection<GroupSummary> filteredCatalog = new();
        string? membershipFilter = null; // "unjoined", "joined", or null
        string? selectedTagFilterId = null; // tag.Id, "__untagged__", or null
        SearchFilterBar searchFilterBar = null!;

        void RefreshCatalogFilter()
        {
            int unjoinedCount = 0;
            int joinedCount = 0;
            string kw = (searchFilterBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<GroupSummary> list = new(this._vm.JoinedGroups.Count);
            for (int i = 0; i < this._vm.JoinedGroups.Count; i++)
            {
                GroupSummary g = this._vm.JoinedGroups[i];
                bool isJoined = g.MembershipState == GroupMembershipState.Member;
                if (isJoined)
                {
                    joinedCount++;
                }
                else
                {
                    unjoinedCount++;
                }

                if (string.Equals(membershipFilter, "unjoined", StringComparison.OrdinalIgnoreCase) && isJoined)
                {
                    continue;
                }

                if (string.Equals(membershipFilter, "joined", StringComparison.OrdinalIgnoreCase) && !isJoined)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(selectedTagFilterId))
                {
                    SavedTargetGroup? tg = this._vm.FindTargetGroupById(g.GroupId);
                    if (string.Equals(selectedTagFilterId, "__untagged__", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tg is not null && tg.GetTagIds().Count > 0)
                        {
                            continue;
                        }
                    }
                    else if (tg is null || !tg.HasTag(selectedTagFilterId))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(kw) && !g.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(g);
            }

            autoJoinHeaderBtn.IsVisible = unjoinedCount > 0;
            titleTb.Text = $"{this._i18n["Groups.Header"]} — {joinedCount} {this._i18n["Groups.FilterSegJoined"]} • {unjoinedCount} {this._i18n["Groups.FilterSegUnjoined"]}";
            filteredCatalog.ReplaceAll(list);
        }

        searchFilterBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.SearchKeywordPlaceholder"],
            this._vm.GroupSearchKeyword,
            searchCommand: this._vm.SearchGroupsByKeywordCommand);

        FilterCategoryDefinition membershipCategory = new(
            key: "membership",
            title: this._i18n["Groups.FilterCatMembership"],
            iconSvg: VectorIcons.Groups,
            optionsProvider: () =>
            [
                new FilterOptionItem("unjoined", this._i18n["Groups.FilterSegUnjoined"]),
                new FilterOptionItem("joined", this._i18n["Groups.FilterSegJoined"])
            ],
            onApplied: selectedIds =>
            {
                membershipFilter = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshCatalogFilter();
            },
            allowMultiple: false);

        FilterCategoryDefinition discoverTagCategory = new(
            key: "tag",
            title: this._i18n["Groups.FilterCatColorTag"],
            iconSvg: VectorIcons.Bookmark,
            optionsProvider: () =>
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
            onApplied: selectedIds =>
            {
                selectedTagFilterId = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshCatalogFilter();
            },
            allowMultiple: false);

        searchFilterBar.SetFilterCategories(
            [membershipCategory, discoverTagCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        searchFilterBar.SearchTextBox.TextChanged += (_, _) =>
        {
            this._vm.GroupSearchKeyword = searchFilterBar.SearchTextBox.Text ?? string.Empty;
            RefreshCatalogFilter();
        };

        Grid.SetRow(searchFilterBar, 1);
        catalogPane.Children.Add(searchFilterBar);

        RefreshCatalogFilter();

        ListBox groupsList = FormField.CreateListBox<GroupSummary>(
            this._theme,
            filteredCatalog,
            grp => GroupRowCard.CreateCatalogRow(
                this._theme,
                this._i18n,
                grp,
                assignedTags: this._vm.GetGroupTagsForGroup(grp.GroupId),
                allTags: this._vm.GroupTags,
                onToggleTag: async tagId =>
                {
                    await this._vm.ToggleGroupTagAsync(grp.GroupId, grp.Name, grp.AvatarUrl, tagId).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(selectedTagFilterId))
                    {
                        RefreshCatalogFilter();
                    }
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"],
                isAlreadyInTargets: this._vm.FindTargetGroupById(grp.GroupId) is not null,
                onAddToTarget: () => _ = this._vm.AddGroupToTargetsAsync(grp),
                onJoinGroup: () => _ = this._vm.JoinGroupItemAsync(grp),
                addToTargetLabel: this._i18n["Groups.BtnAddTargetShort"],
                joinLabel: this._i18n["Groups.BtnJoinSelected"]),
            selectedItem: this._vm.SelectedGroup,
            isVisible: filteredCatalog.Count > 0);

        groupsList.SelectionChanged += (_, _) =>
        {
            if (groupsList.SelectedItem is GroupSummary g)
            {
                this._vm.SelectedGroup = g;
            }
        };

        Border emptyGroupsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.Groups"],
            VectorIcons.Groups,
            out TextBlock emptyGroupsTb);
        emptyGroupsBanner.IsVisible = filteredCatalog.Count == 0;

        this._vm.JoinedGroups.CollectionChanged += (_, _) =>
        {
            RefreshCatalogFilter();
            bool isEmpty = filteredCatalog.Count == 0;
            emptyGroupsBanner.IsVisible = isEmpty;
            groupsList.IsVisible = !isEmpty;
        };
        this._vm.TargetGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
                string.IsNullOrEmpty(selectedTagFilterId) &&
                e.NewItems is { Count: > 0 } &&
                e.NewItems[0] is SavedTargetGroup updatedTg)
            {
                for (int i = 0; i < filteredCatalog.Count; i++)
                {
                    if (string.Equals(filteredCatalog[i].GroupId, updatedTg.GroupId, StringComparison.Ordinal))
                    {
                        filteredCatalog[i] = filteredCatalog[i] with { };
                        return;
                    }
                }
            }

            RefreshCatalogFilter();
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            RefreshCatalogFilter();
            searchFilterBar.RefreshFilterChips();
        };
        filteredCatalog.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredCatalog.Count == 0;
            emptyGroupsBanner.IsVisible = isEmpty;
            groupsList.IsVisible = !isEmpty;
        };

        Grid groupsListHost = EmptyStateBanner.CreateHost(groupsList, emptyGroupsBanner);
        Grid.SetRow(groupsListHost, 2);
        catalogPane.Children.Add(groupsListHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedGroup), StringComparison.Ordinal) &&
                !ReferenceEquals(groupsList.SelectedItem, this._vm.SelectedGroup))
            {
                groupsList.SelectedItem = this._vm.SelectedGroup;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.GroupSearchKeyword), StringComparison.Ordinal) &&
                     !string.Equals(searchFilterBar.SearchTextBox.Text, this._vm.GroupSearchKeyword, StringComparison.Ordinal))
            {
                searchFilterBar.SearchTextBox.Text = this._vm.GroupSearchKeyword;
            }
        };

        refreshI18n = () =>
        {
            membershipCategory.Title = this._i18n["Groups.FilterCatMembership"];
            discoverTagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            searchFilterBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Groups.SearchKeywordPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            RefreshCatalogFilter();
            descTb.Text = this._i18n["Groups.Desc"];
            emptyGroupsTb.Text = this._i18n["Empty.Groups"];
            AppButton.SetText(autoJoinHeaderBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
            AppButton.SetText(searchFbBtn, this._i18n["Groups.BtnSearchOnFacebook"]);
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
        };

        return catalogPane;
    }
}
