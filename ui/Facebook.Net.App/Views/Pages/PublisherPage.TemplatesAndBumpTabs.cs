// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tabs 2 and 3 of <see cref="PublisherPage"/>:
/// Tab 2: Saved Post Templates Manager,
/// Tab 3: Up-Top / Group Post Bumper.
/// </summary>
public sealed partial class PublisherPage
{
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
                StackPanel sp = new() { Spacing = 4, Margin = new Thickness(6, 4) };
                sp.Children.Add(SectionHeader.CreateLabel(this._theme, tpl.Title, 13.5, FontWeight.SemiBold));
                TextBlock bodyTb = SectionHeader.CreateLabel(this._theme, tpl.SpintaxContent, 12, FontWeight.Normal, isSecondary: true);
                bodyTb.MaxLines = 2;
                sp.Children.Add(bodyTb);
                return sp;
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

        return SurfaceCard.Create(this._theme, tplGrid, new Thickness(18));
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
