// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone Group Catalog and Target Campaign Group row item component.
/// </summary>
public static class GroupRowCard
{
    /// <summary>
    /// Creates a reusable row for a <see cref="GroupSummary"/> item in the Facebook Group Catalog.
    /// </summary>
    public static Grid CreateCatalogRow(
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

        Border badge = AvatarBadge.Create(theme, grp.Name, 30, grp.AvatarUrl);
        Grid.SetColumn(badge, 0);

        TextBlock nameTb = SectionHeader.CreateLabel(theme, grp.Name, 13, FontWeight.Medium);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(nameTb, 1);

        Border statePill = StatusPill.Create(
            theme,
            i18n.FormatGroupMembership(grp.MembershipState),
            grp.MembershipState == GroupMembershipState.Member ? AuditSeverity.Success : AuditSeverity.Info);
        Grid.SetColumn(statePill, 2);

        i18n.LanguageChanged += (_, _) =>
            StatusPill.SetText(statePill, i18n.FormatGroupMembership(grp.MembershipState));

        row.Children.Add(badge);
        row.Children.Add(nameTb);
        row.Children.Add(statePill);
        return row;
    }

    /// <summary>
    /// Creates a reusable row for a <see cref="SavedTargetGroup"/> item with an enable/disable checkbox.
    /// </summary>
    public static Grid CreateTargetRow(
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

        Border avatar = AvatarBadge.Create(theme, tg.GroupName, 28, tg.AvatarUrl);
        TextBlock nameTb = SectionHeader.CreateLabel(theme, tg.GroupName, 13, FontWeight.SemiBold);
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
}
