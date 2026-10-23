// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable Flyout button component that inserts dynamic Spintax &amp; context variables
/// (<c>{author_name}</c>, <c>{group_name}</c>, <c>{date}</c>, <c>{time}</c>, <c>#{random_id}</c>, greeting spintax)
/// into a target <see cref="TextBox"/>.
/// </summary>
public static class SpintaxVariablePicker
{
    /// <summary>
    /// Creates a secondary button with an attached <see cref="Flyout"/> menu of dynamic variables.
    /// </summary>
    public static Button CreateButton(
        ThemeService theme,
        LocalizationService i18n,
        TextBox targetBox,
        out Action refreshI18n,
        bool includeAuthorName = false)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(targetBox);

        StackPanel variableMenuStack = new()
        {
            Spacing = 4,
            MinWidth = 245
        };

        Flyout variableFlyout = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = SurfaceCard.Create(theme, variableMenuStack, new Thickness(8))
        };

        Button insertVarBtn = AppButton.CreateSecondary(
            theme,
            i18n["Common.BtnInsertVariable"],
            null,
            VectorIcons.Plus);
        insertVarBtn.Flyout = variableFlyout;

        void RebuildItems()
        {
            AppButton.SetText(insertVarBtn, i18n["Common.BtnInsertVariable"]);
            variableMenuStack.Children.Clear();

            (string Label, string Token)[] items = includeAuthorName
                ?
                [
                    ($"{i18n["Common.VarAuthorNameLabel"]}  •  {{author_name}}", "{author_name}"),
                    ($"{i18n["Common.VarGroupNameLabel"]}  •  {{group_name}}", "{group_name}"),
                    ($"{i18n["Common.VarGreetingLabel"]}  •  {{Xin chào|Chào bạn}}", "{Xin chào|Chào bạn|Chào anh/chị}"),
                    ($"{i18n["Common.VarDateLabel"]}  •  {{date}}", "{date}"),
                    ($"{i18n["Common.VarTimeLabel"]}  •  {{time}}", "{time}"),
                    ($"{i18n["Common.VarRandomIdLabel"]}  •  #{{random_id}}", "#{random_id}")
                ]
                :
                [
                    ($"{i18n["Common.VarGreetingLabel"]}  •  {{Xin chào|Hello}}", "{Xin chào|Chào cả nhà|Chào anh chị em}"),
                    ($"{i18n["Common.VarGroupNameLabel"]}  •  {{group_name}}", "{group_name}"),
                    ($"{i18n["Common.VarDateLabel"]}  •  {{date}}", "{date}"),
                    ($"{i18n["Common.VarTimeLabel"]}  •  {{time}}", "{time}"),
                    ($"{i18n["Common.VarRandomIdLabel"]}  •  #{{random_id}}", "#{random_id}")
                ];

            for (int i = 0; i < items.Length; i++)
            {
                (string itemLabel, string tokenToInsert) = items[i];
                Button itemBtn = new()
                {
                    Content = SectionHeader.CreateLabel(theme, itemLabel, 12, FontWeight.SemiBold),
                    Background = theme.Palette.SecondarySurface,
                    BorderBrush = theme.Palette.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                itemBtn.Click += (_, _) =>
                {
                    variableFlyout.Hide();
                    string current = targetBox.Text ?? string.Empty;
                    targetBox.Text = string.IsNullOrEmpty(current)
                        ? tokenToInsert
                        : $"{current} {tokenToInsert}";
                    targetBox.CaretIndex = targetBox.Text.Length;
                    _ = targetBox.Focus();
                };
                variableMenuStack.Children.Add(itemBtn);
            }
        }

        RebuildItems();
        ThemeLifecycle.Bind(insertVarBtn, theme, RebuildItems);
        refreshI18n = RebuildItems;
        return insertVarBtn;
    }
}
