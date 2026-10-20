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
    /// Default URL wildcard patterns blocked via CDP <c>Network.setBlockedURLs</c> during automated/headless sessions
    /// to accelerate token extraction and save 75%–90% of proxy bandwidth.
    /// </summary>
    public static readonly IReadOnlyList<string> BlockedStaticAssetPatterns =
    [
        "*.jpg",
        "*.jpeg",
        "*.png",
        "*.webp",
        "*.mp4",
        "*.gif",
        "*.woff2"
    ];

    private static readonly string[] s_ephemeralCacheSubpaths =
    [
        Path.Combine("Default", "Cache"),
        Path.Combine("Default", "Code Cache"),
        Path.Combine("Default", "GPUCache"),
        "GrShaderCache",
        "ShaderCache",
        "Crashpad",
        "BrowserMetrics"
    ];

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
    /// Safely deletes ephemeral disk caches (<c>Default\Cache</c>, <c>Default\Code Cache</c>, <c>GrShaderCache</c>, <c>Crashpad</c>)
    /// inside <paramref name="userDataDir"/> without touching <c>Cookies</c>, <c>Local State</c>, or <c>Preferences</c>.
    /// </summary>
    /// <param name="userDataDir">The Chromium profile root directory.</param>
    /// <returns>The number of cache directories cleaned.</returns>
    public static int CleanProfileDiskCache(string userDataDir)
    {
        if (string.IsNullOrWhiteSpace(userDataDir) || !Directory.Exists(userDataDir))
        {
            return 0;
        }

        int cleaned = 0;
        for (int i = 0; i < s_ephemeralCacheSubpaths.Length; i++)
        {
            string targetDir = Path.Combine(userDataDir, s_ephemeralCacheSubpaths[i]);
            if (!Directory.Exists(targetDir))
            {
                continue;
            }

            try
            {
                Directory.Delete(targetDir, recursive: true);
                cleaned++;
            }
            catch
            {
                // Ignore files locked by a running browser instance
            }
        }

        return cleaned;
    }

    /// <summary>
    /// Builds the list of stealth and resource-optimized command-line arguments for launching Chromium/Edge.
    /// Separates Scenario 1 (Visible user login/checkpoint: full rendering) from Scenario 2 (Headless/Automated: low RAM, GPU off).
    /// </summary>
    public static List<string> BuildStealthArguments(BrowserLaunchOptions options, int debuggingPort)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> args = new(28)
        {
            string.Concat("--remote-debugging-port=", debuggingPort.ToString(CultureInfo.InvariantCulture)),
            string.Concat("--user-data-dir=", Path.GetFullPath(options.UserDataDir)),
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-infobars",
            "--disable-popup-blocking",
            "--disable-sync",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-domain-reliability",
            "--disable-breakpad",
            "--disable-features=msEdgeSyncConfirmation,msImplicitSignin,Translate,OptimizationHints,MediaRouter,DialMediaRouteProvider,CalculateNativeWinOcclusion,InterestFeedContentSuggestions",
            "--disk-cache-size=104857600",
            string.Concat(
                "--window-size=",
                options.WindowWidth.ToString(CultureInfo.InvariantCulture),
                ",",
                options.WindowHeight.ToString(CultureInfo.InvariantCulture))
        };

        if (options.EffectiveLowResourceMode)
        {
            args.Add("--js-flags=--max-old-space-size=256");
            args.Add("--enable-low-end-device-mode");
        }

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
            args.Add("--disable-gpu");
            args.Add("--disable-software-rasterizer");
            args.Add("--mute-audio");
        }

        args.Add(ProtocolConstants.Endpoints.HomeEnglishUrl);
        return args;
    }

    /// <summary>
    /// Persists the allocated CDP debugging port in the profile directory so subsequent commands or app restarts
    /// can seamlessly reattach to an already-open browser window for the same profile.
    /// </summary>
    public static void RecordActiveDebuggingPort(string userDataDir, int debuggingPort)
    {
        if (string.IsNullOrWhiteSpace(userDataDir) || debuggingPort <= 0)
        {
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(userDataDir);
            string portFile = Path.Combine(userDataDir, ".fbnet-cdp-port");
            File.WriteAllText(portFile, debuggingPort.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // Best-effort port marker
        }
    }

    /// <summary>
    /// Checks if an existing Chromium/Edge browser window for <paramref name="userDataDir"/> is already running
    /// and listening on its recorded CDP debugging port, returning its page-level WebSocket debugger URI if alive.
    /// </summary>
    public static async Task<Uri?> TryResolveExistingDebuggerUriAsync(string userDataDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userDataDir) || !Directory.Exists(userDataDir))
        {
            return null;
        }

        List<int> candidatePorts = new(2);
        string markerFile = Path.Combine(userDataDir, ".fbnet-cdp-port");
        if (File.Exists(markerFile))
        {
            try
            {
                string raw = (await File.ReadAllTextAsync(markerFile, ct).ConfigureAwait(false)).Trim();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p1) && p1 > 0)
                {
                    candidatePorts.Add(p1);
                }
            }
            catch
            {
                // Ignore unreadable marker
            }
        }

        string devToolsFile = Path.Combine(userDataDir, "DevToolsActivePort");
        if (File.Exists(devToolsFile))
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(devToolsFile, ct).ConfigureAwait(false);
                if (lines.Length > 0 &&
                    int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p2) &&
                    p2 > 0 &&
                    !candidatePorts.Contains(p2))
                {
                    candidatePorts.Add(p2);
                }
            }
            catch
            {
                // Ignore unreadable DevToolsActivePort
            }
        }

        for (int i = 0; i < candidatePorts.Count; i++)
        {
            int port = candidatePorts[i];
            Uri? wsUri = await TryQueryPageTargetOnPortAsync(port, ct).ConfigureAwait(false);
            if (wsUri is not null)
            {
                return wsUri;
            }
        }

        return null;
    }

    /// <summary>
    /// Polls <c>http://127.0.0.1:{port}/json/list</c> until the initial page target is ready and returns its WebSocket debugger URI.
    /// </summary>
    public static async Task<Uri> WaitForPageDebuggerUriAsync(int debuggingPort, Process process, string? userDataDir = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(process);

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        while (!timeoutCts.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                if (!string.IsNullOrWhiteSpace(userDataDir))
                {
                    Uri? existingUri = await TryResolveExistingDebuggerUriAsync(userDataDir, timeoutCts.Token).ConfigureAwait(false);
                    if (existingUri is not null)
                    {
                        return existingUri;
                    }
                }

                throw new ProtocolException($"Browser process (PID {process.Id}) exited prematurely with code {process.ExitCode}.");
            }

            Uri? wsUri = await TryQueryPageTargetOnPortAsync(debuggingPort, timeoutCts.Token).ConfigureAwait(false);
            if (wsUri is not null)
            {
                if (!string.IsNullOrWhiteSpace(userDataDir))
                {
                    RecordActiveDebuggingPort(userDataDir, debuggingPort);
                }

                return wsUri;
            }

            await Task.Delay(200, timeoutCts.Token).ConfigureAwait(false);
        }

        throw new ProtocolException($"Timed out waiting for CDP endpoint on port {debuggingPort}.");
    }

    private static async Task<Uri?> TryQueryPageTargetOnPortAsync(int debuggingPort, CancellationToken ct)
    {
        string listUrl = string.Concat("http://127.0.0.1:", debuggingPort.ToString(CultureInfo.InvariantCulture), "/json/list");
        try
        {
            string json = await s_httpClient.GetStringAsync(listUrl, ct).ConfigureAwait(false);
            CdpTargetInfo[]? targets = JsonSerializer.Deserialize(json, BrowserJsonContext.Default.CdpTargetInfoArray);
            if (targets is null)
            {
                return null;
            }

            Uri? nonInternalPageUri = null;
            foreach (CdpTargetInfo target in targets)
            {
                if (string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) &&
                    Uri.TryCreate(target.WebSocketDebuggerUrl, UriKind.Absolute, out Uri? wsUri))
                {
                    string targetUrl = target.Url ?? string.Empty;
                    if (targetUrl.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return wsUri;
                    }

                    bool isInternalPage =
                        targetUrl.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
                        targetUrl.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                        targetUrl.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase) ||
                        targetUrl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);

                    if (!isInternalPage)
                    {
                        nonInternalPageUri ??= wsUri;
                    }
                }
            }

            return nonInternalPageUri;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Default cross-platform implementation of <see cref="IBrowserProcess"/> wrapping an optional <see cref="Process"/>.
/// </summary>
public sealed class ManagedBrowserProcess : IBrowserProcess
{
    private readonly Process? _process;
    private readonly bool _killOnDispose;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedBrowserProcess"/> class.
    /// </summary>
    public ManagedBrowserProcess(Process? process, Uri debuggerWebSocketUri, bool killOnDispose = false)
    {
        ArgumentNullException.ThrowIfNull(debuggerWebSocketUri);

        this._process = process;
        this._killOnDispose = killOnDispose;
        this.ProcessId = process is not null ? process.Id : Environment.ProcessId;
        this.DebuggerWebSocketUri = debuggerWebSocketUri;
    }

    /// <inheritdoc />
    public int ProcessId { get; }

    /// <inheritdoc />
    public Uri DebuggerWebSocketUri { get; }

    /// <inheritdoc />
    public bool HasExited => this._isDisposed || (this._process is not null && this._process.HasExited);

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
        if (this._killOnDispose)
        {
            this.KillSafe();
        }

        this._process?.Dispose();
    }

    private void KillSafe()
    {
        try
        {
            if (this._process is not null && !this._process.HasExited)
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
