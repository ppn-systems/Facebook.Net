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

    /// <inheritdoc />
    public async Task UpsertScrapedPostsAsync(IReadOnlyList<PostSummary> posts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(posts);
        if (posts.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO scraped_posts (
                post_id, group_id, group_name, author_uid, author_name,
                message_text, permalink_url, reaction_count, comment_count,
                created_at, scraped_at
            )
            VALUES (
                $post_id, $group_id, $group_name, $author_uid, $author_name,
                $message_text, $permalink_url, $reaction_count, $comment_count,
                $created_at, $scraped_at
            )
            ON CONFLICT(post_id) DO UPDATE SET
                group_id = CASE WHEN excluded.group_id <> '' THEN excluded.group_id ELSE scraped_posts.group_id END,
                group_name = CASE WHEN excluded.group_name <> '' THEN excluded.group_name ELSE scraped_posts.group_name END,
                author_uid = CASE WHEN excluded.author_uid <> '' THEN excluded.author_uid ELSE scraped_posts.author_uid END,
                author_name = COALESCE(excluded.author_name, scraped_posts.author_name),
                message_text = COALESCE(excluded.message_text, scraped_posts.message_text),
                permalink_url = COALESCE(excluded.permalink_url, scraped_posts.permalink_url),
                reaction_count = MAX(scraped_posts.reaction_count, excluded.reaction_count),
                comment_count = MAX(scraped_posts.comment_count, excluded.comment_count),
                created_at = COALESCE(excluded.created_at, scraped_posts.created_at),
                scraped_at = excluded.scraped_at;
            """;

        string nowIso = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using System.Data.Common.DbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        for (int i = 0; i < posts.Count; i++)
        {
            PostSummary p = posts[i];
            if (string.IsNullOrWhiteSpace(p.PostId))
            {
                continue;
            }

            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$post_id", p.PostId);
            _ = cmd.Parameters.AddWithValue("$group_id", p.GroupId ?? string.Empty);
            _ = cmd.Parameters.AddWithValue("$group_name", p.GroupName ?? string.Empty);
            _ = cmd.Parameters.AddWithValue("$author_uid", p.AuthorUid ?? string.Empty);
            _ = cmd.Parameters.AddWithValue("$author_name", (object?)p.AuthorName ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("$message_text", (object?)p.MessageText ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("$permalink_url", (object?)p.PermalinkUrl ?? DBNull.Value);
            _ = cmd.Parameters.AddWithValue("$reaction_count", p.ReactionCount);
            _ = cmd.Parameters.AddWithValue("$comment_count", p.CommentCount);
            _ = cmd.Parameters.AddWithValue(
                "$created_at",
                p.CreatedAt.HasValue
                    ? p.CreatedAt.Value.ToString("O", CultureInfo.InvariantCulture)
                    : DBNull.Value);
            _ = cmd.Parameters.AddWithValue("$scraped_at", nowIso);

            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostSummary>> QueryScrapedPostsAsync(
        string? groupId = null,
        string? searchKeyword = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT post_id, group_id, group_name, author_uid, author_name,
                   message_text, permalink_url, reaction_count, comment_count, created_at
            FROM scraped_posts
            WHERE ($group_id IS NULL OR group_id = $group_id)
              AND ($keyword IS NULL
                   OR message_text LIKE $keyword
                   OR author_name LIKE $keyword
                   OR group_name LIKE $keyword)
            ORDER BY COALESCE(created_at, scraped_at) DESC
            LIMIT $limit;
            """;

        string? normalizedGroup = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        string? normalizedKeyword = string.IsNullOrWhiteSpace(searchKeyword)
            ? null
            : $"%{searchKeyword.Trim()}%";

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", (object?)normalizedGroup ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$keyword", (object?)normalizedKeyword ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 200 : limit);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<PostSummary> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapScrapedPost(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<int> ClearScrapedPostsAsync(string? groupId = null, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM scraped_posts
            WHERE ($group_id IS NULL OR group_id = $group_id);
            """;

        string? normalizedGroup = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", (object?)normalizedGroup ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    private static PostSummary MapScrapedPost(SqliteDataReader reader)
    {
        DateTimeOffset? createdAt = null;
        if (!reader.IsDBNull(9))
        {
            string rawCreated = reader.GetString(9);
            if (DateTimeOffset.TryParse(rawCreated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                createdAt = parsed;
            }
        }

        string groupId = reader.GetString(1);
        string groupName = reader.GetString(2);

        return new PostSummary(
            PostId: reader.GetString(0),
            AuthorUid: reader.GetString(3),
            AuthorName: reader.IsDBNull(4) ? null : reader.GetString(4),
            MessageText: reader.IsDBNull(5) ? null : reader.GetString(5),
            PermalinkUrl: reader.IsDBNull(6) ? null : reader.GetString(6),
            ReactionCount: reader.GetInt32(7),
            CommentCount: reader.GetInt32(8),
            CreatedAt: createdAt,
            GroupId: string.IsNullOrWhiteSpace(groupId) ? null : groupId,
            GroupName: string.IsNullOrWhiteSpace(groupName) ? null : groupName);
    }
}
