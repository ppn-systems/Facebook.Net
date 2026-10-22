// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Collections;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Pages;

/// <summary>
/// Independent Group &amp; Personal Timeline Publisher Page (<c>PublisherPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Facebook-Style Composer + Smart Multi-Group &amp; Colored Tag Target Picker,
/// 2) Published Posts &amp; Personal Profile Timeline Sync with 1-Click Repost to Groups/Tags,
/// 3) Saved Spintax Templates &amp; Auto-Bump Commenter.
/// </summary>
public sealed partial class PublisherPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public PublisherPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public PublisherPage(ShellViewModel viewModel)
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
        SubTabBar? subTabBar = null;
        Control tab1View = this.BuildComposeTab(out TextBox contentBox, out TextBox photoBox, out Action refreshTab1);
        Action? refreshTab2 = null;
        Action? refreshTab3 = null;

        subTabBar = new SubTabBar(
            this._theme,
            [
                new SubTabItem(this._i18n["Publisher.TabCompose"], VectorIcons.Publish, tab1View),
                new SubTabItem(
                    this._i18n["Publisher.TabPostManager"],
                    VectorIcons.Groups,
                    () => this.BuildPublishedPostsAndRepostTab(() => subTabBar?.SelectTab(0), out refreshTab2)),
                new SubTabItem(
                    this._i18n["Publisher.TabTemplatesAndBump"],
                    VectorIcons.Bookmark,
                    () => this.BuildTemplatesAndBumpCombinedTab(
                        contentBox,
                        photoBox,
                        () => subTabBar?.SelectTab(0),
                        out refreshTab3))
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Publisher.TabCompose"],
                this._i18n["Publisher.TabPostManager"],
                this._i18n["Publisher.TabTemplatesAndBump"]
            ]);
            refreshTab1();
            refreshTab2?.Invoke();
            refreshTab3?.Invoke();
        };

        return subTabBar;
    }

    private Control BuildComposeTab(out TextBox contentBox, out TextBox photoBox, out Action refreshI18n)
    {
        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,420"),
            ColumnSpacing = 14
        };

        // =====================================================================
        // LEFT COLUMN: Facebook-Style Post Composer Card
        // =====================================================================
        Grid composerGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            RowSpacing = 10
        };

        // Row 0: Personal Profile Identity Card + 3 Publish Target Modes
        Grid identityBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        ContentControl avatarHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel identityMeta = new()
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid nameAndBadgeRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock profileNameTb = SectionHeader.CreateLabel(
            this._theme,
            this._vm.SelectedAccount?.DisplayName ?? this._i18n["TopBar.NoAccount"],
            14.5,
            FontWeight.Bold);

        Border profileActivePill = StatusPill.Create(
            this._theme,
            this._i18n["Publisher.PersonalProfileBadge"],
            AuditSeverity.Success);

        Grid.SetColumn(profileNameTb, 0);
        Grid.SetColumn(profileActivePill, 1);
        nameAndBadgeRow.Children.Add(profileNameTb);
        nameAndBadgeRow.Children.Add(profileActivePill);

        string[] GetModeLabels() =>
        [
            this._i18n["Publisher.ModeOnlyFeed"],
            this._i18n["Publisher.ModeOnlyGroups"],
            this._i18n["Publisher.ModeFeedAndGroups"]
        ];

        List<(Button Btn, TextBlock Lbl)> modeButtons = new(3);
        StackPanel modeRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2
        };

        void RefreshModeButtonsVisuals()
        {
            ThemePalette p = this._theme.Palette;
            int activeIdx = Math.Clamp(this._vm.PublishTargetMode, 0, 2);
            for (int i = 0; i < modeButtons.Count; i++)
            {
                (Button btn, TextBlock lbl) = modeButtons[i];
                bool isActive = i == activeIdx;
                btn.Background = isActive ? p.BrandPrimary : Brushes.Transparent;
                lbl.Foreground = isActive ? Brushes.White : p.TextSecondary;
                lbl.FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium;
            }
        }

        string[] initialModeLabels = GetModeLabels();
        for (int i = 0; i < initialModeLabels.Length; i++)
        {
            int capturedMode = i;
            TextBlock modeLbl = new()
            {
                Text = initialModeLabels[i],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Button modeBtn = new()
            {
                Content = modeLbl,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            modeBtn.Click += (_, _) =>
            {
                if (this._vm.PublishTargetMode != capturedMode)
                {
                    this._vm.PublishTargetMode = capturedMode;
                    RefreshModeButtonsVisuals();
                }
            };

            modeButtons.Add((modeBtn, modeLbl));
            modeRow.Children.Add(modeBtn);
        }

        Border modeSelectorBar = new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = modeRow
        };

        void ApplyModeSelectorColors()
        {
            ThemePalette p = this._theme.Palette;
            modeSelectorBar.Background = p.ElevatedSurface;
            modeSelectorBar.BorderBrush = p.InteractiveBorder;
            RefreshModeButtonsVisuals();
        }

        ApplyModeSelectorColors();
        ThemeLifecycle.Bind(modeSelectorBar, this._theme, ApplyModeSelectorColors);

        identityMeta.Children.Add(nameAndBadgeRow);
        identityMeta.Children.Add(modeSelectorBar);

        Grid.SetColumn(avatarHost, 0);
        Grid.SetColumn(identityMeta, 1);
        identityBar.Children.Add(avatarHost);
        identityBar.Children.Add(identityMeta);

        Border identityInset = SurfaceCard.CreateInset(this._theme, identityBar, new Thickness(12, 10));
        Grid.SetRow(identityInset, 0);
        composerGrid.Children.Add(identityInset);

        // Row 1: Facebook-style Multiline Post Composer Box
        TextBox localContentBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.ContentPlaceholder"],
            this._vm.PostSpintaxText,
            multiline: true,
            height: 165);
        localContentBox.VerticalAlignment = VerticalAlignment.Stretch;
        localContentBox.FontSize = 13.5;
        localContentBox.TextChanged += (_, _) => this._vm.PostSpintaxText = localContentBox.Text ?? string.Empty;
        Grid.SetRow(localContentBox, 1);
        composerGrid.Children.Add(localContentBox);
        contentBox = localContentBox;

        // Row 2: 1-Button Dynamic Variable Picker Flyout + Simulated Facebook Post Preview
        Button insertVarBtn = SpintaxVariablePicker.CreateButton(
            this._theme,
            this._i18n,
            localContentBox,
            out Action rebuildComposeVars);

        Button previewSpintaxBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnPreviewSpintax"],
            null,
            VectorIcons.Browser);

        StackPanel composerToolbarRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { insertVarBtn, previewSpintaxBtn }
        };
        Grid.SetRow(composerToolbarRow, 2);
        composerGrid.Children.Add(composerToolbarRow);

        // Row 3: Photo Picker + Live Attached Image Thumbnail Preview
        StackPanel mediaStack = new() { Spacing = 8 };

        Grid photoRow = FilePickerInput.Create(
            this._theme,
            this._i18n["Publisher.PhotoPlaceholder"],
            this._vm.PostMediaFilePath,
            this._i18n["Publisher.BtnPickPhoto"],
            out TextBox localPhotoBox,
            out Button pickPhotoBtn);

        Border photoPreviewContainer = MediaThumbnailStrip.Create(
            this._theme,
            this._i18n,
            onClearClicked: () =>
            {
                localPhotoBox.Text = string.Empty;
                this._vm.PostMediaFilePath = string.Empty;
            },
            out Action<IReadOnlyList<string>> refreshStripFiles);

        void RefreshPhotoPreview() =>
            refreshStripFiles(ShellViewModel.ParseExistingMediaFilePaths(localPhotoBox.Text));

        localPhotoBox.TextChanged += (_, _) =>
        {
            this._vm.PostMediaFilePath = localPhotoBox.Text ?? string.Empty;
            RefreshPhotoPreview();
        };
        pickPhotoBtn.Click += async (_, _) =>
        {
            IReadOnlyList<string> picked = await this.PickMultipleFilesAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (picked.Count > 0)
            {
                string joined = string.Join(";", picked);
                localPhotoBox.Text = joined;
                this._vm.PostMediaFilePath = joined;
                RefreshPhotoPreview();
            }
        };

        mediaStack.Children.Add(photoRow);
        mediaStack.Children.Add(photoPreviewContainer);
        Grid.SetRow(mediaStack, 3);
        composerGrid.Children.Add(mediaStack);
        photoBox = localPhotoBox;

        // Row 4: Auto-React + Schedule Delay Row + Quick Presets
        Grid optionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,64,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        CheckBox autoReactOwnPostCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Publisher.AutoReactOwnPost"],
            this._vm.AutoReactPublishedPost,
            out TextBlock autoReactOwnPostLbl);
        autoReactOwnPostCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoReactPublishedPost = autoReactOwnPostCheck.IsChecked ?? false;

        TextBlock scheduleDelayLbl = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Publisher.ScheduleDelayLabel"],
            12,
            FontWeight.Medium,
            isSecondary: true);

        TextBox scheduleDelayBox = FormField.CreateTextBox(
            this._theme,
            "0",
            this._vm.ScheduleDelayMinutes.ToString(CultureInfo.InvariantCulture));
        scheduleDelayBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(scheduleDelayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mins))
            {
                this._vm.ScheduleDelayMinutes = Math.Clamp(mins, 0, 1440);
            }
        };

        StackPanel schedulePresetsRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button CreateSchedulePresetBtn(string i18nKey, int minutes)
        {
            Button btn = AppButton.CreateSecondary(this._theme, this._i18n[i18nKey]);
            btn.Padding = new Thickness(7, 4);
            btn.MinHeight = 26;
            btn.Click += (_, _) =>
            {
                this._vm.ScheduleDelayMinutes = minutes;
                scheduleDelayBox.Text = minutes.ToString(CultureInfo.InvariantCulture);
            };
            return btn;
        }

        Button presetNowBtn = CreateSchedulePresetBtn("Publisher.PresetNow", 0);
        Button preset15mBtn = CreateSchedulePresetBtn("Publisher.Preset15m", 15);
        Button preset30mBtn = CreateSchedulePresetBtn("Publisher.Preset30m", 30);
        Button preset1hBtn = CreateSchedulePresetBtn("Publisher.Preset1h", 60);
        Button preset2hBtn = CreateSchedulePresetBtn("Publisher.Preset2h", 120);

        schedulePresetsRow.Children.Add(presetNowBtn);
        schedulePresetsRow.Children.Add(preset15mBtn);
        schedulePresetsRow.Children.Add(preset30mBtn);
        schedulePresetsRow.Children.Add(preset1hBtn);
        schedulePresetsRow.Children.Add(preset2hBtn);

        Grid.SetColumn(autoReactOwnPostCheck, 0);
        Grid.SetColumn(scheduleDelayLbl, 1);
        Grid.SetColumn(scheduleDelayBox, 2);
        Grid.SetColumn(schedulePresetsRow, 3);
        optionsRow.Children.Add(autoReactOwnPostCheck);
        optionsRow.Children.Add(scheduleDelayLbl);
        optionsRow.Children.Add(scheduleDelayBox);
        optionsRow.Children.Add(schedulePresetsRow);
        Grid.SetRow(optionsRow, 4);
        composerGrid.Children.Add(optionsRow);

        // Row 5: Save Template + Dynamic Primary Publish Button
        Grid btnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10
        };

        Button saveTplBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnSaveTemplate"],
            this._vm.SaveTemplateCommand,
            VectorIcons.Bookmark);

        Button publishBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Publisher.BtnPublishNow"],
            this._vm.PublishCampaignCommand,
            VectorIcons.Publish);

        Grid.SetColumn(saveTplBtn, 0);
        Grid.SetColumn(publishBtn, 1);
        btnRow.Children.Add(saveTplBtn);
        btnRow.Children.Add(publishBtn);
        Grid.SetRow(btnRow, 5);
        composerGrid.Children.Add(btnRow);

        Border leftCard = SurfaceCard.Create(this._theme, composerGrid, new Thickness(16));

        // =====================================================================
        // RIGHT COLUMN: Smart Multi-Group & Colored Tag Target Picker
        // =====================================================================
        Control rightTargetCard = this.BuildTargetGroupAndTagPickerCard(
            out Action refreshTargetCard,
            onSelectionChanged: UpdatePublishButtonAndIdentity);

        void UpdatePublishButtonAndIdentity()
        {
            string accName = this._vm.SelectedAccount?.DisplayName ?? this._i18n["TopBar.NoAccount"];
            profileNameTb.Text = accName;
            avatarHost.Content = AvatarBadge.Create(this._theme, accName, 42, this._vm.SelectedAccount?.AvatarUrl);
            RefreshModeButtonsVisuals();

            int enabledGroups = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledGroups++;
                }
            }

            string dynamicBtnLabel = !string.IsNullOrWhiteSpace(this._vm.CampaignProgressText)
                ? this._vm.CampaignProgressText
                : (this._vm.PublishTargetMode switch
                {
                    0 => this._i18n.Format("Publisher.BtnPublishFeedDynamic", accName),
                    2 => this._i18n.Format("Publisher.BtnPublishBothDynamic", enabledGroups),
                    _ => this._i18n.Format("Publisher.BtnPublishGroupsDynamic", enabledGroups)
                });
            AppButton.SetText(publishBtn, dynamicBtnLabel);
        }

        UpdatePublishButtonAndIdentity();

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightTargetCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightTargetCard);

        // =====================================================================
        // FULL-PAGE SIMULATED FACEBOOK POST PREVIEW VIEW (With Back Navigation)
        // =====================================================================
        TransitioningContentControl composeHost = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = splitGrid
        };

        Action<FacebookPostPreviewData> populatePreviewCard = null!;

        void PopulateSimulatedPreview()
        {
            if (this._vm.PreviewSpintaxCommand.CanExecute(null))
            {
                this._vm.PreviewSpintaxCommand.Execute(null);
            }

            string accName = this._vm.SelectedAccount?.DisplayName ?? this._i18n["TopBar.NoAccount"];
            string sampleGroupName = this._i18n["Publisher.DefaultSampleGroupName"];
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    sampleGroupName = this._vm.TargetGroups[i].GroupName;
                    break;
                }
            }

            string headerDisplayLine = this._vm.PublishTargetMode == 0
                ? accName
                : $"{accName}  ▸  {sampleGroupName}";

            string rawPreview = this._vm.SpintaxPreviewText ?? string.Empty;
            int colonIdx = rawPreview.IndexOf(": ", StringComparison.Ordinal);
            if (colonIdx > 0 && colonIdx < 28)
            {
                rawPreview = rawPreview[(colonIdx + 2)..];
            }

            DateTime now = DateTime.Now;
            string resolvedBody = rawPreview
                .Replace("{group_name}", sampleGroupName, StringComparison.OrdinalIgnoreCase)
                .Replace("{date}", now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{random_id}", Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

            populatePreviewCard(new FacebookPostPreviewData(
                AccountDisplayName: accName,
                AccountAvatarUrl: this._vm.SelectedAccount?.AvatarUrl,
                HeaderDisplayLine: headerDisplayLine,
                ResolvedBodyText: resolvedBody,
                ShowAutoReactionSummary: this._vm.AutoReactPublishedPost,
                MediaFilePaths: ShellViewModel.ParseExistingMediaFilePaths(localPhotoBox.Text)));
        }

        Border previewPageCard = FacebookPostPreviewCard.Create(
            this._theme,
            this._i18n,
            onBackClicked: () => composeHost.Content = splitGrid,
            onSpinAnotherClicked: PopulateSimulatedPreview,
            out populatePreviewCard,
            out Action refreshPreviewCardI18n);

        previewSpintaxBtn.Click += (_, _) =>
        {
            PopulateSimulatedPreview();
            composeHost.Content = previewPageCard;
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.PostSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(localContentBox.Text, this._vm.PostSpintaxText, StringComparison.Ordinal))
            {
                localContentBox.Text = this._vm.PostSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.PostMediaFilePath), StringComparison.Ordinal) &&
                     !string.Equals(localPhotoBox.Text, this._vm.PostMediaFilePath, StringComparison.Ordinal))
            {
                localPhotoBox.Text = this._vm.PostMediaFilePath;
                RefreshPhotoPreview();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.PublishTargetMode), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.CampaignProgressText), StringComparison.Ordinal))
            {
                UpdatePublishButtonAndIdentity();
            }
        };

        refreshI18n = () =>
        {
            StatusPill.SetText(profileActivePill, this._i18n["Publisher.PersonalProfileBadge"]);
            string[] nextModeLabels = GetModeLabels();
            for (int i = 0; i < modeButtons.Count && i < nextModeLabels.Length; i++)
            {
                modeButtons[i].Lbl.Text = nextModeLabels[i];
            }

            autoReactOwnPostLbl.Text = this._i18n["Publisher.AutoReactOwnPost"];
            scheduleDelayLbl.Text = this._i18n["Publisher.ScheduleDelayLabel"];
            AppButton.SetText(presetNowBtn, this._i18n["Publisher.PresetNow"]);
            AppButton.SetText(preset15mBtn, this._i18n["Publisher.Preset15m"]);
            AppButton.SetText(preset30mBtn, this._i18n["Publisher.Preset30m"]);
            AppButton.SetText(preset1hBtn, this._i18n["Publisher.Preset1h"]);
            AppButton.SetText(preset2hBtn, this._i18n["Publisher.Preset2h"]);
            localContentBox.PlaceholderText = this._i18n["Publisher.ContentPlaceholder"];
            rebuildComposeVars();
            AppButton.SetText(previewSpintaxBtn, this._i18n["Publisher.BtnPreviewSpintax"]);
            localPhotoBox.PlaceholderText = this._i18n["Publisher.PhotoPlaceholder"];
            AppButton.SetText(pickPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(saveTplBtn, this._i18n["Publisher.BtnSaveTemplate"]);

            refreshPreviewCardI18n();
            UpdatePublishButtonAndIdentity();
            RefreshPhotoPreview();
            refreshTargetCard();
        };

        return composeHost;
    }

    internal Control BuildTargetGroupAndTagPickerCard(
        out Action refreshCard,
        Action? onSelectionChanged = null)
    {
        Grid pickerGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
            RowSpacing = 8
        };

        // Row 0: Header with Selected / Total counter
        TextBlock pickerTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Publisher.TargetPickerTitle"],
            13.5,
            FontWeight.Bold);
        Grid.SetRow(pickerTitleTb, 0);
        pickerGrid.Children.Add(pickerTitleTb);

        // Row 1: Colored Tag Quick-Selection Strip ("Chọn nhanh nhóm theo Nhãn màu")
        StackPanel tagSectionStack = new() { Spacing = 5 };
        TextBlock tagQuickHintTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Publisher.QuickSelectByTagHint"],
            11,
            FontWeight.SemiBold,
            isSecondary: true);

        WrapPanel tagChipsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 5,
            LineSpacing = 5
        };

        tagSectionStack.Children.Add(tagQuickHintTb);
        tagSectionStack.Children.Add(tagChipsWrap);
        Border tagSectionCard = SurfaceCard.CreateInset(this._theme, tagSectionStack, new Thickness(10, 8));
        Grid.SetRow(tagSectionCard, 1);
        pickerGrid.Children.Add(tagSectionCard);

        // Row 2: Quick Batch Selection Toolbar (All | None | Random 10 | Random 30)
        WrapPanel batchBtnsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6
        };

        Button btnAll = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand);
        btnAll.Padding = new Thickness(8, 4);

        Button btnNone = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand);
        btnNone.Padding = new Thickness(8, 4);

        Button btnRand10 = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnRandom10"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => this._vm.SelectRandomTargetGroupsAsync(10)));
        btnRand10.Padding = new Thickness(8, 4);

        Button btnRand30 = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnRandom30"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => this._vm.SelectRandomTargetGroupsAsync(30)));
        btnRand30.Padding = new Thickness(8, 4);

        batchBtnsWrap.Children.Add(btnAll);
        batchBtnsWrap.Children.Add(btnNone);
        batchBtnsWrap.Children.Add(btnRand10);
        batchBtnsWrap.Children.Add(btnRand30);
        Grid.SetRow(batchBtnsWrap, 2);
        pickerGrid.Children.Add(batchBtnsWrap);

        // Row 3: Unified Group SearchFilterBar with 2-Level Drill-Down Categories (Tag + Selection State)
        BulkObservableCollection<SavedTargetGroup> filteredPickerGroups = new();
        string? selectedTagFilter = null;
        string? selectedSelectionFilter = null;
        SearchFilterBar groupSearchBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"]);

        void UpdatePickerHeaderAndTagChips()
        {
            int enabledTotal = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledTotal++;
                }
            }

            pickerTitleTb.Text = $"{this._i18n["Publisher.TargetPickerTitle"]} — {enabledTotal} / {this._vm.TargetGroups.Count}";

            // Rebuild colored tag quick-select chips
            tagChipsWrap.Children.Clear();
            for (int i = 0; i < this._vm.GroupTags.Count; i++)
            {
                GroupTag tag = this._vm.GroupTags[i];
                int totalWithTag = 0;
                int enabledWithTag = 0;
                for (int j = 0; j < this._vm.TargetGroups.Count; j++)
                {
                    if (this._vm.TargetGroups[j].HasTag(tag.Id))
                    {
                        totalWithTag++;
                        if (this._vm.TargetGroups[j].IsEnabled)
                        {
                            enabledWithTag++;
                        }
                    }
                }

                bool allSelected = totalWithTag > 0 && enabledWithTag == totalWithTag;
                string suffix = $"{enabledWithTag}/{totalWithTag}";
                Border chip = GroupRowCard.CreateTagPill(
                    tag,
                    suffixText: suffix,
                    isActive: allSelected || enabledWithTag > 0,
                    onClick: async () =>
                    {
                        await this._vm.ToggleSelectTargetGroupsByTagAsync(tag.Id).ConfigureAwait(true);
                        RebuildPickerState();
                        onSelectionChanged?.Invoke();
                    });
                tagChipsWrap.Children.Add(chip);
            }

            onSelectionChanged?.Invoke();
        }

        void RebuildPickerState()
        {
            UpdatePickerHeaderAndTagChips();

            string q = (groupSearchBar.SearchTextBox.Text ?? string.Empty).Trim();
            List<SavedTargetGroup> matches = new(this._vm.TargetGroups.Count);
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup tg = this._vm.TargetGroups[i];
                if (string.Equals(selectedSelectionFilter, "enabled", StringComparison.OrdinalIgnoreCase) && !tg.IsEnabled)
                {
                    continue;
                }

                if (string.Equals(selectedSelectionFilter, "disabled", StringComparison.OrdinalIgnoreCase) && tg.IsEnabled)
                {
                    continue;
                }

                if (string.Equals(selectedTagFilter, "__untagged__", StringComparison.OrdinalIgnoreCase))
                {
                    if (tg.GetTagIds().Count > 0)
                    {
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(selectedTagFilter) && !tg.HasTag(selectedTagFilter))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(q) &&
                    !tg.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(tg);
            }

            filteredPickerGroups.ReplaceAll(matches);
        }

        FilterCategoryDefinition tagCategory = new(
            "tag",
            this._i18n["Groups.FilterCatColorTag"],
            VectorIcons.Bookmark,
            () =>
            {
                List<FilterOptionItem> opts = new(this._vm.GroupTags.Count + 1);
                for (int i = 0; i < this._vm.GroupTags.Count; i++)
                {
                    GroupTag t = this._vm.GroupTags[i];
                    opts.Add(new FilterOptionItem(t.Id, t.Name, t.ColorHex));
                }

                opts.Add(new FilterOptionItem("__untagged__", this._i18n["Groups.FilterUntagged"]));
                return opts;
            },
            ids =>
            {
                selectedTagFilter = ids.Count > 0 ? ids[0] : null;
                RebuildPickerState();
            },
            allowMultiple: false);

        FilterCategoryDefinition selectionCategory = new(
            "selection",
            this._i18n["Publisher.FilterCatSelection"],
            VectorIcons.CheckCircle,
            () =>
            [
                new FilterOptionItem("enabled", this._i18n["Groups.FilterSegEnabled"]),
                new FilterOptionItem("disabled", this._i18n["Groups.FilterSegDisabled"])
            ],
            ids =>
            {
                selectedSelectionFilter = ids.Count > 0 ? ids[0] : null;
                RebuildPickerState();
            },
            allowMultiple: false);

        groupSearchBar.SetFilterCategories(
            [tagCategory, selectionCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        groupSearchBar.SearchTextBox.TextChanged += (_, _) => RebuildPickerState();
        groupSearchBar.SearchButton.Click += (_, _) => RebuildPickerState();
        Grid.SetRow(groupSearchBar, 3);
        pickerGrid.Children.Add(groupSearchBar);

        // Row 4: Checkable Target Groups List
        ListBox pickerList = FormField.CreateListBox<SavedTargetGroup>(
            this._theme,
            filteredPickerGroups,
            tg => GroupRowCard.CreateTargetRow(
                this._theme,
                tg,
                async nextState =>
                {
                    await this._vm.ToggleTargetGroupEnabledAsync(tg, nextState).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(selectedSelectionFilter))
                    {
                        RebuildPickerState();
                    }
                },
                assignedTags: this._vm.GetGroupTagsForGroup(tg.GroupId),
                allTags: this._vm.GroupTags,
                onToggleTag: async tagId =>
                {
                    await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, tagId).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(selectedTagFilter))
                    {
                        RebuildPickerState();
                    }
                },
                addTagLabel: this._i18n["Groups.BtnAddTagOnRow"]),
            isVisible: true);

        Grid.SetRow(pickerList, 4);
        pickerGrid.Children.Add(pickerList);

        this._vm.TargetGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
                string.IsNullOrEmpty(selectedTagFilter) &&
                string.IsNullOrEmpty(selectedSelectionFilter) &&
                e.NewItems is { Count: > 0 } &&
                e.NewItems[0] is SavedTargetGroup updatedTg)
            {
                for (int i = 0; i < filteredPickerGroups.Count; i++)
                {
                    if (string.Equals(filteredPickerGroups[i].GroupId, updatedTg.GroupId, StringComparison.Ordinal))
                    {
                        filteredPickerGroups[i] = updatedTg;
                        UpdatePickerHeaderAndTagChips();
                        return;
                    }
                }
            }

            RebuildPickerState();
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            groupSearchBar.RefreshFilterChips();
            RebuildPickerState();
        };
        RebuildPickerState();

        refreshCard = () =>
        {
            tagQuickHintTb.Text = this._i18n["Publisher.QuickSelectByTagHint"];
            tagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            selectionCategory.Title = this._i18n["Publisher.FilterCatSelection"];
            groupSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Groups.FilterPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            AppButton.SetText(btnAll, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(btnNone, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(btnRand10, this._i18n["Publisher.BtnRandom10"]);
            AppButton.SetText(btnRand30, this._i18n["Publisher.BtnRandom30"]);
            RebuildPickerState();
        };

        return SurfaceCard.Create(this._theme, pickerGrid, new Thickness(14));
    }

    private async Task<IReadOnlyList<string>> PickMultipleFilesAsync(string title)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return [];
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        }).ConfigureAwait(true);

        List<string> paths = new(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string? localPath = files[i].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                paths.Add(localPath);
            }
        }

        return paths;
    }
}
