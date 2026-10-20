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
/// Standalone Account list item card component (<see cref="AccountProfile"/>).
/// </summary>
public static class AccountRowCard
{
    /// <summary>
    /// Creates a reusable list card for an <see cref="AccountProfile"/> item.
    /// </summary>
    public static Border Create(
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

        Border avatar = AvatarBadge.Create(theme, item.DisplayName, 34, item.AvatarUrl);
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
        TextBlock displayName = SectionHeader.CreateLabel(theme, item.DisplayName, 13, FontWeight.SemiBold);
        TextBlock subInfo = SectionHeader.CreateLabel(theme, FormatSubInfo(), 11, FontWeight.Normal, isSecondary: true);
        info.Children.Add(displayName);
        info.Children.Add(subInfo);
        Grid.SetColumn(info, 1);

        AuditSeverity sev = item.Status == AccountStatus.Active
            ? AuditSeverity.Success
            : (item.Status == AccountStatus.Checkpoint ? AuditSeverity.Error : AuditSeverity.Warning);
        Border statusPill = StatusPill.Create(theme, i18n.FormatAccountStatus(item.Status), sev);
        Grid.SetColumn(statusPill, 2);

        i18n.LanguageChanged += (_, _) =>
        {
            subInfo.Text = FormatSubInfo();
            StatusPill.SetText(statusPill, i18n.FormatAccountStatus(item.Status));
        };

        row.Children.Add(avatar);
        row.Children.Add(info);
        row.Children.Add(statusPill);
        return SurfaceCard.CreateInset(theme, row, new Thickness(10, 6));
    }
}
