// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.AiAssistant;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Components;

/// <summary>
/// Top-right shell context bar component (<c>TopAccountSwitcherBar.cs</c>) hosting the active task stop button,
/// active account badge / multi-account switcher ComboBox with pulsing status indicator, and AI Assistant toggle button.
/// </summary>
public sealed class TopAccountSwitcherBar : StackPanel
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopAccountSwitcherBar"/> class.
    /// </summary>
    public TopAccountSwitcherBar(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _vm = viewModel;
        _theme = viewModel.Context.Theme;
        _i18n = viewModel.Context.Localization;

        this.Orientation = Orientation.Horizontal;
        this.Spacing = 8;
        this.VerticalAlignment = VerticalAlignment.Top;
        this.HorizontalAlignment = HorizontalAlignment.Right;

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
                Background = ThemeLifecycle.GetCachedBrush(dotColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border statusDotHalo = new()
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(999),
                Background = ThemeLifecycle.GetCachedBrush(haloColor),
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

        this.Children.Add(stopTaskBtn);
        this.Children.Add(singleAccBadge);
        this.Children.Add(activeAccCombo);

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
            Cursor = new Cursor(StandardCursorType.Hand)
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

        this.Children.Add(aiToggleBtn);

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
    }
}
