// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Browser.Protocol;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Contracts.Serialization;

namespace Facebook.Net.Browser.Session;

public sealed partial class ChromiumSession
{
    private HttpClient? _detachedHttpClient;

    /// <inheritdoc />
    public async Task<string> ExecuteInPagePostAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        string formUrlEncodedBody,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(formUrlEncodedBody);

        if (_isDetached || !this.IsBrowserProcessAttached || (!_isOnFacebookOrigin && this.CurrentMaterial is not null))
        {
            return await this.ExecuteDetachedHttpPostAsync(url, headers, formUrlEncodedBody, ct).ConfigureAwait(false);
        }

        if (string.Equals(formUrlEncodedBody, "__SSR_RELAY_JSON__", StringComparison.Ordinal))
        {
            string ssrJs = """
                (() => {
                    const scripts = Array.from(document.querySelectorAll('script[type="application/json"]'));
                    const lines = [];
                    for (const s of scripts) {
                        const t = s.textContent;
                        if (t && (t.includes('"post_id"') || t.includes('"Story"') || t.includes('"comet_sections"') || t.includes('"page_info"') || t.includes('"Group"') || t.includes('"viewer_join_state"') || t.includes('"timeline_list_feed_units"'))) {
                            lines.push(t.trim());
                        }
                    }
                    return lines.join('\n');
                })()
                """;
            try
            {
                string domLines = await this.EvaluateStringAsync(ssrJs, awaitPromise: false, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(domLines))
                {
                    return domLines;
                }
            }
            catch (ProtocolException) when (!ct.IsCancellationRequested)
            {
                // Fall back to HTTP GET extraction below
            }

            return await this.ExecuteDetachedHttpPostAsync(url, headers, formUrlEncodedBody, ct).ConfigureAwait(false);
        }

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

        try
        {
            return await this.EvaluateStringAsync(fetchJs, awaitPromise: true, ct).ConfigureAwait(false);
        }
        catch (ProtocolException) when (this.CurrentMaterial is not null && !ct.IsCancellationRequested)
        {
            _isOnFacebookOrigin = false;
            return await this.ExecuteDetachedHttpPostAsync(url, headers, formUrlEncodedBody, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string> UploadViaDomBridgeAsync(
        string uploadUrl,
        string localFilePath,
        string fileFieldName = "file",
        IReadOnlyDictionary<string, string>? additionalFields = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local file for DOM bridge upload was not found.", localFilePath);
        }

        if (_isDetached || !this.IsBrowserProcessAttached || (!_isOnFacebookOrigin && this.CurrentMaterial is not null))
        {
            return await this.ExecuteDetachedMultipartUploadAsync(uploadUrl, localFilePath, fileFieldName, additionalFields, ct).ConfigureAwait(false);
        }

        string bridgeId = string.Concat("__fb_bridge_", Guid.NewGuid().ToString("N"));

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

        _ = await _cdp.SendCommandAsync(
            "Runtime.evaluate",
            new CdpEvaluateParams(createInputJs, AwaitPromise: false, ReturnByValue: true),
            ct).ConfigureAwait(false);

        try
        {
            int rootNodeId = await this.GetDocumentRootNodeIdAsync(ct).ConfigureAwait(false);
            int inputNodeId = await this.QuerySelectorNodeIdAsync(rootNodeId, string.Concat("#", bridgeId), ct).ConfigureAwait(false);

            List<string> fileList = new(1) { Path.GetFullPath(localFilePath) };
            using JsonDocument setFilesDoc = await _cdp.SendCommandAsync(
                "DOM.setFileInputFiles",
                new CdpSetFileInputFilesParams(fileList, inputNodeId),
                ct).ConfigureAwait(false);

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
        catch (ProtocolException) when (this.CurrentMaterial is not null && !ct.IsCancellationRequested)
        {
            _isOnFacebookOrigin = false;
            return await this.ExecuteDetachedMultipartUploadAsync(uploadUrl, localFilePath, fileFieldName, additionalFields, ct).ConfigureAwait(false);
        }
        finally
        {
            string cleanupJs = $"(() => {{ const el = document.getElementById('{bridgeId}'); if (el) el.remove(); return true; }})()";
            try
            {
                using JsonDocument cleanupDoc = await _cdp.SendCommandAsync(
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

    private async Task<string> EvaluateStringAsync(string expression, bool awaitPromise, CancellationToken ct)
    {
        using JsonDocument doc = await _cdp.SendCommandAsync(
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
            if (exDetails.TryGetProperty("exception"u8, out JsonElement exObj) &&
                exObj.TryGetProperty("description"u8, out JsonElement descEl) &&
                descEl.GetString() is { Length: > 0 } desc)
            {
                exText = string.Concat(exText, " — ", desc);
            }

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
        using JsonDocument doc = await _cdp.SendCommandAsync(
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
        using JsonDocument doc = await _cdp.SendCommandAsync(
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

    private HttpClient GetOrCreateDetachedHttpClient()
    {
        if (_detachedHttpClient is not null)
        {
            return _detachedHttpClient;
        }

        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        if (_options?.Proxy is { } proxy)
        {
            string scheme = proxy.Protocol == ProxyProtocol.Socks5 ? "socks5" : "http";
            Uri proxyUri = new(string.Create(CultureInfo.InvariantCulture, $"{scheme}://{proxy.Host}:{proxy.Port}"));
            WebProxy webProxy = new(proxyUri);
            if (!string.IsNullOrWhiteSpace(proxy.Username))
            {
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password ?? string.Empty);
            }

            handler.Proxy = webProxy;
            handler.UseProxy = true;
        }

        _detachedHttpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        return _detachedHttpClient;
    }

    private void DisposeDetachedHttpClient()
    {
        _detachedHttpClient?.Dispose();
        _detachedHttpClient = null;
    }

    private async Task<string> ExecuteDetachedHttpPostAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        string formUrlEncodedBody,
        CancellationToken ct)
    {
        SessionMaterial material = this.CurrentMaterial
            ?? throw new ProtocolException("Cannot execute detached HTTP request before extracting SessionMaterial.");

        HttpClient client = this.GetOrCreateDetachedHttpClient();
        if (string.Equals(formUrlEncodedBody, "__SSR_RELAY_JSON__", StringComparison.Ordinal))
        {
            using HttpRequestMessage getReq = new(HttpMethod.Get, url);
            _ = getReq.Headers.TryAddWithoutValidation("User-Agent", material.UserAgent);
            _ = getReq.Headers.TryAddWithoutValidation("Cookie", material.CookieHeader);
            _ = getReq.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _ = getReq.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            _ = getReq.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            _ = getReq.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            using HttpResponseMessage getResp = await client.SendAsync(getReq, ct).ConfigureAwait(false);
            if (!getResp.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            string html = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractRelayJsonScriptsFromHtml(html);
        }

        using HttpRequestMessage req = new(HttpMethod.Post, url)
        {
            Content = new StringContent(formUrlEncodedBody, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        _ = req.Headers.TryAddWithoutValidation("User-Agent", material.UserAgent);
        _ = req.Headers.TryAddWithoutValidation("Cookie", material.CookieHeader);
        _ = req.Headers.TryAddWithoutValidation("Origin", ProtocolConstants.Endpoints.BaseOrigin);
        _ = req.Headers.TryAddWithoutValidation("Referer", string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/"));
        _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        foreach (KeyValuePair<string, string> kvp in headers)
        {
            if (string.Equals(kvp.Key, "content-type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _ = req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new ProtocolException($"Detached HTTP POST failed with status {(int)resp.StatusCode} ({resp.ReasonPhrase}).");
        }

        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteDetachedMultipartUploadAsync(
        string uploadUrl,
        string localFilePath,
        string fileFieldName,
        IReadOnlyDictionary<string, string>? additionalFields,
        CancellationToken ct)
    {
        SessionMaterial material = this.CurrentMaterial
            ?? throw new ProtocolException("Cannot execute detached file upload before extracting SessionMaterial.");

        HttpClient client = this.GetOrCreateDetachedHttpClient();
        using MultipartFormDataContent form = new();
        if (additionalFields is not null)
        {
            foreach (KeyValuePair<string, string> kvp in additionalFields)
            {
                form.Add(new StringContent(kvp.Value, Encoding.UTF8), kvp.Key);
            }
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath, ct).ConfigureAwait(false);
        ByteArrayContent fileContent = new(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, fileFieldName, Path.GetFileName(localFilePath));

        using HttpRequestMessage req = new(HttpMethod.Post, uploadUrl)
        {
            Content = form
        };

        _ = req.Headers.TryAddWithoutValidation("User-Agent", material.UserAgent);
        _ = req.Headers.TryAddWithoutValidation("Cookie", material.CookieHeader);
        _ = req.Headers.TryAddWithoutValidation("Origin", ProtocolConstants.Endpoints.BaseOrigin);
        _ = req.Headers.TryAddWithoutValidation("Referer", string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/"));

        using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new ProtocolException($"Detached upload failed with status {(int)resp.StatusCode} ({resp.ReasonPhrase}).");
        }

        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<DomMaterialSnapshot?> TryFetchMaterialFromHttpHtmlAsync(
        string cookieHeader,
        string? userAgent,
        CancellationToken ct)
    {
        try
        {
            HttpClient client = this.GetOrCreateDetachedHttpClient();
            string resolvedUa = !string.IsNullOrWhiteSpace(userAgent)
                ? userAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent;

            using HttpRequestMessage req = new(HttpMethod.Get, string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/"));
            _ = req.Headers.TryAddWithoutValidation("User-Agent", resolvedUa);
            _ = req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            _ = req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            _ = req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");

            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Match dtsgMatch = DtsgInitialDataRegex().Match(html);
            if (!dtsgMatch.Success)
            {
                dtsgMatch = FbDtsgPropertyRegex().Match(html);
            }

            if (!dtsgMatch.Success || string.IsNullOrWhiteSpace(dtsgMatch.Groups[1].Value))
            {
                return null;
            }

            Match lsdMatch = LsdTokenRegex().Match(html);
            Match revMatch = ClientRevisionRegex().Match(html);
            Match hsMatch = HasteSessionRegex().Match(html);
            Match uidMatch = UserIdRegex().Match(html);
            string resolvedUid = uidMatch.Success ? uidMatch.Groups[1].Value : this.AccountUid;

            Match nameMatch = CurrentUserNameRegex().Match(html);
            if (!nameMatch.Success)
            {
                nameMatch = FallbackNamePropertyRegex().Match(html);
            }

            string? displayName = null;
            if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
            {
                displayName = UnescapeJsonString(nameMatch.Groups[1].Value);
            }

            string? avatarUrl = null;
            if (!string.IsNullOrWhiteSpace(resolvedUid))
            {
                Match picMatch = Regex.Match(
                    html,
                    @"""" + Regex.Escape(resolvedUid) + @"""[^}]{0,400}""profile_picture""\s*:\s*\{\s*""uri""\s*:\s*""([^""]+)""",
                    RegexOptions.CultureInvariant);
                if (!picMatch.Success)
                {
                    picMatch = ViewerProfilePictureRegex().Match(html);
                }

                if (picMatch.Success && !string.IsNullOrWhiteSpace(picMatch.Groups[1].Value))
                {
                    avatarUrl = UnescapeJsonString(picMatch.Groups[1].Value);
                }
            }

            return new DomMaterialSnapshot(
                Uid: resolvedUid,
                FbDtsg: dtsgMatch.Groups[1].Value,
                Lsd: lsdMatch.Success ? lsdMatch.Groups[1].Value : string.Empty,
                ClientRevision: revMatch.Success ? revMatch.Groups[1].Value : "1020000000",
                HasteSession: hsMatch.Success ? hsMatch.Groups[1].Value : "20745.HYP:comet_pkg.2.1...1",
                WebSessionId: string.Concat(Guid.NewGuid().ToString("N")[..6], ":", Guid.NewGuid().ToString("N")[..6], ":", Guid.NewGuid().ToString("N")[..6]),
                UserAgent: resolvedUa,
                DisplayName: displayName,
                AvatarUrl: avatarUrl);
        }
        catch
        {
            return null;
        }
    }

    private static string UnescapeJsonString(string rawEscaped)
    {
        try
        {
            return JsonSerializer.Deserialize(string.Concat("\"", rawEscaped, "\""), ContractsJsonContext.Default.String) ?? rawEscaped;
        }
        catch
        {
            return rawEscaped.Replace("\\/", "/", StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"""CurrentUserInitialData""\s*,\s*\[\]\s*,\s*\{[^}]*""NAME""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentUserNameRegex();

    [GeneratedRegex(@"""NAME""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex FallbackNamePropertyRegex();

    [GeneratedRegex(@"""viewer""\s*:\s*\{[^}]{0,400}""profile_picture""\s*:\s*\{\s*""uri""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ViewerProfilePictureRegex();

    [GeneratedRegex(@"DTSGInit(?:ial)?Data[""\]]*\s*,\s*\[\]\s*,\s*\{[^}]*""token""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex DtsgInitialDataRegex();

    [GeneratedRegex(@"""fb_dtsg""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex FbDtsgPropertyRegex();

    [GeneratedRegex(@"""LSD""\s*,\s*\[\]\s*,\s*\{[^}]*""token""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex LsdTokenRegex();

    [GeneratedRegex(@"""client_revision""\s*:\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ClientRevisionRegex();

    [GeneratedRegex(@"""haste_session""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex HasteSessionRegex();

    [GeneratedRegex(@"""USER_ID""\s*:\s*""(\d+)""", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();

    [GeneratedRegex(@"<script\b[^>]*type=""application/json""[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationJsonScriptRegex();

    private static string ExtractRelayJsonScriptsFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        StringBuilder sb = new(4096);
        MatchCollection matches = ApplicationJsonScriptRegex().Matches(html);
        for (int i = 0; i < matches.Count; i++)
        {
            string json = matches[i].Groups[1].Value.Trim();
            if (json.Length > 2 &&
                (json.Contains("\"post_id\"", StringComparison.Ordinal) ||
                 json.Contains("\"Story\"", StringComparison.Ordinal) ||
                 json.Contains("\"comet_sections\"", StringComparison.Ordinal) ||
                 json.Contains("\"page_info\"", StringComparison.Ordinal) ||
                 json.Contains("\"Group\"", StringComparison.Ordinal) ||
                 json.Contains("\"viewer_join_state\"", StringComparison.Ordinal) ||
                 json.Contains("\"timeline_list_feed_units\"", StringComparison.Ordinal)))
            {
                if (sb.Length > 0)
                {
                    _ = sb.Append('\n');
                }

                _ = sb.Append(json);
            }
        }

        return sb.ToString();
    }
}
