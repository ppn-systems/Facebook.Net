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
        ObjectDisposedException.ThrowIf(this._isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(formUrlEncodedBody);

        if (this._isDetached || !this.IsBrowserProcessAttached)
        {
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

        if (this._isDetached || !this.IsBrowserProcessAttached)
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

        _ = await this._cdp.SendCommandAsync(
            "Runtime.evaluate",
            new CdpEvaluateParams(createInputJs, AwaitPromise: false, ReturnByValue: true),
            ct).ConfigureAwait(false);

        try
        {
            int rootNodeId = await this.GetDocumentRootNodeIdAsync(ct).ConfigureAwait(false);
            int inputNodeId = await this.QuerySelectorNodeIdAsync(rootNodeId, string.Concat("#", bridgeId), ct).ConfigureAwait(false);

            List<string> fileList = new(1) { Path.GetFullPath(localFilePath) };
            using JsonDocument setFilesDoc = await this._cdp.SendCommandAsync(
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
        finally
        {
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

    private HttpClient GetOrCreateDetachedHttpClient()
    {
        if (this._detachedHttpClient is not null)
        {
            return this._detachedHttpClient;
        }

        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        if (this._options?.Proxy is { } proxy)
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

        this._detachedHttpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        return this._detachedHttpClient;
    }

    private void DisposeDetachedHttpClient()
    {
        this._detachedHttpClient?.Dispose();
        this._detachedHttpClient = null;
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
}
