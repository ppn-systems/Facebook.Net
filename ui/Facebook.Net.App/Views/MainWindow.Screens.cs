// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the single-focus Accounts Page and Groups Page.
/// Uses compact headers with SVG Info (`i`) flyouts and clean popovers/segmented views so each page stays uncluttered.
/// </summary>
public sealed partial class MainWindow
{
    private Control BuildAccountsScreen()
    {
        StackPanel pageStack = new() { Spacing = 14 };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Accounts.Header"],
            this._i18n["Accounts.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        pageStack.Children.Add(header);

        // Quick Import Flyout Card (opened only when the user clicks the Import Session button)
        TextBox nameBox = UiControls.CreateTextBox(this._theme, this._i18n["Accounts.NamePlaceholder"]);
        nameBox.TextChanged += (_, _) => this._vm.AccountFriendlyNameInput = nameBox.Text ?? string.Empty;

        TextBox cookieBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Accounts.CookiePlaceholder"],
            multiline: true,
            height: 85);
        cookieBox.TextChanged += (_, _) => this._vm.SessionImportRawInput = cookieBox.Text ?? string.Empty;

        Button confirmImportBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Accounts.BtnQuickImport"],
            this._vm.QuickImportSessionCommand,
            VectorIcons.Accounts);

        StackPanel importFlyoutStack = new()
        {
            Spacing = 8,
            Width = 340
        };
        importFlyoutStack.Children.Add(nameBox);
        importFlyoutStack.Children.Add(cookieBox);
        importFlyoutStack.Children.Add(confirmImportBtn);

        Flyout importFlyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = UiControls.CreateCard(this._theme, importFlyoutStack, new Thickness(14))
        };

        // Top 1-Click Action Bar
        Grid topActionBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = 8
        };

        Button openBrowserBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Button pasteClipboardBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnPasteClipboard"],
            iconSvg: VectorIcons.Sync);
        pasteClipboardBtn.Click += async (_, _) =>
        {
            if (this.Clipboard is not null)
            {
                string? clipText = await this.Clipboard.TryGetTextAsync().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(clipText))
                {
                    cookieBox.Text = clipText;
                    this._vm.SessionImportRawInput = clipText;
                    await this._vm.QuickImportSessionAsync().ConfigureAwait(true);
                    cookieBox.Text = string.Empty;
                    nameBox.Text = string.Empty;
                }
            }
        };

        Button openImportFlyoutBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnQuickImport"],
            iconSvg: VectorIcons.Accounts);
        openImportFlyoutBtn.Flyout = importFlyout;

        Grid.SetColumn(openBrowserBtn, 0);
        Grid.SetColumn(pasteClipboardBtn, 1);
        Grid.SetColumn(openImportFlyoutBtn, 2);
        topActionBar.Children.Add(openBrowserBtn);
        topActionBar.Children.Add(pasteClipboardBtn);
        topActionBar.Children.Add(openImportFlyoutBtn);
        pageStack.Children.Add(topActionBar);

        // Visual Account List (Single-focus center card)
        ListBox accountList = new()
        {
            MinHeight = 300,
            MaxHeight = 380,
            ItemsSource = this._vm.Accounts,
            SelectedItem = this._vm.SelectedAccount,
            ItemTemplate = new FuncDataTemplate<AccountProfile>((item, _) =>
            {
                if (item is null)
                {
                    return new Border();
                }

                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Thickness(4, 5)
                };

                Border avatar = UiControls.CreateAvatarBadge(this._theme, item.DisplayName, 36);
                Grid.SetColumn(avatar, 0);

                string FormatSubInfo()
                {
                    string timePart = item.LastActiveAt.HasValue
                        ? $"{this._i18n["Accounts.LastActivePrefix"]} {item.LastActiveAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}"
                        : this._i18n["Accounts.ReadyToConnect"];
                    string netPart = !string.IsNullOrWhiteSpace(item.ProxyId)
                        ? $"{this._i18n["Accounts.ProxyBadgePrefix"]} ✓"
                        : this._i18n["Accounts.NoProxyBadge"];
                    return $"{timePart}  •  {netPart}";
                }

                StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                TextBlock displayName = UiControls.CreateThemedLabel(this._theme, item.DisplayName, 14, FontWeight.SemiBold);
                TextBlock subInfo = UiControls.CreateThemedLabel(
                    this._theme,
                    FormatSubInfo(),
                    11,
                    FontWeight.Normal,
                    isSecondary: true);
                info.Children.Add(displayName);
                info.Children.Add(subInfo);
                Grid.SetColumn(info, 1);

                AuditSeverity sev = item.Status == AccountStatus.Active
                    ? AuditSeverity.Success
                    : (item.Status == AccountStatus.Checkpoint ? AuditSeverity.Error : AuditSeverity.Warning);
                Border statusPill = UiControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatAccountStatus(item.Status),
                    sev);
                Grid.SetColumn(statusPill, 2);

                this._i18n.LanguageChanged += (_, _) =>
                {
                    subInfo.Text = FormatSubInfo();
                    UiControls.SetPillText(statusPill, this._i18n.FormatAccountStatus(item.Status));
                };

                row.Children.Add(avatar);
                row.Children.Add(info);
                row.Children.Add(statusPill);
                return UiControls.CreateInsetWell(this._theme, row, new Thickness(10, 6));
            })
        };

        accountList.SelectionChanged += (_, _) =>
        {
            if (accountList.SelectedItem is AccountProfile acc)
            {
                this._vm.SelectedAccount = acc;
            }
        };

        Border emptyAccountsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.Accounts"],
            VectorIcons.Accounts,
            out TextBlock emptyAccTb);
        emptyAccountsBanner.IsVisible = this._vm.Accounts.Count == 0;
        this._vm.Accounts.CollectionChanged += (_, _) =>
            emptyAccountsBanner.IsVisible = this._vm.Accounts.Count == 0;

        // Bottom Selected Account Actions & Proxy Bar
        Grid selectedAccBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };

        TextBox proxyBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Accounts.ProxyPlaceholder"]);
        proxyBox.TextChanged += (_, _) => this._vm.AccountProxyInput = proxyBox.Text ?? string.Empty;

        Button saveProxyBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnSaveProxy"],
            this._vm.SaveAccountProxyCommand,
            VectorIcons.Settings);

        Button checkHealthBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnCheckHealth"],
            this._vm.CheckAccountHealthCommand,
            VectorIcons.CheckCircle);

        Button warmUpBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnWarmUp"],
            this._vm.WarmUpAccountCommand,
            VectorIcons.Sync);

        Button deleteBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnDelete"],
            this._vm.DeleteSelectedAccountCommand);

        Grid.SetColumn(proxyBox, 0);
        Grid.SetColumn(saveProxyBtn, 1);
        Grid.SetColumn(checkHealthBtn, 2);
        Grid.SetColumn(warmUpBtn, 3);
        Grid.SetColumn(deleteBtn, 4);
        selectedAccBar.Children.Add(proxyBox);
        selectedAccBar.Children.Add(saveProxyBtn);
        selectedAccBar.Children.Add(checkHealthBtn);
        selectedAccBar.Children.Add(warmUpBtn);
        selectedAccBar.Children.Add(deleteBtn);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedAccount), StringComparison.Ordinal) &&
                !ReferenceEquals(accountList.SelectedItem, this._vm.SelectedAccount))
            {
                accountList.SelectedItem = this._vm.SelectedAccount;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.AccountFriendlyNameInput), StringComparison.Ordinal) &&
                     !string.Equals(nameBox.Text, this._vm.AccountFriendlyNameInput, StringComparison.Ordinal))
            {
                nameBox.Text = this._vm.AccountFriendlyNameInput;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SessionImportRawInput), StringComparison.Ordinal) &&
                     !string.Equals(cookieBox.Text, this._vm.SessionImportRawInput, StringComparison.Ordinal))
            {
                cookieBox.Text = this._vm.SessionImportRawInput;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.AccountProxyInput), StringComparison.Ordinal) &&
                     !string.Equals(proxyBox.Text, this._vm.AccountProxyInput, StringComparison.Ordinal))
            {
                proxyBox.Text = this._vm.AccountProxyInput;
            }
        };

        pageStack.Children.Add(emptyAccountsBanner);
        pageStack.Children.Add(accountList);
        pageStack.Children.Add(selectedAccBar);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Accounts.Header"];
            descTb.Text = this._i18n["Accounts.Desc"];
            emptyAccTb.Text = this._i18n["Empty.Accounts"];
            nameBox.PlaceholderText = this._i18n["Accounts.NamePlaceholder"];
            cookieBox.PlaceholderText = this._i18n["Accounts.CookiePlaceholder"];
            proxyBox.PlaceholderText = this._i18n["Accounts.ProxyPlaceholder"];
            UiControls.SetButtonText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            UiControls.SetButtonText(openImportFlyoutBtn, this._i18n["Accounts.BtnQuickImport"]);
            UiControls.SetButtonText(confirmImportBtn, this._i18n["Accounts.BtnQuickImport"]);
            UiControls.SetButtonText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            UiControls.SetButtonText(checkHealthBtn, this._i18n["Accounts.BtnCheckHealth"]);
            UiControls.SetButtonText(warmUpBtn, this._i18n["Accounts.BtnWarmUp"]);
            UiControls.SetButtonText(saveProxyBtn, this._i18n["Accounts.BtnSaveProxy"]);
            UiControls.SetButtonText(deleteBtn, this._i18n["Accounts.BtnDelete"]);
        };

        return UiControls.CreateCard(this._theme, new ScrollViewer { Content = pageStack });
    }

    private Control BuildGroupsScreen()
    {
        StackPanel pageStack = new() { Spacing = 12 };

        // Header + Segmented View Switcher (Catalog vs Target Campaign Groups) on one clean single page
        Grid topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button tabCatalogBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Nav.Groups"],
            iconSvg: VectorIcons.Groups);

        Button tabTargetsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.TargetListTitle"],
            iconSvg: VectorIcons.CheckCircle);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(tabCatalogBtn, 1);
        Grid.SetColumn(tabTargetsBtn, 2);
        topBar.Children.Add(header);
        topBar.Children.Add(tabCatalogBtn);
        topBar.Children.Add(tabTargetsBtn);
        pageStack.Children.Add(topBar);

        // View 1: Group Catalog & Discovery
        StackPanel catalogPane = new() { Spacing = 10 };

        Grid searchAndSyncRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8
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

        TextBox localFilterBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.FilterPlaceholder"]);
        localFilterBox.TextChanged += (_, _) => this._vm.ApplyLocalGroupFilter(localFilterBox.Text);

        ListBox groupsList = new()
        {
            Height = 290,
            ItemsSource = this._vm.JoinedGroups,
            SelectedItem = this._vm.SelectedGroup,
            ItemTemplate = new FuncDataTemplate<GroupSummary>((grp, _) =>
            {
                if (grp is null)
                {
                    return new Border();
                }

                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 10,
                    Margin = new Thickness(4)
                };

                Border badge = UiControls.CreateAvatarBadge(this._theme, grp.Name, 30);
                Grid.SetColumn(badge, 0);

                TextBlock nameTb = UiControls.CreateThemedLabel(this._theme, grp.Name, 13, FontWeight.Medium);
                Grid.SetColumn(nameTb, 1);

                Border statePill = UiControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatGroupMembership(grp.MembershipState),
                    grp.MembershipState == GroupMembershipState.Member ? AuditSeverity.Success : AuditSeverity.Info);
                Grid.SetColumn(statePill, 2);

                this._i18n.LanguageChanged += (_, _) =>
                    UiControls.SetPillText(statePill, this._i18n.FormatGroupMembership(grp.MembershipState));

                row.Children.Add(badge);
                row.Children.Add(nameTb);
                row.Children.Add(statePill);
                return row;
            })
        };

        groupsList.SelectionChanged += (_, _) =>
        {
            if (groupsList.SelectedItem is GroupSummary g)
            {
                this._vm.SelectedGroup = g;
            }
        };

        Grid actionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        Button saveTargetBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand);

        Button joinBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnJoinSelected"],
            this._vm.JoinSelectedGroupCommand);

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

        Border emptyGroupsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.Groups"],
            VectorIcons.Groups,
            out TextBlock emptyGroupsTb);
        emptyGroupsBanner.IsVisible = this._vm.JoinedGroups.Count == 0;
        this._vm.JoinedGroups.CollectionChanged += (_, _) =>
            emptyGroupsBanner.IsVisible = this._vm.JoinedGroups.Count == 0;

        catalogPane.Children.Add(searchAndSyncRow);
        catalogPane.Children.Add(localFilterBox);
        catalogPane.Children.Add(emptyGroupsBanner);
        catalogPane.Children.Add(groupsList);
        catalogPane.Children.Add(actionRow);

        // View 2: Target Campaign Groups Pane
        StackPanel targetsPane = new()
        {
            Spacing = 10,
            IsVisible = false
        };

        Grid bulkToggleRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        Button selectAllTargetsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button deselectAllTargetsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand);

        Button removeTargetBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnRemoveTarget"],
            this._vm.RemoveSelectedTargetGroupCommand);

        Grid.SetColumn(selectAllTargetsBtn, 0);
        Grid.SetColumn(deselectAllTargetsBtn, 1);
        Grid.SetColumn(removeTargetBtn, 2);
        bulkToggleRow.Children.Add(selectAllTargetsBtn);
        bulkToggleRow.Children.Add(deselectAllTargetsBtn);
        bulkToggleRow.Children.Add(removeTargetBtn);

        ListBox targetList = new()
        {
            Height = 330,
            ItemsSource = this._vm.TargetGroups,
            SelectedItem = this._vm.SelectedTargetGroup,
            ItemTemplate = new FuncDataTemplate<SavedTargetGroup>((tg, _) =>
            {
                if (tg is null)
                {
                    return new Border();
                }

                StackPanel sp = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Thickness(4)
                };

                CheckBox enabledCheck = new()
                {
                    IsChecked = tg.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center
                };
                enabledCheck.IsCheckedChanged += async (_, _) =>
                {
                    bool nextState = enabledCheck.IsChecked ?? false;
                    if (nextState != tg.IsEnabled)
                    {
                        await this._vm.ToggleTargetGroupEnabledAsync(tg, nextState).ConfigureAwait(true);
                    }
                };

                sp.Children.Add(enabledCheck);
                sp.Children.Add(UiControls.CreateAvatarBadge(this._theme, tg.GroupName, 28));
                sp.Children.Add(UiControls.CreateThemedLabel(this._theme, tg.GroupName, 13, FontWeight.SemiBold));
                return sp;
            })
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
            emptyTargetsBanner.IsVisible = this._vm.TargetGroups.Count == 0;

        targetsPane.Children.Add(bulkToggleRow);
        targetsPane.Children.Add(emptyTargetsBanner);
        targetsPane.Children.Add(targetList);

        tabCatalogBtn.Click += (_, _) =>
        {
            catalogPane.IsVisible = true;
            targetsPane.IsVisible = false;
        };
        tabTargetsBtn.Click += (_, _) =>
        {
            catalogPane.IsVisible = false;
            targetsPane.IsVisible = true;
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedGroup), StringComparison.Ordinal) &&
                !ReferenceEquals(groupsList.SelectedItem, this._vm.SelectedGroup))
            {
                groupsList.SelectedItem = this._vm.SelectedGroup;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedTargetGroup), StringComparison.Ordinal) &&
                     !ReferenceEquals(targetList.SelectedItem, this._vm.SelectedTargetGroup))
            {
                targetList.SelectedItem = this._vm.SelectedTargetGroup;
            }
        };

        pageStack.Children.Add(catalogPane);
        pageStack.Children.Add(targetsPane);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Groups.Header"];
            descTb.Text = this._i18n["Groups.Desc"];
            UiControls.SetButtonText(tabCatalogBtn, this._i18n["Nav.Groups"]);
            UiControls.SetButtonText(tabTargetsBtn, this._i18n["Groups.TargetListTitle"]);
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

        return UiControls.CreateCard(this._theme, new ScrollViewer { Content = pageStack });
    }
}
