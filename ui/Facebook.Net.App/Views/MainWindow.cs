// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Cloudflare-styled desktop Shell Window featuring:
/// - Top Header Bar with active account chip, quick Language toggle (`vi-VN`/`en-US`), and Theme toggle (`Dark`/`Light`).
/// - Left Navigation Sidebar with 5 dedicated screens (Accounts, Groups, Publisher, Sales Leads, Settings).
/// - Central Multi-Screen Workspace (zero raw UID/Group ID entry).
/// - Collapsible Bottom Audit Log Drawer / Taskbar backed by <c>facebook_audit.db</c>.
/// Split into partial files by screen area.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    private Control? _accountsScreen;
    private Control? _groupsScreen;
    private Control? _publisherScreen;
    private Control? _leadsScreen;
    private Control? _settingsScreen;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this._vm = viewModel;
        this._theme = viewModel.Context.Theme;
        this._i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.Title = this._i18n["App.Title"];
        this.Width = 1340;
        this.Height = 860;
        this.MinWidth = 1080;
        this.MinHeight = 720;

        this._i18n.LanguageChanged += (_, _) => this.Title = this._i18n["App.Title"];
        this.Content = this.BuildShellLayout();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        this._vm.Dispose();
        base.OnClosed(e);
    }

    private Grid BuildShellLayout()
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        void ApplyRootTheme() => root.Background = this._theme.Palette.AppCanvasBackground;
        ApplyRootTheme();
        this._theme.ThemeChanged += (_, _) => ApplyRootTheme();

        Border topBar = this.BuildTopBar();
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        Grid middleBody = new()
        {
            ColumnDefinitions = new ColumnDefinitions("240,*")
        };

        Border sidebar = this.BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        middleBody.Children.Add(sidebar);

        Border screenHost = this.BuildScreenHost();
        Grid.SetColumn(screenHost, 1);
        middleBody.Children.Add(screenHost);

        Grid.SetRow(middleBody, 1);
        root.Children.Add(middleBody);

        Border bottomTaskbar = this.BuildCollapsibleAuditTaskbar();
        Grid.SetRow(bottomTaskbar, 2);
        root.Children.Add(bottomTaskbar);

        return root;
    }

    private Border BuildTopBar()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(18, 10)
        };

        StackPanel titleStack = new() { Spacing = 2 };
        TextBlock appTitle = new()
        {
            Text = this._i18n["App.Title"],
            FontSize = 16,
            FontWeight = FontWeight.Bold
        };
        TextBlock appSub = new()
        {
            Text = this._i18n["App.Subtitle"],
            FontSize = 12
        };
        titleStack.Children.Add(appTitle);
        titleStack.Children.Add(appSub);

        StackPanel rightActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock activeAccLabel = new()
        {
            Text = this._i18n["TopBar.ActiveAccount"],
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        ComboBox activeAccCombo = new()
        {
            MinWidth = 210,
            PlaceholderText = this._i18n["TopBar.NoAccount"],
            ItemsSource = this._vm.Accounts,
            SelectedItem = this._vm.SelectedAccount,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Facebook.Net.Contracts.Models.AccountProfile>((acc, _) =>
            {
                if (acc is null)
                {
                    return new TextBlock { Text = this._i18n["TopBar.NoAccount"] };
                }

                return new TextBlock
                {
                    Text = $"{acc.DisplayName} ({this._i18n.FormatAccountStatus(acc.Status)})",
                    FontSize = 12,
                    FontWeight = FontWeight.Medium
                };
            })
        };

        activeAccCombo.SelectionChanged += (_, _) =>
        {
            if (activeAccCombo.SelectedItem is Facebook.Net.Contracts.Models.AccountProfile selected)
            {
                this._vm.SelectedAccount = selected;
            }
        };

        Button stopTaskBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Common.BtnStopTask"],
            this._vm.StopActiveTaskCommand,
            VectorIcons.StopCircle);
        stopTaskBtn.IsVisible = this._vm.IsBusy;

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                activeAccCombo.SelectedItem = this._vm.SelectedAccount;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.IsBusy), StringComparison.Ordinal))
            {
                stopTaskBtn.IsVisible = this._vm.IsBusy;
            }
        };

        Button langBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n.CurrentLanguage == AppLanguage.ViVn ? "VI / EN" : "EN / VI",
            this._vm.ToggleLanguageQuickCommand);

        Button themeBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._theme.CurrentMode == AppThemeMode.Dark ? this._i18n["Settings.ThemeDark"] : this._i18n["Settings.ThemeLight"],
            this._vm.ToggleThemeQuickCommand,
            VectorIcons.Sun);

        rightActions.Children.Add(stopTaskBtn);
        rightActions.Children.Add(activeAccLabel);
        rightActions.Children.Add(activeAccCombo);
        rightActions.Children.Add(langBtn);
        rightActions.Children.Add(themeBtn);

        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(rightActions, 1);
        grid.Children.Add(titleStack);
        grid.Children.Add(rightActions);

        Border bar = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };

        void ApplyColors()
        {
            CloudflarePalette p = this._theme.Palette;
            bar.Background = p.ElevatedSurface;
            bar.BorderBrush = p.SubtleBorder;
            appTitle.Foreground = p.TextPrimary;
            appSub.Foreground = p.TextSecondary;
            activeAccLabel.Foreground = p.TextSecondary;
            stopTaskBtn.Background = p.DangerText;
            CloudflareControls.SetButtonText(
                themeBtn,
                this._theme.CurrentMode == AppThemeMode.Dark ? this._i18n["Settings.ThemeDark"] : this._i18n["Settings.ThemeLight"]);
        }

        ApplyColors();
        this._theme.ThemeChanged += (_, _) => ApplyColors();
        this._i18n.LanguageChanged += (_, _) =>
        {
            appTitle.Text = this._i18n["App.Title"];
            appSub.Text = this._i18n["App.Subtitle"];
            activeAccLabel.Text = this._i18n["TopBar.ActiveAccount"];
            activeAccCombo.PlaceholderText = this._i18n["TopBar.NoAccount"];
            CloudflareControls.SetButtonText(stopTaskBtn, this._i18n["Common.BtnStopTask"]);
            CloudflareControls.SetButtonText(
                langBtn,
                this._i18n.CurrentLanguage == AppLanguage.ViVn ? "VI / EN" : "EN / VI");
            ApplyColors();
        };

        return bar;
    }

    private Border BuildSidebar()
    {
        StackPanel navStack = new()
        {
            Spacing = 6,
            Margin = new Thickness(12, 16)
        };

        Button navAccounts = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Nav.Accounts"],
            this._vm.NavigateAccountsCommand,
            VectorIcons.Accounts);

        Button navGroups = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Nav.Groups"],
            this._vm.NavigateGroupsCommand,
            VectorIcons.Groups);

        Button navPublisher = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Nav.Publisher"],
            this._vm.NavigatePublisherCommand,
            VectorIcons.Publish);

        Button navLeads = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Nav.SalesLeads"],
            this._vm.NavigateSalesLeadsCommand,
            VectorIcons.SalesComment);

        Button navSettings = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Nav.Settings"],
            this._vm.NavigateSettingsCommand,
            VectorIcons.Settings);

        navStack.Children.Add(navAccounts);
        navStack.Children.Add(navGroups);
        navStack.Children.Add(navPublisher);
        navStack.Children.Add(navLeads);
        navStack.Children.Add(navSettings);

        Border sidebar = new()
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navStack
        };

        void UpdateActiveHighlight()
        {
            CloudflarePalette p = this._theme.Palette;
            sidebar.Background = p.ElevatedSurface;
            sidebar.BorderBrush = p.SubtleBorder;

            HighlightNavButton(navAccounts, this._vm.CurrentSection == NavigationSection.Accounts, p);
            HighlightNavButton(navGroups, this._vm.CurrentSection == NavigationSection.Groups, p);
            HighlightNavButton(navPublisher, this._vm.CurrentSection == NavigationSection.Publisher, p);
            HighlightNavButton(navLeads, this._vm.CurrentSection == NavigationSection.SalesLeads, p);
            HighlightNavButton(navSettings, this._vm.CurrentSection == NavigationSection.Settings, p);
        }

        UpdateActiveHighlight();
        this._theme.ThemeChanged += (_, _) => UpdateActiveHighlight();
        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentSection), StringComparison.Ordinal))
            {
                UpdateActiveHighlight();
            }
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            CloudflareControls.SetButtonText(navAccounts, this._i18n["Nav.Accounts"]);
            CloudflareControls.SetButtonText(navGroups, this._i18n["Nav.Groups"]);
            CloudflareControls.SetButtonText(navPublisher, this._i18n["Nav.Publisher"]);
            CloudflareControls.SetButtonText(navLeads, this._i18n["Nav.SalesLeads"]);
            CloudflareControls.SetButtonText(navSettings, this._i18n["Nav.Settings"]);
        };

        return sidebar;
    }

    private static void HighlightNavButton(Button btn, bool isActive, CloudflarePalette p)
    {
        btn.Background = isActive ? p.BrandSubtleFill : p.SecondarySurface;
        btn.BorderBrush = isActive ? p.BrandPrimary : p.InteractiveBorder;
    }

    private Border BuildScreenHost()
    {
        this._accountsScreen = this.BuildAccountsScreen();
        this._groupsScreen = this.BuildGroupsScreen();
        this._publisherScreen = this.BuildPublisherScreen();
        this._leadsScreen = this.BuildSalesLeadsScreen();
        this._settingsScreen = this.BuildSettingsScreen();

        Border host = new()
        {
            Padding = new Thickness(20),
            Child = this._accountsScreen
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentSection), StringComparison.Ordinal))
            {
                host.Child = this._vm.CurrentSection switch
                {
                    NavigationSection.Accounts => this._accountsScreen,
                    NavigationSection.Groups => this._groupsScreen,
                    NavigationSection.Publisher => this._publisherScreen,
                    NavigationSection.SalesLeads => this._leadsScreen,
                    NavigationSection.Settings => this._settingsScreen,
                    _ => this._accountsScreen
                };
            }
        };

        return host;
    }
}
