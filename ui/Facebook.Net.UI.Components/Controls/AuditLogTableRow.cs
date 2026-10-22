// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone Audit Log table row component (<see cref="AuditLogRecord"/>) aligned with <see cref="DataTableView{T}"/>.
/// </summary>
public static class AuditLogTableRow
{
    /// <summary>
    /// Column width specification shared between <see cref="DataTableView{T}"/> headers and <see cref="Create"/> rows.
    /// </summary>
    public const string ColumnLayout = "96,115,105,130,*";

    /// <summary>
    /// Creates a reusable 5-column row for an <see cref="AuditLogRecord"/> entry.
    /// </summary>
    public static Grid Create(
        ThemeService theme,
        LocalizationService i18n,
        AuditLogRecord rec)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(rec);

        Grid logRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions(ColumnLayout),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 2)
        };

        TextBlock tsTb = SectionHeader.CreateLabel(
            theme,
            rec.Timestamp.ToLocalTime().ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture),
            11,
            FontWeight.Normal,
            isSecondary: true);
        tsTb.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        logRow.Children.Add(tsTb);

        TextBlock accTb = SectionHeader.CreateLabel(theme, rec.AccountDisplayName, 11, FontWeight.SemiBold);
        accTb.TextTrimming = TextTrimming.CharacterEllipsis;
        accTb.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(accTb, 1);
        logRow.Children.Add(accTb);

        Border catPill = StatusPill.Create(
            theme,
            i18n.FormatAuditCategory(rec.Category),
            rec.Severity);
        Grid.SetColumn(catPill, 2);
        logRow.Children.Add(catPill);

        TextBlock targetTb = SectionHeader.CreateLabel(theme, rec.TargetName, 11, FontWeight.Normal, isSecondary: true);
        targetTb.TextTrimming = TextTrimming.CharacterEllipsis;
        targetTb.TextWrapping = TextWrapping.NoWrap;
        ToolTip.SetTip(targetTb, rec.TargetName);
        Grid.SetColumn(targetTb, 3);
        logRow.Children.Add(targetTb);

        Grid msgCellGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock msgTb = SectionHeader.CreateLabel(theme, rec.Message, 12);
        msgTb.TextWrapping = TextWrapping.NoWrap;
        msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(msgTb, rec.Message);
        Grid.SetColumn(msgTb, 0);
        msgCellGrid.Children.Add(msgTb);

        StackPanel actionBtns = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button copyMsgBtn = AppButton.CreateRowIcon(
            theme,
            VectorIcons.Clipboard,
            i18n["Audit.BtnCopyMessage"],
            async () =>
            {
                if (TopLevel.GetTopLevel(logRow)?.Clipboard is { } clipboard && !string.IsNullOrEmpty(rec.Message))
                {
                    await clipboard.SetTextAsync(rec.Message).ConfigureAwait(true);
                }
            });
        actionBtns.Children.Add(copyMsgBtn);

        Button? openLinkBtn = null;
        if (!string.IsNullOrWhiteSpace(rec.PermalinkUrl))
        {
            string url = rec.PermalinkUrl;
            openLinkBtn = AppButton.CreateRowIcon(
                theme,
                VectorIcons.Browser,
                i18n["Audit.BtnOpenPermalink"],
                () =>
                {
                    try
                    {
                        _ = Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Ignore browser launch errors
                    }
                });
            actionBtns.Children.Add(openLinkBtn);
        }

        Grid.SetColumn(actionBtns, 1);
        msgCellGrid.Children.Add(actionBtns);

        Grid.SetColumn(msgCellGrid, 4);
        logRow.Children.Add(msgCellGrid);

        void OnLangChanged(object? sender, AppLanguage lang)
        {
            StatusPill.SetText(catPill, i18n.FormatAuditCategory(rec.Category));
            ToolTip.SetTip(copyMsgBtn, i18n["Audit.BtnCopyMessage"]);
            if (openLinkBtn is not null)
            {
                ToolTip.SetTip(openLinkBtn, i18n["Audit.BtnOpenPermalink"]);
            }
        }

        logRow.AttachedToVisualTree += (_, _) =>
        {
            i18n.LanguageChanged -= OnLangChanged;
            i18n.LanguageChanged += OnLangChanged;
        };
        logRow.DetachedFromVisualTree += (_, _) => i18n.LanguageChanged -= OnLangChanged;

        return logRow;
    }

    /// <summary>
    /// Creates a compact 3-column table row (<c>64,95,*</c>) for the Dashboard's Recent Activity table.
    /// </summary>
    public static Grid CreateCompact(
        ThemeService theme,
        LocalizationService i18n,
        AuditLogRecord rec)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(rec);

        Grid logRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("64,95,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 2)
        };

        TextBlock tsTb = SectionHeader.CreateLabel(
            theme,
            rec.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            11,
            FontWeight.Normal,
            isSecondary: true);
        tsTb.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        logRow.Children.Add(tsTb);

        Border catPill = StatusPill.Create(
            theme,
            i18n.FormatAuditCategory(rec.Category),
            rec.Severity);
        void OnCompactLangChanged(object? sender, AppLanguage lang) =>
            StatusPill.SetText(catPill, i18n.FormatAuditCategory(rec.Category));
        logRow.AttachedToVisualTree += (_, _) =>
        {
            i18n.LanguageChanged -= OnCompactLangChanged;
            i18n.LanguageChanged += OnCompactLangChanged;
        };
        logRow.DetachedFromVisualTree += (_, _) => i18n.LanguageChanged -= OnCompactLangChanged;
        Grid.SetColumn(catPill, 1);
        logRow.Children.Add(catPill);

        TextBlock msgTb = SectionHeader.CreateLabel(theme, rec.Message, 11);
        msgTb.TextWrapping = TextWrapping.NoWrap;
        msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(msgTb, rec.Message);
        Grid.SetColumn(msgTb, 2);
        logRow.Children.Add(msgTb);

        return logRow;
    }
}
