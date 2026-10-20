// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the Accounts Management screen (`MainWindow.Accounts.cs`).
/// </summary>
public sealed partial class MainWindow
{
    private Control BuildAccountsScreen()
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 10
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Accounts.Header"],
            this._i18n["Accounts.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Quick Import Flyout Card
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
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
            iconSvg: VectorIcons.Clipboard);
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
        Grid.SetRow(topActionBar, 1);
        pageGrid.Children.Add(topActionBar);

        // Visual Account List using shared UiControls.CreateAccountItemCard
        ListBox accountList = new()
        {
            IsVisible = this._vm.Accounts.Count > 0,
            ItemsSource = this._vm.Accounts,
            SelectedItem = this._vm.SelectedAccount,
            ItemTemplate = new FuncDataTemplate<AccountProfile>((item, _) =>
                item is null
                    ? new Border()
                    : UiControls.CreateAccountItemCard(this._theme, this._i18n, item))
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
        {
            bool isEmpty = this._vm.Accounts.Count == 0;
            emptyAccountsBanner.IsVisible = isEmpty;
            accountList.IsVisible = !isEmpty;
        };

        Grid accountListHost = UiControls.CreateListWithEmptyStateHost(accountList, emptyAccountsBanner);
        Grid.SetRow(accountListHost, 2);
        pageGrid.Children.Add(accountListHost);

        // Row 3: Dedicated Proxy Configuration Row
        Grid proxyRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
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
            VectorIcons.Shield);

        Grid.SetColumn(proxyBox, 0);
        Grid.SetColumn(saveProxyBtn, 1);
        proxyRow.Children.Add(proxyBox);
        proxyRow.Children.Add(saveProxyBtn);
        Grid.SetRow(proxyRow, 3);
        pageGrid.Children.Add(proxyRow);

        // Row 4: Selected Account Action Buttons (all with SVG icons)
        Grid selectedAccBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

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

        Button deleteBtn = UiControls.CreateDangerButton(
            this._theme,
            this._i18n["Accounts.BtnDelete"],
            this._vm.DeleteSelectedAccountCommand,
            VectorIcons.Trash);

        Grid.SetColumn(checkHealthBtn, 0);
        Grid.SetColumn(warmUpBtn, 1);
        Grid.SetColumn(deleteBtn, 2);
        selectedAccBar.Children.Add(checkHealthBtn);
        selectedAccBar.Children.Add(warmUpBtn);
        selectedAccBar.Children.Add(deleteBtn);
        Grid.SetRow(selectedAccBar, 4);
        pageGrid.Children.Add(selectedAccBar);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal) &&
                !ReferenceEquals(accountList.SelectedItem, this._vm.SelectedAccount))
            {
                accountList.SelectedItem = this._vm.SelectedAccount;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.AccountFriendlyNameInput), StringComparison.Ordinal) &&
                     !string.Equals(nameBox.Text, this._vm.AccountFriendlyNameInput, StringComparison.Ordinal))
            {
                nameBox.Text = this._vm.AccountFriendlyNameInput;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SessionImportRawInput), StringComparison.Ordinal) &&
                     !string.Equals(cookieBox.Text, this._vm.SessionImportRawInput, StringComparison.Ordinal))
            {
                cookieBox.Text = this._vm.SessionImportRawInput;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.AccountProxyInput), StringComparison.Ordinal) &&
                     !string.Equals(proxyBox.Text, this._vm.AccountProxyInput, StringComparison.Ordinal))
            {
                proxyBox.Text = this._vm.AccountProxyInput;
            }
        };

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

        return UiControls.CreateCard(this._theme, pageGrid);
    }
}
