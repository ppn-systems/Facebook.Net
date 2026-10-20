// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Groups Catalog &amp; Target Campaign Groups Page (<c>GroupsPage.axaml</c>).
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
        Grid pageGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.25*,1*"),
            ColumnSpacing = 12
        };

        // Left Column: Facebook Group Catalog & Discovery
        Grid catalogPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Grid searchAndSyncRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button syncJoinedBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        TextBox searchKeywordBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.SearchKeywordPlaceholder"]);
        searchKeywordBox.TextChanged += (_, _) => this._vm.GroupSearchKeyword = searchKeywordBox.Text ?? string.Empty;

        Button searchBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSearchNew"],
            this._vm.SearchGroupsByKeywordCommand,
            VectorIcons.Search);

        Grid.SetColumn(syncJoinedBtn, 0);
        Grid.SetColumn(searchKeywordBox, 1);
        Grid.SetColumn(searchBtn, 2);
        searchAndSyncRow.Children.Add(syncJoinedBtn);
        searchAndSyncRow.Children.Add(searchKeywordBox);
        searchAndSyncRow.Children.Add(searchBtn);

        StackPanel leftTopHeader = new() { Spacing = 8 };
        leftTopHeader.Children.Add(header);
        leftTopHeader.Children.Add(searchAndSyncRow);
        Grid.SetRow(leftTopHeader, 0);
        catalogPane.Children.Add(leftTopHeader);

        TextBox localFilterBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.FilterPlaceholder"]);
        localFilterBox.TextChanged += (_, _) => this._vm.ApplyLocalGroupFilter(localFilterBox.Text);
        Grid.SetRow(localFilterBox, 1);
        catalogPane.Children.Add(localFilterBox);

        ListBox groupsList = UiControls.CreateListBox<GroupSummary>(
            this._theme,
            this._vm.JoinedGroups,
            grp => UiControls.CreateGroupCatalogItemCard(this._theme, this._i18n, grp),
            selectedItem: this._vm.SelectedGroup,
            isVisible: this._vm.JoinedGroups.Count > 0);

        groupsList.SelectionChanged += (_, _) =>
        {
            if (groupsList.SelectedItem is GroupSummary g)
            {
                this._vm.SelectedGroup = g;
            }
        };

        Border emptyGroupsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.Groups"],
            VectorIcons.Groups,
            out TextBlock emptyGroupsTb);
        emptyGroupsBanner.IsVisible = this._vm.JoinedGroups.Count == 0;
        this._vm.JoinedGroups.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.JoinedGroups.Count == 0;
            emptyGroupsBanner.IsVisible = isEmpty;
            groupsList.IsVisible = !isEmpty;
        };

        Grid groupsListHost = UiControls.CreateListWithEmptyStateHost(groupsList, emptyGroupsBanner);
        Grid.SetRow(groupsListHost, 2);
        catalogPane.Children.Add(groupsListHost);

        Grid actionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 6
        };

        Button saveTargetBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand,
            VectorIcons.Plus);

        Button joinBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnJoinSelected"],
            this._vm.JoinSelectedGroupCommand,
            VectorIcons.CheckCircle);

        Button autoJoinAllBtn = UiControls.CreateSecondaryButton(
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

        Border leftCard = UiControls.CreateCard(this._theme, catalogPane, new Thickness(12));

        // Right Column: Target Campaign Groups
        Grid targetsPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8
        };

        StackPanel targetHeaderRow = UiControls.CreateSubSectionHeader(
            this._theme,
            this._i18n["Groups.TargetListTitle"],
            VectorIcons.CheckCircle,
            out TextBlock targetListTitle);
        Grid.SetRow(targetHeaderRow, 0);
        targetsPane.Children.Add(targetHeaderRow);

        Grid bulkToggleRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 6
        };

        Button selectAllTargetsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button deselectAllTargetsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand,
            VectorIcons.StopCircle);

        Grid.SetColumn(selectAllTargetsBtn, 0);
        Grid.SetColumn(deselectAllTargetsBtn, 1);
        bulkToggleRow.Children.Add(selectAllTargetsBtn);
        bulkToggleRow.Children.Add(deselectAllTargetsBtn);
        Grid.SetRow(bulkToggleRow, 1);
        targetsPane.Children.Add(bulkToggleRow);

        ListBox targetList = UiControls.CreateListBox<SavedTargetGroup>(
            this._theme,
            this._vm.TargetGroups,
            tg => UiControls.CreateTargetGroupItemCard(
                this._theme,
                tg,
                nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState)),
            selectedItem: this._vm.SelectedTargetGroup,
            isVisible: this._vm.TargetGroups.Count > 0);

        targetList.SelectionChanged += (_, _) =>
        {
            if (targetList.SelectedItem is SavedTargetGroup stg)
            {
                this._vm.SelectedTargetGroup = stg;
            }
        };

        Border emptyTargetsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.CheckCircle,
            out TextBlock emptyTargetsTb);
        emptyTargetsBanner.IsVisible = this._vm.TargetGroups.Count == 0;
        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.TargetGroups.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetList.IsVisible = !isEmpty;
        };

        Grid targetListHost = UiControls.CreateListWithEmptyStateHost(targetList, emptyTargetsBanner);
        Grid.SetRow(targetListHost, 2);
        targetsPane.Children.Add(targetListHost);

        Button removeTargetBtn = UiControls.CreateDangerButton(
            this._theme,
            this._i18n["Groups.BtnRemoveTarget"],
            this._vm.RemoveSelectedTargetGroupCommand,
            VectorIcons.Trash);
        Grid.SetRow(removeTargetBtn, 3);
        targetsPane.Children.Add(removeTargetBtn);

        Border rightCard = UiControls.CreateCard(this._theme, targetsPane, new Thickness(12));

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedGroup), StringComparison.Ordinal) &&
                !ReferenceEquals(groupsList.SelectedItem, this._vm.SelectedGroup))
            {
                groupsList.SelectedItem = this._vm.SelectedGroup;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedTargetGroup), StringComparison.Ordinal) &&
                     !ReferenceEquals(targetList.SelectedItem, this._vm.SelectedTargetGroup))
            {
                targetList.SelectedItem = this._vm.SelectedTargetGroup;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.GroupSearchKeyword), StringComparison.Ordinal) &&
                     !string.Equals(searchKeywordBox.Text, this._vm.GroupSearchKeyword, StringComparison.Ordinal))
            {
                searchKeywordBox.Text = this._vm.GroupSearchKeyword;
            }
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Groups.Header"];
            descTb.Text = this._i18n["Groups.Desc"];
            emptyGroupsTb.Text = this._i18n["Empty.Groups"];
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            searchKeywordBox.PlaceholderText = this._i18n["Groups.SearchKeywordPlaceholder"];
            localFilterBox.PlaceholderText = this._i18n["Groups.FilterPlaceholder"];
            targetListTitle.Text = this._i18n["Groups.TargetListTitle"];
            UiControls.SetButtonText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            UiControls.SetButtonText(searchBtn, this._i18n["Groups.BtnSearchNew"]);
            UiControls.SetButtonText(saveTargetBtn, this._i18n["Groups.BtnSaveTarget"]);
            UiControls.SetButtonText(joinBtn, this._i18n["Groups.BtnJoinSelected"]);
            UiControls.SetButtonText(autoJoinAllBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
            UiControls.SetButtonText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            UiControls.SetButtonText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            UiControls.SetButtonText(removeTargetBtn, this._i18n["Groups.BtnRemoveTarget"]);
        };

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        pageGrid.Children.Add(leftCard);
        pageGrid.Children.Add(rightCard);
        return pageGrid;
    }
}
