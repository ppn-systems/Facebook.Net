// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Groups Catalog &amp; Target Campaign Groups Page (<c>GroupsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Target Campaign Groups Management with quick search &amp; Enabled/Disabled filter,
/// 2) Group Discovery, Keyword Search &amp; Auto-Join with Unjoined/Joined filter.
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

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Groups.TabTargets"], VectorIcons.CheckCircle, tab1View),
                new SubTabItem(this._i18n["Groups.TabDiscover"], VectorIcons.Groups, tab2View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Groups.TabTargets"],
                this._i18n["Groups.TabDiscover"]
            ]);
            refreshTab1();
            refreshTab2();
        };

        return subTabBar;
    }

    private Control BuildTargetsTab(out Action refreshI18n)
    {
        Grid targetsPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.TargetListTitle"],
            this._i18n["Groups.Desc"],
            out TextBlock targetListTitle,
            out TextBlock targetListDesc);

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
        Grid.SetColumn(selectAllTargetsBtn, 1);
        Grid.SetColumn(deselectAllTargetsBtn, 2);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(selectAllTargetsBtn);
        topHeaderRow.Children.Add(deselectAllTargetsBtn);
        Grid.SetRow(topHeaderRow, 0);
        targetsPane.Children.Add(topHeaderRow);

        // Row 1: Unified Search & Filter Bar ([ ▽ Bộ lọc | Lọc nhanh tên nhóm... | 🔍 ]) with Flyout Popover
        BulkObservableCollection<SavedTargetGroup> filteredTargets = new();
        int targetStateFilter = 2; // 0 = Enabled, 1 = Disabled, 2 = All
        SearchFilterBar targetSearchBar = null!;

        void RefreshFilteredTargets()
        {
            string q = (targetSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
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

            filteredTargets.ReplaceAll(matches);
        }

        SegmentedFilterPill targetStatePill = new(
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
                targetSearchBar?.SetFilterActive(idx != 2);
                RefreshFilteredTargets();
            });

        Border targetFilterFlyoutCard = SurfaceCard.Create(this._theme, targetStatePill, new Thickness(12));
        targetSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"],
            filterFlyoutContent: targetFilterFlyoutCard);

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
                nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState)),
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

        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            RefreshFilteredTargets();
            bool isEmpty = filteredTargets.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
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

        Button removeTargetBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Groups.BtnRemoveTarget"],
            this._vm.RemoveSelectedTargetGroupCommand,
            VectorIcons.Trash);
        Grid.SetRow(removeTargetBtn, 3);
        targetsPane.Children.Add(removeTargetBtn);

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
            targetListTitle.Text = this._i18n["Groups.TargetListTitle"];
            targetListDesc.Text = this._i18n["Groups.Desc"];
            targetSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Groups.FilterPlaceholder"]);
            targetStatePill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegEnabled"],
                this._i18n["Groups.FilterSegDisabled"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            AppButton.SetText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(removeTargetBtn, this._i18n["Groups.BtnRemoveTarget"]);
        };

        return SurfaceCard.Create(this._theme, targetsPane, new Thickness(16));
    }

    private Control BuildDiscoverTab(out Action refreshI18n)
    {
        Grid catalogPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button syncJoinedBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncJoinedBtn, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncJoinedBtn);
        Grid.SetRow(topHeaderRow, 0);
        catalogPane.Children.Add(topHeaderRow);

        BulkObservableCollection<GroupSummary> filteredCatalog = new();
        int membershipFilter = 2; // 0 = Unjoined, 1 = Joined, 2 = All

        void RefreshCatalogFilter()
        {
            List<GroupSummary> list = new(this._vm.JoinedGroups.Count);
            for (int i = 0; i < this._vm.JoinedGroups.Count; i++)
            {
                GroupSummary g = this._vm.JoinedGroups[i];
                bool isJoined = g.MembershipState == GroupMembershipState.Member;
                if (membershipFilter == 0 && isJoined)
                {
                    continue;
                }

                if (membershipFilter == 1 && !isJoined)
                {
                    continue;
                }

                list.Add(g);
            }

            filteredCatalog.ReplaceAll(list);
        }

        SearchFilterBar searchFilterBar = null!;
        TextBox localFilterBox = null!;

        void UpdateDiscoverFilterActive()
        {
            searchFilterBar?.SetFilterActive(
                membershipFilter != 2 ||
                !string.IsNullOrWhiteSpace(localFilterBox?.Text));
        }

        SegmentedFilterPill membershipPill = new(
            this._theme,
            [
                this._i18n["Groups.FilterSegUnjoined"],
                this._i18n["Groups.FilterSegJoined"],
                this._i18n["Groups.FilterSegAll"]
            ],
            initialIndex: 2,
            onSelected: idx =>
            {
                membershipFilter = idx;
                UpdateDiscoverFilterActive();
                RefreshCatalogFilter();
            });

        localFilterBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Groups.FilterPlaceholder"]);
        localFilterBox.TextChanged += (_, _) =>
        {
            UpdateDiscoverFilterActive();
            this._vm.ApplyLocalGroupFilter(localFilterBox.Text);
        };

        StackPanel filterPopoverStack = new() { Spacing = 10, Width = 310 };
        filterPopoverStack.Children.Add(membershipPill);
        filterPopoverStack.Children.Add(localFilterBox);
        Border filterPopoverCard = SurfaceCard.Create(this._theme, filterPopoverStack, new Thickness(12));

        searchFilterBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.SearchKeywordPlaceholder"],
            this._vm.GroupSearchKeyword,
            filterPopoverCard,
            this._vm.SearchGroupsByKeywordCommand);

        searchFilterBar.SearchTextBox.TextChanged += (_, _) =>
        {
            string text = searchFilterBar.SearchTextBox.Text ?? string.Empty;
            this._vm.GroupSearchKeyword = text;
            this._vm.ApplyLocalGroupFilter(text);
        };

        Grid.SetRow(searchFilterBar, 1);
        catalogPane.Children.Add(searchFilterBar);

        RefreshCatalogFilter();

        ListBox groupsList = FormField.CreateListBox<GroupSummary>(
            this._theme,
            filteredCatalog,
            grp => GroupRowCard.CreateCatalogRow(this._theme, this._i18n, grp),
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
        filteredCatalog.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredCatalog.Count == 0;
            emptyGroupsBanner.IsVisible = isEmpty;
            groupsList.IsVisible = !isEmpty;
        };

        Grid groupsListHost = EmptyStateBanner.CreateHost(groupsList, emptyGroupsBanner);
        Grid.SetRow(groupsListHost, 2);
        catalogPane.Children.Add(groupsListHost);

        Grid actionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
        };

        Button saveTargetBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand,
            VectorIcons.Plus);

        Button joinBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnJoinSelected"],
            this._vm.JoinSelectedGroupCommand,
            VectorIcons.CheckCircle);

        Button autoJoinAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnAutoJoinDiscovered"],
            this._vm.AutoJoinDiscoveredGroupsCommand,
            VectorIcons.Groups);

        Grid.SetColumn(saveTargetBtn, 0);
        Grid.SetColumn(joinBtn, 1);
        Grid.SetColumn(autoJoinAllBtn, 2);
        actionRow.Children.Add(saveTargetBtn);
        actionRow.Children.Add(joinBtn);
        actionRow.Children.Add(autoJoinAllBtn);
        Grid.SetRow(actionRow, 3);
        catalogPane.Children.Add(actionRow);

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
            titleTb.Text = this._i18n["Groups.Header"];
            descTb.Text = this._i18n["Groups.Desc"];
            emptyGroupsTb.Text = this._i18n["Empty.Groups"];
            searchFilterBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Groups.SearchKeywordPlaceholder"]);
            membershipPill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegUnjoined"],
                this._i18n["Groups.FilterSegJoined"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            localFilterBox.PlaceholderText = this._i18n["Groups.FilterPlaceholder"];
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(saveTargetBtn, this._i18n["Groups.BtnSaveTarget"]);
            AppButton.SetText(joinBtn, this._i18n["Groups.BtnJoinSelected"]);
            AppButton.SetText(autoJoinAllBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
        };

        return SurfaceCard.Create(this._theme, catalogPane, new Thickness(16));
    }
}
