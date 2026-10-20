// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Reusable Entity List Item Card builders of <see cref="UiControls"/>:
/// Accounts, Group Catalog, Target Campaign Groups, Discovered Buyer Lead Posts, and Audit Log Rows.
/// Ensures 100% visual consistency across all lists in the application.
/// </summary>
public static partial class UiControls
{
    /// <summary>
    /// Creates a reusable list card for an <see cref="AccountProfile"/> item.
    /// </summary>
    public static Border CreateAccountItemCard(
        ThemeService theme,
        LocalizationService i18n,
        AccountProfile item)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(item);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(4, 4)
        };

        Border avatar = CreateAvatarBadge(theme, item.DisplayName, 34);
        Grid.SetColumn(avatar, 0);

        string FormatSubInfo()
        {
            string timePart = item.LastActiveAt.HasValue
                ? $"{i18n["Accounts.LastActivePrefix"]} {item.LastActiveAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}"
                : i18n["Accounts.ReadyToConnect"];
            string netPart = !string.IsNullOrWhiteSpace(item.ProxyId)
                ? i18n["Accounts.ProxyBadgePrefix"]
                : i18n["Accounts.NoProxyBadge"];
            return $"{timePart}  •  {netPart}";
        }

        StackPanel info = new() { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        TextBlock displayName = CreateThemedLabel(theme, item.DisplayName, 13, FontWeight.SemiBold);
        TextBlock subInfo = CreateThemedLabel(theme, FormatSubInfo(), 11, FontWeight.Normal, isSecondary: true);
        info.Children.Add(displayName);
        info.Children.Add(subInfo);
        Grid.SetColumn(info, 1);

        AuditSeverity sev = item.Status == AccountStatus.Active
            ? AuditSeverity.Success
            : (item.Status == AccountStatus.Checkpoint ? AuditSeverity.Error : AuditSeverity.Warning);
        Border statusPill = CreateStatusPill(theme, i18n.FormatAccountStatus(item.Status), sev);
        Grid.SetColumn(statusPill, 2);

        i18n.LanguageChanged += (_, _) =>
        {
            subInfo.Text = FormatSubInfo();
            SetPillText(statusPill, i18n.FormatAccountStatus(item.Status));
        };

        row.Children.Add(avatar);
        row.Children.Add(info);
        row.Children.Add(statusPill);
        return CreateInsetWell(theme, row, new Thickness(10, 6));
    }

    /// <summary>
    /// Creates a reusable row for a <see cref="GroupSummary"/> item in the Facebook Group Catalog.
    /// </summary>
    public static Grid CreateGroupCatalogItemCard(
        ThemeService theme,
        LocalizationService i18n,
        GroupSummary grp)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(grp);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(4)
        };

        Border badge = CreateAvatarBadge(theme, grp.Name, 30);
        Grid.SetColumn(badge, 0);

        TextBlock nameTb = CreateThemedLabel(theme, grp.Name, 13, FontWeight.Medium);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(nameTb, 1);

        Border statePill = CreateStatusPill(
            theme,
            i18n.FormatGroupMembership(grp.MembershipState),
            grp.MembershipState == GroupMembershipState.Member ? AuditSeverity.Success : AuditSeverity.Info);
        Grid.SetColumn(statePill, 2);

        i18n.LanguageChanged += (_, _) =>
            SetPillText(statePill, i18n.FormatGroupMembership(grp.MembershipState));

        row.Children.Add(badge);
        row.Children.Add(nameTb);
        row.Children.Add(statePill);
        return row;
    }

    /// <summary>
    /// Creates a reusable row for a <see cref="SavedTargetGroup"/> item with an enable/disable checkbox.
    /// </summary>
    public static Grid CreateTargetGroupItemCard(
        ThemeService theme,
        SavedTargetGroup tg,
        Action<bool> onToggled)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(tg);
        ArgumentNullException.ThrowIfNull(onToggled);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(4)
        };

        CheckBox enabledCheck = new()
        {
            IsChecked = tg.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        enabledCheck.IsCheckedChanged += (_, _) =>
        {
            bool nextState = enabledCheck.IsChecked ?? false;
            if (nextState != tg.IsEnabled)
            {
                onToggled(nextState);
            }
        };

        Border avatar = CreateAvatarBadge(theme, tg.GroupName, 28);
        TextBlock nameTb = CreateThemedLabel(theme, tg.GroupName, 13, FontWeight.SemiBold);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;

        Grid.SetColumn(enabledCheck, 0);
        Grid.SetColumn(avatar, 1);
        Grid.SetColumn(nameTb, 2);
        row.Children.Add(enabledCheck);
        row.Children.Add(avatar);
        row.Children.Add(nameTb);
        return row;
    }

    /// <summary>
    /// Creates a reusable card for a discovered <see cref="PostSummary"/> lead using SVG icons for reactions and comments.
    /// </summary>
    public static Border CreateLeadPostItemCard(
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
        authorRow.Children.Add(CreateAvatarBadge(theme, post.AuthorName ?? i18n["Leads.DefaultAuthor"], 26));

        TextBlock authorTb = CreateThemedLabel(
            theme,
            string.IsNullOrWhiteSpace(post.AuthorName) ? i18n["Leads.DefaultAuthor"] : post.AuthorName,
            13,
            FontWeight.SemiBold);
        authorRow.Children.Add(authorTb);

        Border intentPill = CreateStatusPill(
            theme,
            formatIntentBadge(),
            intentScore >= 75 ? AuditSeverity.Success : AuditSeverity.Warning);
        authorRow.Children.Add(intentPill);

        Border reactionsPill = CreateIconMetricPill(
            theme,
            VectorIcons.ThumbUp,
            post.ReactionCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Info,
            out _);
        authorRow.Children.Add(reactionsPill);

        Border commentsPill = CreateIconMetricPill(
            theme,
            VectorIcons.SalesComment,
            post.CommentCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Info,
            out _);
        authorRow.Children.Add(commentsPill);

        TextBlock snippet = CreateThemedLabel(
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

            SetPillText(intentPill, formatIntentBadge());
        };

        card.Children.Add(authorRow);
        card.Children.Add(snippet);
        return CreateInsetWell(theme, card, new Thickness(10, 6));
    }

    /// <summary>
    /// Creates a reusable 5-column row for an <see cref="AuditLogRecord"/> entry.
    /// </summary>
    public static Grid CreateAuditLogItemRow(
        ThemeService theme,
        LocalizationService i18n,
        AuditLogRecord rec)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(rec);

        Grid logRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("66,115,105,130,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(2, 2)
        };

        TextBlock tsTb = CreateThemedLabel(
            theme,
            rec.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            11,
            FontWeight.Normal,
            isSecondary: true);
        tsTb.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        logRow.Children.Add(tsTb);

        TextBlock accTb = CreateThemedLabel(theme, rec.AccountDisplayName, 11, FontWeight.SemiBold);
        accTb.TextTrimming = TextTrimming.CharacterEllipsis;
        accTb.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(accTb, 1);
        logRow.Children.Add(accTb);

        Border catPill = CreateStatusPill(
            theme,
            i18n.FormatAuditCategory(rec.Category),
            rec.Severity);
        i18n.LanguageChanged += (_, _) =>
            SetPillText(catPill, i18n.FormatAuditCategory(rec.Category));
        Grid.SetColumn(catPill, 2);
        logRow.Children.Add(catPill);

        TextBlock targetTb = CreateThemedLabel(theme, rec.TargetName, 11, FontWeight.Normal, isSecondary: true);
        targetTb.TextTrimming = TextTrimming.CharacterEllipsis;
        targetTb.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(targetTb, 3);
        logRow.Children.Add(targetTb);

        TextBlock msgTb = CreateThemedLabel(theme, rec.Message, 12);
        msgTb.TextWrapping = TextWrapping.NoWrap;
        msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(msgTb, 4);
        logRow.Children.Add(msgTb);

        return logRow;
    }
}
