// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Facebook.Net.App.Views.Pages;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Root Shell Window (<c>MainWindow.axaml</c>) hosting the collapsible left navigation sidebar,
/// top active account context bar, and <see cref="TransitioningContentControl"/> navigating between
/// independent <see cref="UserControl"/> Pages (<see cref="OverviewPage"/>, <see cref="AccountsPage"/>,
/// <see cref="GroupsPage"/>, <see cref="PublisherPage"/>, <see cref="SalesLeadsPage"/>,
/// <see cref="SettingsPage"/>, and <see cref="AuditLogsPage"/>).
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;
    private readonly Dictionary<NavigationSection, UserControl> _pageCache = new();
    private readonly TransitioningContentControl _pageNavigationHost = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class for XAML designer/runtime loader.
    /// </summary>
    public MainWindow()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this._vm = viewModel;
        this._theme = viewModel.Context.Theme;
        this._i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.InitializeComponent();

        this.Title = this._i18n["App.Title"];
        this._i18n.LanguageChanged += (_, _) => this.Title = this._i18n["App.Title"];

        Grid rootGrid = this.FindControl<Grid>("ShellRootGrid")!;
        Border sidebarContainer = this.FindControl<Border>("SidebarContainer")!;
        Border topContextContainer = this.FindControl<Border>("TopContextContainer")!;
        this._pageNavigationHost = this.FindControl<TransitioningContentControl>("PageNavigationHost")!;
        Border footerContainer = this.FindControl<Border>("FooterContainer")!;

        void ApplyRootTheme() => rootGrid.Background = this._theme.Palette.AppCanvasBackground;
        ApplyRootTheme();
        this._theme.ThemeChanged += (_, _) => ApplyRootTheme();

        sidebarContainer.Child = this.BuildSidebar();
        topContextContainer.Child = this.BuildTopContextStrip();
        footerContainer.Child = this.BuildFooterRow();

        // Subscribe to NavigationService to swap independent UserControl pages
        this._vm.Navigation.Navigated += (_, section) => this.NavigateToPage(section);
        this.NavigateToPage(this._vm.Navigation.CurrentSection);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (this._vm is not null)
        {
            this._vm.FlushSettingsToDisk();
            this._vm.Dispose();
        }

        base.OnClosed(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void NavigateToPage(NavigationSection section)
    {
        if (!this._pageCache.TryGetValue(section, out UserControl? page))
        {
            page = section switch
            {
                NavigationSection.Overview => new OverviewPage(this._vm),
                NavigationSection.Accounts => new AccountsPage(this._vm),
                NavigationSection.Groups => new GroupsPage(this._vm),
                NavigationSection.Publisher => new PublisherPage(this._vm),
                NavigationSection.SalesLeads => new SalesLeadsPage(this._vm),
                NavigationSection.Settings => new SettingsPage(this._vm),
                NavigationSection.AuditLogs => new AuditLogsPage(this._vm),
                _ => new AccountsPage(this._vm)
            };
            this._pageCache[section] = page;
        }

        this._pageNavigationHost.Content = page;
    }

    private Border BuildSidebar()
    {
        Grid sidebarGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(10, 14)
        };

        // 1. Top Brand Logo + Collapse Menu Toggle Button
        Grid topHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4, 0, 4, 18),
            VerticalAlignment = VerticalAlignment.Center
        };

        Control brandIcon = VectorIcons.Create(VectorIcons.BrandMark, 22, margin: new Thickness(0));
        Border brandBadge = new()
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = brandIcon
        };

        Button collapseBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.SidebarToggle,
            this._i18n["Sidebar.Collapse"],
            this._vm.ToggleSidebarCommand);

        Grid.SetColumn(brandBadge, 0);
        Grid.SetColumn(collapseBtn, 2);
        topHeader.Children.Add(brandBadge);
        topHeader.Children.Add(collapseBtn);
        Grid.SetRow(topHeader, 0);
        sidebarGrid.Children.Add(topHeader);

        // 2. Navigation Items (6 primary pages; AuditLogs is accessed from the bottom of SettingsPage)
        StackPanel navStack = new()
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top
        };

        List<(Button Btn, TextBlock Label, Control Icon, string Key, NavigationSection Section)> navEntries = new(6);

        void AddNavItem(string i18nKey, string svgFileName, NavigationSection section, System.Windows.Input.ICommand cmd)
        {
            Button btn = AppButton.CreateSidebarNav(
                this._i18n[i18nKey],
                svgFileName,
                cmd,
                out TextBlock lbl,
                out Control icn);
            navEntries.Add((btn, lbl, icn, i18nKey, section));
            navStack.Children.Add(btn);
        }

        AddNavItem("Nav.Overview", VectorIcons.Home, NavigationSection.Overview, this._vm.NavigateOverviewCommand);
        AddNavItem("Nav.Accounts", VectorIcons.Accounts, NavigationSection.Accounts, this._vm.NavigateAccountsCommand);
        AddNavItem("Nav.Groups", VectorIcons.Groups, NavigationSection.Groups, this._vm.NavigateGroupsCommand);
        AddNavItem("Nav.Publisher", VectorIcons.Publish, NavigationSection.Publisher, this._vm.NavigatePublisherCommand);
        AddNavItem("Nav.SalesLeads", VectorIcons.SalesComment, NavigationSection.SalesLeads, this._vm.NavigateSalesLeadsCommand);
        AddNavItem("Nav.Settings", VectorIcons.Settings, NavigationSection.Settings, this._vm.NavigateSettingsCommand);

        Grid.SetRow(navStack, 1);
        sidebarGrid.Children.Add(navStack);

        Border sidebar = new()
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebarGrid
        };

        void UpdateSidebarLayoutAndColors()
        {
            ThemePalette p = this._theme.Palette;
            bool collapsed = this._vm.IsSidebarCollapsed;

            sidebar.Width = collapsed ? 60 : 196;
            sidebar.Background = p.ElevatedSurface;
            sidebar.BorderBrush = p.SubtleBorder;

            brandBadge.IsVisible = !collapsed;
            brandBadge.Background = p.BrandSubtleFill;
            VectorIcons.SetForeground(brandIcon, p.BrandPrimary);

            Grid.SetColumn(collapseBtn, collapsed ? 0 : 2);
            collapseBtn.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;

            string collapseTip = collapsed ? this._i18n["Sidebar.Expand"] : this._i18n["Sidebar.Collapse"];
            ToolTip.SetTip(collapseBtn, collapseTip);
            AutomationProperties.SetName(collapseBtn, collapseTip);

            for (int i = 0; i < navEntries.Count; i++)
            {
                (Button btn, TextBlock lbl, Control icn, string key, NavigationSection sec) = navEntries[i];
                bool isActive = this._vm.CurrentSection == sec ||
                                (sec == NavigationSection.Settings && this._vm.CurrentSection == NavigationSection.AuditLogs);

                lbl.Text = this._i18n[key];
                lbl.IsVisible = !collapsed;
                lbl.Foreground = isActive ? p.TextPrimary : p.TextSecondary;
                lbl.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium;

                VectorIcons.SetForeground(icn, isActive ? p.BrandPrimary : p.TextSecondary);

                if (btn.Content is StackPanel navRow)
                {
                    navRow.Spacing = collapsed ? 0 : 10;
                }

                btn.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                btn.Background = isActive ? p.SecondarySurface : Brushes.Transparent;
                btn.BorderBrush = isActive ? p.InteractiveBorder : Brushes.Transparent;
                AutomationProperties.SetName(btn, this._i18n[key]);
                ToolTip.SetTip(btn, this._i18n[key]);
            }
        }

        UpdateSidebarLayoutAndColors();
        this._theme.ThemeChanged += (_, _) => UpdateSidebarLayoutAndColors();
        this._i18n.LanguageChanged += (_, _) => UpdateSidebarLayoutAndColors();
        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentSection), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ShellViewModel.IsSidebarCollapsed), StringComparison.Ordinal))
            {
                UpdateSidebarLayoutAndColors();
            }
        };

        return sidebar;
    }

    private StackPanel BuildTopContextStrip()
    {
        StackPanel topContextStrip = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 1, 0, 0)
        };

        ProgressBar busyStrip = new()
        {
            IsIndeterminate = true,
            Width = 80,
            Height = 4,
            MinHeight = 4,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = this._vm.IsBusy
        };

        Button stopTaskBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Common.BtnStopTask"],
            this._vm.StopActiveTaskCommand,
            VectorIcons.StopCircle);
        stopTaskBtn.IsVisible = this._vm.IsBusy;
        stopTaskBtn.Padding = new Thickness(10, 5);

        StackPanel modeRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock modeLbl = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Overview.ModeLabel"],
            12,
            FontWeight.Medium,
            isSecondary: true);

        ComboBox activeAccCombo = FormField.CreateComboBox<AccountProfile>(
            this._theme,
            this._vm.Accounts,
            acc => SectionHeader.CreateLabel(
                this._theme,
                $"{acc.DisplayName} — {this._i18n.FormatAccountStatus(acc.Status)}",
                12,
                FontWeight.Medium),
            selectedItem: this._vm.SelectedAccount,
            placeholderText: this._i18n["TopBar.NoAccount"]);
        activeAccCombo.MinWidth = 190;
        activeAccCombo.Height = 32;
        AutomationProperties.SetName(activeAccCombo, this._i18n["TopBar.ActiveAccount"]);
        activeAccCombo.SelectionChanged += (_, _) =>
        {
            if (activeAccCombo.SelectedItem is AccountProfile selected &&
                !ReferenceEquals(this._vm.SelectedAccount, selected))
            {
                this._vm.SelectedAccount = selected;
            }
        };

        modeRow.Children.Add(modeLbl);
        modeRow.Children.Add(activeAccCombo);

        topContextStrip.Children.Add(busyStrip);
        topContextStrip.Children.Add(stopTaskBtn);
        topContextStrip.Children.Add(modeRow);

        void ApplyColors() => busyStrip.Foreground = this._theme.Palette.BrandPrimary;
        ApplyColors();
        this._theme.ThemeChanged += (_, _) => ApplyColors();

        this._i18n.LanguageChanged += (_, _) =>
        {
            modeLbl.Text = this._i18n["Overview.ModeLabel"];
            activeAccCombo.PlaceholderText = this._i18n["TopBar.NoAccount"];
            AppButton.SetText(stopTaskBtn, this._i18n["Common.BtnStopTask"]);
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                if (!ReferenceEquals(activeAccCombo.SelectedItem, this._vm.SelectedAccount))
                {
                    activeAccCombo.SelectedItem = this._vm.SelectedAccount;
                }
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.IsBusy), StringComparison.Ordinal))
            {
                busyStrip.IsVisible = this._vm.IsBusy;
                stopTaskBtn.IsVisible = this._vm.IsBusy;
            }
        };

        return topContextStrip;
    }

    private Grid BuildFooterRow()
    {
        Grid footerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0)
        };

        TextBlock footerTicker = SectionHeader.CreateLabel(
            this._theme,
            this._vm.AuditDrawer.LatestTickerText,
            11,
            FontWeight.Normal,
            isSecondary: true);
        footerTicker.TextWrapping = TextWrapping.NoWrap;
        footerTicker.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock versionLabel = SectionHeader.CreateLabel(
            this._theme,
            "Facebook.Net  |  v2026.10.0",
            11,
            FontWeight.Normal,
            isSecondary: true);

        Grid.SetColumn(footerTicker, 0);
        Grid.SetColumn(versionLabel, 1);
        footerRow.Children.Add(footerTicker);
        footerRow.Children.Add(versionLabel);

        this._vm.AuditDrawer.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Audit.AuditLogDrawerViewModel.LatestTickerText), StringComparison.Ordinal))
            {
                footerTicker.Text = this._vm.AuditDrawer.LatestTickerText;
            }
        };

        return footerRow;
    }
}
