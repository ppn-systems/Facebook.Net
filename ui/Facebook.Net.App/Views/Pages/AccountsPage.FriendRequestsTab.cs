// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

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
            RowDefinitions = new RowDefinitions("Auto,*,Auto,*"),
            RowSpacing = 10
        };

        // ── Row 0: Incoming section header + Sync button ────────────────────────
        Grid incomingHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid incomingHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.IncomingHeader"],
            this._i18n["Personal.IncomingDesc"],
            out TextBlock incomingTitleTb,
            out TextBlock incomingDescTb);

        Button syncIncomingBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Personal.BtnSyncIncoming"],
            this._vm.SyncIncomingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(incomingHeader, 0);
        Grid.SetColumn(syncIncomingBtn, 1);
        incomingHeaderRow.Children.Add(incomingHeader);
        incomingHeaderRow.Children.Add(syncIncomingBtn);
        Grid.SetRow(incomingHeaderRow, 0);
        pane.Children.Add(incomingHeaderRow);

        // ── Row 1: Incoming requests list ───────────────────────────────────────
        ListBox incomingList = FormField.CreateListBox<UserSummary>(
            this._theme,
            this._vm.IncomingFriendRequests,
            user => this.CreateFriendRow(
                user,
                acceptCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.ConfirmFriendRequestCommand.ExecuteAsync(null); },
                rejectCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.DeleteFriendRequestCommand.ExecuteAsync(null); },
                acceptLabel: this._i18n["Personal.BtnAccept"],
                rejectLabel: this._i18n["Personal.BtnReject"]),
            selectedItem: this._vm.SelectedIncomingRequest,
            isVisible: this._vm.IncomingFriendRequests.Count > 0);

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
        emptyIncoming.IsVisible = this._vm.IncomingFriendRequests.Count == 0;

        this._vm.IncomingFriendRequests.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.IncomingFriendRequests.Count == 0;
            emptyIncoming.IsVisible = isEmpty;
            incomingList.IsVisible = !isEmpty;
        };

        Grid incomingHost = EmptyStateBanner.CreateHost(incomingList, emptyIncoming);
        Grid.SetRow(incomingHost, 1);
        pane.Children.Add(incomingHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedIncomingRequest), StringComparison.Ordinal) &&
                !ReferenceEquals(incomingList.SelectedItem, this._vm.SelectedIncomingRequest))
            {
                incomingList.SelectedItem = this._vm.SelectedIncomingRequest;
            }
        };

        // ── Row 2: Outgoing section header + Sync button ────────────────────────
        Grid outgoingHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Grid outgoingHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.OutgoingHeader"],
            this._i18n["Personal.OutgoingDesc"],
            out TextBlock outgoingTitleTb,
            out TextBlock outgoingDescTb);

        Button syncOutgoingBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Personal.BtnSyncOutgoing"],
            this._vm.SyncOutgoingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(outgoingHeader, 0);
        Grid.SetColumn(syncOutgoingBtn, 1);
        outgoingHeaderRow.Children.Add(outgoingHeader);
        outgoingHeaderRow.Children.Add(syncOutgoingBtn);
        Grid.SetRow(outgoingHeaderRow, 2);
        pane.Children.Add(outgoingHeaderRow);

        // ── Row 3: Outgoing requests list ───────────────────────────────────────
        ListBox outgoingList = FormField.CreateListBox<UserSummary>(
            this._theme,
            this._vm.OutgoingFriendRequests,
            user => this.CreateFriendRow(
                user,
                cancelCommand: () => { this._vm.SelectedOutgoingRequest = user; _ = this._vm.CancelFriendRequestCommand.ExecuteAsync(null); },
                cancelLabel: this._i18n["Personal.BtnCancel"]),
            selectedItem: this._vm.SelectedOutgoingRequest,
            isVisible: this._vm.OutgoingFriendRequests.Count > 0);

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
        emptyOutgoing.IsVisible = this._vm.OutgoingFriendRequests.Count == 0;

        this._vm.OutgoingFriendRequests.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.OutgoingFriendRequests.Count == 0;
            emptyOutgoing.IsVisible = isEmpty;
            outgoingList.IsVisible = !isEmpty;
        };

        Grid outgoingHost = EmptyStateBanner.CreateHost(outgoingList, emptyOutgoing);
        Grid.SetRow(outgoingHost, 3);
        pane.Children.Add(outgoingHost);

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
            incomingTitleTb.Text = this._i18n["Personal.IncomingHeader"];
            incomingDescTb.Text = this._i18n["Personal.IncomingDesc"];
            emptyIncomingTb.Text = this._i18n["Empty.IncomingRequests"];
            AppButton.SetText(syncIncomingBtn, this._i18n["Personal.BtnSyncIncoming"]);
            outgoingTitleTb.Text = this._i18n["Personal.OutgoingHeader"];
            outgoingDescTb.Text = this._i18n["Personal.OutgoingDesc"];
            emptyOutgoingTb.Text = this._i18n["Empty.OutgoingRequests"];
            AppButton.SetText(syncOutgoingBtn, this._i18n["Personal.BtnSyncOutgoing"]);
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
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
