// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
/// Independent Groups Catalog, Color-Coded Tag Manager &amp; Target Campaign Groups Page (<c>GroupsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Target Campaign Groups Management with Color-Coded Tags, quick search &amp; Enabled/Disabled filter,
/// 2) Group Discovery, Keyword Search on Facebook &amp; Auto-Join with Unjoined/Joined filter.
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
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
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
        int targetStateFilter = 2; // 0 = Enabled, 1 = Disabled, 2 = All
        string? selectedTagFilterId = null;
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
                if (targetStateFilter == 0 && !tg.IsEnabled)
                {
                    continue;
                }

                if (targetStateFilter == 1 && tg.IsEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(selectedTagFilterId) && !tg.HasTag(selectedTagFilterId))
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
                targetSearchBar?.SetFilterActive(idx != 2 || !string.IsNullOrEmpty(selectedTagFilterId));
                RefreshFilteredTargets();
            });

        targetSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"],
            filterFlyoutContent: targetStatePill);

        targetSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredTargets();
        targetSearchBar.SearchButton.Click += (_, _) => RefreshFilteredTargets();
        Grid.SetRow(targetSearchBar, 1);
        targetsPane.Children.Add(targetSearchBar);

        // Row 2: Color-Coded Tag Filter & Tag Creator Strip
        Control tagStripCard = this.BuildColorTagBar(
            getSelectedTagId: () => selectedTagFilterId,
            setSelectedTagId: tagId =>
            {
                selectedTagFilterId = tagId;
                targetSearchBar?.SetFilterActive(targetStateFilter != 2 || !string.IsNullOrEmpty(selectedTagFilterId));
                RefreshFilteredTargets();
            },
            onBatchTagFiltered: async tagId =>
            {
                for (int i = 0; i < filteredTargets.Count; i++)
                {
                    SavedTargetGroup tg = filteredTargets[i];
                    if (!tg.HasTag(tagId))
                    {
                        await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, tagId).ConfigureAwait(true);
                    }
                }

                RefreshFilteredTargets();
            },
            out Action refreshTagBar);
        Grid.SetRow(tagStripCard, 2);
        targetsPane.Children.Add(tagStripCard);

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
                    RefreshFilteredTargets();
                    refreshTagBar();
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"]),
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
            refreshTagBar();
            bool isEmpty = filteredTargets.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            RefreshFilteredTargets();
            refreshTagBar();
        };
        filteredTargets.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredTargets.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
        };

        Grid targetListHost = EmptyStateBanner.CreateHost(targetList, emptyTargetsBanner);
        Grid.SetRow(targetListHost, 3);
        targetsPane.Children.Add(targetListHost);

        Button removeTargetBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Groups.BtnRemoveTarget"],
            this._vm.RemoveSelectedTargetGroupCommand,
            VectorIcons.Trash);
        Grid.SetRow(removeTargetBtn, 4);
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
            UpdateHeaderStats();
            targetListDesc.Text = this._i18n["Groups.Desc"];
            targetSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Groups.FilterPlaceholder"]);
            targetStatePill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegEnabled"],
                this._i18n["Groups.FilterSegDisabled"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            AppButton.SetText(syncAllJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(removeTargetBtn, this._i18n["Groups.BtnRemoveTarget"]);
            refreshTagBar();
        };

        return targetsPane;
    }

    private Control BuildDiscoverTab(out Action refreshI18n)
    {
        Grid catalogPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

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
        Grid.SetColumn(searchFbBtn, 1);
        Grid.SetColumn(syncJoinedBtn, 2);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(searchFbBtn);
        topHeaderRow.Children.Add(syncJoinedBtn);
        Grid.SetRow(topHeaderRow, 0);
        catalogPane.Children.Add(topHeaderRow);

        BulkObservableCollection<GroupSummary> filteredCatalog = new();
        int membershipFilter = 2; // 0 = Unjoined, 1 = Joined, 2 = All

        void RefreshCatalogFilter()
        {
            int unjoinedCount = 0;
            int joinedCount = 0;
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

            titleTb.Text = $"{this._i18n["Groups.Header"]} — {joinedCount} {this._i18n["Groups.FilterSegJoined"]} • {unjoinedCount} {this._i18n["Groups.FilterSegUnjoined"]}";
            filteredCatalog.ReplaceAll(list);
        }

        SearchFilterBar searchFilterBar = null!;

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
                searchFilterBar?.SetFilterActive(idx != 2);
                RefreshCatalogFilter();
            });

        searchFilterBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.SearchKeywordPlaceholder"],
            this._vm.GroupSearchKeyword,
            membershipPill,
            this._vm.SearchGroupsByKeywordCommand);

        searchFilterBar.SearchTextBox.TextChanged += (_, _) =>
        {
            string text = searchFilterBar.SearchTextBox.Text ?? string.Empty;
            this._vm.GroupSearchKeyword = text;
            this._vm.ApplyLocalGroupFilter(text);
        };
        searchFilterBar.SearchTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && this._vm.SearchGroupsByKeywordCommand.CanExecute(null))
            {
                _ = this._vm.SearchGroupsByKeywordCommand.ExecuteAsync(null);
            }
        };

        Grid.SetRow(searchFilterBar, 1);
        catalogPane.Children.Add(searchFilterBar);

        // Row 2: Visible Quick Filter Bar for Unjoined / Joined / All + Batch Tagging
        Grid quickStatusRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        SegmentedFilterPill inlineMembershipPill = new(
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
                searchFilterBar?.SetFilterActive(idx != 2);
                RefreshCatalogFilter();
            });

        TextBlock discoverHintTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Groups.DiscoverSearchHint"],
            11.5,
            FontWeight.Normal,
            isSecondary: true);
        discoverHintTb.VerticalAlignment = VerticalAlignment.Center;
        discoverHintTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetColumn(inlineMembershipPill, 0);
        Grid.SetColumn(discoverHintTb, 1);
        quickStatusRow.Children.Add(inlineMembershipPill);
        quickStatusRow.Children.Add(discoverHintTb);
        Grid.SetRow(quickStatusRow, 2);
        catalogPane.Children.Add(quickStatusRow);

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
                    RefreshCatalogFilter();
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"]),
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
        Grid.SetRow(groupsListHost, 3);
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
        Grid.SetRow(actionRow, 4);
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
            RefreshCatalogFilter();
            descTb.Text = this._i18n["Groups.Desc"];
            discoverHintTb.Text = this._i18n["Groups.DiscoverSearchHint"];
            emptyGroupsTb.Text = this._i18n["Empty.Groups"];
            searchFilterBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Groups.SearchKeywordPlaceholder"]);
            membershipPill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegUnjoined"],
                this._i18n["Groups.FilterSegJoined"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            inlineMembershipPill.UpdateLabels(
            [
                this._i18n["Groups.FilterSegUnjoined"],
                this._i18n["Groups.FilterSegJoined"],
                this._i18n["Groups.FilterSegAll"]
            ]);
            AppButton.SetText(searchFbBtn, this._i18n["Groups.BtnSearchOnFacebook"]);
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            AppButton.SetText(saveTargetBtn, this._i18n["Groups.BtnSaveTarget"]);
            AppButton.SetText(joinBtn, this._i18n["Groups.BtnJoinSelected"]);
            AppButton.SetText(autoJoinAllBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
        };

        return catalogPane;
    }

    private Control BuildColorTagBar(
        Func<string?> getSelectedTagId,
        Action<string?> setSelectedTagId,
        Action<string> onBatchTagFiltered,
        out Action refreshTagBar)
    {
        Grid barGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        WrapPanel chipsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Flyout for creating a new color tag or batch-tagging filtered groups
        StackPanel creatorFlyoutStack = new()
        {
            Spacing = 10,
            Width = 310
        };

        TextBlock creatorTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Groups.TagCreatorTitle"],
            13,
            FontWeight.Bold);

        TextBox newTagNameBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Groups.TagNamePlaceholder"],
            this._vm.NewGroupTagName);
        newTagNameBox.TextChanged += (_, _) =>
            this._vm.NewGroupTagName = newTagNameBox.Text ?? string.Empty;

        WrapPanel swatchesWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };

        Action rebuildSwatches = null!;
        rebuildSwatches = () =>
        {
            swatchesWrap.Children.Clear();
            for (int i = 0; i < GroupRowCard.PresetTagColors.Length; i++)
            {
                string hex = GroupRowCard.PresetTagColors[i];
                bool isChosen = string.Equals(this._vm.NewGroupTagColorHex, hex, StringComparison.OrdinalIgnoreCase);
                Color c = Color.Parse(hex);

                Border swatch = new()
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(999),
                    Background = new SolidColorBrush(c),
                    BorderBrush = isChosen ? Brushes.White : Brushes.Transparent,
                    BorderThickness = new Thickness(isChosen ? 2.5 : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = isChosen
                        ? new TextBlock
                        {
                            Text = "✓",
                            FontSize = 12,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                        : null
                };
                swatch.PointerPressed += (_, _) =>
                {
                    this._vm.NewGroupTagColorHex = hex;
                    rebuildSwatches();
                };
                swatchesWrap.Children.Add(swatch);
            }
        };
        rebuildSwatches();

        Button createTagBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnCreateTag"],
            this._vm.CreateGroupTagCommand,
            VectorIcons.Plus);
        createTagBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        TextBlock batchAssignLabelTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Groups.BatchAssignFilteredLabel"],
            11.5,
            FontWeight.SemiBold,
            isSecondary: true);

        WrapPanel batchAssignWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6
        };

        creatorFlyoutStack.Children.Add(creatorTitleTb);
        creatorFlyoutStack.Children.Add(newTagNameBox);
        creatorFlyoutStack.Children.Add(swatchesWrap);
        creatorFlyoutStack.Children.Add(createTagBtn);
        creatorFlyoutStack.Children.Add(batchAssignLabelTb);
        creatorFlyoutStack.Children.Add(batchAssignWrap);

        Flyout manageTagsFlyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = SurfaceCard.Create(this._theme, creatorFlyoutStack, new Thickness(14))
        };

        Button manageTagsBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnManageTags"],
            null,
            VectorIcons.Bookmark);
        manageTagsBtn.Flyout = manageTagsFlyout;

        void RebuildTagChips()
        {
            chipsWrap.Children.Clear();
            batchAssignWrap.Children.Clear();

            string? currentFilterId = getSelectedTagId();
            GroupTag allTag = new("ALL", this._i18n["Groups.TagFilterAll"], "#64748B");
            chipsWrap.Children.Add(GroupRowCard.CreateTagPill(
                allTag,
                suffixText: $"{this._vm.TargetGroups.Count}",
                isActive: string.IsNullOrEmpty(currentFilterId),
                onClick: () =>
                {
                    setSelectedTagId(null);
                    RebuildTagChips();
                }));

            for (int i = 0; i < this._vm.GroupTags.Count; i++)
            {
                GroupTag tag = this._vm.GroupTags[i];
                int countWithTag = 0;
                for (int j = 0; j < this._vm.TargetGroups.Count; j++)
                {
                    if (this._vm.TargetGroups[j].HasTag(tag.Id))
                    {
                        countWithTag++;
                    }
                }

                bool isFilterActive = string.Equals(currentFilterId, tag.Id, StringComparison.OrdinalIgnoreCase);
                Border filterChip = GroupRowCard.CreateTagPill(
                    tag,
                    suffixText: $"{countWithTag}",
                    isActive: isFilterActive || string.IsNullOrEmpty(currentFilterId),
                    onClick: () =>
                    {
                        string? nextId = string.Equals(getSelectedTagId(), tag.Id, StringComparison.OrdinalIgnoreCase)
                            ? null
                            : tag.Id;
                        setSelectedTagId(nextId);
                        RebuildTagChips();
                    },
                    onDelete: () => _ = this._vm.DeleteGroupTagAsync(tag));
                chipsWrap.Children.Add(filterChip);

                Border batchChip = GroupRowCard.CreateTagPill(
                    tag,
                    suffixText: "+",
                    isActive: true,
                    onClick: () =>
                    {
                        manageTagsFlyout.Hide();
                        onBatchTagFiltered(tag.Id);
                    });
                batchAssignWrap.Children.Add(batchChip);
            }
        }

        RebuildTagChips();

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.NewGroupTagName), StringComparison.Ordinal) &&
                !string.Equals(newTagNameBox.Text, this._vm.NewGroupTagName, StringComparison.Ordinal))
            {
                newTagNameBox.Text = this._vm.NewGroupTagName;
            }
        };

        Grid.SetColumn(chipsWrap, 0);
        Grid.SetColumn(manageTagsBtn, 1);
        barGrid.Children.Add(chipsWrap);
        barGrid.Children.Add(manageTagsBtn);

        refreshTagBar = () =>
        {
            creatorTitleTb.Text = this._i18n["Groups.TagCreatorTitle"];
            newTagNameBox.PlaceholderText = this._i18n["Groups.TagNamePlaceholder"];
            AppButton.SetText(createTagBtn, this._i18n["Groups.BtnCreateTag"]);
            batchAssignLabelTb.Text = this._i18n["Groups.BatchAssignFilteredLabel"];
            AppButton.SetText(manageTagsBtn, this._i18n["Groups.BtnManageTags"]);
            RebuildTagChips();
        };

        return SurfaceCard.CreateInset(this._theme, barGrid, new Thickness(12, 8));
    }
}
