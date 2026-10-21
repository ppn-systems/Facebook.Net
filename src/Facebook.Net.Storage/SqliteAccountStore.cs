// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Microsoft.Data.Sqlite;

namespace Facebook.Net.Storage;

/// <summary>
/// AOT-compatible SQLite implementation of <see cref="IAccountStore"/> using <see cref="Microsoft.Data.Sqlite"/>.
/// Uses WAL journaling mode and zero-reflection reader mapping.
/// </summary>
public sealed partial class SqliteAccountStore : IAccountStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAccountStore"/> class.
    /// </summary>
    /// <param name="databaseFilePath">The path to the SQLite <c>.db</c> file.</param>
    public SqliteAccountStore(string databaseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 10
        };
        _connectionString = builder.ToString();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        const string sql = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 67108864;
            PRAGMA cache_size = -8000;

            CREATE TABLE IF NOT EXISTS proxies (
                id TEXT PRIMARY KEY NOT NULL,
                host TEXT NOT NULL,
                port INTEGER NOT NULL,
                protocol INTEGER NOT NULL DEFAULT 0,
                username TEXT NULL,
                password TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS accounts (
                uid TEXT PRIMARY KEY NOT NULL,
                display_name TEXT NOT NULL,
                cookie_header TEXT NOT NULL,
                user_agent TEXT NOT NULL,
                profile_directory TEXT NOT NULL,
                proxy_id TEXT NULL,
                two_factor_secret TEXT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                last_active_at TEXT NULL,
                avatar_url TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS target_groups (
                group_id TEXT PRIMARY KEY NOT NULL,
                group_name TEXT NOT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                notes TEXT NULL,
                avatar_url TEXT NULL,
                tags_csv TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS group_tags (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS lead_tags (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS published_posts (
                post_id TEXT PRIMARY KEY NOT NULL,
                account_uid TEXT NOT NULL,
                account_name TEXT NOT NULL,
                account_avatar_url TEXT NULL,
                target_kind TEXT NOT NULL,
                target_group_id TEXT NULL,
                target_group_name TEXT NULL,
                target_group_avatar_url TEXT NULL,
                message_text TEXT NOT NULL,
                media_file_path TEXT NULL,
                media_preview_url TEXT NULL,
                permalink_url TEXT NULL,
                published_at TEXT NOT NULL,
                reaction_count INTEGER NOT NULL DEFAULT 0,
                comment_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_published_posts_account_uid ON published_posts(account_uid);
            CREATE INDEX IF NOT EXISTS idx_published_posts_published_at ON published_posts(published_at DESC);

            CREATE TABLE IF NOT EXISTS campaign_templates (
                id TEXT PRIMARY KEY NOT NULL,
                title TEXT NOT NULL,
                spintax_content TEXT NOT NULL,
                media_file_path TEXT NULL,
                is_comment_template INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS action_history (
                account_uid TEXT NOT NULL,
                action_kind TEXT NOT NULL,
                target_id TEXT NOT NULL,
                result_id TEXT NULL,
                executed_at TEXT NOT NULL,
                PRIMARY KEY (account_uid, action_kind, target_id)
            );

            CREATE TABLE IF NOT EXISTS scraped_posts (
                post_id TEXT PRIMARY KEY NOT NULL,
                group_id TEXT NOT NULL,
                group_name TEXT NOT NULL,
                author_uid TEXT NOT NULL,
                author_name TEXT NULL,
                message_text TEXT NULL,
                permalink_url TEXT NULL,
                reaction_count INTEGER NOT NULL DEFAULT 0,
                comment_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NULL,
                scraped_at TEXT NOT NULL,
                media_preview_url TEXT NULL,
                is_video INTEGER NOT NULL DEFAULT 0,
                author_avatar_url TEXT NULL,
                group_avatar_url TEXT NULL,
                tags_csv TEXT NULL,
                notes TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_scraped_posts_group_id ON scraped_posts(group_id);
            CREATE INDEX IF NOT EXISTS idx_scraped_posts_scraped_at ON scraped_posts(scraped_at DESC);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await EnsureColumnExistsAsync(conn, "accounts", "avatar_url", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "target_groups", "avatar_url", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "target_groups", "tags_csv", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "media_preview_url", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "is_video", "INTEGER NOT NULL DEFAULT 0", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "author_avatar_url", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "group_avatar_url", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "tags_csv", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(conn, "scraped_posts", "notes", "TEXT NULL", ct).ConfigureAwait(false);

        using SqliteCommand seedTagsCmd = conn.CreateCommand();
        seedTagsCmd.CommandText = """
            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_market', 'Mua bán & Chợ', '#F6821F'
            WHERE NOT EXISTS (SELECT 1 FROM group_tags);

            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_bds', 'Bất động sản', '#3B82F6'
            WHERE (SELECT COUNT(1) FROM group_tags) < 2;

            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_job', 'Việc làm & Tuyển dụng', '#10B981'
            WHERE (SELECT COUNT(1) FROM group_tags) < 3;

            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_community', 'Cộng đồng & Giao lưu', '#8B5CF6'
            WHERE (SELECT COUNT(1) FROM group_tags) < 4;

            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_vip', 'Nhóm Ưu tiên VIP', '#F43F5E'
            WHERE (SELECT COUNT(1) FROM group_tags) < 5;

            INSERT OR IGNORE INTO group_tags (id, name, color_hex)
            SELECT 'tag_tech', 'Công nghệ & Lập trình', '#06B6D4'
            WHERE (SELECT COUNT(1) FROM group_tags) < 6;

            INSERT OR IGNORE INTO lead_tags (id, name, color_hex)
            SELECT 'lead_vip', 'Khách VIP / Nóng', '#F43F5E'
            WHERE NOT EXISTS (SELECT 1 FROM lead_tags);

            INSERT OR IGNORE INTO lead_tags (id, name, color_hex)
            SELECT 'lead_phone', 'Có SĐT — Gọi ngay', '#10B981'
            WHERE (SELECT COUNT(1) FROM lead_tags) < 2;

            INSERT OR IGNORE INTO lead_tags (id, name, color_hex)
            SELECT 'lead_followup', 'Hẹn tư vấn lại', '#F6821F'
            WHERE (SELECT COUNT(1) FROM lead_tags) < 3;

            INSERT OR IGNORE INTO lead_tags (id, name, color_hex)
            SELECT 'lead_quoted', 'Đã báo giá', '#3B82F6'
            WHERE (SELECT COUNT(1) FROM lead_tags) < 4;

            INSERT OR IGNORE INTO lead_tags (id, name, color_hex)
            SELECT 'lead_seeding', 'Môi giới / Seeding', '#8B5CF6'
            WHERE (SELECT COUNT(1) FROM lead_tags) < 5;
            """;
        _ = await seedTagsCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        using SqliteCommand cleanupCmd = conn.CreateCommand();
        cleanupCmd.CommandText = """
            DELETE FROM scraped_posts
            WHERE (author_name IS NULL OR TRIM(author_name) = '')
              AND (message_text IS NULL OR TRIM(message_text) = '')
              AND (media_preview_url IS NULL OR TRIM(media_preview_url) = '');
            """;
        _ = await cleanupCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await NormalizeAndCleanGroupTagsAsync(conn, ct).ConfigureAwait(false);
    }

    private static async Task NormalizeAndCleanGroupTagsAsync(SqliteConnection conn, CancellationToken ct)
    {
        // Normalize legacy tag IDs (tag_jobs -> tag_job, tag_sales -> tag_market)
        using SqliteCommand normCmd = conn.CreateCommand();
        normCmd.CommandText = """
            UPDATE target_groups
            SET tags_csv = REPLACE(REPLACE(tags_csv, 'tag_jobs', 'tag_job'), 'tag_sales', 'tag_market')
            WHERE tags_csv LIKE '%tag_jobs%' OR tags_csv LIKE '%tag_sales%';
            """;
        _ = await normCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection conn,
        string tableName,
        string columnName,
        string columnTypeDef,
        CancellationToken ct)
    {
        try
        {
            using SqliteCommand alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnTypeDef};";
            _ = await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // Column already exists in existing database.
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountProfile>> GetAllAccountsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT uid, display_name, cookie_header, user_agent, profile_directory,
                   proxy_id, two_factor_secret, status, last_active_at, avatar_url
            FROM accounts
            ORDER BY uid ASC;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<AccountProfile> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapAccount(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<AccountProfile?> GetAccountByUidAsync(string uid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        const string sql = """
            SELECT uid, display_name, cookie_header, user_agent, profile_directory,
                   proxy_id, two_factor_secret, status, last_active_at, avatar_url
            FROM accounts
            WHERE uid = $uid
            LIMIT 1;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$uid", uid);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapAccount(reader) : null;
    }

    /// <inheritdoc />
    public async Task UpsertAccountAsync(AccountProfile account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        const string sql = """
            INSERT INTO accounts (
                uid, display_name, cookie_header, user_agent, profile_directory,
                proxy_id, two_factor_secret, status, last_active_at, avatar_url
            )
            VALUES (
                $uid, $display_name, $cookie_header, $user_agent, $profile_directory,
                $proxy_id, $two_factor_secret, $status, $last_active_at, $avatar_url
            )
            ON CONFLICT(uid) DO UPDATE SET
                display_name = excluded.display_name,
                cookie_header = excluded.cookie_header,
                user_agent = excluded.user_agent,
                profile_directory = excluded.profile_directory,
                proxy_id = excluded.proxy_id,
                two_factor_secret = excluded.two_factor_secret,
                status = excluded.status,
                last_active_at = excluded.last_active_at,
                avatar_url = COALESCE(excluded.avatar_url, accounts.avatar_url);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$uid", account.Uid);
        _ = cmd.Parameters.AddWithValue("$display_name", account.DisplayName);
        _ = cmd.Parameters.AddWithValue("$cookie_header", account.CookieHeader);
        _ = cmd.Parameters.AddWithValue("$user_agent", account.UserAgent);
        _ = cmd.Parameters.AddWithValue("$profile_directory", account.ProfileDirectory);
        _ = cmd.Parameters.AddWithValue("$proxy_id", (object?)account.ProxyId ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$two_factor_secret", (object?)account.TwoFactorSecret ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$status", (int)account.Status);
        _ = cmd.Parameters.AddWithValue(
            "$last_active_at",
            account.LastActiveAt.HasValue
                ? account.LastActiveAt.Value.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$avatar_url", (object?)account.AvatarUrl ?? DBNull.Value);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        const string sql = "DELETE FROM accounts WHERE uid = $uid;";
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$uid", uid);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static AccountProfile MapAccount(SqliteDataReader reader)
    {
        string uid = reader.GetString(0);
        string displayName = reader.GetString(1);
        string cookieHeader = reader.GetString(2);
        string userAgent = reader.GetString(3);
        string profileDir = reader.GetString(4);
        string? proxyId = reader.IsDBNull(5) ? null : reader.GetString(5);
        string? twoFactor = reader.IsDBNull(6) ? null : reader.GetString(6);
        AccountStatus status = (AccountStatus)reader.GetInt32(7);
        DateTimeOffset? lastActive = null;

        if (!reader.IsDBNull(8))
        {
            string rawDate = reader.GetString(8);
            if (DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                lastActive = parsed;
            }
        }

        string? avatarUrl = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null;

        return new AccountProfile(
            Uid: uid,
            DisplayName: displayName,
            CookieHeader: cookieHeader,
            UserAgent: userAgent,
            ProfileDirectory: profileDir,
            ProxyId: proxyId,
            TwoFactorSecret: twoFactor,
            Status: status,
            LastActiveAt: lastActive,
            AvatarUrl: avatarUrl);
    }
}
