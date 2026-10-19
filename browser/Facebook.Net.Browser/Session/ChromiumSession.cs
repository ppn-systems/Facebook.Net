// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Browser.Protocol;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Browser.Session;

/// <summary>
/// Active Chromium browser session implementing <see cref="IBrowserSession"/> via Chrome DevTools Protocol (CDP).
/// Split into partial files for core lifecycle/navigation and in-page delegation/DOM bridge.
/// </summary>
public sealed partial class ChromiumSession : IBrowserSession
{
    private static readonly IReadOnlyList<string> s_baseOriginUrls = new List<string>(1)
    {
        ProtocolConstants.Endpoints.BaseOrigin
    };

    private readonly IBrowserProcess _process;
    private readonly CdpConnection _cdp;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromiumSession"/> class.
    /// </summary>
    /// <param name="accountUid">The associated account UID.</param>
    /// <param name="process">The underlying OS browser process.</param>
    /// <param name="cdp">The connected CDP WebSocket client.</param>
    public ChromiumSession(string accountUid, IBrowserProcess process, CdpConnection cdp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(cdp);

        this.AccountUid = accountUid;
        this._process = process;
        this._cdp = cdp;
    }

    /// <inheritdoc />
    public string AccountUid { get; }

    /// <inheritdoc />
    public BrowserState State =>
        this._isDisposed || this._process.HasExited
            ? BrowserState.Stopped
            : (this._cdp.IsConnected ? BrowserState.Running : BrowserState.Faulted);

    /// <inheritdoc />
    public SessionMaterial? CurrentMaterial { get; private set; }

    /// <inheritdoc />
    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using JsonDocument navDoc = await this._cdp.SendCommandAsync(
            "Page.navigate",
            new CdpNavigateParams(url),
            ct).ConfigureAwait(false);

        const string waitReadyJs = """
            new Promise((resolve) => {
                if (document.readyState === 'complete') {
                    setTimeout(() => resolve(true), 250);
                    return;
                }
                const onReady = () => {
                    window.removeEventListener('load', onReady);
                    setTimeout(() => resolve(true), 250);
                };
                window.addEventListener('load', onReady);
                setTimeout(() => resolve(true), 8000);
            })
            """;

        using JsonDocument evalDoc = await this._cdp.SendCommandAsync(
            "Runtime.evaluate",
            new CdpEvaluateParams(waitReadyJs, AwaitPromise: true, ReturnByValue: true),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SessionMaterial> ExtractMaterialAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);

        using JsonDocument cookieDoc = await this._cdp.SendCommandAsync(
            "Network.getCookies",
            new CdpGetCookiesParams(s_baseOriginUrls),
            ct).ConfigureAwait(false);

        string cookieHeader = BuildCookieHeader(cookieDoc.RootElement, out string? cookieUid);

        const string extractDomJs = """
            (() => {
                const html = document.documentElement.innerHTML;
                const dtsg1 = html.match(/DTSGInitialData"\s*,\s*\[\]\s*,\s*\{"token":"([^"]+)"/);
                const dtsg2 = html.match(/name="fb_dtsg"\s+value="([^"]+)"/);
                const lsd = html.match(/"LSD"\s*,\s*\[\]\s*,\s*\{"token":"([^"]+)"/);
                const rev = html.match(/"client_revision":(\d+)/);
                const hs = html.match(/"haste_session":"([^"]+)"/);
                const uidMatch = document.cookie.match(/c_user=(\d+)/) || html.match(/"USER_ID":"(\d+)"/);
                const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
                const rand6 = () => Array.from({length: 6}, () => chars[Math.floor(Math.random() * chars.length)]).join('');
                return JSON.stringify({
                    uid: uidMatch ? uidMatch[1] : '',
                    fbDtsg: dtsg1 ? dtsg1[1] : (dtsg2 ? dtsg2[1] : ''),
                    lsd: lsd ? lsd[1] : '',
                    clientRevision: rev ? rev[1] : '',
                    hasteSession: hs ? hs[1] : '20745.HYP:comet_pkg.2.1...1',
                    webSessionId: rand6() + ':' + rand6() + ':' + rand6(),
                    userAgent: navigator.userAgent
                });
            })()
            """;

        string snapshotJson = await this.EvaluateStringAsync(extractDomJs, awaitPromise: false, ct).ConfigureAwait(false);
        DomMaterialSnapshot? snapshot = JsonSerializer.Deserialize(snapshotJson, BrowserJsonContext.Default.DomMaterialSnapshot)
            ?? throw new ProtocolException("Failed to deserialize DOM session material snapshot.");

        string resolvedUid = !string.IsNullOrWhiteSpace(cookieUid) && cookieUid != "0"
            ? cookieUid
            : snapshot.Uid;

        if (string.IsNullOrWhiteSpace(resolvedUid) || resolvedUid == "0" || string.IsNullOrWhiteSpace(snapshot.FbDtsg))
        {
            throw new CheckpointException(
                this.AccountUid,
                "Authenticated session tokens (c_user or fb_dtsg) were not found in the active browser tab.");
        }

        string jazoest = ProtocolCodec.ComputeJazoest(snapshot.FbDtsg.AsSpan());

        SessionMaterial material = new(
            Uid: resolvedUid,
            CookieHeader: cookieHeader,
            FbDtsg: snapshot.FbDtsg,
            Jazoest: jazoest,
            Lsd: snapshot.Lsd,
            ClientRevision: snapshot.ClientRevision,
            HasteSession: snapshot.HasteSession,
            WebSessionId: snapshot.WebSessionId,
            UserAgent: snapshot.UserAgent,
            ExtractedAt: DateTimeOffset.UtcNow);

        this.CurrentMaterial = material;
        return material;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        await this._cdp.DisposeAsync().ConfigureAwait(false);
        await this._process.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._cdp.Dispose();
        this._process.Dispose();
    }

    private static string BuildCookieHeader(JsonElement root, out string? cUser)
    {
        cUser = null;
        if (!root.TryGetProperty("result"u8, out JsonElement resultEl) ||
            !resultEl.TryGetProperty("cookies"u8, out JsonElement cookiesEl) ||
            cookiesEl.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        StringBuilder sb = new(512);
        foreach (JsonElement cookie in cookiesEl.EnumerateArray())
        {
            string? name = cookie.TryGetProperty("name"u8, out JsonElement nEl) ? nEl.GetString() : null;
            string? val = cookie.TryGetProperty("value"u8, out JsonElement vEl) ? vEl.GetString() : null;
            if (string.IsNullOrEmpty(name) || val is null)
            {
                continue;
            }

            if (string.Equals(name, "c_user", StringComparison.Ordinal))
            {
                cUser = val;
            }

            if (sb.Length > 0)
            {
                _ = sb.Append("; ");
            }

            _ = sb.Append(name).Append('=').Append(val);
        }

        return sb.ToString();
    }
}
