// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Meta.App.Views.Components;
using Meta.App.Views.Pages;
using Meta.UI.Components.Controls;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.AiAssistant;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views;

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
        _vm = null!;
        _theme = null!;
        _i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _vm = viewModel;
        _theme = viewModel.Context.Theme;
        _i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.InitializeComponent();

        this.Title = _i18n["App.Title"];
        _i18n.LanguageChanged += (_, _) => this.Title = _i18n["App.Title"];

        Grid rootGrid = this.FindControl<Grid>("ShellRootGrid")!;
        Border sidebarContainer = this.FindControl<Border>("SidebarContainer")!;
        Border topBusyContainer = this.FindControl<Border>("TopBusyContainer")!;
        Border topContextContainer = this.FindControl<Border>("TopContextContainer")!;
        _pageNavigationHost = this.FindControl<TransitioningContentControl>("PageNavigationHost")!;
        Border footerContainer = this.FindControl<Border>("FooterContainer")!;
        Border aiAssistantContainer = this.FindControl<Border>("AiAssistantContainer")!;

        void ApplyRootTheme() => rootGrid.Background = _theme.Palette.AppCanvasBackground;
        ApplyRootTheme();
        _theme.ThemeChanged += (_, _) => ApplyRootTheme();

        sidebarContainer.Child = new ShellSidebar(_vm);
        topBusyContainer.Child = this.BuildTopBusyStrip();
        topContextContainer.Child = new TopAccountSwitcherBar(_vm);
        footerContainer.Child = this.BuildFooterRow();

        aiAssistantContainer.Child = new AiAssistantPanel(_vm);
        aiAssistantContainer.IsVisible = _vm.AiAssistant.IsPanelOpen;
        _vm.AiAssistant.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.IsPanelOpen), StringComparison.Ordinal))
            {
                aiAssistantContainer.IsVisible = _vm.AiAssistant.IsPanelOpen;
            }
        };

        // Subscribe to NavigationService to swap independent UserControl pages
        _vm.Navigation.Navigated += (_, section) => this.NavigateToPage(section);
        this.NavigateToPage(_vm.Navigation.CurrentSection);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.FlushSettingsToDisk();
            _vm.Dispose();
        }

        base.OnClosed(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void NavigateToPage(NavigationSection section)
    {
        if (!_pageCache.TryGetValue(section, out UserControl? page))
        {
            page = section switch
            {
                NavigationSection.Overview => new OverviewPage(_vm),
                NavigationSection.Accounts => new AccountsPage(_vm),
                NavigationSection.Groups => new GroupsPage(_vm),
                NavigationSection.Publisher => new PublisherPage(_vm),
                NavigationSection.SalesLeads => new SalesLeadsPage(_vm),
                NavigationSection.Settings => new SettingsPage(_vm),
                NavigationSection.AuditLogs => new AuditLogsPage(_vm),
                _ => new AccountsPage(_vm)
            };
            _pageCache[section] = page;
        }

        _pageNavigationHost.Content = page;
    }

    private ProgressBar BuildTopBusyStrip()
    {
        ProgressBar busyStrip = new()
        {
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 3,
            MinHeight = 3,
            CornerRadius = new CornerRadius(1.5),
            BorderThickness = new Thickness(0),
            IsVisible = _vm.IsBusy
        };

        void ApplyColors() => busyStrip.Foreground = _theme.Palette.BrandPrimary;
        ApplyColors();
        _theme.ThemeChanged += (_, _) => ApplyColors();

        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.IsBusy), StringComparison.Ordinal))
            {
                busyStrip.IsVisible = _vm.IsBusy;
            }
        };

        return busyStrip;
    }

    private Grid BuildFooterRow()
    {
        Grid footerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0)
        };

        TextBlock footerTicker = SectionHeader.CreateLabel(
            _theme,
            _vm.AuditDrawer.LatestTickerText,
            11,
            FontWeight.Normal,
            isSecondary: true);
        footerTicker.TextWrapping = TextWrapping.NoWrap;
        footerTicker.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock versionLabel = SectionHeader.CreateLabel(
            _theme,
            $"Facebook One Client  |  {_vm.CurrentAppVersion}",
            11,
            FontWeight.Normal,
            isSecondary: true);

        Grid.SetColumn(footerTicker, 0);
        Grid.SetColumn(versionLabel, 1);
        footerRow.Children.Add(footerTicker);
        footerRow.Children.Add(versionLabel);

        _vm.AuditDrawer.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(UI.ViewModels.Audit.AuditLogDrawerViewModel.LatestTickerText), StringComparison.Ordinal))
            {
                footerTicker.Text = _vm.AuditDrawer.LatestTickerText;
            }
        };

        return footerRow;
    }
}
