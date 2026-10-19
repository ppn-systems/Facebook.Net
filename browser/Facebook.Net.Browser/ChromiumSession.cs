// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts;
using Facebook.Net.Contracts.Exceptions;

namespace Facebook.Net.Browser;

/// <summary>
/// Active Chromium browser session implementing <see cref="IBrowserSession"/> via Chrome DevTools Protocol (CDP).
/// Provides In-Page Request Delegation, Contextual Navigation, and Hidden DOM File Bridge.
/// </summary>
public sealed class ChromiumSession : IBrowserSession
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

        // Wait for document readyState == 'complete' inside the page
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

        // 1. Fetch all cookies (including HttpOnly xs, datr, fr) via Network.getCookies
        using JsonDocument cookieDoc = await this._cdp.SendCommandAsync(
            "Network.getCookies",
            new CdpGetCookiesParams(s_baseOriginUrls),
            ct).ConfigureAwait(false);

        string cookieHeader = BuildCookieHeader(cookieDoc.RootElement, out string? cookieUid);

        // 2. Extract dynamic tokens from live DOM
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
    public async Task<string> ExecuteInPagePostAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        string formUrlEncodedBody,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(formUrlEncodedBody);

        string headersJson = JsonSerializer.Serialize(headers, BrowserJsonContext.Default.IReadOnlyDictionaryStringString);
        string escapedUrl = JsonSerializer.Serialize(url, ContractsJsonContext.Default.String);
        string escapedBody = JsonSerializer.Serialize(formUrlEncodedBody, ContractsJsonContext.Default.String);

        string fetchJs = $$"""
            (async () => {
                const response = await fetch({{escapedUrl}}, {
                    method: 'POST',
                    headers: {{headersJson}},
                    body: {{escapedBody}},
                    credentials: 'include'
                });
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return await response.text();
            })()
            """;

        return await this.EvaluateStringAsync(fetchJs, awaitPromise: true, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> UploadViaDomBridgeAsync(
        string uploadUrl,
        string localFilePath,
        string fileFieldName = "file",
        IReadOnlyDictionary<string, string>? additionalFields = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local file for DOM bridge upload was not found.", localFilePath);
        }

        string bridgeId = string.Concat("__fb_bridge_", Guid.NewGuid().ToString("N"));

        // 1. Inject hidden <input type="file"> element into DOM
        string createInputJs = $$"""
            (() => {
                const input = document.createElement('input');
                input.type = 'file';
                input.id = '{{bridgeId}}';
                input.style.display = 'none';
                document.body.appendChild(input);
                return true;
            })()
            """;

        _ = await this._cdp.SendCommandAsync(
            "Runtime.evaluate",
            new CdpEvaluateParams(createInputJs, AwaitPromise: false, ReturnByValue: true),
            ct).ConfigureAwait(false);

        try
        {
            // 2. Locate DOM NodeId and attach the local file via CDP DOM.setFileInputFiles
            int rootNodeId = await this.GetDocumentRootNodeIdAsync(ct).ConfigureAwait(false);
            int inputNodeId = await this.QuerySelectorNodeIdAsync(rootNodeId, string.Concat("#", bridgeId), ct).ConfigureAwait(false);

            List<string> fileList = new(1) { Path.GetFullPath(localFilePath) };
            using JsonDocument setFilesDoc = await this._cdp.SendCommandAsync(
                "DOM.setFileInputFiles",
                new CdpSetFileInputFilesParams(fileList, inputNodeId),
                ct).ConfigureAwait(false);

            // 3. Construct FormData inside the page from the attached File object and POST via fetch
            string fieldsJson = additionalFields is not null
                ? JsonSerializer.Serialize(additionalFields, BrowserJsonContext.Default.IReadOnlyDictionaryStringString)
                : "{}";
            string escapedUploadUrl = JsonSerializer.Serialize(uploadUrl, ContractsJsonContext.Default.String);
            string escapedFieldName = JsonSerializer.Serialize(fileFieldName, ContractsJsonContext.Default.String);

            string submitJs = $$"""
                (async () => {
                    const input = document.getElementById('{{bridgeId}}');
                    if (!input || !input.files || input.files.length === 0) {
                        throw new Error('DOM file bridge input has no attached file.');
                    }
                    const form = new FormData();
                    const extra = {{fieldsJson}};
                    for (const [k, v] of Object.entries(extra)) {
                        form.append(k, v);
                    }
                    form.append({{escapedFieldName}}, input.files[0]);
                    const response = await fetch({{escapedUploadUrl}}, {
                        method: 'POST',
                        body: form,
                        credentials: 'include'
                    });
                    if (!response.ok) {
                        throw new Error('Upload HTTP ' + response.status);
                    }
                    return await response.text();
                })()
                """;

            return await this.EvaluateStringAsync(submitJs, awaitPromise: true, ct).ConfigureAwait(false);
        }
        finally
        {
            // 4. Always remove the hidden DOM element
            string cleanupJs = $"(() => {{ const el = document.getElementById('{bridgeId}'); if (el) el.remove(); return true; }})()";
            try
            {
                using JsonDocument cleanupDoc = await this._cdp.SendCommandAsync(
                    "Runtime.evaluate",
                    new CdpEvaluateParams(cleanupJs, AwaitPromise: false, ReturnByValue: true),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup failure if tab closed
            }
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

    private async Task<string> EvaluateStringAsync(string expression, bool awaitPromise, CancellationToken ct)
    {
        using JsonDocument doc = await this._cdp.SendCommandAsync(
            "Runtime.evaluate",
            new CdpEvaluateParams(expression, awaitPromise, ReturnByValue: true),
            ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("result"u8, out JsonElement resultOuter))
        {
            throw new ProtocolException("CDP Runtime.evaluate response missing 'result' property.");
        }

        if (resultOuter.TryGetProperty("exceptionDetails"u8, out JsonElement exDetails))
        {
            string exText = exDetails.TryGetProperty("text"u8, out JsonElement tEl)
                ? tEl.GetString() ?? "JS exception"
                : "JS exception";
            throw new ProtocolException($"In-page JS evaluation failed: {exText}");
        }

        if (resultOuter.TryGetProperty("result"u8, out JsonElement innerResult) &&
            innerResult.TryGetProperty("value"u8, out JsonElement valueEl))
        {
            return valueEl.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<int> GetDocumentRootNodeIdAsync(CancellationToken ct)
    {
        using JsonDocument doc = await this._cdp.SendCommandAsync(
            "DOM.getDocument",
            new CdpGetDocumentParams(Depth: 1),
            ct).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("result"u8, out JsonElement resEl) &&
            resEl.TryGetProperty("root"u8, out JsonElement rootEl) &&
            rootEl.TryGetProperty("nodeId"u8, out JsonElement nodeIdEl) &&
            nodeIdEl.TryGetInt32(out int nodeId))
        {
            return nodeId;
        }

        throw new ProtocolException("Failed to retrieve DOM document root nodeId.");
    }

    private async Task<int> QuerySelectorNodeIdAsync(int rootNodeId, string selector, CancellationToken ct)
    {
        using JsonDocument doc = await this._cdp.SendCommandAsync(
            "DOM.querySelector",
            new CdpQuerySelectorParams(rootNodeId, selector),
            ct).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("result"u8, out JsonElement resEl) &&
            resEl.TryGetProperty("nodeId"u8, out JsonElement nodeIdEl) &&
            nodeIdEl.TryGetInt32(out int nodeId) &&
            nodeId > 0)
        {
            return nodeId;
        }

        throw new ProtocolException($"DOM element matching selector '{selector}' was not found.");
    }
}
