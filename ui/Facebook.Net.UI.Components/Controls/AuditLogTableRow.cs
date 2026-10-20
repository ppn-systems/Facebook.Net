// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    public const string ColumnLayout = "66,115,105,130,*";

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
            rec.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
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
        i18n.LanguageChanged += (_, _) =>
            StatusPill.SetText(catPill, i18n.FormatAuditCategory(rec.Category));
        Grid.SetColumn(catPill, 2);
        logRow.Children.Add(catPill);

        TextBlock targetTb = SectionHeader.CreateLabel(theme, rec.TargetName, 11, FontWeight.Normal, isSecondary: true);
        targetTb.TextTrimming = TextTrimming.CharacterEllipsis;
        targetTb.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(targetTb, 3);
        logRow.Children.Add(targetTb);

        TextBlock msgTb = SectionHeader.CreateLabel(theme, rec.Message, 12);
        msgTb.TextWrapping = TextWrapping.NoWrap;
        msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(msgTb, 4);
        logRow.Children.Add(msgTb);

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
        i18n.LanguageChanged += (_, _) =>
            StatusPill.SetText(catPill, i18n.FormatAuditCategory(rec.Category));
        Grid.SetColumn(catPill, 1);
        logRow.Children.Add(catPill);

        TextBlock msgTb = SectionHeader.CreateLabel(theme, rec.Message, 11);
        msgTb.TextWrapping = TextWrapping.NoWrap;
        msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(msgTb, 2);
        logRow.Children.Add(msgTb);

        return logRow;
    }
}
