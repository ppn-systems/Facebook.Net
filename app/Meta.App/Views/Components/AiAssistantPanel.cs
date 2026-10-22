// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.AiAssistant;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.AiAssistant;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Components;

/// <summary>
/// VS Code-style right-hand dockable AI Assistant drawer (<c>Width = 370</c>).
/// Hosts three stage-aware views:
/// 1) On-Demand Google Antigravity CLI (<c>agy</c>) installation card,
/// 2) Browser-selectable CDP Google OAuth sign-in card,
/// 3) Ready workspace with interactive AI Chat and inline action confirmation buttons.
/// </summary>
[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Localized via LocalizationService with symbol prefixes.")]
public sealed class AiAssistantPanel : Border
{
    private readonly ShellViewModel _vm;
    private readonly AiAssistantViewModel _aiVm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiAssistantPanel"/> class.
    /// </summary>
    /// <param name="viewModel">The root <see cref="ShellViewModel"/> owning <see cref="ShellViewModel.AiAssistant"/>.</param>
    public AiAssistantPanel(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _vm = viewModel;
        _aiVm = viewModel.AiAssistant;
        _theme = viewModel.Context.Theme;
        _i18n = viewModel.Context.Localization;

        this.Width = 370;
        this.VerticalAlignment = VerticalAlignment.Stretch;
        this.HorizontalAlignment = HorizontalAlignment.Right;
        this.BorderThickness = new Thickness(1, 0, 0, 0);

        Grid rootGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Control topBar = this.BuildTopTitleBar(out Action refreshTopBarTheme, out Action refreshTopBarI18n);
        Control statusStrip = this.BuildStatusBannerStrip(out Action refreshStatusStrip);

        Grid stageBodyGrid = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Control installView = this.BuildInstallView(out Action refreshInstallView, out Action refreshInstallI18n);
        Control authView = this.BuildOAuthView(out Action refreshAuthView, out Action refreshAuthI18n);
        Control chatSubView = this.BuildReadyChatSubView(out Action refreshChatView, out Action refreshChatI18n);

        stageBodyGrid.Children.Add(installView);
        stageBodyGrid.Children.Add(authView);
        stageBodyGrid.Children.Add(chatSubView);

        Grid.SetRow(topBar, 0);
        Grid.SetRow(statusStrip, 1);
        Grid.SetRow(stageBodyGrid, 2);
        rootGrid.Children.Add(topBar);
        rootGrid.Children.Add(statusStrip);
        rootGrid.Children.Add(stageBodyGrid);

        this.Child = rootGrid;

        void UpdateActiveStageVisibility()
        {
            AiAssistantSetupStage stage = _aiVm.SetupStage;
            bool isInstallStage = stage is AiAssistantSetupStage.NeedsInstall or AiAssistantSetupStage.Installing;
            bool isAuthStage = stage is AiAssistantSetupStage.NeedsAuth or AiAssistantSetupStage.Authenticating;
            bool isReadyOrChat = !isInstallStage && !isAuthStage;

            installView.IsVisible = isInstallStage;
            authView.IsVisible = isAuthStage;
            chatSubView.IsVisible = isReadyOrChat;
        }

        void RefreshAllVisuals()
        {
            ThemePalette p = _theme.Palette;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.SubtleBorder;

            refreshTopBarTheme();
            refreshStatusStrip();
            UpdateActiveStageVisibility();
            refreshInstallView();
            refreshAuthView();
            refreshChatView();
        }

        RefreshAllVisuals();
        ThemeLifecycle.Bind(this, _theme, RefreshAllVisuals);

        _i18n.LanguageChanged += (_, _) =>
        {
            refreshTopBarI18n();
            refreshInstallI18n();
            refreshAuthI18n();
            refreshChatI18n();
            RefreshAllVisuals();
        };

        _aiVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.SetupStage), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AiAssistantViewModel.ActiveSubTabIndex), StringComparison.Ordinal))
            {
                refreshTopBarTheme();
                UpdateActiveStageVisibility();
                refreshInstallView();
                refreshAuthView();
            }

            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.StatusBannerText), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AiAssistantViewModel.IsBusy), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AiAssistantViewModel.InstallProgressPercent), StringComparison.Ordinal))
            {
                refreshStatusStrip();
                refreshInstallView();
                refreshAuthView();
            }
        };
    }

    // ───────────────────────────── 1. Top Title Bar ─────────────────────────────

    private static readonly FontFamily s_monospaceFont = new("Cascadia Mono, Consolas, monospace");

    private Border BuildTopTitleBar(out Action refreshTheme, out Action refreshI18n)
    {
        ThemePalette initialPalette = _theme.Palette;

        Grid headerGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel titleStack = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control sparkleIcon = VectorIcons.CreateAiAssistantIcon(18);

        TextBlock panelTitleTb = new()
        {
            Text = _i18n["Ai.PanelTitle"],
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        titleStack.Children.Add(sparkleIcon);
        titleStack.Children.Add(panelTitleTb);

        StackPanel rightActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };

        // ── Conversation History & New Chat Popup Flyout ──
        StackPanel flyoutListStack = new()
        {
            Spacing = 4
        };

        ScrollViewer flyoutScroll = new()
        {
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = flyoutListStack
        };

        TextBlock flyoutHeaderTb = new()
        {
            Text = _i18n["Ai.HistoryPlaceholder"],
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control newConvPopupIcon = VectorIcons.Create(VectorIcons.Plus, 12, initialPalette.BrandPrimary);
        TextBlock newConvPopupTb = new()
        {
            Text = _i18n["Ai.BtnNewChat"],
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel newConvPopupContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { newConvPopupIcon, newConvPopupTb }
        };

        Flyout historyFlyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard
        };

        Button newConvPopupBtn = new()
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4),
            Content = newConvPopupContent,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        newConvPopupBtn.Click += (_, _) =>
        {
            if (_aiVm.StartNewConversationCommand.CanExecute(null))
            {
                _aiVm.StartNewConversationCommand.Execute(null);
            }

            historyFlyout.Hide();
        };

        Grid flyoutHeaderGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(flyoutHeaderTb, 0);
        Grid.SetColumn(newConvPopupBtn, 1);
        flyoutHeaderGrid.Children.Add(flyoutHeaderTb);
        flyoutHeaderGrid.Children.Add(newConvPopupBtn);

        Border flyoutDivider = new()
        {
            Height = 1
        };

        StackPanel flyoutRootStack = new()
        {
            Spacing = 8,
            Width = 310,
            Children = { flyoutHeaderGrid, flyoutDivider, flyoutScroll }
        };

        Border flyoutCard = new()
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = flyoutRootStack
        };

        historyFlyout.Content = flyoutCard;

        void RebuildHistoryFlyoutItems()
        {
            ThemePalette p = _theme.Palette;
            flyoutCard.Background = p.ElevatedSurface;
            flyoutCard.BorderBrush = p.InteractiveBorder;
            flyoutDivider.Background = p.SubtleBorder;
            flyoutHeaderTb.Foreground = p.TextPrimary;
            newConvPopupBtn.Background = p.BrandSubtleFill;
            newConvPopupBtn.BorderBrush = p.BrandSubtleBorder;
            newConvPopupTb.Foreground = p.BrandPrimary;
            VectorIcons.SetForeground(newConvPopupIcon, p.BrandPrimary);

            flyoutListStack.Children.Clear();

            if (_aiVm.Conversations.Count == 0)
            {
                TextBlock emptyTb = new()
                {
                    Text = _i18n["Ai.HistoryEmpty"],
                    FontSize = 11.5,
                    Foreground = p.TextMuted,
                    Margin = new Thickness(4, 10),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                flyoutListStack.Children.Add(emptyTb);
                return;
            }

            for (int i = 0; i < _aiVm.Conversations.Count; i++)
            {
                AiConversationThread thread = _aiVm.Conversations[i];
                bool isCurrent = string.Equals(thread.Id, _aiVm.ActiveConversationId, StringComparison.Ordinal);

                Grid rowGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };

                StackPanel infoStack = new()
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock itemTitleTb = new()
                {
                    Text = string.IsNullOrWhiteSpace(thread.Title) ? _i18n["Ai.NewConversationDefaultTitle"] : thread.Title,
                    FontSize = 12,
                    FontWeight = isCurrent ? FontWeight.Bold : FontWeight.SemiBold,
                    Foreground = isCurrent ? p.BrandPrimary : p.TextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                string timeStamp = thread.UpdatedAtUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
                TextBlock itemMetaTb = new()
                {
                    Text = string.Create(CultureInfo.InvariantCulture, $"{timeStamp} • {thread.MessageCount}"),
                    FontSize = 10.5,
                    Foreground = p.TextMuted
                };

                infoStack.Children.Add(itemTitleTb);
                infoStack.Children.Add(itemMetaTb);

                Button selectItemBtn = new()
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 5),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = infoStack,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                selectItemBtn.Click += (_, _) =>
                {
                    if (_aiVm.SelectConversationCommand.CanExecute(thread))
                    {
                        _aiVm.SelectConversationCommand.Execute(thread);
                    }

                    historyFlyout.Hide();
                };

                Control delIcon = VectorIcons.Create(VectorIcons.Trash, 12, p.TextSecondary);
                Button delBtn = new()
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = delIcon,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(delBtn, _i18n["Ai.BtnDeleteConversationTip"]);
                AutomationProperties.SetName(delBtn, _i18n["Ai.BtnDeleteConversationTip"]);
                delBtn.Click += (_, _) =>
                {
                    if (_aiVm.DeleteConversationCommand.CanExecute(thread))
                    {
                        _aiVm.DeleteConversationCommand.Execute(thread);
                    }
                };

                Grid.SetColumn(selectItemBtn, 0);
                Grid.SetColumn(delBtn, 1);
                rowGrid.Children.Add(selectItemBtn);
                rowGrid.Children.Add(delBtn);

                Border itemBorder = new()
                {
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 2),
                    Background = isCurrent ? p.BrandSubtleFill : p.SecondarySurface,
                    BorderBrush = isCurrent ? p.BrandSubtleBorder : p.SubtleBorder,
                    Child = rowGrid
                };

                flyoutListStack.Children.Add(itemBorder);
            }
        }

        historyFlyout.Opening += (_, _) => RebuildHistoryFlyoutItems();

        Control historyIcon = VectorIcons.Create(VectorIcons.History, 14, initialPalette.TextSecondary);
        Button historyBtn = new()
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = historyIcon,
            Flyout = historyFlyout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(historyBtn, _i18n["Ai.HistoryTooltip"]);
        AutomationProperties.SetName(historyBtn, _i18n["Ai.HistoryTooltip"]);

        TextBlock minimizeGlyph = new()
        {
            Text = "─",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button minimizeBtn = new()
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = minimizeGlyph,
            Command = _aiVm.ClosePanelCommand,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(minimizeBtn, _i18n["Ai.BtnToggleTip"]);
        AutomationProperties.SetName(minimizeBtn, _i18n["Ai.BtnToggleTip"]);

        rightActions.Children.Add(historyBtn);
        rightActions.Children.Add(minimizeBtn);

        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(rightActions, 1);
        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(rightActions);

        Border headerBorder = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8),
            Child = headerGrid
        };

        void SyncHistoryButtonState()
        {
            AiAssistantSetupStage stage = _aiVm.SetupStage;
            bool isInstallOrAuth = stage is AiAssistantSetupStage.NeedsInstall or AiAssistantSetupStage.Installing
                or AiAssistantSetupStage.NeedsAuth or AiAssistantSetupStage.Authenticating;
            historyBtn.IsVisible = !isInstallOrAuth;
            RebuildHistoryFlyoutItems();
        }

        _aiVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.SelectedConversation), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AiAssistantViewModel.ActiveConversationId), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(AiAssistantViewModel.SetupStage), StringComparison.Ordinal))
            {
                SyncHistoryButtonState();
            }
        };

        _aiVm.Conversations.CollectionChanged += (_, _) => SyncHistoryButtonState();

        refreshTheme = () =>
        {
            ThemePalette p = _theme.Palette;
            headerBorder.Background = p.ElevatedSurface;
            headerBorder.BorderBrush = p.SubtleBorder;
            VectorIcons.SetForeground(sparkleIcon, p.BrandPrimary);
            panelTitleTb.Foreground = p.TextPrimary;

            historyBtn.Background = p.SecondarySurface;
            historyBtn.BorderBrush = p.InteractiveBorder;
            VectorIcons.SetForeground(historyIcon, p.TextSecondary);

            minimizeBtn.Background = p.SecondarySurface;
            minimizeBtn.BorderBrush = p.InteractiveBorder;
            minimizeGlyph.Foreground = p.TextPrimary;

            SyncHistoryButtonState();
        };

        refreshI18n = () =>
        {
            panelTitleTb.Text = _i18n["Ai.PanelTitle"];
            flyoutHeaderTb.Text = _i18n["Ai.HistoryPlaceholder"];
            newConvPopupTb.Text = _i18n["Ai.BtnNewChat"];
            ToolTip.SetTip(historyBtn, _i18n["Ai.HistoryTooltip"]);
            AutomationProperties.SetName(historyBtn, _i18n["Ai.HistoryTooltip"]);
            ToolTip.SetTip(minimizeBtn, _i18n["Ai.BtnToggleTip"]);
            AutomationProperties.SetName(minimizeBtn, _i18n["Ai.BtnToggleTip"]);
            RebuildHistoryFlyoutItems();
        };

        return headerBorder;
    }

    private Border BuildStatusBannerStrip(out Action refreshStrip)
    {
        StackPanel stripStack = new()
        {
            Spacing = 4
        };

        ProgressBar busyBar = new()
        {
            IsIndeterminate = true,
            Height = 2.5,
            MinHeight = 2.5,
            CornerRadius = new CornerRadius(1.25),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        TextBlock bannerTb = new()
        {
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.Wrap
        };

        stripStack.Children.Add(busyBar);
        stripStack.Children.Add(bannerTb);

        Border stripBorder = new()
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6),
            Child = stripStack
        };

        refreshStrip = () =>
        {
            ThemePalette p = _theme.Palette;
            stripBorder.Background = p.SecondarySurface;
            stripBorder.BorderBrush = p.SubtleBorder;
            busyBar.Foreground = p.BrandPrimary;
            busyBar.IsVisible = _aiVm.IsBusy;

            string text = _aiVm.StatusBannerText;
            bannerTb.Text = string.IsNullOrWhiteSpace(text)
                ? (_aiVm.IsBusy ? _i18n["Ai.StatusThinking"] : string.Empty)
                : text;
            bannerTb.Foreground = _aiVm.IsBusy ? p.BrandPrimary : p.TextSecondary;
            bannerTb.IsVisible = !string.IsNullOrWhiteSpace(bannerTb.Text);

            stripBorder.IsVisible = _aiVm.IsBusy || !string.IsNullOrWhiteSpace(text);
        };

        return stripBorder;
    }

    // ───────────────────────────── 2A. View A — Install Card ─────────────────────────────

    private ScrollViewer BuildInstallView(out Action refreshState, out Action refreshI18n)
    {
        StackPanel outerStack = new()
        {
            Spacing = 14,
            Margin = new Thickness(14)
        };

        StackPanel cardStack = new()
        {
            Spacing = 12
        };

        StackPanel headerRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control installHeaderIcon = VectorIcons.Create(VectorIcons.AiSparkle, 18, _theme.Palette.BrandPrimary);
        TextBlock titleTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.InstallTitle"],
            15,
            FontWeight.Bold);
        headerRow.Children.Add(installHeaderIcon);
        headerRow.Children.Add(titleTb);

        TextBlock descTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.InstallDescription"],
            12.5,
            FontWeight.Normal,
            isSecondary: true);

        StackPanel bulletsStack = new()
        {
            Spacing = 5,
            Margin = new Thickness(2, 2, 0, 2)
        };

        TextBlock bulletFreeTb = SectionHeader.CreateLabel(_theme, _i18n["Ai.InstallBulletFree"], 12.5, FontWeight.SemiBold);
        TextBlock bulletAutoTb = SectionHeader.CreateLabel(_theme, _i18n["Ai.InstallBulletAuto"], 12.5, FontWeight.SemiBold);
        TextBlock bulletNetTb = SectionHeader.CreateLabel(_theme, _i18n["Ai.InstallBulletInternet"], 12.5, FontWeight.SemiBold);
        bulletsStack.Children.Add(bulletFreeTb);
        bulletsStack.Children.Add(bulletAutoTb);
        bulletsStack.Children.Add(bulletNetTb);

        StackPanel progressSection = new()
        {
            Spacing = 6,
            IsVisible = false
        };

        ProgressBar installProgressBar = new()
        {
            Minimum = 0,
            Maximum = 100,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        TextBlock installStatusTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.InstallingStatus"],
            11.5,
            FontWeight.Medium,
            isSecondary: true);

        progressSection.Children.Add(installProgressBar);
        progressSection.Children.Add(installStatusTb);

        Grid actionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.35*,1*"),
            ColumnSpacing = 8
        };

        Button installNowBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Ai.BtnInstallNow"],
            _aiVm.InstallAgyCommand,
            VectorIcons.Download);

        Button skipBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnSkipInstall"],
            _aiVm.SkipInstallCommand);

        Grid.SetColumn(installNowBtn, 0);
        Grid.SetColumn(skipBtn, 1);
        actionsRow.Children.Add(installNowBtn);
        actionsRow.Children.Add(skipBtn);

        cardStack.Children.Add(headerRow);
        cardStack.Children.Add(descTb);
        cardStack.Children.Add(bulletsStack);
        cardStack.Children.Add(progressSection);
        cardStack.Children.Add(actionsRow);

        Border dialogCard = SurfaceCard.CreateInset(_theme, cardStack, new Thickness(16));
        outerStack.Children.Add(dialogCard);

        refreshState = () =>
        {
            ThemePalette p = _theme.Palette;
            VectorIcons.SetForeground(installHeaderIcon, p.BrandPrimary);
            installProgressBar.Foreground = p.BrandPrimary;
            installProgressBar.Background = p.ElevatedSurface;

            bool isInstalling = _aiVm.SetupStage == AiAssistantSetupStage.Installing;

            progressSection.IsVisible = isInstalling;
            installProgressBar.IsIndeterminate = false;
            installProgressBar.Value = Math.Clamp(_aiVm.InstallProgressPercent, 0, 100);

            installStatusTb.Text = !string.IsNullOrWhiteSpace(_aiVm.StatusBannerText)
                ? _aiVm.StatusBannerText
                : _i18n["Ai.InstallingStatus"];

            installNowBtn.IsEnabled = !isInstalling;
        };

        refreshI18n = () =>
        {
            titleTb.Text = _i18n["Ai.InstallTitle"];
            descTb.Text = _i18n["Ai.InstallDescription"];
            bulletFreeTb.Text = _i18n["Ai.InstallBulletFree"];
            bulletAutoTb.Text = _i18n["Ai.InstallBulletAuto"];
            bulletNetTb.Text = _i18n["Ai.InstallBulletInternet"];
            AppButton.SetText(installNowBtn, _i18n["Ai.BtnInstallNow"]);
            AppButton.SetText(skipBtn, _i18n["Ai.BtnSkipInstall"]);
        };

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = outerStack
        };
    }

    // ───────────────────────────── 2B. View B — Google OAuth Card ─────────────────────────────

    private ScrollViewer BuildOAuthView(out Action refreshState, out Action refreshI18n)
    {
        StackPanel outerStack = new()
        {
            Spacing = 12,
            Margin = new Thickness(14)
        };

        StackPanel cardStack = new()
        {
            Spacing = 12
        };

        StackPanel headerRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control authHeaderIcon = VectorIcons.Create(VectorIcons.Shield, 18, _theme.Palette.BrandPrimary);
        TextBlock titleTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.AuthTitle"],
            15,
            FontWeight.Bold);
        headerRow.Children.Add(authHeaderIcon);
        headerRow.Children.Add(titleTb);

        TextBlock descTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.AuthDescription"],
            12,
            FontWeight.Normal,
            isSecondary: true);

        Button startOAuthBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Ai.BtnStartOAuth"],
            _aiVm.StartCdpOAuthLoginCommand,
            VectorIcons.Browser);

        Button verifyAuthBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnVerifyAuthAgain"],
            _aiVm.VerifyAuthStateCommand,
            VectorIcons.CheckCircle);

        Border divider = new()
        {
            Height = 1,
            Margin = new Thickness(0, 2)
        };

        TextBox manualCodeBox = FormField.CreateTextBox(
            _theme,
            _i18n["Ai.AuthManualCodePlaceholder"],
            _aiVm.ManualOAuthCode);

        bool updatingManualCode = false;
        manualCodeBox.TextChanged += (_, _) =>
        {
            if (!updatingManualCode)
            {
                _aiVm.ManualOAuthCode = manualCodeBox.Text ?? string.Empty;
            }
        };

        Button submitCodeBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnSubmitCode"],
            _aiVm.SubmitManualOAuthCodeCommand,
            VectorIcons.CheckCircle);

        cardStack.Children.Add(headerRow);
        cardStack.Children.Add(descTb);
        cardStack.Children.Add(startOAuthBtn);
        cardStack.Children.Add(verifyAuthBtn);
        cardStack.Children.Add(divider);
        cardStack.Children.Add(manualCodeBox);
        cardStack.Children.Add(submitCodeBtn);

        Border authCard = SurfaceCard.CreateInset(_theme, cardStack, new Thickness(16));
        outerStack.Children.Add(authCard);

        _aiVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.ManualOAuthCode), StringComparison.Ordinal))
            {
                if (!string.Equals(manualCodeBox.Text, _aiVm.ManualOAuthCode, StringComparison.Ordinal))
                {
                    updatingManualCode = true;
                    manualCodeBox.Text = _aiVm.ManualOAuthCode;
                    updatingManualCode = false;
                }
            }
        };

        refreshState = () =>
        {
            ThemePalette p = _theme.Palette;
            VectorIcons.SetForeground(authHeaderIcon, p.BrandPrimary);
            divider.Background = p.SubtleBorder;
        };

        refreshI18n = () =>
        {
            titleTb.Text = _i18n["Ai.AuthTitle"];
            descTb.Text = _i18n["Ai.AuthDescription"];
            AppButton.SetText(startOAuthBtn, _i18n["Ai.BtnStartOAuth"]);
            AppButton.SetText(verifyAuthBtn, _i18n["Ai.BtnVerifyAuthAgain"]);
            manualCodeBox.PlaceholderText = _i18n["Ai.AuthManualCodePlaceholder"];
            AppButton.SetText(submitCodeBtn, _i18n["Ai.BtnSubmitCode"]);
        };

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = outerStack
        };
    }

    // ───────────────────────────── 2C. Ready State — AI Chat View ─────────────────────────────

    private Grid BuildReadyChatSubView(out Action refreshState, out Action refreshI18n)
    {
        Grid chatGrid = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        StackPanel scrollContentStack = new()
        {
            Spacing = 10,
            Margin = new Thickness(12, 12, 12, 14)
        };

        Border welcomeCard = this.BuildWelcomeCard(out Action refreshWelcomeTheme, out Action refreshWelcomeI18n);
        scrollContentStack.Children.Add(welcomeCard);

        StackPanel messagesStack = new()
        {
            Spacing = 10
        };
        scrollContentStack.Children.Add(messagesStack);

        ScrollViewer chatScrollViewer = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = scrollContentStack
        };

        Grid inputRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBox promptBox = FormField.CreateTextBox(
            _theme,
            _i18n["Ai.InputPlaceholder"],
            _aiVm.InputPromptText,
            multiline: true);
        promptBox.AcceptsReturn = false;
        promptBox.TextWrapping = TextWrapping.Wrap;
        promptBox.MinHeight = 38;
        promptBox.MaxHeight = 96;
        promptBox.VerticalContentAlignment = VerticalAlignment.Center;

        bool updatingPrompt = false;
        promptBox.TextChanged += (_, _) =>
        {
            if (!updatingPrompt)
            {
                _aiVm.InputPromptText = promptBox.Text ?? string.Empty;
            }
        };

        promptBox.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    e.Handled = true;
                    string current = promptBox.Text ?? string.Empty;
                    int caret = Math.Clamp(promptBox.CaretIndex, 0, current.Length);
                    promptBox.Text = current.Insert(caret, "\n");
                    promptBox.CaretIndex = caret + 1;
                    return;
                }

                e.Handled = true;
                if (_aiVm.SendMessageCommand.CanExecute(null))
                {
                    _aiVm.SendMessageCommand.Execute(null);
                }
            },
            RoutingStrategies.Tunnel);

        Control sendIcon = VectorIcons.Create(VectorIcons.Publish, 16, Brushes.White);
        Button sendBtn = new()
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = _theme.Palette.BrandPrimary,
            Content = sendIcon,
            Command = _aiVm.SendMessageCommand,
            IsVisible = !_aiVm.IsBusy,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(sendBtn, _i18n["Ai.BtnSend"]);
        AutomationProperties.SetName(sendBtn, _i18n["Ai.BtnSend"]);

        Button stopBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnStopGeneration"],
            _aiVm.StopGenerationCommand,
            VectorIcons.StopCircle);
        stopBtn.Height = 38;
        stopBtn.VerticalAlignment = VerticalAlignment.Bottom;
        stopBtn.IsVisible = _aiVm.IsBusy;

        StackPanel actionBtnHost = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { stopBtn, sendBtn }
        };

        Grid.SetColumn(promptBox, 0);
        Grid.SetColumn(actionBtnHost, 1);
        inputRow.Children.Add(promptBox);
        inputRow.Children.Add(actionBtnHost);

        // ── Active Context Pills + (+ Nạp Context ▾) & (Kịch bản mẫu ▾) Toolbar ──
        WrapPanel activeContextPillsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 5,
            LineSpacing = 4,
            IsVisible = false
        };

        Button injectContextBtn = this.CreateInjectContextFlyoutButton(out Action refreshInjectBtnTheme, out Action refreshInjectBtnI18n);
        Button presetsToolbarBtn = this.CreatePromptPresetsFlyoutButton(out Action refreshPresetsBtnTheme, out Action refreshPresetsBtnI18n);

        WrapPanel composerToolbarRow = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { injectContextBtn, presetsToolbarBtn }
        };

        StackPanel composerContainerStack = new()
        {
            Spacing = 7,
            Children = { activeContextPillsWrap, composerToolbarRow, inputRow }
        };

        void RebuildActiveContextPills()
        {
            activeContextPillsWrap.Children.Clear();
            activeContextPillsWrap.IsVisible = _aiVm.ActiveContextPacks.Count > 0;
            if (_aiVm.ActiveContextPacks.Count == 0)
            {
                return;
            }

            ThemePalette p = _theme.Palette;
            for (int i = 0; i < _aiVm.ActiveContextPacks.Count; i++)
            {
                string packId = _aiVm.ActiveContextPacks[i];
                string packTitle = this.FormatContextPackLabel(packId);

                TextBlock pillText = new()
                {
                    Text = $"{packTitle}  ✕",
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = p.BrandPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Button pillBtn = new()
                {
                    CornerRadius = new CornerRadius(999),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 2),
                    MinHeight = 20,
                    Background = p.BrandSubtleFill,
                    BorderBrush = p.BrandSubtleBorder,
                    Content = pillText,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                pillBtn.Click += (_, _) => _aiVm.ToggleContextPack(packId);
                activeContextPillsWrap.Children.Add(pillBtn);
            }
        }

        _aiVm.ActiveContextPacks.CollectionChanged += (_, _) => RebuildActiveContextPills();
        RebuildActiveContextPills();

        Border inputBarBorder = new()
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 10),
            Child = composerContainerStack
        };

        Grid.SetRow(chatScrollViewer, 0);
        Grid.SetRow(inputBarBorder, 1);
        chatGrid.Children.Add(chatScrollViewer);
        chatGrid.Children.Add(inputBarBorder);

        Dictionary<AiChatBubbleItem, PropertyChangedEventHandler> bubbleSubscriptions = new();
        List<Action> bubbleRefreshers = new();

        void AppendBubbleItem(AiChatBubbleItem item)
        {
            if (bubbleSubscriptions.TryGetValue(item, out PropertyChangedEventHandler? existingHandler))
            {
                item.PropertyChanged -= existingHandler;
            }

            Control bubbleControl = this.CreateChatBubbleCard(item, out SelectableTextBlock contentTb, out Action refreshBubble);
            bubbleRefreshers.Add(refreshBubble);
            messagesStack.Children.Add(bubbleControl);

            bool contentUpdatePending = false;
            void OnBubblePropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (string.Equals(e.PropertyName, nameof(AiChatBubbleItem.Content), StringComparison.Ordinal))
                {
                    if (contentUpdatePending)
                    {
                        return;
                    }

                    contentUpdatePending = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        contentUpdatePending = false;
                        RenderMarkdownIntoControl(contentTb, item.Content, _theme.Palette);
                        chatScrollViewer.ScrollToEnd();
                    }, DispatcherPriority.Render);
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    refreshBubble();
                    chatScrollViewer.ScrollToEnd();
                });
            }

            PropertyChangedEventHandler handler = OnBubblePropertyChanged;
            item.PropertyChanged += handler;
            bubbleSubscriptions[item] = handler;

            Dispatcher.UIThread.Post(chatScrollViewer.ScrollToEnd, DispatcherPriority.Background);
        }

        void RebuildMessageBubbles()
        {
            foreach (KeyValuePair<AiChatBubbleItem, PropertyChangedEventHandler> kvp in bubbleSubscriptions)
            {
                kvp.Key.PropertyChanged -= kvp.Value;
            }

            bubbleSubscriptions.Clear();
            bubbleRefreshers.Clear();
            messagesStack.Children.Clear();

            for (int i = 0; i < _aiVm.Messages.Count; i++)
            {
                AppendBubbleItem(_aiVm.Messages[i]);
            }

            Dispatcher.UIThread.Post(chatScrollViewer.ScrollToEnd, DispatcherPriority.Background);
        }

        void RefreshExistingBubbles()
        {
            for (int i = 0; i < bubbleRefreshers.Count; i++)
            {
                bubbleRefreshers[i]();
            }
        }

        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshExistingBubbles();
            }
        };

        _vm.Accounts.CollectionChanged += (_, _) => RefreshExistingBubbles();

        _aiVm.Messages.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    if (e.NewItems[i] is AiChatBubbleItem addedItem)
                    {
                        AppendBubbleItem(addedItem);
                    }
                }

                return;
            }

            RebuildMessageBubbles();
        };

        _aiVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.InputPromptText), StringComparison.Ordinal))
            {
                if (!string.Equals(promptBox.Text, _aiVm.InputPromptText, StringComparison.Ordinal))
                {
                    updatingPrompt = true;
                    promptBox.Text = _aiVm.InputPromptText;
                    updatingPrompt = false;
                }
            }
            else if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.IsBusy), StringComparison.Ordinal))
            {
                sendBtn.IsVisible = !_aiVm.IsBusy;
                stopBtn.IsVisible = _aiVm.IsBusy;
            }
        };

        RebuildMessageBubbles();

        refreshState = () =>
        {
            ThemePalette p = _theme.Palette;
            refreshWelcomeTheme();
            refreshInjectBtnTheme();
            refreshPresetsBtnTheme();
            RebuildActiveContextPills();
            inputBarBorder.Background = p.ElevatedSurface;
            inputBarBorder.BorderBrush = p.SubtleBorder;
            sendBtn.Background = p.BrandPrimary;
            sendBtn.IsVisible = !_aiVm.IsBusy;
            stopBtn.IsVisible = _aiVm.IsBusy;
            VectorIcons.SetForeground(sendIcon, Brushes.White);
            RebuildMessageBubbles();
        };

        refreshI18n = () =>
        {
            refreshWelcomeI18n();
            refreshInjectBtnI18n();
            refreshPresetsBtnI18n();
            RebuildActiveContextPills();
            promptBox.PlaceholderText = _i18n["Ai.InputPlaceholder"];
            AutomationProperties.SetName(promptBox, _i18n["Ai.InputPlaceholder"]);
            ToolTip.SetTip(promptBox, _i18n["Ai.InputPlaceholder"]);
            ToolTip.SetTip(sendBtn, _i18n["Ai.BtnSend"]);
            AutomationProperties.SetName(sendBtn, _i18n["Ai.BtnSend"]);
            AppButton.SetText(stopBtn, _i18n["Ai.BtnStopGeneration"]);
            RebuildMessageBubbles();
        };

        return chatGrid;
    }

    private Button CreateInjectContextFlyoutButton(out Action refreshTheme, out Action refreshI18n)
    {
        ThemePalette initialPalette = _theme.Palette;
        Control icon = VectorIcons.Create(VectorIcons.Plus, 11, initialPalette.BrandPrimary);
        TextBlock labelTb = new()
        {
            Text = _i18n["Ai.BtnInjectContext"],
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel btnContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, labelTb }
        };

        Flyout flyout = new()
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard
        };

        StackPanel flyoutStack = new()
        {
            Spacing = 5,
            Width = 260
        };

        Border flyoutCard = SurfaceCard.Create(_theme, flyoutStack, new Thickness(10));
        flyout.Content = flyoutCard;

        (string PackId, string LabelKey, string IconName)[] packDefinitions =
        [
            ("selected_lead", "Ai.CtxSelectedLead", VectorIcons.SalesComment),
            ("top_hot_leads", "Ai.CtxTopHotLeads", VectorIcons.Search),
            ("deep_analytics", "Ai.CtxDeepAnalytics", VectorIcons.AiSparkle),
            ("all_groups_tags", "Ai.CtxAllGroupsAndTags", VectorIcons.Groups),
            ("templates_posts", "Ai.CtxTemplatesAndPosts", VectorIcons.Publish)
        ];

        void RebuildFlyoutItems()
        {
            ThemePalette p = _theme.Palette;
            flyoutStack.Children.Clear();

            for (int i = 0; i < packDefinitions.Length; i++)
            {
                (string packId, string labelKey, string iconName) = packDefinitions[i];
                bool isEnabled = _aiVm.IsContextPackEnabled(packId);

                Control itemIcon = VectorIcons.Create(
                    iconName,
                    13,
                    isEnabled ? p.BrandPrimary : p.TextSecondary);

                TextBlock itemLabel = SectionHeader.CreateLabel(
                    _theme,
                    _i18n[labelKey],
                    11.5,
                    isEnabled ? FontWeight.Bold : FontWeight.Medium);
                itemLabel.VerticalAlignment = VerticalAlignment.Center;
                itemLabel.TextTrimming = TextTrimming.CharacterEllipsis;

                TextBlock checkTb = new()
                {
                    Text = isEnabled ? "✓" : string.Empty,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = p.BrandPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid rowGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(itemIcon, 0);
                Grid.SetColumn(itemLabel, 1);
                Grid.SetColumn(checkTb, 2);
                rowGrid.Children.Add(itemIcon);
                rowGrid.Children.Add(itemLabel);
                rowGrid.Children.Add(checkTb);

                Button optionBtn = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6),
                    Background = isEnabled ? p.BrandSubtleFill : p.SecondarySurface,
                    BorderBrush = isEnabled ? p.BrandSubtleBorder : p.SubtleBorder,
                    Content = rowGrid,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                optionBtn.Click += (_, _) =>
                {
                    _aiVm.ToggleContextPack(packId);
                    RebuildFlyoutItems();
                };

                flyoutStack.Children.Add(optionBtn);
            }
        }

        flyout.Opening += (_, _) => RebuildFlyoutItems();

        Button btn = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3),
            MinHeight = 22,
            Content = btnContent,
            Flyout = flyout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        refreshTheme = () =>
        {
            ThemePalette p = _theme.Palette;
            btn.Background = p.BrandSubtleFill;
            btn.BorderBrush = p.BrandSubtleBorder;
            labelTb.Foreground = p.BrandPrimary;
            VectorIcons.SetForeground(icon, p.BrandPrimary);
        };

        refreshI18n = () => labelTb.Text = _i18n["Ai.BtnInjectContext"];
        refreshTheme();
        return btn;
    }

    private Button CreatePromptPresetsFlyoutButton(out Action refreshTheme, out Action refreshI18n)
    {
        ThemePalette initialPalette = _theme.Palette;
        Control icon = VectorIcons.Create(VectorIcons.AiSparkle, 11, initialPalette.TextSecondary);
        TextBlock labelTb = new()
        {
            Text = _i18n["Ai.BtnPromptPresets"],
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel btnContent = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, labelTb }
        };

        Flyout flyout = new()
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard
        };

        StackPanel flyoutStack = new()
        {
            Spacing = 5,
            Width = 260
        };

        Border flyoutCard = SurfaceCard.Create(_theme, flyoutStack, new Thickness(10));
        flyout.Content = flyoutCard;

        (string PresetKey, string LabelKey, string IconName)[] presets =
        [
            ("closing_expert", "Ai.PresetClosingExpert", VectorIcons.SalesComment),
            ("viral_copywriter", "Ai.PresetViralCopywriter", VectorIcons.Publish),
            ("data_analyst", "Ai.PresetDataAnalyst", VectorIcons.AiSparkle)
        ];

        void RebuildPresetItems()
        {
            ThemePalette p = _theme.Palette;
            flyoutStack.Children.Clear();

            for (int i = 0; i < presets.Length; i++)
            {
                (string presetKey, string labelKey, string iconName) = presets[i];
                Control itemIcon = VectorIcons.Create(iconName, 13, p.BrandPrimary);
                TextBlock itemLabel = SectionHeader.CreateLabel(_theme, _i18n[labelKey], 11.5, FontWeight.SemiBold);
                itemLabel.VerticalAlignment = VerticalAlignment.Center;
                itemLabel.TextWrapping = TextWrapping.Wrap;

                Grid rowGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(itemIcon, 0);
                Grid.SetColumn(itemLabel, 1);
                rowGrid.Children.Add(itemIcon);
                rowGrid.Children.Add(itemLabel);

                Button presetBtn = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6),
                    Background = p.SecondarySurface,
                    BorderBrush = p.SubtleBorder,
                    Content = rowGrid,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                presetBtn.Click += (_, _) =>
                {
                    flyout.Hide();
                    _aiVm.ApplySystemPromptPreset(presetKey);
                };

                flyoutStack.Children.Add(presetBtn);
            }
        }

        flyout.Opening += (_, _) => RebuildPresetItems();

        Button btn = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3),
            MinHeight = 22,
            Content = btnContent,
            Flyout = flyout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        refreshTheme = () =>
        {
            ThemePalette p = _theme.Palette;
            btn.Background = p.SecondarySurface;
            btn.BorderBrush = p.InteractiveBorder;
            labelTb.Foreground = p.TextSecondary;
            VectorIcons.SetForeground(icon, p.TextSecondary);
        };

        refreshI18n = () => labelTb.Text = _i18n["Ai.BtnPromptPresets"];
        refreshTheme();
        return btn;
    }

    private string FormatContextPackLabel(string packId) =>
        packId.ToLowerInvariant() switch
        {
            "selected_lead" => _i18n["Ai.CtxSelectedLead"],
            "top_hot_leads" => _i18n["Ai.CtxTopHotLeads"],
            "deep_analytics" => _i18n["Ai.CtxDeepAnalytics"],
            "all_groups_tags" => _i18n["Ai.CtxAllGroupsAndTags"],
            "templates_posts" => _i18n["Ai.CtxTemplatesAndPosts"],
            _ => packId
        };

    private Border BuildWelcomeCard(out Action refreshTheme, out Action refreshI18n)
    {
        StackPanel stack = new()
        {
            Spacing = 8
        };

        StackPanel titleRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control welcomeIcon = VectorIcons.CreateAiAssistantIcon(16);
        TextBlock welcomeTitleTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Ai.WelcomeTitle"],
            13,
            FontWeight.Bold);
        titleRow.Children.Add(welcomeIcon);
        titleRow.Children.Add(welcomeTitleTb);

        StackPanel bulletsStack = new()
        {
            Spacing = 4
        };

        TextBlock b1 = SectionHeader.CreateLabel(_theme, _i18n["Ai.WelcomeBullet1"], 12, FontWeight.Normal, isSecondary: true);
        TextBlock b2 = SectionHeader.CreateLabel(_theme, _i18n["Ai.WelcomeBullet2"], 12, FontWeight.Normal, isSecondary: true);
        TextBlock b3 = SectionHeader.CreateLabel(_theme, _i18n["Ai.WelcomeBullet3"], 12, FontWeight.Normal, isSecondary: true);
        TextBlock b4 = SectionHeader.CreateLabel(_theme, _i18n["Ai.WelcomeBullet4"], 12, FontWeight.Normal, isSecondary: true);
        bulletsStack.Children.Add(b1);
        bulletsStack.Children.Add(b2);
        bulletsStack.Children.Add(b3);
        bulletsStack.Children.Add(b4);

        WrapPanel chipsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
            Margin = new Thickness(0, 4, 0, 0)
        };

        Button chipPublish = this.CreateQuickActionChip(_i18n["Ai.QuickChipPublish"], VectorIcons.Publish);
        Button chipFindGroups = this.CreateQuickActionChip(_i18n["Ai.QuickChipFindGroups"], VectorIcons.Groups);
        Button chipScanLeads = this.CreateQuickActionChip(_i18n["Ai.QuickChipScanLeads"], VectorIcons.Search);
        Button chipBrainSummary = this.CreateQuickActionChip(_i18n["Ai.QuickChipBrainSummary"], VectorIcons.AiContext);

        chipsWrap.Children.Add(chipPublish);
        chipsWrap.Children.Add(chipFindGroups);
        chipsWrap.Children.Add(chipScanLeads);
        chipsWrap.Children.Add(chipBrainSummary);

        stack.Children.Add(titleRow);
        stack.Children.Add(bulletsStack);
        stack.Children.Add(chipsWrap);

        refreshTheme = () => VectorIcons.SetForeground(welcomeIcon, _theme.Palette.BrandPrimary);

        refreshI18n = () =>
        {
            welcomeTitleTb.Text = _i18n["Ai.WelcomeTitle"];
            b1.Text = _i18n["Ai.WelcomeBullet1"];
            b2.Text = _i18n["Ai.WelcomeBullet2"];
            b3.Text = _i18n["Ai.WelcomeBullet3"];
            b4.Text = _i18n["Ai.WelcomeBullet4"];
            AppButton.SetText(chipPublish, _i18n["Ai.QuickChipPublish"]);
            AppButton.SetText(chipFindGroups, _i18n["Ai.QuickChipFindGroups"]);
            AppButton.SetText(chipScanLeads, _i18n["Ai.QuickChipScanLeads"]);
            AppButton.SetText(chipBrainSummary, _i18n["Ai.QuickChipBrainSummary"]);
        };

        return SurfaceCard.CreateInset(_theme, stack, new Thickness(12));
    }

    private Button CreateQuickActionChip(string initialText, string iconSvg)
    {
        Button chip = AppButton.CreateSecondary(_theme, initialText, command: null, iconSvg: iconSvg);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.Padding = new Thickness(9, 5);
        chip.CornerRadius = new CornerRadius(999);
        chip.Click += (_, _) =>
        {
            string promptText = chip.Tag as string ?? AutomationProperties.GetName(chip) ?? initialText;
            _aiVm.InputPromptText = promptText;
            if (_aiVm.SendMessageCommand.CanExecute(null))
            {
                _aiVm.SendMessageCommand.Execute(null);
            }
        };
        return chip;
    }

    private Border CreateChatBubbleCard(
        AiChatBubbleItem item,
        out SelectableTextBlock contentTb,
        out Action refreshBubble)
    {
        bool isUser = item.IsUser;

        StackPanel bubbleStack = new()
        {
            Spacing = 6
        };

        Grid metaRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        ContentControl userAvatarHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        Control roleIcon = VectorIcons.CreateAiAssistantIcon(16);

        Border roleBadge = new()
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = roleIcon
        };

        TextBlock roleNameTb = new()
        {
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock timeTb = new()
        {
            Text = item.TimestampText,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control leftRoleControl = isUser ? userAvatarHost : roleBadge;
        Grid.SetColumn(leftRoleControl, 0);
        Grid.SetColumn(roleNameTb, 1);
        Grid.SetColumn(timeTb, 2);
        metaRow.Children.Add(leftRoleControl);
        metaRow.Children.Add(roleNameTb);
        metaRow.Children.Add(timeTb);

        SelectableTextBlock innerContentTb = new()
        {
            FontSize = 12.5,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap
        };
        contentTb = innerContentTb;

        Border actionCard = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4, 0, 0),
            IsVisible = item.HasProposedAction
        };

        StackPanel actionStack = new()
        {
            Spacing = 8
        };

        TextBlock actionSummaryTb = new()
        {
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        Grid actionButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 6
        };

        Button confirmActionBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Ai.BtnConfirmAction"],
            iconSvg: VectorIcons.CheckCircle);
        confirmActionBtn.Padding = new Thickness(8, 5);
        confirmActionBtn.Click += (_, _) => ExecuteBubbleCommand(_aiVm.ExecuteProposedActionCommand, item);

        Button editActionBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnEditAction"],
            iconSvg: VectorIcons.Edit);
        editActionBtn.Padding = new Thickness(8, 5);
        editActionBtn.Click += (_, _) => ExecuteBubbleCommand(_aiVm.EditProposedActionCommand, item);

        Grid.SetColumn(confirmActionBtn, 0);
        Grid.SetColumn(editActionBtn, 1);
        actionButtonsRow.Children.Add(confirmActionBtn);
        actionButtonsRow.Children.Add(editActionBtn);

        Border executedBadge = StatusPill.Create(
            _theme,
            _i18n["Ai.ActionExecutedBadge"],
            AuditSeverity.Success);

        actionStack.Children.Add(actionSummaryTb);
        actionStack.Children.Add(actionButtonsRow);
        actionStack.Children.Add(executedBadge);
        actionCard.Child = actionStack;

        WrapPanel assistantActionsRow = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 5,
            LineSpacing = 4,
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = !isUser && !item.IsLoading && !string.IsNullOrWhiteSpace(item.Content) && !string.Equals(item.Content, "...", StringComparison.Ordinal)
        };

        Button copyReplyBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnCopyReply"],
            command: null,
            iconSvg: VectorIcons.Clipboard);
        copyReplyBtn.Padding = new Thickness(7, 3);
        copyReplyBtn.MinHeight = 24;
        copyReplyBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && !string.IsNullOrWhiteSpace(item.Content))
            {
                await clipboard.SetTextAsync(item.Content).ConfigureAwait(true);
                AppButton.SetText(copyReplyBtn, _i18n["Ai.BtnCopiedReply"]);
            }
        };

        Button toPublisherBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnToPublisher"],
            command: null,
            iconSvg: VectorIcons.Publish);
        toPublisherBtn.Padding = new Thickness(7, 3);
        toPublisherBtn.MinHeight = 24;
        toPublisherBtn.Click += (_, _) => _aiVm.SendTextToPublisherComposer(item.Content);

        Button toLeadCommentBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnToLeadComment"],
            command: null,
            iconSvg: VectorIcons.SalesComment);
        toLeadCommentBtn.Padding = new Thickness(7, 3);
        toLeadCommentBtn.MinHeight = 24;
        toLeadCommentBtn.Click += (_, _) => _aiVm.SendTextToSalesCommentComposer(item.Content);

        assistantActionsRow.Children.Add(copyReplyBtn);
        assistantActionsRow.Children.Add(toPublisherBtn);
        assistantActionsRow.Children.Add(toLeadCommentBtn);

        bubbleStack.Children.Add(metaRow);
        bubbleStack.Children.Add(innerContentTb);
        bubbleStack.Children.Add(assistantActionsRow);
        bubbleStack.Children.Add(actionCard);

        Border bubbleBorder = new()
        {
            MaxWidth = 316,
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            CornerRadius = isUser ? new CornerRadius(12, 12, 3, 12) : new CornerRadius(12, 12, 12, 3),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9),
            Child = bubbleStack
        };

        refreshBubble = () =>
        {
            ThemePalette p = _theme.Palette;
            bubbleBorder.Background = isUser ? p.BrandSubtleFill : p.SecondarySurface;
            bubbleBorder.BorderBrush = isUser ? p.BrandSubtleBorder : p.SubtleBorder;
            if (isUser)
            {
                AccountProfile? acc = _vm.SelectedAccount ?? (_vm.Accounts.Count > 0 ? _vm.Accounts[0] : null);
                string userDisplayName = acc?.DisplayName ?? "FB";
                string? userAvatarUrl = acc?.AvatarUrl;
                if (string.IsNullOrWhiteSpace(userAvatarUrl) && acc is not null && !string.IsNullOrWhiteSpace(acc.Uid))
                {
                    bool isNumericUid = true;
                    for (int i = 0; i < acc.Uid.Length; i++)
                    {
                        if (!char.IsDigit(acc.Uid[i]))
                        {
                            isNumericUid = false;
                            break;
                        }
                    }

                    if (isNumericUid)
                    {
                        userAvatarUrl = $"https://graph.facebook.com/{acc.Uid}/picture?type=normal";
                    }
                }

                userAvatarHost.Content = AvatarBadge.Create(_theme, userDisplayName, 24, userAvatarUrl);
                roleNameTb.Text = acc?.DisplayName ?? string.Empty;
                roleNameTb.Foreground = p.TextPrimary;
            }
            else
            {
                roleBadge.Background = p.BrandSubtleFill;
                roleBadge.BorderBrush = p.SubtleBorder;
                VectorIcons.SetForeground(roleIcon, p.TextSecondary);
                roleNameTb.Text = string.Empty;
            }

            timeTb.Foreground = p.TextMuted;
            innerContentTb.Foreground = p.TextPrimary;
            RenderMarkdownIntoControl(innerContentTb, item.Content, p);
            timeTb.Text = item.TimestampText;

            bool hasAction = item.HasProposedAction && item.ProposedAction is not null;
            actionCard.IsVisible = hasAction;
            actionCard.Background = p.ElevatedSurface;
            actionCard.BorderBrush = p.InteractiveBorder;

            if (hasAction && item.ProposedAction is { } proposed)
            {
                actionSummaryTb.Text = proposed.SummaryText;
                actionSummaryTb.Foreground = p.BrandPrimary;
                actionSummaryTb.IsVisible = !string.IsNullOrWhiteSpace(proposed.SummaryText);

                string confirmLabel = (proposed.ActionKind ?? string.Empty).ToUpperInvariant() switch
                {
                    "FILTER_LEADS" => _i18n["Ai.ActionFilterLeads"],
                    "COMMENT_LEAD" => _i18n["Ai.ActionCommentLead"],
                    "OPEN_GROUP_TAGS" => _i18n["Ai.ActionOpenGroupTags"],
                    "BUMP_POSTS" => _i18n["Ai.ActionBumpPosts"],
                    "SEARCH_GROUPS" => _i18n["Ai.QuickChipFindGroups"],
                    "SCAN_LEADS" => _i18n["Ai.QuickChipScanLeads"],
                    _ => _i18n["Ai.BtnConfirmAction"]
                };

                AppButton.SetText(confirmActionBtn, confirmLabel);
                AppButton.SetText(editActionBtn, _i18n["Ai.BtnEditAction"]);
                StatusPill.SetText(executedBadge, _i18n["Ai.ActionExecutedBadge"]);
            }

            actionButtonsRow.IsVisible = hasAction && !item.IsActionExecuted;
            executedBadge.IsVisible = hasAction && item.IsActionExecuted;
            assistantActionsRow.IsVisible = !isUser &&
                                            !item.IsLoading &&
                                            !string.IsNullOrWhiteSpace(item.Content) &&
                                            !string.Equals(item.Content, "...", StringComparison.Ordinal);
            AppButton.SetText(copyReplyBtn, _i18n["Ai.BtnCopyReply"]);
            AppButton.SetText(toPublisherBtn, _i18n["Ai.BtnToPublisher"]);
            AppButton.SetText(toLeadCommentBtn, _i18n["Ai.BtnToLeadComment"]);
        };

        refreshBubble();
        return bubbleBorder;
    }

    private static void RenderMarkdownIntoControl(SelectableTextBlock targetTb, string? markdown, ThemePalette p)
    {
        InlineCollection? inlines = targetTb.Inlines;
        if (inlines is null)
        {
            targetTb.Text = markdown ?? string.Empty;
            return;
        }

        targetTb.Text = null;
        inlines.Clear();

        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        bool inCodeBlock = false;
        bool hasRenderedLine = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string rawLine = lines[i];
            string trimmedStart = rawLine.TrimStart();

            if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (hasRenderedLine)
            {
                inlines.Add(new LineBreak());
            }

            hasRenderedLine = true;

            if (inCodeBlock)
            {
                inlines.Add(new Run(rawLine)
                {
                    FontFamily = s_monospaceFont,
                    Foreground = p.BrandPrimary,
                    FontSize = 11.5
                });
                continue;
            }

            if (trimmedStart.StartsWith("### ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[4..], p, isHeading: true);
                continue;
            }

            if (trimmedStart.StartsWith("## ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[3..], p, isHeading: true);
                continue;
            }

            if (trimmedStart.StartsWith("# ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[2..], p, isHeading: true);
                continue;
            }

            string lineToParse = rawLine;
            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("* ", StringComparison.Ordinal))
            {
                lineToParse = string.Concat("• ", trimmedStart[2..]);
            }

            AppendInlineMarkdownRuns(inlines, lineToParse, p, isHeading: false);
        }

        if (inlines.Count == 0 && !string.IsNullOrEmpty(markdown))
        {
            inlines.Add(new Run(markdown));
        }
    }

    private static void AppendInlineMarkdownRuns(
        InlineCollection inlines,
        string line,
        ThemePalette p,
        bool isHeading)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        int idx = 0;
        while (idx < line.Length)
        {
            int nextCode = FindValidSpanStart(line, idx, "`", "`", out int codeEnd);
            int nextBold = FindValidSpanStart(line, idx, "**", "**", out int boldEnd);
            int nextItalic = FindValidSingleAsteriskStart(line, idx, out int italicEnd);

            int earliestPos = int.MaxValue;
            int kind = 0; // 1 = code, 2 = bold, 3 = italic
            int spanEnd = -1;

            if (nextCode >= 0 && nextCode < earliestPos)
            {
                earliestPos = nextCode;
                kind = 1;
                spanEnd = codeEnd;
            }

            if (nextBold >= 0 && nextBold < earliestPos)
            {
                earliestPos = nextBold;
                kind = 2;
                spanEnd = boldEnd;
            }

            if (nextItalic >= 0 && nextItalic < earliestPos)
            {
                earliestPos = nextItalic;
                kind = 3;
                spanEnd = italicEnd;
            }

            if (kind == 0)
            {
                AddPlainRun(inlines, line[idx..], isHeading);
                break;
            }

            if (earliestPos > idx)
            {
                AddPlainRun(inlines, line[idx..earliestPos], isHeading);
            }

            if (kind == 1)
            {
                string inner = line[(earliestPos + 1)..spanEnd];
                Run codeRun = new(inner)
                {
                    FontFamily = s_monospaceFont,
                    Foreground = p.BrandPrimary,
                    FontWeight = FontWeight.SemiBold
                };
                if (isHeading)
                {
                    codeRun.FontSize = 13.5;
                }

                inlines.Add(codeRun);
                idx = spanEnd + 1;
            }
            else if (kind == 2)
            {
                string inner = line[(earliestPos + 2)..spanEnd];
                Run boldRun = new(inner)
                {
                    FontWeight = FontWeight.Bold
                };
                if (isHeading)
                {
                    boldRun.FontSize = 13.5;
                }

                inlines.Add(boldRun);
                idx = spanEnd + 2;
            }
            else
            {
                string inner = line[(earliestPos + 1)..spanEnd];
                Run italicRun = new(inner)
                {
                    FontStyle = FontStyle.Italic
                };
                if (isHeading)
                {
                    italicRun.FontWeight = FontWeight.Bold;
                    italicRun.FontSize = 13.5;
                }

                inlines.Add(italicRun);
                idx = spanEnd + 1;
            }
        }
    }

    private static void AddPlainRun(InlineCollection inlines, string text, bool isHeading)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Run run = new(text);
        if (isHeading)
        {
            run.FontWeight = FontWeight.Bold;
            run.FontSize = 13.5;
        }

        inlines.Add(run);
    }

    private static int FindValidSpanStart(
        string text,
        int startIndex,
        string openDelimiter,
        string closeDelimiter,
        out int closePos)
    {
        int openPos = text.IndexOf(openDelimiter, startIndex, StringComparison.Ordinal);
        while (openPos >= 0)
        {
            int contentStart = openPos + openDelimiter.Length;
            if (contentStart < text.Length)
            {
                int foundClose = text.IndexOf(closeDelimiter, contentStart, StringComparison.Ordinal);
                if (foundClose > contentStart)
                {
                    closePos = foundClose;
                    return openPos;
                }
            }

            openPos = text.IndexOf(openDelimiter, openPos + 1, StringComparison.Ordinal);
        }

        closePos = -1;
        return -1;
    }

    private static int FindValidSingleAsteriskStart(string text, int startIndex, out int closePos)
    {
        int i = startIndex;
        while (i < text.Length)
        {
            int starPos = text.IndexOf('*', i);
            if (starPos < 0)
            {
                break;
            }

            bool isDouble = (starPos + 1 < text.Length && text[starPos + 1] == '*') ||
                            (starPos > 0 && text[starPos - 1] == '*');
            if (isDouble || starPos + 1 >= text.Length || char.IsWhiteSpace(text[starPos + 1]))
            {
                i = starPos + 1;
                continue;
            }

            int searchClose = starPos + 1;
            while (searchClose < text.Length)
            {
                int endStar = text.IndexOf('*', searchClose);
                if (endStar < 0)
                {
                    break;
                }

                bool endIsDouble = (endStar + 1 < text.Length && text[endStar + 1] == '*') ||
                                   (endStar > 0 && text[endStar - 1] == '*');
                if (!endIsDouble && !char.IsWhiteSpace(text[endStar - 1]))
                {
                    closePos = endStar;
                    return starPos;
                }

                searchClose = endStar + 1;
            }

            i = starPos + 1;
        }

        closePos = -1;
        return -1;
    }

    private static void ExecuteBubbleCommand(ICommand command, AiChatBubbleItem bubble)
    {
        if (command.CanExecute(bubble))
        {
            command.Execute(bubble);
        }
        else if (bubble.ProposedAction is not null && command.CanExecute(bubble.ProposedAction))
        {
            command.Execute(bubble.ProposedAction);
        }
    }
}
