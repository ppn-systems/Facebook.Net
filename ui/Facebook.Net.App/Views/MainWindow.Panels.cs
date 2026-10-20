// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the focused Publisher Page
/// and the Sales Leads &amp; Auto-Comment Page using SVG Info (`i`) flyouts and clean popovers.
/// </summary>
public sealed partial class MainWindow
{
    private async Task<string?> PickSingleFileAsync(string title)
    {
        if (this.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private Control BuildPublisherScreen()
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10
        };

        // Top Bar: Title + SVG Info (`i`) Flyout + Saved Templates Flyout Button + Up-Top Flyout Button
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

        TextBox photoBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.PhotoPlaceholder"],
            this._vm.PostMediaFilePath);
        photoBox.TextChanged += (_, _) => this._vm.PostMediaFilePath = photoBox.Text ?? string.Empty;

        // Saved Templates Flyout (replaces permanent right column)
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

        Button deleteTplBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnDeleteTemplate"],
            this._vm.DeleteSelectedTemplateCommand);

        Border emptyTemplatesBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.Templates"],
            VectorIcons.Publish,
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
            iconSvg: VectorIcons.Folder);
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
            this._vm.ScheduleDelayMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        scheduleDelayBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(scheduleDelayBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mins))
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

        Grid photoRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        Button pickPhotoBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnPickPhoto"]);
        pickPhotoBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                photoBox.Text = path;
                this._vm.PostMediaFilePath = path;
            }
        };

        Grid.SetColumn(photoBox, 0);
        Grid.SetColumn(pickPhotoBtn, 1);
        photoRow.Children.Add(photoBox);
        photoRow.Children.Add(pickPhotoBtn);
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
            this._vm.SaveTemplateCommand);

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
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SpintaxPreviewText), StringComparison.Ordinal))
            {
                previewSpintaxTb.Text = this._vm.SpintaxPreviewText;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.PostSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(contentBox.Text, this._vm.PostSpintaxText, StringComparison.Ordinal))
            {
                contentBox.Text = this._vm.PostSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.BumpCommentSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(bumpBox.Text, this._vm.BumpCommentSpintaxText, StringComparison.Ordinal))
            {
                bumpBox.Text = this._vm.BumpCommentSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.PostMediaFilePath), StringComparison.Ordinal) &&
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

    private Control BuildSalesLeadsScreen()
    {
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Leads.Header"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Top Filter & Scan Bar
        Grid scanBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };

        TextBox buyerKwBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords);
        buyerKwBox.TextChanged += (_, _) => this._vm.BuyerIntentKeywords = buyerKwBox.Text ?? string.Empty;

        Button scanBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnScanGroupPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Search);

        Button exportLeadsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnExportLeads"]);
        exportLeadsBtn.Click += async (_, _) =>
        {
            if (this.StorageProvider is null)
            {
                return;
            }

            IStorageFile? file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = this._i18n["Leads.BtnExportLeads"],
                SuggestedFileName = $"facebook-net-leads-{DateTimeOffset.Now:yyyyMMdd-HHmm}.csv",
                DefaultExtension = "csv"
            }).ConfigureAwait(true);

            string? path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await this._vm.ExportScrapedLeadsAsync(path).ConfigureAwait(true);
            }
        };

        Grid.SetColumn(buyerKwBox, 0);
        Grid.SetColumn(scanBtn, 1);
        Grid.SetColumn(exportLeadsBtn, 2);
        scanBar.Children.Add(buyerKwBox);
        scanBar.Children.Add(scanBtn);
        scanBar.Children.Add(exportLeadsBtn);
        Grid.SetRow(scanBar, 1);
        pageGrid.Children.Add(scanBar);

        // Discovered Group Posts List
        ListBox postsList = new()
        {
            IsVisible = this._vm.ScrapedPosts.Count > 0,
            ItemsSource = this._vm.ScrapedPosts,
            SelectedItem = this._vm.SelectedScrapedPost,
            ItemTemplate = new FuncDataTemplate<PostSummary>((post, _) =>
            {
                if (post is null)
                {
                    return new Border();
                }

                StackPanel card = new() { Spacing = 4, Margin = new Thickness(4) };
                StackPanel authorRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
                authorRow.Children.Add(UiControls.CreateAvatarBadge(this._theme, post.AuthorName ?? this._i18n["Leads.DefaultAuthor"], 26));

                TextBlock authorTb = UiControls.CreateThemedLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.AuthorName) ? this._i18n["Leads.DefaultAuthor"] : post.AuthorName,
                    13,
                    FontWeight.SemiBold);
                authorRow.Children.Add(authorTb);

                int intentScore = Facebook.Net.UI.ViewModels.Shell.ShellViewModel.CalculateBuyerIntentScore(
                    post.MessageText,
                    this._vm.BuyerIntentKeywords);
                Border intentPill = UiControls.CreateStatusPill(
                    this._theme,
                    this._vm.FormatBuyerIntentBadge(post),
                    intentScore >= 75 ? AuditSeverity.Success : AuditSeverity.Warning);
                authorRow.Children.Add(intentPill);

                authorRow.Children.Add(UiControls.CreateStatusPill(
                    this._theme,
                    $"👍 {post.ReactionCount}  💬 {post.CommentCount}",
                    AuditSeverity.Info));

                TextBlock snippet = UiControls.CreateThemedLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.MessageText) ? this._i18n["Leads.MediaOnlyPost"] : post.MessageText,
                    12,
                    FontWeight.Normal,
                    isSecondary: true);
                snippet.MaxLines = 2;

                this._i18n.LanguageChanged += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(post.AuthorName))
                    {
                        authorTb.Text = this._i18n["Leads.DefaultAuthor"];
                    }

                    if (string.IsNullOrWhiteSpace(post.MessageText))
                    {
                        snippet.Text = this._i18n["Leads.MediaOnlyPost"];
                    }

                    UiControls.SetPillText(intentPill, this._vm.FormatBuyerIntentBadge(post));
                };

                card.Children.Add(authorRow);
                card.Children.Add(snippet);
                return UiControls.CreateInsetWell(this._theme, card, new Thickness(10, 6));
            })
        };

        Border emptyLeadsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.Leads"],
            VectorIcons.Search,
            out TextBlock emptyLeadsTb);
        emptyLeadsBanner.IsVisible = this._vm.ScrapedPosts.Count == 0;
        this._vm.ScrapedPosts.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.ScrapedPosts.Count == 0;
            emptyLeadsBanner.IsVisible = isEmpty;
            postsList.IsVisible = !isEmpty;
        };

        Grid leadsListHost = new();
        leadsListHost.Children.Add(postsList);
        leadsListHost.Children.Add(emptyLeadsBanner);
        Grid.SetRow(leadsListHost, 2);
        pageGrid.Children.Add(leadsListHost);

        // Compact Sales Comment Composer below the list
        TextBox commentBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentContentPlaceholder"],
            this._vm.SalesCommentSpintaxText,
            multiline: true,
            height: 62);
        commentBox.TextChanged += (_, _) => this._vm.SalesCommentSpintaxText = commentBox.Text ?? string.Empty;
        Grid.SetRow(commentBox, 3);
        pageGrid.Children.Add(commentBox);

        Grid commentOptionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        CheckBox autoReactCheck = new()
        {
            Content = this._i18n["Leads.AutoReactToggle"],
            IsChecked = this._vm.AutoReactBeforeComment,
            VerticalAlignment = VerticalAlignment.Center
        };
        autoReactCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoReactBeforeComment = autoReactCheck.IsChecked ?? false;

        TextBox commentPhotoBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentPhotoPlaceholder"],
            this._vm.SalesCommentPhotoPath);
        commentPhotoBox.TextChanged += (_, _) => this._vm.SalesCommentPhotoPath = commentPhotoBox.Text ?? string.Empty;

        Button pickCommentPhotoBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnPickPhoto"]);
        pickCommentPhotoBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                commentPhotoBox.Text = path;
                this._vm.SalesCommentPhotoPath = path;
            }
        };

        Grid.SetColumn(autoReactCheck, 0);
        Grid.SetColumn(commentPhotoBox, 1);
        Grid.SetColumn(pickCommentPhotoBtn, 2);
        commentOptionsRow.Children.Add(autoReactCheck);
        commentOptionsRow.Children.Add(commentPhotoBox);
        commentOptionsRow.Children.Add(pickCommentPhotoBtn);
        Grid.SetRow(commentOptionsRow, 4);
        pageGrid.Children.Add(commentOptionsRow);

        Grid commentButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        Button sendSelectedBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnSendCommentSelected"],
            this._vm.SendSalesCommentOnSelectedPostCommand,
            VectorIcons.SalesComment);

        Button autoCommentAllBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnAutoCommentAllMatched"],
            this._vm.AutoCommentAllMatchedPostsCommand,
            VectorIcons.Publish);

        Button openPostBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenSelectedPostInBrowserCommand,
            VectorIcons.Browser);

        Grid.SetColumn(sendSelectedBtn, 0);
        Grid.SetColumn(autoCommentAllBtn, 1);
        Grid.SetColumn(openPostBtn, 2);
        commentButtonsRow.Children.Add(sendSelectedBtn);
        commentButtonsRow.Children.Add(autoCommentAllBtn);
        commentButtonsRow.Children.Add(openPostBtn);
        Grid.SetRow(commentButtonsRow, 5);
        pageGrid.Children.Add(commentButtonsRow);

        postsList.SelectionChanged += (_, _) =>
        {
            if (postsList.SelectedItem is PostSummary p)
            {
                this._vm.SelectedScrapedPost = p;
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SalesCommentSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(commentBox.Text, this._vm.SalesCommentSpintaxText, StringComparison.Ordinal))
            {
                commentBox.Text = this._vm.SalesCommentSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SalesCommentPhotoPath), StringComparison.Ordinal) &&
                     !string.Equals(commentPhotoBox.Text, this._vm.SalesCommentPhotoPath, StringComparison.Ordinal))
            {
                commentPhotoBox.Text = this._vm.SalesCommentPhotoPath;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal) &&
                     !ReferenceEquals(postsList.SelectedItem, this._vm.SelectedScrapedPost))
            {
                postsList.SelectedItem = this._vm.SelectedScrapedPost;
            }
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            emptyLeadsTb.Text = this._i18n["Empty.Leads"];
            buyerKwBox.PlaceholderText = this._i18n["Leads.BuyerKeywordPlaceholder"];
            UiControls.SetButtonText(scanBtn, this._i18n["Leads.BtnScanGroupPosts"]);
            UiControls.SetButtonText(exportLeadsBtn, this._i18n["Leads.BtnExportLeads"]);
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactCheck.Content = this._i18n["Leads.AutoReactToggle"];
            UiControls.SetButtonText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            UiControls.SetButtonText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            UiControls.SetButtonText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            UiControls.SetButtonText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return UiControls.CreateCard(this._theme, pageGrid);
    }
}
