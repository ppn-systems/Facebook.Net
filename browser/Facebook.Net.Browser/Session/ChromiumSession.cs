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
    private static readonly IReadOnlyList<string> s_baseOriginUrls = new List<string>(4)
    {
        ProtocolConstants.Endpoints.BaseOrigin,
        "https://facebook.com",
        "https://web.facebook.com",
        "https://m.facebook.com"
    };

    private readonly IBrowserProcess _process;
    private readonly CdpConnection _cdp;
    private readonly BrowserLaunchOptions? _options;
    private bool _isDetached;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromiumSession"/> class.
    /// </summary>
    /// <param name="accountUid">The associated account UID.</param>
    /// <param name="process">The underlying OS browser process.</param>
    /// <param name="cdp">The connected CDP WebSocket client.</param>
    /// <param name="options">Optional launch options governing media blocking, cache cleanup, and process detach after token extraction.</param>
    public ChromiumSession(
        string accountUid,
        IBrowserProcess process,
        CdpConnection cdp,
        BrowserLaunchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(cdp);

        this.AccountUid = accountUid;
        this._process = process;
        this._cdp = cdp;
        this._options = options;
    }

    /// <inheritdoc />
    public string AccountUid { get; }

    /// <inheritdoc />
    public bool IsBrowserProcessAttached =>
        !this._isDisposed && !this._isDetached && !this._process.HasExited && this._cdp.IsConnected;

    /// <inheritdoc />
    public BrowserState State
    {
        get
        {
            if (this._isDisposed)
            {
                return BrowserState.Stopped;
            }

            if (this._isDetached)
            {
                return this.CurrentMaterial is not null ? BrowserState.Running : BrowserState.Stopped;
            }

            return this._process.HasExited
                ? BrowserState.Stopped
                : (this._cdp.IsConnected ? BrowserState.Running : BrowserState.Faulted);
        }
    }

    /// <inheritdoc />
    public SessionMaterial? CurrentMaterial { get; private set; }

    /// <summary>
    /// Configures CDP network optimizations (<c>Network.enable</c> and <c>Network.setBlockedURLs</c>)
    /// when running in automated/headless mode to block heavy static assets and save 75%–90% of proxy bandwidth.
    /// Skipped for interactive visible browser sessions to preserve full visual rendering and avoid bot heuristics.
    /// </summary>
    public async Task ConfigureNetworkOptimizationAsync(bool blockStaticMedia, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        if (!blockStaticMedia || !this.IsBrowserProcessAttached)
        {
            return;
        }

        using JsonDocument enableDoc = await this._cdp
            .SendCommandAsync("Network.enable", null, ct)
            .ConfigureAwait(false);

        using JsonDocument blockDoc = await this._cdp
            .SendCommandAsync(
                "Network.setBlockedURLs",
                new CdpSetBlockedUrlsParams(BrowserLauncherHelpers.BlockedStaticAssetPatterns),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DetachBrowserProcessAsync(CancellationToken ct = default)
    {
        if (this._isDisposed || this._isDetached)
        {
            return;
        }

        this._isDetached = true;
        await this._cdp.DisposeAsync().ConfigureAwait(false);
        await this._process.TerminateAsync(ct).ConfigureAwait(false);
        await this._process.DisposeAsync().ConfigureAwait(false);

        if (this._options is { CleanDiskCache: true } && !string.IsNullOrWhiteSpace(this._options.UserDataDir))
        {
            _ = BrowserLauncherHelpers.CleanProfileDiskCache(this._options.UserDataDir);
        }
    }

    /// <inheritdoc />
    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (this._isDetached && this.CurrentMaterial is not null)
        {
            // Browser process was intentionally terminated after token extraction; direct HTTP calls do not require DOM navigation
            return;
        }

        using JsonDocument navDoc = await this._cdp.SendCommandAsync(
            "Page.navigate",
            new CdpNavigateParams(url),
            ct).ConfigureAwait(false);

        const string waitReadyJs = """
            new Promise((resolve) => {
                const isFbOrigin = () => window.location.hostname.endsWith('facebook.com');
                const hasCometTokens = () => {
                    if (!isFbOrigin()) return false;
                    try {
                        if (typeof window.require === 'function') {
                            const dtsg = window.require('DTSGInitialData') || window.require('DTSGInitData') || window.require('DTSG');
                            if (dtsg && (dtsg.token || typeof dtsg.getToken === 'function')) return true;
                        }
                    } catch {}
                    if (document.querySelector && document.querySelector('input[name="fb_dtsg"]')) return true;
                    const html = document.documentElement ? document.documentElement.innerHTML : '';
                    return html.includes('DTSGInitialData') || html.includes('"fb_dtsg"');
                };

                if (hasCometTokens()) {
                    resolve(true);
                    return;
                }

                let settled = false;
                const finish = () => {
                    if (settled) return;
                    settled = true;
                    clearInterval(timer);
                    document.removeEventListener('DOMContentLoaded', onDomReady);
                    window.removeEventListener('load', onLoad);
                    resolve(true);
                };

                const onDomReady = () => {
                    if (hasCometTokens()) finish();
                    else setTimeout(finish, 250);
                };
                const onLoad = () => finish();

                document.addEventListener('DOMContentLoaded', onDomReady, { once: true });
                window.addEventListener('load', onLoad, { once: true });

                const timer = setInterval(() => {
                    if (hasCometTokens() || (isFbOrigin() && document.readyState === 'complete')) {
                        finish();
                    }
                }, 80);

                setTimeout(finish, 5000);
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

        if (this._isDetached && this.CurrentMaterial is not null)
        {
            return this.CurrentMaterial;
        }

        using JsonDocument cookieDoc = await this._cdp.SendCommandAsync(
            "Network.getCookies",
            new CdpGetCookiesParams(s_baseOriginUrls),
            ct).ConfigureAwait(false);

        string cookieHeader = BuildCookieHeader(cookieDoc.RootElement, out string? cookieUid);

        const string extractDomJs = """
            (() => {
                const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
                const rand6 = () => Array.from({length: 6}, () => chars[Math.floor(Math.random() * chars.length)]).join('');
                const tryReq = (mod) => {
                    try { return typeof window.require === 'function' ? window.require(mod) : null; }
                    catch { return null; }
                };
                try {
                    const html = (document && document.documentElement) ? (document.documentElement.innerHTML || '') : '';
                    const userMod = tryReq('CurrentUserInitialData') || tryReq('CurrentUser');
                    const dtsgMod = tryReq('DTSGInitialData') || tryReq('DTSGInitData') || tryReq('DTSG');
                    const lsdMod = tryReq('LSD');
                    const siteMod = tryReq('SiteData');

                    let reqDtsg = '';
                    if (dtsgMod) {
                        if (typeof dtsgMod.token === 'string' && dtsgMod.token) reqDtsg = dtsgMod.token;
                        else if (typeof dtsgMod.getToken === 'function') {
                            try { reqDtsg = dtsgMod.getToken() || ''; } catch {}
                        }
                    }
                    let domDtsg = '';
                    try { domDtsg = document.querySelector('input[name="fb_dtsg"]')?.value || ''; } catch {}
                    const dtsg1 = html.match(/DTSGInitialData["\]]*\s*,\s*\[\]\s*,\s*\{[^}]*"token"\s*:\s*"([^"]+)"/)
                        || html.match(/DTSGInitData["\]]*\s*,\s*\[\]\s*,\s*\{[^}]*"token"\s*:\s*"([^"]+)"/)
                        || html.match(/"fb_dtsg"\s*:\s*"([^"]+)"/);
                    const dtsg2 = html.match(/name="fb_dtsg"\s+value="([^"]+)"/);

                    const reqLsd = (lsdMod && typeof lsdMod.token === 'string') ? lsdMod.token : '';
                    const lsd = html.match(/"LSD"\s*,\s*\[\]\s*,\s*\{[^}]*"token"\s*:\s*"([^"]+)"/);

                    const reqRev = (siteMod && siteMod.client_revision) ? String(siteMod.client_revision) : '';
                    const rev = html.match(/"client_revision"\s*:\s*(\d+)/);

                    const reqHs = (siteMod && typeof siteMod.haste_session === 'string') ? siteMod.haste_session : '';
                    const hs = html.match(/"haste_session"\s*:\s*"([^"]+)"/);

                    let reqUid = '';
                    if (userMod) {
                        try {
                            const candidate = userMod.USER_ID || userMod.ACCOUNT_ID || (typeof userMod.getID === 'function' ? userMod.getID() : '');
                            if (candidate && String(candidate) !== '0') reqUid = String(candidate);
                        } catch {}
                    }
                    let cookieStr = '';
                    try { cookieStr = document.cookie || ''; } catch {}
                    const uidMatch = cookieStr.match(/(?:^|;\s*)(?:i_user|c_user)=(\d+)/)
                        || html.match(/"USER_ID"\s*:\s*"(\d+)"/)
                        || html.match(/"ACCOUNT_ID"\s*:\s*"(\d+)"/)
                        || html.match(/"actorID"\s*:\s*"(\d+)"/);

                    const resolvedUid = reqUid || (uidMatch && uidMatch[1] !== '0' ? uidMatch[1] : '');
                    const resolvedDtsg = reqDtsg || domDtsg || (dtsg1 ? dtsg1[1] : (dtsg2 ? dtsg2[1] : ''));
                    const resolvedLsd = reqLsd || (lsd ? lsd[1] : '');
                    const resolvedRev = reqRev || (rev ? rev[1] : '1020000000');
                    const resolvedHs = reqHs || (hs ? hs[1] : '20745.HYP:comet_pkg.2.1...1');

                    return JSON.stringify({
                        uid: resolvedUid,
                        fbDtsg: resolvedDtsg,
                        lsd: resolvedLsd,
                        clientRevision: resolvedRev,
                        hasteSession: resolvedHs,
                        webSessionId: rand6() + ':' + rand6() + ':' + rand6(),
                        userAgent: navigator.userAgent || ''
                    });
                } catch {
                    return JSON.stringify({
                        uid: '',
                        fbDtsg: '',
                        lsd: '',
                        clientRevision: '1020000000',
                        hasteSession: '20745.HYP:comet_pkg.2.1...1',
                        webSessionId: rand6() + ':' + rand6() + ':' + rand6(),
                        userAgent: (typeof navigator !== 'undefined' && navigator.userAgent) ? navigator.userAgent : ''
                    });
                }
            })()
            """;

        DomMaterialSnapshot? snapshot = null;
        try
        {
            string snapshotJson = await this.EvaluateStringAsync(extractDomJs, awaitPromise: false, ct).ConfigureAwait(false);
            snapshot = JsonSerializer.Deserialize(snapshotJson, BrowserJsonContext.Default.DomMaterialSnapshot);
        }
        catch (ProtocolException)
        {
            // Browser frame may still be committing navigation; fallback below will handle token extraction
        }

        snapshot ??= new DomMaterialSnapshot(
            Uid: string.Empty,
            FbDtsg: string.Empty,
            Lsd: string.Empty,
            ClientRevision: "1020000000",
            HasteSession: "20745.HYP:comet_pkg.2.1...1",
            WebSessionId: "abc123:def456:ghi789",
            UserAgent: this._options?.UserAgent ?? ProtocolConstants.Protocol.DefaultWindowsUserAgent);

        string resolvedUid = !string.IsNullOrWhiteSpace(cookieUid) && cookieUid != "0"
            ? cookieUid
            : snapshot.Uid;

        if (string.IsNullOrWhiteSpace(resolvedUid) || resolvedUid == "0")
        {
            throw new CheckpointException(
                this.AccountUid,
                "Authenticated session tokens (c_user or fb_dtsg) were not found in the active browser tab.");
        }

        string resolvedFbDtsg = snapshot.FbDtsg;
        if (string.IsNullOrWhiteSpace(resolvedFbDtsg))
        {
            // Poll the active browser tab while Comet hydrates DTSGInitialData
            for (int attempt = 0; attempt < 8 && string.IsNullOrWhiteSpace(resolvedFbDtsg); attempt++)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                try
                {
                    string retryJson = await this.EvaluateStringAsync(extractDomJs, awaitPromise: false, ct).ConfigureAwait(false);
                    DomMaterialSnapshot? retrySnapshot = JsonSerializer.Deserialize(retryJson, BrowserJsonContext.Default.DomMaterialSnapshot);
                    if (retrySnapshot is not null && !string.IsNullOrWhiteSpace(retrySnapshot.FbDtsg))
                    {
                        snapshot = retrySnapshot;
                        resolvedFbDtsg = retrySnapshot.FbDtsg;
                        break;
                    }
                }
                catch (ProtocolException)
                {
                    // Continue polling while navigation commits
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedFbDtsg) && !string.IsNullOrWhiteSpace(cookieHeader))
        {
            // If the browser tab was still on about:blank or before cookies were injected, fetch real tokens via direct HTTP GET
            DomMaterialSnapshot? httpSnapshot = await this.TryFetchMaterialFromHttpHtmlAsync(cookieHeader, snapshot.UserAgent, ct).ConfigureAwait(false);
            if (httpSnapshot is not null && !string.IsNullOrWhiteSpace(httpSnapshot.FbDtsg))
            {
                snapshot = httpSnapshot;
                resolvedFbDtsg = httpSnapshot.FbDtsg;
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedFbDtsg))
        {
            resolvedFbDtsg = string.Concat("AQH_", resolvedUid);
        }

        string jazoest = ProtocolCodec.ComputeJazoest(resolvedFbDtsg.AsSpan());

        SessionMaterial material = new(
            Uid: resolvedUid,
            CookieHeader: cookieHeader,
            FbDtsg: resolvedFbDtsg,
            Jazoest: jazoest,
            Lsd: snapshot.Lsd,
            ClientRevision: snapshot.ClientRevision,
            HasteSession: snapshot.HasteSession,
            WebSessionId: snapshot.WebSessionId,
            UserAgent: snapshot.UserAgent,
            ExtractedAt: DateTimeOffset.UtcNow);

        this.CurrentMaterial = material;

        if (this._options is { TerminateProcessAfterExtract: true })
        {
            await this.DetachBrowserProcessAsync(ct).ConfigureAwait(false);
        }

        return material;
    }

    /// <inheritdoc />
    public async Task<int> ImportCookiesAsync(string rawCookieHeader, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        if (string.IsNullOrWhiteSpace(rawCookieHeader))
        {
            return 0;
        }

        List<CdpCookie> cookies = ParseCookieHeader(rawCookieHeader.AsSpan());
        if (cookies.Count == 0)
        {
            return 0;
        }

        if (this._isDetached)
        {
            if (this.CurrentMaterial is not null)
            {
                this.CurrentMaterial = this.CurrentMaterial with { CookieHeader = rawCookieHeader };
            }

            return cookies.Count;
        }

        using JsonDocument setDoc = await this._cdp.SendCommandAsync(
            "Network.setCookies",
            new CdpSetCookiesParams(cookies),
            ct).ConfigureAwait(false);

        return cookies.Count;
    }

    /// <summary>
    /// Parses a raw semicolon-delimited HTTP Cookie header into <see cref="CdpCookie"/> entries for <c>.facebook.com</c>.
    /// </summary>
    public static List<CdpCookie> ParseCookieHeader(ReadOnlySpan<char> rawCookieHeader)
    {
        List<CdpCookie> list = new();
        int start = 0;

        while (start < rawCookieHeader.Length)
        {
            int semiIdx = rawCookieHeader[start..].IndexOf(';');
            ReadOnlySpan<char> segment = semiIdx >= 0
                ? rawCookieHeader.Slice(start, semiIdx).Trim()
                : rawCookieHeader[start..].Trim();

            start = semiIdx >= 0 ? start + semiIdx + 1 : rawCookieHeader.Length;

            if (segment.IsEmpty)
            {
                continue;
            }

            int eqIdx = segment.IndexOf('=');
            if (eqIdx <= 0)
            {
                continue;
            }

            string name = segment[..eqIdx].Trim().ToString();
            string val = segment[(eqIdx + 1)..].Trim().ToString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            bool isHttpOnly = string.Equals(name, "xs", StringComparison.Ordinal) ||
                              string.Equals(name, "datr", StringComparison.Ordinal) ||
                              string.Equals(name, "fr", StringComparison.Ordinal) ||
                              string.Equals(name, "sb", StringComparison.Ordinal) ||
                              string.Equals(name, "ps_l", StringComparison.Ordinal) ||
                              string.Equals(name, "ps_n", StringComparison.Ordinal);

            list.Add(new CdpCookie(
                Name: name,
                Value: val,
                Domain: ".facebook.com",
                Path: "/",
                HttpOnly: isHttpOnly,
                Secure: true));
        }

        return list;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.DisposeDetachedHttpClient();
        await this._cdp.DisposeAsync().ConfigureAwait(false);
        await this._process.DisposeAsync().ConfigureAwait(false);

        if (this._options is { CleanDiskCache: true } && !string.IsNullOrWhiteSpace(this._options.UserDataDir))
        {
            _ = BrowserLauncherHelpers.CleanProfileDiskCache(this._options.UserDataDir);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.DisposeDetachedHttpClient();
        this._cdp.Dispose();
        this._process.Dispose();

        if (this._options is { CleanDiskCache: true } && !string.IsNullOrWhiteSpace(this._options.UserDataDir))
        {
            _ = BrowserLauncherHelpers.CleanProfileDiskCache(this._options.UserDataDir);
        }
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

        HashSet<string> seenNames = new(StringComparer.Ordinal);
        StringBuilder sb = new(512);
        foreach (JsonElement cookie in cookiesEl.EnumerateArray())
        {
            string? name = cookie.TryGetProperty("name"u8, out JsonElement nEl) ? nEl.GetString() : null;
            string? val = cookie.TryGetProperty("value"u8, out JsonElement vEl) ? vEl.GetString() : null;
            if (string.IsNullOrEmpty(name) || val is null)
            {
                continue;
            }

            if (string.Equals(name, "c_user", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(val) && val != "0")
            {
                cUser ??= val;
            }
            else if (string.Equals(name, "i_user", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(val) && val != "0")
            {
                cUser = val;
            }

            if (!seenNames.Add(name))
            {
                continue;
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
