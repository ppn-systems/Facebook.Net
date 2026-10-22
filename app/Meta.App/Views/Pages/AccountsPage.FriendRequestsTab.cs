// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Collections;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Pages;

/// <summary>
/// Tab 4 of <see cref="AccountsPage"/>:
/// Combined Incoming and Outgoing Friend Requests management with
/// one-click Accept / Decline / Cancel row actions.
/// </summary>
public sealed partial class AccountsPage
{
    private Control BuildFriendRequestsTab(out Action refreshI18n)
    {
        Grid pane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,*"),
            RowSpacing = 10
        };

        BulkObservableCollection<UserSummary> filteredIncoming = new();
        BulkObservableCollection<UserSummary> filteredOutgoing = new();

        // ── Row 0: Unified SearchFilterBar ──────────────────────────────────────
        string? directionFilter = null;
        SearchFilterBar friendSearchBar = null!;
        Action refreshFilteredRequests = null!;

        FilterCategoryDefinition directionCategory = new(
            "direction",
            this._i18n["Accounts.FilterCatStatus"],
            VectorIcons.Personal,
            optionsProvider: () =>
            [
                new FilterOptionItem("incoming", this._i18n["Personal.IncomingHeader"]),
                new FilterOptionItem("outgoing", this._i18n["Personal.OutgoingHeader"])
            ],
            onApplied: selectedIds =>
            {
                directionFilter = selectedIds.Count > 0 ? selectedIds[0] : null;
                refreshFilteredRequests?.Invoke();
            },
            allowMultiple: false);

        friendSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Personal.SearchRequestsPlaceholder"],
            string.Empty);
        friendSearchBar.SetFilterCategories(
            [directionCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);
        Grid.SetRow(friendSearchBar, 0);
        pane.Children.Add(friendSearchBar);

        // ── Row 1: Incoming section header + Accept All + Sync button ───────────
        Grid incomingHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid incomingHeader = SectionHeader.Create(
            this._theme,
            $"{this._i18n["Personal.IncomingHeader"]} (0)",
            this._i18n["Personal.IncomingDesc"],
            out TextBlock incomingTitleTb,
            out TextBlock incomingDescTb);

        Button acceptAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Personal.BtnAcceptAll"],
            null,
            VectorIcons.CheckCircle);
        acceptAllBtn.Click += (_, _) =>
        {
            if (this._vm.IncomingFriendRequests.Count == 0)
            {
                return;
            }

            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Personal.BtnAcceptAll"],
                this._i18n.Format("Personal.ConfirmAcceptAllMsg", this._vm.IncomingFriendRequests.Count),
                this._i18n["Personal.BtnAcceptAll"],
                this._i18n["Groups.BtnCancelEditTag"],
                () => this._vm.AcceptAllIncomingRequestsAsync(),
                VectorIcons.CheckCircle);
        };

        Button syncIncomingBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Personal.BtnSyncIncoming"],
            this._vm.SyncIncomingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(incomingHeader, 0);
        Grid.SetColumn(acceptAllBtn, 1);
        Grid.SetColumn(syncIncomingBtn, 2);
        incomingHeaderRow.Children.Add(incomingHeader);
        incomingHeaderRow.Children.Add(acceptAllBtn);
        incomingHeaderRow.Children.Add(syncIncomingBtn);
        Grid.SetRow(incomingHeaderRow, 1);
        pane.Children.Add(incomingHeaderRow);

        // ── Row 2: Incoming requests list ───────────────────────────────────────
        ListBox incomingList = FormField.CreateListBox<UserSummary>(
            this._theme,
            filteredIncoming,
            user => this.CreateFriendRow(
                user,
                acceptCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.ConfirmFriendRequestCommand.ExecuteAsync(null); },
                rejectCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.DeleteFriendRequestCommand.ExecuteAsync(null); },
                acceptLabel: this._i18n["Personal.BtnAccept"],
                rejectLabel: this._i18n["Personal.BtnReject"]),
            selectedItem: this._vm.SelectedIncomingRequest,
            isVisible: false);

        incomingList.SelectionChanged += (_, _) =>
        {
            if (incomingList.SelectedItem is UserSummary u)
            {
                this._vm.SelectedIncomingRequest = u;
            }
        };

        Border emptyIncoming = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.IncomingRequests"],
            VectorIcons.ThumbUp,
            out TextBlock emptyIncomingTb);
        emptyIncoming.IsVisible = true;

        Grid incomingHost = EmptyStateBanner.CreateHost(incomingList, emptyIncoming);
        Grid.SetRow(incomingHost, 2);
        pane.Children.Add(incomingHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedIncomingRequest), StringComparison.Ordinal) &&
                !ReferenceEquals(incomingList.SelectedItem, this._vm.SelectedIncomingRequest))
            {
                incomingList.SelectedItem = this._vm.SelectedIncomingRequest;
            }
        };

        // ── Row 3: Outgoing section header + Cancel All + Sync button ───────────
        Grid outgoingHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Grid outgoingHeader = SectionHeader.Create(
            this._theme,
            $"{this._i18n["Personal.OutgoingHeader"]} (0)",
            this._i18n["Personal.OutgoingDesc"],
            out TextBlock outgoingTitleTb,
            out TextBlock outgoingDescTb);

        Button cancelAllOutgoingBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Personal.BtnCancelAllOutgoing"],
            null,
            VectorIcons.StopCircle);
        cancelAllOutgoingBtn.Click += (_, _) =>
        {
            if (this._vm.OutgoingFriendRequests.Count == 0)
            {
                return;
            }

            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Personal.BtnCancelAllOutgoing"],
                this._i18n.Format("Personal.ConfirmCancelAllMsg", this._vm.OutgoingFriendRequests.Count),
                this._i18n["Personal.BtnCancelAllOutgoing"],
                this._i18n["Groups.BtnCancelEditTag"],
                () => this._vm.CancelAllOutgoingRequestsAsync(),
                VectorIcons.StopCircle);
        };

        Button syncOutgoingBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Personal.BtnSyncOutgoing"],
            this._vm.SyncOutgoingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(outgoingHeader, 0);
        Grid.SetColumn(cancelAllOutgoingBtn, 1);
        Grid.SetColumn(syncOutgoingBtn, 2);
        outgoingHeaderRow.Children.Add(outgoingHeader);
        outgoingHeaderRow.Children.Add(cancelAllOutgoingBtn);
        outgoingHeaderRow.Children.Add(syncOutgoingBtn);
        Grid.SetRow(outgoingHeaderRow, 3);
        pane.Children.Add(outgoingHeaderRow);

        // ── Row 4: Outgoing requests list ───────────────────────────────────────
        ListBox outgoingList = FormField.CreateListBox<UserSummary>(
            this._theme,
            filteredOutgoing,
            user => this.CreateFriendRow(
                user,
                cancelCommand: () => { this._vm.SelectedOutgoingRequest = user; _ = this._vm.CancelFriendRequestCommand.ExecuteAsync(null); },
                cancelLabel: this._i18n["Personal.BtnCancel"]),
            selectedItem: this._vm.SelectedOutgoingRequest,
            isVisible: false);

        outgoingList.SelectionChanged += (_, _) =>
        {
            if (outgoingList.SelectedItem is UserSummary u)
            {
                this._vm.SelectedOutgoingRequest = u;
            }
        };

        Border emptyOutgoing = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.OutgoingRequests"],
            VectorIcons.Sync,
            out TextBlock emptyOutgoingTb);
        emptyOutgoing.IsVisible = true;

        Grid outgoingHost = EmptyStateBanner.CreateHost(outgoingList, emptyOutgoing);
        Grid.SetRow(outgoingHost, 4);
        pane.Children.Add(outgoingHost);

        void RefreshFilteredRequests()
        {
            string query = (friendSearchBar.SearchTextBox.Text ?? string.Empty).Trim();
            bool showIncoming = !string.Equals(directionFilter, "outgoing", StringComparison.OrdinalIgnoreCase);
            bool showOutgoing = !string.Equals(directionFilter, "incoming", StringComparison.OrdinalIgnoreCase);

            List<UserSummary> inMatches = new(this._vm.IncomingFriendRequests.Count);
            if (showIncoming)
            {
                for (int i = 0; i < this._vm.IncomingFriendRequests.Count; i++)
                {
                    UserSummary u = this._vm.IncomingFriendRequests[i];
                    if (this._vm.MatchesTextAccentInsensitive(u.Name, query))
                    {
                        inMatches.Add(u);
                    }
                }
            }

            filteredIncoming.ReplaceAll(inMatches);

            List<UserSummary> outMatches = new(this._vm.OutgoingFriendRequests.Count);
            if (showOutgoing)
            {
                for (int i = 0; i < this._vm.OutgoingFriendRequests.Count; i++)
                {
                    UserSummary u = this._vm.OutgoingFriendRequests[i];
                    if (this._vm.MatchesTextAccentInsensitive(u.Name, query))
                    {
                        outMatches.Add(u);
                    }
                }
            }

            filteredOutgoing.ReplaceAll(outMatches);

            incomingTitleTb.Text = $"{this._i18n["Personal.IncomingHeader"]} ({filteredIncoming.Count})";
            outgoingTitleTb.Text = $"{this._i18n["Personal.OutgoingHeader"]} ({filteredOutgoing.Count})";
            acceptAllBtn.IsEnabled = this._vm.IncomingFriendRequests.Count > 0;
            cancelAllOutgoingBtn.IsEnabled = this._vm.OutgoingFriendRequests.Count > 0;

            bool isIncomingEmpty = filteredIncoming.Count == 0;
            emptyIncoming.IsVisible = isIncomingEmpty;
            incomingList.IsVisible = !isIncomingEmpty;

            bool isOutgoingEmpty = filteredOutgoing.Count == 0;
            emptyOutgoing.IsVisible = isOutgoingEmpty;
            outgoingList.IsVisible = !isOutgoingEmpty;
        }

        refreshFilteredRequests = RefreshFilteredRequests;
        friendSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredRequests();
        friendSearchBar.SearchButton.Click += (_, _) => RefreshFilteredRequests();
        this._vm.IncomingFriendRequests.CollectionChanged += (_, _) => RefreshFilteredRequests();
        this._vm.OutgoingFriendRequests.CollectionChanged += (_, _) => RefreshFilteredRequests();
        RefreshFilteredRequests();

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedOutgoingRequest), StringComparison.Ordinal) &&
                !ReferenceEquals(outgoingList.SelectedItem, this._vm.SelectedOutgoingRequest))
            {
                outgoingList.SelectedItem = this._vm.SelectedOutgoingRequest;
            }
        };

        // ── i18n refresh ────────────────────────────────────────────────────────
        refreshI18n = () =>
        {
            directionCategory.Title = this._i18n["Accounts.FilterCatStatus"];
            friendSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Personal.SearchRequestsPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            incomingDescTb.Text = this._i18n["Personal.IncomingDesc"];
            emptyIncomingTb.Text = this._i18n["Empty.IncomingRequests"];
            AppButton.SetText(acceptAllBtn, this._i18n["Personal.BtnAcceptAll"]);
            AppButton.SetText(syncIncomingBtn, this._i18n["Personal.BtnSyncIncoming"]);
            outgoingDescTb.Text = this._i18n["Personal.OutgoingDesc"];
            emptyOutgoingTb.Text = this._i18n["Empty.OutgoingRequests"];
            AppButton.SetText(cancelAllOutgoingBtn, this._i18n["Personal.BtnCancelAllOutgoing"]);
            AppButton.SetText(syncOutgoingBtn, this._i18n["Personal.BtnSyncOutgoing"]);
            RefreshFilteredRequests();
        };

        return pane;
    }

    private Grid CreateFriendRow(
        UserSummary user,
        Action? acceptCommand = null,
        Action? rejectCommand = null,
        Action? cancelCommand = null,
        string? acceptLabel = null,
        string? rejectLabel = null,
        string? cancelLabel = null)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        Border avatar = AvatarBadge.Create(this._theme, user.Name, 34, user.AvatarUri);
        Grid.SetColumn(avatar, 0);

        TextBlock nameTb = SectionHeader.CreateLabel(this._theme, user.Name, 13, FontWeight.SemiBold);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        nameTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(nameTb, 1);

        row.Children.Add(avatar);
        row.Children.Add(nameTb);

        int colIndex = 2;

        Button openProfileBtn = AppButton.CreateRowIcon(
            this._theme,
            VectorIcons.Browser,
            this._i18n["Personal.BtnOpenProfile"],
            () => this._vm.OpenFriendProfileInBrowser(user),
            AuditSeverity.Info);
        Grid.SetColumn(openProfileBtn, colIndex++);
        row.Children.Add(openProfileBtn);

        if (acceptCommand is not null)
        {
            Button acceptBtn = AppButton.CreateRowIcon(
                this._theme,
                VectorIcons.CheckCircle,
                acceptLabel ?? "Accept",
                () => acceptCommand(),
                AuditSeverity.Success);
            Grid.SetColumn(acceptBtn, colIndex++);
            row.Children.Add(acceptBtn);
        }

        if (rejectCommand is not null)
        {
            Button rejectBtn = AppButton.CreateRowIcon(
                this._theme,
                VectorIcons.Trash,
                rejectLabel ?? "Decline",
                () => rejectCommand(),
                AuditSeverity.Error);
            Grid.SetColumn(rejectBtn, colIndex++);
            row.Children.Add(rejectBtn);
        }

        if (cancelCommand is not null)
        {
            Button cancelBtn = AppButton.CreateRowIcon(
                this._theme,
                VectorIcons.StopCircle,
                cancelLabel ?? "Cancel",
                () => cancelCommand(),
                AuditSeverity.Warning);
            Grid.SetColumn(cancelBtn, colIndex++);
            row.Children.Add(cancelBtn);
        }

        return row;
    }
}
