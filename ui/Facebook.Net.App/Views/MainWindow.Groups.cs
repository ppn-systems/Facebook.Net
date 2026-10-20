// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the Groups Discovery &amp; Campaign Targets screen (`MainWindow.Groups.cs`).
/// </summary>
public sealed partial class MainWindow
{
    private Control BuildGroupsScreen()
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Balanced 2-Column Layout (`1.15*,1*`) for Facebook Group Catalog (Left) & Campaign Target Groups (Right)
        Grid mainSplit = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,1*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Left Column: Facebook Group Catalog & Discovery
        Grid catalogPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8
        };

        Grid searchAndSyncRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6
        };

        Button syncJoinedBtn = UiControls.CreatePrimaryButton(
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
        Grid.SetRow(searchAndSyncRow, 0);
        catalogPane.Children.Add(searchAndSyncRow);

        TextBox localFilterBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.FilterPlaceholder"]);
        localFilterBox.TextChanged += (_, _) => this._vm.ApplyLocalGroupFilter(localFilterBox.Text);
        Grid.SetRow(localFilterBox, 1);
        catalogPane.Children.Add(localFilterBox);

        ListBox groupsList = new()
        {
            IsVisible = this._vm.JoinedGroups.Count > 0,
            ItemsSource = this._vm.JoinedGroups,
            SelectedItem = this._vm.SelectedGroup,
            ItemTemplate = new FuncDataTemplate<GroupSummary>((grp, _) =>
                grp is null
                    ? new Border()
                    : UiControls.CreateGroupCatalogItemCard(this._theme, this._i18n, grp))
        };

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

        ListBox targetList = new()
        {
            IsVisible = this._vm.TargetGroups.Count > 0,
            ItemsSource = this._vm.TargetGroups,
            SelectedItem = this._vm.SelectedTargetGroup,
            ItemTemplate = new FuncDataTemplate<SavedTargetGroup>((tg, ns) =>
                tg is null
                    ? new Border()
                    : UiControls.CreateTargetGroupItemCard(
                        this._theme,
                        tg,
                        nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState)))
        };

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

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        mainSplit.Children.Add(leftCard);
        mainSplit.Children.Add(rightCard);
        Grid.SetRow(mainSplit, 1);
        pageGrid.Children.Add(mainSplit);

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
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Groups.Header"];
            descTb.Text = this._i18n["Groups.Desc"];
            targetListTitle.Text = this._i18n["Groups.TargetListTitle"];
            emptyGroupsTb.Text = this._i18n["Empty.Groups"];
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            searchKeywordBox.PlaceholderText = this._i18n["Groups.SearchKeywordPlaceholder"];
            localFilterBox.PlaceholderText = this._i18n["Groups.FilterPlaceholder"];
            UiControls.SetButtonText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            UiControls.SetButtonText(searchBtn, this._i18n["Groups.BtnSearchNew"]);
            UiControls.SetButtonText(saveTargetBtn, this._i18n["Groups.BtnSaveTarget"]);
            UiControls.SetButtonText(joinBtn, this._i18n["Groups.BtnJoinSelected"]);
            UiControls.SetButtonText(autoJoinAllBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
            UiControls.SetButtonText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            UiControls.SetButtonText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            UiControls.SetButtonText(removeTargetBtn, this._i18n["Groups.BtnRemoveTarget"]);
        };

        return pageGrid;
    }
}
