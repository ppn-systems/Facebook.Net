// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the Group &amp; Feed Publisher screen (`MainWindow.Publisher.cs`).
/// </summary>
public sealed partial class MainWindow
{
    private Control BuildPublisherScreen()
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10
        };

        Grid topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Publisher.Header"],
            this._i18n["Publisher.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        TextBox contentBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.ContentPlaceholder"],
            this._vm.PostSpintaxText,
            multiline: true,
            height: 150);
        contentBox.VerticalAlignment = VerticalAlignment.Stretch;
        contentBox.TextChanged += (_, _) => this._vm.PostSpintaxText = contentBox.Text ?? string.Empty;

        Grid photoRow = UiControls.CreateFilePickerRow(
            this._theme,
            this._i18n["Publisher.PhotoPlaceholder"],
            this._vm.PostMediaFilePath,
            this._i18n["Publisher.BtnPickPhoto"],
            out TextBox photoBox,
            out Button pickPhotoBtn);
        photoBox.TextChanged += (_, _) => this._vm.PostMediaFilePath = photoBox.Text ?? string.Empty;
        pickPhotoBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                photoBox.Text = path;
                this._vm.PostMediaFilePath = path;
            }
        };

        // Saved Templates Flyout
        ListBox tplList = new()
        {
            Height = 200,
            IsVisible = this._vm.SavedTemplates.Count > 0,
            ItemsSource = this._vm.SavedTemplates,
            SelectedItem = this._vm.SelectedTemplate,
            ItemTemplate = new FuncDataTemplate<CampaignTemplate>((tpl, _) =>
            {
                if (tpl is null)
                {
                    return new Border();
                }

                StackPanel sp = new() { Spacing = 2, Margin = new Thickness(4) };
                sp.Children.Add(UiControls.CreateThemedLabel(this._theme, tpl.Title, 13, FontWeight.SemiBold));
                TextBlock bodyTb = UiControls.CreateThemedLabel(this._theme, tpl.SpintaxContent, 11, FontWeight.Normal, isSecondary: true);
                bodyTb.MaxLines = 2;
                sp.Children.Add(bodyTb);
                return sp;
            })
        };

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

        Button deleteTplBtn = UiControls.CreateDangerButton(
            this._theme,
            this._i18n["Publisher.BtnDeleteTemplate"],
            this._vm.DeleteSelectedTemplateCommand,
            VectorIcons.Trash);

        Border emptyTemplatesBanner = UiControls.CreateEmptyState(
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

        StackPanel tplFlyoutStack = new() { Spacing = 8, Width = 340 };
        tplFlyoutStack.Children.Add(emptyTemplatesBanner);
        tplFlyoutStack.Children.Add(tplList);
        tplFlyoutStack.Children.Add(deleteTplBtn);

        Button openTemplatesBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.TemplatesTitle"],
            iconSvg: VectorIcons.Bookmark);
        openTemplatesBtn.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = UiControls.CreateCard(this._theme, tplFlyoutStack, new Thickness(12))
        };

        // Up-Top / Bump Posts Flyout
        TextBox bumpBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.BumpPlaceholder"],
            this._vm.BumpCommentSpintaxText,
            multiline: true,
            height: 75);
        bumpBox.TextChanged += (_, _) => this._vm.BumpCommentSpintaxText = bumpBox.Text ?? string.Empty;

        Button bumpBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Publisher.BtnBumpRecentPosts"],
            this._vm.BumpRecentPostsCommand,
            VectorIcons.SalesComment);

        StackPanel bumpFlyoutStack = new() { Spacing = 8, Width = 340 };
        bumpFlyoutStack.Children.Add(bumpBox);
        bumpFlyoutStack.Children.Add(bumpBtn);

        Button openBumpFlyoutBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnBumpRecentPosts"],
            iconSvg: VectorIcons.SalesComment);
        openBumpFlyoutBtn.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = UiControls.CreateCard(this._theme, bumpFlyoutStack, new Thickness(12))
        };

        Grid.SetColumn(header, 0);
        Grid.SetColumn(openTemplatesBtn, 1);
        Grid.SetColumn(openBumpFlyoutBtn, 2);
        topBar.Children.Add(header);
        topBar.Children.Add(openTemplatesBtn);
        topBar.Children.Add(openBumpFlyoutBtn);
        Grid.SetRow(topBar, 0);
        pageGrid.Children.Add(topBar);

        // Options Row: Target Mode + Auto-React + Schedule Delay
        Grid optionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,76"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        CheckBox targetModeCheck = new()
        {
            Content = this._i18n["Publisher.TargetModeGroup"],
            IsChecked = this._vm.PublishToTargetGroups,
            VerticalAlignment = VerticalAlignment.Center
        };
        targetModeCheck.IsCheckedChanged += (_, _) =>
            this._vm.PublishToTargetGroups = targetModeCheck.IsChecked ?? false;

        CheckBox autoReactOwnPostCheck = new()
        {
            Content = this._i18n["Publisher.AutoReactOwnPost"],
            IsChecked = this._vm.AutoReactPublishedPost,
            VerticalAlignment = VerticalAlignment.Center
        };
        autoReactOwnPostCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoReactPublishedPost = autoReactOwnPostCheck.IsChecked ?? false;

        TextBlock scheduleDelayLbl = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Publisher.ScheduleDelayLabel"],
            12,
            FontWeight.Medium,
            isSecondary: true);

        TextBox scheduleDelayBox = UiControls.CreateTextBox(
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

        Grid.SetRow(contentBox, 2);
        pageGrid.Children.Add(contentBox);

        // Spintax Live Preview Row
        Grid previewRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button previewSpintaxBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnPreviewSpintax"],
            this._vm.PreviewSpintaxCommand,
            VectorIcons.Sync);

        TextBlock previewSpintaxTb = UiControls.CreateThemedLabel(
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

        Grid.SetRow(photoRow, 4);
        pageGrid.Children.Add(photoRow);

        Grid btnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10
        };

        Button saveTplBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnSaveTemplate"],
            this._vm.SaveTemplateCommand,
            VectorIcons.Bookmark);

        Button publishBtn = UiControls.CreatePrimaryButton(
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
                     !string.Equals(contentBox.Text, this._vm.PostSpintaxText, StringComparison.Ordinal))
            {
                contentBox.Text = this._vm.PostSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.BumpCommentSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(bumpBox.Text, this._vm.BumpCommentSpintaxText, StringComparison.Ordinal))
            {
                bumpBox.Text = this._vm.BumpCommentSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.PostMediaFilePath), StringComparison.Ordinal) &&
                     !string.Equals(photoBox.Text, this._vm.PostMediaFilePath, StringComparison.Ordinal))
            {
                photoBox.Text = this._vm.PostMediaFilePath;
            }
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Publisher.Header"];
            descTb.Text = this._i18n["Publisher.Desc"];
            emptyTplTb.Text = this._i18n["Empty.Templates"];
            targetModeCheck.Content = this._i18n["Publisher.TargetModeGroup"];
            autoReactOwnPostCheck.Content = this._i18n["Publisher.AutoReactOwnPost"];
            scheduleDelayLbl.Text = this._i18n["Publisher.ScheduleDelayLabel"];
            contentBox.PlaceholderText = this._i18n["Publisher.ContentPlaceholder"];
            UiControls.SetButtonText(previewSpintaxBtn, this._i18n["Publisher.BtnPreviewSpintax"]);
            photoBox.PlaceholderText = this._i18n["Publisher.PhotoPlaceholder"];
            UiControls.SetButtonText(pickPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            UiControls.SetButtonText(saveTplBtn, this._i18n["Publisher.BtnSaveTemplate"]);
            UiControls.SetButtonText(publishBtn, this._i18n["Publisher.BtnPublishNow"]);
            bumpBox.PlaceholderText = this._i18n["Publisher.BumpPlaceholder"];
            UiControls.SetButtonText(bumpBtn, this._i18n["Publisher.BtnBumpRecentPosts"]);
            UiControls.SetButtonText(openBumpFlyoutBtn, this._i18n["Publisher.BtnBumpRecentPosts"]);
            UiControls.SetButtonText(openTemplatesBtn, this._i18n["Publisher.TemplatesTitle"]);
            UiControls.SetButtonText(deleteTplBtn, this._i18n["Publisher.BtnDeleteTemplate"]);
        };

        return UiControls.CreateCard(this._theme, pageGrid);
    }
}
