// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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
/// Standalone Group Catalog, Target Campaign Group, and Color-Coded Group Tag Pill component.
/// </summary>
public static class GroupRowCard
{
    /// <summary>
    /// Preset vibrant color palette for user-defined <see cref="GroupTag"/> badges.
    /// </summary>
    public static readonly string[] PresetTagColors =
    [
        "#F6821F", // Cloudflare Orange
        "#3B82F6", // Ocean Blue
        "#10B981", // Emerald Green
        "#8B5CF6", // Royal Violet
        "#F43F5E", // Crimson Rose
        "#F59E0B", // Amber Gold
        "#06B6D4", // Cyan Teal
        "#EC4899"  // Hot Pink
    ];

    /// <summary>
    /// Creates a vibrant color-coded badge or interactive chip for a <see cref="GroupTag"/>.
    /// </summary>
    public static Border CreateTagPill(
        GroupTag tag,
        string? suffixText = null,
        bool isActive = true,
        Action? onClick = null,
        Action? onDelete = null)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (!Color.TryParse(tag.ColorHex, out Color baseColor))
        {
            baseColor = Color.FromRgb(246, 130, 31);
        }

        SolidColorBrush dotBrush = new(baseColor);
        SolidColorBrush bgBrush = new(Color.FromArgb(isActive ? (byte)46 : (byte)18, baseColor.R, baseColor.G, baseColor.B));
        SolidColorBrush borderBrush = new(Color.FromArgb(isActive ? (byte)175 : (byte)80, baseColor.R, baseColor.G, baseColor.B));
        SolidColorBrush textBrush = new(Color.FromRgb(
            (byte)Math.Min(255, baseColor.R + 35),
            (byte)Math.Min(255, baseColor.G + 35),
            (byte)Math.Min(255, baseColor.B + 35)));

        Border dot = new()
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(999),
            Background = dotBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        string fullText = string.IsNullOrWhiteSpace(suffixText)
            ? tag.Name
            : $"{tag.Name} {suffixText}";

        TextBlock label = new()
        {
            Text = fullText,
            FontSize = 11,
            FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = textBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { dot, label }
        };

        if (onDelete is not null)
        {
            TextBlock deleteBtn = new()
            {
                Text = "×",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = textBrush,
                Margin = new Thickness(3, -1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            deleteBtn.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                onDelete();
            };
            content.Children.Add(deleteBtn);
        }

        Border pill = new()
        {
            Background = bgBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 2.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content
        };

        if (onClick is not null)
        {
            pill.Cursor = new Cursor(StandardCursorType.Hand);
            pill.PointerPressed += (_, e) =>
            {
                if (!e.Handled)
                {
                    e.Handled = true;
                    onClick();
                }
            };
        }

        return pill;
    }

    /// <summary>
    /// Creates a reusable row for a <see cref="GroupSummary"/> item in the Facebook Group Catalog,
    /// including optional colored tag pills, a 1-click tag assignment popover, and contextual per-row actions.
    /// </summary>
    public static Grid CreateCatalogRow(
        ThemeService theme,
        LocalizationService i18n,
        GroupSummary grp,
        IReadOnlyList<GroupTag>? assignedTags = null,
        IReadOnlyList<GroupTag>? allTags = null,
        Action<string>? onToggleTag = null,
        string? addTagLabel = null,
        bool isAlreadyInTargets = false,
        Action? onAddToTarget = null,
        Action? onJoinGroup = null,
        string? addToTargetLabel = null,
        string? joinLabel = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(grp);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        Border badge = AvatarBadge.Create(theme, grp.Name, 32, grp.AvatarUrl);
        Grid.SetColumn(badge, 0);

        StackPanel nameAndTagsStack = new()
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock nameTb = SectionHeader.CreateLabel(theme, grp.Name, 13, FontWeight.SemiBold);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        nameAndTagsStack.Children.Add(nameTb);

        if (assignedTags is not null && assignedTags.Count > 0)
        {
            WrapPanel tagWrap = new()
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 5,
                LineSpacing = 4
            };
            for (int i = 0; i < assignedTags.Count; i++)
            {
                GroupTag t = assignedTags[i];
                tagWrap.Children.Add(CreateTagPill(
                    t,
                    onDelete: onToggleTag is not null ? () => onToggleTag(t.Id) : null));
            }

            nameAndTagsStack.Children.Add(tagWrap);
        }

        Grid.SetColumn(nameAndTagsStack, 1);

        StackPanel rightActionsStack = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (allTags is not null && onToggleTag is not null)
        {
            Button tagMenuBtn = BuildTagAssignFlyoutButton(theme, assignedTags, allTags, onToggleTag, addTagLabel ?? "+ Nhãn");
            rightActionsStack.Children.Add(tagMenuBtn);
        }

        Border statePill = StatusPill.Create(
            theme,
            i18n.FormatGroupMembership(grp.MembershipState),
            grp.MembershipState == GroupMembershipState.Member ? AuditSeverity.Success : AuditSeverity.Info);
        statePill.Padding = new Thickness(10, 3);
        statePill.MinHeight = 25;
        rightActionsStack.Children.Add(statePill);

        if (!isAlreadyInTargets && onAddToTarget is not null)
        {
            Button addTargetBtn = new()
            {
                Content = new TextBlock
                {
                    Text = addToTargetLabel ?? "+ Nhóm đích",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = theme.Palette.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = theme.Palette.SecondarySurface,
                BorderBrush = theme.Palette.InteractiveBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 25,
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            addTargetBtn.Click += (_, _) => onAddToTarget();
            rightActionsStack.Children.Add(addTargetBtn);
        }

        if ((grp.MembershipState == GroupMembershipState.CanJoin ||
             grp.MembershipState == GroupMembershipState.Unknown) &&
            onJoinGroup is not null)
        {
            Button joinBtn = new()
            {
                Content = new TextBlock
                {
                    Text = joinLabel ?? "Tham gia",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = theme.Palette.BrandPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = theme.Palette.BrandSubtleFill,
                BorderBrush = theme.Palette.BrandPrimary,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Height = 25,
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            joinBtn.Click += (_, _) => onJoinGroup();
            rightActionsStack.Children.Add(joinBtn);
        }

        i18n.LanguageChanged += (_, _) =>
            StatusPill.SetText(statePill, i18n.FormatGroupMembership(grp.MembershipState));

        Grid.SetColumn(rightActionsStack, 2);
        row.Children.Add(badge);
        row.Children.Add(nameAndTagsStack);
        row.Children.Add(rightActionsStack);
        return row;
    }

    /// <summary>
    /// Creates a reusable row for a <see cref="SavedTargetGroup"/> item with an enable/disable checkbox,
    /// colored tag pills, a 1-click tag assignment popover, and an optional remove button.
    /// </summary>
    public static Grid CreateTargetRow(
        ThemeService theme,
        SavedTargetGroup tg,
        Action<bool> onToggled,
        IReadOnlyList<GroupTag>? assignedTags = null,
        IReadOnlyList<GroupTag>? allTags = null,
        Action<string>? onToggleTag = null,
        string? addTagLabel = null,
        Action? onRemove = null,
        string? removeLabel = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(tg);
        ArgumentNullException.ThrowIfNull(onToggled);

        Grid row = new()
        {
            ColumnDefinitions = onRemove is not null
                ? new ColumnDefinitions("Auto,Auto,*,Auto,28")
                : new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 3),
            VerticalAlignment = VerticalAlignment.Center
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

        Border avatar = AvatarBadge.Create(theme, tg.GroupName, 30, tg.AvatarUrl);

        StackPanel nameAndTagsStack = new()
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock nameTb = SectionHeader.CreateLabel(theme, tg.GroupName, 13, FontWeight.SemiBold);
        nameTb.TextWrapping = TextWrapping.NoWrap;
        nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        nameAndTagsStack.Children.Add(nameTb);

        if (assignedTags is not null && assignedTags.Count > 0)
        {
            WrapPanel tagWrap = new()
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 5,
                LineSpacing = 4
            };
            for (int i = 0; i < assignedTags.Count; i++)
            {
                GroupTag t = assignedTags[i];
                tagWrap.Children.Add(CreateTagPill(
                    t,
                    onDelete: onToggleTag is not null ? () => onToggleTag(t.Id) : null));
            }

            nameAndTagsStack.Children.Add(tagWrap);
        }

        Grid.SetColumn(enabledCheck, 0);
        Grid.SetColumn(avatar, 1);
        Grid.SetColumn(nameAndTagsStack, 2);
        row.Children.Add(enabledCheck);
        row.Children.Add(avatar);
        row.Children.Add(nameAndTagsStack);

        if (allTags is not null && onToggleTag is not null)
        {
            Button tagMenuBtn = BuildTagAssignFlyoutButton(theme, assignedTags, allTags, onToggleTag, addTagLabel ?? "+ Nhãn");
            Grid.SetColumn(tagMenuBtn, 3);
            row.Children.Add(tagMenuBtn);
        }

        if (onRemove is not null)
        {
            Button removeBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.Trash,
                removeLabel ?? "Gỡ",
                onRemove,
                AuditSeverity.Error);
            Grid.SetColumn(removeBtn, 4);
            row.Children.Add(removeBtn);
        }

        return row;
    }

    private static Button BuildTagAssignFlyoutButton(
        ThemeService theme,
        IReadOnlyList<GroupTag>? assignedTags,
        IReadOnlyList<GroupTag> allTags,
        Action<string> onToggleTag,
        string buttonLabel)
    {
        HashSet<string> assignedSet = new(StringComparer.OrdinalIgnoreCase);
        if (assignedTags is not null)
        {
            for (int i = 0; i < assignedTags.Count; i++)
            {
                _ = assignedSet.Add(assignedTags[i].Id);
            }
        }

        StackPanel flyoutStack = new()
        {
            Spacing = 6,
            MinWidth = 195
        };

        Flyout flyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedRight
        };

        for (int i = 0; i < allTags.Count; i++)
        {
            GroupTag tag = allTags[i];
            bool hasTag = assignedSet.Contains(tag.Id);
            string suffix = hasTag ? "✓" : string.Empty;
            Border chip = CreateTagPill(
                tag,
                suffixText: suffix,
                isActive: hasTag,
                onClick: () =>
                {
                    flyout.Hide();
                    onToggleTag(tag.Id);
                });
            chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            flyoutStack.Children.Add(chip);
        }

        flyout.Content = SurfaceCard.Create(theme, flyoutStack, new Thickness(10));

        Button btn = new()
        {
            Content = new TextBlock
            {
                Text = buttonLabel,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = theme.Palette.TextSecondary
            },
            Background = theme.Palette.SecondarySurface,
            BorderBrush = theme.Palette.SubtleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Flyout = flyout
        };

        return btn;
    }
}
