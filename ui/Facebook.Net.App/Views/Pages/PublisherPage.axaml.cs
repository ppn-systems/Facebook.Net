// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Group &amp; Feed Publisher Page (<c>PublisherPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) Spintax Campaign Composer &amp; Publisher,
/// 2) Saved Post Templates Manager,
/// 3) Up-Top / Group Post Bumper.
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
        Control tab1View = this.BuildComposeTab(out TextBox contentBox, out TextBox photoBox, out Action refreshTab1);
        Control tab2View = this.BuildTemplatesTab(contentBox, photoBox, out Action refreshTab2);
        Control tab3View = this.BuildBumpTab(out Action refreshTab3);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Publisher.TabCompose"], VectorIcons.Publish, tab1View),
                new SubTabItem(this._i18n["Publisher.TabTemplates"], VectorIcons.Bookmark, tab2View),
                new SubTabItem(this._i18n["Publisher.TabBump"], VectorIcons.SalesComment, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Publisher.TabCompose"],
                this._i18n["Publisher.TabTemplates"],
                this._i18n["Publisher.TabBump"]
            ]);
            refreshTab1();
            refreshTab2();
            refreshTab3();
        };

        return subTabBar;
    }

    private Control BuildComposeTab(out TextBox contentBox, out TextBox photoBox, out Action refreshI18n)
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 12
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Publisher.Header"],
            this._i18n["Publisher.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Options Row: Target Mode + Auto-React + Schedule Delay
        Grid optionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,84"),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        CheckBox targetModeCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Publisher.TargetModeGroup"],
            this._vm.PublishToTargetGroups,
            out TextBlock targetModeLbl);
        targetModeCheck.IsCheckedChanged += (_, _) =>
            this._vm.PublishToTargetGroups = targetModeCheck.IsChecked ?? false;

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

        Grid.SetColumn(targetModeCheck, 0);
        Grid.SetColumn(autoReactOwnPostCheck, 1);
        Grid.SetColumn(scheduleDelayLbl, 3);
        Grid.SetColumn(scheduleDelayBox, 4);
        optionsRow.Children.Add(targetModeCheck);
        optionsRow.Children.Add(autoReactOwnPostCheck);
        optionsRow.Children.Add(scheduleDelayLbl);
        optionsRow.Children.Add(scheduleDelayBox);
        Grid.SetRow(optionsRow, 1);
        pageGrid.Children.Add(optionsRow);

        TextBox localContentBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.ContentPlaceholder"],
            this._vm.PostSpintaxText,
            multiline: true,
            height: 180);
        localContentBox.VerticalAlignment = VerticalAlignment.Stretch;
        localContentBox.TextChanged += (_, _) => this._vm.PostSpintaxText = localContentBox.Text ?? string.Empty;
        Grid.SetRow(localContentBox, 2);
        pageGrid.Children.Add(localContentBox);
        contentBox = localContentBox;

        // Spintax Live Preview Row
        Grid previewRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button previewSpintaxBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnPreviewSpintax"],
            this._vm.PreviewSpintaxCommand,
            VectorIcons.Sync);

        TextBlock previewSpintaxTb = SectionHeader.CreateLabel(
            this._theme,
            this._vm.SpintaxPreviewText,
            12,
            FontWeight.Normal,
            isSecondary: true);
        previewSpintaxTb.MaxLines = 1;
        previewSpintaxTb.TextWrapping = TextWrapping.NoWrap;
        previewSpintaxTb.TextTrimming = TextTrimming.CharacterEllipsis;
        previewSpintaxTb.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetColumn(previewSpintaxBtn, 0);
        Grid.SetColumn(previewSpintaxTb, 1);
        previewRow.Children.Add(previewSpintaxBtn);
        previewRow.Children.Add(previewSpintaxTb);
        Grid.SetRow(previewRow, 3);
        pageGrid.Children.Add(previewRow);

        Grid photoRow = FilePickerInput.Create(
            this._theme,
            this._i18n["Publisher.PhotoPlaceholder"],
            this._vm.PostMediaFilePath,
            this._i18n["Publisher.BtnPickPhoto"],
            out TextBox localPhotoBox,
            out Button pickPhotoBtn);
        localPhotoBox.TextChanged += (_, _) => this._vm.PostMediaFilePath = localPhotoBox.Text ?? string.Empty;
        pickPhotoBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                localPhotoBox.Text = path;
                this._vm.PostMediaFilePath = path;
            }
        };
        Grid.SetRow(photoRow, 4);
        pageGrid.Children.Add(photoRow);
        photoBox = localPhotoBox;

        Grid btnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
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
        pageGrid.Children.Add(btnRow);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SpintaxPreviewText), StringComparison.Ordinal))
            {
                previewSpintaxTb.Text = this._vm.SpintaxPreviewText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.PostSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(localContentBox.Text, this._vm.PostSpintaxText, StringComparison.Ordinal))
            {
                localContentBox.Text = this._vm.PostSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.PostMediaFilePath), StringComparison.Ordinal) &&
                     !string.Equals(localPhotoBox.Text, this._vm.PostMediaFilePath, StringComparison.Ordinal))
            {
                localPhotoBox.Text = this._vm.PostMediaFilePath;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.Header"];
            descTb.Text = this._i18n["Publisher.Desc"];
            targetModeLbl.Text = this._i18n["Publisher.TargetModeGroup"];
            autoReactOwnPostLbl.Text = this._i18n["Publisher.AutoReactOwnPost"];
            scheduleDelayLbl.Text = this._i18n["Publisher.ScheduleDelayLabel"];
            localContentBox.PlaceholderText = this._i18n["Publisher.ContentPlaceholder"];
            AppButton.SetText(previewSpintaxBtn, this._i18n["Publisher.BtnPreviewSpintax"]);
            localPhotoBox.PlaceholderText = this._i18n["Publisher.PhotoPlaceholder"];
            AppButton.SetText(pickPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(saveTplBtn, this._i18n["Publisher.BtnSaveTemplate"]);
            AppButton.SetText(publishBtn, this._i18n["Publisher.BtnPublishNow"]);
        };

        return SurfaceCard.Create(this._theme, pageGrid, new Thickness(18));
    }

    private Control BuildTemplatesTab(TextBox contentBox, TextBox photoBox, out Action refreshI18n)
    {
        Grid tplGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Publisher.TemplatesTitle"],
            this._i18n["Publisher.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        tplGrid.Children.Add(header);

        ListBox tplList = FormField.CreateListBox<CampaignTemplate>(
            this._theme,
            this._vm.SavedTemplates,
            tpl =>
            {
                StackPanel sp = new() { Spacing = 4, Margin = new Thickness(6, 4) };
                sp.Children.Add(SectionHeader.CreateLabel(this._theme, tpl.Title, 13.5, FontWeight.SemiBold));
                TextBlock bodyTb = SectionHeader.CreateLabel(this._theme, tpl.SpintaxContent, 12, FontWeight.Normal, isSecondary: true);
                bodyTb.MaxLines = 2;
                sp.Children.Add(bodyTb);
                return sp;
            },
            selectedItem: this._vm.SelectedTemplate,
            isVisible: this._vm.SavedTemplates.Count > 0);

        tplList.SelectionChanged += (_, _) =>
        {
            if (tplList.SelectedItem is CampaignTemplate selectedTpl)
            {
                this._vm.SelectedTemplate = selectedTpl;
                contentBox.Text = selectedTpl.SpintaxContent;
                this._vm.PostSpintaxText = selectedTpl.SpintaxContent;

                string mediaPath = selectedTpl.MediaFilePath ?? string.Empty;
                photoBox.Text = mediaPath;
                this._vm.PostMediaFilePath = mediaPath;
            }
        };

        Border emptyTemplatesBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.Templates"],
            VectorIcons.Bookmark,
            out TextBlock emptyTplTb);
        emptyTemplatesBanner.IsVisible = this._vm.SavedTemplates.Count == 0;
        this._vm.SavedTemplates.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.SavedTemplates.Count == 0;
            emptyTemplatesBanner.IsVisible = isEmpty;
            tplList.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(tplList, emptyTemplatesBanner);
        Grid.SetRow(listHost, 1);
        tplGrid.Children.Add(listHost);

        Button deleteTplBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Publisher.BtnDeleteTemplate"],
            this._vm.DeleteSelectedTemplateCommand,
            VectorIcons.Trash);
        Grid.SetRow(deleteTplBtn, 2);
        tplGrid.Children.Add(deleteTplBtn);

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.TemplatesTitle"];
            descTb.Text = this._i18n["Publisher.Desc"];
            emptyTplTb.Text = this._i18n["Empty.Templates"];
            AppButton.SetText(deleteTplBtn, this._i18n["Publisher.BtnDeleteTemplate"]);
        };

        return SurfaceCard.Create(this._theme, tplGrid, new Thickness(18));
    }

    private Control BuildBumpTab(out Action refreshI18n)
    {
        Grid bumpGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Publisher.BumpTitle"],
            this._i18n["Publisher.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        bumpGrid.Children.Add(header);

        TextBox bumpBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.BumpPlaceholder"],
            this._vm.BumpCommentSpintaxText,
            multiline: true,
            height: 160);
        bumpBox.VerticalAlignment = VerticalAlignment.Stretch;
        bumpBox.TextChanged += (_, _) => this._vm.BumpCommentSpintaxText = bumpBox.Text ?? string.Empty;
        Grid.SetRow(bumpBox, 1);
        bumpGrid.Children.Add(bumpBox);

        Button bumpBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Publisher.BtnBumpRecentPosts"],
            this._vm.BumpRecentPostsCommand,
            VectorIcons.SalesComment);
        Grid.SetRow(bumpBtn, 2);
        bumpGrid.Children.Add(bumpBtn);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.BumpCommentSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(bumpBox.Text, this._vm.BumpCommentSpintaxText, StringComparison.Ordinal))
            {
                bumpBox.Text = this._vm.BumpCommentSpintaxText;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.BumpTitle"];
            descTb.Text = this._i18n["Publisher.Desc"];
            bumpBox.PlaceholderText = this._i18n["Publisher.BumpPlaceholder"];
            AppButton.SetText(bumpBtn, this._i18n["Publisher.BtnBumpRecentPosts"]);
        };

        return SurfaceCard.Create(this._theme, bumpGrid, new Thickness(18));
    }

    private async Task<string?> PickSingleFileAsync(string title)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
