// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Contracts.Abstractions;

/// <summary>
/// OS-specific adapter responsible for locating the Chromium binary and managing its native OS process.
/// Implemented separately for Windows and macOS.
/// </summary>
public interface IBrowserAdapter
{
    /// <summary>
    /// Gets the platform identifier (e.g., "windows", "macos").
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Locates the installed or portable Chromium/Chrome executable path on the current operating system.
    /// </summary>
    /// <returns>The absolute path to the browser executable, or <c>null</c> if not found.</returns>
    string? LocateExecutablePath();

    /// <summary>
    /// Prepares the profile directory and launches an isolated Chromium process with anti-automation flags.
    /// </summary>
    /// <param name="options">Launch configuration for the browser profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An active <see cref="IBrowserProcess"/> exposing the CDP WebSocket debugging endpoint.</returns>
    Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default);
}

/// <summary>
/// Represents a running native Chromium OS process and its exposed DevTools endpoint.
/// </summary>
public interface IBrowserProcess : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the native OS process identifier (PID).
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    /// Gets the local Chrome DevTools Protocol (CDP) WebSocket URL (ws://127.0.0.1:port/devtools/...).
    /// </summary>
    Uri DebuggerWebSocketUri { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying OS process has exited.
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    /// Terminates the browser process and cleans up associated child processes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task TerminateAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents an active Chromium tab session controlled via Chrome DevTools Protocol (CDP).
/// Supports In-Page Request Delegation, Contextual Navigation, and Hidden DOM File Bridge.
/// </summary>
public interface IBrowserSession : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the account UID associated with this browser session.
    /// </summary>
    string AccountUid { get; }

    /// <summary>
    /// Gets the current lifecycle state of the browser session.
    /// </summary>
    BrowserState State { get; }

    /// <summary>
    /// Gets the most recently extracted <see cref="SessionMaterial"/> for this session, if available.
    /// </summary>
    SessionMaterial? CurrentMaterial { get; }

    /// <summary>
    /// Gets a value indicating whether the native Chromium OS process is currently attached and running.
    /// Returns <c>false</c> after <see cref="DetachBrowserProcessAsync"/> terminates the browser process in Extract-and-Terminate mode.
    /// </summary>
    bool IsBrowserProcessAttached => true;

    /// <summary>
    /// Terminates the underlying Chromium OS process to free RAM while keeping <see cref="CurrentMaterial"/>
    /// active for direct HTTP API execution via <see cref="ExecuteInPagePostAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task DetachBrowserProcessAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Navigates the active tab to the specified URL and waits for network stability (Pre/Post-Navigation).
    /// </summary>
    /// <param name="url">Target URL to navigate to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NavigateAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Extracts fresh dynamic tokens (<c>fb_dtsg</c>, <c>jazoest</c>, <c>lsd</c>, <c>client_revision</c>) and cookies from the live page.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted <see cref="SessionMaterial"/>.</returns>
    Task<SessionMaterial> ExtractMaterialAsync(CancellationToken ct = default);

    /// <summary>
    /// Injects a raw HTTP <c>Cookie</c> header string (e.g., <c>c_user=...; xs=...; datr=...</c>)
    /// directly into the Chromium cookie jar for <c>.facebook.com</c> via CDP <c>Network.setCookies</c>.
    /// </summary>
    /// <param name="rawCookieHeader">The raw semicolon-delimited cookie header string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of cookies successfully injected.</returns>
    Task<int> ImportCookiesAsync(string rawCookieHeader, CancellationToken ct = default);

    /// <summary>
    /// Executes an HTTP POST request directly inside the page's DOM context (In-Page Request Delegation)
    /// to inherit the browser's genuine TLS/JA3 fingerprint, HTTP/2 framing, and cookies.
    /// </summary>
    /// <param name="url">Target endpoint URL.</param>
    /// <param name="headers">Custom request headers (e.g., <c>x-fb-lsd</c>, <c>x-asbd-id</c>, <c>x-fb-friendly-name</c>).</param>
    /// <param name="formUrlEncodedBody">URL-encoded form payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw response text returned by the server.</returns>
    Task<string> ExecuteInPagePostAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        string formUrlEncodedBody,
        CancellationToken ct = default);

    /// <summary>
    /// Uploads a local binary file using the Hidden DOM File Bridge technique
    /// (injects a hidden file input, attaches the file via CDP, and submits <c>FormData</c> in-page).
    /// </summary>
    /// <param name="uploadUrl">Target upload endpoint URL.</param>
    /// <param name="localFilePath">Absolute path to the file on disk.</param>
    /// <param name="fileFieldName">Form field name for the binary file (typically <c>file</c>).</param>
    /// <param name="additionalFields">Optional additional multipart form fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw response text from the upload server.</returns>
    Task<string> UploadViaDomBridgeAsync(
        string uploadUrl,
        string localFilePath,
        string fileFieldName = "file",
        IReadOnlyDictionary<string, string>? additionalFields = null,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates multiple isolated <see cref="IBrowserSession"/> instances with a staggered launch queue.
/// </summary>
public interface IBrowserManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Occurs when any managed browser session changes state.
    /// </summary>
    event EventHandler<SessionStateChangedEvent>? SessionStateChanged;

    /// <summary>
    /// Enqueues and launches an isolated browser session for an account, respecting the staggered launch delay.
    /// </summary>
    /// <param name="options">Browser launch configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The initialized and connected <see cref="IBrowserSession"/>.</returns>
    Task<IBrowserSession> AcquireSessionAsync(BrowserLaunchOptions options, CancellationToken ct = default);

    /// <summary>
    /// Attempts to retrieve an active session by account UID.
    /// </summary>
    /// <param name="accountUid">The account UID.</param>
    /// <returns>The active <see cref="IBrowserSession"/>, or <c>null</c> if not running.</returns>
    IBrowserSession? TryGetSession(string accountUid);

    /// <summary>
    /// Closes and disposes the browser session for the specified account UID.
    /// </summary>
    /// <param name="accountUid">The account UID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CloseSessionAsync(string accountUid, CancellationToken ct = default);
}
