// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Facebook.Net.UI.Core.Audit;

/// <summary>
/// Abstraction for persisting and querying structured application audit logs.
/// </summary>
public interface IAuditLogStore
{
    /// <summary>
    /// Occurs whenever a new <see cref="AuditLogRecord"/> is appended to the store.
    /// </summary>
    event EventHandler<AuditLogRecord>? LogAppended;

    /// <summary>
    /// Initializes the underlying audit log storage schema.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Appends a structured audit log entry and notifies live listeners.
    /// </summary>
    Task<AuditLogRecord> AppendAsync(
        AuditCategory category,
        AuditSeverity severity,
        string accountUid,
        string accountDisplayName,
        string targetName,
        string message,
        string? permalinkUrl = null,
        CancellationToken ct = default);

    /// <summary>
    /// Queries audit log records matching the specified <see cref="AuditLogFilter"/> criteria.
    /// </summary>
    Task<List<AuditLogRecord>> QueryAsync(AuditLogFilter? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Clears all stored audit log records.
    /// </summary>
    Task ClearAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Dedicated Native AOT-compatible SQLite store (<c>facebook_audit.db</c>) for persisting and filtering
/// application audit logs by event category, severity, account, timestamp range, and search keyword.
/// </summary>
public sealed class SqliteAuditLogStore : IAuditLogStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAuditLogStore"/> class.
    /// </summary>
    /// <param name="databaseFilePath">Path to the dedicated <c>facebook_audit.db</c> SQLite file.</param>
    public SqliteAuditLogStore(string databaseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5
        };
        this._connectionString = builder.ToString();
    }

    /// <inheritdoc />
    public event EventHandler<AuditLogRecord>? LogAppended;

    /// <summary>
    /// Initializes the WAL-mode <c>audit_logs</c> schema and indexes for fast filtering.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        const string sql = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 67108864;
            PRAGMA cache_size = -8000;

            CREATE TABLE IF NOT EXISTS audit_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                category INTEGER NOT NULL,
                severity INTEGER NOT NULL,
                account_uid TEXT NOT NULL,
                account_display_name TEXT NOT NULL,
                target_name TEXT NOT NULL,
                message TEXT NOT NULL,
                permalink_url TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_audit_category ON audit_logs(category);
            CREATE INDEX IF NOT EXISTS idx_audit_account ON audit_logs(account_uid);
            CREATE INDEX IF NOT EXISTS idx_audit_cat_id ON audit_logs(category, id DESC);
            CREATE INDEX IF NOT EXISTS idx_audit_acc_id ON audit_logs(account_uid, id DESC);
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends a structured audit log entry to the dedicated SQLite database and notifies live listeners.
    /// </summary>
    public async Task<AuditLogRecord> AppendAsync(
        AuditCategory category,
        AuditSeverity severity,
        string accountUid,
        string accountDisplayName,
        string targetName,
        string message,
        string? permalinkUrl = null,
        CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string safeUid = string.IsNullOrWhiteSpace(accountUid) ? "system" : accountUid;
        string safeName = string.IsNullOrWhiteSpace(accountDisplayName) ? "Hệ thống" : accountDisplayName;
        string safeTarget = string.IsNullOrWhiteSpace(targetName) ? "-" : targetName;
        string safeMsg = message ?? string.Empty;

        const string sql = """
            INSERT INTO audit_logs (
                timestamp, category, severity, account_uid, account_display_name, target_name, message, permalink_url
            )
            VALUES (
                $timestamp, $category, $severity, $account_uid, $account_display_name, $target_name, $message, $permalink_url
            );
            SELECT last_insert_rowid();
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$timestamp", now.ToString("O", CultureInfo.InvariantCulture));
        _ = cmd.Parameters.AddWithValue("$category", (int)category);
        _ = cmd.Parameters.AddWithValue("$severity", (int)severity);
        _ = cmd.Parameters.AddWithValue("$account_uid", safeUid);
        _ = cmd.Parameters.AddWithValue("$account_display_name", safeName);
        _ = cmd.Parameters.AddWithValue("$target_name", safeTarget);
        _ = cmd.Parameters.AddWithValue("$message", safeMsg);
        _ = cmd.Parameters.AddWithValue("$permalink_url", (object?)permalinkUrl ?? DBNull.Value);

        object? scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        long insertedId = scalar is long l ? l : Convert.ToInt64(scalar ?? 0L, CultureInfo.InvariantCulture);

        AuditLogRecord record = new(
            Id: insertedId,
            Timestamp: now,
            Category: category,
            Severity: severity,
            AccountUid: safeUid,
            AccountDisplayName: safeName,
            TargetName: safeTarget,
            Message: safeMsg,
            PermalinkUrl: permalinkUrl);

        this.LogAppended?.Invoke(this, record);
        return record;
    }

    /// <summary>
    /// Queries audit log records matching the specified <see cref="AuditLogFilter"/> criteria.
    /// </summary>
    public async Task<List<AuditLogRecord>> QueryAsync(AuditLogFilter? filter = null, CancellationToken ct = default)
    {
        AuditLogFilter activeFilter = filter ?? new AuditLogFilter();

        const string sql = """
            SELECT id, timestamp, category, severity, account_uid, account_display_name, target_name, message, permalink_url
            FROM audit_logs
            WHERE ($category IS NULL OR category = $category)
              AND ($severity IS NULL OR severity = $severity)
              AND ($account_uid IS NULL OR account_uid = $account_uid)
              AND ($from_time IS NULL OR timestamp >= $from_time)
              AND ($to_time IS NULL OR timestamp <= $to_time)
              AND ($keyword IS NULL OR message LIKE $keyword OR target_name LIKE $keyword OR account_display_name LIKE $keyword)
            ORDER BY id DESC
            LIMIT $limit;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$category", activeFilter.Category.HasValue ? (int)activeFilter.Category.Value : DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$severity", activeFilter.Severity.HasValue ? (int)activeFilter.Severity.Value : DBNull.Value);
        _ = cmd.Parameters.AddWithValue(
            "$account_uid",
            !string.IsNullOrWhiteSpace(activeFilter.AccountUid) ? activeFilter.AccountUid : DBNull.Value);
        _ = cmd.Parameters.AddWithValue(
            "$from_time",
            activeFilter.FromTime.HasValue ? activeFilter.FromTime.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        _ = cmd.Parameters.AddWithValue(
            "$to_time",
            activeFilter.ToTime.HasValue ? activeFilter.ToTime.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        _ = cmd.Parameters.AddWithValue(
            "$keyword",
            !string.IsNullOrWhiteSpace(activeFilter.SearchKeyword) ? $"%{activeFilter.SearchKeyword.Trim()}%" : DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$limit", activeFilter.Limit <= 0 ? 200 : activeFilter.Limit);

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<AuditLogRecord> results = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapRecord(reader));
        }

        return results;
    }

    /// <summary>
    /// Clears all stored audit log records and reclaims SQLite database pages via <c>VACUUM</c>.
    /// </summary>
    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM audit_logs;
            DELETE FROM sqlite_sequence WHERE name = 'audit_logs';
            PRAGMA wal_checkpoint(TRUNCATE);
            VACUUM;
            """;
        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static AuditLogRecord MapRecord(SqliteDataReader reader)
    {
        long id = reader.GetInt64(0);
        string rawTime = reader.GetString(1);
        DateTimeOffset ts = DateTimeOffset.TryParse(rawTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;

        return new AuditLogRecord(
            Id: id,
            Timestamp: ts,
            Category: (AuditCategory)reader.GetInt32(2),
            Severity: (AuditSeverity)reader.GetInt32(3),
            AccountUid: reader.GetString(4),
            AccountDisplayName: reader.GetString(5),
            TargetName: reader.GetString(6),
            Message: reader.GetString(7),
            PermalinkUrl: reader.IsDBNull(8) ? null : reader.GetString(8));
    }
}
