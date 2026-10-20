// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Overview / Home Dashboard Page (<c>OverviewPage.axaml</c>).
/// Displays live account connection status, quick actions, and 4 KPI summary cards.
/// </summary>
public sealed partial class OverviewPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverviewPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public OverviewPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverviewPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public OverviewPage(ShellViewModel viewModel)
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
        StackPanel centerHero = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 560
        };

        Grid headerRow = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Nav.Overview"],
            this._i18n["Overview.InfoTip"],
            out TextBlock titleTb,
            out TextBlock infoTb);
        headerRow.HorizontalAlignment = HorizontalAlignment.Center;

        Control heroIcon = VectorIcons.Create(VectorIcons.CheckCircle, 46, margin: new Thickness(0));
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
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
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

        Border heroCard = UiControls.CreateCard(this._theme, centerHero, new Thickness(26, 20));
        heroCard.VerticalAlignment = VerticalAlignment.Center;

        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 12,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(heroCard, 0);
        Grid.SetRow(statsRow, 1);
        pageGrid.Children.Add(heroCard);
        pageGrid.Children.Add(statsRow);

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
                VectorIcons.SetForeground(heroIcon, p.BrandPrimary);
                statusSub.Text = this._vm.AuditDrawer.LatestTickerText;
            }
            else if (isConnected && acc is not null)
            {
                statusHeading.Text = this._i18n["Overview.StatusConnected"];
                statusHeading.Foreground = p.SuccessText;
                heroCircle.Background = p.SuccessFill;
                heroCircle.BorderBrush = p.SuccessBorder;
                VectorIcons.SetForeground(heroIcon, p.SuccessText);
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
                VectorIcons.SetForeground(heroIcon, p.BrandPrimary);
                statusSub.Text = this._i18n["Overview.SubtextDisconnected"];
            }

            accValTb.Text = this._vm.Accounts.Count.ToString(CultureInfo.InvariantCulture);
            grpValTb.Text = this._vm.TargetGroups.Count.ToString(CultureInfo.InvariantCulture);
            leadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(CultureInfo.InvariantCulture);
            okValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);
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

        return pageGrid;
    }
}
