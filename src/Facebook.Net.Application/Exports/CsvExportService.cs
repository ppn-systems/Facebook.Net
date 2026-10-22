// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.UI.Core.Exports;

/// <summary>
/// Immutable row projection for exporting a scraped sales lead to a UTF-8 with BOM CSV file.
/// </summary>
public sealed record LeadCsvExportRow(
    PostSummary Post,
    IReadOnlyList<string> PhoneNumbers,
    int BuyerIntentScore,
    string StatusText,
    IReadOnlyList<string> TagNames);

/// <summary>
/// Application service contract for formatting and exporting Sales Leads CRM rows and Audit Log records
/// to UTF-8 with BOM CSV files compatible with Microsoft Excel and external CRMs.
/// </summary>
public interface ICsvExportService
{
    /// <summary>Escapes a raw string field according to RFC 4180 CSV rules.</summary>
    string EscapeCsvField(string? value, bool normalizeNewlines = false);

    /// <summary>Writes a collection of <see cref="LeadCsvExportRow"/> items to a UTF-8 with BOM CSV file and returns the exported row count.</summary>
    Task<int> ExportLeadsToCsvAsync(
        string filePath,
        IReadOnlyList<LeadCsvExportRow> rows,
        CancellationToken ct = default);

    /// <summary>Writes a collection of <see cref="AuditLogRecord"/> items to a UTF-8 with BOM CSV file and returns the exported row count.</summary>
    Task<int> ExportAuditLogsToCsvAsync(
        string filePath,
        IReadOnlyList<AuditLogRecord> records,
        Func<AuditCategory, string>? formatCategory = null,
        CancellationToken ct = default);
}

/// <summary>
/// Default stateless implementation of <see cref="ICsvExportService"/>.
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    private static readonly UTF8Encoding s_utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Gets the shared stateless singleton instance of <see cref="CsvExportService"/>.</summary>
    public static CsvExportService Instance { get; } = new();

    /// <inheritdoc />
    public string EscapeCsvField(string? value, bool normalizeNewlines = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        string escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (normalizeNewlines)
        {
            escaped = escaped.Replace('\r', ' ').Replace('\n', ' ');
        }

        return $"\"{escaped}\"";
    }

    /// <inheritdoc />
    public async Task<int> ExportLeadsToCsvAsync(
        string filePath,
        IReadOnlyList<LeadCsvExportRow> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return 0;
        }

        StringBuilder sb = new((rows.Count * 160) + 128);
        _ = sb.AppendLine("CreatedAt,AuthorName,PhoneNumbers,GroupName,IntentScore,Status,Tags,Notes,MessageText,PermalinkUrl");

        for (int i = 0; i < rows.Count; i++)
        {
            LeadCsvExportRow row = rows[i];
            PostSummary p = row.Post;
            string createdStr = p.CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
            string phonesStr = row.PhoneNumbers.Count > 0 ? string.Join(" | ", row.PhoneNumbers) : string.Empty;
            string tagsStr = row.TagNames.Count > 0 ? string.Join(" | ", row.TagNames) : string.Empty;

            _ = sb.Append(this.EscapeCsvField(createdStr, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(p.AuthorName, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(phonesStr, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(p.GroupName, normalizeNewlines: true)).Append(',')
                  .Append(row.BuyerIntentScore.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(this.EscapeCsvField(row.StatusText, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(tagsStr, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(p.Notes, normalizeNewlines: true)).Append(',')
                  .Append(this.EscapeCsvField(p.MessageText, normalizeNewlines: true)).Append(',')
                  .AppendLine(this.EscapeCsvField(p.PermalinkUrl, normalizeNewlines: true));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), s_utf8WithBom, ct).ConfigureAwait(false);
        return rows.Count;
    }

    /// <inheritdoc />
    public async Task<int> ExportAuditLogsToCsvAsync(
        string filePath,
        IReadOnlyList<AuditLogRecord> records,
        Func<AuditCategory, string>? formatCategory = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return 0;
        }

        StringBuilder sb = new((records.Count + 1) * 128);
        _ = sb.AppendLine("Timestamp,Severity,Category,Account,Target,Message,PermalinkUrl");

        for (int i = 0; i < records.Count; i++)
        {
            AuditLogRecord rec = records[i];
            string categoryText = formatCategory is not null
                ? formatCategory(rec.Category)
                : rec.Category.ToString();

            _ = sb.Append(this.EscapeCsvField(rec.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
                  .Append(this.EscapeCsvField(rec.Severity.ToString())).Append(',')
                  .Append(this.EscapeCsvField(categoryText)).Append(',')
                  .Append(this.EscapeCsvField(rec.AccountDisplayName)).Append(',')
                  .Append(this.EscapeCsvField(rec.TargetName)).Append(',')
                  .Append(this.EscapeCsvField(rec.Message)).Append(',')
                  .AppendLine(this.EscapeCsvField(rec.PermalinkUrl ?? string.Empty));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), s_utf8WithBom, ct).ConfigureAwait(false);
        return records.Count;
    }
}
