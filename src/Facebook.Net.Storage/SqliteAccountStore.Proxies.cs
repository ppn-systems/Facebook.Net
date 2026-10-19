// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Microsoft.Data.Sqlite;

namespace Facebook.Net.Storage;

/// <summary>
/// Partial implementation of <see cref="SqliteAccountStore"/> containing proxy CRUD operations.
/// </summary>
public sealed partial class SqliteAccountStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProxyConfig>> GetAllProxiesAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT id, host, port, protocol, username, password FROM proxies ORDER BY id ASC;";

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        List<ProxyConfig> list = new();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapProxy(reader));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task UpsertProxyAsync(ProxyConfig proxy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        const string sql = """
            INSERT INTO proxies (id, host, port, protocol, username, password)
            VALUES ($id, $host, $port, $protocol, $username, $password)
            ON CONFLICT(id) DO UPDATE SET
                host = excluded.host,
                port = excluded.port,
                protocol = excluded.protocol,
                username = excluded.username,
                password = excluded.password;
            """;

        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", proxy.Id);
        _ = cmd.Parameters.AddWithValue("$host", proxy.Host);
        _ = cmd.Parameters.AddWithValue("$port", proxy.Port);
        _ = cmd.Parameters.AddWithValue("$protocol", (int)proxy.Protocol);
        _ = cmd.Parameters.AddWithValue("$username", (object?)proxy.Username ?? DBNull.Value);
        _ = cmd.Parameters.AddWithValue("$password", (object?)proxy.Password ?? DBNull.Value);

        _ = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProxyAsync(string proxyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyId);

        const string sql = "DELETE FROM proxies WHERE id = $id;";
        using SqliteConnection conn = new(this._connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.Parameters.AddWithValue("$id", proxyId);

        int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static ProxyConfig MapProxy(SqliteDataReader reader) =>
        new(
            Id: reader.GetString(0),
            Host: reader.GetString(1),
            Port: reader.GetInt32(2),
            Protocol: (ProxyProtocol)reader.GetInt32(3),
            Username: reader.IsDBNull(4) ? null : reader.GetString(4),
            Password: reader.IsDBNull(5) ? null : reader.GetString(5));
}
