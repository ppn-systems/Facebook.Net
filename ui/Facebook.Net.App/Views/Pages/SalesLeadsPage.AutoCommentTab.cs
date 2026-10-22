// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

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
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel variableMenuStack = new()
        {
            Spacing = 4,
            MinWidth = 245
        };

        Flyout variableFlyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = SurfaceCard.Create(this._theme, variableMenuStack, new Thickness(8))
        };

        void RebuildVariableFlyoutItems()
        {
            variableMenuStack.Children.Clear();
            (string Label, string Token)[] items =
            [
                ($"{this._i18n["Common.VarAuthorNameLabel"]}  •  {{author_name}}", "{author_name}"),
                ($"{this._i18n["Common.VarGroupNameLabel"]}  •  {{group_name}}", "{group_name}"),
                ($"{this._i18n["Common.VarGreetingLabel"]}  •  {{Xin chào|Chào bạn}}", "{Xin chào|Chào bạn|Chào anh/chị}"),
                ($"{this._i18n["Common.VarDateLabel"]}  •  {{date}}", "{date}"),
                ($"{this._i18n["Common.VarTimeLabel"]}  •  {{time}}", "{time}"),
                ($"{this._i18n["Common.VarRandomIdLabel"]}  •  #{{random_id}}", "#{random_id}")
            ];

            for (int i = 0; i < items.Length; i++)
            {
                (string itemLabel, string tokenToInsert) = items[i];
                Button itemBtn = new()
                {
                    Content = SectionHeader.CreateLabel(this._theme, itemLabel, 12, FontWeight.SemiBold),
                    Background = this._theme.Palette.SecondarySurface,
                    BorderBrush = this._theme.Palette.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                itemBtn.Click += (_, _) =>
                {
                    variableFlyout.Hide();
                    string current = commentBox.Text ?? string.Empty;
                    commentBox.Text = string.IsNullOrEmpty(current)
                        ? tokenToInsert
                        : $"{current} {tokenToInsert}";
                    commentBox.CaretIndex = commentBox.Text.Length;
                    _ = commentBox.Focus();
                };
                variableMenuStack.Children.Add(itemBtn);
            }
        }

        RebuildVariableFlyoutItems();

        Button insertVarBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Common.BtnInsertVariable"],
            null,
            VectorIcons.Plus);
        insertVarBtn.Flyout = variableFlyout;

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

        Grid.SetColumn(insertVarBtn, 0);
        Grid.SetColumn(previewSpinBtn, 1);
        Grid.SetColumn(autoReactCheck, 2);
        Grid.SetColumn(commentPhotoPicker, 3);
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
            this._vm.AutoCommentAllMatchedPostsCommand,
            VectorIcons.Publish);

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
        };
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => UpdateSelectedPostBanner();
        this._theme.ThemeChanged += (_, _) =>
        {
            UpdateSelectedPostBanner();
            RebuildVariableFlyoutItems();
            previewPrefixTb.Foreground = this._theme.Palette.BrandPrimary;
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Leads.TabAutoComment"];
            descTb.Text = this._i18n["Leads.Desc"];
            previewPrefixTb.Text = this._i18n["Leads.CommentPreviewPrefix"];
            UpdateSelectedPostBanner();
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            AppButton.SetText(insertVarBtn, this._i18n["Common.BtnInsertVariable"]);
            AppButton.SetText(previewSpinBtn, this._i18n["Leads.BtnPreviewComment"]);
            RebuildVariableFlyoutItems();
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactLbl.Text = this._i18n["Leads.AutoReactToggle"];
            AppButton.SetText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            AppButton.SetText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return SurfaceCard.Create(this._theme, commentTabGrid, new Thickness(18));
    }
}
