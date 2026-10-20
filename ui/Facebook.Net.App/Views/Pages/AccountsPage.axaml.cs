// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Accounts Management Page (<c>AccountsPage.axaml</c>).
/// Supports 1-click browser login, clipboard cookie import, proxy assignment, and session health checks.
/// </summary>
public sealed partial class AccountsPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountsPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public AccountsPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public AccountsPage(ShellViewModel viewModel)
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
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 8
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Accounts.Header"],
            this._i18n["Accounts.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Quick Import Flyout Card
        TextBox nameBox = FormField.CreateTextBox(this._theme, this._i18n["Accounts.NamePlaceholder"]);
        nameBox.TextChanged += (_, _) => this._vm.AccountFriendlyNameInput = nameBox.Text ?? string.Empty;

        TextBox cookieBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Accounts.CookiePlaceholder"],
            multiline: true,
            height: 85);
        cookieBox.TextChanged += (_, _) => this._vm.SessionImportRawInput = cookieBox.Text ?? string.Empty;

        Button confirmImportBtn = AppButton.CreatePrimary(
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
            Content = SurfaceCard.Create(this._theme, importFlyoutStack, new Thickness(14))
        };

        // Row 1: Top 1-Click Action Bar
        Grid topActionBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };

        Button openBrowserBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Button pasteClipboardBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnPasteClipboard"],
            iconSvg: VectorIcons.Clipboard);
        pasteClipboardBtn.Click += async (_, _) =>
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                string? clipText = await clipboard.TryGetTextAsync().ConfigureAwait(true);
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

        Button openImportFlyoutBtn = AppButton.CreateSecondary(
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

        // Row 2: Unified SearchFilterBar for Accounts
        SearchFilterBar accountSearchBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Accounts.SearchPlaceholder"],
            this._vm.AccountFilterText);
        accountSearchBar.SearchTextBox.TextChanged += (_, _) =>
            this._vm.ApplyLocalAccountFilter(accountSearchBar.SearchTextBox.Text);
        accountSearchBar.SearchButton.Click += (_, _) =>
            this._vm.ApplyLocalAccountFilter(accountSearchBar.SearchTextBox.Text);
        Grid.SetRow(accountSearchBar, 2);
        pageGrid.Children.Add(accountSearchBar);

        // Row 3: Visual Account List using shared AccountRowCard
        ListBox accountList = FormField.CreateListBox<AccountProfile>(
            this._theme,
            this._vm.Accounts,
            item => AccountRowCard.Create(this._theme, this._i18n, item),
            selectedItem: this._vm.SelectedAccount,
            isVisible: this._vm.Accounts.Count > 0);

        accountList.SelectionChanged += (_, _) =>
        {
            if (accountList.SelectedItem is AccountProfile acc)
            {
                this._vm.SelectedAccount = acc;
            }
        };

        Border emptyAccountsBanner = EmptyStateBanner.Create(
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

        Grid accountListHost = EmptyStateBanner.CreateHost(accountList, emptyAccountsBanner);
        Grid.SetRow(accountListHost, 3);
        pageGrid.Children.Add(accountListHost);

        // Row 4: Dedicated Proxy Configuration Row
        Grid proxyRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox proxyBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Accounts.ProxyPlaceholder"]);
        proxyBox.TextChanged += (_, _) => this._vm.AccountProxyInput = proxyBox.Text ?? string.Empty;

        Button saveProxyBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnSaveProxy"],
            this._vm.SaveAccountProxyCommand,
            VectorIcons.Shield);

        Grid.SetColumn(proxyBox, 0);
        Grid.SetColumn(saveProxyBtn, 1);
        proxyRow.Children.Add(proxyBox);
        proxyRow.Children.Add(saveProxyBtn);
        Grid.SetRow(proxyRow, 4);
        pageGrid.Children.Add(proxyRow);

        // Row 5: Selected Account Action Buttons
        Grid selectedAccBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        Button checkHealthBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnCheckHealth"],
            this._vm.CheckAccountHealthCommand,
            VectorIcons.CheckCircle);

        Button warmUpBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnWarmUp"],
            this._vm.WarmUpAccountCommand,
            VectorIcons.Sync);

        Button deleteBtn = AppButton.CreateDanger(
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
        Grid.SetRow(selectedAccBar, 5);
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
            accountSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Accounts.SearchPlaceholder"]);
            nameBox.PlaceholderText = this._i18n["Accounts.NamePlaceholder"];
            cookieBox.PlaceholderText = this._i18n["Accounts.CookiePlaceholder"];
            proxyBox.PlaceholderText = this._i18n["Accounts.ProxyPlaceholder"];
            AppButton.SetText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            AppButton.SetText(openImportFlyoutBtn, this._i18n["Accounts.BtnQuickImport"]);
            AppButton.SetText(confirmImportBtn, this._i18n["Accounts.BtnQuickImport"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            AppButton.SetText(checkHealthBtn, this._i18n["Accounts.BtnCheckHealth"]);
            AppButton.SetText(warmUpBtn, this._i18n["Accounts.BtnWarmUp"]);
            AppButton.SetText(saveProxyBtn, this._i18n["Accounts.BtnSaveProxy"]);
            AppButton.SetText(deleteBtn, this._i18n["Accounts.BtnDelete"]);
        };

        return SurfaceCard.Create(this._theme, pageGrid);
    }
}
