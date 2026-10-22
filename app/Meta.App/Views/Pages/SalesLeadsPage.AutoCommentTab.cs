// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Pages;

/// <summary>
/// Tab 3 of <see cref="SalesLeadsPage"/>: Sales Comment Composer &amp; Batch Auto-Commenter.
/// </summary>
public sealed partial class SalesLeadsPage
{
    private Control BuildSalesAutoCommentTab(out Action refreshI18n)
    {
        Grid commentTabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 12
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Leads.TabAutoComment"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        commentTabGrid.Children.Add(header);

        // Selected Post Preview Banner with Author Avatar, Group Name & Media Preview
        ContentControl selectedPostBannerHost = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(selectedPostBannerHost, 1);
        commentTabGrid.Children.Add(selectedPostBannerHost);

        TextBox commentBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentContentPlaceholder"],
            this._vm.SalesCommentSpintaxText,
            multiline: true,
            height: 140);
        commentBox.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(commentBox, 2);
        commentTabGrid.Children.Add(commentBox);

        // Live Spintax & Variable Comment Preview Strip
        Grid previewRowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock previewPrefixTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Leads.CommentPreviewPrefix"],
            11.5,
            FontWeight.Bold);
        previewPrefixTb.Foreground = this._theme.Palette.BrandPrimary;
        previewPrefixTb.VerticalAlignment = VerticalAlignment.Top;

        TextBlock previewBodyTb = SectionHeader.CreateLabel(
            this._theme,
            string.Empty,
            12,
            FontWeight.Normal,
            isSecondary: true);
        previewBodyTb.TextWrapping = TextWrapping.Wrap;
        previewBodyTb.MaxLines = 3;
        previewBodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetColumn(previewPrefixTb, 0);
        Grid.SetColumn(previewBodyTb, 1);
        previewRowGrid.Children.Add(previewPrefixTb);
        previewRowGrid.Children.Add(previewBodyTb);

        Border commentPreviewBanner = SurfaceCard.CreateInset(this._theme, previewRowGrid, new Thickness(12, 8));
        Grid.SetRow(commentPreviewBanner, 3);
        commentTabGrid.Children.Add(commentPreviewBanner);

        void RefreshCommentPreview()
        {
            string resolved = this._vm.GenerateSalesCommentPreview();
            previewBodyTb.Text = string.IsNullOrWhiteSpace(resolved)
                ? this._i18n["Leads.CommentContentPlaceholder"]
                : resolved;
        }

        commentBox.TextChanged += (_, _) =>
        {
            this._vm.SalesCommentSpintaxText = commentBox.Text ?? string.Empty;
            RefreshCommentPreview();
        };

        Grid commentOptionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        List<CampaignTemplate> templatePickerItems = new();
        ComboBox commentTemplateCombo = FormField.CreateComboBox<string>(
            this._theme,
            Array.Empty<string>(),
            label => SectionHeader.CreateLabel(this._theme, label, 12, FontWeight.Medium),
            selectedItem: null,
            placeholderText: this._i18n["Leads.PickCommentTemplatePlaceholder"]);
        commentTemplateCombo.MinWidth = 175;
        bool suppressTemplateCombo = false;

        void RefreshCommentTemplateCombo()
        {
            suppressTemplateCombo = true;
            templatePickerItems.Clear();
            List<string> labels = new(this._vm.SavedTemplates.Count);
            for (int i = 0; i < this._vm.SavedTemplates.Count; i++)
            {
                CampaignTemplate tpl = this._vm.SavedTemplates[i];
                templatePickerItems.Add(tpl);
                labels.Add(tpl.Title);
            }

            commentTemplateCombo.ItemsSource = labels;
            commentTemplateCombo.SelectedIndex = -1;
            suppressTemplateCombo = false;
        }

        RefreshCommentTemplateCombo();
        this._vm.SavedTemplates.CollectionChanged += (_, _) => RefreshCommentTemplateCombo();

        commentTemplateCombo.SelectionChanged += (_, _) =>
        {
            if (suppressTemplateCombo)
            {
                return;
            }

            int idx = commentTemplateCombo.SelectedIndex;
            if (idx >= 0 && idx < templatePickerItems.Count)
            {
                CampaignTemplate picked = templatePickerItems[idx];
                commentBox.Text = picked.SpintaxContent;
                this._vm.SalesCommentSpintaxText = picked.SpintaxContent;
                if (!string.IsNullOrWhiteSpace(picked.MediaFilePath))
                {
                    this._vm.SalesCommentPhotoPath = picked.MediaFilePath;
                }

                RefreshCommentPreview();
            }
        };

        Button saveCommentTemplateBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnSaveAsCommentTemplate"],
            null,
            VectorIcons.Bookmark);
        saveCommentTemplateBtn.Click += async (_, _) =>
            await this._vm.SaveSalesCommentAsTemplateAsync().ConfigureAwait(true);

        Button insertVarBtn = SpintaxVariablePicker.CreateButton(
            this._theme,
            this._i18n,
            commentBox,
            out Action rebuildVarFlyout,
            includeAuthorName: true);

        Button previewSpinBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnPreviewComment"],
            null,
            VectorIcons.Sync);
        previewSpinBtn.Click += (_, _) => RefreshCommentPreview();

        CheckBox autoReactCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.AutoReactToggle"],
            this._vm.AutoReactBeforeComment,
            out TextBlock autoReactLbl);
        autoReactCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoReactBeforeComment = autoReactCheck.IsChecked ?? false;

        Grid commentPhotoPicker = FilePickerInput.Create(
            this._theme,
            this._i18n["Leads.CommentPhotoPlaceholder"],
            this._vm.SalesCommentPhotoPath,
            this._i18n["Publisher.BtnPickPhoto"],
            out TextBox commentPhotoBox,
            out Button pickCommentPhotoBtn);
        commentPhotoBox.TextChanged += (_, _) => this._vm.SalesCommentPhotoPath = commentPhotoBox.Text ?? string.Empty;
        pickCommentPhotoBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Publisher.BtnPickPhoto"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                commentPhotoBox.Text = path;
                this._vm.SalesCommentPhotoPath = path;
            }
        };

        Grid.SetColumn(commentTemplateCombo, 0);
        Grid.SetColumn(saveCommentTemplateBtn, 1);
        Grid.SetColumn(insertVarBtn, 2);
        Grid.SetColumn(previewSpinBtn, 3);
        Grid.SetColumn(autoReactCheck, 4);
        Grid.SetColumn(commentPhotoPicker, 5);
        commentOptionsRow.Children.Add(commentTemplateCombo);
        commentOptionsRow.Children.Add(saveCommentTemplateBtn);
        commentOptionsRow.Children.Add(insertVarBtn);
        commentOptionsRow.Children.Add(previewSpinBtn);
        commentOptionsRow.Children.Add(autoReactCheck);
        commentOptionsRow.Children.Add(commentPhotoPicker);
        Grid.SetRow(commentOptionsRow, 4);
        commentTabGrid.Children.Add(commentOptionsRow);

        Grid commentButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
        };

        Button sendSelectedBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnSendCommentSelected"],
            this._vm.SendSalesCommentOnSelectedPostCommand,
            VectorIcons.SalesComment);

        Button autoCommentAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnAutoCommentAllMatched"],
            command: null,
            VectorIcons.Publish);
        autoCommentAllBtn.Click += (_, _) =>
        {
            int count = this._vm.ScrapedPosts.Count;
            if (count == 0)
            {
                if (this._vm.AutoCommentAllMatchedPostsCommand.CanExecute(null))
                {
                    this._vm.AutoCommentAllMatchedPostsCommand.Execute(null);
                }

                return;
            }

            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Leads.BtnAutoCommentAllMatched"],
                this._i18n.Format("Leads.ConfirmAutoCommentAllMsg", count),
                this._i18n["Leads.BtnAutoCommentAllMatched"],
                this._i18n["Groups.BtnCancelEditTag"],
                async () =>
                {
                    if (this._vm.AutoCommentAllMatchedPostsCommand.CanExecute(null))
                    {
                        await this._vm.AutoCommentAllMatchedPostsCommand.ExecuteAsync(null).ConfigureAwait(true);
                    }
                });
        };

        void UpdateAutoCommentAllBtnLabel()
        {
            string label = !string.IsNullOrWhiteSpace(this._vm.AutoCommentProgressText)
                ? this._vm.AutoCommentProgressText
                : this._i18n["Leads.BtnAutoCommentAllMatched"];
            AppButton.SetText(autoCommentAllBtn, label);
        }

        Button openPostBtn = AppButton.CreateSecondary(
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
        commentTabGrid.Children.Add(commentButtonsRow);

        void UpdateSelectedPostBanner()
        {
            RefreshCommentPreview();
            PostSummary? post = this._vm.SelectedScrapedPost;
            sendSelectedBtn.IsVisible = post is not null;
            openPostBtn.IsVisible = post is not null;

            if (post is null)
            {
                TextBlock emptyTb = SectionHeader.CreateLabel(
                    this._theme,
                    this._i18n["Leads.NoPostSelected"],
                    12.5,
                    FontWeight.SemiBold,
                    isSecondary: true);
                selectedPostBannerHost.Content = SurfaceCard.CreateInset(this._theme, emptyTb, new Thickness(14, 12));
                return;
            }

            string author = string.IsNullOrWhiteSpace(post.AuthorName)
                ? this._i18n["Leads.DefaultAuthor"]
                : post.AuthorName;
            string group = string.IsNullOrWhiteSpace(post.GroupName)
                ? this._i18n["Audit.Cat.Group"]
                : post.GroupName;
            string msg = string.IsNullOrWhiteSpace(post.MessageText)
                ? this._i18n["Leads.EmptyPostText"]
                : post.MessageText;

            Grid postCardGrid = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border avatar = AvatarBadge.Create(this._theme, author, 38, post.AuthorAvatarUrl);
            StackPanel infoStack = new() { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            TextBlock topLine = SectionHeader.CreateLabel(
                this._theme,
                $"{this._i18n["Leads.SelectedPostBanner"]} {author}  •  {group}",
                12.5,
                FontWeight.Bold);
            topLine.MaxLines = 1;
            topLine.TextTrimming = TextTrimming.CharacterEllipsis;

            TextBlock bodyLine = SectionHeader.CreateLabel(this._theme, msg, 12, FontWeight.Normal, isSecondary: true);
            bodyLine.MaxLines = 2;
            bodyLine.TextWrapping = TextWrapping.Wrap;
            bodyLine.TextTrimming = TextTrimming.CharacterEllipsis;

            infoStack.Children.Add(topLine);
            infoStack.Children.Add(bodyLine);

            bool commented = this._vm.IsPostCommented(post.PostId);
            Border statusBadge = StatusPill.Create(
                this._theme,
                commented ? this._i18n["Leads.StatusCommented"] : this._i18n["Leads.StatusPending"],
                commented ? AuditSeverity.Success : AuditSeverity.Warning);

            Grid.SetColumn(avatar, 0);
            Grid.SetColumn(infoStack, 1);
            Grid.SetColumn(statusBadge, 2);
            postCardGrid.Children.Add(avatar);
            postCardGrid.Children.Add(infoStack);
            postCardGrid.Children.Add(statusBadge);

            selectedPostBannerHost.Content = SurfaceCard.CreateInset(this._theme, postCardGrid, new Thickness(14, 10));
        }

        UpdateSelectedPostBanner();

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal))
            {
                UpdateSelectedPostBanner();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SalesCommentSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(commentBox.Text, this._vm.SalesCommentSpintaxText, StringComparison.Ordinal))
            {
                commentBox.Text = this._vm.SalesCommentSpintaxText;
                RefreshCommentPreview();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SalesCommentPhotoPath), StringComparison.Ordinal) &&
                     !string.Equals(commentPhotoBox.Text, this._vm.SalesCommentPhotoPath, StringComparison.Ordinal))
            {
                commentPhotoBox.Text = this._vm.SalesCommentPhotoPath;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.AutoCommentProgressText), StringComparison.Ordinal))
            {
                UpdateAutoCommentAllBtnLabel();
            }
        };
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => UpdateSelectedPostBanner();
        this._theme.ThemeChanged += (_, _) =>
        {
            UpdateSelectedPostBanner();
            rebuildVarFlyout();
            previewPrefixTb.Foreground = this._theme.Palette.BrandPrimary;
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Leads.TabAutoComment"];
            descTb.Text = this._i18n["Leads.Desc"];
            previewPrefixTb.Text = this._i18n["Leads.CommentPreviewPrefix"];
            UpdateSelectedPostBanner();
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentTemplateCombo.PlaceholderText = this._i18n["Leads.PickCommentTemplatePlaceholder"];
            AppButton.SetText(saveCommentTemplateBtn, this._i18n["Leads.BtnSaveAsCommentTemplate"]);
            AppButton.SetText(insertVarBtn, this._i18n["Common.BtnInsertVariable"]);
            AppButton.SetText(previewSpinBtn, this._i18n["Leads.BtnPreviewComment"]);
            rebuildVarFlyout();
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactLbl.Text = this._i18n["Leads.AutoReactToggle"];
            AppButton.SetText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            UpdateAutoCommentAllBtnLabel();
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return SurfaceCard.Create(this._theme, commentTabGrid, new Thickness(18));
    }
}
