// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Minimalist desktop Shell Window featuring:
/// - Collapsible Left Navigation Sidebar (expanded `200px` with labels or collapsed `60px` icon rail) with top collapse button and bottom Activity Log icon button.
/// - Clean, focused Single-Page Workspace per section with clickable SVG Info (`i`) flyouts instead of verbose inline descriptions.
/// - Automatic settings persistence to <c>appsettings.json</c>.
/// Split into partial files by screen area.
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
        this.Width = 1060;
        this.Height = 700;
        this.MinWidth = 860;
        this.MinHeight = 580;

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

        // 1. Top Brand Logo + Collapse Menu Toggle Button (`[|]`)
        Grid topHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4, 0, 4, 18),
            VerticalAlignment = VerticalAlignment.Center
        };

        PathIcon brandIcon = VectorIcons.Create(VectorIcons.BrandMark, 22, margin: new Thickness(0));
        Border brandBadge = new()
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = brandIcon
        };

        Button collapseBtn = new()
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Command = this._vm.ToggleSidebarCommand,
            Content = VectorIcons.Create(VectorIcons.SidebarToggle, 16, margin: new Thickness(0)),
            Focusable = true
        };

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

        List<(Button Btn, TextBlock Label, PathIcon Icon, string Key, NavigationSection Section)> navEntries = new(6);

        Button CreateNavItem(string i18nKey, string svgPath, NavigationSection section, System.Windows.Input.ICommand cmd)
        {
            PathIcon icon = VectorIcons.Create(svgPath, 17, margin: new Thickness(0));
            TextBlock label = new()
            {
                Text = this._i18n[i18nKey],
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(icon);
            row.Children.Add(label);

            Button btn = new()
            {
                Content = row,
                Command = cmd,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 9),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(1),
                Focusable = true
            };
            AutomationProperties.SetName(btn, this._i18n[i18nKey]);
            ToolTip.SetTip(btn, this._i18n[i18nKey]);

            navEntries.Add((btn, label, icon, i18nKey, section));
            return btn;
        }

        navStack.Children.Add(CreateNavItem("Nav.Overview", VectorIcons.Home, NavigationSection.Overview, this._vm.NavigateOverviewCommand));
        navStack.Children.Add(CreateNavItem("Nav.Accounts", VectorIcons.Accounts, NavigationSection.Accounts, this._vm.NavigateAccountsCommand));
        navStack.Children.Add(CreateNavItem("Nav.Groups", VectorIcons.Groups, NavigationSection.Groups, this._vm.NavigateGroupsCommand));
        navStack.Children.Add(CreateNavItem("Nav.Publisher", VectorIcons.Publish, NavigationSection.Publisher, this._vm.NavigatePublisherCommand));
        navStack.Children.Add(CreateNavItem("Nav.SalesLeads", VectorIcons.SalesComment, NavigationSection.SalesLeads, this._vm.NavigateSalesLeadsCommand));
        navStack.Children.Add(CreateNavItem("Nav.Settings", VectorIcons.Settings, NavigationSection.Settings, this._vm.NavigateSettingsCommand));

        Grid.SetRow(navStack, 1);
        sidebarGrid.Children.Add(navStack);

        // 3. Bottom-left Activity / Audit Log Icon Button (matching the uploaded One Client screenshot)
        PathIcon auditIcon = VectorIcons.Create(VectorIcons.SalesComment, 16, margin: new Thickness(0));
        TextBlock auditLabel = new()
        {
            Text = this._i18n["Nav.AuditLogs"],
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel bottomRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottomRow.Children.Add(auditIcon);
        bottomRow.Children.Add(auditLabel);

        Button bottomAuditBtn = new()
        {
            Content = bottomRow,
            Command = this._vm.NavigateAuditLogsCommand,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(10, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            Focusable = true
        };
        AutomationProperties.SetName(bottomAuditBtn, this._i18n["Nav.AuditLogs"]);
        ToolTip.SetTip(bottomAuditBtn, this._i18n["Nav.AuditLogs"]);

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

            sidebar.Width = collapsed ? 64 : 204;
            sidebar.Background = p.ElevatedSurface;
            sidebar.BorderBrush = p.SubtleBorder;

            brandBadge.IsVisible = !collapsed;
            brandBadge.Background = p.BrandSubtleFill;
            brandIcon.Foreground = p.BrandPrimary;

            Grid.SetColumn(collapseBtn, collapsed ? 0 : 2);
            collapseBtn.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            collapseBtn.Background = p.SecondarySurface;
            collapseBtn.BorderBrush = p.SubtleBorder;
            if (collapseBtn.Content is PathIcon toggleIcon)
            {
                toggleIcon.Foreground = p.TextSecondary;
            }

            string collapseTip = collapsed ? this._i18n["Sidebar.Expand"] : this._i18n["Sidebar.Collapse"];
            ToolTip.SetTip(collapseBtn, collapseTip);
            AutomationProperties.SetName(collapseBtn, collapseTip);

            for (int i = 0; i < navEntries.Count; i++)
            {
                (Button btn, TextBlock lbl, PathIcon icn, string key, NavigationSection sec) = navEntries[i];
                bool isActive = this._vm.CurrentSection == sec;

                lbl.Text = this._i18n[key];
                lbl.IsVisible = !collapsed;
                lbl.Foreground = isActive ? p.TextPrimary : p.TextSecondary;
                lbl.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium;

                icn.Foreground = isActive ? p.BrandPrimary : p.TextSecondary;

                btn.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                btn.Background = isActive ? p.SecondarySurface : Brushes.Transparent;
                btn.BorderBrush = isActive ? p.InteractiveBorder : Brushes.Transparent;
                AutomationProperties.SetName(btn, this._i18n[key]);
                ToolTip.SetTip(btn, this._i18n[key]);
            }

            bool isAuditActive = this._vm.CurrentSection == NavigationSection.AuditLogs;
            auditLabel.Text = this._i18n["Nav.AuditLogs"];
            auditLabel.IsVisible = !collapsed;
            auditLabel.Foreground = isAuditActive ? p.BrandPrimary : p.TextSecondary;
            auditIcon.Foreground = isAuditActive ? p.BrandPrimary : p.TextSecondary;
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

        // Top subtle busy progress strip + active account selector bar
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

        Button stopTaskBtn = UiControls.CreatePrimaryButton(
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

        // Subtle footer with live status ticker & version (matching the bottom of the One Client screenshot)
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
            Padding = new Thickness(28, 18, 28, 14),
            Child = workspaceGrid
        };

        void ApplyHostColors()
        {
            ThemePalette p = this._theme.Palette;
            busyStrip.Foreground = p.BrandPrimary;
            stopTaskBtn.Background = p.DangerText;
        }

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

    private Control BuildOverviewScreen()
    {
        StackPanel centerHero = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460
        };

        Grid headerRow = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Nav.Overview"],
            this._i18n["Overview.InfoTip"],
            out TextBlock titleTb,
            out TextBlock infoTb);
        headerRow.HorizontalAlignment = HorizontalAlignment.Center;

        PathIcon heroIcon = VectorIcons.Create(VectorIcons.CheckCircle, 46, margin: new Thickness(0));
        Border heroCircle = new()
        {
            Width = 88,
            Height = 88,
            CornerRadius = new CornerRadius(44),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = heroIcon
        };

        TextBlock statusHeading = new()
        {
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        TextBlock statusSub = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Overview.SubtextDisconnected"],
            13,
            FontWeight.Normal,
            isSecondary: true);
        statusSub.TextAlignment = TextAlignment.Center;
        statusSub.HorizontalAlignment = HorizontalAlignment.Center;

        Grid actionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };

        Button openBrowserBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Accounts.BtnOpenBrowserLogin"],
            this._vm.OpenBrowserAndSyncCommand,
            VectorIcons.Browser);

        Button liveCheckBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Accounts.BtnCheckHealth"],
            this._vm.CheckAccountHealthCommand,
            VectorIcons.CheckCircle);

        Grid.SetColumn(openBrowserBtn, 0);
        Grid.SetColumn(liveCheckBtn, 1);
        actionRow.Children.Add(openBrowserBtn);
        actionRow.Children.Add(liveCheckBtn);

        // Compact 4-metric summary pills row inside the Overview card
        Grid statsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Border cardAcc = UiControls.CreateKpiStatCard(
            this._theme,
            this._i18n["Nav.Accounts"],
            "0",
            VectorIcons.Accounts,
            out TextBlock accTitleTb,
            out TextBlock accValTb);

        Border cardGrp = UiControls.CreateKpiStatCard(
            this._theme,
            this._i18n["Nav.Groups"],
            "0",
            VectorIcons.Groups,
            out TextBlock grpTitleTb,
            out TextBlock grpValTb);

        Border cardLeads = UiControls.CreateKpiStatCard(
            this._theme,
            this._i18n["Nav.SalesLeads"],
            "0",
            VectorIcons.Search,
            out TextBlock leadsTitleTb,
            out TextBlock leadsValTb);

        Border cardOk = UiControls.CreateKpiStatCard(
            this._theme,
            this._i18n["Nav.AuditLogs"],
            "0",
            VectorIcons.CheckCircle,
            out TextBlock okTitleTb,
            out TextBlock okValTb);

        Grid.SetColumn(cardAcc, 0);
        Grid.SetColumn(cardGrp, 1);
        Grid.SetColumn(cardLeads, 2);
        Grid.SetColumn(cardOk, 3);
        statsRow.Children.Add(cardAcc);
        statsRow.Children.Add(cardGrp);
        statsRow.Children.Add(cardLeads);
        statsRow.Children.Add(cardOk);

        centerHero.Children.Add(headerRow);
        centerHero.Children.Add(heroCircle);
        centerHero.Children.Add(statusHeading);
        centerHero.Children.Add(statusSub);
        centerHero.Children.Add(actionRow);

        Border heroCard = UiControls.CreateCard(this._theme, centerHero, new Thickness(32, 28));

        StackPanel pageStack = new()
        {
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        pageStack.Children.Add(heroCard);
        pageStack.Children.Add(statsRow);

        void RefreshOverviewState()
        {
            ThemePalette p = this._theme.Palette;
            AccountProfile? acc = this._vm.SelectedAccount;
            bool isConnected = acc is not null && acc.Status == AccountStatus.Active;

            if (this._vm.IsBusy)
            {
                statusHeading.Text = this._i18n["Overview.StatusRunning"];
                statusHeading.Foreground = p.BrandPrimary;
                heroCircle.Background = p.BrandSubtleFill;
                heroCircle.BorderBrush = p.BrandPrimary;
                heroIcon.Foreground = p.BrandPrimary;
                statusSub.Text = this._vm.AuditDrawer.LatestTickerText;
            }
            else if (isConnected && acc is not null)
            {
                statusHeading.Text = this._i18n["Overview.StatusConnected"];
                statusHeading.Foreground = p.SuccessText;
                heroCircle.Background = p.SuccessFill;
                heroCircle.BorderBrush = p.SuccessBorder;
                heroIcon.Foreground = p.SuccessText;
                statusSub.Text = $"{acc.DisplayName} — {this._i18n["Overview.SubtextConnected"]}";
            }
            else
            {
                statusHeading.Text = acc is not null
                    ? this._i18n.FormatAccountStatus(acc.Status)
                    : this._i18n["Overview.StatusDisconnected"];
                statusHeading.Foreground = p.TextPrimary;
                heroCircle.Background = p.SecondarySurface;
                heroCircle.BorderBrush = p.SubtleBorder;
                heroIcon.Foreground = p.BrandPrimary;
                statusSub.Text = this._i18n["Overview.SubtextDisconnected"];
            }

            accValTb.Text = this._vm.Accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            grpValTb.Text = this._vm.TargetGroups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            leadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            okValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        RefreshOverviewState();
        this._theme.ThemeChanged += (_, _) => RefreshOverviewState();
        this._vm.PropertyChanged += (_, _) => RefreshOverviewState();
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.TargetGroups.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => RefreshOverviewState();
        this._vm.AuditDrawer.PropertyChanged += (_, _) => RefreshOverviewState();

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Nav.Overview"];
            infoTb.Text = this._i18n["Overview.InfoTip"];
            accTitleTb.Text = this._i18n["Nav.Accounts"];
            grpTitleTb.Text = this._i18n["Nav.Groups"];
            leadsTitleTb.Text = this._i18n["Nav.SalesLeads"];
            okTitleTb.Text = this._i18n["Nav.AuditLogs"];
            UiControls.SetButtonText(openBrowserBtn, this._i18n["Accounts.BtnOpenBrowserLogin"]);
            UiControls.SetButtonText(liveCheckBtn, this._i18n["Accounts.BtnCheckHealth"]);
            RefreshOverviewState();
        };

        return new ScrollViewer { Content = pageStack };
    }
}
