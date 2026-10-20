// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tabs 2 and 3 of <see cref="AccountsPage"/>:
/// Tab 2: Add &amp; Import Account Session,
/// Tab 3: Dedicated Network Proxy Configuration.
/// </summary>
public sealed partial class AccountsPage
{
    private Control BuildImportTab(out Action refreshI18n)
    {
        StackPanel importStack = new() { Spacing = 14 };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel importHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Accounts.TabImport"],
            VectorIcons.Accounts,
            out TextBlock importTitleTb);

        Button pasteClipboardBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Accounts.BtnPasteClipboard"],
            iconSvg: VectorIcons.Clipboard);

        Button openBrowserBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Grid.SetColumn(importHeader, 0);
        Grid.SetColumn(pasteClipboardBtn, 1);
        Grid.SetColumn(openBrowserBtn, 2);
        topHeaderRow.Children.Add(importHeader);
        topHeaderRow.Children.Add(pasteClipboardBtn);
        topHeaderRow.Children.Add(openBrowserBtn);
        importStack.Children.Add(topHeaderRow);

        TextBox nameBox = FormField.CreateTextBox(this._theme, this._i18n["Accounts.NamePlaceholder"]);
        nameBox.TextChanged += (_, _) => this._vm.AccountFriendlyNameInput = nameBox.Text ?? string.Empty;

        TextBox cookieBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Accounts.CookiePlaceholder"],
            multiline: true,
            height: 220);
        cookieBox.TextChanged += (_, _) => this._vm.SessionImportRawInput = cookieBox.Text ?? string.Empty;

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
                }
            }
        };

        Button confirmImportBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnQuickImport"],
            this._vm.QuickImportSessionCommand,
            VectorIcons.CheckCircle);

        importStack.Children.Add(nameBox);
        importStack.Children.Add(cookieBox);
        importStack.Children.Add(confirmImportBtn);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.AccountFriendlyNameInput), StringComparison.Ordinal) &&
                !string.Equals(nameBox.Text, this._vm.AccountFriendlyNameInput, StringComparison.Ordinal))
            {
                nameBox.Text = this._vm.AccountFriendlyNameInput;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SessionImportRawInput), StringComparison.Ordinal) &&
                     !string.Equals(cookieBox.Text, this._vm.SessionImportRawInput, StringComparison.Ordinal))
            {
                cookieBox.Text = this._vm.SessionImportRawInput;
            }
        };

        refreshI18n = () =>
        {
            importTitleTb.Text = this._i18n["Accounts.TabImport"];
            nameBox.PlaceholderText = this._i18n["Accounts.NamePlaceholder"];
            cookieBox.PlaceholderText = this._i18n["Accounts.CookiePlaceholder"];
            AppButton.SetText(pasteClipboardBtn, this._i18n["Accounts.BtnPasteClipboard"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            AppButton.SetText(confirmImportBtn, this._i18n["Accounts.BtnQuickImport"]);
        };

        return SurfaceCard.Create(this._theme, importStack, new Thickness(20, 18));
    }

    private Control BuildProxyTab(out Action refreshI18n)
    {
        StackPanel proxyStack = new() { Spacing = 14 };

        StackPanel proxyHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Accounts.TabProxy"],
            VectorIcons.Shield,
            out TextBlock proxyTitleTb);
        proxyStack.Children.Add(proxyHeader);

        // Selected Account Context Banner
        Grid selectedAccRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock selectedAccLabelTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Accounts.ProxySelectedAccountLabel"],
            12,
            FontWeight.Medium,
            isSecondary: true);

        TextBlock selectedAccNameTb = SectionHeader.CreateLabel(
            this._theme,
            "—",
            13.5,
            FontWeight.Bold);

        Border proxyStatusBadge = StatusPill.Create(
            this._theme,
            this._i18n["Accounts.NoProxyBadge"],
            AuditSeverity.Info);

        StackPanel accInfoStack = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        accInfoStack.Children.Add(selectedAccLabelTb);
        accInfoStack.Children.Add(selectedAccNameTb);

        Border accIconWell = AvatarBadge.Create(this._theme, "FB", 36);
        Grid.SetColumn(accIconWell, 0);
        Grid.SetColumn(accInfoStack, 1);
        Grid.SetColumn(proxyStatusBadge, 2);
        selectedAccRow.Children.Add(accIconWell);
        selectedAccRow.Children.Add(accInfoStack);
        selectedAccRow.Children.Add(proxyStatusBadge);

        Border selectedAccWell = SurfaceCard.CreateInset(this._theme, selectedAccRow, new Thickness(14, 10));
        proxyStack.Children.Add(selectedAccWell);

        TextBox proxyBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Accounts.ProxyPlaceholder"],
            this._vm.AccountProxyInput);
        proxyBox.TextChanged += (_, _) => this._vm.AccountProxyInput = proxyBox.Text ?? string.Empty;

        Button saveProxyBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Accounts.BtnSaveProxy"],
            this._vm.SaveAccountProxyCommand,
            VectorIcons.Shield);

        proxyStack.Children.Add(proxyBox);
        proxyStack.Children.Add(saveProxyBtn);

        void RefreshSelectedProxyBanner()
        {
            AccountProfile? acc = this._vm.SelectedAccount;
            if (acc is not null)
            {
                selectedAccNameTb.Text = acc.DisplayName;
                bool hasProxy = !string.IsNullOrWhiteSpace(acc.ProxyId);
                StatusPill.SetText(
                    proxyStatusBadge,
                    hasProxy ? $"{this._i18n["Accounts.ProxyBadgePrefix"]}: {acc.ProxyId}" : this._i18n["Accounts.NoProxyBadge"]);
                StatusPill.SetSeverity(proxyStatusBadge, this._theme, hasProxy ? AuditSeverity.Success : AuditSeverity.Info);
            }
            else
            {
                selectedAccNameTb.Text = this._i18n["Overview.StatusDisconnected"];
                StatusPill.SetText(proxyStatusBadge, this._i18n["Accounts.NoProxyBadge"]);
                StatusPill.SetSeverity(proxyStatusBadge, this._theme, AuditSeverity.Warning);
            }
        }

        RefreshSelectedProxyBanner();

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.AccountProxyInput), StringComparison.Ordinal) &&
                !string.Equals(proxyBox.Text, this._vm.AccountProxyInput, StringComparison.Ordinal))
            {
                proxyBox.Text = this._vm.AccountProxyInput;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshSelectedProxyBanner();
            }
        };

        refreshI18n = () =>
        {
            proxyTitleTb.Text = this._i18n["Accounts.TabProxy"];
            selectedAccLabelTb.Text = this._i18n["Accounts.ProxySelectedAccountLabel"];
            proxyBox.PlaceholderText = this._i18n["Accounts.ProxyPlaceholder"];
            AppButton.SetText(saveProxyBtn, this._i18n["Accounts.BtnSaveProxy"]);
            RefreshSelectedProxyBanner();
        };

        return SurfaceCard.Create(this._theme, proxyStack, new Thickness(20, 18));
    }
}
