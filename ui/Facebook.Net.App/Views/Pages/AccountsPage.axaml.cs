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
/// 3) Dedicated Network Proxy Setup,
/// 4) Friend Requests (Incoming &amp; Outgoing).
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
        Action? refreshTab1 = null;
        Action? refreshTab2 = null;
        Action? refreshTab3 = null;
        Action? refreshTab4 = null;

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Accounts.TabVault"], VectorIcons.Accounts, () => this.BuildVaultTab(out refreshTab1)),
                new SubTabItem(this._i18n["Accounts.TabImport"], VectorIcons.Plus, () => this.BuildImportTab(out refreshTab2)),
                new SubTabItem(this._i18n["Accounts.TabProxy"], VectorIcons.Shield, () => this.BuildProxyTab(out refreshTab3)),
                new SubTabItem(this._i18n["Accounts.TabFriendRequests"], VectorIcons.Personal, () => this.BuildFriendRequestsTab(out refreshTab4))
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Accounts.TabVault"],
                this._i18n["Accounts.TabImport"],
                this._i18n["Accounts.TabProxy"],
                this._i18n["Accounts.TabFriendRequests"]
            ]);
            refreshTab1?.Invoke();
            refreshTab2?.Invoke();
            refreshTab3?.Invoke();
            refreshTab4?.Invoke();
        };

        return subTabBar;
    }

    private Control BuildVaultTab(out Action refreshI18n)
    {
        Grid tabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
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

        // Row 1: Unified Search & Filter Bar ([ ▽ Bộ lọc | Tìm hoặc lọc... | 🔍 ]) with 2-Level Drill-Down Popover
        string? currentModeFilter = null;
        bool? currentProxyFilter = null;
        SearchFilterBar accountSearchBar = null!;

        void RefreshAccountFilters() =>
            this._vm.ApplyLocalAccountFilterByMode(accountSearchBar?.SearchTextBox.Text, currentModeFilter, currentProxyFilter);

        FilterCategoryDefinition statusCategory = new(
            "status",
            this._i18n["Accounts.FilterCatStatus"],
            VectorIcons.CheckCircle,
            optionsProvider: () =>
            [
                new FilterOptionItem("active", this._i18n["Status.Account.Active"]),
                new FilterOptionItem("attention", this._i18n["Accounts.FilterSegAttention"])
            ],
            onApplied: selectedIds =>
            {
                currentModeFilter = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshAccountFilters();
            },
            allowMultiple: false);

        FilterCategoryDefinition proxyCategory = new(
            "proxy",
            this._i18n["Accounts.FilterCatProxy"],
            VectorIcons.Shield,
            optionsProvider: () =>
            [
                new FilterOptionItem("configured", this._i18n["Accounts.FilterProxyConfigured"]),
                new FilterOptionItem("direct", this._i18n["Accounts.FilterProxyDirect"])
            ],
            onApplied: selectedIds =>
            {
                string? selected = selectedIds.Count > 0 ? selectedIds[0] : null;
                currentProxyFilter = selected switch
                {
                    "configured" => true,
                    "direct" => false,
                    _ => null
                };
                RefreshAccountFilters();
            },
            allowMultiple: false);

        accountSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Accounts.SearchPlaceholder"],
            this._vm.AccountFilterText);
        accountSearchBar.SetFilterCategories(
            [statusCategory, proxyCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);
        accountSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshAccountFilters();
        accountSearchBar.SearchButton.Click += (_, _) => RefreshAccountFilters();

        Grid.SetRow(accountSearchBar, 1);
        tabGrid.Children.Add(accountSearchBar);

        // Row 2: Visual Account List with Per-Row Contextual Actions
        ListBox accountList = FormField.CreateListBox<AccountProfile>(
            this._theme,
            this._vm.Accounts,
            item => AccountRowCard.Create(
                this._theme,
                this._i18n,
                item,
                onWarmUpOrLogin: () => _ = this._vm.WarmUpOrLoginAccountItemAsync(item),
                warmUpOrLoginLabel: item.Status == AccountStatus.Active ? this._i18n["Accounts.BtnRowWarmUp"] : this._i18n["Accounts.BtnRowRelogin"],
                onCheckHealth: () => _ = this._vm.CheckAccountItemHealthAsync(item),
                checkHealthLabel: this._i18n["Accounts.BtnRowCheck"],
                onDelete: () =>
                {
                    ConfirmDialogModal.ShowAsync(
                        this,
                        this._theme,
                        this._i18n["Confirm.DeleteAccountTitle"],
                        this._i18n.Format("Confirm.DeleteAccountMessage", item.DisplayName),
                        this._i18n["Confirm.BtnConfirmDelete"],
                        this._i18n["Groups.BtnCancelEditTag"],
                        () => this._vm.DeleteAccountItemAsync(item));
                },
                deleteLabel: this._i18n["Accounts.BtnRowDelete"]),
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
            emptyAccTb.Text = this._i18n["Empty.Accounts"];
            statusCategory.Title = this._i18n["Accounts.FilterCatStatus"];
            proxyCategory.Title = this._i18n["Accounts.FilterCatProxy"];
            accountSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Accounts.SearchPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            AppButton.SetText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            RefreshAccountFilters();
        };

        return tabGrid;
    }
}
