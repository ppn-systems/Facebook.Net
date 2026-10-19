// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Microsoft.Data.Sqlite;

namespace Facebook.Net.Storage;

/// <summary>
/// Partial implementation of <see cref="SqliteAccountStore"/> containing Target Group, Campaign Template,
/// and duplicate-prevention Action History operations.
/// </summary>
public sealed partial class SqliteAccountStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedTargetGroup>> GetAllTargetGroupsAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT group_id, group_name, is_enabled, notes FROM target_groups ORDER BY group_name ASC;";

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<SavedTargetGroup> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapTargetGroup(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpsertTargetGroupAsync(SavedTargetGroup group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        const string sql = """
            INSERT INTO target_groups (group_id, group_name, is_enabled, notes)
            VALUES ($group_id, $group_name, $is_enabled, $notes)
            ON CONFLICT(group_id) DO UPDATE SET
                group_name = excluded.group_name,
                is_enabled = excluded.is_enabled,
                notes = excluded.notes;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", group.GroupId);
        _ = cmd.Parameters.AddWithValue("$group_name", group.GroupName);
        _ = cmd.Parameters.AddWithValue("$is_enabled", group.IsEnabled ? 1 : 0);
        _ = cmd.Parameters.AddWithValue("$notes", (object?)group.Notes ?? DBNull.Value);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTargetGroupAsync(string groupId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        const string sql = "DELETE FROM target_groups WHERE group_id = $group_id;";
        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", groupId);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CampaignTemplate>> GetAllCampaignTemplatesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, spintax_content, media_file_path, is_comment_template
            FROM campaign_templates
            ORDER BY title ASC;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<CampaignTemplate> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapCampaignTemplate(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpsertCampaignTemplateAsync(CampaignTemplate campaignTemplate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(campaignTemplate);

        const string sql = """
            INSERT INTO campaign_templates (id, title, spintax_content, media_file_path, is_comment_template)
            VALUES ($id, $title, $spintax_content, $media_file_path, $is_comment_template)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                spintax_content = excluded.spintax_content,
                media_file_path = excluded.media_file_path,
                is_comment_template = excluded.is_comment_template;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", campaignTemplate.Id);
        _ = cmd.Parameters.AddWithValue("$title", campaignTemplate.Title);
        _ = cmd.Parameters.AddWithValue("$spintax_content", campaignTemplate.SpintaxContent);
        _ = cmd.Parameters.AddWithValue("$media_file_path", (object?)campaignTemplate.MediaFilePath ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$is_comment_template", campaignTemplate.IsCommentTemplate ? 1 : 0);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCampaignTemplateAsync(string templateId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        const string sql = "DELETE FROM campaign_templates WHERE id = $id;";
        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", templateId);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> HasExecutedActionAsync(string accountUid, string actionKind, string targetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        const string sql = """
            SELECT 1
            FROM action_history
            WHERE account_uid = $account_uid AND action_kind = $action_kind AND target_id = $target_id
            LIMIT 1;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$account_uid", accountUid);
        _ = cmd.Parameters.AddWithValue("$action_kind", actionKind);
        _ = cmd.Parameters.AddWithValue("$target_id", targetId);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordActionAsync(ActionHistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        const string sql = """
            INSERT INTO action_history (account_uid, action_kind, target_id, result_id, executed_at)
            VALUES ($account_uid, $action_kind, $target_id, $result_id, $executed_at)
            ON CONFLICT(account_uid, action_kind, target_id) DO UPDATE SET
                result_id = excluded.result_id,
                executed_at = excluded.executed_at;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$account_uid", entry.AccountUid);
        _ = cmd.Parameters.AddWithValue("$action_kind", entry.ActionKind);
        _ = cmd.Parameters.AddWithValue("$target_id", entry.TargetId);
        _ = cmd.Parameters.AddWithValue("$result_id", (object?)entry.ResultId ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$executed_at", entry.ExecutedAt.ToString("O", CultureInfo.InvariantCulture));

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionHistoryEntry>> GetRecentActionsAsync(int limit = 100, CancellationToken ct = default)
    {
        const string sql = """
            SELECT account_uid, action_kind, target_id, result_id, executed_at
            FROM action_history
            ORDER BY executed_at DESC
            LIMIT $limit;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 100 : limit);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<ActionHistoryEntry> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapActionHistory(reader));
        }

        return list;
    }

    private static SavedTargetGroup MapTargetGroup(SqliteDataReader reader) =>
        new(
            GroupId: reader.GetString(0),
            GroupName: reader.GetString(1),
            IsEnabled: reader.GetInt32(2) != 0,
            Notes: reader.IsDBNull(3) ? null : reader.GetString(3));

    private static CampaignTemplate MapCampaignTemplate(SqliteDataReader reader) =>
        new(
            Id: reader.GetString(0),
            Title: reader.GetString(1),
            SpintaxContent: reader.GetString(2),
            MediaFilePath: reader.IsDBNull(3) ? null : reader.GetString(3),
            IsCommentTemplate: reader.GetInt32(4) != 0);

    private static ActionHistoryEntry MapActionHistory(SqliteDataReader reader)
    {
        string rawTime = reader.GetString(4);
        DateTimeOffset executedAt = DateTimeOffset.TryParse(rawTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;

        return new ActionHistoryEntry(
            AccountUid: reader.GetString(0),
            ActionKind: reader.GetString(1),
            TargetId: reader.GetString(2),
            ResultId: reader.IsDBNull(3) ? null : reader.GetString(3),
            ExecutedAt: executedAt);
    }
}
