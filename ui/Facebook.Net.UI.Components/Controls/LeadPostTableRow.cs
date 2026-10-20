// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Icons;
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
    public static Grid Create(
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
            ColumnDefinitions = new ColumnDefinitions("155,145,*,120,85,125"),
            ColumnSpacing = 12,
            MinHeight = 46,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Col 0: Author Avatar + Name
        string authorDisplay = string.IsNullOrWhiteSpace(post.AuthorName)
            ? i18n["Leads.DefaultAuthor"]
            : post.AuthorName;

        Border avatarBadge = AvatarBadge.Create(theme, authorDisplay, size: 30);
        TextBlock authorTb = SectionHeader.CreateLabel(theme, authorDisplay, 12.5, FontWeight.SemiBold);
        authorTb.MaxLines = 2;
        authorTb.TextWrapping = TextWrapping.Wrap;
        authorTb.TextTrimming = TextTrimming.CharacterEllipsis;
        authorTb.VerticalAlignment = VerticalAlignment.Center;

        Grid authorCell = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(avatarBadge, 0);
        Grid.SetColumn(authorTb, 1);
        authorCell.Children.Add(avatarBadge);
        authorCell.Children.Add(authorTb);

        // Col 1: Group Name Badge
        string groupText = string.IsNullOrWhiteSpace(post.GroupName)
            ? i18n["Audit.Cat.Group"]
            : post.GroupName;
        Border groupPill = IconMetricPill.Create(
            theme,
            VectorIcons.Groups,
            groupText,
            AuditSeverity.Info,
            out _);
        groupPill.VerticalAlignment = VerticalAlignment.Center;
        groupPill.HorizontalAlignment = HorizontalAlignment.Left;

        // Col 2: Post Message Preview + Engagement Counters
        string rawBody = string.IsNullOrWhiteSpace(post.MessageText)
            ? i18n["Leads.EmptyPostText"]
            : post.MessageText.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');

        TextBlock bodyTb = SectionHeader.CreateLabel(theme, rawBody, 12.5, FontWeight.Normal);
        bodyTb.MaxLines = 2;
        bodyTb.TextWrapping = TextWrapping.Wrap;
        bodyTb.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock engagementTb = SectionHeader.CreateLabel(
            theme,
            $"👍 {post.ReactionCount}   •   💬 {post.CommentCount}",
            11,
            FontWeight.Medium,
            isSecondary: true);

        StackPanel messageCell = new()
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { bodyTb, engagementTb }
        };

        // Col 3: Buyer-Intent Score Pill
        AuditSeverity intentSeverity = intentScore >= 75 ? AuditSeverity.Success : AuditSeverity.Warning;
        Border intentPill = StatusPill.Create(theme, formatIntentBadge(), intentSeverity);
        intentPill.HorizontalAlignment = HorizontalAlignment.Left;
        intentPill.VerticalAlignment = VerticalAlignment.Center;

        // Col 4: Timestamp
        string timeText = post.CreatedAt.HasValue
            ? post.CreatedAt.Value.ToLocalTime().ToString("HH:mm dd/MM", CultureInfo.InvariantCulture)
            : i18n["Leads.TimeRecent"];
        TextBlock timeTb = SectionHeader.CreateLabel(theme, timeText, 11.5, FontWeight.Medium, isSecondary: true);
        timeTb.VerticalAlignment = VerticalAlignment.Center;

        // Col 5: Comment Status Pill ("✓ Đã bình luận" vs "● Chưa bình luận")
        string statusLabel = isCommented
            ? i18n["Leads.StatusCommented"]
            : i18n["Leads.StatusPending"];
        AuditSeverity statusSeverity = isCommented ? AuditSeverity.Success : AuditSeverity.Info;
        Border statusPill = StatusPill.Create(theme, statusLabel, statusSeverity);
        statusPill.HorizontalAlignment = HorizontalAlignment.Left;
        statusPill.VerticalAlignment = VerticalAlignment.Center;

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
        Grid.SetColumn(groupPill, 1);
        Grid.SetColumn(messageCell, 2);
        Grid.SetColumn(intentPill, 3);
        Grid.SetColumn(timeTb, 4);
        Grid.SetColumn(statusPill, 5);

        row.Children.Add(authorCell);
        row.Children.Add(groupPill);
        row.Children.Add(messageCell);
        row.Children.Add(intentPill);
        row.Children.Add(timeTb);
        row.Children.Add(statusPill);

        return row;
    }
}
