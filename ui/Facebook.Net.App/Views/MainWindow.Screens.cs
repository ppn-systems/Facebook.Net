// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

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
/// Partial implementation of <see cref="MainWindow"/> building the Accounts Screen and Groups Screen.
/// Enforces the Zero-UID Visual Selection paradigm (only friendly names, avatars, and status pills are shown).
/// </summary>
public sealed partial class MainWindow
{
    private Control BuildAccountsScreen()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("380,*"),
            ColumnSpacing = 16
        };

        // Left Card: 1-Click Session Import & Browser Login
        StackPanel leftStack = new() { Spacing = 10 };
        StackPanel header = CloudflareControls.CreateSectionHeader(
            this._theme,
            this._i18n["Accounts.Header"],
            this._i18n["Accounts.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        TextBox nameBox = CloudflareControls.CreateTextBox(this._theme, this._i18n["Accounts.NamePlaceholder"]);
        nameBox.TextChanged += (_, _) => this._vm.AccountFriendlyNameInput = nameBox.Text ?? string.Empty;

        TextBox cookieBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Accounts.CookiePlaceholder"],
            multiline: true,
            height: 95);
        cookieBox.TextChanged += (_, _) => this._vm.SessionImportRawInput = cookieBox.Text ?? string.Empty;

        TextBox proxyBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Accounts.ProxyPlaceholder"]);
        proxyBox.TextChanged += (_, _) => this._vm.AccountProxyInput = proxyBox.Text ?? string.Empty;

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.AccountFriendlyNameInput), System.StringComparison.Ordinal) &&
                !string.Equals(nameBox.Text, this._vm.AccountFriendlyNameInput, System.StringComparison.Ordinal))
            {
                nameBox.Text = this._vm.AccountFriendlyNameInput;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SessionImportRawInput), System.StringComparison.Ordinal) &&
                     !string.Equals(cookieBox.Text, this._vm.SessionImportRawInput, System.StringComparison.Ordinal))
            {
                cookieBox.Text = this._vm.SessionImportRawInput;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.AccountProxyInput), System.StringComparison.Ordinal) &&
                     !string.Equals(proxyBox.Text, this._vm.AccountProxyInput, System.StringComparison.Ordinal))
            {
                proxyBox.Text = this._vm.AccountProxyInput;
            }
        };

        Button pasteClipboardBtn = CloudflareControls.CreateSecondaryButton(
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

        Button quickImportBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnQuickImport"],
            this._vm.QuickImportSessionCommand,
            VectorIcons.Accounts);

        Button openBrowserBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Button checkHealthBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnCheckHealth"],
            this._vm.CheckAccountHealthCommand,
            VectorIcons.CheckCircle);

        Button saveProxyBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnSaveProxy"],
            this._vm.SaveAccountProxyCommand,
            VectorIcons.Settings);

        Button deleteBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnDelete"],
            this._vm.DeleteSelectedAccountCommand);

        leftStack.Children.Add(nameBox);
        leftStack.Children.Add(cookieBox);
        leftStack.Children.Add(proxyBox);
        leftStack.Children.Add(pasteClipboardBtn);
        leftStack.Children.Add(quickImportBtn);
        leftStack.Children.Add(openBrowserBtn);
        leftStack.Children.Add(checkHealthBtn);
        leftStack.Children.Add(saveProxyBtn);
        leftStack.Children.Add(deleteBtn);

        Border leftCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = leftStack });
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right Card: Visual Account Vault (Avatar + DisplayName + Proxy Status + Status Pill, NO raw UID)
        ListBox accountList = new()
        {
            ItemsSource = this._vm.Accounts,
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
                    Margin = new Thickness(4, 6)
                };

                Border avatar = CloudflareControls.CreateAvatarBadge(this._theme, item.DisplayName, 36);
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
                TextBlock displayName = CloudflareControls.CreateThemedLabel(this._theme, item.DisplayName, 14, FontWeight.SemiBold);
                TextBlock subInfo = CloudflareControls.CreateThemedLabel(
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
                Border statusPill = CloudflareControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatAccountStatus(item.Status),
                    sev);
                Grid.SetColumn(statusPill, 2);

                this._i18n.LanguageChanged += (_, _) =>
                {
                    subInfo.Text = FormatSubInfo();
                    CloudflareControls.SetPillText(statusPill, this._i18n.FormatAccountStatus(item.Status));
                };

                row.Children.Add(avatar);
                row.Children.Add(info);
                row.Children.Add(statusPill);
                return CloudflareControls.CreateInsetWell(this._theme, row, new Thickness(10, 6));
            })
        };

        accountList.SelectionChanged += (_, _) =>
        {
            if (accountList.SelectedItem is AccountProfile acc)
            {
                this._vm.SelectedAccount = acc;
            }
        };

        Border rightCard = CloudflareControls.CreateCard(this._theme, accountList);
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Accounts.Header"];
            descTb.Text = this._i18n["Accounts.Desc"];
            nameBox.PlaceholderText = this._i18n["Accounts.NamePlaceholder"];
            cookieBox.PlaceholderText = this._i18n["Accounts.CookiePlaceholder"];
            proxyBox.PlaceholderText = this._i18n["Accounts.ProxyPlaceholder"];
            CloudflareControls.SetButtonText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            CloudflareControls.SetButtonText(quickImportBtn, this._i18n["Accounts.BtnQuickImport"]);
            CloudflareControls.SetButtonText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            CloudflareControls.SetButtonText(checkHealthBtn, this._i18n["Accounts.BtnCheckHealth"]);
            CloudflareControls.SetButtonText(saveProxyBtn, this._i18n["Accounts.BtnSaveProxy"]);
            CloudflareControls.SetButtonText(deleteBtn, this._i18n["Accounts.BtnDelete"]);
        };

        return grid;
    }

    private Control BuildGroupsScreen()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 16
        };

        // Left: Synced & Discovered Groups Catalog
        StackPanel leftStack = new() { Spacing = 10 };
        StackPanel header = CloudflareControls.CreateSectionHeader(
            this._theme,
            this._i18n["Groups.Header"],
            this._i18n["Groups.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        Button syncJoinedBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Grid searchRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox searchKeywordBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.SearchKeywordPlaceholder"]);
        searchKeywordBox.TextChanged += (_, _) => this._vm.GroupSearchKeyword = searchKeywordBox.Text ?? string.Empty;

        Button searchBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSearchNew"],
            this._vm.SearchGroupsByKeywordCommand,
            VectorIcons.Search);

        Grid.SetColumn(searchKeywordBox, 0);
        Grid.SetColumn(searchBtn, 1);
        searchRow.Children.Add(searchKeywordBox);
        searchRow.Children.Add(searchBtn);

        TextBox localFilterBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Groups.FilterPlaceholder"]);
        localFilterBox.TextChanged += (_, _) => this._vm.ApplyLocalGroupFilter(localFilterBox.Text);

        ListBox groupsList = new()
        {
            Height = 250,
            ItemsSource = this._vm.JoinedGroups,
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

                Border badge = CloudflareControls.CreateAvatarBadge(this._theme, grp.Name, 30);
                Grid.SetColumn(badge, 0);

                TextBlock nameTb = CloudflareControls.CreateThemedLabel(this._theme, grp.Name, 13, FontWeight.Medium);
                Grid.SetColumn(nameTb, 1);

                Border statePill = CloudflareControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatGroupMembership(grp.MembershipState),
                    grp.MembershipState == GroupMembershipState.Member ? AuditSeverity.Success : AuditSeverity.Info);
                Grid.SetColumn(statePill, 2);

                this._i18n.LanguageChanged += (_, _) =>
                    CloudflareControls.SetPillText(statePill, this._i18n.FormatGroupMembership(grp.MembershipState));

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
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };

        Button saveTargetBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand);

        Button joinBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnJoinSelected"],
            this._vm.JoinSelectedGroupCommand);

        Grid.SetColumn(saveTargetBtn, 0);
        Grid.SetColumn(joinBtn, 1);
        actionRow.Children.Add(saveTargetBtn);
        actionRow.Children.Add(joinBtn);

        Button autoJoinAllBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnAutoJoinDiscovered"],
            this._vm.AutoJoinDiscoveredGroupsCommand,
            VectorIcons.Groups);

        leftStack.Children.Add(syncJoinedBtn);
        leftStack.Children.Add(searchRow);
        leftStack.Children.Add(localFilterBox);
        leftStack.Children.Add(groupsList);
        leftStack.Children.Add(actionRow);
        leftStack.Children.Add(autoJoinAllBtn);

        Border leftCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = leftStack });
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right: Selected Target Campaign Groups with CheckBox toggles
        StackPanel rightStack = new() { Spacing = 10 };
        TextBlock targetListHeader = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Groups.TargetListTitle"],
            15,
            FontWeight.SemiBold);

        Grid bulkToggleRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };

        Button selectAllTargetsBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button deselectAllTargetsBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand);

        Grid.SetColumn(selectAllTargetsBtn, 0);
        Grid.SetColumn(deselectAllTargetsBtn, 1);
        bulkToggleRow.Children.Add(selectAllTargetsBtn);
        bulkToggleRow.Children.Add(deselectAllTargetsBtn);

        ListBox targetList = new()
        {
            Height = 330,
            ItemsSource = this._vm.TargetGroups,
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
                sp.Children.Add(CloudflareControls.CreateAvatarBadge(this._theme, tg.GroupName, 28));
                sp.Children.Add(CloudflareControls.CreateThemedLabel(this._theme, tg.GroupName, 13, FontWeight.SemiBold));
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

        Button removeTargetBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Groups.BtnRemoveTarget"],
            this._vm.RemoveSelectedTargetGroupCommand);

        rightStack.Children.Add(targetListHeader);
        rightStack.Children.Add(bulkToggleRow);
        rightStack.Children.Add(targetList);
        rightStack.Children.Add(removeTargetBtn);

        Border rightCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = rightStack });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Groups.Header"];
            descTb.Text = this._i18n["Groups.Desc"];
            searchKeywordBox.PlaceholderText = this._i18n["Groups.SearchKeywordPlaceholder"];
            localFilterBox.PlaceholderText = this._i18n["Groups.FilterPlaceholder"];
            CloudflareControls.SetButtonText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            CloudflareControls.SetButtonText(searchBtn, this._i18n["Groups.BtnSearchNew"]);
            CloudflareControls.SetButtonText(saveTargetBtn, this._i18n["Groups.BtnSaveTarget"]);
            CloudflareControls.SetButtonText(joinBtn, this._i18n["Groups.BtnJoinSelected"]);
            CloudflareControls.SetButtonText(autoJoinAllBtn, this._i18n["Groups.BtnAutoJoinDiscovered"]);
            targetListHeader.Text = this._i18n["Groups.TargetListTitle"];
            CloudflareControls.SetButtonText(selectAllTargetsBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            CloudflareControls.SetButtonText(deselectAllTargetsBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            CloudflareControls.SetButtonText(removeTargetBtn, this._i18n["Groups.BtnRemoveTarget"]);
        };

        return grid;
    }
}
