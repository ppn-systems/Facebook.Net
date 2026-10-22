// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
using Meta.UI.Core.Audit;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.AiAssistant;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Components;

public sealed partial class AiAssistantPanel
{
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
                        MarkdownRenderer.Render(contentTb, item.Content, _theme.Palette);
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

        Border executedBadge = StatusBadge.Create(
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
            MarkdownRenderer.Render(innerContentTb, item.Content, p);
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
                StatusBadge.SetText(executedBadge, _i18n["Ai.ActionExecutedBadge"]);
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
