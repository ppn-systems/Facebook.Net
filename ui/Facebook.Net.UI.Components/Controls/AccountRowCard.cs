// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
/// Standalone Account list item card component (<see cref="AccountProfile"/>).
/// </summary>
public static class AccountRowCard
{
    /// <summary>
    /// Creates a reusable list card for an <see cref="AccountProfile"/> item with optional contextual row actions.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        AccountProfile item,
        string? subText = null,
        Action? onWarmUpOrLogin = null,
        string? warmUpOrLoginLabel = null,
        Action? onCheckHealth = null,
        string? checkHealthLabel = null,
        Action? onDelete = null,
        string? deleteLabel = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(item);

        bool hasActions = onWarmUpOrLogin is not null || onCheckHealth is not null || onDelete is not null;

        Grid row = new()
        {
            ColumnDefinitions = hasActions
                ? new ColumnDefinitions("Auto,*,124,108,28,28")
                : new ColumnDefinitions("Auto,*,124"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        Border avatar = AvatarBadge.Create(theme, item.DisplayName, 34, item.AvatarUrl);
        Grid.SetColumn(avatar, 0);

        string FormatSubInfo()
        {
            if (!string.IsNullOrWhiteSpace(subText))
            {
                return subText;
            }

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
        statusPill.HorizontalAlignment = HorizontalAlignment.Stretch;
        statusPill.Height = 28;
        Grid.SetColumn(statusPill, 2);

        if (hasActions)
        {
            if (onWarmUpOrLogin is not null)
            {
                bool isActive = item.Status == AccountStatus.Active;
                string primaryActionText = warmUpOrLoginLabel ?? (isActive ? "Tương tác" : "Đăng nhập lại");

                Button warmOrLoginBtn = new()
                {
                    Content = new TextBlock
                    {
                        Text = primaryActionText,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = isActive ? theme.Palette.BrandPrimary : theme.Palette.WarningText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Background = isActive ? theme.Palette.BrandSubtleFill : theme.Palette.WarningFill,
                    BorderBrush = isActive ? theme.Palette.BrandPrimary : theme.Palette.WarningBorder,
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
                warmOrLoginBtn.Click += (_, _) => onWarmUpOrLogin();
                Grid.SetColumn(warmOrLoginBtn, 3);
                row.Children.Add(warmOrLoginBtn);
            }

            if (onCheckHealth is not null)
            {
                Button checkBtn = AppButton.CreateRowIcon(
                    theme,
                    VectorIcons.Sync,
                    checkHealthLabel ?? "Kiểm tra",
                    onCheckHealth);
                Grid.SetColumn(checkBtn, 4);
                row.Children.Add(checkBtn);
            }

            if (onDelete is not null)
            {
                Button deleteBtn = AppButton.CreateRowIcon(
                    theme,
                    VectorIcons.Trash,
                    deleteLabel ?? "Xóa",
                    onDelete,
                    AuditSeverity.Error);
                Grid.SetColumn(deleteBtn, 5);
                row.Children.Add(deleteBtn);
            }
        }

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
