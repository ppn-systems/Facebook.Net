// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable row card component (<c>CampaignTemplateCard.cs</c>) for displaying and inline-editing
/// a <see cref="CampaignTemplate"/> in the Saved Templates manager.
/// </summary>
public static class CampaignTemplateCard
{
    /// <summary>
    /// Creates a themed surface card for a saved post or comment template with 1-click use, inline edit, and delete actions.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        CampaignTemplate tpl,
        Action onUseTemplate,
        Func<string, string, Task> onSaveInlineEditAsync,
        Action onDeleteTemplate)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(tpl);
        ArgumentNullException.ThrowIfNull(onUseTemplate);
        ArgumentNullException.ThrowIfNull(onSaveInlineEditAsync);
        ArgumentNullException.ThrowIfNull(onDeleteTemplate);

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

        TextBlock tplTitleTb = SectionHeader.CreateLabel(theme, tpl.Title, 13.5, FontWeight.SemiBold);
        tplTitleTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Border typePill = StatusBadge.Create(
            theme,
            tpl.IsCommentTemplate ? i18n["Publisher.FilterTemplatesComment"] : i18n["Publisher.FilterTemplatesPost"],
            tpl.IsCommentTemplate ? AuditSeverity.Info : AuditSeverity.Success);
        typePill.HorizontalAlignment = HorizontalAlignment.Stretch;
        typePill.Height = 28;

        TextBlock useBtnLabel = new()
        {
            Text = !tpl.IsCommentTemplate
                ? i18n["Publisher.BtnUsePostTemplate"]
                : i18n["Publisher.BtnUseCommentTemplate"],
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button useBtn = new()
        {
            Content = useBtnLabel,
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
        useBtn.Click += (_, _) => onUseTemplate();

        StackPanel inlineEditPanel = new()
        {
            Spacing = 6,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        TextBox editTitleBox = FormField.CreateTextBox(
            theme,
            i18n["Publisher.TemplatesTitle"],
            tpl.Title);
        TextBox editContentBox = FormField.CreateTextBox(
            theme,
            i18n["Publisher.ContentPlaceholder"],
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
            theme,
            i18n["Publisher.BtnSaveEditTemplate"],
            new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
            {
                inlineEditPanel.IsVisible = false;
                await onSaveInlineEditAsync(
                    editTitleBox.Text ?? tpl.Title,
                    editContentBox.Text ?? tpl.SpintaxContent).ConfigureAwait(true);
            }),
            VectorIcons.CheckCircle);

        Button cancelEditBtn = AppButton.CreateSecondary(
            theme,
            i18n["Groups.BtnCancelEditTag"],
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
            Content = SectionHeader.CreateLabel(theme, i18n["Publisher.BtnEditTemplateShort"], 11, FontWeight.SemiBold),
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
            theme,
            VectorIcons.Trash,
            i18n["Groups.BtnDeleteTagShort"],
            onDeleteTemplate,
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

        TextBlock bodyTb = SectionHeader.CreateLabel(theme, tpl.SpintaxContent, 12, FontWeight.Normal, isSecondary: true);
        bodyTb.MaxLines = 2;
        bodyTb.TextWrapping = TextWrapping.Wrap;
        bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetRow(topRow, 0);
        Grid.SetRow(bodyTb, 1);
        Grid.SetRow(inlineEditPanel, 2);
        cardGrid.Children.Add(topRow);
        cardGrid.Children.Add(bodyTb);
        cardGrid.Children.Add(inlineEditPanel);

        Border outerCard = SurfaceCard.Create(theme, cardGrid, new Thickness(12, 10));

        void ApplyColors()
        {
            useBtnLabel.Foreground = theme.Palette.BrandPrimary;
            useBtn.Background = theme.Palette.BrandSubtleFill;
            useBtn.BorderBrush = theme.Palette.BrandPrimary;
            editRowBtn.Background = theme.Palette.SecondarySurface;
            editRowBtn.BorderBrush = theme.Palette.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(outerCard, theme, ApplyColors);
        return outerCard;
    }
}
