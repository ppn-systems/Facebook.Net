// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Data payload for populating a <see cref="FacebookPostPreviewCard"/>.
/// </summary>
public sealed record FacebookPostPreviewData(
    string AccountDisplayName,
    string? AccountAvatarUrl,
    string HeaderDisplayLine,
    string ResolvedBodyText,
    bool ShowAutoReactionSummary,
    IReadOnlyList<string> MediaFilePaths);

/// <summary>
/// Full-page simulated Facebook Feed Post Preview view component (<c>FacebookPostPreviewCard.cs</c>)
/// with back navigation, spin-another-variant button, author/group header, media gallery, and action footer.
/// </summary>
public static class FacebookPostPreviewCard
{
    /// <summary>
    /// Creates the simulated Facebook post preview card view and returns delegates to populate data and refresh localization.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        Action onBackClicked,
        Action onSpinAnotherClicked,
        out Action<FacebookPostPreviewData> populatePreview,
        out Action refreshI18n)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(onBackClicked);
        ArgumentNullException.ThrowIfNull(onSpinAnotherClicked);

        Grid previewPageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 14
        };

        Grid previewTopBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button backToComposerBtn = AppButton.CreateSecondary(
            theme,
            i18n["Publisher.BtnBackToComposer"]);
        backToComposerBtn.Click += (_, _) => onBackClicked();

        TextBlock previewPageTitleTb = SectionHeader.CreateLabel(
            theme,
            i18n["Publisher.PreviewPageTitle"],
            16,
            FontWeight.Bold);
        previewPageTitleTb.HorizontalAlignment = HorizontalAlignment.Center;

        Button spinAnotherBtn = AppButton.CreatePrimary(
            theme,
            i18n["Publisher.BtnSpinAnotherVariant"],
            null,
            VectorIcons.Sync);
        spinAnotherBtn.Click += (_, _) => onSpinAnotherClicked();

        Grid.SetColumn(backToComposerBtn, 0);
        Grid.SetColumn(previewPageTitleTb, 1);
        Grid.SetColumn(spinAnotherBtn, 2);
        previewTopBar.Children.Add(backToComposerBtn);
        previewTopBar.Children.Add(previewPageTitleTb);
        previewTopBar.Children.Add(spinAnotherBtn);
        Grid.SetRow(previewTopBar, 0);
        previewPageGrid.Children.Add(previewTopBar);

        StackPanel fbPostCardStack = new()
        {
            Spacing = 14
        };

        Grid fbPostHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        ContentControl fbPreviewAvatarHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel fbHeaderMeta = new()
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock fbAuthorAndGroupTb = SectionHeader.CreateLabel(
            theme,
            string.Empty,
            14.5,
            FontWeight.Bold);

        TextBlock fbTimestampTb = SectionHeader.CreateLabel(
            theme,
            i18n["Publisher.PreviewJustNowPublic"],
            12,
            FontWeight.Normal,
            isSecondary: true);

        fbHeaderMeta.Children.Add(fbAuthorAndGroupTb);
        fbHeaderMeta.Children.Add(fbTimestampTb);
        Grid.SetColumn(fbPreviewAvatarHost, 0);
        Grid.SetColumn(fbHeaderMeta, 1);
        fbPostHeader.Children.Add(fbPreviewAvatarHost);
        fbPostHeader.Children.Add(fbHeaderMeta);
        fbPostCardStack.Children.Add(fbPostHeader);

        TextBlock fbBodyContentTb = SectionHeader.CreateLabel(
            theme,
            string.Empty,
            14.5,
            FontWeight.Normal);
        fbBodyContentTb.LineHeight = 22;
        fbBodyContentTb.TextWrapping = TextWrapping.Wrap;
        fbPostCardStack.Children.Add(fbBodyContentTb);

        Image fbFullPreviewImage = new()
        {
            MaxHeight = 360,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Border fbMediaFrame = new()
        {
            IsVisible = false,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            Child = fbFullPreviewImage
        };
        fbPostCardStack.Children.Add(fbMediaFrame);

        StackPanel fbExtraPhotosStrip = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            IsVisible = false
        };
        fbPostCardStack.Children.Add(fbExtraPhotosStrip);

        TextBlock fbReactionSummaryTb = SectionHeader.CreateLabel(
            theme,
            $"❤️ 👍  {i18n["Publisher.PreviewAutoLikedHint"]}",
            12,
            FontWeight.Medium,
            isSecondary: true);
        fbPostCardStack.Children.Add(fbReactionSummaryTb);

        Border fbDivider = new()
        {
            Height = 1
        };
        fbPostCardStack.Children.Add(fbDivider);

        Grid fbActionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        TextBlock fbLikeLbl = SectionHeader.CreateLabel(theme, $"👍  {i18n["Publisher.PreviewActionLike"]}", 13, FontWeight.SemiBold, isSecondary: true);
        fbLikeLbl.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock fbCommentLbl = SectionHeader.CreateLabel(theme, $"💬  {i18n["Publisher.PreviewActionComment"]}", 13, FontWeight.SemiBold, isSecondary: true);
        fbCommentLbl.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock fbShareLbl = SectionHeader.CreateLabel(theme, $"↗  {i18n["Publisher.PreviewActionShare"]}", 13, FontWeight.SemiBold, isSecondary: true);
        fbShareLbl.HorizontalAlignment = HorizontalAlignment.Center;

        Grid.SetColumn(fbLikeLbl, 0);
        Grid.SetColumn(fbCommentLbl, 1);
        Grid.SetColumn(fbShareLbl, 2);
        fbActionRow.Children.Add(fbLikeLbl);
        fbActionRow.Children.Add(fbCommentLbl);
        fbActionRow.Children.Add(fbShareLbl);
        fbPostCardStack.Children.Add(fbActionRow);

        Border fbPostOuterCard = SurfaceCard.Create(theme, fbPostCardStack, new Thickness(22, 18));
        fbPostOuterCard.MaxWidth = 680;
        fbPostOuterCard.HorizontalAlignment = HorizontalAlignment.Stretch;

        ScrollViewer fbPreviewScroll = new()
        {
            Content = fbPostOuterCard
        };
        Grid.SetRow(fbPreviewScroll, 1);
        previewPageGrid.Children.Add(fbPreviewScroll);

        Border previewPageCard = SurfaceCard.Create(theme, previewPageGrid, new Thickness(18));

        void ApplyColors()
        {
            fbMediaFrame.BorderBrush = theme.Palette.SubtleBorder;
            fbDivider.Background = theme.Palette.SubtleBorder;
        }

        ApplyColors();
        ThemeLifecycle.Bind(previewPageCard, theme, ApplyColors);

        previewPageCard.DetachedFromVisualTree += (_, _) =>
        {
            if (fbFullPreviewImage.Source is IDisposable oldBmp)
            {
                fbFullPreviewImage.Source = null;
                oldBmp.Dispose();
            }

            MediaThumbnailStrip.DisposeStripBitmaps(fbExtraPhotosStrip);
        };

        populatePreview = data =>
        {
            ArgumentNullException.ThrowIfNull(data);
            fbPreviewAvatarHost.Content = AvatarBadge.Create(theme, data.AccountDisplayName, 44, data.AccountAvatarUrl);
            fbAuthorAndGroupTb.Text = data.HeaderDisplayLine;
            fbBodyContentTb.Text = data.ResolvedBodyText;
            fbReactionSummaryTb.IsVisible = data.ShowAutoReactionSummary;

            if (fbFullPreviewImage.Source is IDisposable oldPreviewBmp)
            {
                fbFullPreviewImage.Source = null;
                oldPreviewBmp.Dispose();
            }

            MediaThumbnailStrip.DisposeStripBitmaps(fbExtraPhotosStrip);

            IReadOnlyList<string> files = data.MediaFilePaths;
            if (files is { Count: > 0 })
            {
                Bitmap? mainBmp = MediaThumbnailStrip.TryDecodeThumbnail(files[0], 720);
                if (mainBmp is not null)
                {
                    fbFullPreviewImage.Source = mainBmp;
                    fbMediaFrame.IsVisible = true;
                }
                else
                {
                    fbMediaFrame.IsVisible = false;
                }

                if (files.Count > 1)
                {
                    int maxExtra = Math.Min(files.Count, 6);
                    for (int i = 0; i < maxExtra; i++)
                    {
                        string path = files[i];
                        Bitmap? extraBmp = MediaThumbnailStrip.TryDecodeThumbnail(path, 176);
                        if (extraBmp is null)
                        {
                            continue;
                        }

                        Image extraImg = new()
                        {
                            Width = 88,
                            Height = 66,
                            Stretch = Stretch.UniformToFill,
                            Source = extraBmp
                        };
                        Border extraBorder = new()
                        {
                            CornerRadius = new CornerRadius(8),
                            ClipToBounds = true,
                            BorderThickness = new Thickness(1),
                            BorderBrush = theme.Palette.SubtleBorder,
                            Cursor = new Cursor(StandardCursorType.Hand),
                            Child = extraImg
                        };
                        extraBorder.PointerPressed += (_, _) =>
                        {
                            Bitmap? clickedMainBmp = MediaThumbnailStrip.TryDecodeThumbnail(path, 720);
                            if (clickedMainBmp is not null)
                            {
                                if (fbFullPreviewImage.Source is IDisposable prevBmp)
                                {
                                    fbFullPreviewImage.Source = null;
                                    prevBmp.Dispose();
                                }

                                fbFullPreviewImage.Source = clickedMainBmp;
                            }
                        };
                        fbExtraPhotosStrip.Children.Add(extraBorder);
                    }

                    fbExtraPhotosStrip.IsVisible = fbExtraPhotosStrip.Children.Count > 1;
                }
                else
                {
                    fbExtraPhotosStrip.IsVisible = false;
                }
            }
            else
            {
                fbMediaFrame.IsVisible = false;
                fbExtraPhotosStrip.IsVisible = false;
            }
        };

        refreshI18n = () =>
        {
            AppButton.SetText(backToComposerBtn, i18n["Publisher.BtnBackToComposer"]);
            previewPageTitleTb.Text = i18n["Publisher.PreviewPageTitle"];
            AppButton.SetText(spinAnotherBtn, i18n["Publisher.BtnSpinAnotherVariant"]);
            fbTimestampTb.Text = i18n["Publisher.PreviewJustNowPublic"];
            fbReactionSummaryTb.Text = $"❤️ 👍  {i18n["Publisher.PreviewAutoLikedHint"]}";
            fbLikeLbl.Text = $"👍  {i18n["Publisher.PreviewActionLike"]}";
            fbCommentLbl.Text = $"💬  {i18n["Publisher.PreviewActionComment"]}";
            fbShareLbl.Text = $"↗  {i18n["Publisher.PreviewActionShare"]}";
        };

        return previewPageCard;
    }
}
