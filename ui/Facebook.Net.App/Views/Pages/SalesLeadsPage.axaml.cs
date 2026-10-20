// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Buyer Lead Scanner &amp; Sales Commenting Page (<c>SalesLeadsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to cleanly separate:
/// 1) Local SQLite Post Repository &amp; Master-Detail Inspector (<see cref="DataTableView{T}"/> + <see cref="SegmentedFilterPill"/>),
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

    private Control BuildRepositoryAndFilterTab(Action onQuickReplyClicked, out Action refreshI18n)
    {
        Grid tabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        // 1. Page Title + Quick Action Bar
        Grid topHeaderBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Leads.Header"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);

        Button scrapeNowBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnScanPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        Button openPostBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenSelectedPostInBrowserCommand,
            VectorIcons.Browser);

        StackPanel headerBtnRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { openPostBtn, scrapeNowBtn }
        };

        Grid.SetColumn(header, 0);
        Grid.SetColumn(headerBtnRow, 1);
        topHeaderBar.Children.Add(header);
        topHeaderBar.Children.Add(headerBtnRow);
        Grid.SetRow(topHeaderBar, 0);
        tabGrid.Children.Add(topHeaderBar);

        // 2. Structured Filter Card ("Bộ lọc" + Segmented Pill [ Chưa bình luận | Đã bình luận | Tất cả ])
        StackPanel filterCardStack = new() { Spacing = 12 };

        Grid filterCardTopRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock filterCardTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Common.FilterButton"],
            14,
            FontWeight.Bold);

        SegmentedFilterPill statusSegmentPill = new(
            this._theme,
            [
                this._i18n["Leads.FilterSegPending"],
                this._i18n["Leads.FilterSegCommented"],
                this._i18n["Leads.FilterSegAll"]
            ],
            initialIndex: 2,
            onSelected: idx =>
            {
                this._vm.LeadCommentStatusFilter = idx switch
                {
                    0 => 1,
                    1 => 2,
                    _ => 0
                };
            });

        Grid.SetColumn(filterCardTitleTb, 0);
        Grid.SetColumn(statusSegmentPill, 1);
        filterCardTopRow.Children.Add(filterCardTitleTb);
        filterCardTopRow.Children.Add(statusSegmentPill);
        filterCardStack.Children.Add(filterCardTopRow);

        // Filter Controls Row: Group Dropdown (Left) + Local DB Keyword Search Input (Right)
        Grid filterFieldsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        ObservableCollection<object> groupFilterItems = new() { "ALL" };
        for (int i = 0; i < this._vm.TargetGroups.Count; i++)
        {
            groupFilterItems.Add(this._vm.TargetGroups[i]);
        }

        ComboBox groupFilterCombo = FormField.CreateComboBox<object>(
            this._theme,
            groupFilterItems,
            obj =>
            {
                if (obj is SavedTargetGroup grp)
                {
                    Grid grpItemRow = new()
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                        ColumnSpacing = 8,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Border badge = AvatarBadge.Create(this._theme, grp.GroupName, 22, grp.AvatarUrl);
                    TextBlock grpItemTb = SectionHeader.CreateLabel(this._theme, grp.GroupName, 12.5, FontWeight.Medium);
                    grpItemTb.MaxLines = 1;
                    grpItemTb.TextTrimming = TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(badge, 0);
                    Grid.SetColumn(grpItemTb, 1);
                    grpItemRow.Children.Add(badge);
                    grpItemRow.Children.Add(grpItemTb);
                    return grpItemRow;
                }

                TextBlock allTb = SectionHeader.CreateLabel(this._theme, this._i18n["Leads.FilterAllGroups"], 12.5, FontWeight.Medium);
                this._i18n.LanguageChanged += (_, _) => allTb.Text = this._i18n["Leads.FilterAllGroups"];
                return allTb;
            },
            selectedItem: "ALL",
            placeholderText: this._i18n["Leads.FilterAllGroups"]);
        groupFilterCombo.Height = 36;

        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            object? prev = groupFilterCombo.SelectedItem;
            groupFilterItems.Clear();
            groupFilterItems.Add("ALL");
            object? restored = "ALL";
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup item = this._vm.TargetGroups[i];
                groupFilterItems.Add(item);
                if (prev is SavedTargetGroup prevGrp &&
                    string.Equals(prevGrp.GroupId, item.GroupId, StringComparison.Ordinal))
                {
                    restored = item;
                }
            }

            groupFilterCombo.SelectedItem = restored;
        };

        groupFilterCombo.SelectionChanged += (_, _) =>
        {
            if (groupFilterCombo.SelectedItem is SavedTargetGroup grp)
            {
                this._vm.SelectedTargetGroup = grp;
                this._vm.FilterScrapedGroupId = grp.GroupId;
            }
            else
            {
                this._vm.FilterScrapedGroupId = null;
            }
        };

        StackPanel groupFilterField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.FilterGroupLabel"],
            groupFilterCombo,
            out TextBlock groupFilterLbl);

        SearchFilterBar dbSearchBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords,
            filterFlyoutContent: null,
            searchCommand: this._vm.QueryLocalScrapedPostsCommand);

        dbSearchBar.SearchTextBox.TextChanged += (_, _) =>
            this._vm.ApplyLocalLeadFilter(dbSearchBar.SearchTextBox.Text);

        StackPanel keywordFilterField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.FilterKeywordLabel"],
            dbSearchBar,
            out TextBlock keywordFilterLbl);

        Grid.SetColumn(groupFilterField, 0);
        Grid.SetColumn(keywordFilterField, 1);
        filterFieldsRow.Children.Add(groupFilterField);
        filterFieldsRow.Children.Add(keywordFilterField);
        filterCardStack.Children.Add(filterFieldsRow);

        Border filterCard = SurfaceCard.Create(this._theme, filterCardStack, new Thickness(16, 14));
        Grid.SetRow(filterCard, 1);
        tabGrid.Children.Add(filterCard);

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
                new DataTableColumn("140", this._i18n["Leads.ColGroup"]),
                new DataTableColumn("*", this._i18n["Leads.ColMessage"]),
                new DataTableColumn("120", this._i18n["Leads.ColIntent"]),
                new DataTableColumn("85", this._i18n["Leads.ColTime"]),
                new DataTableColumn("125", this._i18n["Leads.ColStatus"])
            ],
            this._vm.ScrapedPosts,
            post => LeadPostTableRow.Create(
                this._theme,
                this._i18n,
                post,
                ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords),
                () => this._vm.FormatBuyerIntentBadge(post),
                this._vm.IsPostCommented(post.PostId)),
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

        Button quickReplyBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnQuickReplyTab"],
            new CommunityToolkit.Mvvm.Input.RelayCommand(onQuickReplyClicked),
            VectorIcons.SalesComment);
        quickReplyBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        Button inspectOpenBrowserBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnOpenPostInBrowser"],
            this._vm.OpenSelectedPostInBrowserCommand,
            VectorIcons.Browser);
        inspectOpenBrowserBtn.HorizontalAlignment = HorizontalAlignment.Stretch;

        detailBtnStack.Children.Add(quickReplyBtn);
        detailBtnStack.Children.Add(inspectOpenBrowserBtn);
        Grid.SetRow(detailBtnStack, 2);
        detailCardGrid.Children.Add(detailBtnStack);

        Border detailCard = SurfaceCard.Create(this._theme, detailCardGrid, new Thickness(14));

        void RefreshSelectedPostDetail()
        {
            PostSummary? post = this._vm.SelectedScrapedPost;
            if (post is null && this._vm.ScrapedPosts.Count > 0)
            {
                post = this._vm.ScrapedPosts[0];
            }

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
            Border grpTinyAvatar = AvatarBadge.Create(this._theme, groupName, 16, post.GroupAvatarUrl);
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
            AppButton.SetText(scrapeNowBtn, this._i18n["Leads.BtnScanPosts"]);
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
            detailCardTitleTb.Text = this._i18n["Leads.DetailCardTitle"];
            AppButton.SetText(quickReplyBtn, this._i18n["Leads.BtnQuickReplyTab"]);
            AppButton.SetText(inspectOpenBrowserBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
            filterCardTitleTb.Text = this._i18n["Common.FilterButton"];
            statusSegmentPill.UpdateLabels(
            [
                this._i18n["Leads.FilterSegPending"],
                this._i18n["Leads.FilterSegCommented"],
                this._i18n["Leads.FilterSegAll"]
            ]);
            groupFilterLbl.Text = this._i18n["Leads.FilterGroupLabel"];
            groupFilterCombo.PlaceholderText = this._i18n["Leads.FilterAllGroups"];
            keywordFilterLbl.Text = this._i18n["Leads.FilterKeywordLabel"];
            dbSearchBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Leads.BuyerKeywordPlaceholder"]);
            postsTable.UpdateHeadersAndEmptyText(
                [
                    this._i18n["Leads.ColAuthor"],
                    this._i18n["Leads.ColGroup"],
                    this._i18n["Leads.ColMessage"],
                    this._i18n["Leads.ColIntent"],
                    this._i18n["Leads.ColTime"],
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
}
