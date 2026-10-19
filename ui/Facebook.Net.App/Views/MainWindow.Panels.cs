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
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Audit;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the Publisher Screen,
/// Sales Leads &amp; Auto-Comment Screen, Settings Screen, and the Collapsible Bottom Audit Log Taskbar.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] s_csvPatterns = ["*.csv"];
    private static readonly string[] s_jsonPatterns = ["*.json"];
    private static readonly FilePickerFileType[] s_exportFileTypeChoices =
    [
        new("CSV Report (*.csv)") { Patterns = s_csvPatterns },
        new("JSON Report (*.json)") { Patterns = s_jsonPatterns }
    ];

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

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SpintaxPreviewText), StringComparison.Ordinal))
            {
                previewSpintaxTb.Text = this._vm.SpintaxPreviewText;
            }
        };

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
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.PostSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(contentBox.Text, this._vm.PostSpintaxText, StringComparison.Ordinal))
            {
                contentBox.Text = this._vm.PostSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.BumpCommentSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(bumpBox.Text, this._vm.BumpCommentSpintaxText, StringComparison.Ordinal))
            {
                bumpBox.Text = this._vm.BumpCommentSpintaxText;
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
                if (!string.IsNullOrWhiteSpace(selectedTpl.MediaFilePath))
                {
                    photoBox.Text = selectedTpl.MediaFilePath;
                    this._vm.PostMediaFilePath = selectedTpl.MediaFilePath;
                }
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
            ItemTemplate = new FuncDataTemplate<PostSummary>((post, _) =>
            {
                if (post is null)
                {
                    return new Border();
                }

                StackPanel card = new() { Spacing = 4, Margin = new Thickness(4) };
                StackPanel authorRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
                authorRow.Children.Add(CloudflareControls.CreateAvatarBadge(this._theme, post.AuthorName ?? this._i18n["Leads.DefaultAuthor"], 26));
                authorRow.Children.Add(CloudflareControls.CreateThemedLabel(
                    this._theme,
                    string.IsNullOrWhiteSpace(post.AuthorName) ? this._i18n["Leads.DefaultAuthor"] : post.AuthorName,
                    13,
                    FontWeight.SemiBold));
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

        postsList.SelectionChanged += (_, _) =>
        {
            if (postsList.SelectedItem is PostSummary p)
            {
                this._vm.SelectedScrapedPost = p;
                selectedBanner.Text = $"{this._i18n["Leads.SelectedPostBanner"]} {p.AuthorName ?? this._i18n["Leads.DefaultAuthor"]} — \"{p.MessageText}\"";
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

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SalesCommentSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(commentBox.Text, this._vm.SalesCommentSpintaxText, StringComparison.Ordinal))
            {
                commentBox.Text = this._vm.SalesCommentSpintaxText;
            }
        };

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
            if (this._vm.SelectedScrapedPost is null)
            {
                selectedBanner.Text = this._i18n["Leads.NoPostSelected"];
            }

            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            CloudflareControls.SetButtonText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            CloudflareControls.SetButtonText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            CloudflareControls.SetButtonText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            CloudflareControls.SetButtonText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return grid;
    }

    private Control BuildSettingsScreen()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 16
        };

        StackPanel leftStack = new() { Spacing = 10 };
        StackPanel header = CloudflareControls.CreateSectionHeader(
            this._theme,
            this._i18n["Settings.Header"],
            this._i18n["Settings.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        // 1. Theme Selector
        TextBlock themeLabel = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.ThemeLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox themeCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AppThemeMode[] { AppThemeMode.Dark, AppThemeMode.Light },
            SelectedItem = this._vm.SelectedThemeMode,
            ItemTemplate = new FuncDataTemplate<AppThemeMode>((m, _) =>
            {
                TextBlock tb = CloudflareControls.CreateThemedLabel(this._theme, this._i18n.FormatThemeMode(m), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatThemeMode(m);
                return tb;
            })
        };
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is AppThemeMode mode)
            {
                this._vm.SelectedThemeMode = mode;
            }
        };

        // 2. Language Selector
        TextBlock langLabel = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.LanguageLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox langCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AppLanguage[] { AppLanguage.ViVn, AppLanguage.EnUs },
            SelectedItem = this._vm.SelectedLanguage,
            ItemTemplate = new FuncDataTemplate<AppLanguage>((l, _) =>
                CloudflareControls.CreateThemedLabel(this._theme, LocalizationService.FormatLanguage(l), 12))
        };
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is AppLanguage lang)
            {
                this._vm.SelectedLanguage = lang;
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedThemeMode), StringComparison.Ordinal))
            {
                themeCombo.SelectedItem = this._vm.SelectedThemeMode;
            }
            else if (string.Equals(e.PropertyName, nameof(Facebook.Net.UI.ViewModels.Shell.ShellViewModel.SelectedLanguage), StringComparison.Ordinal))
            {
                langCombo.SelectedItem = this._vm.SelectedLanguage;
            }
        };

        // 3. Browser Engine Selector
        TextBlock browserLabel = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.BrowserEngineLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox browserCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new PreferredBrowserKind[]
            {
                PreferredBrowserKind.AutoDetect,
                PreferredBrowserKind.Edge,
                PreferredBrowserKind.Chrome,
                PreferredBrowserKind.Brave
            },
            SelectedItem = this._vm.SelectedBrowserKind,
            ItemTemplate = new FuncDataTemplate<PreferredBrowserKind>((k, _) =>
            {
                TextBlock tb = CloudflareControls.CreateThemedLabel(this._theme, this._i18n.FormatBrowserKind(k), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatBrowserKind(k);
                return tb;
            })
        };
        browserCombo.SelectionChanged += (_, _) =>
        {
            if (browserCombo.SelectedItem is PreferredBrowserKind kind)
            {
                this._vm.SelectedBrowserKind = kind;
            }
        };

        Grid customExeRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox customExeBox = CloudflareControls.CreateTextBox(
            this._theme,
            this._i18n["Settings.BrowserCustomPathPlaceholder"],
            this._vm.CustomBrowserExecutablePath);
        customExeBox.TextChanged += (_, _) => this._vm.CustomBrowserExecutablePath = customExeBox.Text ?? string.Empty;

        Button browseExeBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Common.BrowseFile"]);
        browseExeBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Common.BrowseFile"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                customExeBox.Text = path;
                this._vm.CustomBrowserExecutablePath = path;
            }
        };

        Grid.SetColumn(customExeBox, 0);
        Grid.SetColumn(browseExeBtn, 1);
        customExeRow.Children.Add(customExeBox);
        customExeRow.Children.Add(browseExeBtn);

        CheckBox headlessCheck = new()
        {
            Content = this._i18n["Settings.HeadlessLabel"],
            IsChecked = this._vm.RunBrowserHeadless
        };
        headlessCheck.IsCheckedChanged += (_, _) =>
            this._vm.RunBrowserHeadless = headlessCheck.IsChecked ?? false;

        // 4. Anti-Checkpoint Delay Range Controls
        TextBlock delayRangeLabel = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.DelayRangeLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        Grid delayRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
            ColumnSpacing = 8
        };

        TextBlock minDelayPrefix = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            12);

        NumericUpDown minDelayBox = new()
        {
            Minimum = 0,
            Maximum = 600,
            Increment = 5,
            Value = this._vm.MinDelaySeconds,
            FormatString = "0"
        };
        minDelayBox.ValueChanged += (_, _) =>
            this._vm.MinDelaySeconds = (int)(minDelayBox.Value ?? 15);

        TextBlock maxDelayPrefix = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.MaxDelayPrefix"],
            12);

        NumericUpDown maxDelayBox = new()
        {
            Minimum = 0,
            Maximum = 1200,
            Increment = 5,
            Value = this._vm.MaxDelaySeconds,
            FormatString = "0"
        };
        maxDelayBox.ValueChanged += (_, _) =>
            this._vm.MaxDelaySeconds = (int)(maxDelayBox.Value ?? 45);

        Grid.SetColumn(minDelayPrefix, 0);
        Grid.SetColumn(minDelayBox, 1);
        Grid.SetColumn(maxDelayPrefix, 2);
        Grid.SetColumn(maxDelayBox, 3);
        delayRow.Children.Add(minDelayPrefix);
        delayRow.Children.Add(minDelayBox);
        delayRow.Children.Add(maxDelayPrefix);
        delayRow.Children.Add(maxDelayBox);

        Button saveBtn = CloudflareControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Settings.BtnSaveSettings"],
            this._vm.SaveSettingsCommand,
            VectorIcons.Settings);

        leftStack.Children.Add(themeLabel);
        leftStack.Children.Add(themeCombo);
        leftStack.Children.Add(langLabel);
        leftStack.Children.Add(langCombo);
        leftStack.Children.Add(browserLabel);
        leftStack.Children.Add(browserCombo);
        leftStack.Children.Add(customExeRow);
        leftStack.Children.Add(headlessCheck);
        leftStack.Children.Add(delayRangeLabel);
        leftStack.Children.Add(delayRow);
        leftStack.Children.Add(saveBtn);

        Border leftCard = CloudflareControls.CreateCard(this._theme, new ScrollViewer { Content = leftStack });
        Grid.SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // Right: Non-technical User Guide Card
        StackPanel guideStack = new() { Spacing = 8 };
        TextBlock guideTitle = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.GuideTitle"],
            15,
            FontWeight.SemiBold,
            isBrand: true);
        TextBlock guideBody = CloudflareControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.GuideBody"],
            13);
        guideStack.Children.Add(guideTitle);
        guideStack.Children.Add(guideBody);

        Border rightCard = CloudflareControls.CreateCard(this._theme, guideStack);
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Settings.Header"];
            descTb.Text = this._i18n["Settings.Desc"];
            themeLabel.Text = this._i18n["Settings.ThemeLabel"];
            langLabel.Text = this._i18n["Settings.LanguageLabel"];
            browserLabel.Text = this._i18n["Settings.BrowserEngineLabel"];
            customExeBox.PlaceholderText = this._i18n["Settings.BrowserCustomPathPlaceholder"];
            CloudflareControls.SetButtonText(browseExeBtn, this._i18n["Common.BrowseFile"]);
            headlessCheck.Content = this._i18n["Settings.HeadlessLabel"];
            delayRangeLabel.Text = this._i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            CloudflareControls.SetButtonText(saveBtn, this._i18n["Settings.BtnSaveSettings"]);
            guideTitle.Text = this._i18n["Settings.GuideTitle"];
            guideBody.Text = this._i18n["Settings.GuideBody"];
        };

        return grid;
    }

    private Border BuildCollapsibleAuditTaskbar()
    {
        AuditLogDrawerViewModel drawerVm = this._vm.AuditDrawer;
        StackPanel container = new() { Spacing = 0 };

        // 1. Collapsed Persistent Status Bar (36px height)
        Grid barGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(16, 6)
        };

        Border livePill = CloudflareControls.CreateStatusPill(
            this._theme,
            this._i18n["Audit.TaskbarTitle"],
            AuditSeverity.Info);
        Grid.SetColumn(livePill, 0);

        TextBlock tickerText = new()
        {
            Text = drawerVm.LatestTickerText,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(tickerText, 1);

        TextBlock countersText = new()
        {
            Text = $"✔ {drawerVm.SuccessCount}   ⚠ {drawerVm.WarningCount}   ✖ {drawerVm.ErrorCount}",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countersText, 2);

        Button toggleBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            drawerVm.IsExpanded ? this._i18n["Audit.ToggleCollapse"] : this._i18n["Audit.ToggleExpand"],
            drawerVm.ToggleDrawerCommand,
            VectorIcons.AuditLog);
        Grid.SetColumn(toggleBtn, 3);

        barGrid.Children.Add(livePill);
        barGrid.Children.Add(tickerText);
        barGrid.Children.Add(countersText);
        barGrid.Children.Add(toggleBtn);

        // 2. Expandable Audit Log Drawer Panel (Hidden by default unless expanded)
        Grid drawerBody = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Height = 250,
            Margin = new Thickness(16, 4, 16, 12),
            RowSpacing = 8,
            IsVisible = drawerVm.IsExpanded
        };

        // 4-Way Filter Bar: Category (Sự kiện), Time Range (Thời gian), Account (Tài khoản), Keyword (Từ khóa)
        Grid filterRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("150,150,175,*,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };

        ComboBox categoryCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new object[]
            {
                "ALL",
                AuditCategory.System,
                AuditCategory.Account,
                AuditCategory.Group,
                AuditCategory.Post,
                AuditCategory.SalesComment,
                AuditCategory.Scrape
            },
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<object>((obj, _) =>
            {
                AuditCategory? cat = obj is AuditCategory c ? c : null;
                TextBlock tb = CloudflareControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditCategory(cat), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditCategory(cat);
                return tb;
            })
        };
        categoryCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedCategory = categoryCombo.SelectedItem is AuditCategory cat ? cat : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(categoryCombo, 0);

        ComboBox timeRangeCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AuditTimeRange[]
            {
                AuditTimeRange.AllTime,
                AuditTimeRange.LastHour,
                AuditTimeRange.Today24Hours,
                AuditTimeRange.Last7Days
            },
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<AuditTimeRange>((r, _) =>
            {
                TextBlock tb = CloudflareControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditTimeRange(r), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditTimeRange(r);
                return tb;
            })
        };
        timeRangeCombo.SelectionChanged += async (_, _) =>
        {
            if (timeRangeCombo.SelectedItem is AuditTimeRange range)
            {
                drawerVm.SelectedTimeRange = range;
                await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
            }
        };
        Grid.SetColumn(timeRangeCombo, 1);

        System.Collections.ObjectModel.ObservableCollection<object> accountFilterItems = new() { "ALL" };
        for (int i = 0; i < this._vm.Accounts.Count; i++)
        {
            accountFilterItems.Add(this._vm.Accounts[i]);
        }

        ComboBox accountFilterCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = this._i18n["Audit.FilterAccountAll"],
            ItemsSource = accountFilterItems,
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<object>((obj, _) =>
            {
                if (obj is AccountProfile acc)
                {
                    return CloudflareControls.CreateThemedLabel(this._theme, acc.DisplayName, 12);
                }

                TextBlock allTb = CloudflareControls.CreateThemedLabel(this._theme, this._i18n["Audit.FilterAccountAll"], 12);
                this._i18n.LanguageChanged += (_, _) => allTb.Text = this._i18n["Audit.FilterAccountAll"];
                return allTb;
            })
        };

        this._vm.Accounts.CollectionChanged += (_, _) =>
        {
            object? prev = accountFilterCombo.SelectedItem;
            accountFilterItems.Clear();
            accountFilterItems.Add("ALL");
            object? restored = "ALL";
            for (int i = 0; i < this._vm.Accounts.Count; i++)
            {
                AccountProfile item = this._vm.Accounts[i];
                accountFilterItems.Add(item);
                if (prev is AccountProfile prevAcc && string.Equals(prevAcc.Uid, item.Uid, StringComparison.Ordinal))
                {
                    restored = item;
                }
            }

            accountFilterCombo.SelectedItem = restored;
        };

        accountFilterCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedAccountUid = accountFilterCombo.SelectedItem is AccountProfile acc ? acc.Uid : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(accountFilterCombo, 2);

        TextBox searchBox = CloudflareControls.CreateTextBox(this._theme, this._i18n["Audit.SearchPlaceholder"]);
        searchBox.TextChanged += async (_, _) =>
        {
            drawerVm.SearchKeyword = searchBox.Text ?? string.Empty;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(searchBox, 3);

        Button refreshBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnRefresh"],
            drawerVm.RefreshFilterCommand);
        Grid.SetColumn(refreshBtn, 4);

        Button exportBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnExport"]);
        exportBtn.Click += async (_, _) =>
        {
            if (this.StorageProvider is null)
            {
                return;
            }

            IStorageFile? saveFile = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = this._i18n["Audit.BtnExport"],
                SuggestedFileName = $"facebook_audit_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = s_exportFileTypeChoices
            }).ConfigureAwait(true);

            string? localPath = saveFile?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await drawerVm.ExportFilteredLogsAsync(localPath).ConfigureAwait(true);
            }
        };
        Grid.SetColumn(exportBtn, 5);

        Button clearBtn = CloudflareControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnClear"],
            drawerVm.ClearLogsCommand);
        Grid.SetColumn(clearBtn, 6);

        filterRow.Children.Add(categoryCombo);
        filterRow.Children.Add(timeRangeCombo);
        filterRow.Children.Add(accountFilterCombo);
        filterRow.Children.Add(searchBox);
        filterRow.Children.Add(refreshBtn);
        filterRow.Children.Add(exportBtn);
        filterRow.Children.Add(clearBtn);
        Grid.SetRow(filterRow, 0);
        drawerBody.Children.Add(filterRow);

        ListBox logList = new()
        {
            ItemsSource = drawerVm.FilteredLogs,
            ItemTemplate = new FuncDataTemplate<AuditLogRecord>((rec, _) =>
            {
                if (rec is null)
                {
                    return new Border();
                }

                Grid logRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("75,130,120,160,*"),
                    ColumnSpacing = 8,
                    Margin = new Thickness(2, 1)
                };

                logRow.Children.Add(CloudflareControls.CreateThemedLabel(
                    this._theme,
                    rec.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    11,
                    FontWeight.Normal,
                    isSecondary: true));

                TextBlock accTb = CloudflareControls.CreateThemedLabel(this._theme, rec.AccountDisplayName, 11, FontWeight.SemiBold);
                Grid.SetColumn(accTb, 1);
                logRow.Children.Add(accTb);

                Border catPill = CloudflareControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatAuditCategory(rec.Category),
                    rec.Severity);
                this._i18n.LanguageChanged += (_, _) =>
                    CloudflareControls.SetPillText(catPill, this._i18n.FormatAuditCategory(rec.Category));
                Grid.SetColumn(catPill, 2);
                logRow.Children.Add(catPill);

                TextBlock targetTb = CloudflareControls.CreateThemedLabel(this._theme, rec.TargetName, 11, FontWeight.Normal, isSecondary: true);
                targetTb.TextTrimming = TextTrimming.CharacterEllipsis;
                targetTb.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(targetTb, 3);
                logRow.Children.Add(targetTb);

                TextBlock msgTb = CloudflareControls.CreateThemedLabel(this._theme, rec.Message, 12);
                msgTb.TextWrapping = TextWrapping.NoWrap;
                msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(msgTb, 4);
                logRow.Children.Add(msgTb);

                return logRow;
            })
        };
        Grid.SetRow(logList, 1);
        drawerBody.Children.Add(logList);

        container.Children.Add(barGrid);
        container.Children.Add(drawerBody);

        Border wrapper = new()
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = container
        };

        void ApplyColors()
        {
            CloudflarePalette p = this._theme.Palette;
            wrapper.Background = p.ElevatedSurface;
            wrapper.BorderBrush = p.SubtleBorder;
            tickerText.Foreground = p.TextSecondary;
            countersText.Foreground = p.TextPrimary;
        }

        ApplyColors();
        this._theme.ThemeChanged += (_, _) => ApplyColors();

        this._i18n.LanguageChanged += (_, _) =>
        {
            CloudflareControls.SetPillText(livePill, this._i18n["Audit.TaskbarTitle"]);
            accountFilterCombo.PlaceholderText = this._i18n["Audit.FilterAccountAll"];
            searchBox.PlaceholderText = this._i18n["Audit.SearchPlaceholder"];
            CloudflareControls.SetButtonText(refreshBtn, this._i18n["Audit.BtnRefresh"]);
            CloudflareControls.SetButtonText(exportBtn, this._i18n["Audit.BtnExport"]);
            CloudflareControls.SetButtonText(clearBtn, this._i18n["Audit.BtnClear"]);
            CloudflareControls.SetButtonText(
                toggleBtn,
                drawerVm.IsExpanded ? this._i18n["Audit.ToggleCollapse"] : this._i18n["Audit.ToggleExpand"]);
        };

        drawerVm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(AuditLogDrawerViewModel.IsExpanded), StringComparison.Ordinal))
            {
                drawerBody.IsVisible = drawerVm.IsExpanded;
                CloudflareControls.SetButtonText(
                    toggleBtn,
                    drawerVm.IsExpanded ? this._i18n["Audit.ToggleCollapse"] : this._i18n["Audit.ToggleExpand"]);
            }
            else if (string.Equals(e.PropertyName, nameof(AuditLogDrawerViewModel.LatestTickerText), StringComparison.Ordinal))
            {
                tickerText.Text = drawerVm.LatestTickerText;
            }
            else
            {
                countersText.Text = $"✔ {drawerVm.SuccessCount}   ⚠ {drawerVm.WarningCount}   ✖ {drawerVm.ErrorCount}";
            }
        };

        return wrapper;
    }
}
