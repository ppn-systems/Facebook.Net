// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable row component (<c>FriendRequestRowCard.cs</c>) for displaying a <see cref="UserSummary"/>
/// in the Incoming and Outgoing Friend Requests lists.
/// </summary>
public static class FriendRequestRowCard
{
    /// <summary>
    /// Creates a row for an incoming or outgoing friend request with Open Profile, Accept, Decline, or Cancel actions.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        UserSummary user,
        string openProfileLabel,
        Action onOpenProfile,
        Action? acceptCommand = null,
        Action? rejectCommand = null,
        Action? cancelCommand = null,
        string? acceptLabel = null,
        string? rejectLabel = null,
        string? cancelLabel = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(onOpenProfile);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        Border avatar = AvatarBadge.Create(theme, user.Name, 34, user.AvatarUri);
        Grid.SetColumn(avatar, 0);

        TextBlock nameTb = SectionHeader.CreateLabel(theme, user.Name, 13, FontWeight.SemiBold);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        nameTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(nameTb, 1);

        row.Children.Add(avatar);
        row.Children.Add(nameTb);

        int colIndex = 2;

        Button openProfileBtn = AppButton.CreateRowIcon(
            theme,
            VectorIcons.Browser,
            openProfileLabel,
            onOpenProfile,
            AuditSeverity.Info);
        Grid.SetColumn(openProfileBtn, colIndex++);
        row.Children.Add(openProfileBtn);

        if (acceptCommand is not null)
        {
            Button acceptBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.CheckCircle,
                acceptLabel ?? "Accept",
                acceptCommand,
                AuditSeverity.Success);
            Grid.SetColumn(acceptBtn, colIndex++);
            row.Children.Add(acceptBtn);
        }

        if (rejectCommand is not null)
        {
            Button rejectBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.Trash,
                rejectLabel ?? "Decline",
                rejectCommand,
                AuditSeverity.Error);
            Grid.SetColumn(rejectBtn, colIndex++);
            row.Children.Add(rejectBtn);
        }

        if (cancelCommand is not null)
        {
            Button cancelBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.StopCircle,
                cancelLabel ?? "Cancel",
                cancelCommand,
                AuditSeverity.Warning);
            Grid.SetColumn(cancelBtn, colIndex++);
            row.Children.Add(cancelBtn);
        }

        return row;
    }
}
