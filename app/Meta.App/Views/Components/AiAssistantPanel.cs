// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.AiAssistant;
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
public sealed partial class AiAssistantPanel : Border
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
        ThemeLifecycle.BindLanguage(this, _i18n, () =>
        {
            refreshTopBarI18n();
            refreshInstallI18n();
            refreshAuthI18n();
            refreshChatI18n();
            RefreshAllVisuals();
        });

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
}
