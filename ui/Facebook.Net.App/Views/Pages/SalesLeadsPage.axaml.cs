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
/// 1) Local SQLite Post Repository &amp; Filter Table (<see cref="DataTableView{T}"/> + <see cref="SegmentedFilterPill"/>),
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
        Control tab1View = this.BuildRepositoryAndFilterTab(out Action refreshTab1I18n);
        Control tab2View = this.BuildMultiGroupAndScraperConfigTab(out Action refreshTab2I18n);
        Control tab3View = this.BuildSalesAutoCommentTab(out Action refreshTab3I18n);

        SubTabBar subTabBar = new(
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

    private Control BuildRepositoryAndFilterTab(out Action refreshI18n)
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

        // Order: [ Chưa bình luận (1) | Đã bình luận (2) | Tất cả (0) ]
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
                    return SectionHeader.CreateLabel(this._theme, grp.GroupName, 12.5, FontWeight.Medium);
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

        // 3. Spacious DataTableView<PostSummary> ("Số bài viết đã thu thập (N)")
        DataTableView<PostSummary> postsTable = new(
            this._theme,
            [
                new DataTableColumn("155", this._i18n["Leads.ColAuthor"]),
                new DataTableColumn("145", this._i18n["Leads.ColGroup"]),
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

        postsTable.RowsListBox.SelectionChanged += (_, _) =>
        {
            if (postsTable.RowsListBox.SelectedItem is PostSummary p)
            {
                this._vm.SelectedScrapedPost = p;
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal) &&
                !ReferenceEquals(postsTable.RowsListBox.SelectedItem, this._vm.SelectedScrapedPost))
            {
                postsTable.RowsListBox.SelectedItem = this._vm.SelectedScrapedPost;
            }
        };

        Grid.SetRow(postsTable, 2);
        tabGrid.Children.Add(postsTable);

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            AppButton.SetText(scrapeNowBtn, this._i18n["Leads.BtnScanPosts"]);
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
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
        };

        return tabGrid;
    }

    private Control BuildMultiGroupAndScraperConfigTab(out Action refreshI18n)
    {
        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1*"),
            ColumnSpacing = 14
        };

        // Left Column: Multi-Group Selection for Scraping
        Grid leftPane = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 10
        };

        Grid leftHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Leads.ConfigMultiGroupTitle"],
            this._i18n["Leads.ConfigMultiGroupDesc"],
            out TextBlock multiGroupTitleTb,
            out TextBlock multiGroupDescTb);
        Grid.SetRow(leftHeader, 0);
        leftPane.Children.Add(leftHeader);

        Grid bulkGroupBtnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,Auto"),
            ColumnSpacing = 8
        };

        Button enableAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSelectAllTargets"],
            this._vm.EnableAllTargetGroupsCommand,
            VectorIcons.CheckCircle);

        Button disableAllBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnDeselectAllTargets"],
            this._vm.DisableAllTargetGroupsCommand,
            VectorIcons.StopCircle);

        Button syncJoinedBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnSyncJoined"],
            this._vm.SyncJoinedGroupsCommand,
            VectorIcons.Sync);

        Grid.SetColumn(enableAllBtn, 0);
        Grid.SetColumn(disableAllBtn, 1);
        Grid.SetColumn(syncJoinedBtn, 2);
        bulkGroupBtnRow.Children.Add(enableAllBtn);
        bulkGroupBtnRow.Children.Add(disableAllBtn);
        bulkGroupBtnRow.Children.Add(syncJoinedBtn);
        Grid.SetRow(bulkGroupBtnRow, 1);
        leftPane.Children.Add(bulkGroupBtnRow);

        ListBox targetGroupsList = FormField.CreateListBox<SavedTargetGroup>(
            this._theme,
            this._vm.TargetGroups,
            tg => GroupRowCard.CreateTargetRow(
                this._theme,
                tg,
                nextState => _ = this._vm.ToggleTargetGroupEnabledAsync(tg, nextState)),
            selectedItem: this._vm.SelectedTargetGroup,
            isVisible: this._vm.TargetGroups.Count > 0);

        Border emptyTargetsBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.Groups,
            out TextBlock emptyTargetsTb);
        emptyTargetsBanner.IsVisible = this._vm.TargetGroups.Count == 0;
        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            bool isEmpty = this._vm.TargetGroups.Count == 0;
            emptyTargetsBanner.IsVisible = isEmpty;
            targetGroupsList.IsVisible = !isEmpty;
        };

        Grid targetListHost = EmptyStateBanner.CreateHost(targetGroupsList, emptyTargetsBanner);
        Grid.SetRow(targetListHost, 2);
        leftPane.Children.Add(targetListHost);

        // Quick Add from Joined Groups row at bottom of Left Card
        Grid quickAddJoinedRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        ComboBox joinedGroupPicker = FormField.CreateComboBox<GroupSummary>(
            this._theme,
            this._vm.JoinedGroups,
            g => SectionHeader.CreateLabel(this._theme, g.Name, 12, FontWeight.Medium),
            this._vm.SelectedGroup,
            this._i18n["Groups.Header"]);
        joinedGroupPicker.SelectionChanged += (_, _) =>
        {
            if (joinedGroupPicker.SelectedItem is GroupSummary g)
            {
                this._vm.SelectedGroup = g;
            }
        };

        Button addJoinedToTargetsBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnSaveTarget"],
            this._vm.AddSelectedGroupToTargetsCommand,
            VectorIcons.Plus);

        Grid.SetColumn(joinedGroupPicker, 0);
        Grid.SetColumn(addJoinedToTargetsBtn, 1);
        quickAddJoinedRow.Children.Add(joinedGroupPicker);
        quickAddJoinedRow.Children.Add(addJoinedToTargetsBtn);
        Grid.SetRow(quickAddJoinedRow, 3);
        leftPane.Children.Add(quickAddJoinedRow);

        Border leftCard = SurfaceCard.Create(this._theme, leftPane, new Thickness(16));

        // Right Column: Selective Scraper & Anti-Ban Rate-Limit Configuration
        StackPanel rightStack = new() { Spacing = 12 };

        StackPanel configHeader = SectionHeader.CreateSubHeader(
            this._theme,
            this._i18n["Leads.ConfigTitle"],
            VectorIcons.Settings,
            out TextBlock configTitleTb);
        rightStack.Children.Add(configHeader);

        Border chronoPill = IconMetricPill.Create(
            this._theme,
            VectorIcons.CheckCircle,
            this._i18n["Leads.ConfigChronologicalBadge"],
            AuditSeverity.Success,
            out TextBlock chronoBadgeTb);
        rightStack.Children.Add(chronoPill);

        TextBox scrapeKeywordsBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Leads.ConfigKeywordsPlaceholder"],
            this._vm.ScrapeFilterKeywords);
        scrapeKeywordsBox.TextChanged += (_, _) =>
            this._vm.ScrapeFilterKeywords = scrapeKeywordsBox.Text ?? string.Empty;

        StackPanel scrapeKeywordsField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigKeywordsLabel"],
            scrapeKeywordsBox,
            out TextBlock scrapeKeywordsLbl);
        rightStack.Children.Add(scrapeKeywordsField);

        // Anti-Ban Rate-Limit Delay between Groups (Min..Max seconds)
        Grid scrapeDelayRow = DelayRangeSelector.Create(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            this._vm.ScrapeDelayMinSeconds,
            this._i18n["Settings.MaxDelayPrefix"],
            this._vm.ScrapeDelayMaxSeconds,
            out TextBlock scrapeMinDelayPrefix,
            out NumericUpDown scrapeMinDelayBox,
            out TextBlock scrapeMaxDelayPrefix,
            out NumericUpDown scrapeMaxDelayBox);

        scrapeMinDelayBox.ValueChanged += (_, _) =>
            this._vm.ScrapeDelayMinSeconds = Math.Clamp((int)(scrapeMinDelayBox.Value ?? 8), 2, 600);
        scrapeMaxDelayBox.ValueChanged += (_, _) =>
            this._vm.ScrapeDelayMaxSeconds = Math.Clamp((int)(scrapeMaxDelayBox.Value ?? 18), this._vm.ScrapeDelayMinSeconds, 600);

        StackPanel scrapeDelayField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigRateLimitLabel"],
            scrapeDelayRow,
            out TextBlock scrapeDelayLbl);
        rightStack.Children.Add(scrapeDelayField);

        // 3-Column Numbers: Posts/Group, Max Groups/Cycle (Round-Robin), Background Interval (min)
        Grid numbersGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
        };

        TextBox postsPerGroupBox = FormField.CreateTextBox(
            this._theme,
            "15",
            this._vm.ScrapePostsPerGroup.ToString(CultureInfo.InvariantCulture));
        postsPerGroupBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(postsPerGroupBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapePostsPerGroup = Math.Clamp(val, 5, 100);
            }
        };

        StackPanel postsPerGroupField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigPostsPerGroupLabel"],
            postsPerGroupBox,
            out TextBlock postsPerGroupLbl);

        TextBox maxGroupsCycleBox = FormField.CreateTextBox(
            this._theme,
            "5",
            this._vm.ScrapeMaxGroupsPerCycle.ToString(CultureInfo.InvariantCulture));
        maxGroupsCycleBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxGroupsCycleBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapeMaxGroupsPerCycle = Math.Clamp(val, 1, 50);
            }
        };

        StackPanel maxGroupsCycleField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigMaxGroupsPerCycleLabel"],
            maxGroupsCycleBox,
            out TextBlock maxGroupsCycleLbl);

        TextBox intervalMinBox = FormField.CreateTextBox(
            this._theme,
            "10",
            this._vm.AutoScrapeIntervalMinutes.ToString(CultureInfo.InvariantCulture));
        intervalMinBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(intervalMinBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                this._vm.AutoScrapeIntervalMinutes = Math.Clamp(val, 1, 120);
            }
        };

        StackPanel intervalMinField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigIntervalMinutesLabel"],
            intervalMinBox,
            out TextBlock intervalMinLbl);

        Grid.SetColumn(postsPerGroupField, 0);
        Grid.SetColumn(maxGroupsCycleField, 1);
        Grid.SetColumn(intervalMinField, 2);
        numbersGrid.Children.Add(postsPerGroupField);
        numbersGrid.Children.Add(maxGroupsCycleField);
        numbersGrid.Children.Add(intervalMinField);
        rightStack.Children.Add(numbersGrid);

        CheckBox scrapeAllGroupsCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAllGroupsToggle"],
            this._vm.ScrapeAllEnabledTargetGroups,
            out TextBlock scrapeAllGroupsLbl);
        scrapeAllGroupsCheck.IsCheckedChanged += (_, _) =>
            this._vm.ScrapeAllEnabledTargetGroups = scrapeAllGroupsCheck.IsChecked ?? false;
        rightStack.Children.Add(scrapeAllGroupsCheck);

        CheckBox autoBgScrapeCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAutoBackgroundToggle"],
            this._vm.AutoBackgroundScrapeEnabled,
            out TextBlock autoBgScrapeLbl);
        autoBgScrapeCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoBackgroundScrapeEnabled = autoBgScrapeCheck.IsChecked ?? false;
        rightStack.Children.Add(autoBgScrapeCheck);

        Grid configActionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };

        Button runScrapeBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnScanPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        Button clearDbBtn = AppButton.CreateDanger(
            this._theme,
            this._i18n["Leads.BtnClearScrapedDb"],
            this._vm.ClearScrapedPostsDbCommand,
            VectorIcons.Trash);

        Grid.SetColumn(runScrapeBtn, 0);
        Grid.SetColumn(clearDbBtn, 1);
        configActionRow.Children.Add(runScrapeBtn);
        configActionRow.Children.Add(clearDbBtn);
        rightStack.Children.Add(configActionRow);

        Border rightCard = SurfaceCard.Create(this._theme, new ScrollViewer { Content = rightStack }, new Thickness(16));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightCard);

        refreshI18n = () =>
        {
            multiGroupTitleTb.Text = this._i18n["Leads.ConfigMultiGroupTitle"];
            multiGroupDescTb.Text = this._i18n["Leads.ConfigMultiGroupDesc"];
            AppButton.SetText(enableAllBtn, this._i18n["Groups.BtnSelectAllTargets"]);
            AppButton.SetText(disableAllBtn, this._i18n["Groups.BtnDeselectAllTargets"]);
            AppButton.SetText(syncJoinedBtn, this._i18n["Groups.BtnSyncJoined"]);
            emptyTargetsTb.Text = this._i18n["Empty.TargetGroups"];
            joinedGroupPicker.PlaceholderText = this._i18n["Groups.Header"];
            AppButton.SetText(addJoinedToTargetsBtn, this._i18n["Groups.BtnSaveTarget"]);
            configTitleTb.Text = this._i18n["Leads.ConfigTitle"];
            chronoBadgeTb.Text = this._i18n["Leads.ConfigChronologicalBadge"];
            scrapeKeywordsLbl.Text = this._i18n["Leads.ConfigKeywordsLabel"];
            scrapeKeywordsBox.PlaceholderText = this._i18n["Leads.ConfigKeywordsPlaceholder"];
            scrapeDelayLbl.Text = this._i18n["Leads.ConfigRateLimitLabel"];
            scrapeMinDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            scrapeMaxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            postsPerGroupLbl.Text = this._i18n["Leads.ConfigPostsPerGroupLabel"];
            maxGroupsCycleLbl.Text = this._i18n["Leads.ConfigMaxGroupsPerCycleLabel"];
            intervalMinLbl.Text = this._i18n["Leads.ConfigIntervalMinutesLabel"];
            scrapeAllGroupsLbl.Text = this._i18n["Leads.ConfigAllGroupsToggle"];
            autoBgScrapeLbl.Text = this._i18n["Leads.ConfigAutoBackgroundToggle"];
            AppButton.SetText(runScrapeBtn, this._i18n["Leads.BtnScanPosts"]);
            AppButton.SetText(clearDbBtn, this._i18n["Leads.BtnClearScrapedDb"]);
        };

        return splitGrid;
    }

    private Control BuildSalesAutoCommentTab(out Action refreshI18n)
    {
        Grid commentTabGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
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

        // Selected Post Banner
        TextBlock selectedPostTb = SectionHeader.CreateLabel(
            this._theme,
            this._vm.SelectedScrapedPost is not null
                ? $"{this._i18n["Leads.SelectedPostBanner"]} {this._vm.SelectedScrapedPost.AuthorName} — {this._vm.SelectedScrapedPost.MessageText}"
                : this._i18n["Leads.NoPostSelected"],
            12.5,
            FontWeight.SemiBold);
        selectedPostTb.MaxLines = 2;
        selectedPostTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Border selectedPostWell = SurfaceCard.CreateInset(this._theme, selectedPostTb, new Thickness(14, 10));
        Grid.SetRow(selectedPostWell, 1);
        commentTabGrid.Children.Add(selectedPostWell);

        TextBox commentBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Leads.CommentContentPlaceholder"],
            this._vm.SalesCommentSpintaxText,
            multiline: true,
            height: 160);
        commentBox.VerticalAlignment = VerticalAlignment.Stretch;
        commentBox.TextChanged += (_, _) => this._vm.SalesCommentSpintaxText = commentBox.Text ?? string.Empty;
        Grid.SetRow(commentBox, 2);
        commentTabGrid.Children.Add(commentBox);

        Grid commentOptionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

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

        Grid.SetColumn(autoReactCheck, 0);
        Grid.SetColumn(commentPhotoPicker, 1);
        commentOptionsRow.Children.Add(autoReactCheck);
        commentOptionsRow.Children.Add(commentPhotoPicker);
        Grid.SetRow(commentOptionsRow, 3);
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
        Grid.SetRow(commentButtonsRow, 4);
        commentTabGrid.Children.Add(commentButtonsRow);

        void UpdateSelectedPostBanner()
        {
            selectedPostTb.Text = this._vm.SelectedScrapedPost is not null
                ? $"{this._i18n["Leads.SelectedPostBanner"]} {this._vm.SelectedScrapedPost.AuthorName} — {this._vm.SelectedScrapedPost.MessageText}"
                : this._i18n["Leads.NoPostSelected"];
        }

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
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SalesCommentPhotoPath), StringComparison.Ordinal) &&
                     !string.Equals(commentPhotoBox.Text, this._vm.SalesCommentPhotoPath, StringComparison.Ordinal))
            {
                commentPhotoBox.Text = this._vm.SalesCommentPhotoPath;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Leads.TabAutoComment"];
            descTb.Text = this._i18n["Leads.Desc"];
            UpdateSelectedPostBanner();
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactLbl.Text = this._i18n["Leads.AutoReactToggle"];
            AppButton.SetText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            AppButton.SetText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return SurfaceCard.Create(this._theme, commentTabGrid, new Thickness(18));
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
