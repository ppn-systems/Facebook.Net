// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent Buyer Lead Scanner &amp; Sales Commenting Page (<c>SalesLeadsPage.axaml</c>).
/// Uses the unified <see cref="SearchFilterBar"/> (<c>[ ▽ Bộ lọc | Tìm hoặc lọc | 🔍 ]</c>) to filter buyer keywords and scan group posts.
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
        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        Grid headerBar = new()
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

        // Selective Scraper Configuration Popover ("⚙ Cấu hình Thu thập")
        TextBlock configTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Leads.ConfigTitle"],
            13,
            Avalonia.Media.FontWeight.Bold);

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

        TextBox postsPerGroupBox = FormField.CreateTextBox(
            this._theme,
            "15",
            this._vm.ScrapePostsPerGroup.ToString(System.Globalization.CultureInfo.InvariantCulture));
        postsPerGroupBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(postsPerGroupBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
            {
                this._vm.ScrapePostsPerGroup = Math.Clamp(val, 1, 50);
            }
        };

        StackPanel postsPerGroupField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigPostsPerGroupLabel"],
            postsPerGroupBox,
            out TextBlock postsPerGroupLbl);

        TextBox intervalMinBox = FormField.CreateTextBox(
            this._theme,
            "10",
            this._vm.AutoScrapeIntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        intervalMinBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(intervalMinBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
            {
                this._vm.AutoScrapeIntervalMinutes = Math.Clamp(val, 1, 120);
            }
        };

        StackPanel intervalMinField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigIntervalMinutesLabel"],
            intervalMinBox,
            out TextBlock intervalMinLbl);

        Grid configNumbersGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(postsPerGroupField, 0);
        Grid.SetColumn(intervalMinField, 1);
        configNumbersGrid.Children.Add(postsPerGroupField);
        configNumbersGrid.Children.Add(intervalMinField);

        CheckBox scrapeAllGroupsCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAllGroupsToggle"],
            this._vm.ScrapeAllEnabledTargetGroups,
            out TextBlock scrapeAllGroupsLbl);
        scrapeAllGroupsCheck.IsCheckedChanged += (_, _) =>
            this._vm.ScrapeAllEnabledTargetGroups = scrapeAllGroupsCheck.IsChecked ?? false;

        CheckBox autoBgScrapeCheck = FormField.CreateCheckBox(
            this._theme,
            this._i18n["Leads.ConfigAutoBackgroundToggle"],
            this._vm.AutoBackgroundScrapeEnabled,
            out TextBlock autoBgScrapeLbl);
        autoBgScrapeCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoBackgroundScrapeEnabled = autoBgScrapeCheck.IsChecked ?? false;

        Button clearDbBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnClearScrapedDb"],
            this._vm.ClearScrapedPostsDbCommand,
            VectorIcons.Trash);

        StackPanel configFlyoutStack = new()
        {
            Width = 340,
            Spacing = 10,
            Children =
            {
                configTitleTb,
                scrapeKeywordsField,
                configNumbersGrid,
                scrapeAllGroupsCheck,
                autoBgScrapeCheck,
                clearDbBtn
            }
        };
        Border configFlyoutCard = SurfaceCard.Create(this._theme, configFlyoutStack, new Thickness(14));

        Button configScraperBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.BtnConfigScraper"],
            null,
            VectorIcons.Settings);
        configScraperBtn.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = configFlyoutCard
        };

        Button scrapeNowBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Leads.BtnScanPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Sync);

        StackPanel headerActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { configScraperBtn, scrapeNowBtn }
        };

        Grid.SetColumn(header, 0);
        Grid.SetColumn(headerActions, 1);
        headerBar.Children.Add(header);
        headerBar.Children.Add(headerActions);

        Grid.SetRow(headerBar, 0);
        pageGrid.Children.Add(headerBar);

        // Group Filter inside SearchFilterBar's [ ▽ Bộ lọc ] Popover (filters local SQLite DB)
        Button allGroupsFilterBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Leads.FilterAllGroups"],
            null,
            VectorIcons.Groups);
        allGroupsFilterBtn.Click += (_, _) => this._vm.FilterScrapedGroupId = null;

        ComboBox targetGroupCombo = FormField.CreateComboBox<SavedTargetGroup>(
            this._theme,
            this._vm.TargetGroups,
            grp => SectionHeader.CreateLabel(this._theme, grp.GroupName, 12, Avalonia.Media.FontWeight.Medium),
            this._vm.SelectedTargetGroup ?? (this._vm.TargetGroups.Count > 0 ? this._vm.TargetGroups[0] : null),
            this._i18n["Groups.TargetListTitle"]);
        targetGroupCombo.Height = 36;
        targetGroupCombo.SelectionChanged += (_, _) =>
        {
            if (targetGroupCombo.SelectedItem is SavedTargetGroup grp)
            {
                this._vm.SelectedTargetGroup = grp;
                this._vm.FilterScrapedGroupId = grp.GroupId;
            }
        };

        StackPanel filterFlyoutStack = new()
        {
            Width = 280,
            Spacing = 8,
            Children =
            {
                allGroupsFilterBtn,
                targetGroupCombo
            }
        };
        Border filterFlyoutCard = SurfaceCard.Create(this._theme, filterFlyoutStack, new Thickness(12, 10));

        // Unified Local DB Search & Filter Bar ([ ▽ Bộ lọc | Tìm hoặc lọc bài viết trong kho dữ liệu (DB)... | 🔍 ])
        // NEVER calls Facebook — queries/filters local SQLite DB only!
        SearchFilterBar searchFilterBar = new(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords,
            filterFlyoutCard,
            this._vm.QueryLocalScrapedPostsCommand);

        searchFilterBar.SearchTextBox.TextChanged += (_, _) =>
            this._vm.ApplyLocalLeadFilter(searchFilterBar.SearchTextBox.Text);

        Grid.SetRow(searchFilterBar, 1);
        pageGrid.Children.Add(searchFilterBar);

        // Discovered Group Posts List using shared LeadPostRowCard
        ListBox postsList = FormField.CreateListBox<PostSummary>(
            this._theme,
            this._vm.ScrapedPosts,
            post => LeadPostRowCard.Create(
                this._theme,
                this._i18n,
                post,
                ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords),
                () => this._vm.FormatBuyerIntentBadge(post)),
            selectedItem: this._vm.SelectedScrapedPost,
            isVisible: this._vm.ScrapedPosts.Count > 0);

        Border emptyLeadsBanner = EmptyStateBanner.Create(
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

        Grid leadsListHost = EmptyStateBanner.CreateHost(postsList, emptyLeadsBanner);
        Grid.SetRow(leadsListHost, 2);
        pageGrid.Children.Add(leadsListHost);

        // Compact Sales Comment Composer below the list
        TextBox commentBox = FormField.CreateTextBox(
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
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
        Grid.SetRow(commentOptionsRow, 4);
        pageGrid.Children.Add(commentOptionsRow);

        Grid commentButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
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
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SalesCommentSpintaxText), StringComparison.Ordinal) &&
                !string.Equals(commentBox.Text, this._vm.SalesCommentSpintaxText, StringComparison.Ordinal))
            {
                commentBox.Text = this._vm.SalesCommentSpintaxText;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SalesCommentPhotoPath), StringComparison.Ordinal) &&
                     !string.Equals(commentPhotoBox.Text, this._vm.SalesCommentPhotoPath, StringComparison.Ordinal))
            {
                commentPhotoBox.Text = this._vm.SalesCommentPhotoPath;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedScrapedPost), StringComparison.Ordinal) &&
                     !ReferenceEquals(postsList.SelectedItem, this._vm.SelectedScrapedPost))
            {
                postsList.SelectedItem = this._vm.SelectedScrapedPost;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedTargetGroup), StringComparison.Ordinal) &&
                     !ReferenceEquals(targetGroupCombo.SelectedItem, this._vm.SelectedTargetGroup))
            {
                targetGroupCombo.SelectedItem = this._vm.SelectedTargetGroup;
            }
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            configTitleTb.Text = this._i18n["Leads.ConfigTitle"];
            scrapeKeywordsLbl.Text = this._i18n["Leads.ConfigKeywordsLabel"];
            scrapeKeywordsBox.PlaceholderText = this._i18n["Leads.ConfigKeywordsPlaceholder"];
            postsPerGroupLbl.Text = this._i18n["Leads.ConfigPostsPerGroupLabel"];
            intervalMinLbl.Text = this._i18n["Leads.ConfigIntervalMinutesLabel"];
            scrapeAllGroupsLbl.Text = this._i18n["Leads.ConfigAllGroupsToggle"];
            autoBgScrapeLbl.Text = this._i18n["Leads.ConfigAutoBackgroundToggle"];
            AppButton.SetText(clearDbBtn, this._i18n["Leads.BtnClearScrapedDb"]);
            AppButton.SetText(configScraperBtn, this._i18n["Leads.BtnConfigScraper"]);
            AppButton.SetText(scrapeNowBtn, this._i18n["Leads.BtnScanPosts"]);
            AppButton.SetText(allGroupsFilterBtn, this._i18n["Leads.FilterAllGroups"]);
            targetGroupCombo.PlaceholderText = this._i18n["Groups.TargetListTitle"];
            emptyLeadsTb.Text = this._i18n["Empty.Leads"];
            searchFilterBar.UpdateLocalization(this._i18n["Common.FilterButton"], this._i18n["Leads.BuyerKeywordPlaceholder"]);
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactLbl.Text = this._i18n["Leads.AutoReactToggle"];
            AppButton.SetText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            AppButton.SetText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            AppButton.SetText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            AppButton.SetText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return SurfaceCard.Create(this._theme, pageGrid);
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
