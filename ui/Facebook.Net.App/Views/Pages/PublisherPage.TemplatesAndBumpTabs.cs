// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private Control BuildPublishedPostsAndRepostTab(Action? onSwitchToComposeTab, out Action refreshI18n)
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

        Button openBrowserBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.ExternalLink,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenPublishedPostInBrowserCommand,
            size: 34,
            iconSize: 16);

        Button deletePostBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.Trash,
            this._i18n["Publisher.BtnDeletePublishedPost"],
            command: null,
            size: 34,
            iconSize: 16,
            tone: AuditSeverity.Error);
        deletePostBtn.Click += (_, _) =>
        {
            if (this._vm.SelectedPublishedPost is null)
            {
                return;
            }

            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Confirm.DeletePublishedPostTitle"],
                this._i18n["Confirm.DeletePublishedPostMessage"],
                this._i18n["Confirm.BtnConfirmDelete"],
                this._i18n["Groups.BtnCancelEditTag"],
                async () =>
                {
                    if (this._vm.DeletePublishedPostCommand.CanExecute(null))
                    {
                        await this._vm.DeletePublishedPostCommand.ExecuteAsync(null).ConfigureAwait(true);
                    }
                });
        };

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

        // Row 1: Unified SearchFilterBar with 2-Level Drill-Down Categories (Source + Target Group)
        BulkObservableCollection<PublishedPostRecord> filteredPosts = new();
        string? selectedSourceFilter = null;
        string? selectedGroupFilter = null;
        SearchFilterBar postsSearchBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Publisher.SearchPublishedPlaceholder"]);

        void RefreshFilteredPosts()
        {
            string q = (postsSearchBar.SearchTextBox.Text ?? string.Empty).Trim();
            List<PublishedPostRecord> matches = new(this._vm.PublishedPosts.Count);
            for (int i = 0; i < this._vm.PublishedPosts.Count; i++)
            {
                PublishedPostRecord rec = this._vm.PublishedPosts[i];
                bool isGroup = !rec.IsPersonalProfilePost;
                if (string.Equals(selectedSourceFilter, "personal", StringComparison.OrdinalIgnoreCase) && isGroup)
                {
                    continue;
                }

                if (string.Equals(selectedSourceFilter, "group", StringComparison.OrdinalIgnoreCase) && !isGroup)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(selectedGroupFilter))
                {
                    bool matchesGroupId = string.Equals(rec.TargetGroupId, selectedGroupFilter, StringComparison.OrdinalIgnoreCase);
                    bool matchesGroupName = string.Equals(rec.TargetGroupName, selectedGroupFilter, StringComparison.OrdinalIgnoreCase);
                    if (!matchesGroupId && !matchesGroupName)
                    {
                        continue;
                    }
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

        FilterCategoryDefinition sourceCategory = new(
            "source",
            this._i18n["Publisher.FilterCatSource"],
            VectorIcons.Publish,
            () =>
            [
                new FilterOptionItem("personal", this._i18n["Publisher.FilterPersonalPosts"]),
                new FilterOptionItem("group", this._i18n["Publisher.FilterGroupPosts"])
            ],
            ids =>
            {
                selectedSourceFilter = ids.Count > 0 ? ids[0] : null;
                RefreshFilteredPosts();
            },
            allowMultiple: false);

        FilterCategoryDefinition groupCategory = new(
            "group",
            this._i18n["Publisher.FilterCatTargetGroup"],
            VectorIcons.Groups,
            () =>
            {
                List<FilterOptionItem> opts = new(this._vm.TargetGroups.Count);
                for (int i = 0; i < this._vm.TargetGroups.Count; i++)
                {
                    SavedTargetGroup tg = this._vm.TargetGroups[i];
                    opts.Add(new FilterOptionItem(tg.GroupId, tg.GroupName));
                }

                return opts;
            },
            ids =>
            {
                selectedGroupFilter = ids.Count > 0 ? ids[0] : null;
                RefreshFilteredPosts();
            },
            allowMultiple: false);

        postsSearchBar.SetFilterCategories(
            [sourceCategory, groupCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        postsSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredPosts();
        postsSearchBar.SearchButton.Click += (_, _) => RefreshFilteredPosts();
        Grid.SetRow(postsSearchBar, 1);
        tabGrid.Children.Add(postsSearchBar);

        this._vm.TargetGroups.CollectionChanged += (_, _) => postsSearchBar.RefreshFilterChips();
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
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,114,28,28"),
                    ColumnSpacing = 8,
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
                kindPill.HorizontalAlignment = HorizontalAlignment.Stretch;
                kindPill.Height = 28;

                Button rowOpenBtn = AppButton.CreateRowIcon(
                    this._theme,
                    VectorIcons.ExternalLink,
                    this._i18n["Leads.BtnOpenPostInBrowser"],
                    () =>
                    {
                        this._vm.SelectedPublishedPost = rec;
                        if (this._vm.OpenPublishedPostInBrowserCommand.CanExecute(null))
                        {
                            this._vm.OpenPublishedPostInBrowserCommand.Execute(null);
                        }
                    });

                Button rowDeleteBtn = AppButton.CreateRowIcon(
                    this._theme,
                    VectorIcons.Trash,
                    this._i18n["Publisher.BtnDeletePublishedPost"],
                    () =>
                    {
                        this._vm.SelectedPublishedPost = rec;
                        ConfirmDialogModal.ShowAsync(
                            this,
                            this._theme,
                            this._i18n["Confirm.DeletePublishedPostTitle"],
                            this._i18n["Confirm.DeletePublishedPostMessage"],
                            this._i18n["Confirm.BtnConfirmDelete"],
                            this._i18n["Groups.BtnCancelEditTag"],
                            async () =>
                            {
                                this._vm.SelectedPublishedPost = rec;
                                if (this._vm.DeletePublishedPostCommand.CanExecute(null))
                                {
                                    await this._vm.DeletePublishedPostCommand.ExecuteAsync(null).ConfigureAwait(true);
                                }
                            });
                    },
                    AuditSeverity.Error);

                Grid.SetColumn(av, 0);
                Grid.SetColumn(authorAndTarget, 1);
                Grid.SetColumn(kindPill, 2);
                Grid.SetColumn(rowOpenBtn, 3);
                Grid.SetColumn(rowDeleteBtn, 4);
                topMeta.Children.Add(av);
                topMeta.Children.Add(authorAndTarget);
                topMeta.Children.Add(kindPill);
                topMeta.Children.Add(rowOpenBtn);
                topMeta.Children.Add(rowDeleteBtn);

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
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 8
        };

        TextBlock repostHeaderTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Publisher.RepostPanelTitle"],
            13.5,
            FontWeight.Bold);
        repostHeaderTb.VerticalAlignment = VerticalAlignment.Center;

        TextBox repostCaptionBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.RepostCaptionPlaceholder"],
            this._vm.RepostCustomSpintaxText,
            multiline: true,
            height: 82);
        repostCaptionBox.TextChanged += (_, _) =>
            this._vm.RepostCustomSpintaxText = repostCaptionBox.Text ?? string.Empty;

        Button insertRepostVarBtn = this.CreateVariableInsertFlyoutButton(repostCaptionBox, out Action rebuildRepostVars);
        insertRepostVarBtn.Padding = new Thickness(8, 4);

        Grid repostHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(repostHeaderTb, 0);
        Grid.SetColumn(insertRepostVarBtn, 1);
        repostHeaderRow.Children.Add(repostHeaderTb);
        repostHeaderRow.Children.Add(insertRepostVarBtn);

        Grid.SetRow(repostHeaderRow, 0);
        Grid.SetRow(repostCaptionBox, 1);
        repostCardGrid.Children.Add(repostHeaderRow);
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

            string repostLabel = !string.IsNullOrWhiteSpace(this._vm.CampaignProgressText)
                ? this._vm.CampaignProgressText
                : this._i18n.Format("Publisher.BtnRepostToGroupsDynamic", enabledGroups);

            AppButton.SetText(repostNowBtn, repostLabel);
        }

        Control embeddedGroupTagPicker = this.BuildTargetGroupAndTagPickerCard(
            out Action refreshRepostPicker,
            onSelectionChanged: UpdateRepostButtonLabel);
        Grid.SetRow(embeddedGroupTagPicker, 2);
        repostCardGrid.Children.Add(embeddedGroupTagPicker);

        Grid quickPostActionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };

        Button cloneToComposerBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnCloneToComposer"],
            command: null,
            iconSvg: VectorIcons.Edit);
        cloneToComposerBtn.Click += (_, _) =>
        {
            this._vm.ClonePublishedPostToComposer(this._vm.SelectedPublishedPost);
            onSwitchToComposeTab?.Invoke();
        };

        Button bumpSingleNowBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnBumpSelectedNow"],
            command: null,
            iconSvg: VectorIcons.SalesComment);
        bumpSingleNowBtn.Click += async (_, _) =>
            await this._vm.BumpSinglePublishedPostAsync(this._vm.SelectedPublishedPost).ConfigureAwait(true);

        Grid.SetColumn(cloneToComposerBtn, 0);
        Grid.SetColumn(bumpSingleNowBtn, 1);
        quickPostActionsRow.Children.Add(cloneToComposerBtn);
        quickPostActionsRow.Children.Add(bumpSingleNowBtn);
        Grid.SetRow(quickPostActionsRow, 3);
        repostCardGrid.Children.Add(quickPostActionsRow);

        repostNowBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n.Format("Publisher.BtnRepostToGroupsDynamic", 0),
            this._vm.RepostSelectedPostToGroupsCommand,
            VectorIcons.Publish);
        repostNowBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        UpdateRepostButtonLabel();

        Grid.SetRow(repostNowBtn, 4);
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
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.CampaignProgressText), StringComparison.Ordinal))
            {
                UpdateRepostButtonLabel();
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
            sourceCategory.Title = this._i18n["Publisher.FilterCatSource"];
            groupCategory.Title = this._i18n["Publisher.FilterCatTargetGroup"];
            postsSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Publisher.SearchPublishedPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            emptyPostsTb.Text = this._i18n["Empty.PublishedPosts"];
            repostHeaderTb.Text = this._i18n["Publisher.RepostPanelTitle"];
            repostCaptionBox.PlaceholderText = this._i18n["Publisher.RepostCaptionPlaceholder"];
            AppButton.SetText(cloneToComposerBtn, this._i18n["Publisher.BtnCloneToComposer"]);
            AppButton.SetText(bumpSingleNowBtn, this._i18n["Publisher.BtnBumpSelectedNow"]);
            rebuildRepostVars();
            refreshRepostPicker();
            RefreshFilteredPosts();
            UpdateRepostButtonLabel();
        };

        return tabGrid;
    }

    private Control BuildTemplatesAndBumpCombinedTab(
        TextBox contentBox,
        TextBox photoBox,
        Action onSelectComposeTab,
        out Action refreshI18n)
    {
        Grid combinedGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 14
        };

        Control bumpCard = this.BuildBumpTab(out TextBox bumpBox, out Action refreshBump);
        Control tplCard = SurfaceCard.Create(
            this._theme,
            this.BuildTemplatesTab(contentBox, photoBox, bumpBox, onSelectComposeTab, out Action refreshTpl),
            new Thickness(16));

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

    private Control BuildTemplatesTab(
        TextBox contentBox,
        TextBox photoBox,
        TextBox bumpBox,
        Action onSelectComposeTab,
        out Action refreshI18n)
    {
        Grid tplGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
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

        BulkObservableCollection<CampaignTemplate> filteredTemplates = new();
        string? selectedTypeFilter = null;
        SearchFilterBar tplSearchBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Publisher.SearchTemplatesPlaceholder"]);

        void RefreshFilteredTemplates()
        {
            string q = (tplSearchBar.SearchTextBox.Text ?? string.Empty).Trim();
            List<CampaignTemplate> matches = new(this._vm.SavedTemplates.Count);
            for (int i = 0; i < this._vm.SavedTemplates.Count; i++)
            {
                CampaignTemplate tpl = this._vm.SavedTemplates[i];
                if (string.Equals(selectedTypeFilter, "post", StringComparison.OrdinalIgnoreCase) && tpl.IsCommentTemplate)
                {
                    continue;
                }

                if (string.Equals(selectedTypeFilter, "comment", StringComparison.OrdinalIgnoreCase) && !tpl.IsCommentTemplate)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(q) &&
                    !tpl.Title.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !tpl.SpintaxContent.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(tpl);
            }

            filteredTemplates.ReplaceAll(matches);
        }

        FilterCategoryDefinition typeCategory = new(
            "type",
            this._i18n["Publisher.FilterCatTemplateType"],
            VectorIcons.Bookmark,
            () =>
            [
                new FilterOptionItem("post", this._i18n["Publisher.FilterTemplatesPost"]),
                new FilterOptionItem("comment", this._i18n["Publisher.FilterTemplatesComment"])
            ],
            ids =>
            {
                selectedTypeFilter = ids.Count > 0 ? ids[0] : null;
                RefreshFilteredTemplates();
            },
            allowMultiple: false);

        tplSearchBar.SetFilterCategories(
            [typeCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        tplSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredTemplates();
        tplSearchBar.SearchButton.Click += (_, _) => RefreshFilteredTemplates();
        Grid.SetRow(tplSearchBar, 1);
        tplGrid.Children.Add(tplSearchBar);

        RefreshFilteredTemplates();

        ListBox tplList = FormField.CreateListBox<CampaignTemplate>(
            this._theme,
            filteredTemplates,
            tpl =>
            {
                Grid cardGrid = new()
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                    RowSpacing = 6
                };

                Grid topRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("*,110,108,52,28"),
                    ColumnSpacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock tplTitleTb = SectionHeader.CreateLabel(this._theme, tpl.Title, 13.5, FontWeight.SemiBold);
                tplTitleTb.TextTrimming = TextTrimming.CharacterEllipsis;

                Border typePill = StatusPill.Create(
                    this._theme,
                    tpl.IsCommentTemplate ? this._i18n["Publisher.FilterTemplatesComment"] : this._i18n["Publisher.FilterTemplatesPost"],
                    tpl.IsCommentTemplate ? AuditSeverity.Info : AuditSeverity.Success);
                typePill.HorizontalAlignment = HorizontalAlignment.Stretch;
                typePill.Height = 28;

                Button useBtn = new()
                {
                    Content = new TextBlock
                    {
                        Text = !tpl.IsCommentTemplate
                            ? this._i18n["Publisher.BtnUsePostTemplate"]
                            : this._i18n["Publisher.BtnUseCommentTemplate"],
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = this._theme.Palette.BrandPrimary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Background = this._theme.Palette.BrandSubtleFill,
                    BorderBrush = this._theme.Palette.BrandPrimary,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Height = 28,
                    Padding = new Thickness(8, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                useBtn.Click += (_, _) =>
                {
                    this._vm.SelectedTemplate = tpl;
                    if (!tpl.IsCommentTemplate)
                    {
                        contentBox.Text = tpl.SpintaxContent;
                        this._vm.PostSpintaxText = tpl.SpintaxContent;

                        string mediaPath = tpl.MediaFilePath ?? string.Empty;
                        photoBox.Text = mediaPath;
                        this._vm.PostMediaFilePath = mediaPath;

                        onSelectComposeTab?.Invoke();
                        _ = contentBox.Focus();
                    }
                    else
                    {
                        this._vm.BumpCommentSpintaxText = tpl.SpintaxContent;
                        this._vm.SalesCommentSpintaxText = tpl.SpintaxContent;
                        _ = bumpBox.Focus();
                    }
                };

                StackPanel inlineEditPanel = new()
                {
                    Spacing = 6,
                    IsVisible = false,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                TextBox editTitleBox = FormField.CreateTextBox(
                    this._theme,
                    this._i18n["Publisher.TemplatesTitle"],
                    tpl.Title);
                TextBox editContentBox = FormField.CreateTextBox(
                    this._theme,
                    this._i18n["Publisher.ContentPlaceholder"],
                    tpl.SpintaxContent,
                    multiline: true,
                    height: 76);

                StackPanel editActionsRow = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                Button saveEditBtn = AppButton.CreatePrimary(
                    this._theme,
                    this._i18n["Publisher.BtnSaveEditTemplate"],
                    new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
                    {
                        inlineEditPanel.IsVisible = false;
                        await this._vm.UpdateTemplateContentAsync(
                            tpl,
                            editTitleBox.Text ?? tpl.Title,
                            editContentBox.Text ?? tpl.SpintaxContent).ConfigureAwait(true);
                    }),
                    VectorIcons.CheckCircle);

                Button cancelEditBtn = AppButton.CreateSecondary(
                    this._theme,
                    this._i18n["Groups.BtnCancelEditTag"],
                    new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        editTitleBox.Text = tpl.Title;
                        editContentBox.Text = tpl.SpintaxContent;
                        inlineEditPanel.IsVisible = false;
                    }));

                editActionsRow.Children.Add(cancelEditBtn);
                editActionsRow.Children.Add(saveEditBtn);
                inlineEditPanel.Children.Add(editTitleBox);
                inlineEditPanel.Children.Add(editContentBox);
                inlineEditPanel.Children.Add(editActionsRow);

                Button editRowBtn = new()
                {
                    Content = SectionHeader.CreateLabel(this._theme, this._i18n["Publisher.BtnEditTemplateShort"], 11, FontWeight.SemiBold),
                    Background = this._theme.Palette.SecondarySurface,
                    BorderBrush = this._theme.Palette.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Height = 28,
                    Padding = new Thickness(8, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                editRowBtn.Click += (_, _) => inlineEditPanel.IsVisible = !inlineEditPanel.IsVisible;

                Button deleteRowBtn = AppButton.CreateRowIcon(
                    this._theme,
                    VectorIcons.Trash,
                    this._i18n["Groups.BtnDeleteTagShort"],
                    () =>
                    {
                        ConfirmDialogModal.ShowAsync(
                            this,
                            this._theme,
                            this._i18n["Confirm.DeleteTemplateTitle"],
                            this._i18n.Format("Confirm.DeleteTemplateMessage", tpl.Title),
                            this._i18n["Confirm.BtnConfirmDelete"],
                            this._i18n["Groups.BtnCancelEditTag"],
                            async () =>
                            {
                                this._vm.SelectedTemplate = tpl;
                                if (this._vm.DeleteSelectedTemplateCommand.CanExecute(null))
                                {
                                    await this._vm.DeleteSelectedTemplateCommand.ExecuteAsync(null).ConfigureAwait(true);
                                }
                            });
                    },
                    AuditSeverity.Error);

                Grid.SetColumn(tplTitleTb, 0);
                Grid.SetColumn(typePill, 1);
                Grid.SetColumn(useBtn, 2);
                Grid.SetColumn(editRowBtn, 3);
                Grid.SetColumn(deleteRowBtn, 4);
                topRow.Children.Add(tplTitleTb);
                topRow.Children.Add(typePill);
                topRow.Children.Add(useBtn);
                topRow.Children.Add(editRowBtn);
                topRow.Children.Add(deleteRowBtn);

                TextBlock bodyTb = SectionHeader.CreateLabel(this._theme, tpl.SpintaxContent, 12, FontWeight.Normal, isSecondary: true);
                bodyTb.MaxLines = 2;
                bodyTb.TextWrapping = TextWrapping.Wrap;
                bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

                Grid.SetRow(topRow, 0);
                Grid.SetRow(bodyTb, 1);
                Grid.SetRow(inlineEditPanel, 2);
                cardGrid.Children.Add(topRow);
                cardGrid.Children.Add(bodyTb);
                cardGrid.Children.Add(inlineEditPanel);

                return SurfaceCard.Create(this._theme, cardGrid, new Thickness(12, 10));
            },
            selectedItem: this._vm.SelectedTemplate,
            isVisible: filteredTemplates.Count > 0);

        tplList.SelectionChanged += (_, _) =>
        {
            if (tplList.SelectedItem is CampaignTemplate selectedTpl)
            {
                this._vm.SelectedTemplate = selectedTpl;
            }
        };

        Border emptyTemplatesBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.Templates"],
            VectorIcons.Bookmark,
            out TextBlock emptyTplTb);
        emptyTemplatesBanner.IsVisible = filteredTemplates.Count == 0;

        this._vm.SavedTemplates.CollectionChanged += (_, _) =>
        {
            RefreshFilteredTemplates();
            bool isEmpty = filteredTemplates.Count == 0;
            emptyTemplatesBanner.IsVisible = isEmpty;
            tplList.IsVisible = !isEmpty;
        };
        filteredTemplates.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredTemplates.Count == 0;
            emptyTemplatesBanner.IsVisible = isEmpty;
            tplList.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(tplList, emptyTemplatesBanner);
        Grid.SetRow(listHost, 2);
        tplGrid.Children.Add(listHost);

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.TemplatesTitle"];
            descTb.Text = this._i18n["Publisher.Desc"];
            typeCategory.Title = this._i18n["Publisher.FilterCatTemplateType"];
            tplSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Publisher.SearchTemplatesPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            emptyTplTb.Text = this._i18n["Empty.Templates"];
            RefreshFilteredTemplates();
        };

        return tplGrid;
    }

    private Control BuildBumpTab(out TextBox bumpBox, out Action refreshI18n)
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

        TextBox localBumpBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Publisher.BumpPlaceholder"],
            this._vm.BumpCommentSpintaxText,
            multiline: true,
            height: 160);
        localBumpBox.VerticalAlignment = VerticalAlignment.Stretch;
        localBumpBox.TextChanged += (_, _) => this._vm.BumpCommentSpintaxText = localBumpBox.Text ?? string.Empty;
        Grid.SetRow(localBumpBox, 1);
        bumpGrid.Children.Add(localBumpBox);
        bumpBox = localBumpBox;

        Grid bumpActionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 10
        };

        Button insertBumpVarBtn = this.CreateVariableInsertFlyoutButton(localBumpBox, out Action rebuildBumpVars);

        Button saveBumpTplBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Publisher.BtnSaveBumpTemplate"],
            this._vm.SaveBumpTemplateCommand,
            VectorIcons.Bookmark);

        Button bumpBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Publisher.BtnBumpRecentPosts"],
            this._vm.BumpRecentPostsCommand,
            VectorIcons.SalesComment);
        bumpBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid.SetColumn(insertBumpVarBtn, 0);
        Grid.SetColumn(saveBumpTplBtn, 1);
        Grid.SetColumn(bumpBtn, 2);
        bumpActionsRow.Children.Add(insertBumpVarBtn);
        bumpActionsRow.Children.Add(saveBumpTplBtn);
        bumpActionsRow.Children.Add(bumpBtn);
        Grid.SetRow(bumpActionsRow, 2);
        bumpGrid.Children.Add(bumpActionsRow);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.BumpCommentSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(localBumpBox.Text, this._vm.BumpCommentSpintaxText, StringComparison.Ordinal))
            {
                localBumpBox.Text = this._vm.BumpCommentSpintaxText;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Publisher.BumpTitle"];
            descTb.Text = this._i18n["Publisher.Desc"];
            localBumpBox.PlaceholderText = this._i18n["Publisher.BumpPlaceholder"];
            rebuildBumpVars();
            AppButton.SetText(saveBumpTplBtn, this._i18n["Publisher.BtnSaveBumpTemplate"]);
            AppButton.SetText(bumpBtn, this._i18n["Publisher.BtnBumpRecentPosts"]);
        };

        return SurfaceCard.Create(this._theme, bumpGrid, new Thickness(18));
    }
}
