// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Leads;
using Microsoft.Data.Sqlite;

namespace Meta.UI.Core.AiAssistant;

/// <summary>
/// Categories of persistent AI Assistant context memories stored in SQLite.
/// </summary>
public enum AiMemoryCategory
{
    /// <summary>General contextual facts or instructions.</summary>
    General = 0,

    /// <summary>User preferences, workflow habits, and UI/automation settings.</summary>
    UserPreference = 1,

    /// <summary>Product catalog, pricing, USPs, and service details.</summary>
    ProductKnowledge = 2,

    /// <summary>Copywriting tone, brand voice, and Spintax formatting rules.</summary>
    WritingStyle = 3,

    /// <summary>Campaign scheduling, delay bounds, and safety constraints.</summary>
    CampaignRule = 4,

    /// <summary>Target group selection, niches, and tagging strategies.</summary>
    GroupStrategy = 5,

    /// <summary>Buyer-intent keywords, lead qualification, and consultation scripts.</summary>
    LeadPlaybook = 6
}

/// <summary>
/// Represents a single persistent AI context memory item in <c>facebook_ai_memory.db</c>.
/// </summary>
public sealed record AiMemoryRecord(
    string Id,
    AiMemoryCategory Category,
    string Key,
    string Content,
    int Importance,
    string? AccountUid,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents a conversation thread in the AI Assistant multi-conversation history.
/// </summary>
public sealed record AiConversationThread(
    string Id,
    string Title,
    string? AgyConversationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int MessageCount);

/// <summary>
/// Represents a persisted chat message turn in the AI Assistant conversation history.
/// </summary>
public sealed record AiChatMessageRecord(
    long Id,
    string Role,
    string Content,
    string? ProposedActionKind,
    string? ProposedActionPayloadJson,
    bool IsActionExecuted,
    DateTimeOffset TimestampUtc,
    string ConversationId = "default");

/// <summary>
/// Contract for persisting and querying AI Assistant context memories and chat history in SQLite.
/// </summary>
public interface IAiMemoryStore
{
    /// <summary>
    /// Initializes the SQLite WAL schema and indexes for AI memories and chat history.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically inserts or updates an AI memory record keyed by <paramref name="category"/> and case-insensitive <paramref name="key"/>.
    /// </summary>
    Task<AiMemoryRecord> SaveMemoryAsync(
        AiMemoryCategory category,
        string key,
        string content,
        int importance = 3,
        string? accountUid = null,
        CancellationToken ct = default);

    /// <summary>
    /// Queries stored AI memory records ordered by importance and most recent update.
    /// </summary>
    Task<IReadOnlyList<AiMemoryRecord>> QueryMemoriesAsync(
        string? keyword = null,
        AiMemoryCategory? category = null,
        string? accountUid = null,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a memory record by its exact ID or case-insensitive key.
    /// </summary>
    Task<bool> DeleteMemoryAsync(string idOrKey, CancellationToken ct = default);

    /// <summary>
    /// Clears all memory records or only those belonging to a specific <paramref name="category"/>.
    /// </summary>
    Task<int> ClearMemoriesAsync(AiMemoryCategory? category = null, CancellationToken ct = default);

    /// <summary>
    /// Appends a user or assistant message to the persistent chat history table.
    /// </summary>
    Task<AiChatMessageRecord> AppendChatMessageAsync(
        string role,
        string content,
        string? proposedActionKind = null,
        string? proposedActionPayloadJson = null,
        string? conversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a previously proposed batch action on a chat message as executed.
    /// </summary>
    Task MarkChatActionExecutedAsync(long messageId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent chat messages in chronological order, optionally filtered by <paramref name="conversationId"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Backward-compatible optional conversationId parameter.")]
    Task<IReadOnlyList<AiChatMessageRecord>> GetRecentChatHistoryAsync(
        int limit = 100,
        CancellationToken ct = default,
        string? conversationId = null);

    /// <summary>
    /// Clears stored chat messages from SQLite, optionally scoped to a specific <paramref name="conversationId"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Backward-compatible optional conversationId parameter.")]
    Task ClearChatHistoryAsync(CancellationToken ct = default, string? conversationId = null);

    /// <summary>
    /// Retrieves stored conversation threads ordered by most recently updated.
    /// </summary>
    Task<IReadOnlyList<AiConversationThread>> GetConversationsAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a conversation thread's metadata and optional <c>agy</c> CLI session ID.
    /// </summary>
    Task<AiConversationThread> SaveConversationAsync(
        string conversationId,
        string title,
        string? agyConversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation thread and all of its associated chat messages.
    /// </summary>
    Task<bool> DeleteConversationAsync(
        string conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Builds the composite system context prompt combining the user's configured <c>AiSystemPrompt</c>,
    /// active Facebook account metadata, and top stored <see cref="AiMemoryRecord"/> entries.
    /// </summary>
    Task<string> BuildSystemContextPromptAsync(
        string? baseSystemPrompt = null,
        string? activeAccountUid = null,
        string? activeAccountName = null,
        CancellationToken ct = default);
}

/// <summary>
/// High-throughput, Native AOT-compatible SQLite implementation of <see cref="IAiMemoryStore"/> (<c>facebook_ai_memory.db</c>).
/// Shared between the desktop UI and <c>Facebook.Net.Mcp</c> using WAL mode.
/// </summary>
public sealed class SqliteAiMemoryStore : IAiMemoryStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAiMemoryStore"/> class.
    /// </summary>
    /// <param name="databaseFilePath">Path to <c>facebook_ai_memory.db</c>.</param>
    public SqliteAiMemoryStore(string databaseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        string? dir = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

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
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA mmap_size=67108864;
            PRAGMA cache_size=-8000;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS ai_memories (
                id TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                mem_key TEXT NOT NULL,
                content TEXT NOT NULL,
                importance INTEGER NOT NULL DEFAULT 3,
                account_uid TEXT,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_ai_memories_cat_lower_key
                ON ai_memories(category, LOWER(mem_key));

            CREATE INDEX IF NOT EXISTS idx_ai_memories_importance
                ON ai_memories(importance DESC, updated_at_utc DESC);

            CREATE TABLE IF NOT EXISTS ai_conversations (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                agy_conversation_id TEXT,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_ai_conversations_updated
                ON ai_conversations(updated_at_utc DESC);

            CREATE TABLE IF NOT EXISTS ai_chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                proposed_action_kind TEXT,
                proposed_action_payload_json TEXT,
                is_action_executed INTEGER NOT NULL DEFAULT 0,
                timestamp_utc TEXT NOT NULL,
                conversation_id TEXT NOT NULL DEFAULT 'default'
            );

            CREATE INDEX IF NOT EXISTS idx_ai_chat_messages_ts
                ON ai_chat_messages(id DESC);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        try
        {
            using SqliteCommand alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE ai_chat_messages ADD COLUMN conversation_id TEXT NOT NULL DEFAULT 'default';";
            _ = await alterCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // Column already exists
        }

        using (SqliteCommand idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_ai_chat_messages_conv
                    ON ai_chat_messages(conversation_id, id DESC);
                """;
            _ = await idxCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await BackfillConversationsFromMessagesAsync(conn, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AiMemoryRecord> SaveMemoryAsync(
        AiMemoryCategory category,
        string key,
        string content,
        int importance = 3,
        string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        string cleanKey = key.Trim();
        string cleanContent = content.Trim();
        int clampedImportance = Math.Clamp(importance, 1, 5);
        string? cleanUid = string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string nowIso = now.ToString("O", CultureInfo.InvariantCulture);
        string newId = $"mem_{Guid.NewGuid():N}";

        const string sql = """
            INSERT INTO ai_memories (
                id, category, mem_key, content, importance, account_uid, created_at_utc, updated_at_utc
            )
            VALUES (
                $id, $category, $mem_key, $content, $importance, $account_uid, $created_at_utc, $updated_at_utc
            )
            ON CONFLICT(category, LOWER(mem_key)) DO UPDATE SET
                content = excluded.content,
                importance = excluded.importance,
                account_uid = COALESCE(excluded.account_uid, ai_memories.account_uid),
                updated_at_utc = excluded.updated_at_utc
            RETURNING id, category, mem_key, content, importance, account_uid, created_at_utc, updated_at_utc;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", newId);
        _ = cmd.Parameters.AddWithValue("$category", (int)category);
        _ = cmd.Parameters.AddWithValue("$mem_key", cleanKey);
        _ = cmd.Parameters.AddWithValue("$content", cleanContent);
        _ = cmd.Parameters.AddWithValue("$importance", clampedImportance);
        _ = cmd.Parameters.AddWithValue("$account_uid", (object?)cleanUid ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$created_at_utc", nowIso);
        _ = cmd.Parameters.AddWithValue("$updated_at_utc", nowIso);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return MapMemoryRecord(reader);
        }

        return new AiMemoryRecord(
            Id: newId,
            Category: category,
            Key: cleanKey,
            Content: cleanContent,
            Importance: clampedImportance,
            AccountUid: cleanUid,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiMemoryRecord>> QueryMemoriesAsync(
        string? keyword = null,
        AiMemoryCategory? category = null,
        string? accountUid = null,
        int limit = int.MaxValue,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, category, mem_key, content, importance, account_uid, created_at_utc, updated_at_utc
            FROM ai_memories
            WHERE ($category IS NULL OR category = $category)
              AND ($account_uid IS NULL OR account_uid IS NULL OR account_uid = '' OR account_uid = $account_uid)
            ORDER BY importance DESC, updated_at_utc DESC;
            """;

        int maxResults = limit <= 0 ? int.MaxValue : limit;
        string? trimmedKw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$category", category.HasValue ? (int)category.Value : DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$account_uid", !string.IsNullOrWhiteSpace(accountUid) ? accountUid.Trim() : DBNull.Value);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<AiMemoryRecord> items = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            AiMemoryRecord record = MapMemoryRecord(reader);
            if (trimmedKw is not null &&
                !LeadScoringService.Instance.MatchesAccentInsensitive(record.Key, trimmedKw) &&
                !LeadScoringService.Instance.MatchesAccentInsensitive(record.Content, trimmedKw))
            {
                continue;
            }

            items.Add(record);
            if (items.Count >= maxResults)
            {
                break;
            }
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMemoryAsync(string idOrKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrKey))
        {
            return false;
        }

        const string sql = """
            DELETE FROM ai_memories
            WHERE id = $target OR LOWER(mem_key) = LOWER($target);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$target", idOrKey.Trim());
        int deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<int> ClearMemoriesAsync(AiMemoryCategory? category = null, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM ai_memories
            WHERE ($category IS NULL OR category = $category);
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$category", category.HasValue ? (int)category.Value : DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AiChatMessageRecord> AppendChatMessageAsync(
        string role,
        string content,
        string? proposedActionKind = null,
        string? proposedActionPayloadJson = null,
        string? conversationId = null,
        CancellationToken ct = default)
    {
        string safeRole = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim();
        string safeContent = content ?? string.Empty;
        string safeConversationId = string.IsNullOrWhiteSpace(conversationId) ? "default" : conversationId.Trim();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string nowIso = now.ToString("O", CultureInfo.InvariantCulture);
        string fallbackTitle = DeriveConversationTitle(safeContent);

        const string sql = """
            INSERT INTO ai_conversations (
                id, title, agy_conversation_id, created_at_utc, updated_at_utc
            )
            VALUES (
                $conv_id, $title, NULL, $timestamp_utc, $timestamp_utc
            )
            ON CONFLICT(id) DO UPDATE SET
                updated_at_utc = excluded.updated_at_utc;

            INSERT INTO ai_chat_messages (
                role, content, proposed_action_kind, proposed_action_payload_json, is_action_executed, timestamp_utc, conversation_id
            )
            VALUES (
                $role, $content, $action_kind, $action_payload, 0, $timestamp_utc, $conv_id
            );
            SELECT last_insert_rowid();
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$role", safeRole);
        _ = cmd.Parameters.AddWithValue("$content", safeContent);
        _ = cmd.Parameters.AddWithValue("$action_kind", (object?)proposedActionKind ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$action_payload", (object?)proposedActionPayloadJson ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$timestamp_utc", nowIso);
        _ = cmd.Parameters.AddWithValue("$conv_id", safeConversationId);
        _ = cmd.Parameters.AddWithValue("$title", fallbackTitle);

        object? scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        long id = scalar is long l ? l : Convert.ToInt64(scalar ?? 0L, CultureInfo.InvariantCulture);

        return new AiChatMessageRecord(
            Id: id,
            Role: safeRole,
            Content: safeContent,
            ProposedActionKind: proposedActionKind,
            ProposedActionPayloadJson: proposedActionPayloadJson,
            IsActionExecuted: false,
            TimestampUtc: now,
            ConversationId: safeConversationId);
    }

    /// <inheritdoc />
    public async Task MarkChatActionExecutedAsync(long messageId, CancellationToken ct = default)
    {
        const string sql = "UPDATE ai_chat_messages SET is_action_executed = 1 WHERE id = $id;";
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", messageId);
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Backward-compatible optional conversationId parameter.")]
    public async Task<IReadOnlyList<AiChatMessageRecord>> GetRecentChatHistoryAsync(
        int limit = 100,
        CancellationToken ct = default,
        string? conversationId = null)
    {
        const string sql = """
            SELECT id, role, content, proposed_action_kind, proposed_action_payload_json, is_action_executed, timestamp_utc, conversation_id
            FROM ai_chat_messages
            WHERE ($conv_id IS NULL OR conversation_id = $conv_id)
            ORDER BY id DESC
            LIMIT $limit;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$conv_id", !string.IsNullOrWhiteSpace(conversationId) ? conversationId.Trim() : DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<AiChatMessageRecord> list = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            long id = reader.GetInt64(0);
            string role = reader.GetString(1);
            string content = reader.GetString(2);
            string? actionKind = await reader.IsDBNullAsync(3, ct).ConfigureAwait(false) ? null : reader.GetString(3);
            string? actionPayload = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetString(4);
            bool executed = reader.GetInt32(5) != 0;
            string rawTs = reader.GetString(6);
            string convId = await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? "default" : reader.GetString(7);
            DateTimeOffset ts = DateTimeOffset.TryParse(rawTs, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            list.Add(new AiChatMessageRecord(id, role, content, actionKind, actionPayload, executed, ts, convId));
        }

        list.Reverse();
        return list;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Backward-compatible optional conversationId parameter.")]
    public async Task ClearChatHistoryAsync(CancellationToken ct = default, string? conversationId = null)
    {
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            cmd.CommandText = "DELETE FROM ai_chat_messages WHERE conversation_id = $conv_id;";
            _ = cmd.Parameters.AddWithValue("$conv_id", conversationId.Trim());
        }
        else
        {
            cmd.CommandText = """
                DELETE FROM ai_chat_messages;
                DELETE FROM sqlite_sequence WHERE name = 'ai_chat_messages';
                """;
        }

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiConversationThread>> GetConversationsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await BackfillConversationsFromMessagesAsync(conn, ct).ConfigureAwait(false);

        const string sql = """
            SELECT
                c.id,
                c.title,
                c.agy_conversation_id,
                c.created_at_utc,
                c.updated_at_utc,
                COUNT(m.id) AS msg_count
            FROM ai_conversations c
            LEFT JOIN ai_chat_messages m ON m.conversation_id = c.id
            GROUP BY c.id
            ORDER BY c.updated_at_utc DESC
            LIMIT $limit;
            """;

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<AiConversationThread> threads = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            threads.Add(MapConversationThread(reader));
        }

        return threads;
    }

    /// <inheritdoc />
    public async Task<AiConversationThread> SaveConversationAsync(
        string conversationId,
        string title,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        string cleanId = string.IsNullOrWhiteSpace(conversationId) ? "default" : conversationId.Trim();
        string cleanTitle = DeriveConversationTitle(title);
        string? cleanAgyId = string.IsNullOrWhiteSpace(agyConversationId) ? null : agyConversationId.Trim();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string nowIso = now.ToString("O", CultureInfo.InvariantCulture);

        const string sql = """
            INSERT INTO ai_conversations (
                id, title, agy_conversation_id, created_at_utc, updated_at_utc
            )
            VALUES (
                $id, $title, $agy_id, $now, $now
            )
            ON CONFLICT(id) DO UPDATE SET
                title = CASE WHEN $has_explicit_title = 1 THEN excluded.title ELSE ai_conversations.title END,
                agy_conversation_id = COALESCE(excluded.agy_conversation_id, ai_conversations.agy_conversation_id),
                updated_at_utc = excluded.updated_at_utc;

            SELECT
                c.id,
                c.title,
                c.agy_conversation_id,
                c.created_at_utc,
                c.updated_at_utc,
                (SELECT COUNT(*) FROM ai_chat_messages m WHERE m.conversation_id = c.id) AS msg_count
            FROM ai_conversations c
            WHERE c.id = $id;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", cleanId);
        _ = cmd.Parameters.AddWithValue("$title", cleanTitle);
        _ = cmd.Parameters.AddWithValue("$agy_id", (object?)cleanAgyId ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$now", nowIso);
        _ = cmd.Parameters.AddWithValue("$has_explicit_title", !string.IsNullOrWhiteSpace(title) ? 1 : 0);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return MapConversationThread(reader);
        }

        return new AiConversationThread(
            Id: cleanId,
            Title: cleanTitle,
            AgyConversationId: cleanAgyId,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            MessageCount: 0);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteConversationAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        string cleanId = conversationId.Trim();
        const string sql = """
            DELETE FROM ai_chat_messages WHERE conversation_id = $id;
            DELETE FROM ai_conversations WHERE id = $id;
            """;

        using SqliteConnection conn = new(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", cleanId);
        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<string> BuildSystemContextPromptAsync(
        string? baseSystemPrompt = null,
        string? activeAccountUid = null,
        string? activeAccountName = null,
        CancellationToken ct = default)
    {
        StringBuilder sb = new();
        _ = sb.AppendLine("[SYSTEM PROMPT]");
        _ = sb.AppendLine(AppConfig.DefaultAiSystemPrompt);

        if (!string.IsNullOrWhiteSpace(baseSystemPrompt) &&
            !string.Equals(baseSystemPrompt.Trim(), AppConfig.DefaultAiSystemPrompt, StringComparison.Ordinal))
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine("[USER CUSTOM CONTEXT]");
            _ = sb.AppendLine(baseSystemPrompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(activeAccountUid) || !string.IsNullOrWhiteSpace(activeAccountName))
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine(CultureInfo.InvariantCulture, $"[ACTIVE FACEBOOK ACCOUNT]: {activeAccountName ?? "Unknown"} (UID: {activeAccountUid ?? "none"})");
        }

        IReadOnlyList<AiMemoryRecord> memories = await this.QueryMemoriesAsync(
            keyword: null,
            category: null,
            accountUid: activeAccountUid,
            limit: 40,
            ct: ct).ConfigureAwait(false);

        if (memories.Count > 0)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine("[AI CONTEXT MEMORY]");
            for (int i = 0; i < memories.Count; i++)
            {
                AiMemoryRecord m = memories[i];
                _ = sb.AppendLine(CultureInfo.InvariantCulture, $"- [{m.Category}] {m.Key}: {m.Content} (importance={m.Importance})");
            }
        }

        return sb.ToString().Trim();
    }

    private static async Task BackfillConversationsFromMessagesAsync(SqliteConnection conn, CancellationToken ct)
    {
        const string backfillSql = """
            INSERT OR IGNORE INTO ai_conversations (id, title, agy_conversation_id, created_at_utc, updated_at_utc)
            SELECT
                m.conversation_id,
                COALESCE(
                    (SELECT SUBSTR(TRIM(m2.content), 1, 60)
                     FROM ai_chat_messages m2
                     WHERE m2.conversation_id = m.conversation_id AND m2.role = 'user' AND TRIM(m2.content) != ''
                     ORDER BY m2.id ASC
                     LIMIT 1),
                    'Cuộc trò chuyện trước'
                ),
                NULL,
                MIN(m.timestamp_utc),
                MAX(m.timestamp_utc)
            FROM ai_chat_messages m
            WHERE m.conversation_id IS NOT NULL AND m.conversation_id != ''
            GROUP BY m.conversation_id;
            """;

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = backfillSql;
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string DeriveConversationTitle(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "Cuộc trò chuyện mới";
        }

        string trimmed = rawText.Trim();
        int newlineIdx = trimmed.IndexOfAny(['\r', '\n']);
        string firstLine = newlineIdx >= 0 ? trimmed[..newlineIdx].Trim() : trimmed;
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "Cuộc trò chuyện mới";
        }

        return firstLine.Length > 60 ? firstLine[..60].Trim() + "…" : firstLine;
    }

    private static AiConversationThread MapConversationThread(SqliteDataReader reader)
    {
        string id = reader.GetString(0);
        string title = reader.GetString(1);
        string? agyConvId = reader.IsDBNull(2) ? null : reader.GetString(2);
        string createdRaw = reader.GetString(3);
        string updatedRaw = reader.GetString(4);
        int msgCount = reader.GetInt32(5);

        DateTimeOffset created = DateTimeOffset.TryParse(createdRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset cParsed)
            ? cParsed
            : DateTimeOffset.UtcNow;
        DateTimeOffset updated = DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset uParsed)
            ? uParsed
            : created;

        return new AiConversationThread(
            Id: id,
            Title: title,
            AgyConversationId: agyConvId,
            CreatedAtUtc: created,
            UpdatedAtUtc: updated,
            MessageCount: msgCount);
    }

    private static AiMemoryRecord MapMemoryRecord(SqliteDataReader reader)
    {
        string id = reader.GetString(0);
        AiMemoryCategory category = (AiMemoryCategory)reader.GetInt32(1);
        string key = reader.GetString(2);
        string content = reader.GetString(3);
        int importance = reader.GetInt32(4);
        string? accountUid = reader.IsDBNull(5) ? null : reader.GetString(5);
        string createdRaw = reader.GetString(6);
        string updatedRaw = reader.GetString(7);

        DateTimeOffset created = DateTimeOffset.TryParse(createdRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset cParsed)
            ? cParsed
            : DateTimeOffset.UtcNow;
        DateTimeOffset updated = DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset uParsed)
            ? uParsed
            : created;

        return new AiMemoryRecord(
            Id: id,
            Category: category,
            Key: key,
            Content: content,
            Importance: importance,
            AccountUid: accountUid,
            CreatedAtUtc: created,
            UpdatedAtUtc: updated);
    }
}
