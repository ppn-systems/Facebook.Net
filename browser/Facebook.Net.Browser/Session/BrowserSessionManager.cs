// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Browser.Protocol;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Browser.Session;

/// <summary>
/// Orchestrates multiple isolated <see cref="IBrowserSession"/> instances with a staggered launch queue
/// to prevent simultaneous process spikes and proxy IP bursts.
/// </summary>
public sealed class BrowserSessionManager : IBrowserManager
{
    private readonly IBrowserAdapter _adapter;
    private readonly TimeSpan _staggerDelay;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IBrowserSession> _sessions = new(StringComparer.Ordinal);
    private DateTimeOffset _lastLaunchTime = DateTimeOffset.MinValue;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserSessionManager"/> class.
    /// </summary>
    public BrowserSessionManager(IBrowserAdapter adapter, TimeSpan? staggerDelay = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        this._adapter = adapter;
        this._staggerDelay = staggerDelay ?? TimeSpan.FromMilliseconds(ProtocolConstants.Protocol.DefaultStaggeredLaunchDelayMs);
    }

    /// <inheritdoc />
    public event EventHandler<SessionStateChangedEvent>? SessionStateChanged;

    /// <inheritdoc />
    public async Task<IBrowserSession> AcquireSessionAsync(BrowserLaunchOptions options, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccountUid);

        if (this._sessions.TryGetValue(options.AccountUid, out IBrowserSession? existing) &&
            existing.State == BrowserState.Running &&
            (options.Headless || existing.IsBrowserProcessAttached))
        {
            return existing;
        }

        await this._launchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (this._sessions.TryGetValue(options.AccountUid, out existing))
            {
                if (existing.State == BrowserState.Running &&
                    (options.Headless || existing.IsBrowserProcessAttached))
                {
                    return existing;
                }

                _ = this._sessions.TryRemove(options.AccountUid, out _);
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            if (this._lastLaunchTime != DateTimeOffset.MinValue && this._staggerDelay > TimeSpan.Zero)
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - this._lastLaunchTime;
                if (elapsed < this._staggerDelay)
                {
                    await Task.Delay(this._staggerDelay - elapsed, ct).ConfigureAwait(false);
                }
            }

            this.EmitStateChanged(options.AccountUid, BrowserState.Starting, AccountStatus.Running, "Launching browser profile...");

            IBrowserProcess process = await this._adapter.LaunchProcessAsync(options, ct).ConfigureAwait(false);
            CdpConnection cdp = new();
            try
            {
                await cdp.ConnectAsync(process.DebuggerWebSocketUri, ct).ConfigureAwait(false);
                ChromiumSession session = new(options.AccountUid, process, cdp, options);
                if (options.EffectiveBlockStaticMedia)
                {
                    await session.ConfigureNetworkOptimizationAsync(blockStaticMedia: true, ct).ConfigureAwait(false);
                }

                this._sessions[options.AccountUid] = session;
                this._lastLaunchTime = DateTimeOffset.UtcNow;

                this.EmitStateChanged(options.AccountUid, BrowserState.Running, AccountStatus.Active, "Browser session connected.");
                return session;
            }
            catch
            {
                await cdp.DisposeAsync().ConfigureAwait(false);
                await process.DisposeAsync().ConfigureAwait(false);
                this.EmitStateChanged(options.AccountUid, BrowserState.Faulted, AccountStatus.Idle, "Failed to initialize CDP session.");
                throw;
            }
        }
        finally
        {
            _ = this._launchGate.Release();
        }
    }

    /// <inheritdoc />
    public IBrowserSession? TryGetSession(string accountUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        return this._sessions.TryGetValue(accountUid, out IBrowserSession? session) && session.State == BrowserState.Running
            ? session
            : null;
    }

    /// <inheritdoc />
    public async Task CloseSessionAsync(string accountUid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        if (this._sessions.TryRemove(accountUid, out IBrowserSession? session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            this.EmitStateChanged(accountUid, BrowserState.Stopped, AccountStatus.Idle, "Browser session closed.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        foreach (string uid in this._sessions.Keys)
        {
            if (this._sessions.TryRemove(uid, out IBrowserSession? session))
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        this._launchGate.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        foreach (string uid in this._sessions.Keys)
        {
            if (this._sessions.TryRemove(uid, out IBrowserSession? session))
            {
                session.Dispose();
            }
        }

        this._launchGate.Dispose();
    }

    private void EmitStateChanged(string accountUid, BrowserState browserState, AccountStatus accountStatus, string message) =>
        this.SessionStateChanged?.Invoke(this, new SessionStateChangedEvent(accountUid, browserState, accountStatus, message));
}
