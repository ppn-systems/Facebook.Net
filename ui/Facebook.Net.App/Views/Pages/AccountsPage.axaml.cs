// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Accounts Management Page (<c>AccountsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to cleanly separate:
/// 1) Account Vault &amp; Filter List,
/// 2) Add &amp; Import Account Session,
/// 3) Dedicated Network Proxy Setup.
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
        Control tab1View = this.BuildVaultTab(out Action refreshTab1);
        Control tab2View = this.BuildImportTab(out Action refreshTab2);
        Control tab3View = this.BuildProxyTab(out Action refreshTab3);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Accounts.TabVault"], VectorIcons.Accounts, tab1View),
                new SubTabItem(this._i18n["Accounts.TabImport"], VectorIcons.Plus, tab2View),
                new SubTabItem(this._i18n["Accounts.TabProxy"], VectorIcons.Shield, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Accounts.TabVault"],
                this._i18n["Accounts.TabImport"],
                this._i18n["Accounts.TabProxy"]
            ]);
            refreshTab1();
            refreshTab2();
            refreshTab3();
        };

        return subTabBar;
    }

    private Control BuildVaultTab(out Action refreshI18n)
    {
        Grid tabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };

        // Row 0: Header + 1-Click Browser Login & Paste Clipboard
        Grid topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Accounts.Header"],
            this._i18n["Accounts.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

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
                    this._vm.SessionImportRawInput = clipText;
                    await this._vm.QuickImportSessionAsync().ConfigureAwait(true);
                }
            }
        };

        Button openBrowserBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(pasteClipboardBtn, 1);
        Grid.SetColumn(openBrowserBtn, 2);
        topBar.Children.Add(header);
        topBar.Children.Add(pasteClipboardBtn);
        topBar.Children.Add(openBrowserBtn);
        Grid.SetRow(topBar, 0);
        tabGrid.Children.Add(topBar);

        // Row 1: Unified Search & Filter Bar ([ ▽ Bộ lọc | Tìm hoặc lọc... | 🔍 ]) with Flyout Popover
        AccountStatus? currentStatusFilter = null;
        SearchFilterBar accountSearchBar = null!;

        SegmentedFilterPill statusPill = new(
            this._theme,
            [
                this._i18n["Accounts.FilterSegActive"],
                this._i18n["Accounts.FilterSegAttention"],
                this._i18n["Accounts.FilterSegAll"]
            ],
            initialIndex: 2,
            onSelected: idx =>
            {
                currentStatusFilter = idx switch
                {
                    0 => AccountStatus.Active,
                    1 => AccountStatus.CookieExpired,
                    _ => null
                };
                accountSearchBar?.SetFilterActive(currentStatusFilter is not null);
                this._vm.ApplyLocalAccountFilter(accountSearchBar?.SearchTextBox.Text, currentStatusFilter);
            });

        accountSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Accounts.SearchPlaceholder"],
            this._vm.AccountFilterText,
            filterFlyoutContent: statusPill);
        accountSearchBar.SearchTextBox.TextChanged += (_, _) =>
            this._vm.ApplyLocalAccountFilter(accountSearchBar.SearchTextBox.Text, currentStatusFilter);
        accountSearchBar.SearchButton.Click += (_, _) =>
            this._vm.ApplyLocalAccountFilter(accountSearchBar.SearchTextBox.Text, currentStatusFilter);

        Grid.SetRow(accountSearchBar, 1);
        tabGrid.Children.Add(accountSearchBar);

        // Row 2: Visual Account List
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
        Grid.SetRow(accountListHost, 2);
        tabGrid.Children.Add(accountListHost);

        // Row 3: Selected Account Actions
        Grid selectedAccBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
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
        Grid.SetRow(selectedAccBar, 3);
        tabGrid.Children.Add(selectedAccBar);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal) &&
                !ReferenceEquals(accountList.SelectedItem, this._vm.SelectedAccount))
            {
                accountList.SelectedItem = this._vm.SelectedAccount;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Accounts.Header"];
            descTb.Text = this._i18n["Accounts.Desc"];
            statusPill.UpdateLabels(
            [
                this._i18n["Accounts.FilterSegActive"],
                this._i18n["Accounts.FilterSegAttention"],
                this._i18n["Accounts.FilterSegAll"]
            ]);
            emptyAccTb.Text = this._i18n["Empty.Accounts"];
            accountSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Accounts.SearchPlaceholder"]);
            AppButton.SetText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            AppButton.SetText(checkHealthBtn, this._i18n["Accounts.BtnCheckHealth"]);
            AppButton.SetText(warmUpBtn, this._i18n["Accounts.BtnWarmUp"]);
            AppButton.SetText(deleteBtn, this._i18n["Accounts.BtnDelete"]);
        };

        return tabGrid;
    }
}
