// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Meta.App.Views.Components;
using Meta.App.Views.Pages;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
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

        sidebarContainer.Child = this.BuildSidebar();
        topBusyContainer.Child = this.BuildTopBusyStrip();
        topContextContainer.Child = this.BuildTopContextStrip();
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

    private Border BuildSidebar()
    {
        Grid sidebarGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(10, 14)
        };

        // 1. Top Brand Logo + Brand Title + Collapse Menu Toggle Button
        Grid topHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4, 0, 4, 18),
            VerticalAlignment = VerticalAlignment.Center
        };

        Image brandLogoImage = new()
        {
            Width = 30,
            Height = 30,
            Stretch = Stretch.UniformToFill
        };
        RenderOptions.SetBitmapInterpolationMode(brandLogoImage, BitmapInterpolationMode.HighQuality);
        using (Stream logoStream = AssetLoader.Open(new Uri("avares://FacebookOneClient/Assets/icon.png")))
        {
            brandLogoImage.Source = new Bitmap(logoStream);
        }

        Border brandBadge = new()
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = brandLogoImage
        };

        TextBlock brandTitle = new()
        {
            Text = "One Client",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 6, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Button collapseBtn = AppButton.CreateIcon(
            _theme,
            VectorIcons.SidebarToggle,
            _i18n["Sidebar.Collapse"],
            _vm.ToggleSidebarCommand);

        Grid.SetColumn(brandBadge, 0);
        Grid.SetColumn(brandTitle, 1);
        Grid.SetColumn(collapseBtn, 2);
        topHeader.Children.Add(brandBadge);
        topHeader.Children.Add(brandTitle);
        topHeader.Children.Add(collapseBtn);
        Grid.SetRow(topHeader, 0);
        sidebarGrid.Children.Add(topHeader);

        // 2. Navigation Items (6 primary pages; AuditLogs is accessed from the bottom of SettingsPage)
        StackPanel navStack = new()
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top
        };

        StackPanel bottomNavStack = new()
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        Border bottomNavDivider = new()
        {
            Height = 1,
            Margin = new Thickness(4, 0, 4, 2)
        };
        bottomNavStack.Children.Add(bottomNavDivider);

        List<(Button Btn, TextBlock Label, Control Icon, string Key, NavigationSection Section)> navEntries = new(6);

        void AddNavItem(
            StackPanel targetPanel,
            string i18nKey,
            string svgFileName,
            NavigationSection section,
            System.Windows.Input.ICommand cmd)
        {
            Button btn = AppButton.CreateSidebarNav(
                _i18n[i18nKey],
                svgFileName,
                cmd,
                out TextBlock lbl,
                out Control icn);
            navEntries.Add((btn, lbl, icn, i18nKey, section));
            targetPanel.Children.Add(btn);
        }

        AddNavItem(navStack, "Nav.Overview", VectorIcons.Home, NavigationSection.Overview, _vm.NavigateOverviewCommand);
        AddNavItem(navStack, "Nav.Accounts", VectorIcons.Accounts, NavigationSection.Accounts, _vm.NavigateAccountsCommand);
        AddNavItem(navStack, "Nav.Groups", VectorIcons.Groups, NavigationSection.Groups, _vm.NavigateGroupsCommand);
        AddNavItem(navStack, "Nav.Publisher", VectorIcons.Publish, NavigationSection.Publisher, _vm.NavigatePublisherCommand);
        AddNavItem(navStack, "Nav.SalesLeads", VectorIcons.SalesComment, NavigationSection.SalesLeads, _vm.NavigateSalesLeadsCommand);
        AddNavItem(bottomNavStack, "Nav.Settings", VectorIcons.Settings, NavigationSection.Settings, _vm.NavigateSettingsCommand);

        Grid.SetRow(navStack, 1);
        Grid.SetRow(bottomNavStack, 2);
        sidebarGrid.Children.Add(navStack);
        sidebarGrid.Children.Add(bottomNavStack);

        Border sidebar = new()
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebarGrid
        };

        void UpdateSidebarLayoutAndColors()
        {
            ThemePalette p = _theme.Palette;
            bool collapsed = _vm.IsSidebarCollapsed;

            sidebar.Width = collapsed ? 60 : 232;
            sidebar.Background = p.ElevatedSurface;
            sidebar.BorderBrush = p.SubtleBorder;
            bottomNavDivider.Background = p.SubtleBorder;

            brandBadge.IsVisible = !collapsed;
            brandTitle.IsVisible = !collapsed;
            brandTitle.Foreground = p.TextPrimary;
            ToolTip.SetTip(brandBadge, _i18n["App.Title"]);
            ToolTip.SetTip(brandTitle, _i18n["App.Title"]);

            Grid.SetColumn(collapseBtn, collapsed ? 0 : 2);
            collapseBtn.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;

            string collapseTip = collapsed ? _i18n["Sidebar.Expand"] : _i18n["Sidebar.Collapse"];
            ToolTip.SetTip(collapseBtn, collapseTip);
            AutomationProperties.SetName(collapseBtn, collapseTip);

            for (int i = 0; i < navEntries.Count; i++)
            {
                (Button btn, TextBlock lbl, Control icn, string key, NavigationSection sec) = navEntries[i];
                bool isActive = _vm.CurrentSection == sec ||
                                (sec == NavigationSection.Settings && _vm.CurrentSection == NavigationSection.AuditLogs);

                lbl.Text = _i18n[key];
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
                AutomationProperties.SetName(btn, _i18n[key]);
                ToolTip.SetTip(btn, _i18n[key]);
            }
        }

        UpdateSidebarLayoutAndColors();
        _theme.ThemeChanged += (_, _) => UpdateSidebarLayoutAndColors();
        _i18n.LanguageChanged += (_, _) => UpdateSidebarLayoutAndColors();
        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentSection), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ShellViewModel.IsSidebarCollapsed), StringComparison.Ordinal))
            {
                UpdateSidebarLayoutAndColors();
            }
        };

        return sidebar;
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

    private StackPanel BuildTopContextStrip()
    {
        StackPanel topContextStrip = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 0)
        };

        Button stopTaskBtn = AppButton.CreateDanger(
            _theme,
            _i18n["Common.BtnStopTask"],
            _vm.StopActiveTaskCommand,
            VectorIcons.StopCircle);
        stopTaskBtn.IsVisible = _vm.IsBusy;
        stopTaskBtn.Padding = new Thickness(10, 5);

        List<Border> disconnectedStatusDots = new();
        bool pulseDim = false;
        DispatcherTimer pulseTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        pulseTimer.Tick += (_, _) =>
        {
            pulseDim = !pulseDim;
            double nextOpacity = pulseDim ? 0.32 : 1.0;
            for (int i = disconnectedStatusDots.Count - 1; i >= 0; i--)
            {
                disconnectedStatusDots[i].Opacity = nextOpacity;
            }
        };
        pulseTimer.Start();

        Grid BuildAccountRowVisual(AccountProfile acc)
        {
            bool isConnected = acc.Status == AccountStatus.Active;
            Grid itemRow = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border av = AvatarBadge.Create(_theme, acc.DisplayName, 20, acc.AvatarUrl);
            TextBlock tb = SectionHeader.CreateLabel(
                _theme,
                acc.DisplayName,
                12.5,
                FontWeight.SemiBold);
            tb.MaxLines = 1;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;

            Color dotColor = isConnected
                ? Color.Parse("#10B981")
                : Color.Parse("#EF4444");
            Color haloColor = isConnected
                ? Color.FromArgb(55, 16, 185, 129)
                : Color.FromArgb(65, 239, 68, 68);

            Border innerDot = new()
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(dotColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border statusDotHalo = new()
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(haloColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0),
                Child = innerDot
            };
            ToolTip.SetTip(statusDotHalo, _i18n.FormatAccountStatus(acc.Status));

            if (!isConnected)
            {
                statusDotHalo.AttachedToVisualTree += (_, _) =>
                {
                    if (!disconnectedStatusDots.Contains(statusDotHalo))
                    {
                        disconnectedStatusDots.Add(statusDotHalo);
                    }

                    if (!pulseTimer.IsEnabled)
                    {
                        pulseTimer.Start();
                    }
                };
                statusDotHalo.DetachedFromVisualTree += (_, _) =>
                {
                    _ = disconnectedStatusDots.Remove(statusDotHalo);
                    if (disconnectedStatusDots.Count == 0 && pulseTimer.IsEnabled)
                    {
                        pulseTimer.Stop();
                    }
                };
            }

            Grid.SetColumn(av, 0);
            Grid.SetColumn(tb, 1);
            Grid.SetColumn(statusDotHalo, 2);
            itemRow.Children.Add(av);
            itemRow.Children.Add(tb);
            itemRow.Children.Add(statusDotHalo);
            return itemRow;
        }

        Border singleAccBadge = new()
        {
            Height = 32,
            MinWidth = 165,
            MaxWidth = 245,
            Padding = new Thickness(10, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(singleAccBadge, _i18n["TopBar.ActiveAccount"]);
        ToolTip.SetTip(singleAccBadge, _i18n["TopBar.ActiveAccount"]);

        ComboBox activeAccCombo = FormField.CreateComboBox<AccountProfile>(
            _theme,
            _vm.Accounts,
            BuildAccountRowVisual,
            selectedItem: _vm.SelectedAccount,
            placeholderText: _i18n["TopBar.NoAccount"]);
        activeAccCombo.MinWidth = 195;
        activeAccCombo.MaxWidth = 245;
        activeAccCombo.Height = 32;
        AutomationProperties.SetName(activeAccCombo, _i18n["TopBar.ActiveAccount"]);
        ToolTip.SetTip(activeAccCombo, _i18n["TopBar.ActiveAccount"]);
        activeAccCombo.SelectionChanged += (_, _) =>
        {
            if (activeAccCombo.SelectedItem is AccountProfile selected &&
                !ReferenceEquals(_vm.SelectedAccount, selected))
            {
                _vm.SelectedAccount = selected;
            }
        };

        void RefreshAccountSwitcherVisibility()
        {
            ThemePalette p = _theme.Palette;
            singleAccBadge.Background = p.ElevatedSurface;
            singleAccBadge.BorderBrush = p.SubtleBorder;
            ToolTip.SetTip(singleAccBadge, _i18n["TopBar.ActiveAccount"]);
            AutomationProperties.SetName(singleAccBadge, _i18n["TopBar.ActiveAccount"]);

            bool hasMultipleAccounts = _vm.Accounts.Count > 1;
            activeAccCombo.IsVisible = hasMultipleAccounts;
            singleAccBadge.IsVisible = !hasMultipleAccounts;

            if (!hasMultipleAccounts)
            {
                AccountProfile? singleAcc = _vm.SelectedAccount ?? (_vm.Accounts.Count == 1 ? _vm.Accounts[0] : null);
                if (singleAcc is not null)
                {
                    singleAccBadge.Child = BuildAccountRowVisual(singleAcc);
                }
                else
                {
                    TextBlock noAccTb = SectionHeader.CreateLabel(
                        _theme,
                        _i18n["TopBar.NoAccount"],
                        12,
                        FontWeight.Medium,
                        isSecondary: true);
                    noAccTb.VerticalAlignment = VerticalAlignment.Center;
                    noAccTb.MaxLines = 1;
                    noAccTb.TextTrimming = TextTrimming.CharacterEllipsis;
                    singleAccBadge.Child = noAccTb;
                }
            }
        }

        RefreshAccountSwitcherVisibility();
        _vm.Accounts.CollectionChanged += (_, _) => RefreshAccountSwitcherVisibility();

        topContextStrip.Children.Add(stopTaskBtn);
        topContextStrip.Children.Add(singleAccBadge);
        topContextStrip.Children.Add(activeAccCombo);

        Button aiToggleBtn = new()
        {
            Width = 34,
            Height = 32,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = VectorIcons.CreateAiAssistantIcon(22),
            Command = _vm.ToggleAiAssistantCommand,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        ToolTip.SetTip(aiToggleBtn, _i18n["Ai.BtnToggleTip"]);
        AutomationProperties.SetName(aiToggleBtn, _i18n["Ai.PanelTitle"]);

        void UpdateAiToggleButtonVisuals()
        {
            ThemePalette p = _theme.Palette;
            bool isOpen = _vm.AiAssistant.IsPanelOpen;
            aiToggleBtn.Background = isOpen ? p.BrandSubtleFill : Brushes.Transparent;
        }

        UpdateAiToggleButtonVisuals();
        _theme.ThemeChanged += (_, _) =>
        {
            UpdateAiToggleButtonVisuals();
            RefreshAccountSwitcherVisibility();
        };
        _vm.AiAssistant.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.IsPanelOpen), StringComparison.Ordinal))
            {
                UpdateAiToggleButtonVisuals();
            }
        };

        topContextStrip.Children.Add(aiToggleBtn);

        _i18n.LanguageChanged += (_, _) =>
        {
            activeAccCombo.PlaceholderText = _i18n["TopBar.NoAccount"];
            ToolTip.SetTip(activeAccCombo, _i18n["TopBar.ActiveAccount"]);
            AppButton.SetText(stopTaskBtn, _i18n["Common.BtnStopTask"]);
            ToolTip.SetTip(aiToggleBtn, _i18n["Ai.BtnToggleTip"]);
            AutomationProperties.SetName(aiToggleBtn, _i18n["Ai.PanelTitle"]);
            UpdateAiToggleButtonVisuals();
            RefreshAccountSwitcherVisibility();
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                if (!ReferenceEquals(activeAccCombo.SelectedItem, _vm.SelectedAccount))
                {
                    activeAccCombo.SelectedItem = _vm.SelectedAccount;
                }

                RefreshAccountSwitcherVisibility();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.IsBusy), StringComparison.Ordinal))
            {
                stopTaskBtn.IsVisible = _vm.IsBusy;
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
