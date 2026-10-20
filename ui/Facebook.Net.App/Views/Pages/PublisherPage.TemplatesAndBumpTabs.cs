// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tabs 2 and 3 of <see cref="PublisherPage"/>:
/// Tab 2: Published Posts Manager, Personal Profile Timeline Sync &amp; 1-Click Repost to Groups/Tags,
/// Tab 3: Saved Post Templates Manager &amp; Up-Top / Group Post Bumper.
/// </summary>
public sealed partial class PublisherPage
{
    private Control BuildPublishedPostsAndRepostTab(out Action refreshI18n)
    {
        Grid tabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };

        // Row 0: Header + Sync Personal Timeline Posts Button + Open / Delete Buttons
        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Publisher.PostManagerTitle"],
            this._i18n["Publisher.PostManagerDesc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button syncTimelineBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Publisher.BtnSyncTimelinePosts"],
            this._vm.SyncPersonalTimelinePostsCommand,
            VectorIcons.Sync);

        Button openBrowserBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenPublishedPostInBrowserCommand,
            VectorIcons.Browser);

        Button deletePostBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Publisher.BtnDeletePublishedPost"],
            this._vm.DeletePublishedPostCommand,
            VectorIcons.Trash);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(syncTimelineBtn, 1);
        Grid.SetColumn(openBrowserBtn, 2);
        Grid.SetColumn(deletePostBtn, 3);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(syncTimelineBtn);
        topHeaderRow.Children.Add(openBrowserBtn);
        topHeaderRow.Children.Add(deletePostBtn);
        Grid.SetRow(topHeaderRow, 0);
        tabGrid.Children.Add(topHeaderRow);

        // Row 1: Filter Bar (All | Personal Timeline | Group Posts) + Search Box
        BulkObservableCollection<PublishedPostRecord> filteredPosts = new();
        int sourceFilter = 0; // 0 = All, 1 = Personal (FEED / PERSONAL_SYNC), 2 = Group (GROUP)
        TextBox postSearchBox = null!;

        void RefreshFilteredPosts()
        {
            string q = (postSearchBox?.Text ?? string.Empty).Trim();
            List<PublishedPostRecord> matches = new(this._vm.PublishedPosts.Count);
            for (int i = 0; i < this._vm.PublishedPosts.Count; i++)
            {
                PublishedPostRecord rec = this._vm.PublishedPosts[i];
                bool isGroup = !rec.IsPersonalProfilePost;
                if (sourceFilter == 1 && isGroup)
                {
                    continue;
                }

                if (sourceFilter == 2 && !isGroup)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(q) &&
                    !rec.MessageText.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !(rec.TargetGroupName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                matches.Add(rec);
            }

            filteredPosts.ReplaceAll(matches);
        }

        Grid filterRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        SegmentedFilterPill sourceFilterPill = new(
            this._theme,
            [
                this._i18n["Publisher.FilterAllPosts"],
                this._i18n["Publisher.FilterPersonalPosts"],
                this._i18n["Publisher.FilterGroupPosts"]
            ],
            initialIndex: 0,
            onSelected: idx =>
            {
                sourceFilter = idx;
                RefreshFilteredPosts();
            });

        postSearchBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.SearchPublishedPlaceholder"],
            string.Empty);
        postSearchBox.TextChanged += (_, _) => RefreshFilteredPosts();

        Grid.SetColumn(sourceFilterPill, 0);
        Grid.SetColumn(postSearchBox, 1);
        filterRow.Children.Add(sourceFilterPill);
        filterRow.Children.Add(postSearchBox);
        Grid.SetRow(filterRow, 1);
        tabGrid.Children.Add(filterRow);

        RefreshFilteredPosts();

        // Row 2: Master-Detail Split (Left = Published/Synced Posts List, Right = 1-Click Repost to Groups/Tags)
        Grid masterDetailGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,430"),
            ColumnSpacing = 12
        };

        ListBox postsList = FormField.CreateListBox<PublishedPostRecord>(
            this._theme,
            filteredPosts,
            rec =>
            {
                Grid cardGrid = new()
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    RowSpacing = 6
                };

                Grid topMeta = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 9,
                    VerticalAlignment = VerticalAlignment.Center
                };

                string authorName = !string.IsNullOrWhiteSpace(rec.AuthorName)
                    ? rec.AuthorName
                    : (this._vm.SelectedAccount?.DisplayName ?? this._i18n["TopBar.NoAccount"]);
                string? authorAvatar = rec.AuthorAvatarUrl ?? this._vm.SelectedAccount?.AvatarUrl;
                Border av = AvatarBadge.Create(this._theme, authorName, 32, authorAvatar);

                StackPanel authorAndTarget = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                TextBlock authorTb = SectionHeader.CreateLabel(this._theme, authorName, 12.5, FontWeight.Bold);

                bool isGroup = !rec.IsPersonalProfilePost;
                string targetLabel = isGroup
                    ? $"👥 {rec.TargetGroupName ?? this._i18n["Audit.Cat.Group"]}"
                    : $"🏠 {this._i18n["Publisher.TargetModePersonal"]}";
                TextBlock targetTb = SectionHeader.CreateLabel(
                    this._theme,
                    $"{targetLabel}  •  {rec.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}",
                    11,
                    FontWeight.Medium,
                    isSecondary: true);
                targetTb.MaxLines = 1;
                targetTb.TextTrimming = TextTrimming.CharacterEllipsis;

                authorAndTarget.Children.Add(authorTb);
                authorAndTarget.Children.Add(targetTb);

                Border kindPill = StatusPill.Create(
                    this._theme,
                    isGroup ? this._i18n["Publisher.BadgeGroupPost"] : this._i18n["Publisher.BadgePersonalPost"],
                    isGroup ? AuditSeverity.Info : AuditSeverity.Success);

                Grid.SetColumn(av, 0);
                Grid.SetColumn(authorAndTarget, 1);
                Grid.SetColumn(kindPill, 2);
                topMeta.Children.Add(av);
                topMeta.Children.Add(authorAndTarget);
                topMeta.Children.Add(kindPill);

                TextBlock bodyTb = SectionHeader.CreateLabel(this._theme, rec.MessageText, 12.5, FontWeight.Normal);
                bodyTb.MaxLines = 2;
                bodyTb.TextWrapping = TextWrapping.Wrap;
                bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

                Grid.SetRow(topMeta, 0);
                Grid.SetRow(bodyTb, 1);
                cardGrid.Children.Add(topMeta);
                cardGrid.Children.Add(bodyTb);

                return SurfaceCard.CreateInset(this._theme, cardGrid, new Thickness(10, 8));
            },
            selectedItem: this._vm.SelectedPublishedPost,
            isVisible: filteredPosts.Count > 0);

        postsList.SelectionChanged += (_, _) =>
        {
            if (postsList.SelectedItem is PublishedPostRecord sel)
            {
                this._vm.SelectedPublishedPost = sel;
            }
        };

        Border emptyPostsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.PublishedPosts"],
            VectorIcons.Publish,
            out TextBlock emptyPostsTb);
        emptyPostsBanner.IsVisible = filteredPosts.Count == 0;

        this._vm.PublishedPosts.CollectionChanged += (_, _) =>
        {
            RefreshFilteredPosts();
            bool isEmpty = filteredPosts.Count == 0;
            emptyPostsBanner.IsVisible = isEmpty;
            postsList.IsVisible = !isEmpty;
        };
        filteredPosts.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredPosts.Count == 0;
            emptyPostsBanner.IsVisible = isEmpty;
            postsList.IsVisible = !isEmpty;
        };

        Grid leftListHost = EmptyStateBanner.CreateHost(postsList, emptyPostsBanner);

        // Right Column: Repost Selected Post to Groups / Colored Tags
        Grid repostCardGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8
        };

        TextBlock repostHeaderTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Publisher.RepostPanelTitle"],
            13.5,
            FontWeight.Bold);
        Grid.SetRow(repostHeaderTb, 0);
        repostCardGrid.Children.Add(repostHeaderTb);

        TextBox repostCaptionBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.RepostCaptionPlaceholder"],
            this._vm.RepostCustomSpintaxText,
            multiline: true,
            height: 82);
        repostCaptionBox.TextChanged += (_, _) =>
            this._vm.RepostCustomSpintaxText = repostCaptionBox.Text ?? string.Empty;
        Grid.SetRow(repostCaptionBox, 1);
        repostCardGrid.Children.Add(repostCaptionBox);

        Button repostNowBtn = null!;

        void UpdateRepostButtonLabel()
        {
            if (repostNowBtn is null)
            {
                return;
            }

            int enabledGroups = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledGroups++;
                }
            }

            AppButton.SetText(
                repostNowBtn,
                this._i18n.Format("Publisher.BtnRepostToGroupsDynamic", enabledGroups));
        }

        Control embeddedGroupTagPicker = this.BuildTargetGroupAndTagPickerCard(
            out Action refreshRepostPicker,
            onSelectionChanged: UpdateRepostButtonLabel);
        Grid.SetRow(embeddedGroupTagPicker, 2);
        repostCardGrid.Children.Add(embeddedGroupTagPicker);

        repostNowBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n.Format("Publisher.BtnRepostToGroupsDynamic", 0),
            this._vm.RepostSelectedPostToGroupsCommand,
            VectorIcons.Publish);
        repostNowBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        UpdateRepostButtonLabel();

        Grid.SetRow(repostNowBtn, 3);
        repostCardGrid.Children.Add(repostNowBtn);

        Border rightRepostCard = SurfaceCard.Create(this._theme, repostCardGrid, new Thickness(14));

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedPublishedPost), StringComparison.Ordinal) &&
                !ReferenceEquals(postsList.SelectedItem, this._vm.SelectedPublishedPost))
            {
                postsList.SelectedItem = this._vm.SelectedPublishedPost;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.RepostCustomSpintaxText), StringComparison.Ordinal) &&
                     !string.Equals(repostCaptionBox.Text, this._vm.RepostCustomSpintaxText, StringComparison.Ordinal))
            {
                repostCaptionBox.Text = this._vm.RepostCustomSpintaxText;
            }
        };

        Grid.SetColumn(leftListHost, 0);
        Grid.SetColumn(rightRepostCard, 1);
        masterDetailGrid.Children.Add(leftListHost);
        masterDetailGrid.Children.Add(rightRepostCard);
        Grid.SetRow(masterDetailGrid, 2);
        tabGrid.Children.Add(masterDetailGrid);

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.PostManagerTitle"];
            descTb.Text = this._i18n["Publisher.PostManagerDesc"];
            AppButton.SetText(syncTimelineBtn, this._i18n["Publisher.BtnSyncTimelinePosts"]);
            AppButton.SetText(openBrowserBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
            AppButton.SetText(deletePostBtn, this._i18n["Publisher.BtnDeletePublishedPost"]);
            sourceFilterPill.UpdateLabels(
            [
                this._i18n["Publisher.FilterAllPosts"],
                this._i18n["Publisher.FilterPersonalPosts"],
                this._i18n["Publisher.FilterGroupPosts"]
            ]);
            postSearchBox.PlaceholderText = this._i18n["Publisher.SearchPublishedPlaceholder"];
            emptyPostsTb.Text = this._i18n["Empty.PublishedPosts"];
            repostHeaderTb.Text = this._i18n["Publisher.RepostPanelTitle"];
            repostCaptionBox.PlaceholderText = this._i18n["Publisher.RepostCaptionPlaceholder"];
            refreshRepostPicker();
            UpdateRepostButtonLabel();
        };

        return tabGrid;
    }

    private Control BuildTemplatesAndBumpCombinedTab(TextBox contentBox, TextBox photoBox, out Action refreshI18n)
    {
        Grid combinedGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 14
        };

        Control tplCard = SurfaceCard.Create(
            this._theme,
            this.BuildTemplatesTab(contentBox, photoBox, out Action refreshTpl),
            new Thickness(16));
        Control bumpCard = this.BuildBumpTab(out Action refreshBump);

        Grid.SetColumn(tplCard, 0);
        Grid.SetColumn(bumpCard, 1);
        combinedGrid.Children.Add(tplCard);
        combinedGrid.Children.Add(bumpCard);

        refreshI18n = () =>
        {
            refreshTpl();
            refreshBump();
        };

        return combinedGrid;
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
                StackPanel sp = new() { Spacing = 4 };
                sp.Children.Add(SectionHeader.CreateLabel(this._theme, tpl.Title, 13.5, FontWeight.SemiBold));
                TextBlock bodyTb = SectionHeader.CreateLabel(this._theme, tpl.SpintaxContent, 12, FontWeight.Normal, isSecondary: true);
                bodyTb.MaxLines = 2;
                sp.Children.Add(bodyTb);
                return SurfaceCard.Create(this._theme, sp, new Thickness(12, 10));
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

        return tplGrid;
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
}
