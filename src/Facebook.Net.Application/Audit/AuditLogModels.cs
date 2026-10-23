// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Meta.UI.Core.Audit;

/// <summary>
/// Event category for structured audit logging.
/// </summary>
public enum AuditCategory
{
    /// <summary>System startup, settings, or configuration event.</summary>
    System = 0,

    /// <summary>Account session import, login, or health check event.</summary>
    Account = 1,

    /// <summary>Group sync, search, or membership join event.</summary>
    Group = 2,

    /// <summary>Group or personal timeline post publishing event.</summary>
    Post = 3,

    /// <summary>Sales commenting or post bumping event.</summary>
    SalesComment = 4,

    /// <summary>Group feed or buyer-intent lead scraping event.</summary>
    Scrape = 5,

    /// <summary>User-initiated personal action (create, edit, delete, toggle, assign, or preference change).</summary>
    Personal = 6
}

/// <summary>
/// Severity level for an audit log event.
/// </summary>
public enum AuditSeverity
{
    /// <summary>Informational progress event.</summary>
    Info = 0,

    /// <summary>Completed action with positive outcome.</summary>
    Success = 1,

    /// <summary>Skipped action, rate limit warning, or pending approval notice.</summary>
    Warning = 2,

    /// <summary>Failed action or security checkpoint alert.</summary>
    Error = 3
}

/// <summary>
/// Preset time range filters for querying audit logs in the collapsible taskbar drawer.
/// </summary>
public enum AuditTimeRange
{
    /// <summary>All recorded audit logs.</summary>
    AllTime = 0,

    /// <summary>Logs from the last 1 hour.</summary>
    LastHour = 1,

    /// <summary>Logs from the last 24 hours.</summary>
    Today24Hours = 2,

    /// <summary>Logs from the last 7 days.</summary>
    Last7Days = 3
}

/// <summary>
/// Represents a single immutable row in the dedicated <c>facebook_audit.db</c> SQLite database.
/// Uses human-friendly display names so end users never have to read raw numeric UIDs.
/// </summary>
public sealed record AuditLogRecord(
    long Id,
    DateTimeOffset Timestamp,
    AuditCategory Category,
    AuditSeverity Severity,
    string AccountUid,
    string AccountDisplayName,
    string TargetName,
    string Message,
    string? PermalinkUrl = null);

/// <summary>
/// Multi-criteria filter for querying the dedicated SQLite audit log database.
/// </summary>
public sealed record AuditLogFilter(
    AuditCategory? Category = null,
    AuditSeverity? Severity = null,
    string? AccountUid = null,
    DateTimeOffset? FromTime = null,
    DateTimeOffset? ToTime = null,
    string? SearchKeyword = null,
    int Limit = 200);

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
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    Task<List<AuditLogRecord>> QueryAsync(AuditLogFilter? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Clears all stored audit log records.
    /// </summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
