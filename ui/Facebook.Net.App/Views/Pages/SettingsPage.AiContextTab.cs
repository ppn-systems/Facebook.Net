// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.AiAssistant;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Dedicated AI Assistant &amp; Context Memory configuration tab (<c>SettingsPage.AiContextTab.cs</c>).
/// Provides a 2-column layout for managing the persistent System Prompt and categorized <see cref="AiMemoryRecord"/> entries.
/// </summary>
public sealed partial class SettingsPage
{
    private static readonly AiMemoryCategory[] s_aiMemoryCategories = Enum.GetValues<AiMemoryCategory>();

    private Control BuildAiContextTab(out Action refreshI18n)
    {
        AiAssistantViewModel aiVm = _vm.AiAssistant;

        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("420,*"),
            ColumnSpacing = 14
        };

        // ───────────────────────────── Left Column: System Prompt & Add Context Memory ─────────────────────────────
        StackPanel leftStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // 1. AI Model, Reasoning Effort & Unlimited Custom User Context Section
        Grid sysHeader = SectionHeader.Create(
            _theme,
            _i18n["Ai.SystemPromptHeaderTitle"],
            _i18n["Ai.SystemPromptHeaderDesc"],
            out TextBlock sysTitleTb,
            out TextBlock sysDescTb,
            showInfoButton: true);

        Grid modelEffortRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.45*,1*"),
            ColumnSpacing = 10
        };

        AgyModelOption initialModelOption = aiVm.AvailableModels[0];
        for (int i = 0; i < aiVm.AvailableModels.Count; i++)
        {
            if (string.Equals(aiVm.AvailableModels[i].Id, aiVm.SelectedAiModel, StringComparison.OrdinalIgnoreCase))
            {
                initialModelOption = aiVm.AvailableModels[i];
                break;
            }
        }

        ComboBox modelCombo = FormField.CreateComboBox<AgyModelOption>(
            _theme,
            aiVm.AvailableModels,
            opt => SectionHeader.CreateLabel(_theme, opt.DisplayName, 12, FontWeight.Medium),
            selectedItem: initialModelOption);

        modelCombo.SelectionChanged += (_, _) =>
        {
            if (modelCombo.SelectedItem is AgyModelOption selectedModel &&
                !string.Equals(aiVm.SelectedAiModel, selectedModel.Id, StringComparison.Ordinal))
            {
                aiVm.SelectedAiModel = selectedModel.Id;
            }
        };

        StackPanel modelField = FormField.CreateLabeled(
            _theme,
            _i18n["Ai.ModelLabel"],
            modelCombo,
            out TextBlock modelLabelTb);

        ComboBox effortCombo = FormField.CreateComboBox<string>(
            _theme,
            aiVm.AvailableEffortLevels,
            eff => SectionHeader.CreateLabel(_theme, eff.ToUpperInvariant(), 12, FontWeight.SemiBold),
            selectedItem: aiVm.SelectedAiEffort);

        effortCombo.SelectionChanged += (_, _) =>
        {
            if (effortCombo.SelectedItem is string selectedEffort &&
                !string.Equals(aiVm.SelectedAiEffort, selectedEffort, StringComparison.OrdinalIgnoreCase))
            {
                aiVm.SelectedAiEffort = selectedEffort;
            }
        };

        StackPanel effortField = FormField.CreateLabeled(
            _theme,
            _i18n["Ai.EffortLabel"],
            effortCombo,
            out TextBlock effortLabelTb);

        Grid.SetColumn(modelField, 0);
        Grid.SetColumn(effortField, 1);
        modelEffortRow.Children.Add(modelField);
        modelEffortRow.Children.Add(effortField);

        TextBox sysPromptBox = FormField.CreateTextBox(
            _theme,
            _i18n["Ai.SystemPromptPlaceholder"],
            aiVm.SystemPromptText,
            multiline: true,
            height: 130);

        bool updatingSysPrompt = false;
        sysPromptBox.TextChanged += (_, _) =>
        {
            if (!updatingSysPrompt)
            {
                aiVm.SystemPromptText = sysPromptBox.Text ?? string.Empty;
            }
        };

        Grid sysButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1*,Auto"),
            ColumnSpacing = 8
        };

        Button saveSysPromptBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Ai.BtnSaveSystemPrompt"],
            aiVm.SaveSystemPromptCommand,
            VectorIcons.CheckCircle);

        Button resetSysPromptBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnResetSystemPrompt"],
            aiVm.ResetSystemPromptCommand,
            VectorIcons.Sync);

        Button presetsBtn = this.CreateSettingsPromptPresetsFlyoutButton(aiVm, out Action refreshPresetsBtnI18n);

        Grid.SetColumn(saveSysPromptBtn, 0);
        Grid.SetColumn(resetSysPromptBtn, 1);
        Grid.SetColumn(presetsBtn, 2);
        sysButtonsRow.Children.Add(saveSysPromptBtn);
        sysButtonsRow.Children.Add(resetSysPromptBtn);
        sysButtonsRow.Children.Add(presetsBtn);

        // 2. Separator
        Border sectionDivider = new()
        {
            Height = 1,
            Background = _theme.Palette.SubtleBorder,
            Margin = new Thickness(0, 2)
        };
        ThemeLifecycle.Bind(sectionDivider, _theme, () => sectionDivider.Background = _theme.Palette.SubtleBorder);

        // 3. Add Context Memory Form
        Grid memHeader = SectionHeader.Create(
            _theme,
            _i18n["Ai.BrainHeaderTitle"],
            _i18n["Ai.BrainHeaderDesc"],
            out TextBlock memTitleTb,
            out TextBlock memDescTb,
            showInfoButton: true);

        ComboBox categoryCombo = FormField.CreateComboBox<AiMemoryCategory>(
            _theme,
            s_aiMemoryCategories,
            cat =>
            {
                TextBlock tb = SectionHeader.CreateLabel(_theme, this.FormatCategoryLabel(cat), 12, FontWeight.Medium);
                _i18n.LanguageChanged += (_, _) => tb.Text = this.FormatCategoryLabel(cat);
                return tb;
            },
            selectedItem: aiVm.NewMemoryCategory);

        categoryCombo.SelectionChanged += (_, _) =>
        {
            if (categoryCombo.SelectedItem is AiMemoryCategory selectedCat &&
                aiVm.NewMemoryCategory != selectedCat)
            {
                aiVm.NewMemoryCategory = selectedCat;
            }
        };

        StackPanel categoryField = FormField.CreateLabeled(
            _theme,
            _i18n["Ai.BrainCategoryLabel"],
            categoryCombo,
            out TextBlock categoryLabelTb);

        TextBox memKeyBox = FormField.CreateTextBox(
            _theme,
            _i18n["Ai.BrainKeyPlaceholder"],
            aiVm.NewMemoryKey);

        bool updatingKey = false;
        memKeyBox.TextChanged += (_, _) =>
        {
            if (!updatingKey)
            {
                aiVm.NewMemoryKey = memKeyBox.Text ?? string.Empty;
            }
        };

        TextBox memContentBox = FormField.CreateTextBox(
            _theme,
            _i18n["Ai.BrainContentPlaceholder"],
            aiVm.NewMemoryContent,
            multiline: true,
            height: 76);

        bool updatingContent = false;
        memContentBox.TextChanged += (_, _) =>
        {
            if (!updatingContent)
            {
                aiVm.NewMemoryContent = memContentBox.Text ?? string.Empty;
            }
        };

        Button addMemoryBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Ai.BtnAddMemory"],
            aiVm.SaveMemoryCommand,
            VectorIcons.Plus);

        leftStack.Children.Add(sysHeader);
        leftStack.Children.Add(modelEffortRow);
        leftStack.Children.Add(sysPromptBox);
        leftStack.Children.Add(sysButtonsRow);
        leftStack.Children.Add(sectionDivider);
        leftStack.Children.Add(memHeader);
        leftStack.Children.Add(categoryField);
        leftStack.Children.Add(memKeyBox);
        leftStack.Children.Add(memContentBox);
        leftStack.Children.Add(addMemoryBtn);

        Border leftCard = SurfaceCard.Create(
            _theme,
            new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = leftStack
            },
            new Thickness(18));

        // ───────────────────────────── Right Column: Stored AI Context Memory List with SearchFilterBar ─────────────────────────────
        Grid rightGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12
        };

        AiMemoryCategory? activeCategoryFilter = null;
        SearchFilterBar memorySearchBar = new(
            _theme,
            _i18n["Common.FilterButton"],
            _i18n["Ai.BrainSearchPlaceholder"]);

        Button clearAllMemoriesBtn = AppButton.CreateDanger(
            _theme,
            _i18n["Ai.BtnClearAllMemories"],
            aiVm.ClearAllMemoriesCommand,
            VectorIcons.Trash);
        clearAllMemoriesBtn.VerticalAlignment = VerticalAlignment.Center;

        Grid rightHeaderBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(memorySearchBar, 0);
        Grid.SetColumn(clearAllMemoriesBtn, 1);
        rightHeaderBar.Children.Add(memorySearchBar);
        rightHeaderBar.Children.Add(clearAllMemoriesBtn);

        StackPanel memoriesListStack = new()
        {
            Spacing = 8
        };

        ScrollViewer memoriesScroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = memoriesListStack
        };

        void RebuildFilteredMemories()
        {
            memoriesListStack.Children.Clear();
            clearAllMemoriesBtn.IsVisible = aiVm.Memories.Count > 0;
            string query = (memorySearchBar.SearchTextBox.Text ?? string.Empty).Trim();

            int renderedCount = 0;
            for (int i = 0; i < aiVm.Memories.Count; i++)
            {
                AiMemoryRecord entry = aiVm.Memories[i];
                if (activeCategoryFilter.HasValue && entry.Category != activeCategoryFilter.Value)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(query) &&
                    !entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                memoriesListStack.Children.Add(this.CreateAiMemoryCard(entry));
                renderedCount++;
            }

            if (renderedCount == 0)
            {
                Border emptyBanner = EmptyStateBanner.Create(
                    _theme,
                    _i18n["Ai.BrainEmptyHint"],
                    VectorIcons.AiContext,
                    out _);
                memoriesListStack.Children.Add(emptyBanner);
            }
        }

        FilterCategoryDefinition categoryFilterDef = new(
            "category",
            _i18n["Ai.BrainCategoryLabel"],
            VectorIcons.AiContext,
            () =>
            {
                List<FilterOptionItem> options = new(s_aiMemoryCategories.Length);
                for (int i = 0; i < s_aiMemoryCategories.Length; i++)
                {
                    AiMemoryCategory cat = s_aiMemoryCategories[i];
                    options.Add(new FilterOptionItem(cat.ToString(), this.FormatCategoryLabel(cat)));
                }

                return options;
            },
            selectedIds =>
            {
                if (selectedIds.Count > 0 && Enum.TryParse(selectedIds[0], ignoreCase: true, out AiMemoryCategory parsed))
                {
                    activeCategoryFilter = parsed;
                }
                else
                {
                    activeCategoryFilter = null;
                }

                RebuildFilteredMemories();
            },
            allowMultiple: false);

        memorySearchBar.SetFilterCategories(
            [categoryFilterDef],
            _i18n["Common.SearchInList"],
            _i18n["Common.BtnApplyFilter"]);
        memorySearchBar.SearchTextBox.TextChanged += (_, _) => RebuildFilteredMemories();
        memorySearchBar.SearchButton.Click += (_, _) => RebuildFilteredMemories();

        Grid.SetRow(rightHeaderBar, 0);
        Grid.SetRow(memoriesScroll, 1);
        rightGrid.Children.Add(rightHeaderBar);
        rightGrid.Children.Add(memoriesScroll);

        Border rightCard = SurfaceCard.Create(_theme, rightGrid, new Thickness(18));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightCard);

        RebuildFilteredMemories();
        ThemeLifecycle.Bind(rightCard, _theme, RebuildFilteredMemories);

        aiVm.Memories.CollectionChanged += (_, _) => RebuildFilteredMemories();

        aiVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.SystemPromptText), StringComparison.Ordinal))
            {
                if (!string.Equals(sysPromptBox.Text, aiVm.SystemPromptText, StringComparison.Ordinal))
                {
                    updatingSysPrompt = true;
                    sysPromptBox.Text = aiVm.SystemPromptText;
                    updatingSysPrompt = false;
                }
            }
            else if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.NewMemoryCategory), StringComparison.Ordinal))
            {
                if (categoryCombo.SelectedItem is not AiMemoryCategory current ||
                    current != aiVm.NewMemoryCategory)
                {
                    categoryCombo.SelectedItem = aiVm.NewMemoryCategory;
                }
            }
            else if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.NewMemoryKey), StringComparison.Ordinal))
            {
                if (!string.Equals(memKeyBox.Text, aiVm.NewMemoryKey, StringComparison.Ordinal))
                {
                    updatingKey = true;
                    memKeyBox.Text = aiVm.NewMemoryKey;
                    updatingKey = false;
                }
            }
            else if (string.Equals(e.PropertyName, nameof(AiAssistantViewModel.NewMemoryContent), StringComparison.Ordinal))
            {
                if (!string.Equals(memContentBox.Text, aiVm.NewMemoryContent, StringComparison.Ordinal))
                {
                    updatingContent = true;
                    memContentBox.Text = aiVm.NewMemoryContent;
                    updatingContent = false;
                }
            }
        };

        refreshI18n = () =>
        {
            sysTitleTb.Text = _i18n["Ai.SystemPromptHeaderTitle"];
            sysDescTb.Text = _i18n["Ai.SystemPromptHeaderDesc"];
            modelLabelTb.Text = _i18n["Ai.ModelLabel"];
            effortLabelTb.Text = _i18n["Ai.EffortLabel"];
            sysPromptBox.PlaceholderText = _i18n["Ai.SystemPromptPlaceholder"];
            AppButton.SetText(saveSysPromptBtn, _i18n["Ai.BtnSaveSystemPrompt"]);
            AppButton.SetText(resetSysPromptBtn, _i18n["Ai.BtnResetSystemPrompt"]);
            refreshPresetsBtnI18n();
            AppButton.SetText(clearAllMemoriesBtn, _i18n["Ai.BtnClearAllMemories"]);

            memTitleTb.Text = _i18n["Ai.BrainHeaderTitle"];
            memDescTb.Text = _i18n["Ai.BrainHeaderDesc"];
            categoryLabelTb.Text = _i18n["Ai.BrainCategoryLabel"];
            memKeyBox.PlaceholderText = _i18n["Ai.BrainKeyPlaceholder"];
            memContentBox.PlaceholderText = _i18n["Ai.BrainContentPlaceholder"];
            AppButton.SetText(addMemoryBtn, _i18n["Ai.BtnAddMemory"]);

            categoryFilterDef.Title = _i18n["Ai.BrainCategoryLabel"];
            memorySearchBar.UpdateLocalization(
                _i18n["Common.FilterButton"],
                _i18n["Ai.BrainSearchPlaceholder"],
                _i18n["Common.SearchInList"],
                _i18n["Common.BtnApplyFilter"]);

            RebuildFilteredMemories();
        };

        return splitGrid;
    }

    private Border CreateAiMemoryCard(AiMemoryRecord entry)
    {
        StackPanel cardStack = new()
        {
            Spacing = 6
        };

        Grid topRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border categoryPill = StatusPill.Create(
            _theme,
            this.FormatCategoryLabel(entry.Category),
            AuditSeverity.Info);

        TextBlock timestampTb = SectionHeader.CreateLabel(
            _theme,
            entry.UpdatedAtUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture),
            11,
            FontWeight.Normal,
            isSecondary: true);
        timestampTb.HorizontalAlignment = HorizontalAlignment.Right;

        Button deleteBtn = AppButton.CreateDanger(
            _theme,
            _i18n["Ai.BtnDeleteMemory"],
            command: null,
            iconSvg: VectorIcons.Trash);
        deleteBtn.Padding = new Thickness(9, 4);
        deleteBtn.HorizontalAlignment = HorizontalAlignment.Right;
        deleteBtn.Click += (_, _) =>
        {
            ICommand cmd = _vm.AiAssistant.DeleteMemoryCommand;
            if (cmd.CanExecute(entry))
            {
                cmd.Execute(entry);
            }
            else if (cmd.CanExecute(entry.Id))
            {
                cmd.Execute(entry.Id);
            }
        };

        Grid.SetColumn(categoryPill, 0);
        Grid.SetColumn(timestampTb, 1);
        Grid.SetColumn(deleteBtn, 2);
        topRow.Children.Add(categoryPill);
        topRow.Children.Add(timestampTb);
        topRow.Children.Add(deleteBtn);

        TextBlock keyTb = SectionHeader.CreateLabel(
            _theme,
            entry.Key,
            13,
            FontWeight.Bold);

        SelectableTextBlock contentTb = new()
        {
            Text = entry.Content,
            FontSize = 12,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _theme.Palette.TextSecondary
        };

        cardStack.Children.Add(topRow);
        cardStack.Children.Add(keyTb);
        cardStack.Children.Add(contentTb);

        return SurfaceCard.CreateInset(_theme, cardStack, new Thickness(12, 10));
    }

    private Button CreateSettingsPromptPresetsFlyoutButton(AiAssistantViewModel aiVm, out Action refreshI18n)
    {
        Flyout flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard
        };

        StackPanel flyoutStack = new()
        {
            Spacing = 6,
            Width = 280
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
            flyoutStack.Children.Clear();

            for (int i = 0; i < presets.Length; i++)
            {
                (string presetKey, string labelKey, string iconName) = presets[i];
                Control itemIcon = VectorIcons.Create(iconName, 14, _theme.Palette.BrandPrimary);
                TextBlock itemLabel = SectionHeader.CreateLabel(_theme, _i18n[labelKey], 12, FontWeight.SemiBold);
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
                    Padding = new Thickness(10, 7),
                    Background = _theme.Palette.SecondarySurface,
                    BorderBrush = _theme.Palette.SubtleBorder,
                    Content = rowGrid,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };

                presetBtn.Click += (_, _) =>
                {
                    flyout.Hide();
                    aiVm.ApplySystemPromptPreset(presetKey);
                };

                flyoutStack.Children.Add(presetBtn);
            }
        }

        flyout.Opening += (_, _) => RebuildPresetItems();

        Button btn = AppButton.CreateSecondary(
            _theme,
            _i18n["Ai.BtnPromptPresets"],
            command: null,
            iconSvg: VectorIcons.AiSparkle);
        btn.Flyout = flyout;

        refreshI18n = () => AppButton.SetText(btn, _i18n["Ai.BtnPromptPresets"]);
        return btn;
    }

    private string FormatCategoryLabel(AiMemoryCategory category) =>
        category switch
        {
            AiMemoryCategory.General => _i18n["Ai.BrainCatGeneral"],
            AiMemoryCategory.UserPreference => _i18n["Ai.BrainCatPreference"],
            AiMemoryCategory.ProductKnowledge => _i18n["Ai.BrainCatProduct"],
            AiMemoryCategory.WritingStyle => _i18n["Ai.BrainCatStyle"],
            AiMemoryCategory.CampaignRule => _i18n["Ai.BrainCatRule"],
            AiMemoryCategory.GroupStrategy => _i18n["Ai.BrainCatGroup"],
            AiMemoryCategory.LeadPlaybook => _i18n["Ai.BrainCatLead"],
            _ => _i18n["Ai.BrainCatGeneral"]
        };
}
