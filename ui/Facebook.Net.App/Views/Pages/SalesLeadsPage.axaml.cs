// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Leads.Header"],
            this._i18n["Leads.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        Grid.SetRow(header, 0);
        pageGrid.Children.Add(header);

        // Top Filter & Scan Bar
        Grid scanBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox buyerKwBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Leads.BuyerKeywordPlaceholder"],
            this._vm.BuyerIntentKeywords);
        buyerKwBox.TextChanged += (_, _) => this._vm.BuyerIntentKeywords = buyerKwBox.Text ?? string.Empty;

        Button scanBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnScanGroupPosts"],
            this._vm.ScanGroupLeadsCommand,
            VectorIcons.Search);

        Grid.SetColumn(buyerKwBox, 0);
        Grid.SetColumn(scanBtn, 1);
        scanBar.Children.Add(buyerKwBox);
        scanBar.Children.Add(scanBtn);
        Grid.SetRow(scanBar, 1);
        pageGrid.Children.Add(scanBar);

        // Discovered Group Posts List using shared UiControls.CreateLeadPostItemCard with SVG icons
        ListBox postsList = UiControls.CreateListBox<PostSummary>(
            this._theme,
            this._vm.ScrapedPosts,
            post => UiControls.CreateLeadPostItemCard(
                this._theme,
                this._i18n,
                post,
                ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords),
                () => this._vm.FormatBuyerIntentBadge(post)),
            selectedItem: this._vm.SelectedScrapedPost,
            isVisible: this._vm.ScrapedPosts.Count > 0);

        Border emptyLeadsBanner = UiControls.CreateEmptyState(
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

        Grid leadsListHost = UiControls.CreateListWithEmptyStateHost(postsList, emptyLeadsBanner);
        Grid.SetRow(leadsListHost, 2);
        pageGrid.Children.Add(leadsListHost);

        // Compact Sales Comment Composer below the list
        TextBox commentBox = UiControls.CreateTextBox(
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

        CheckBox autoReactCheck = UiControls.CreateCheckBox(
            this._theme,
            this._i18n["Leads.AutoReactToggle"],
            this._vm.AutoReactBeforeComment,
            out TextBlock autoReactLbl);
        autoReactCheck.IsCheckedChanged += (_, _) =>
            this._vm.AutoReactBeforeComment = autoReactCheck.IsChecked ?? false;

        Grid commentPhotoPicker = UiControls.CreateFilePickerRow(
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

        Button sendSelectedBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Leads.BtnSendCommentSelected"],
            this._vm.SendSalesCommentOnSelectedPostCommand,
            VectorIcons.SalesComment);

        Button autoCommentAllBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Leads.BtnAutoCommentAllMatched"],
            this._vm.AutoCommentAllMatchedPostsCommand,
            VectorIcons.Publish);

        Button openPostBtn = UiControls.CreateSecondaryButton(
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
        };

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Leads.Header"];
            descTb.Text = this._i18n["Leads.Desc"];
            emptyLeadsTb.Text = this._i18n["Empty.Leads"];
            buyerKwBox.PlaceholderText = this._i18n["Leads.BuyerKeywordPlaceholder"];
            UiControls.SetButtonText(scanBtn, this._i18n["Leads.BtnScanGroupPosts"]);
            commentBox.PlaceholderText = this._i18n["Leads.CommentContentPlaceholder"];
            commentPhotoBox.PlaceholderText = this._i18n["Leads.CommentPhotoPlaceholder"];
            autoReactLbl.Text = this._i18n["Leads.AutoReactToggle"];
            UiControls.SetButtonText(pickCommentPhotoBtn, this._i18n["Publisher.BtnPickPhoto"]);
            UiControls.SetButtonText(sendSelectedBtn, this._i18n["Leads.BtnSendCommentSelected"]);
            UiControls.SetButtonText(autoCommentAllBtn, this._i18n["Leads.BtnAutoCommentAllMatched"]);
            UiControls.SetButtonText(openPostBtn, this._i18n["Leads.BtnOpenPostInBrowser"]);
        };

        return UiControls.CreateCard(this._theme, pageGrid);
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
