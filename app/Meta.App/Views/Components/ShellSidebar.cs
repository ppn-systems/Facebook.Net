// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Components;

/// <summary>
/// Collapsible left navigation sidebar component (<c>ShellSidebar.cs</c>) hosting the brand logo,
/// title, collapse toggle, and primary navigation buttons.
/// </summary>
[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Brand title 'One Client' is fixed.")]
public sealed class ShellSidebar : Border
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellSidebar"/> class.
    /// </summary>
    public ShellSidebar(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _vm = viewModel;
        _theme = viewModel.Context.Theme;
        _i18n = viewModel.Context.Localization;

        this.BorderThickness = new Thickness(0, 0, 1, 0);

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

        // 2. Navigation Items (6 primary pages; AuditLogs is accessed from SettingsPage)
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
            ICommand cmd)
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

        this.Child = sidebarGrid;

        void UpdateSidebarLayoutAndColors()
        {
            ThemePalette p = _theme.Palette;
            bool collapsed = _vm.IsSidebarCollapsed;

            this.Width = collapsed ? 60 : 232;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.SubtleBorder;
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
    }
}
