// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Buyer Lead Scanner &amp; Sales Commenting Page (<c>SalesLeadsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to cleanly separate:
/// 1) Local SQLite Post Repository &amp; Master-Detail Inspector (<see cref="DataTableView{T}"/> + <see cref="SearchFilterBar"/>),
/// 2) Multi-Group Selector &amp; Anti-Ban Rate-Limit Scraper Configuration,
/// 3) Sales Comment Composer &amp; Batch Auto-Commenter.
/// </summary>
public sealed partial class SalesLeadsPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesLeadsPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public SalesLeadsPage()
    {
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesLeadsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public SalesLeadsPage(ShellViewModel viewModel)
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
        Control tab1View = this.BuildRepositoryAndFilterTab(() => subTabBar?.SelectTab(2), out Action refreshTab1I18n);
        Control tab2View = this.BuildMultiGroupAndScraperConfigTab(out Action refreshTab2I18n);
        Control tab3View = this.BuildSalesAutoCommentTab(out Action refreshTab3I18n);

        subTabBar = new SubTabBar(
            this._theme,
            [
                new SubTabItem(this._i18n["Leads.TabRepository"], VectorIcons.Search, tab1View),
                new SubTabItem(this._i18n["Leads.TabScraperConfig"], VectorIcons.Settings, tab2View),
                new SubTabItem(this._i18n["Leads.TabAutoComment"], VectorIcons.SalesComment, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Leads.TabRepository"],
                this._i18n["Leads.TabScraperConfig"],
                this._i18n["Leads.TabAutoComment"]
            ]);
            refreshTab1I18n();
            refreshTab2I18n();
            refreshTab3I18n();
        };

        return subTabBar;
    }

    private Control BuildRepositoryAndFilterTab(Action onNavigateToAutoCommentTab, out Action refreshI18n)
    {
        Grid tabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        // 1. Page Title + Quick Action Bar
        Grid topHeaderBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Leads.Header"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button exportCsvBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnExportCsv"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
            {
                string? savePath = await this.PickSaveCsvFileAsync(this._i18n["Leads.BtnExportCsv"]).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    await this._vm.ExportFilteredLeadsToCsvAsync(savePath).ConfigureAwait(true);
                }
            }),
            VectorIcons.Clipboard);

        Button scrapeNowBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnScanPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(exportCsvBtn, 1);
        Grid.SetColumn(scrapeNowBtn, 2);
        topHeaderBar.Children.Add(header);
        topHeaderBar.Children.Add(exportCsvBtn);
        topHeaderBar.Children.Add(scrapeNowBtn);
        Grid.SetRow(topHeaderBar, 0);
        tabGrid.Children.Add(topHeaderBar);

        // 2. Unified Search & Filter Bar ([ ▽ Bộ lọc | Nhập từ khóa... | 🔍 ]) with 2-Level Drill-Down Popover
        SearchFilterBar searchFilterBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords,
            searchCommand: this._vm.QueryLocalScrapedPostsCommand);

        FilterCategoryDefinition statusCategory = new(
            "status",
            this._i18n["Leads.ColStatus"],
            VectorIcons.CheckCircle,
            () =>
            [
                new FilterOptionItem("pending", this._i18n["Leads.FilterSegPending"]),
                new FilterOptionItem("commented", this._i18n["Leads.FilterSegCommented"])
            ],
            selectedIds =>
            {
                string? sel = selectedIds.Count > 0 ? selectedIds[0] : null;
                this._vm.LeadCommentStatusFilter = sel switch
                {
                    "pending" => 1,
                    "commented" => 2,
                    _ => 0
                };
            },
            allowMultiple: false);

        FilterCategoryDefinition intentCategory = new(
            "intent",
            this._i18n["Leads.FilterCatIntent"],
            VectorIcons.Shield,
            () =>
            [
                new FilterOptionItem("has_phone", this._i18n["Leads.FilterHasPhone"]),
                new FilterOptionItem("no_phone", this._i18n["Leads.FilterNoPhone"]),
                new FilterOptionItem("high_intent", this._i18n["Leads.FilterHighIntent"]),
                new FilterOptionItem("inquiry", this._i18n["Leads.FilterInquiryQuestion"])
            ],
            this._vm.SetLeadIntentFilters,
            allowMultiple: true);

        FilterCategoryDefinition competitionCategory = new(
            "competition",
            this._i18n["Leads.FilterCatCompetition"],
            VectorIcons.ThumbUp,
            () =>
            [
                new FilterOptionItem("zero_comments", this._i18n["Leads.FilterZeroComments"]),
                new FilterOptionItem("low_comments", this._i18n["Leads.FilterLowComments"]),
                new FilterOptionItem("hot_engagement", this._i18n["Leads.FilterHotEngagement"])
            ],
            selectedIds => this._vm.SetLeadCompetitionFilter(selectedIds.Count > 0 ? selectedIds[0] : null),
            allowMultiple: false);

        FilterCategoryDefinition timeCategory = new(
            "time_range",
            this._i18n["Leads.FilterCatTimeRange"],
            VectorIcons.History,
            () =>
            [
                new FilterOptionItem("last_1h", this._i18n["Leads.FilterTime1h"]),
                new FilterOptionItem("last_24h", this._i18n["Leads.FilterTime24h"]),
                new FilterOptionItem("last_3d", this._i18n["Leads.FilterTime3d"]),
                new FilterOptionItem("last_7d", this._i18n["Leads.FilterTime7d"])
            ],
            selectedIds => this._vm.SetLeadTimeRangeFilter(selectedIds.Count > 0 ? selectedIds[0] : null),
            allowMultiple: false);

        FilterCategoryDefinition mediaCategory = new(
            "media_type",
            this._i18n["Leads.FilterCatMediaType"],
            VectorIcons.Publish,
            () =>
            [
                new FilterOptionItem("has_media", this._i18n["Leads.FilterMediaAny"]),
                new FilterOptionItem("has_photo", this._i18n["Leads.FilterMediaPhoto"]),
                new FilterOptionItem("has_video", this._i18n["Leads.FilterMediaVideo"]),
                new FilterOptionItem("text_only", this._i18n["Leads.FilterMediaTextOnly"])
            ],
            selectedIds => this._vm.SetLeadMediaFilter(selectedIds.Count > 0 ? selectedIds[0] : null),
            allowMultiple: false);

        FilterCategoryDefinition groupTagCategory = new(
            "group_tag",
            this._i18n["Groups.FilterCatColorTag"],
            VectorIcons.Bookmark,
            () =>
            {
                List<FilterOptionItem> options = new(this._vm.GroupTags.Count + 1);
                for (int i = 0; i < this._vm.GroupTags.Count; i++)
                {
                    GroupTag tag = this._vm.GroupTags[i];
                    options.Add(new FilterOptionItem(tag.Id, tag.Name, ColorHex: tag.ColorHex));
                }

                options.Add(new FilterOptionItem("__untagged__", this._i18n["Groups.FilterUntagged"]));
                return options;
            },
            this._vm.SetLeadGroupTagFilters,
            allowMultiple: true);

        FilterCategoryDefinition groupCategory = new(
            "group",
            this._i18n["Leads.FilterGroupLabel"],
            VectorIcons.Groups,
            () =>
            {
                List<(string GroupId, string GroupName)> availableGroups = this._vm.GetAvailableScrapedGroupsForFilter();
                List<FilterOptionItem> options = new(availableGroups.Count);
                for (int i = 0; i < availableGroups.Count; i++)
                {
                    (string gId, string gName) = availableGroups[i];
                    options.Add(new FilterOptionItem(gId, gName));
                }

                return options;
            },
            this._vm.SetLeadGroupFilters,
            allowMultiple: true);

        FilterCategoryDefinition leadTagCategory = new(
            "lead_tag",
            this._i18n["Leads.FilterCategoryLeadTag"],
            VectorIcons.Bookmark,
            () =>
            {
                List<FilterOptionItem> options = new(this._vm.LeadTags.Count + 1);
                for (int i = 0; i < this._vm.LeadTags.Count; i++)
                {
                    GroupTag tag = this._vm.LeadTags[i];
                    options.Add(new FilterOptionItem(tag.Id, tag.Name, ColorHex: tag.ColorHex));
                }

                options.Add(new FilterOptionItem("__untagged__", this._i18n["Leads.FilterLeadUntagged"]));
                return options;
            },
            this._vm.SetLeadCustomerTagFilters,
            allowMultiple: true);

        FilterCategoryDefinition sortCategory = new(
            "sort",
            this._i18n["Leads.FilterCatSort"],
            VectorIcons.History,
            () =>
            [
                new FilterOptionItem("intent_desc", this._i18n["Leads.SortIntentDesc"]),
                new FilterOptionItem("newest_first", this._i18n["Leads.SortNewestFirst"]),
                new FilterOptionItem("engagement_desc", this._i18n["Leads.SortEngagementDesc"]),
                new FilterOptionItem("uncommented_first", this._i18n["Leads.SortUncommentedFirst"])
            ],
            selectedIds => this._vm.SetLeadSortOrder(selectedIds.Count > 0 ? selectedIds[0] : null),
            allowMultiple: false);

        searchFilterBar.SetFilterCategories(
            [
                sortCategory,
                statusCategory,
                intentCategory,
                competitionCategory,
                timeCategory,
                mediaCategory,
                groupTagCategory,
                groupCategory,
                leadTagCategory
            ],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        this._vm.TargetGroups.CollectionChanged += (_, _) => searchFilterBar.RefreshFilterChips();
        this._vm.GroupTags.CollectionChanged += (_, _) => searchFilterBar.RefreshFilterChips();
        this._vm.LeadTags.CollectionChanged += (_, _) => searchFilterBar.RefreshFilterChips();

        searchFilterBar.SearchTextBox.TextChanged += (_, _) =>
            this._vm.ApplyLocalLeadFilter(searchFilterBar.SearchTextBox.Text);

        Grid.SetRow(searchFilterBar, 1);
        tabGrid.Children.Add(searchFilterBar);

        // 3. Master-Detail Split: Left = Spacious DataTableView<PostSummary>, Right = Selected Post Detail Inspector Card
        Grid masterDetailSplit = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,365"),
            ColumnSpacing = 12
        };

        DataTableView<PostSummary> postsTable = new(
            this._theme,
            [
                new DataTableColumn("150", this._i18n["Leads.ColAuthor"]),
                new DataTableColumn("145", this._i18n["Leads.ColGroup"]),
                new DataTableColumn("*", this._i18n["Leads.ColMessage"]),
                new DataTableColumn("135", this._i18n["Leads.ColStatus"])
            ],
            this._vm.ScrapedPosts,
            post =>
            {
                PostSummary enriched = string.IsNullOrWhiteSpace(post.GroupAvatarUrl)
                    ? post with { GroupAvatarUrl = this._vm.FindTargetGroupById(post.GroupId)?.AvatarUrl }
                    : post;
                return LeadPostTableRow.Create(
                    this._theme,
                    this._i18n,
                    enriched,
                    ShellViewModel.CalculateBuyerIntentScore(enriched.MessageText, this._vm.BuyerIntentKeywords),
                    () => this._vm.FormatBuyerIntentBadge(enriched),
                    this._vm.IsPostCommented(enriched.PostId),
                    onQuickReply: () =>
                    {
                        this._vm.SelectedScrapedPost = post;
                        onNavigateToAutoCommentTab?.Invoke();
                    },
                    quickReplyLabel: this._i18n["Leads.BtnRowQuickReply"],
                    onOpenInBrowser: () =>
                    {
                        this._vm.SelectedScrapedPost = post;
                        if (this._vm.OpenSelectedPostInBrowserCommand.CanExecute(null))
                        {
                            this._vm.OpenSelectedPostInBrowserCommand.Execute(null);
                        }
                    },
                    openBrowserLabel: this._i18n["Leads.BtnRowOpenBrowser"],
                    detectedPhone: this._vm.ExtractPostPhoneNumber(enriched),
                    assignedLeadTags: this._vm.ResolveLeadTags(enriched),
                    allLeadTags: this._vm.LeadTags,
                    onToggleLeadTag: tagId => this._vm.ToggleLeadPostTagAsync(post, tagId),
                    onCreateQuickLeadTag: async (name, color) =>
                    {
                        GroupTag? created = await this._vm.CreateQuickLeadTagAsync(name, color).ConfigureAwait(true);
                        if (created is not null)
                        {
                            await this._vm.ToggleLeadPostTagAsync(post, created.Id).ConfigureAwait(true);
                        }
                    });
            },
            this._i18n["Empty.Leads"],
            VectorIcons.Search,
            this._i18n["Leads.TableTitlePrefix"]);

        // Right Inspector Card: Chi tiết Bài viết Đang chọn
        Grid detailCardGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10
        };

        StackPanel detailHeaderRow = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Leads.DetailCardTitle"],
            VectorIcons.Publish,
            out TextBlock detailCardTitleTb);
        Grid.SetRow(detailHeaderRow, 0);
        detailCardGrid.Children.Add(detailHeaderRow);

        ContentControl detailBodyHost = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(detailBodyHost, 1);
        detailCardGrid.Children.Add(detailBodyHost);

        // Full-width stacked action buttons so long labels never clip
        StackPanel detailBtnStack = new()
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Button askAiConsultBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnAskAiConsult"],
            new CommunityToolkit.Mvvm.Input.RelayCommand(() => this._vm.AskAiToAnalyzeAndConsultLead(this._vm.SelectedScrapedPost)),
            VectorIcons.AiSparkle);
        askAiConsultBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        Button quickReplyBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnQuickReplyTab"],
            new CommunityToolkit.Mvvm.Input.RelayCommand(onNavigateToAutoCommentTab),
            VectorIcons.SalesComment);
        quickReplyBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        Button copyPhoneBtn = null!;
        copyPhoneBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnCopyPhone"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
            {
                string? primaryPhone = this._vm.ExtractPostPhoneNumber(this._vm.SelectedScrapedPost);
                if (!string.IsNullOrWhiteSpace(primaryPhone) && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(primaryPhone).ConfigureAwait(true);
                    AppButton.SetText(copyPhoneBtn, $"{this._i18n["Leads.PhoneCopiedShort"]}: {primaryPhone}");
                }
            }),
            VectorIcons.Clipboard);
        copyPhoneBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        copyPhoneBtn.IsVisible = false;

        Button markContactedBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnMarkContacted"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => this._vm.ToggleLeadContactedStatusAsync(this._vm.SelectedScrapedPost)),
            VectorIcons.CheckCircle);
        markContactedBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        Button inspectOpenBrowserBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenSelectedPostInBrowserCommand,
            VectorIcons.Browser);
        inspectOpenBrowserBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        detailBtnStack.Children.Add(askAiConsultBtn);
        detailBtnStack.Children.Add(quickReplyBtn);
        detailBtnStack.Children.Add(copyPhoneBtn);
        detailBtnStack.Children.Add(markContactedBtn);
        detailBtnStack.Children.Add(inspectOpenBrowserBtn);
        Grid.SetRow(detailBtnStack, 2);
        detailCardGrid.Children.Add(detailBtnStack);

        Border detailCard = SurfaceCard.Create(this._theme, detailCardGrid, new Thickness(14));

        void RefreshSelectedPostDetail()
        {
            PostSummary? post = this._vm.SelectedScrapedPost;
            if (post is null)
            {
                detailBtnStack.IsVisible = false;
                StackPanel emptyStack = new()
                {
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12)
                };
                Control emptyIcon = VectorIcons.Create(VectorIcons.Search, 28, this._theme.Palette.TextMuted);
                emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock emptyTb = SectionHeader.CreateLabel(
                    this._theme,
                    this._i18n["Leads.DetailNoSelection"],
                    12,
                    FontWeight.Normal,
                    isSecondary: true);
                emptyTb.TextAlignment = TextAlignment.Center;
                emptyTb.TextWrapping = TextWrapping.Wrap;
                emptyStack.Children.Add(emptyIcon);
                emptyStack.Children.Add(emptyTb);
                detailBodyHost.Content = SurfaceCard.CreateInset(this._theme, emptyStack, new Thickness(14));
                return;
            }

            detailBtnStack.IsVisible = true;
            StackPanel contentStack = new() { Spacing = 10 };

            // Full-width Author + Group Header (Zero Overlap)
            string authorName = string.IsNullOrWhiteSpace(post.AuthorName)
                ? this._i18n["Leads.DefaultAuthor"]
                : post.AuthorName;
            string groupName = string.IsNullOrWhiteSpace(post.GroupName)
                ? this._i18n["Audit.Cat.Group"]
                : post.GroupName;

            Grid authorHeader = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border authorAvatar = AvatarBadge.Create(this._theme, authorName, 38, post.AuthorAvatarUrl);
            StackPanel authorMeta = new() { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            TextBlock authorNameTb = SectionHeader.CreateLabel(this._theme, authorName, 13, FontWeight.Bold);
            authorNameTb.MaxLines = 1;
            authorNameTb.TextTrimming = TextTrimming.CharacterEllipsis;

            Grid groupBadgeRow = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            string? resolvedGroupAvatar = !string.IsNullOrWhiteSpace(post.GroupAvatarUrl)
                ? post.GroupAvatarUrl
                : this._vm.FindTargetGroupById(post.GroupId)?.AvatarUrl;
            Border grpTinyAvatar = AvatarBadge.Create(this._theme, groupName, 18, resolvedGroupAvatar);
            TextBlock grpTb = SectionHeader.CreateLabel(this._theme, groupName, 11.5, FontWeight.Medium, isSecondary: true);
            grpTb.MaxLines = 1;
            grpTb.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(grpTinyAvatar, 0);
            Grid.SetColumn(grpTb, 1);
            groupBadgeRow.Children.Add(grpTinyAvatar);
            groupBadgeRow.Children.Add(grpTb);

            authorMeta.Children.Add(authorNameTb);
            authorMeta.Children.Add(groupBadgeRow);

            Grid.SetColumn(authorAvatar, 0);
            Grid.SetColumn(authorMeta, 1);
            authorHeader.Children.Add(authorAvatar);
            authorHeader.Children.Add(authorMeta);
            contentStack.Children.Add(SurfaceCard.CreateInset(this._theme, authorHeader, new Thickness(10, 8)));

            // Status, Intent & Engagement Pill Wrap Row
            bool commented = this._vm.IsPostCommented(post.PostId);
            AppButton.SetText(
                markContactedBtn,
                commented ? this._i18n["Leads.BtnMarkPending"] : this._i18n["Leads.BtnMarkContacted"]);
            Border statusPill = StatusPill.Create(
                this._theme,
                commented ? this._i18n["Leads.StatusCommented"] : this._i18n["Leads.StatusPending"],
                commented ? AuditSeverity.Success : AuditSeverity.Warning);

            int score = ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords);
            AuditSeverity intentSeverity = score >= 80
                ? AuditSeverity.Success
                : (score >= 45 ? AuditSeverity.Info : AuditSeverity.Warning);

            WrapPanel metaPillsWrap = new()
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6
            };
            metaPillsWrap.Children.Add(statusPill);
            metaPillsWrap.Children.Add(StatusPill.Create(this._theme, this._vm.FormatBuyerIntentBadge(post), intentSeverity));

            IReadOnlyList<string> phones = this._vm.ExtractAllPostPhoneNumbers(post);
            if (phones.Count > 0)
            {
                metaPillsWrap.Children.Add(StatusPill.Create(
                    this._theme,
                    $"{this._i18n["Leads.PhoneBadgePrefix"]} {string.Join(", ", phones)}",
                    AuditSeverity.Success));
                copyPhoneBtn.IsVisible = true;
                AppButton.SetText(copyPhoneBtn, $"{this._i18n["Leads.BtnCopyPhone"]}: {phones[0]}");
            }
            else
            {
                copyPhoneBtn.IsVisible = false;
            }

            metaPillsWrap.Children.Add(StatusPill.Create(
                this._theme,
                $"👍 {this._i18n["Leads.DetailReactionsPrefix"]} {post.ReactionCount}  •  💬 {this._i18n["Leads.DetailCommentsPrefix"]} {post.CommentCount}",
                AuditSeverity.Info));
            if (post.CreatedAt is DateTimeOffset dto)
            {
                metaPillsWrap.Children.Add(StatusPill.Create(
                    this._theme,
                    dto.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture),
                    AuditSeverity.Info));
            }

            contentStack.Children.Add(metaPillsWrap);

            // Customer Color Tags + (+ Gán nhãn) Flyout
            WrapPanel leadTagsWrap = LeadPostTableRow.BuildLeadTagsWrapRow(
                this._theme,
                this._i18n,
                post,
                this._vm.ResolveLeadTags(post),
                this._vm.LeadTags,
                tagId => this._vm.ToggleLeadPostTagAsync(post, tagId),
                async (name, color) =>
                {
                    GroupTag? created = await this._vm.CreateQuickLeadTagAsync(name, color).ConfigureAwait(true);
                    if (created is not null)
                    {
                        await this._vm.ToggleLeadPostTagAsync(post, created.Id).ConfigureAwait(true);
                    }
                });
            contentStack.Children.Add(leadTagsWrap);

            // Editable Customer CRM Notes Box
            TextBox notesBox = FormField.CreateTextBox(
                this._theme,
                this._i18n["Leads.NotesWatermark"],
                post.Notes ?? string.Empty,
                multiline: true,
                height: 54);
            notesBox.FontSize = 11.5;

            StackPanel notesField = FormField.CreateLabeled(
                this._theme,
                this._i18n["Leads.NotesLabel"],
                notesBox,
                out TextBlock notesLabelTb);

            async Task CommitNotesAsync()
            {
                string updatedNotes = (notesBox.Text ?? string.Empty).Trim();
                if (!string.Equals(post.Notes, updatedNotes, StringComparison.Ordinal))
                {
                    await this._vm.UpdateLeadPostNotesAsync(post, updatedNotes).ConfigureAwait(true);
                    notesLabelTb.Text = $"{this._i18n["Leads.NotesLabel"]}  ({this._i18n["Leads.NotesSavedHint"]})";
                }
            }

            notesBox.LostFocus += async (_, _) => await CommitNotesAsync().ConfigureAwait(true);
            notesBox.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    e.Handled = true;
                    await CommitNotesAsync().ConfigureAwait(true);
                }
            };

            contentStack.Children.Add(notesField);

            // Large Media Preview (if post has photo or video)
            if (!string.IsNullOrWhiteSpace(post.MediaPreviewUrl))
            {
                Image previewImg = new()
                {
                    Height = 155,
                    Stretch = Stretch.UniformToFill,
                    IsVisible = false
                };

                Panel mediaPanel = new()
                {
                    Height = 155,
                    Children = { previewImg }
                };

                if (post.IsVideo)
                {
                    Border videoBadge = new()
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                        CornerRadius = new CornerRadius(14),
                        Padding = new Thickness(10, 4),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = $"▶ {this._i18n["Leads.MediaVideoBadge"]}",
                            FontSize = 11.5,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.White
                        }
                    };
                    mediaPanel.Children.Add(videoBadge);
                }

                Border mediaBorder = new()
                {
                    Height = 155,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = this._theme.Palette.SubtleBorder,
                    ClipToBounds = true,
                    Child = mediaPanel,
                    IsVisible = false
                };

                RemoteImageLoader.Attach(previewImg, post.MediaPreviewUrl, mediaBorder);
                contentStack.Children.Add(mediaBorder);
            }

            // Full Post Message Body inside Inset Card
            string fullText = string.IsNullOrWhiteSpace(post.MessageText)
                ? this._i18n["Leads.EmptyPostText"]
                : post.MessageText;
            SelectableTextBlock fullMessageTb = new()
            {
                Text = fullText,
                FontSize = 12.5,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Foreground = this._theme.Palette.TextPrimary
            };

            contentStack.Children.Add(SurfaceCard.CreateInset(this._theme, fullMessageTb, new Thickness(12, 10)));

            detailBodyHost.Content = new ScrollViewer
            {
                Content = contentStack
            };
        }

        postsTable.RowsListBox.SelectionChanged += (_, _) =>
        {
            if (postsTable.RowsListBox.SelectedItem is PostSummary p)
            {
                this._vm.SelectedScrapedPost = p;
                RefreshSelectedPostDetail();
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal))
            {
                if (!ReferenceEquals(postsTable.RowsListBox.SelectedItem, this._vm.SelectedScrapedPost))
                {
                    postsTable.RowsListBox.SelectedItem = this._vm.SelectedScrapedPost;
                }

                RefreshSelectedPostDetail();
            }
        };

        this._vm.ScrapedPosts.CollectionChanged += (_, _) => RefreshSelectedPostDetail();
        this._vm.LeadTags.CollectionChanged += (_, _) => RefreshSelectedPostDetail();
        this._theme.ThemeChanged += (_, _) => RefreshSelectedPostDetail();
        RefreshSelectedPostDetail();

        Grid.SetColumn(postsTable, 0);
        Grid.SetColumn(detailCard, 1);
        masterDetailSplit.Children.Add(postsTable);
        masterDetailSplit.Children.Add(detailCard);

        Grid.SetRow(masterDetailSplit, 2);
        tabGrid.Children.Add(masterDetailSplit);

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            AppButton.SetText(exportCsvBtn, this._i18n["Leads.BtnExportCsv"]);
            AppButton.SetText(scrapeNowBtn, this._i18n["Leads.BtnScanPosts"]);
            detailCardTitleTb.Text = this._i18n["Leads.DetailCardTitle"];
            AppButton.SetText(askAiConsultBtn, this._i18n["Leads.BtnAskAiConsult"]);
            AppButton.SetText(quickReplyBtn, this._i18n["Leads.BtnQuickReplyTab"]);
            AppButton.SetText(inspectOpenBrowserBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
            sortCategory.Title = this._i18n["Leads.FilterCatSort"];
            statusCategory.Title = this._i18n["Leads.ColStatus"];
            intentCategory.Title = this._i18n["Leads.FilterCatIntent"];
            competitionCategory.Title = this._i18n["Leads.FilterCatCompetition"];
            timeCategory.Title = this._i18n["Leads.FilterCatTimeRange"];
            mediaCategory.Title = this._i18n["Leads.FilterCatMediaType"];
            groupTagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            groupCategory.Title = this._i18n["Leads.FilterGroupLabel"];
            leadTagCategory.Title = this._i18n["Leads.FilterCategoryLeadTag"];
            searchFilterBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Leads.BuyerKeywordPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            postsTable.UpdateHeadersAndEmptyText(
                [
                    this._i18n["Leads.ColAuthor"],
                    this._i18n["Leads.ColGroup"],
                    this._i18n["Leads.ColMessage"],
                    this._i18n["Leads.ColStatus"]
                ],
                this._i18n["Empty.Leads"],
                this._i18n["Leads.TableTitlePrefix"]);
            RefreshSelectedPostDetail();
        };

        return tabGrid;
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

    private async Task<string?> PickSaveCsvFileAsync(string title)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = "csv",
            SuggestedFileName = $"facebook_leads_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV Files (*.csv)")
                {
                    Patterns = ["*.csv"]
                }
            ]
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }
}
