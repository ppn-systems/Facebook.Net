// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Minimalist desktop Shell Window featuring:
/// <list type="bullet">
///   <item><description>Fixed standard window geometry (<c>980 × 640</c>, <c>CanResize = false</c>).</description></item>
///   <item><description>Collapsible Left Navigation Sidebar with SVG icons loaded from <c>Assets/Icons/*.svg</c>.</description></item>
///   <item><description>Dedicated partial class per screen (<c>Overview</c>, <c>Accounts</c>, <c>Groups</c>, <c>Publisher</c>, <c>SalesLeads</c>, <c>Settings</c>, <c>Audit</c>).</description></item>
/// </list>
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    private Control? _overviewScreen;
    private Control? _accountsScreen;
    private Control? _groupsScreen;
    private Control? _publisherScreen;
    private Control? _leadsScreen;
    private Control? _settingsScreen;
    private Control? _auditLogsScreen;

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
        this.Width = 980;
        this.Height = 640;
        this.MinWidth = 980;
        this.MaxWidth = 980;
        this.MinHeight = 640;
        this.MaxHeight = 640;
        this.CanResize = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        this._i18n.LanguageChanged += (_, _) => this.Title = this._i18n["App.Title"];
        this.Content = this.BuildShellLayout();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        this._vm.FlushSettingsToDisk();
        this._vm.Dispose();
        base.OnClosed(e);
    }

    private async Task<string?> PickSingleFileAsync(string title)
    {
        if (this.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private Grid BuildShellLayout()
    {
        Grid root = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        void ApplyRootTheme() => root.Background = this._theme.Palette.AppCanvasBackground;
        ApplyRootTheme();
        this._theme.ThemeChanged += (_, _) => ApplyRootTheme();

        Border sidebar = this.BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        Border screenHost = this.BuildScreenHost();
        Grid.SetColumn(screenHost, 1);
        root.Children.Add(screenHost);

        return root;
    }

    private Border BuildSidebar()
    {
        Grid sidebarGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
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

        Button collapseBtn = UiControls.CreateIconButton(
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

        // 2. Navigation Items (Single-page sections)
        StackPanel navStack = new()
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top
        };

        List<(Button Btn, TextBlock Label, Control Icon, string Key, NavigationSection Section)> navEntries = new(6);

        void AddNavItem(string i18nKey, string svgFileName, NavigationSection section, System.Windows.Input.ICommand cmd)
        {
            Button btn = UiControls.CreateSidebarNavButton(
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

        // 3. Bottom-left Activity / Audit Log Icon Button
        Button bottomAuditBtn = UiControls.CreateSidebarNavButton(
            this._i18n["Nav.AuditLogs"],
            VectorIcons.AuditLog,
            this._vm.NavigateAuditLogsCommand,
            out TextBlock auditLabel,
            out Control auditIcon);
        bottomAuditBtn.CornerRadius = new CornerRadius(18);
        bottomAuditBtn.Padding = new Thickness(10, 7);
        bottomAuditBtn.HorizontalAlignment = HorizontalAlignment.Left;

        Grid.SetRow(bottomAuditBtn, 2);
        sidebarGrid.Children.Add(bottomAuditBtn);

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
                bool isActive = this._vm.CurrentSection == sec;

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

            bool isAuditActive = this._vm.CurrentSection == NavigationSection.AuditLogs;
            auditLabel.Text = this._i18n["Nav.AuditLogs"];
            auditLabel.IsVisible = !collapsed;
            if (bottomAuditBtn.Content is StackPanel bottomRow)
            {
                bottomRow.Spacing = collapsed ? 0 : 10;
            }

            auditLabel.Foreground = isAuditActive ? p.BrandPrimary : p.TextSecondary;
            VectorIcons.SetForeground(auditIcon, isAuditActive ? p.BrandPrimary : p.TextSecondary);
            bottomAuditBtn.Background = isAuditActive ? p.BrandSubtleFill : p.SecondarySurface;
            bottomAuditBtn.BorderBrush = isAuditActive ? p.BrandPrimary : p.SubtleBorder;
            bottomAuditBtn.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            AutomationProperties.SetName(bottomAuditBtn, this._i18n["Nav.AuditLogs"]);
            ToolTip.SetTip(bottomAuditBtn, $"{this._i18n["Nav.AuditLogs"]} ({this._vm.AuditDrawer.LatestTickerText})");
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
        this._vm.AuditDrawer.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Audit.AuditLogDrawerViewModel.LatestTickerText), StringComparison.Ordinal))
            {
                ToolTip.SetTip(bottomAuditBtn, $"{this._i18n["Nav.AuditLogs"]} ({this._vm.AuditDrawer.LatestTickerText})");
            }
        };

        return sidebar;
    }

    private Border BuildScreenHost()
    {
        this._overviewScreen = this.BuildOverviewScreen();
        this._accountsScreen = this.BuildAccountsScreen();
        this._groupsScreen = this.BuildGroupsScreen();
        this._publisherScreen = this.BuildPublisherScreen();
        this._leadsScreen = this.BuildSalesLeadsScreen();
        this._settingsScreen = this.BuildSettingsScreen();
        this._auditLogsScreen = this.BuildAuditLogsScreen();

        Grid workspaceGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        Grid topContextStrip = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 12)
        };

        ProgressBar busyStrip = new()
        {
            IsIndeterminate = true,
            Height = 3,
            MinHeight = 3,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = this._vm.IsBusy
        };

        Button stopTaskBtn = UiControls.CreateDangerButton(
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

        TextBlock modeLbl = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Overview.ModeLabel"],
            12,
            FontWeight.Medium,
            isSecondary: true);

        ComboBox activeAccCombo = new()
        {
            MinWidth = 200,
            PlaceholderText = this._i18n["TopBar.NoAccount"],
            ItemsSource = this._vm.Accounts,
            SelectedItem = this._vm.SelectedAccount,
            ItemTemplate = new FuncDataTemplate<AccountProfile>((acc, _) =>
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

        Grid.SetColumn(busyStrip, 0);
        Grid.SetColumn(stopTaskBtn, 1);
        Grid.SetColumn(modeRow, 2);
        topContextStrip.Children.Add(busyStrip);
        topContextStrip.Children.Add(stopTaskBtn);
        topContextStrip.Children.Add(modeRow);

        Grid.SetRow(topContextStrip, 0);
        workspaceGrid.Children.Add(topContextStrip);

        Control? ResolveCurrentScreen() =>
            this._vm.CurrentSection switch
            {
                NavigationSection.Overview => this._overviewScreen,
                NavigationSection.Accounts => this._accountsScreen,
                NavigationSection.Groups => this._groupsScreen,
                NavigationSection.Publisher => this._publisherScreen,
                NavigationSection.SalesLeads => this._leadsScreen,
                NavigationSection.Settings => this._settingsScreen,
                NavigationSection.AuditLogs => this._auditLogsScreen,
                _ => this._accountsScreen
            };

        Border activeScreenContainer = new()
        {
            Child = ResolveCurrentScreen()
        };
        Grid.SetRow(activeScreenContainer, 1);
        workspaceGrid.Children.Add(activeScreenContainer);

        Grid footerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0)
        };

        TextBlock footerTicker = UiControls.CreateThemedLabel(
            this._theme,
            this._vm.AuditDrawer.LatestTickerText,
            11,
            FontWeight.Normal,
            isSecondary: true);
        footerTicker.TextWrapping = TextWrapping.NoWrap;
        footerTicker.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock versionLabel = UiControls.CreateThemedLabel(
            this._theme,
            "Facebook.Net  |  v2026.10.0",
            11,
            FontWeight.Normal,
            isSecondary: true);

        Grid.SetColumn(footerTicker, 0);
        Grid.SetColumn(versionLabel, 1);
        footerRow.Children.Add(footerTicker);
        footerRow.Children.Add(versionLabel);

        Grid.SetRow(footerRow, 2);
        workspaceGrid.Children.Add(footerRow);

        Border host = new()
        {
            Padding = new Thickness(18, 12, 18, 10),
            Child = workspaceGrid
        };

        void ApplyHostColors() => busyStrip.Foreground = this._theme.Palette.BrandPrimary;

        ApplyHostColors();
        this._theme.ThemeChanged += (_, _) => ApplyHostColors();

        this._i18n.LanguageChanged += (_, _) =>
        {
            modeLbl.Text = this._i18n["Overview.ModeLabel"];
            activeAccCombo.PlaceholderText = this._i18n["TopBar.NoAccount"];
            UiControls.SetButtonText(stopTaskBtn, this._i18n["Common.BtnStopTask"]);
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentSection), StringComparison.Ordinal))
            {
                activeScreenContainer.Child = ResolveCurrentScreen();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
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

        this._vm.AuditDrawer.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Audit.AuditLogDrawerViewModel.LatestTickerText), StringComparison.Ordinal))
            {
                footerTicker.Text = this._vm.AuditDrawer.LatestTickerText;
            }
        };

        return host;
    }
}
