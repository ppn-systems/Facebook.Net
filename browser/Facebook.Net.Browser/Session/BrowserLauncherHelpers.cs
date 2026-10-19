// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
/// Shared helper routines for building stealth Chromium command-line arguments, allocating free ports,
/// and resolving the page-level CDP WebSocket URL.
/// </summary>
public static class BrowserLauncherHelpers
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    /// <summary>
    /// Allocates an available local ephemeral TCP port on <c>127.0.0.1</c>.
    /// </summary>
    public static int AllocateFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Builds the list of stealth command-line arguments for launching Chromium/Edge without automation flags.
    /// </summary>
    public static List<string> BuildStealthArguments(BrowserLaunchOptions options, int debuggingPort)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> args = new(16)
        {
            string.Concat("--remote-debugging-port=", debuggingPort.ToString(CultureInfo.InvariantCulture)),
            string.Concat("--user-data-dir=", Path.GetFullPath(options.UserDataDir)),
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-infobars",
            "--disable-popup-blocking",
            string.Concat(
                "--window-size=",
                options.WindowWidth.ToString(CultureInfo.InvariantCulture),
                ",",
                options.WindowHeight.ToString(CultureInfo.InvariantCulture))
        };

        if (options.WindowX.HasValue && options.WindowY.HasValue)
        {
            args.Add(string.Concat(
                "--window-position=",
                options.WindowX.Value.ToString(CultureInfo.InvariantCulture),
                ",",
                options.WindowY.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            args.Add(string.Concat("--user-agent=", options.UserAgent));
        }

        if (options.Proxy is not null)
        {
            string scheme = options.Proxy.Protocol == ProxyProtocol.Socks5 ? "socks5" : "http";
            args.Add(string.Concat(
                "--proxy-server=",
                scheme,
                "://",
                options.Proxy.Host,
                ":",
                options.Proxy.Port.ToString(CultureInfo.InvariantCulture)));
        }

        if (options.Headless)
        {
            args.Add("--headless=new");
        }

        args.Add(ProtocolConstants.Endpoints.HomeEnglishUrl);
        return args;
    }

    /// <summary>
    /// Polls <c>http://127.0.0.1:{port}/json/list</c> until the initial page target is ready and returns its WebSocket debugger URI.
    /// </summary>
    public static async Task<Uri> WaitForPageDebuggerUriAsync(int debuggingPort, Process process, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(process);

        string listUrl = string.Concat("http://127.0.0.1:", debuggingPort.ToString(CultureInfo.InvariantCulture), "/json/list");
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        while (!timeoutCts.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                throw new ProtocolException($"Browser process (PID {process.Id}) exited prematurely with code {process.ExitCode}.");
            }

            try
            {
                string json = await s_httpClient.GetStringAsync(listUrl, timeoutCts.Token).ConfigureAwait(false);
                CdpTargetInfo[]? targets = JsonSerializer.Deserialize(json, BrowserJsonContext.Default.CdpTargetInfoArray);
                if (targets is not null)
                {
                    foreach (CdpTargetInfo target in targets)
                    {
                        if (string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) &&
                            Uri.TryCreate(target.WebSocketDebuggerUrl, UriKind.Absolute, out Uri? wsUri))
                        {
                            return wsUri;
                        }
                    }
                }
            }
            catch (Exception) when (!timeoutCts.IsCancellationRequested)
            {
                // Port not listening yet; wait and retry
            }

            await Task.Delay(200, timeoutCts.Token).ConfigureAwait(false);
        }

        throw new ProtocolException($"Timed out waiting for CDP endpoint on port {debuggingPort}.");
    }
}

/// <summary>
/// Default cross-platform implementation of <see cref="IBrowserProcess"/> wrapping a <see cref="Process"/>.
/// </summary>
public sealed class ManagedBrowserProcess : IBrowserProcess
{
    private readonly Process _process;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedBrowserProcess"/> class.
    /// </summary>
    public ManagedBrowserProcess(Process process, Uri debuggerWebSocketUri)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(debuggerWebSocketUri);

        this._process = process;
        this.ProcessId = process.Id;
        this.DebuggerWebSocketUri = debuggerWebSocketUri;
    }

    /// <inheritdoc />
    public int ProcessId { get; }

    /// <inheritdoc />
    public Uri DebuggerWebSocketUri { get; }

    /// <inheritdoc />
    public bool HasExited => this._isDisposed || this._process.HasExited;

    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken ct = default)
    {
        this.KillSafe();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.KillSafe();
        this._process.Dispose();
    }

    private void KillSafe()
    {
        try
        {
            if (!this._process.HasExited)
            {
                this._process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore if process already exited
        }
    }
}
