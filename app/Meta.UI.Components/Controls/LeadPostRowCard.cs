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
/// Standalone Buyer Lead Post card component (<see cref="PostSummary"/>) with SVG reaction and comment badges.
/// </summary>
public static class LeadPostRowCard
{
    /// <summary>
    /// Creates a reusable card for a discovered <see cref="PostSummary"/> lead using SVG icons for reactions and comments.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        PostSummary post,
        int intentScore,
        Func<string> formatIntentBadge)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(formatIntentBadge);

        StackPanel card = new() { Spacing = 4, Margin = new Thickness(4) };
        StackPanel authorRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        authorRow.Children.Add(AvatarBadge.Create(theme, post.AuthorName ?? i18n["Leads.DefaultAuthor"], 26));

        TextBlock authorTb = SectionHeader.CreateLabel(
            theme,
            string.IsNullOrWhiteSpace(post.AuthorName) ? i18n["Leads.DefaultAuthor"] : post.AuthorName,
            13,
            FontWeight.SemiBold);
        authorRow.Children.Add(authorTb);

        if (!string.IsNullOrWhiteSpace(post.GroupName))
        {
            Border groupPill = IconMetricPill.Create(
                theme,
                VectorIcons.Groups,
                post.GroupName,
                AuditSeverity.Info,
                out _);
            authorRow.Children.Add(groupPill);
        }

        Border intentPill = StatusPill.Create(
            theme,
            formatIntentBadge(),
            intentScore >= 75 ? AuditSeverity.Success : AuditSeverity.Warning);
        authorRow.Children.Add(intentPill);

        Border reactionsPill = IconMetricPill.Create(
            theme,
            VectorIcons.ThumbUp,
            post.ReactionCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Info,
            out _);
        authorRow.Children.Add(reactionsPill);

        Border commentsPill = IconMetricPill.Create(
            theme,
            VectorIcons.SalesComment,
            post.CommentCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Info,
            out _);
        authorRow.Children.Add(commentsPill);

        TextBlock snippet = SectionHeader.CreateLabel(
            theme,
            string.IsNullOrWhiteSpace(post.MessageText) ? i18n["Leads.MediaOnlyPost"] : post.MessageText,
            12,
            FontWeight.Normal,
            isSecondary: true);
        snippet.MaxLines = 2;

        i18n.LanguageChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(post.AuthorName))
            {
                authorTb.Text = i18n["Leads.DefaultAuthor"];
            }

            if (string.IsNullOrWhiteSpace(post.MessageText))
            {
                snippet.Text = i18n["Leads.MediaOnlyPost"];
            }

            StatusPill.SetText(intentPill, formatIntentBadge());
        };

        card.Children.Add(authorRow);
        card.Children.Add(snippet);
        return SurfaceCard.CreateInset(theme, card, new Thickness(10, 6));
    }
}
