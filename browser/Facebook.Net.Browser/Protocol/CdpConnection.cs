// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Exceptions;

namespace Facebook.Net.Browser.Protocol;

/// <summary>
/// Lightweight, zero-dependency Chrome DevTools Protocol (CDP) WebSocket client compatible with Native AOT.
/// </summary>
public sealed class CdpConnection : IAsyncDisposable, IDisposable
{
    private readonly ClientWebSocket _webSocket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pendingRequests = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _receiveLoopTask;
    private int _nextCommandId;
    private bool _isDisposed;

    /// <summary>
    /// Gets a value indicating whether the underlying CDP WebSocket is currently open.
    /// </summary>
    public bool IsConnected => _webSocket.State == WebSocketState.Open && !_isDisposed;

    /// <summary>
    /// Connects to the specified CDP WebSocket endpoint and starts the background response multiplexer.
    /// </summary>
    /// <param name="webSocketUri">The target <c>ws://127.0.0.1:port/devtools/page/...</c> URI.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(Uri webSocketUri, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(webSocketUri);

        await _webSocket.ConnectAsync(webSocketUri, ct).ConfigureAwait(false);
        _receiveLoopTask = Task.Run(() => this.ReceiveLoopAsync(_disposeCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Sends a CDP command and awaits its correlated JSON response document.
    /// </summary>
    /// <param name="method">The CDP method name (e.g., <c>Runtime.evaluate</c>).</param>
    /// <param name="parameters">Optional strongly-typed parameters object registered in <see cref="BrowserJsonContext"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed <see cref="JsonDocument"/> containing the <c>result</c> payload.</returns>
    public async Task<JsonDocument> SendCommandAsync(string method, object? parameters = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        int id = Interlocked.Increment(ref _nextCommandId);
        TaskCompletionSource<JsonDocument> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        using CancellationTokenRegistration ctr = ct.Register(
            static state =>
            {
                (ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> dict, int reqId) =
                    ((ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>>, int))state!;
                if (dict.TryRemove(reqId, out TaskCompletionSource<JsonDocument>? pending))
                {
                    _ = pending.TrySetCanceled();
                }
            },
            (_pendingRequests, id));

        try
        {
            byte[] payloadBytes = SerializeCommand(id, method, parameters);

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _webSocket.SendAsync(
                    payloadBytes.AsMemory(),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _ = _sendLock.Release();
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _ = _pendingRequests.TryRemove(id, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _disposeCts.CancelAsync().ConfigureAwait(false);

        if (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                using CancellationTokenSource closeCts = new(TimeSpan.FromSeconds(2));
                await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore closure errors during disposal
            }
        }

        if (_receiveLoopTask is not null)
        {
            try
            {
                await _receiveLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore background task cancellation
            }
        }

        this.FailAllPending("CDP connection disposed.");
        _webSocket.Dispose();
        _sendLock.Dispose();
        _disposeCts.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCts.Cancel();
        this.FailAllPending("CDP connection disposed.");
        _webSocket.Dispose();
        _sendLock.Dispose();
        _disposeCts.Dispose();
    }

    private static byte[] SerializeCommand(int id, string method, object? parameters)
    {
        using MemoryStream ms = new(256);
        using Utf8JsonWriter writer = new(ms);

        writer.WriteStartObject();
        writer.WriteNumber("id"u8, id);
        writer.WriteString("method"u8, method);

        if (parameters is not null)
        {
            writer.WritePropertyName("params"u8);
            switch (parameters)
            {
                case CdpNavigateParams nav:
                    JsonSerializer.Serialize(writer, nav, BrowserJsonContext.Default.CdpNavigateParams);
                    break;
                case CdpEvaluateParams eval:
                    JsonSerializer.Serialize(writer, eval, BrowserJsonContext.Default.CdpEvaluateParams);
                    break;
                case CdpGetCookiesParams cookies:
                    JsonSerializer.Serialize(writer, cookies, BrowserJsonContext.Default.CdpGetCookiesParams);
                    break;
                case CdpSetCookiesParams setCookies:
                    JsonSerializer.Serialize(writer, setCookies, BrowserJsonContext.Default.CdpSetCookiesParams);
                    break;
                case CdpSetBlockedUrlsParams blockedUrls:
                    JsonSerializer.Serialize(writer, blockedUrls, BrowserJsonContext.Default.CdpSetBlockedUrlsParams);
                    break;
                case CdpGetDocumentParams doc:
                    JsonSerializer.Serialize(writer, doc, BrowserJsonContext.Default.CdpGetDocumentParams);
                    break;
                case CdpQuerySelectorParams query:
                    JsonSerializer.Serialize(writer, query, BrowserJsonContext.Default.CdpQuerySelectorParams);
                    break;
                case CdpSetFileInputFilesParams files:
                    JsonSerializer.Serialize(writer, files, BrowserJsonContext.Default.CdpSetFileInputFilesParams);
                    break;
                default:
                    throw new ProtocolException($"Unsupported CDP parameter type: {parameters.GetType().Name}");
            }
        }

        writer.WriteEndObject();
        writer.Flush();
        return ms.ToArray();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        using MemoryStream messageStream = new(65536);

        try
        {
            while (!ct.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                messageStream.SetLength(0);
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this.FailAllPending("CDP WebSocket closed by remote peer.");
                        return;
                    }

                    await messageStream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);

                if (messageStream.Length == 0)
                {
                    continue;
                }

                this.DispatchMessage(messageStream.GetBuffer().AsMemory(0, (int)messageStream.Length));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            this.FailAllPending($"CDP receive loop faulted: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void DispatchMessage(ReadOnlyMemory<byte> utf8Json)
    {
        JsonDocument doc = JsonDocument.Parse(utf8Json);
        if (!doc.RootElement.TryGetProperty("id"u8, out JsonElement idElement) ||
            !idElement.TryGetInt32(out int id))
        {
            doc.Dispose();
            return;
        }

        if (!_pendingRequests.TryRemove(id, out TaskCompletionSource<JsonDocument>? tcs))
        {
            doc.Dispose();
            return;
        }

        if (doc.RootElement.TryGetProperty("error"u8, out JsonElement errorElement))
        {
            string message = errorElement.TryGetProperty("message"u8, out JsonElement msgEl)
                ? msgEl.GetString() ?? "Unknown CDP error"
                : "Unknown CDP error";
            int? code = errorElement.TryGetProperty("code"u8, out JsonElement codeEl) && codeEl.TryGetInt32(out int c)
                ? c
                : null;
            doc.Dispose();
            _ = tcs.TrySetException(new ProtocolException($"CDP error: {message}", code));
            return;
        }

        _ = tcs.TrySetResult(doc);
    }

    private void FailAllPending(string reason)
    {
        foreach (int key in _pendingRequests.Keys)
        {
            if (_pendingRequests.TryRemove(key, out TaskCompletionSource<JsonDocument>? tcs))
            {
                _ = tcs.TrySetException(new ProtocolException(reason));
            }
        }
    }
}
