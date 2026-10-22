// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Leads;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Spacious, structured table row component for <see cref="PostSummary"/> inside <see cref="DataTableView{T}"/>.
/// Displays Author, Group Badge, Post Content Preview, Buyer-Intent Score, Timestamp, Comment Status Pill, and Customer Color Tags.
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
        bool isCommented,
        Action? onQuickReply = null,
        string? quickReplyLabel = null,
        Action? onOpenInBrowser = null,
        string? openBrowserLabel = null,
        string? detectedPhone = null,
        IReadOnlyList<GroupTag>? assignedLeadTags = null,
        IReadOnlyList<GroupTag>? allLeadTags = null,
        Func<string, Task>? onToggleLeadTag = null,
        Func<string, string, Task>? onCreateQuickLeadTag = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(formatIntentBadge);

        string? resolvedPhone = !string.IsNullOrWhiteSpace(detectedPhone)
            ? detectedPhone
            : LeadScoringService.Instance.ExtractPrimaryPhoneNumber(post.MessageText);

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
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { bodyTb, engagementTb }
        };

        if ((assignedLeadTags is { Count: > 0 }) || onToggleLeadTag is not null || onCreateQuickLeadTag is not null)
        {
            WrapPanel leadTagsWrap = BuildLeadTagsWrapRow(
                theme,
                i18n,
                post,
                assignedLeadTags,
                allLeadTags,
                onToggleLeadTag,
                onCreateQuickLeadTag);
            messageTextStack.Children.Add(leadTagsWrap);
        }

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
                    Background = ThemeLifecycle.GetCachedBrush(Color.FromArgb(190, 0, 0, 0)),
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

            RemoteImageLoader.Attach(thumbImg, post.MediaPreviewUrl, thumbBorder, decodeWidth: 360);
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

        Border? phonePill = null;
        if (!string.IsNullOrWhiteSpace(resolvedPhone))
        {
            phonePill = StatusPill.Create(theme, $"{i18n["Leads.PhoneBadgePrefix"]} {resolvedPhone}", AuditSeverity.Success);
            phonePill.HorizontalAlignment = HorizontalAlignment.Left;
            statusStack.Children.Add(phonePill);
        }

        if (onQuickReply is not null || onOpenInBrowser is not null)
        {
            StackPanel actionRow = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (onQuickReply is not null)
            {
                Button quickReplyBtn = AppButton.CreateRowIcon(
                    theme,
                    VectorIcons.SalesComment,
                    quickReplyLabel ?? i18n["Leads.BtnSendCommentSelected"],
                    onQuickReply,
                    AuditSeverity.Success);
                actionRow.Children.Add(quickReplyBtn);
            }

            if (onOpenInBrowser is not null)
            {
                Button openBrowserBtn = AppButton.CreateRowIcon(
                    theme,
                    VectorIcons.ExternalLink,
                    openBrowserLabel ?? i18n["Leads.BtnOpenPostInBrowser"],
                    onOpenInBrowser);
                actionRow.Children.Add(openBrowserBtn);
            }

            statusStack.Children.Add(actionRow);
        }

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

        ThemeLifecycle.BindLanguage(rowContainer, i18n, () =>
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
            if (phonePill is not null && !string.IsNullOrWhiteSpace(resolvedPhone))
            {
                StatusPill.SetText(phonePill, $"{i18n["Leads.PhoneBadgePrefix"]} {resolvedPhone}");
            }
        });

        return rowContainer;
    }

    /// <summary>
    /// Creates a compact wrap panel of assigned customer color tags and a <c>+ Gán nhãn</c> Flyout button
    /// with 1-click tag toggling and inline quick tag creation.
    /// </summary>
    public static WrapPanel BuildLeadTagsWrapRow(
        ThemeService theme,
        LocalizationService i18n,
        PostSummary post,
        IReadOnlyList<GroupTag>? assignedLeadTags,
        IReadOnlyList<GroupTag>? allLeadTags,
        Func<string, Task>? onToggleLeadTag,
        Func<string, string, Task>? onCreateQuickLeadTag)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(post);

        WrapPanel tagsWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 5,
            LineSpacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (assignedLeadTags is not null)
        {
            for (int i = 0; i < assignedLeadTags.Count; i++)
            {
                GroupTag assignedTag = assignedLeadTags[i];
                Border pill = GroupRowCard.CreateTagPill(
                    assignedTag,
                    isActive: true,
                    onClick: onToggleLeadTag is not null ? () => _ = onToggleLeadTag(assignedTag.Id) : null);
                tagsWrap.Children.Add(pill);
            }
        }

        if (onToggleLeadTag is not null || onCreateQuickLeadTag is not null)
        {
            Button assignTagBtn = CreateAssignLeadTagFlyoutButton(
                theme,
                i18n,
                post,
                allLeadTags,
                onToggleLeadTag,
                onCreateQuickLeadTag);
            tagsWrap.Children.Add(assignTagBtn);
        }

        return tagsWrap;
    }

    private static Button CreateAssignLeadTagFlyoutButton(
        ThemeService theme,
        LocalizationService i18n,
        PostSummary post,
        IReadOnlyList<GroupTag>? allLeadTags,
        Func<string, Task>? onToggleLeadTag,
        Func<string, string, Task>? onCreateQuickLeadTag)
    {
        TextBlock btnLabel = new()
        {
            Text = $"+ {i18n["Leads.BtnAssignTag"]}",
            FontSize = 10.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Flyout flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard
        };

        StackPanel? flyoutStack = null;
        string selectedColorHex = GroupRowCard.PresetTagColors[0];

        void RebuildFlyoutContent()
        {
            if (flyoutStack is null)
            {
                flyoutStack = new StackPanel
                {
                    Spacing = 7,
                    Width = 248
                };
                flyout.Content = SurfaceCard.Create(theme, flyoutStack, new Thickness(10));
            }
            else
            {
                flyoutStack.Children.Clear();
            }

            TextBlock headerTb = SectionHeader.CreateLabel(
                theme,
                i18n["Leads.TagPickerHeader"],
                11.5,
                FontWeight.Bold);
            flyoutStack.Children.Add(headerTb);

            StackPanel tagListStack = new() { Spacing = 4 };
            if (allLeadTags is { Count: > 0 })
            {
                for (int i = 0; i < allLeadTags.Count; i++)
                {
                    GroupTag tag = allLeadTags[i];
                    bool hasTag = post.HasTag(tag.Id);

                    Color parsedColor = Color.TryParse(tag.ColorHex, out Color c)
                        ? c
                        : Color.FromRgb(59, 130, 246);
                    IImmutableSolidColorBrush parsedBrush = ThemeLifecycle.GetCachedBrush(parsedColor);

                    Border dot = new()
                    {
                        Width = 9,
                        Height = 9,
                        CornerRadius = new CornerRadius(999),
                        Background = parsedBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    TextBlock nameTb = SectionHeader.CreateLabel(
                        theme,
                        tag.Name,
                        11.5,
                        hasTag ? FontWeight.Bold : FontWeight.Medium);
                    nameTb.VerticalAlignment = VerticalAlignment.Center;
                    nameTb.TextTrimming = TextTrimming.CharacterEllipsis;

                    TextBlock checkTb = new()
                    {
                        Text = hasTag ? "✓" : string.Empty,
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Foreground = parsedBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    Grid rowGrid = new()
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                        ColumnSpacing = 8,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(dot, 0);
                    Grid.SetColumn(nameTb, 1);
                    Grid.SetColumn(checkTb, 2);
                    rowGrid.Children.Add(dot);
                    rowGrid.Children.Add(nameTb);
                    rowGrid.Children.Add(checkTb);

                    Button tagOptionBtn = new()
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 5),
                        Background = hasTag
                            ? ThemeLifecycle.GetCachedBrush(Color.FromArgb(36, parsedColor.R, parsedColor.G, parsedColor.B))
                            : theme.Palette.SecondarySurface,
                        BorderBrush = hasTag
                            ? ThemeLifecycle.GetCachedBrush(Color.FromArgb(140, parsedColor.R, parsedColor.G, parsedColor.B))
                            : theme.Palette.SubtleBorder,
                        Content = rowGrid,
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };

                    tagOptionBtn.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        if (onToggleLeadTag is not null)
                        {
                            await onToggleLeadTag(tag.Id).ConfigureAwait(true);
                        }
                    };

                    tagListStack.Children.Add(tagOptionBtn);
                }
            }

            ScrollViewer listScroll = new()
            {
                MaxHeight = 175,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = tagListStack
            };
            flyoutStack.Children.Add(listScroll);

            if (onCreateQuickLeadTag is not null)
            {
                Border divider = new()
                {
                    Height = 1,
                    Background = theme.Palette.SubtleBorder,
                    Margin = new Thickness(0, 2)
                };
                flyoutStack.Children.Add(divider);

                TextBox newTagNameBox = FormField.CreateTextBox(
                    theme,
                    i18n["Leads.QuickNewTagWatermark"]);
                newTagNameBox.FontSize = 11.5;

                int colorCount = Math.Min(6, GroupRowCard.PresetTagColors.Length);
                string[] quickPalette = GroupRowCard.PresetTagColors[..colorCount];
                WrapPanel colorDotsRow = ColorSwatchPicker.Create(
                    theme,
                    quickPalette,
                    () => selectedColorHex,
                    hex => selectedColorHex = hex,
                    out _,
                    size: 18,
                    showCheckmark: false);

                Button createBtn = AppButton.CreatePrimary(
                    theme,
                    i18n["Leads.BtnCreateQuickTag"],
                    iconSvg: VectorIcons.Plus);
                createBtn.Padding = new Thickness(8, 4);
                createBtn.HorizontalAlignment = HorizontalAlignment.Right;

                createBtn.Click += async (_, _) =>
                {
                    string rawName = (newTagNameBox.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(rawName))
                    {
                        return;
                    }

                    flyout.Hide();
                    await onCreateQuickLeadTag(rawName, selectedColorHex).ConfigureAwait(true);
                };

                Grid bottomActionRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(colorDotsRow, 0);
                Grid.SetColumn(createBtn, 1);
                bottomActionRow.Children.Add(colorDotsRow);
                bottomActionRow.Children.Add(createBtn);

                flyoutStack.Children.Add(newTagNameBox);
                flyoutStack.Children.Add(bottomActionRow);
            }
        }

        flyout.Opening += (_, _) => RebuildFlyoutContent();

        Button btn = new()
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2),
            MinHeight = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = btnLabel,
            Flyout = flyout,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        void ApplyStyle()
        {
            btn.Background = theme.Palette.SecondarySurface;
            btn.BorderBrush = theme.Palette.InteractiveBorder;
            btnLabel.Foreground = theme.Palette.TextSecondary;
        }

        ApplyStyle();
        ThemeLifecycle.Bind(btn, theme, ApplyStyle);
        ThemeLifecycle.BindLanguage(btn, i18n, () => btnLabel.Text = $"+ {i18n["Leads.BtnAssignTag"]}");
        return btn;
    }
}
