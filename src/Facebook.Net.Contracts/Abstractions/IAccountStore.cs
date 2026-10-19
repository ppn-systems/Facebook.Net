// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.Contracts.Abstractions;

/// <summary>
/// Persistence store for managing <see cref="AccountProfile"/> and <see cref="ProxyConfig"/> records in SQLite.
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// Initializes database schema tables if they do not exist.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all stored account profiles.
    /// </summary>
    Task<IReadOnlyList<AccountProfile>> GetAllAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific account profile by UID.
    /// </summary>
    Task<AccountProfile?> GetAccountByUidAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an account profile.
    /// </summary>
    Task UpsertAccountAsync(AccountProfile account, CancellationToken ct = default);

    /// <summary>
    /// Deletes an account profile by UID.
    /// </summary>
    Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all configured proxies.
    /// </summary>
    Task<IReadOnlyList<ProxyConfig>> GetAllProxiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a proxy configuration.
    /// </summary>
    Task UpsertProxyAsync(ProxyConfig proxy, CancellationToken ct = default);

    /// <summary>
    /// Deletes a proxy configuration by ID.
    /// </summary>
    Task<bool> DeleteProxyAsync(string proxyId, CancellationToken ct = default);
}
