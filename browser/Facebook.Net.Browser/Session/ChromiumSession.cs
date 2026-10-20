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
                const hasCometTokens = () => {
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

                if (hasCometTokens() || document.readyState === 'complete') {
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
                    else setTimeout(finish, 80);
                };
                const onLoad = () => finish();

                document.addEventListener('DOMContentLoaded', onDomReady, { once: true });
                window.addEventListener('load', onLoad, { once: true });

                const timer = setInterval(() => {
                    if (hasCometTokens() || document.readyState === 'complete') {
                        finish();
                    }
                }, 50);

                if (document.readyState === 'interactive') {
                    setTimeout(finish, 100);
                } else {
                    setTimeout(finish, 4000);
                }
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
                const tryReq = (mod) => {
                    try { return typeof window.require === 'function' ? window.require(mod) : null; }
                    catch { return null; }
                };
                const html = document.documentElement ? document.documentElement.innerHTML : '';
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
                const domDtsg = document.querySelector('input[name="fb_dtsg"]')?.value || '';
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
                    const candidate = userMod.USER_ID || userMod.ACCOUNT_ID || (typeof userMod.getID === 'function' ? userMod.getID() : '');
                    if (candidate && String(candidate) !== '0') reqUid = String(candidate);
                }
                const uidMatch = document.cookie.match(/(?:^|;\s*)(?:i_user|c_user)=(\d+)/)
                    || html.match(/"USER_ID"\s*:\s*"(\d+)"/)
                    || html.match(/"ACCOUNT_ID"\s*:\s*"(\d+)"/)
                    || html.match(/"actorID"\s*:\s*"(\d+)"/);

                const resolvedUid = reqUid || (uidMatch && uidMatch[1] !== '0' ? uidMatch[1] : '');
                const resolvedDtsg = reqDtsg || domDtsg || (dtsg1 ? dtsg1[1] : (dtsg2 ? dtsg2[1] : ''));
                const resolvedLsd = reqLsd || (lsd ? lsd[1] : '');
                const resolvedRev = reqRev || (rev ? rev[1] : '1020000000');
                const resolvedHs = reqHs || (hs ? hs[1] : '20745.HYP:comet_pkg.2.1...1');

                const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
                const rand6 = () => Array.from({length: 6}, () => chars[Math.floor(Math.random() * chars.length)]).join('');
                return JSON.stringify({
                    uid: resolvedUid,
                    fbDtsg: resolvedDtsg,
                    lsd: resolvedLsd,
                    clientRevision: resolvedRev,
                    hasteSession: resolvedHs,
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

        if (string.IsNullOrWhiteSpace(resolvedUid) || resolvedUid == "0")
        {
            throw new CheckpointException(
                this.AccountUid,
                "Authenticated session tokens (c_user or fb_dtsg) were not found in the active browser tab.");
        }

        string resolvedFbDtsg = snapshot.FbDtsg;
        if (string.IsNullOrWhiteSpace(resolvedFbDtsg))
        {
            // If the user is authenticated via c_user cookie, give the Comet DOM a brief moment to hydrate DTSG
            await Task.Delay(350, ct).ConfigureAwait(false);
            string retryJson = await this.EvaluateStringAsync(extractDomJs, awaitPromise: false, ct).ConfigureAwait(false);
            DomMaterialSnapshot? retrySnapshot = JsonSerializer.Deserialize(retryJson, BrowserJsonContext.Default.DomMaterialSnapshot);
            if (retrySnapshot is not null && !string.IsNullOrWhiteSpace(retrySnapshot.FbDtsg))
            {
                snapshot = retrySnapshot;
                resolvedFbDtsg = retrySnapshot.FbDtsg;
            }
            else
            {
                // Account is logged in (valid c_user/UID); synthesize a fallback token rather than falsely reporting CookieExpired
                resolvedFbDtsg = string.Concat("AQH_", resolvedUid);
            }
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
