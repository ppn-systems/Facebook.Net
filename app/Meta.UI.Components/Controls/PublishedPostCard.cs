// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable row card component (<c>PublishedPostCard.cs</c>) for displaying a <see cref="PublishedPostRecord"/>
/// in the Published Posts &amp; Timeline Sync list.
/// </summary>
public static class PublishedPostCard
{
    /// <summary>
    /// Creates an inset surface card for a published or synced timeline post record.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        PublishedPostRecord rec,
        string fallbackAuthorName,
        string? fallbackAuthorAvatarUrl,
        Action onOpenInBrowser,
        Action onDelete)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(rec);
        ArgumentNullException.ThrowIfNull(onOpenInBrowser);
        ArgumentNullException.ThrowIfNull(onDelete);

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
            : fallbackAuthorName;
        string? authorAvatar = rec.AuthorAvatarUrl ?? fallbackAuthorAvatarUrl;
        Border av = AvatarBadge.Create(theme, authorName, 32, authorAvatar);

        StackPanel authorAndTarget = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        TextBlock authorTb = SectionHeader.CreateLabel(theme, authorName, 12.5, FontWeight.Bold);

        bool isGroup = !rec.IsPersonalProfilePost;
        string targetLabel = isGroup
            ? $"👥 {rec.TargetGroupName ?? i18n["Audit.Cat.Group"]}"
            : $"🏠 {i18n["Publisher.TargetModePersonal"]}";
        TextBlock targetTb = SectionHeader.CreateLabel(
            theme,
            $"{targetLabel}  •  {rec.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}",
            11,
            FontWeight.Medium,
            isSecondary: true);
        targetTb.MaxLines = 1;
        targetTb.TextTrimming = TextTrimming.CharacterEllipsis;

        authorAndTarget.Children.Add(authorTb);
        authorAndTarget.Children.Add(targetTb);

        Border kindPill = StatusBadge.Create(
            theme,
            isGroup ? i18n["Publisher.BadgeGroupPost"] : i18n["Publisher.BadgePersonalPost"],
            isGroup ? AuditSeverity.Info : AuditSeverity.Success);
        kindPill.HorizontalAlignment = HorizontalAlignment.Stretch;
        kindPill.Height = 28;

        Button rowOpenBtn = AppButton.CreateRowIcon(
            theme,
            VectorIcons.ExternalLink,
            i18n["Leads.BtnOpenPostInBrowser"],
            onOpenInBrowser);

        Button rowDeleteBtn = AppButton.CreateRowIcon(
            theme,
            VectorIcons.Trash,
            i18n["Publisher.BtnDeletePublishedPost"],
            onDelete,
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

        TextBlock bodyTb = SectionHeader.CreateLabel(theme, rec.MessageText, 12.5, FontWeight.Normal);
        bodyTb.MaxLines = 2;
        bodyTb.TextWrapping = TextWrapping.Wrap;
        bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetRow(topMeta, 0);
        Grid.SetRow(bodyTb, 1);
        cardGrid.Children.Add(topMeta);
        cardGrid.Children.Add(bodyTb);

        return SurfaceCard.CreateInset(theme, cardGrid, new Thickness(10, 8));
    }
}
