// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Spacious, structured table row component for <see cref="PostSummary"/> inside <see cref="DataTableView{T}"/>.
/// Displays Author, Group Badge, Post Content Preview, Buyer-Intent Score, Timestamp, and Comment Status Pill.
/// </summary>
public static class LeadPostTableRow
{
    /// <summary>
    /// Creates a themed multi-column table row for a scraped <paramref name="post"/>.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        PostSummary post,
        int intentScore,
        Func<string> formatIntentBadge,
        bool isCommented)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(formatIntentBadge);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("150,145,*,135"),
            ColumnSpacing = 12,
            MinHeight = 52,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Col 0: Author Avatar + Author Name + Timestamp
        string authorDisplay = string.IsNullOrWhiteSpace(post.AuthorName)
            ? i18n["Leads.DefaultAuthor"]
            : post.AuthorName;

        Border avatarBadge = AvatarBadge.Create(theme, authorDisplay, size: 32, imageUrl: post.AuthorAvatarUrl);
        TextBlock authorTb = SectionHeader.CreateLabel(theme, authorDisplay, 12.5, FontWeight.SemiBold);
        authorTb.MaxLines = 1;
        authorTb.TextWrapping = TextWrapping.NoWrap;
        authorTb.TextTrimming = TextTrimming.CharacterEllipsis;

        string timeText = post.CreatedAt.HasValue
            ? post.CreatedAt.Value.ToLocalTime().ToString("HH:mm dd/MM", CultureInfo.InvariantCulture)
            : i18n["Leads.TimeRecent"];
        TextBlock timeTb = SectionHeader.CreateLabel(theme, timeText, 11, FontWeight.Medium, isSecondary: true);

        StackPanel authorInfoStack = new()
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { authorTb, timeTb }
        };

        Grid authorCell = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(avatarBadge, 0);
        Grid.SetColumn(authorInfoStack, 1);
        authorCell.Children.Add(avatarBadge);
        authorCell.Children.Add(authorInfoStack);

        // Col 1: Group Avatar + Group Name
        string groupText = string.IsNullOrWhiteSpace(post.GroupName)
            ? i18n["Audit.Cat.Group"]
            : post.GroupName;
        Border groupAvatar = AvatarBadge.Create(theme, groupText, size: 26, imageUrl: post.GroupAvatarUrl);
        TextBlock groupNameTb = SectionHeader.CreateLabel(theme, groupText, 12, FontWeight.Medium);
        groupNameTb.MaxLines = 2;
        groupNameTb.TextWrapping = TextWrapping.Wrap;
        groupNameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        groupNameTb.VerticalAlignment = VerticalAlignment.Center;

        Grid groupCell = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(groupAvatar, 0);
        Grid.SetColumn(groupNameTb, 1);
        groupCell.Children.Add(groupAvatar);
        groupCell.Children.Add(groupNameTb);

        // Col 2: Optional Media Thumbnail + Post Message Preview + Engagement Counters
        string rawBody = string.IsNullOrWhiteSpace(post.MessageText)
            ? i18n["Leads.EmptyPostText"]
            : post.MessageText.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');

        TextBlock bodyTb = SectionHeader.CreateLabel(theme, rawBody, 12.5, FontWeight.Normal);
        bodyTb.MaxLines = 2;
        bodyTb.TextWrapping = TextWrapping.Wrap;
        bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

        string mediaBadgeSuffix = !string.IsNullOrWhiteSpace(post.MediaPreviewUrl)
            ? (post.IsVideo ? $"   •   🎬 {i18n["Leads.MediaVideoBadge"]}" : $"   •   🖼 {i18n["Leads.MediaPhotoBadge"]}")
            : string.Empty;

        TextBlock engagementTb = SectionHeader.CreateLabel(
            theme,
            $"👍 {post.ReactionCount}   •   💬 {post.CommentCount}{mediaBadgeSuffix}",
            11,
            FontWeight.Medium,
            isSecondary: true);

        StackPanel messageTextStack = new()
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { bodyTb, engagementTb }
        };

        Grid messageCell = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrWhiteSpace(post.MediaPreviewUrl))
        {
            Image thumbImg = new()
            {
                Width = 56,
                Height = 42,
                Stretch = Stretch.UniformToFill,
                IsVisible = false
            };

            Panel thumbPanel = new()
            {
                Width = 56,
                Height = 42,
                Children = { thumbImg }
            };

            if (post.IsVideo)
            {
                Border playBadge = new()
                {
                    Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    Padding = new Avalonia.Thickness(5, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "▶",
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White
                    }
                };
                thumbPanel.Children.Add(playBadge);
            }

            Border thumbBorder = new()
            {
                Width = 56,
                Height = 42,
                CornerRadius = new Avalonia.CornerRadius(6),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = theme.Palette.SubtleBorder,
                ClipToBounds = true,
                Child = thumbPanel,
                IsVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };

            RemoteImageLoader.Attach(thumbImg, post.MediaPreviewUrl, thumbBorder);
            Grid.SetColumn(thumbBorder, 0);
            messageCell.Children.Add(thumbBorder);
        }

        Grid.SetColumn(messageTextStack, 1);
        messageCell.Children.Add(messageTextStack);

        // Col 3: Stacked Comment Status Pill + Buyer-Intent Score Pill
        string statusLabel = isCommented
            ? i18n["Leads.StatusCommented"]
            : i18n["Leads.StatusPending"];
        AuditSeverity statusSeverity = isCommented ? AuditSeverity.Success : AuditSeverity.Info;
        Border statusPill = StatusPill.Create(theme, statusLabel, statusSeverity);
        statusPill.HorizontalAlignment = HorizontalAlignment.Left;

        AuditSeverity intentSeverity = intentScore >= 75 ? AuditSeverity.Success : AuditSeverity.Warning;
        Border intentPill = StatusPill.Create(theme, formatIntentBadge(), intentSeverity);
        intentPill.HorizontalAlignment = HorizontalAlignment.Left;

        StackPanel statusStack = new()
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { statusPill, intentPill }
        };

        i18n.LanguageChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(post.AuthorName))
            {
                authorTb.Text = i18n["Leads.DefaultAuthor"];
            }

            if (string.IsNullOrWhiteSpace(post.MessageText))
            {
                bodyTb.Text = i18n["Leads.EmptyPostText"];
            }

            if (!post.CreatedAt.HasValue)
            {
                timeTb.Text = i18n["Leads.TimeRecent"];
            }

            StatusPill.SetText(intentPill, formatIntentBadge());
            StatusPill.SetText(statusPill, isCommented ? i18n["Leads.StatusCommented"] : i18n["Leads.StatusPending"]);
        };

        Grid.SetColumn(authorCell, 0);
        Grid.SetColumn(groupCell, 1);
        Grid.SetColumn(messageCell, 2);
        Grid.SetColumn(statusStack, 3);

        row.Children.Add(authorCell);
        row.Children.Add(groupCell);
        row.Children.Add(messageCell);
        row.Children.Add(statusStack);

        Border rowContainer = new()
        {
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(0, 4, 0, 6),
            Child = row
        };

        void ApplyRowBorder() => rowContainer.BorderBrush = theme.Palette.SubtleBorder;
        ApplyRowBorder();
        ThemeLifecycle.Bind(rowContainer, theme, ApplyRowBorder);

        return rowContainer;
    }
}
