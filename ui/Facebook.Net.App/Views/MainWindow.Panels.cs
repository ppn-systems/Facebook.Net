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
/// Partial implementation of <see cref="MainWindow"/> building the Publisher Screen
/// and the Sales Leads &amp; Auto-Comment Screen.
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
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,360"),
            ColumnSpacing = 16
        };

        StackPanel leftStack = new() { Spacing = 10 };
        StackPanel header = CloudflareControls.CreateSectionHeader(
            this._theme,
            this._i18n["Publisher.Header"],
            this._i18n["Publisher.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        CheckBox targetModeCheck = new()
        {
            Content = this._i18n["Publisher.TargetModeGroup"],
            IsChecked = this._vm.PublishToTargetGroups
        };
        targetModeCheck.IsCheckedChanged += (_, _) =>
            this._vm.PublishToTargetGroups = targetModeCheck.IsChecked ?? false;

        TextBox contentBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.ContentPlaceholder"],
            this._vm.PostSpintaxText,
            multiline: true,
            height: 120);
        contentBox.TextChanged += (_, _) => this._vm.PostSpintaxText = contentBox.Text ?? string.Empty;

        // Spintax Live Preview Row
        Grid previewRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8
        };

        Button previewSpintaxBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnPreviewSpintax"],
            this._vm.PreviewSpintaxCommand,
            VectorIcons.Sync);

        TextBlock previewSpintaxTb = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._vm.SpintaxPreviewText,
            12,
            FontWeight.Normal,
            isSecondary: true);

        Grid.SetColumn(previewSpintaxBtn, 0);
        Grid.SetColumn(previewSpintaxTb, 1);
        previewRow.Children.Add(previewSpintaxBtn);
        previewRow.Children.Add(previewSpintaxTb);

        Grid photoRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox photoBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.PhotoPlaceholder"],
            this._vm.PostMediaFilePath);
        photoBox.TextChanged += (_, _) => this._vm.PostMediaFilePath = photoBox.Text ?? string.Empty;

        Button pickPhotoBtn = CloudflareControls.CreateSecondaryButton(
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

        Grid btnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };

        Button saveTplBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnSaveTemplate"],
            this._vm.SaveTemplateCommand);

        Button publishBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Publisher.BtnPublishNow"],
            this._vm.PublishCampaignCommand,
            VectorIcons.Publish);

        Grid.SetColumn(saveTplBtn, 0);
        Grid.SetColumn(publishBtn, 1);
        btnRow.Children.Add(saveTplBtn);
        btnRow.Children.Add(publishBtn);

        // Auto Up-Top (Bump Recent Posts) Section
        StackPanel bumpStack = new() { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        TextBlock bumpTitleTb = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Publisher.BumpTitle"],
            14,
            FontWeight.SemiBold,
            isBrand: true);

        TextBox bumpBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Publisher.BumpPlaceholder"],
            this._vm.BumpCommentSpintaxText);
        bumpBox.TextChanged += (_, _) => this._vm.BumpCommentSpintaxText = bumpBox.Text ?? string.Empty;

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

        Button bumpBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnBumpRecentPosts"],
            this._vm.BumpRecentPostsCommand,
            VectorIcons.SalesComment);

        bumpStack.Children.Add(bumpTitleTb);
        bumpStack.Children.Add(bumpBox);
        bumpStack.Children.Add(bumpBtn);

        leftStack.Children.Add(targetModeCheck);
        leftStack.Children.Add(contentBox);
        leftStack.Children.Add(previewRow);
        leftStack.Children.Add(photoRow);
        leftStack.Children.Add(btnRow);
        leftStack.Children.Add(CloudflareControls.CreateInsetWell(this._theme, bumpStack));

        Border leftCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = leftStack });
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right: Saved Content Library
        StackPanel rightStack = new() { Spacing = 10 };
        TextBlock tplTitle = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Publisher.TemplatesTitle"],
            15,
            FontWeight.SemiBold);

        ListBox tplList = new()
        {
            Height = 350,
            ItemsSource = this._vm.SavedTemplates,
            SelectedItem = this._vm.SelectedTemplate,
            ItemTemplate = new FuncDataTemplate<CampaignTemplate>((tpl, _) =>
            {
                if (tpl is null)
                {
                    return new Border();
                }

                StackPanel sp = new() { Spacing = 3, Margin = new Thickness(4) };
                sp.Children.Add(CloudflareControls.CreateThemedLabel(this._theme, tpl.Title, 13, FontWeight.SemiBold));
                TextBlock bodyTb = CloudflareControls.CreateThemedLabel(this._theme, tpl.SpintaxContent, 11, FontWeight.Normal, isSecondary: true);
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

        Button deleteTplBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Publisher.BtnDeleteTemplate"],
            this._vm.DeleteSelectedTemplateCommand);

        rightStack.Children.Add(tplTitle);
        rightStack.Children.Add(tplList);
        rightStack.Children.Add(deleteTplBtn);

        Border rightCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = rightStack });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Publisher.Header"];
            descTb.Text = this._i18n["Publisher.Desc"];
            targetModeCheck.Content = this._i18n["Publisher.TargetModeGroup"];
            contentBox.PlaceholderText = this._i18n["Publisher.ContentPlaceholder"];
            CloudflareControls.SetButtonText(previewSpintaxBtn, this._i18n["Publisher.BtnPreviewSpintax"]);
            photoBox.PlaceholderText = this._i18n["Publisher.PhotoPlaceholder"];
            CloudflareControls.SetButtonText(pickPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            CloudflareControls.SetButtonText(saveTplBtn, this._i18n["Publisher.BtnSaveTemplate"]);
            CloudflareControls.SetButtonText(publishBtn, this._i18n["Publisher.BtnPublishNow"]);
            bumpTitleTb.Text = this._i18n["Publisher.BumpTitle"];
            bumpBox.PlaceholderText = this._i18n["Publisher.BumpPlaceholder"];
            CloudflareControls.SetButtonText(bumpBtn, this._i18n["Publisher.BtnBumpRecentPosts"]);
            tplTitle.Text = this._i18n["Publisher.TemplatesTitle"];
            CloudflareControls.SetButtonText(deleteTplBtn, this._i18n["Publisher.BtnDeleteTemplate"]);
        };

        return grid;
    }

    private Control BuildSalesLeadsScreen()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,420"),
            ColumnSpacing = 16
        };

        // Left: Visual Group Post Scanner & Buyer Keyword Filter
        StackPanel leftStack = new() { Spacing = 10 };
        StackPanel header = CloudflareControls.CreateSectionHeader(
            this._theme,
            this._i18n["Leads.Header"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        TextBox buyerKwBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords);
        buyerKwBox.TextChanged += (_, _) => this._vm.BuyerIntentKeywords = buyerKwBox.Text ?? string.Empty;

        Button scanBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnScanGroupPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Search);

        ListBox postsList = new()
        {
            Height = 340,
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
                authorRow.Children.Add(CloudflareControls.CreateAvatarBadge(this._theme, post.AuthorName ?? this._i18n["Leads.DefaultAuthor"], 26));

                TextBlock authorTb = CloudflareControls.CreateThemedLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.AuthorName) ? this._i18n["Leads.DefaultAuthor"] : post.AuthorName,
                    13,
                    FontWeight.SemiBold);
                authorRow.Children.Add(authorTb);
                authorRow.Children.Add(CloudflareControls.CreateStatusPill(
                    this._theme,
                    $"👍 {post.ReactionCount}  💬 {post.CommentCount}",
                    AuditSeverity.Info));

                TextBlock snippet = CloudflareControls.CreateThemedLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.MessageText) ? this._i18n["Leads.MediaOnlyPost"] : post.MessageText,
                    12,
                    FontWeight.Normal,
                    isSecondary: true);
                snippet.MaxLines = 3;

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
                };

                card.Children.Add(authorRow);
                card.Children.Add(snippet);
                return CloudflareControls.CreateInsetWell(this._theme, card, new Thickness(10, 6));
            })
        };

        TextBlock selectedBanner = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Leads.NoPostSelected"],
            12,
            FontWeight.Medium,
            isBrand: true);

        void UpdateSelectedPostBanner()
        {
            PostSummary? p = this._vm.SelectedScrapedPost;
            if (p is null)
            {
                selectedBanner.Text = this._i18n["Leads.NoPostSelected"];
                return;
            }

            string author = string.IsNullOrWhiteSpace(p.AuthorName) ? this._i18n["Leads.DefaultAuthor"] : p.AuthorName;
            string body = string.IsNullOrWhiteSpace(p.MessageText) ? this._i18n["Leads.MediaOnlyPost"] : p.MessageText;
            selectedBanner.Text = $"{this._i18n["Leads.SelectedPostBanner"]} {author} — \"{body}\"";
        }

        UpdateSelectedPostBanner();

        postsList.SelectionChanged += (_, _) =>
        {
            if (postsList.SelectedItem is PostSummary p)
            {
                this._vm.SelectedScrapedPost = p;
                UpdateSelectedPostBanner();
            }
        };

        leftStack.Children.Add(buyerKwBox);
        leftStack.Children.Add(scanBtn);
        leftStack.Children.Add(postsList);

        Border leftCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = leftStack });
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right: Sales Comment Composer & 1-Click Auto Comment
        StackPanel rightStack = new() { Spacing = 10 };
        rightStack.Children.Add(CloudflareControls.CreateInsetWell(this._theme, selectedBanner));

        TextBox commentBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentContentPlaceholder"],
            this._vm.SalesCommentSpintaxText,
            multiline: true,
            height: 120);
        commentBox.TextChanged += (_, _) => this._vm.SalesCommentSpintaxText = commentBox.Text ?? string.Empty;

        Grid commentPhotoRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox commentPhotoBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentPhotoPlaceholder"],
            this._vm.SalesCommentPhotoPath);
        commentPhotoBox.TextChanged += (_, _) => this._vm.SalesCommentPhotoPath = commentPhotoBox.Text ?? string.Empty;

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
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal))
            {
                if (!ReferenceEquals(postsList.SelectedItem, this._vm.SelectedScrapedPost))
                {
                    postsList.SelectedItem = this._vm.SelectedScrapedPost;
                }

                UpdateSelectedPostBanner();
            }
        };

        Button pickCommentPhotoBtn = CloudflareControls.CreateSecondaryButton(
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

        Grid.SetColumn(commentPhotoBox, 0);
        Grid.SetColumn(pickCommentPhotoBtn, 1);
        commentPhotoRow.Children.Add(commentPhotoBox);
        commentPhotoRow.Children.Add(pickCommentPhotoBtn);

        Button sendSelectedBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnSendCommentSelected"],
            this._vm.SendSalesCommentOnSelectedPostCommand,
            VectorIcons.SalesComment);

        Button autoCommentAllBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnAutoCommentAllMatched"],
            this._vm.AutoCommentAllMatchedPostsCommand,
            VectorIcons.Publish);

        Button openPostBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenSelectedPostInBrowserCommand,
            VectorIcons.Browser);

        rightStack.Children.Add(commentBox);
        rightStack.Children.Add(commentPhotoRow);
        rightStack.Children.Add(sendSelectedBtn);
        rightStack.Children.Add(autoCommentAllBtn);
        rightStack.Children.Add(openPostBtn);

        Border rightCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = rightStack });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            buyerKwBox.PlaceholderText = this._i18n["Leads.BuyerKeywordPlaceholder"];
            CloudflareControls.SetButtonText(scanBtn, this._i18n["Leads.BtnScanGroupPosts"]);
            UpdateSelectedPostBanner();
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            CloudflareControls.SetButtonText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            CloudflareControls.SetButtonText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            CloudflareControls.SetButtonText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            CloudflareControls.SetButtonText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return grid;
    }
}
