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
        const string sql = "SELECT group_id, group_name, is_enabled, notes, avatar_url, tags_csv FROM target_groups ORDER BY group_name ASC;";

        using SqliteConnection conn = new(_connectionString);
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
            INSERT INTO target_groups (group_id, group_name, is_enabled, notes, avatar_url, tags_csv)
            VALUES ($group_id, $group_name, $is_enabled, $notes, $avatar_url, $tags_csv)
            ON CONFLICT(group_id) DO UPDATE SET
                group_name = excluded.group_name,
                is_enabled = excluded.is_enabled,
                notes = excluded.notes,
                avatar_url = COALESCE(excluded.avatar_url, target_groups.avatar_url),
                tags_csv = COALESCE(excluded.tags_csv, target_groups.tags_csv);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", group.GroupId);
        _ = cmd.Parameters.AddWithValue("$group_name", group.GroupName);
        _ = cmd.Parameters.AddWithValue("$is_enabled", group.IsEnabled ? 1 : 0);
        _ = cmd.Parameters.AddWithValue("$notes", (object?)group.Notes ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$avatar_url", (object?)group.AvatarUrl ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$tags_csv", (object?)group.TagsCsv ?? DBNull.Value);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertTargetGroupsBatchAsync(IReadOnlyList<SavedTargetGroup> groups, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO target_groups (group_id, group_name, is_enabled, notes, avatar_url, tags_csv)
            VALUES ($group_id, $group_name, $is_enabled, $notes, $avatar_url, $tags_csv)
            ON CONFLICT(group_id) DO UPDATE SET
                group_name = excluded.group_name,
                is_enabled = excluded.is_enabled,
                notes = excluded.notes,
                avatar_url = COALESCE(excluded.avatar_url, target_groups.avatar_url),
                tags_csv = COALESCE(excluded.tags_csv, target_groups.tags_csv);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using System.Data.Common.DbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;

        SqliteParameter pGroupId = cmd.Parameters.Add("$group_id", SqliteType.Text);
        SqliteParameter pGroupName = cmd.Parameters.Add("$group_name", SqliteType.Text);
        SqliteParameter pIsEnabled = cmd.Parameters.Add("$is_enabled", SqliteType.Integer);
        SqliteParameter pNotes = cmd.Parameters.Add("$notes", SqliteType.Text);
        SqliteParameter pAvatarUrl = cmd.Parameters.Add("$avatar_url", SqliteType.Text);
        SqliteParameter pTagsCsv = cmd.Parameters.Add("$tags_csv", SqliteType.Text);

        for (int i = 0; i < groups.Count; i++)
        {
            SavedTargetGroup group = groups[i];
            if (group is null || string.IsNullOrWhiteSpace(group.GroupId))
            {
                continue;
            }

            pGroupId.Value = group.GroupId;
            pGroupName.Value = group.GroupName;
            pIsEnabled.Value = group.IsEnabled ? 1 : 0;
            pNotes.Value = (object?)group.Notes ?? DBNull.Value;
            pAvatarUrl.Value = (object?)group.AvatarUrl ?? DBNull.Value;
            pTagsCsv.Value = (object?)group.TagsCsv ?? DBNull.Value;

            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTargetGroupAsync(string groupId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        const string sql = "DELETE FROM target_groups WHERE group_id = $group_id;";
        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
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
        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
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
                created_at, scraped_at, media_preview_url, is_video, author_avatar_url, group_avatar_url
            )
            VALUES (
                $post_id, $group_id, $group_name, $author_uid, $author_name,
                $message_text, $permalink_url, $reaction_count, $comment_count,
                $created_at, $scraped_at, $media_preview_url, $is_video, $author_avatar_url, $group_avatar_url
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
                scraped_at = excluded.scraped_at,
                media_preview_url = COALESCE(excluded.media_preview_url, scraped_posts.media_preview_url),
                is_video = CASE WHEN excluded.is_video <> 0 THEN 1 ELSE scraped_posts.is_video END,
                author_avatar_url = COALESCE(excluded.author_avatar_url, scraped_posts.author_avatar_url),
                group_avatar_url = COALESCE(excluded.group_avatar_url, scraped_posts.group_avatar_url);
            """;

        string nowIso = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using System.Data.Common.DbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;

        SqliteParameter pPostId = cmd.Parameters.Add("$post_id", SqliteType.Text);
        SqliteParameter pGroupId = cmd.Parameters.Add("$group_id", SqliteType.Text);
        SqliteParameter pGroupName = cmd.Parameters.Add("$group_name", SqliteType.Text);
        SqliteParameter pAuthorUid = cmd.Parameters.Add("$author_uid", SqliteType.Text);
        SqliteParameter pAuthorName = cmd.Parameters.Add("$author_name", SqliteType.Text);
        SqliteParameter pMessageText = cmd.Parameters.Add("$message_text", SqliteType.Text);
        SqliteParameter pPermalinkUrl = cmd.Parameters.Add("$permalink_url", SqliteType.Text);
        SqliteParameter pReactionCount = cmd.Parameters.Add("$reaction_count", SqliteType.Integer);
        SqliteParameter pCommentCount = cmd.Parameters.Add("$comment_count", SqliteType.Integer);
        SqliteParameter pCreatedAt = cmd.Parameters.Add("$created_at", SqliteType.Text);
        SqliteParameter pScrapedAt = cmd.Parameters.Add("$scraped_at", SqliteType.Text);
        SqliteParameter pMediaPreviewUrl = cmd.Parameters.Add("$media_preview_url", SqliteType.Text);
        SqliteParameter pIsVideo = cmd.Parameters.Add("$is_video", SqliteType.Integer);
        SqliteParameter pAuthorAvatarUrl = cmd.Parameters.Add("$author_avatar_url", SqliteType.Text);
        SqliteParameter pGroupAvatarUrl = cmd.Parameters.Add("$group_avatar_url", SqliteType.Text);
        pScrapedAt.Value = nowIso;

        for (int i = 0; i < posts.Count; i++)
        {
            PostSummary p = posts[i];
            if (p is null || string.IsNullOrWhiteSpace(p.PostId))
            {
                continue;
            }

            pPostId.Value = p.PostId;
            pGroupId.Value = p.GroupId ?? string.Empty;
            pGroupName.Value = p.GroupName ?? string.Empty;
            pAuthorUid.Value = p.AuthorUid ?? string.Empty;
            pAuthorName.Value = (object?)p.AuthorName ?? DBNull.Value;
            pMessageText.Value = (object?)p.MessageText ?? DBNull.Value;
            pPermalinkUrl.Value = (object?)p.PermalinkUrl ?? DBNull.Value;
            pReactionCount.Value = p.ReactionCount;
            pCommentCount.Value = p.CommentCount;
            pCreatedAt.Value = p.CreatedAt.HasValue
                ? p.CreatedAt.Value.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value;
            pMediaPreviewUrl.Value = (object?)p.MediaPreviewUrl ?? DBNull.Value;
            pIsVideo.Value = p.IsVideo ? 1 : 0;
            pAuthorAvatarUrl.Value = (object?)p.AuthorAvatarUrl ?? DBNull.Value;
            pGroupAvatarUrl.Value = (object?)p.GroupAvatarUrl ?? DBNull.Value;

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
            SELECT sp.post_id,
                   sp.group_id,
                   COALESCE(NULLIF(sp.group_name, ''), tg.group_name, '') AS resolved_group_name,
                   sp.author_uid,
                   sp.author_name,
                   sp.message_text,
                   sp.permalink_url,
                   sp.reaction_count,
                   sp.comment_count,
                   sp.created_at,
                   sp.media_preview_url,
                   sp.is_video,
                   sp.author_avatar_url,
                   COALESCE(NULLIF(sp.group_avatar_url, ''), tg.avatar_url) AS resolved_group_avatar_url
            FROM scraped_posts sp
            LEFT JOIN target_groups tg ON tg.group_id = sp.group_id
            WHERE ($group_id IS NULL OR sp.group_id = $group_id)
              AND ($keyword IS NULL
                   OR sp.message_text LIKE $keyword
                   OR sp.author_name LIKE $keyword
                   OR sp.group_name LIKE $keyword
                   OR tg.group_name LIKE $keyword)
            ORDER BY COALESCE(sp.created_at, sp.scraped_at) DESC
            LIMIT $limit;
            """;

        string? normalizedGroup = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        string? normalizedKeyword = string.IsNullOrWhiteSpace(searchKeyword)
            ? null
            : $"%{searchKeyword.Trim()}%";

        using SqliteConnection conn = new(_connectionString);
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

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$group_id", (object?)normalizedGroup ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupTag>> GetAllGroupTagsAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, color_hex FROM group_tags ORDER BY name ASC;";

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<GroupTag> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new GroupTag(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                ColorHex: reader.GetString(2)));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpsertGroupTagAsync(GroupTag tag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        const string sql = """
            INSERT INTO group_tags (id, name, color_hex)
            VALUES ($id, $name, $color_hex)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                color_hex = excluded.color_hex;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", tag.Id);
        _ = cmd.Parameters.AddWithValue("$name", tag.Name);
        _ = cmd.Parameters.AddWithValue("$color_hex", tag.ColorHex);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteGroupTagAsync(string tagId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

        const string sql = "DELETE FROM group_tags WHERE id = $id;";
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", tagId);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PublishedPostRecord>> GetPublishedPostsAsync(string? accountUid = null, int limit = 200, CancellationToken ct = default)
    {
        const string sql = """
            SELECT pp.post_id,
                   pp.account_uid,
                   COALESCE(NULLIF(pp.account_name, ''), a.display_name, pp.account_uid),
                   COALESCE(NULLIF(pp.account_avatar_url, ''), a.avatar_url),
                   pp.target_kind,
                   pp.target_group_id,
                   COALESCE(NULLIF(pp.target_group_name, ''), tg.group_name),
                   COALESCE(NULLIF(pp.target_group_avatar_url, ''), tg.avatar_url),
                   pp.message_text,
                   pp.media_file_path,
                   pp.media_preview_url,
                   pp.permalink_url,
                   pp.published_at,
                   pp.reaction_count,
                   pp.comment_count
            FROM published_posts pp
            LEFT JOIN accounts a ON a.uid = pp.account_uid
            LEFT JOIN target_groups tg ON tg.group_id = pp.target_group_id
            WHERE ($account_uid IS NULL OR pp.account_uid = $account_uid)
            ORDER BY pp.published_at DESC
            LIMIT $limit;
            """;

        string? normalizedUid = string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim();

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$account_uid", (object?)normalizedUid ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 200 : limit);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<PublishedPostRecord> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapPublishedPost(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpsertPublishedPostAsync(PublishedPostRecord post, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        const string sql = """
            INSERT INTO published_posts (
                post_id, account_uid, account_name, account_avatar_url,
                target_kind, target_group_id, target_group_name, target_group_avatar_url,
                message_text, media_file_path, media_preview_url, permalink_url,
                published_at, reaction_count, comment_count
            )
            VALUES (
                $post_id, $account_uid, $account_name, $account_avatar_url,
                $target_kind, $target_group_id, $target_group_name, $target_group_avatar_url,
                $message_text, $media_file_path, $media_preview_url, $permalink_url,
                $published_at, $reaction_count, $comment_count
            )
            ON CONFLICT(post_id) DO UPDATE SET
                account_name = CASE WHEN excluded.account_name <> '' THEN excluded.account_name ELSE published_posts.account_name END,
                account_avatar_url = COALESCE(excluded.account_avatar_url, published_posts.account_avatar_url),
                target_kind = excluded.target_kind,
                target_group_id = COALESCE(excluded.target_group_id, published_posts.target_group_id),
                target_group_name = COALESCE(excluded.target_group_name, published_posts.target_group_name),
                target_group_avatar_url = COALESCE(excluded.target_group_avatar_url, published_posts.target_group_avatar_url),
                message_text = CASE WHEN excluded.message_text <> '' THEN excluded.message_text ELSE published_posts.message_text END,
                media_file_path = COALESCE(excluded.media_file_path, published_posts.media_file_path),
                media_preview_url = COALESCE(excluded.media_preview_url, published_posts.media_preview_url),
                permalink_url = COALESCE(excluded.permalink_url, published_posts.permalink_url),
                reaction_count = MAX(published_posts.reaction_count, excluded.reaction_count),
                comment_count = MAX(published_posts.comment_count, excluded.comment_count);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$post_id", post.PostId);
        _ = cmd.Parameters.AddWithValue("$account_uid", post.AccountUid);
        _ = cmd.Parameters.AddWithValue("$account_name", post.AuthorName ?? string.Empty);
        _ = cmd.Parameters.AddWithValue("$account_avatar_url", (object?)post.AuthorAvatarUrl ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$target_kind", post.IsPersonalProfilePost ? "Feed" : "Group");
        _ = cmd.Parameters.AddWithValue("$target_group_id", (object?)post.TargetGroupId ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$target_group_name", (object?)post.TargetGroupName ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$target_group_avatar_url", (object?)post.TargetGroupAvatarUrl ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$message_text", post.MessageText);
        _ = cmd.Parameters.AddWithValue("$media_file_path", (object?)post.LocalMediaFilePath ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$media_preview_url", (object?)post.MediaPreviewUrl ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$permalink_url", (object?)post.PermalinkUrl ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$published_at", post.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        _ = cmd.Parameters.AddWithValue("$reaction_count", post.ReactionCount);
        _ = cmd.Parameters.AddWithValue("$comment_count", post.CommentCount);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertPublishedPostsBatchAsync(IReadOnlyList<PublishedPostRecord> posts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(posts);
        if (posts.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO published_posts (
                post_id, account_uid, account_name, account_avatar_url,
                target_kind, target_group_id, target_group_name, target_group_avatar_url,
                message_text, media_file_path, media_preview_url, permalink_url,
                published_at, reaction_count, comment_count
            )
            VALUES (
                $post_id, $account_uid, $account_name, $account_avatar_url,
                $target_kind, $target_group_id, $target_group_name, $target_group_avatar_url,
                $message_text, $media_file_path, $media_preview_url, $permalink_url,
                $published_at, $reaction_count, $comment_count
            )
            ON CONFLICT(post_id) DO UPDATE SET
                account_name = CASE WHEN excluded.account_name <> '' THEN excluded.account_name ELSE published_posts.account_name END,
                account_avatar_url = COALESCE(excluded.account_avatar_url, published_posts.account_avatar_url),
                target_kind = excluded.target_kind,
                target_group_id = COALESCE(excluded.target_group_id, published_posts.target_group_id),
                target_group_name = COALESCE(excluded.target_group_name, published_posts.target_group_name),
                target_group_avatar_url = COALESCE(excluded.target_group_avatar_url, published_posts.target_group_avatar_url),
                message_text = CASE WHEN excluded.message_text <> '' THEN excluded.message_text ELSE published_posts.message_text END,
                media_file_path = COALESCE(excluded.media_file_path, published_posts.media_file_path),
                media_preview_url = COALESCE(excluded.media_preview_url, published_posts.media_preview_url),
                permalink_url = COALESCE(excluded.permalink_url, published_posts.permalink_url),
                reaction_count = MAX(published_posts.reaction_count, excluded.reaction_count),
                comment_count = MAX(published_posts.comment_count, excluded.comment_count);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using System.Data.Common.DbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;

        SqliteParameter pPostId = cmd.Parameters.Add("$post_id", SqliteType.Text);
        SqliteParameter pAccountUid = cmd.Parameters.Add("$account_uid", SqliteType.Text);
        SqliteParameter pAccountName = cmd.Parameters.Add("$account_name", SqliteType.Text);
        SqliteParameter pAccountAvatarUrl = cmd.Parameters.Add("$account_avatar_url", SqliteType.Text);
        SqliteParameter pTargetKind = cmd.Parameters.Add("$target_kind", SqliteType.Text);
        SqliteParameter pTargetGroupId = cmd.Parameters.Add("$target_group_id", SqliteType.Text);
        SqliteParameter pTargetGroupName = cmd.Parameters.Add("$target_group_name", SqliteType.Text);
        SqliteParameter pTargetGroupAvatarUrl = cmd.Parameters.Add("$target_group_avatar_url", SqliteType.Text);
        SqliteParameter pMessageText = cmd.Parameters.Add("$message_text", SqliteType.Text);
        SqliteParameter pMediaFilePath = cmd.Parameters.Add("$media_file_path", SqliteType.Text);
        SqliteParameter pMediaPreviewUrl = cmd.Parameters.Add("$media_preview_url", SqliteType.Text);
        SqliteParameter pPermalinkUrl = cmd.Parameters.Add("$permalink_url", SqliteType.Text);
        SqliteParameter pPublishedAt = cmd.Parameters.Add("$published_at", SqliteType.Text);
        SqliteParameter pReactionCount = cmd.Parameters.Add("$reaction_count", SqliteType.Integer);
        SqliteParameter pCommentCount = cmd.Parameters.Add("$comment_count", SqliteType.Integer);

        for (int i = 0; i < posts.Count; i++)
        {
            PublishedPostRecord post = posts[i];
            if (post is null || string.IsNullOrWhiteSpace(post.PostId))
            {
                continue;
            }

            pPostId.Value = post.PostId;
            pAccountUid.Value = post.AccountUid;
            pAccountName.Value = post.AuthorName ?? string.Empty;
            pAccountAvatarUrl.Value = (object?)post.AuthorAvatarUrl ?? DBNull.Value;
            pTargetKind.Value = post.IsPersonalProfilePost ? "Feed" : "Group";
            pTargetGroupId.Value = (object?)post.TargetGroupId ?? DBNull.Value;
            pTargetGroupName.Value = (object?)post.TargetGroupName ?? DBNull.Value;
            pTargetGroupAvatarUrl.Value = (object?)post.TargetGroupAvatarUrl ?? DBNull.Value;
            pMessageText.Value = post.MessageText;
            pMediaFilePath.Value = (object?)post.LocalMediaFilePath ?? DBNull.Value;
            pMediaPreviewUrl.Value = (object?)post.MediaPreviewUrl ?? DBNull.Value;
            pPermalinkUrl.Value = (object?)post.PermalinkUrl ?? DBNull.Value;
            pPublishedAt.Value = post.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            pReactionCount.Value = post.ReactionCount;
            pCommentCount.Value = post.CommentCount;

            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeletePublishedPostAsync(string postId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        const string sql = "DELETE FROM published_posts WHERE post_id = $post_id;";
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$post_id", postId);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static SavedTargetGroup MapTargetGroup(SqliteDataReader reader) =>
        new(
            GroupId: reader.GetString(0),
            GroupName: reader.GetString(1),
            IsEnabled: reader.GetInt32(2) != 0,
            Notes: reader.IsDBNull(3) ? null : reader.GetString(3),
            AvatarUrl: reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null,
            TagsCsv: reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null);

    private static PublishedPostRecord MapPublishedPost(SqliteDataReader reader)
    {
        string rawTime = reader.GetString(12);
        DateTimeOffset publishedAt = DateTimeOffset.TryParse(rawTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        string targetKind = reader.GetString(4);
        string? targetGroupId = reader.IsDBNull(5) ? null : reader.GetString(5);

        return new PublishedPostRecord(
            PostId: reader.GetString(0),
            AccountUid: reader.GetString(1),
            AuthorName: reader.GetString(2),
            AuthorAvatarUrl: reader.IsDBNull(3) ? null : reader.GetString(3),
            MessageText: reader.GetString(8),
            MediaPreviewUrl: reader.IsDBNull(10) ? null : reader.GetString(10),
            LocalMediaFilePath: reader.IsDBNull(9) ? null : reader.GetString(9),
            TargetGroupId: targetGroupId,
            TargetGroupName: reader.IsDBNull(6) ? null : reader.GetString(6),
            TargetGroupAvatarUrl: reader.IsDBNull(7) ? null : reader.GetString(7),
            PermalinkUrl: reader.IsDBNull(11) ? null : reader.GetString(11),
            ReactionCount: reader.GetInt32(13),
            CommentCount: reader.GetInt32(14),
            CreatedAt: publishedAt,
            IsPersonalProfilePost: string.IsNullOrWhiteSpace(targetGroupId) && !string.Equals(targetKind, "Group", StringComparison.OrdinalIgnoreCase));
    }

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
            GroupName: string.IsNullOrWhiteSpace(groupName) ? null : groupName,
            MediaPreviewUrl: reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null,
            IsVideo: reader.FieldCount > 11 && !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
            AuthorAvatarUrl: reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null,
            GroupAvatarUrl: reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : null);
    }
}
