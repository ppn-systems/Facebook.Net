// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable row card component (<c>BrowserProfileRowCard.cs</c>) for displaying a <see cref="BrowserProfileOption"/>
/// in the Browser Profile Manager tab.
/// </summary>
public static class BrowserProfileRowCard
{
    /// <summary>
    /// Creates a row for a managed or system browser profile with async disk size resolution, Use Profile button, Open Folder button, and Delete button.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        LocalizationService i18n,
        BrowserProfileOption opt,
        bool isCurrentlyAssigned,
        IDictionary<string, long> profileSizeCache,
        Func<string, long> computeProfileSizeBytes,
        Func<long, string> formatByteSize,
        Func<Task> onUseProfileAsync,
        Action onOpenFolder,
        Action? onDeleteManagedProfile = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(profileSizeCache);
        ArgumentNullException.ThrowIfNull(computeProfileSizeBytes);
        ArgumentNullException.ThrowIfNull(formatByteSize);
        ArgumentNullException.ThrowIfNull(onUseProfileAsync);
        ArgumentNullException.ThrowIfNull(onOpenFolder);

        Grid rowGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,128,142,28,28"),
            ColumnSpacing = 10,
            Margin = new Thickness(4, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        Border iconCircle = new()
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = opt.IsManagedByApp ? theme.Palette.BrandSubtleFill : theme.Palette.SecondarySurface,
            BorderBrush = opt.IsManagedByApp ? theme.Palette.BrandPrimary : theme.Palette.SubtleBorder,
            BorderThickness = new Thickness(1),
            Child = VectorIcons.Create(
                VectorIcons.Browser,
                16,
                opt.IsManagedByApp ? theme.Palette.BrandPrimary : theme.Palette.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel infoStack = new()
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel nameAndSizeRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock nameTb = SectionHeader.CreateLabel(theme, opt.DisplayName, 13, FontWeight.SemiBold);
        nameTb.MaxLines = 1;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        nameAndSizeRow.Children.Add(nameTb);

        string initialSizeText = profileSizeCache.TryGetValue(opt.DirectoryPath, out long cachedBytes)
            ? $"•  {formatByteSize(cachedBytes)}"
            : string.Empty;
        TextBlock sizeTb = SectionHeader.CreateLabel(
            theme,
            initialSizeText,
            11.5,
            FontWeight.Medium,
            isSecondary: true);
        nameAndSizeRow.Children.Add(sizeTb);

        if (string.IsNullOrEmpty(initialSizeText))
        {
            string dirPath = opt.DirectoryPath;
            _ = Task.Run(() =>
            {
                long bytes = computeProfileSizeBytes(dirPath);
                Dispatcher.UIThread.Post(() =>
                {
                    profileSizeCache[dirPath] = bytes;
                    sizeTb.Text = $"•  {formatByteSize(bytes)}";
                });
            });
        }

        if (opt.HasFacebookSession)
        {
            Border fbSessionBadge = StatusPill.Create(
                theme,
                i18n["Settings.BadgeHasFbSession"],
                AuditSeverity.Success);
            nameAndSizeRow.Children.Add(fbSessionBadge);
        }

        TextBlock pathTb = SectionHeader.CreateLabel(theme, opt.DirectoryPath, 11.5, FontWeight.Normal, isSecondary: true);
        pathTb.MaxLines = 1;
        pathTb.TextTrimming = TextTrimming.CharacterEllipsis;

        infoStack.Children.Add(nameAndSizeRow);
        infoStack.Children.Add(pathTb);

        Border sourceBadge = StatusPill.Create(
            theme,
            opt.IsManagedByApp ? i18n["Settings.BadgeManagedProfile"] : i18n["Settings.BadgeSystemProfile"],
            opt.IsManagedByApp ? AuditSeverity.Success : AuditSeverity.Info);
        sourceBadge.HorizontalAlignment = HorizontalAlignment.Stretch;
        sourceBadge.Height = 28;

        Grid.SetColumn(iconCircle, 0);
        Grid.SetColumn(infoStack, 1);
        Grid.SetColumn(sourceBadge, 2);
        rowGrid.Children.Add(iconCircle);
        rowGrid.Children.Add(infoStack);
        rowGrid.Children.Add(sourceBadge);

        if (isCurrentlyAssigned)
        {
            Border activePill = StatusPill.Create(
                theme,
                i18n["Settings.BadgeActiveProfile"],
                AuditSeverity.Success);
            activePill.HorizontalAlignment = HorizontalAlignment.Stretch;
            activePill.Height = 28;
            Grid.SetColumn(activePill, 3);
            rowGrid.Children.Add(activePill);
        }
        else
        {
            Button useProfileBtn = new()
            {
                Content = new TextBlock
                {
                    Text = i18n["Settings.BtnUseProfile"],
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = theme.Palette.BrandPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = theme.Palette.BrandSubtleFill,
                BorderBrush = theme.Palette.BrandPrimary,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 28,
                Padding = new Thickness(10, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            useProfileBtn.Click += async (_, _) => await onUseProfileAsync().ConfigureAwait(true);
            Grid.SetColumn(useProfileBtn, 3);
            rowGrid.Children.Add(useProfileBtn);
        }

        Button openFolderRowBtn = AppButton.CreateRowIcon(
            theme,
            VectorIcons.Folder,
            i18n["Settings.BtnOpenProfileFolderTip"],
            onOpenFolder,
            AuditSeverity.Info);
        Grid.SetColumn(openFolderRowBtn, 4);
        rowGrid.Children.Add(openFolderRowBtn);

        if (opt.IsManagedByApp && onDeleteManagedProfile is not null)
        {
            Button deleteProfileBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.Trash,
                i18n["Accounts.BtnRowDelete"],
                onDeleteManagedProfile,
                AuditSeverity.Error);
            Grid.SetColumn(deleteProfileBtn, 5);
            rowGrid.Children.Add(deleteProfileBtn);
        }

        return rowGrid;
    }
}
