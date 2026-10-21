// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Personal Profile, Friend Requests, and Friend Suggestions Page (<c>PersonalPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Profile Info — display name, UID, status, and session state of the selected account,
/// 2) Incoming Friend Requests — accept or decline with one-click row actions,
/// 3) Outgoing Friend Requests — cancel pending sent requests,
/// 4) People You May Know — friend suggestions with Add Friend action.
/// </summary>
public sealed partial class PersonalPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonalPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public PersonalPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonalPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public PersonalPage(ShellViewModel viewModel)
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
        Control tab1View = this.BuildProfileTab(out Action refreshTab1);
        Control tab2View = this.BuildIncomingTab(out Action refreshTab2);
        Control tab3View = this.BuildOutgoingTab(out Action refreshTab3);
        Control tab4View = this.BuildSuggestionsTab(out Action refreshTab4);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Personal.TabProfile"], VectorIcons.Accounts, tab1View),
                new SubTabItem(this._i18n["Personal.TabIncoming"], VectorIcons.ThumbUp, tab2View),
                new SubTabItem(this._i18n["Personal.TabOutgoing"], VectorIcons.Sync, tab3View),
                new SubTabItem(this._i18n["Personal.TabSuggestions"], VectorIcons.Groups, tab4View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Personal.TabProfile"],
                this._i18n["Personal.TabIncoming"],
                this._i18n["Personal.TabOutgoing"],
                this._i18n["Personal.TabSuggestions"]
            ]);
            refreshTab1();
            refreshTab2();
            refreshTab3();
            refreshTab4();
        };

        return subTabBar;
    }

    private Control BuildProfileTab(out Action refreshI18n)
    {
        Grid profilePane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 14
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.Header"],
            this._i18n["Personal.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Grid.SetRow(header, 0);
        profilePane.Children.Add(header);

        StackPanel profileContent = new()
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Top
        };

        // Avatar + Name row
        StackPanel avatarRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border avatarBadge = AvatarBadge.Create(
            this._theme,
            this._vm.SelectedAccount?.DisplayName ?? "?",
            48,
            this._vm.SelectedAccount?.AvatarUrl);

        TextBlock displayNameTb = SectionHeader.CreateLabel(
            this._theme,
            this._vm.SelectedAccount?.DisplayName ?? this._i18n["Personal.NoAccountSelected"],
            18,
            FontWeight.Bold);
        displayNameTb.VerticalAlignment = VerticalAlignment.Center;

        avatarRow.Children.Add(avatarBadge);
        avatarRow.Children.Add(displayNameTb);

        // Info grid
        Grid infoGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 10
        };

        TextBlock uidLabel = SectionHeader.CreateLabel(this._theme, this._i18n["Personal.ProfileUid"], 13, FontWeight.SemiBold);
        TextBlock uidValue = SectionHeader.CreateLabel(this._theme, this._vm.SelectedAccount?.Uid ?? "—", 13, FontWeight.Normal, isSecondary: true);
        uidValue.IsVisible = this._vm.SelectedAccount is not null;

        TextBlock statusLabel = SectionHeader.CreateLabel(this._theme, this._i18n["Personal.ProfileStatus"], 13, FontWeight.SemiBold);
        Border statusPill = this._vm.SelectedAccount is not null
            ? StatusPill.Create(
                this._theme,
                this._i18n.FormatAccountStatus(this._vm.SelectedAccount.Status),
                this._vm.SelectedAccount.Status == AccountStatus.Active ? AuditSeverity.Success : AuditSeverity.Warning)
            : StatusPill.Create(this._theme, "—", AuditSeverity.Info);

        TextBlock sessionLabel = SectionHeader.CreateLabel(this._theme, this._i18n["Personal.ProfileSession"], 13, FontWeight.SemiBold);
        bool hasSession = !string.IsNullOrWhiteSpace(this._vm.SelectedAccount?.CookieHeader);
        Border sessionPill = StatusPill.Create(
            this._theme,
            hasSession ? this._i18n["Personal.SessionActive"] : this._i18n["Personal.SessionExpired"],
            hasSession ? AuditSeverity.Success : AuditSeverity.Error);

        Grid.SetColumn(uidLabel, 0);
        Grid.SetRow(uidLabel, 0);
        Grid.SetColumn(uidValue, 1);
        Grid.SetRow(uidValue, 0);
        Grid.SetColumn(statusLabel, 0);
        Grid.SetRow(statusLabel, 1);
        Grid.SetColumn(statusPill, 1);
        Grid.SetRow(statusPill, 1);
        Grid.SetColumn(sessionLabel, 0);
        Grid.SetRow(sessionLabel, 2);
        Grid.SetColumn(sessionPill, 1);
        Grid.SetRow(sessionPill, 2);

        infoGrid.Children.Add(uidLabel);
        infoGrid.Children.Add(uidValue);
        infoGrid.Children.Add(statusLabel);
        infoGrid.Children.Add(statusPill);
        infoGrid.Children.Add(sessionLabel);
        infoGrid.Children.Add(sessionPill);

        // No-account banner
        Border noAccountBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Personal.NoAccountSelected"],
            VectorIcons.Accounts,
            out TextBlock noAccountTb);
        noAccountBanner.IsVisible = this._vm.SelectedAccount is null;

        Border profileCard = SurfaceCard.Create(this._theme, new StackPanel
        {
            Spacing = 14,
            Children = { avatarRow, infoGrid }
        });
        profileCard.IsVisible = this._vm.SelectedAccount is not null;

        StackPanel cardHost = new()
        {
            Spacing = 10,
            Children = { profileCard, noAccountBanner }
        };

        Grid.SetRow(cardHost, 1);
        profilePane.Children.Add(cardHost);

        void RefreshProfileContent()
        {
            AccountProfile? acc = this._vm.SelectedAccount;
            bool hasAcc = acc is not null;
            profileCard.IsVisible = hasAcc;
            noAccountBanner.IsVisible = !hasAcc;

            displayNameTb.Text = acc?.DisplayName ?? this._i18n["Personal.NoAccountSelected"];
            uidValue.Text = acc?.Uid ?? "—";
            uidValue.IsVisible = hasAcc;
        }

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshProfileContent();
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Personal.Header"];
            descTb.Text = this._i18n["Personal.Desc"];
            noAccountTb.Text = this._i18n["Personal.NoAccountSelected"];
            uidLabel.Text = this._i18n["Personal.ProfileUid"];
            statusLabel.Text = this._i18n["Personal.ProfileStatus"];
            sessionLabel.Text = this._i18n["Personal.ProfileSession"];
            RefreshProfileContent();
        };

        return profilePane;
    }

    private Control BuildIncomingTab(out Action refreshI18n)
    {
        Grid pane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.IncomingHeader"],
            this._i18n["Personal.IncomingDesc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button syncBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Personal.BtnSyncIncoming"],
            this._vm.SyncIncomingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncBtn, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncBtn);
        Grid.SetRow(topHeaderRow, 0);
        pane.Children.Add(topHeaderRow);

        ListBox listBox = FormField.CreateListBox<UserSummary>(
            this._theme,
            this._vm.IncomingFriendRequests,
            user => this.CreateUserRow(
                user,
                acceptCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.ConfirmFriendRequestCommand.ExecuteAsync(null); },
                rejectCommand: () => { this._vm.SelectedIncomingRequest = user; _ = this._vm.DeleteFriendRequestCommand.ExecuteAsync(null); },
                acceptLabel: this._i18n["Personal.BtnAccept"],
                rejectLabel: this._i18n["Personal.BtnReject"]),
            selectedItem: this._vm.SelectedIncomingRequest,
            isVisible: this._vm.IncomingFriendRequests.Count > 0);

        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is UserSummary u)
            {
                this._vm.SelectedIncomingRequest = u;
            }
        };

        Border emptyBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.IncomingRequests"],
            VectorIcons.ThumbUp,
            out TextBlock emptyTb);
        emptyBanner.IsVisible = this._vm.IncomingFriendRequests.Count == 0;

        this._vm.IncomingFriendRequests.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.IncomingFriendRequests.Count == 0;
            emptyBanner.IsVisible = isEmpty;
            listBox.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(listBox, emptyBanner);
        Grid.SetRow(listHost, 1);
        pane.Children.Add(listHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedIncomingRequest), StringComparison.Ordinal) &&
                !ReferenceEquals(listBox.SelectedItem, this._vm.SelectedIncomingRequest))
            {
                listBox.SelectedItem = this._vm.SelectedIncomingRequest;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Personal.IncomingHeader"];
            descTb.Text = this._i18n["Personal.IncomingDesc"];
            emptyTb.Text = this._i18n["Empty.IncomingRequests"];
            AppButton.SetText(syncBtn, this._i18n["Personal.BtnSyncIncoming"]);
        };

        return pane;
    }

    private Control BuildOutgoingTab(out Action refreshI18n)
    {
        Grid pane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.OutgoingHeader"],
            this._i18n["Personal.OutgoingDesc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button syncBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Personal.BtnSyncOutgoing"],
            this._vm.SyncOutgoingFriendRequestsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncBtn, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncBtn);
        Grid.SetRow(topHeaderRow, 0);
        pane.Children.Add(topHeaderRow);

        ListBox listBox = FormField.CreateListBox<UserSummary>(
            this._theme,
            this._vm.OutgoingFriendRequests,
            user => this.CreateUserRow(
                user,
                cancelCommand: () => { this._vm.SelectedOutgoingRequest = user; _ = this._vm.CancelFriendRequestCommand.ExecuteAsync(null); },
                cancelLabel: this._i18n["Personal.BtnCancel"]),
            selectedItem: this._vm.SelectedOutgoingRequest,
            isVisible: this._vm.OutgoingFriendRequests.Count > 0);

        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is UserSummary u)
            {
                this._vm.SelectedOutgoingRequest = u;
            }
        };

        Border emptyBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.OutgoingRequests"],
            VectorIcons.Sync,
            out TextBlock emptyTb);
        emptyBanner.IsVisible = this._vm.OutgoingFriendRequests.Count == 0;

        this._vm.OutgoingFriendRequests.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.OutgoingFriendRequests.Count == 0;
            emptyBanner.IsVisible = isEmpty;
            listBox.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(listBox, emptyBanner);
        Grid.SetRow(listHost, 1);
        pane.Children.Add(listHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedOutgoingRequest), StringComparison.Ordinal) &&
                !ReferenceEquals(listBox.SelectedItem, this._vm.SelectedOutgoingRequest))
            {
                listBox.SelectedItem = this._vm.SelectedOutgoingRequest;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Personal.OutgoingHeader"];
            descTb.Text = this._i18n["Personal.OutgoingDesc"];
            emptyTb.Text = this._i18n["Empty.OutgoingRequests"];
            AppButton.SetText(syncBtn, this._i18n["Personal.BtnSyncOutgoing"]);
        };

        return pane;
    }

    private Control BuildSuggestionsTab(out Action refreshI18n)
    {
        Grid pane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Personal.SuggestionsHeader"],
            this._i18n["Personal.SuggestionsDesc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button syncBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Personal.BtnSyncSuggestions"],
            this._vm.SyncFriendSuggestionsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncBtn, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncBtn);
        Grid.SetRow(topHeaderRow, 0);
        pane.Children.Add(topHeaderRow);

        ListBox listBox = FormField.CreateListBox<UserSummary>(
            this._theme,
            this._vm.FriendSuggestions,
            user => this.CreateUserRow(
                user,
                addFriendCommand: () => { this._vm.SelectedSuggestion = user; _ = this._vm.SendFriendRequestCommand.ExecuteAsync(null); },
                addFriendLabel: this._i18n["Personal.BtnAddFriend"]),
            selectedItem: this._vm.SelectedSuggestion,
            isVisible: this._vm.FriendSuggestions.Count > 0);

        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is UserSummary u)
            {
                this._vm.SelectedSuggestion = u;
            }
        };

        Border emptyBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.FriendSuggestions"],
            VectorIcons.Groups,
            out TextBlock emptyTb);
        emptyBanner.IsVisible = this._vm.FriendSuggestions.Count == 0;

        this._vm.FriendSuggestions.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.FriendSuggestions.Count == 0;
            emptyBanner.IsVisible = isEmpty;
            listBox.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(listBox, emptyBanner);
        Grid.SetRow(listHost, 1);
        pane.Children.Add(listHost);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedSuggestion), StringComparison.Ordinal) &&
                !ReferenceEquals(listBox.SelectedItem, this._vm.SelectedSuggestion))
            {
                listBox.SelectedItem = this._vm.SelectedSuggestion;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Personal.SuggestionsHeader"];
            descTb.Text = this._i18n["Personal.SuggestionsDesc"];
            emptyTb.Text = this._i18n["Empty.FriendSuggestions"];
            AppButton.SetText(syncBtn, this._i18n["Personal.BtnSyncSuggestions"]);
        };

        return pane;
    }

    private Grid CreateUserRow(
        UserSummary user,
        Action? acceptCommand = null,
        Action? rejectCommand = null,
        Action? cancelCommand = null,
        Action? addFriendCommand = null,
        string? acceptLabel = null,
        string? rejectLabel = null,
        string? cancelLabel = null,
        string? addFriendLabel = null)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 6)
        };

        Border avatar = AvatarBadge.Create(this._theme, user.Name, 36, user.AvatarUri);

        TextBlock nameTb = SectionHeader.CreateLabel(
            this._theme,
            user.Name,
            13,
            FontWeight.SemiBold);
        nameTb.VerticalAlignment = VerticalAlignment.Center;
        nameTb.MaxLines = 1;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetColumn(avatar, 0);
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
                acceptCommand,
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
                rejectCommand,
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
                cancelCommand,
                AuditSeverity.Warning);
            Grid.SetColumn(cancelBtn, colIndex++);
            row.Children.Add(cancelBtn);
        }

        if (addFriendCommand is not null)
        {
            Button addBtn = AppButton.CreateRowIcon(
                this._theme,
                VectorIcons.Plus,
                addFriendLabel ?? "Add Friend",
                addFriendCommand,
                AuditSeverity.Success);
            Grid.SetColumn(addBtn, colIndex++);
            row.Children.Add(addBtn);
        }

        return row;
    }
}
