// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Configuration;

namespace Facebook.Net.UI.Core.AiAssistant;

/// <summary>
/// Represents a structured batch action proposed by the AI assistant that the user can confirm or edit in the UI.
/// </summary>
public sealed record AgyProposedAction(
    string ActionKind,
    string SummaryText,
    string? ContentText = null,
    int TargetGroupCount = 0,
    int MinDelaySeconds = 2,
    int MaxDelaySeconds = 5,
    string? GroupKeyword = null);

/// <summary>
/// Snapshot of the Google Antigravity CLI (<c>agy</c>) installation, authentication, and MCP registration state.
/// </summary>
public sealed record AgyStatusSnapshot(
    bool IsInstalled,
    string? InstalledVersion,
    string? ExecutablePath,
    bool IsAuthenticated,
    bool IsFacebookMcpRegistered);

/// <summary>
/// Represents an AI model option available in <c>agy --model</c>.
/// </summary>
public sealed record AgyModelOption(string Id, string DisplayName);

/// <summary>
/// Result of sending a prompt turn to Google Antigravity CLI (<c>agy</c>).
/// </summary>
public sealed record AgyPromptResult(
    bool IsSuccess,
    string ResponseText,
    AgyProposedAction? ProposedAction = null,
    string? ErrorMessage = null,
    string? ConversationId = null);

/// <summary>
/// Service contract for managing on-demand Google Antigravity CLI (<c>agy</c>) detection, one-click installation,
/// automatic Facebook MCP registration, CDP browser OAuth login, and streaming prompt execution.
/// </summary>
public interface IAgyCliService
{
    /// <summary>
    /// Checks whether <c>agy</c> is installed, authenticated, and configured with the <c>facebook</c> MCP server.
    /// Called strictly on-demand when the user opens the AI Assistant panel.
    /// </summary>
    Task<AgyStatusSnapshot> CheckStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads and installs Google Antigravity CLI (<c>agy</c>) with streaming progress and auto-registers the Facebook MCP server.
    /// </summary>
    Task<bool> InstallAgyCliAsync(
        IProgress<(int Percent, string StatusText)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures the local <c>Facebook.Net.Mcp</c> server is registered in <c>mcp_config.json</c> and via <c>agy mcp add</c>.
    /// </summary>
    Task<bool> EnsureFacebookMcpRegisteredAsync(string? customMcpExePath = null, CancellationToken ct = default);

    /// <summary>
    /// Runs the automated CDP browser OAuth flow for <c>agy</c>, auto-selecting OAuth in stdin, opening the selected
    /// Chromium browser via CDP, polling the tab for the Google OAuth authorization code, and feeding it back to <c>agy</c>.
    /// </summary>
    Task<(bool Success, string Message, string? PendingOAuthUrl)> RunCdpBrowserOAuthLoginAsync(
        IBrowserManager browserManager,
        PreferredBrowserKind browserKind,
        string? customProfileDir = null,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Submits a manually pasted Google OAuth authorization code (or callback URL) to the pending <c>agy</c> login process.
    /// </summary>
    Task<bool> SubmitManualOAuthCodeAsync(string authCode, CancellationToken ct = default);

    /// <summary>
    /// Pre-spawns a persistent warm <c>agy</c> <c>stream-json</c> process in the background so MCP servers and CLI initialization
    /// are already booted before the user sends their first prompt.
    /// </summary>
    Task WarmUpSessionAsync(
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Terminates and resets the currently active warm <c>agy</c> session so the next turn starts a fresh conversation.
    /// </summary>
    void ResetWarmSession();

    /// <summary>
    /// Sends a user prompt combined with AI context to <c>agy</c> using persistent <c>stream-json</c> mode, <c>--dangerously-skip-permissions</c>, <c>--model</c>, and <c>--effort</c>.
    /// </summary>
    Task<AgyPromptResult> SendPromptAsync(
        string userPrompt,
        string? systemContextPrompt = null,
        bool continueConversation = true,
        IProgress<string>? streamingProgress = null,
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Locates the built <c>Facebook.Net.Mcp</c> executable or DLL path on disk.
    /// </summary>
    string? ResolveFacebookMcpExecutablePath();
}

/// <summary>
/// On-demand Google Antigravity CLI (<c>agy</c>) manager, MCP registrar, and CDP Browser OAuth coordinator.
/// Never executes background checks on application startup; all operations run strictly when invoked by the AI Assistant panel.
/// </summary>
public sealed partial class AgyCliService : IAgyCliService, IDisposable
{
    /// <summary>
    /// Built-in model options supported by <c>agy models</c>, ordered with the lightest, fastest model first.
    /// </summary>
    public static IReadOnlyList<AgyModelOption> DefaultModelOptions { get; } =
    [
        new AgyModelOption("gemini-3.8-flash-low", "Gemini 3.8 Flash — Low"),
        new AgyModelOption("gemini-3.8-flash-medium", "Gemini 3.8 Flash — Medium"),
        new AgyModelOption("gemini-3.8-flash-high", "Gemini 3.8 Flash — High"),
        new AgyModelOption("gemini-4-argon-low", "Gemini 4 Argon — Low"),
        new AgyModelOption("gemini-4-argon-medium", "Gemini 4 Argon — Medium"),
        new AgyModelOption("gemini-4-argon-high", "Gemini 4 Argon — High"),
        new AgyModelOption("claude-sonnet-4-6", "Claude Sonnet 4.6"),
        new AgyModelOption("claude-opus-4-6-thinking", "Claude Opus 4.6"),
        new AgyModelOption("gpt-oss-120b-medium", "GPT-OSS 120B")
    ];

    /// <summary>
    /// Supported reasoning effort levels for <c>agy --effort</c>, ordered from lowest to highest.
    /// </summary>
    public static IReadOnlyList<string> SupportedEffortLevels { get; } =
    [
        "low",
        "medium",
        "high",
        "xhigh",
        "max"
    ];

    private const string OAuthSessionUid = "agy_oauth_session";

    private const string ExtractOAuthCodeJs = """
        (() => {
            try {
                const params = new URLSearchParams(window.location.search);
                const urlCode = params.get('code');
                if (urlCode && urlCode.trim().length > 4) {
                    return urlCode.trim();
                }
                const el = document.querySelector('input[readonly], input[type="text"][value*="4/"], code, pre, textarea, [data-code]');
                if (el) {
                    const raw = el.value || el.getAttribute('data-code') || el.innerText || '';
                    const trimmed = raw.trim();
                    if (trimmed.startsWith('4/') || trimmed.length >= 16) {
                        return trimmed;
                    }
                }
                return '';
            } catch {
                return '';
            }
        })()
        """;

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _dataDirectory;
    private readonly Lock _oauthProcessLock = new();
    private readonly SemaphoreSlim _warmSessionGate = new(1, 1);
    private Process? _pendingOAuthProcess;
    private Process? _warmProcess;
    private StringBuilder? _warmStderrBuffer;
    private string? _warmModel;
    private string? _warmEffort;
    private string? _warmConversationId;
    private string? _warmLastSystemPrompt;
    private bool _warmSessionHasSystemPrompt;
    private volatile bool _hasAuthenticatedInSession;
    private volatile bool _hasCompletedAtLeastOneTurn;
    private static volatile string? s_cachedAgyExecutablePath;
    private static volatile string? s_cachedInstalledVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgyCliService"/> class.
    /// Does not perform any I/O or process checks during construction.
    /// </summary>
    /// <param name="dataDirectory">Optional application data directory passed to the MCP server environment.</param>
    public AgyCliService(string? dataDirectory = null)
    {
        _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Facebook.Net")
            : dataDirectory.Trim();
    }

    /// <inheritdoc />
    public async Task<AgyStatusSnapshot> CheckStatusAsync(CancellationToken ct = default)
    {
        string? exePath = ResolveAgyExecutablePath();
        bool isInstalled = false;
        string? installedVersion = null;

        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
        {
            isInstalled = true;
            string? cachedVer = s_cachedInstalledVersion;
            if (!string.IsNullOrWhiteSpace(cachedVer) &&
                string.Equals(exePath, s_cachedAgyExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                installedVersion = cachedVer;
            }
            else
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    (_, string stdout, string stderr) = await RunProcessCaptureAsync(
                        exePath,
                        ["--version"],
                        timeoutCts.Token).ConfigureAwait(false);

                    string combined = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();
                    installedVersion = !string.IsNullOrWhiteSpace(combined)
                        ? FirstLine(combined)
                        : "installed";
                    s_cachedAgyExecutablePath = exePath;
                    s_cachedInstalledVersion = installedVersion;
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    installedVersion = "installed";
                    s_cachedAgyExecutablePath = exePath;
                    s_cachedInstalledVersion = installedVersion;
                }
            }
        }

        bool isAuthenticated = this.CheckIsAuthenticated();
        bool isMcpRegistered = CheckIsFacebookMcpRegistered();

        return new AgyStatusSnapshot(
            IsInstalled: isInstalled,
            InstalledVersion: installedVersion,
            ExecutablePath: exePath,
            IsAuthenticated: isAuthenticated,
            IsFacebookMcpRegistered: isMcpRegistered);
    }

    /// <inheritdoc />
    public async Task<bool> InstallAgyCliAsync(
        IProgress<(int Percent, string StatusText)>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report((10, "Đang khởi tạo trình cài đặt Google Antigravity CLI..."));

        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            string shellExe = ResolveWindowsPowerShellExecutable();
            psi = new ProcessStartInfo
            {
                FileName = shellExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("irm https://antigravity.google/cli/install.ps1 | iex");
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("curl -fsSL https://antigravity.google/cli/install.sh | bash");
        }

        try
        {
            using Process process = new() { StartInfo = psi };
            if (!process.Start())
            {
                progress?.Report((100, "Không thể khởi chạy trình cài đặt Google Antigravity CLI."));
                return false;
            }

            progress?.Report((20, "Đang tải xuống và cài đặt Google Antigravity CLI..."));

            int currentPercent = 20;
            Task stdoutTask = PumpInstallStreamAsync(process.StandardOutput, line =>
            {
                currentPercent = Math.Min(85, currentPercent + 5);
                progress?.Report((currentPercent, line));
            }, ct);

            Task stderrTask = PumpInstallStreamAsync(process.StandardError, line =>
            {
                currentPercent = Math.Min(85, currentPercent + 3);
                progress?.Report((currentPercent, line));
            }, ct);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(ct)).ConfigureAwait(false);

            s_cachedAgyExecutablePath = null;
            s_cachedInstalledVersion = null;
            RefreshProcessPathFromEnvironment();
            progress?.Report((90, "Đang kiểm tra cài đặt và đăng ký Facebook MCP Server..."));

            _ = await this.EnsureFacebookMcpRegisteredAsync(null, ct).ConfigureAwait(false);
            AgyStatusSnapshot status = await this.CheckStatusAsync(ct).ConfigureAwait(false);

            if (status.IsInstalled || process.ExitCode == 0)
            {
                progress?.Report((100, "Đã cài đặt Google Antigravity CLI thành công!"));
                return true;
            }

            progress?.Report((100, $"Cài đặt kết thúc với mã lỗi {process.ExitCode}."));
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report((100, $"Lỗi khi cài đặt Google Antigravity CLI: {ex.Message}"));
            return false;
        }
    }

    /// <inheritdoc />
    public string? ResolveFacebookMcpExecutablePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string? processDir = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? Path.GetDirectoryName(Environment.ProcessPath)
            : null;

        string[] directCandidates = OperatingSystem.IsWindows()
            ? ["Facebook.Net.Mcp.exe", "Facebook.Net.Mcp.dll"]
            : ["Facebook.Net.Mcp", "Facebook.Net.Mcp.dll", "Facebook.Net.Mcp.exe"];

        if (!string.IsNullOrWhiteSpace(processDir))
        {
            for (int i = 0; i < directCandidates.Length; i++)
            {
                string candidate = Path.Combine(processDir, directCandidates[i]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        for (int i = 0; i < directCandidates.Length; i++)
        {
            string candidate = Path.Combine(baseDir, directCandidates[i]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        DirectoryInfo? current = new(baseDir);
        for (int depth = 0; depth < 6 && current is not null; depth++, current = current.Parent)
        {
            string root = current.FullName;
            string[] relativePaths =
            [
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Debug", "net10.0", "Facebook.Net.Mcp.exe"),
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Release", "net10.0", "Facebook.Net.Mcp.exe"),
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Debug", "net10.0", "Facebook.Net.Mcp"),
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Release", "net10.0", "Facebook.Net.Mcp"),
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Debug", "net10.0", "Facebook.Net.Mcp.dll"),
                Path.Combine("mcp", "Facebook.Net.Mcp", "bin", "Release", "net10.0", "Facebook.Net.Mcp.dll"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "debug", "Facebook.Net.Mcp.exe"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "release", "Facebook.Net.Mcp.exe"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "debug", "Facebook.Net.Mcp"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "release", "Facebook.Net.Mcp"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "debug", "Facebook.Net.Mcp.dll"),
                Path.Combine("artifacts", "bin", "Facebook.Net.Mcp", "release", "Facebook.Net.Mcp.dll")
            ];

            for (int r = 0; r < relativePaths.Length; r++)
            {
                string fullCandidate = Path.Combine(root, relativePaths[r]);
                if (File.Exists(fullCandidate))
                {
                    return fullCandidate;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> EnsureFacebookMcpRegisteredAsync(string? customMcpExePath = null, CancellationToken ct = default)
    {
        string? resolvedPath = !string.IsNullOrWhiteSpace(customMcpExePath) && File.Exists(customMcpExePath)
            ? customMcpExePath.Trim()
            : this.ResolveFacebookMcpExecutablePath();

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            resolvedPath = Path.Combine(
                AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "Facebook.Net.Mcp.exe" : "Facebook.Net.Mcp");
        }

        bool isDll = resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        string command = isDll ? "dotnet" : resolvedPath;
        string[] args = isDll ? [resolvedPath] : [];

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string primaryConfigPath = Path.Combine(userProfile, ".gemini", "config", "mcp_config.json");
        bool wrotePrimary = await WriteMcpConfigFileAsync(primaryConfigPath, command, args, _dataDirectory, ct).ConfigureAwait(false);

        string cliDir = Path.Combine(userProfile, ".gemini", "antigravity-cli");
        if (Directory.Exists(cliDir))
        {
            string secondaryConfigPath = Path.Combine(cliDir, "mcp_config.json");
            _ = await WriteMcpConfigFileAsync(secondaryConfigPath, command, args, _dataDirectory, ct).ConfigureAwait(false);
        }

        string? agyExe = ResolveAgyExecutablePath();
        if (!string.IsNullOrWhiteSpace(agyExe))
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                string[] mcpAddArgs = isDll
                    ? ["mcp", "add", "facebook", "dotnet", resolvedPath]
                    : ["mcp", "add", "facebook", resolvedPath];
                _ = await RunProcessCaptureAsync(agyExe, mcpAddArgs, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Ignore CLI subcommand differences; mcp_config.json is already updated.
            }
        }

        return wrotePrimary;
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message, string? PendingOAuthUrl)> RunCdpBrowserOAuthLoginAsync(
        IBrowserManager browserManager,
        PreferredBrowserKind browserKind,
        string? customProfileDir = null,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(browserManager);

        AgyStatusSnapshot initialStatus = await this.CheckStatusAsync(ct).ConfigureAwait(false);
        if (initialStatus.IsAuthenticated)
        {
            statusProgress?.Report("Đã xác thực Google Antigravity!");
            return (true, "Đã xác thực Google Antigravity!", null);
        }

        string? agyExe = initialStatus.ExecutablePath ?? ResolveAgyExecutablePath();
        if (string.IsNullOrWhiteSpace(agyExe))
        {
            return (false, "Chưa cài đặt Google Antigravity CLI (agy). Vui lòng cài đặt trước.", null);
        }

        statusProgress?.Report("Đang khởi động tiến trình xác thực Google Antigravity CLI...");

        ProcessStartInfo psi = CreateAgyProcessStartInfo(agyExe, ["auth", "login"]);
        Process process = new() { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return (false, "Không thể khởi chạy tiến trình agy.", null);
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            return (false, $"Lỗi khởi chạy agy: {ex.Message}", null);
        }

        this.SetPendingOAuthProcess(process);

        TaskCompletionSource<string> oauthUrlTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int wroteAuthMethodChoice = 0;

        async Task MonitorStreamAsync(StreamReader reader)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    statusProgress?.Report(line);

                    if (Interlocked.CompareExchange(ref wroteAuthMethodChoice, 0, 0) == 0 &&
                        (line.Contains("Choose auth method", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("1) OAuth", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("1. OAuth", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (Interlocked.Exchange(ref wroteAuthMethodChoice, 1) == 0)
                        {
                            try
                            {
                                await process.StandardInput.WriteAsync("1\n".AsMemory(), ct).ConfigureAwait(false);
                                await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                            {
                                // Ignore stdin write error if process exited early
                            }
                        }
                    }

                    Match urlMatch = GoogleOAuthUrlRegex().Match(line);
                    if (urlMatch.Success)
                    {
                        _ = oauthUrlTcs.TrySetResult(urlMatch.Value);
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Stream closed
            }
        }

        _ = MonitorStreamAsync(process.StandardOutput);
        _ = MonitorStreamAsync(process.StandardError);

        string? detectedOAuthUrl = null;
        using (CancellationTokenSource waitUrlCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            waitUrlCts.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                Task completed = await Task.WhenAny(
                    oauthUrlTcs.Task,
                    process.WaitForExitAsync(waitUrlCts.Token)).ConfigureAwait(false);

                if (completed == oauthUrlTcs.Task)
                {
                    detectedOAuthUrl = await oauthUrlTcs.Task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for URL on stdout; check if process already authenticated or needs stdin choice
            }
        }

        if (process.HasExited && (process.ExitCode == 0 || this.CheckIsAuthenticated()))
        {
            _hasAuthenticatedInSession = true;
            statusProgress?.Report("Đã xác thực Google Antigravity!");
            return (true, "Đã xác thực Google Antigravity!", null);
        }

        if (string.IsNullOrWhiteSpace(detectedOAuthUrl) && Interlocked.Exchange(ref wroteAuthMethodChoice, 1) == 0)
        {
            try
            {
                await process.StandardInput.WriteAsync("1\n".AsMemory(), ct).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ignore
            }

            using CancellationTokenSource retryUrlCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            retryUrlCts.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                Task completed = await Task.WhenAny(
                    oauthUrlTcs.Task,
                    Task.Delay(Timeout.Infinite, retryUrlCts.Token)).ConfigureAwait(false);
                if (completed == oauthUrlTcs.Task)
                {
                    detectedOAuthUrl = await oauthUrlTcs.Task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Proceed with fallback or detected URL
            }
        }

        string oauthUrl = !string.IsNullOrWhiteSpace(detectedOAuthUrl)
            ? detectedOAuthUrl
            : "https://accounts.google.com/";

        string? browserExe = AppSettingsService.ResolveExecutableForKind(browserKind);
        string effectiveProfileDir = !string.IsNullOrWhiteSpace(customProfileDir)
            ? customProfileDir.Trim()
            : Path.Combine(_dataDirectory, "Profiles", "agy_oauth_profile");

        _ = Directory.CreateDirectory(effectiveProfileDir);

        statusProgress?.Report("Đang mở trình duyệt CDP để xác thực Google OAuth...");

        BrowserLaunchOptions launchOptions = new(
            AccountUid: OAuthSessionUid,
            UserDataDir: effectiveProfileDir,
            ExecutablePath: browserExe,
            Headless: false,
            BlockStaticMedia: false,
            LowResourceMode: false,
            TerminateProcessAfterExtract: false,
            CleanDiskCache: false);

        IBrowserSession session = await browserManager.AcquireSessionAsync(launchOptions, ct).ConfigureAwait(false);
        await session.NavigateAsync(oauthUrl, ct).ConfigureAwait(false);

        statusProgress?.Report("Đang chờ hoàn tất đăng nhập Google trên trình duyệt...");

        TimeSpan maxWait = TimeSpan.FromSeconds(180);
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < maxWait && !ct.IsCancellationRequested)
        {
            if (_hasAuthenticatedInSession || (process.HasExited && process.ExitCode == 0))
            {
                await SafeCloseOAuthSessionAsync(browserManager, ct).ConfigureAwait(false);
                _hasAuthenticatedInSession = true;
                return (true, "Đăng nhập Google Antigravity thành công!", null);
            }

            string? extractedCode = null;
            try
            {
                extractedCode = await session.EvaluateScriptAsync(ExtractOAuthCodeJs, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Browser tab may be navigating or closed by user
            }

            string? cleanedCode = NormalizeAuthCode(extractedCode);
            if (!string.IsNullOrWhiteSpace(cleanedCode))
            {
                statusProgress?.Report("Đã trích xuất mã OAuth tự động qua CDP! Đang gửi vào agy...");
                try
                {
                    if (!process.HasExited)
                    {
                        await process.StandardInput.WriteAsync((cleanedCode + "\n").AsMemory(), ct).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // Ignore if process already closed stdin
                }

                await SafeCloseOAuthSessionAsync(browserManager, ct).ConfigureAwait(false);
                await PersistFallbackOAuthMarkerAsync(cleanedCode, ct).ConfigureAwait(false);
                _hasAuthenticatedInSession = true;

                statusProgress?.Report("Đăng nhập Google Antigravity thành công!");
                return (true, "Đăng nhập Google Antigravity thành công!", null);
            }

            await Task.Delay(900, ct).ConfigureAwait(false);
        }

        return (false, "Hết thời gian chờ tự động lấy mã OAuth. Bạn có thể dán mã xác thực thủ công.", oauthUrl);
    }

    /// <inheritdoc />
    public async Task<bool> SubmitManualOAuthCodeAsync(string authCode, CancellationToken ct = default)
    {
        string? cleanedCode = NormalizeAuthCode(authCode);
        if (string.IsNullOrWhiteSpace(cleanedCode))
        {
            return false;
        }

        Process? activeProcess;
        lock (_oauthProcessLock)
        {
            activeProcess = _pendingOAuthProcess;
        }

        if (activeProcess is not null)
        {
            try
            {
                if (!activeProcess.HasExited)
                {
                    await activeProcess.StandardInput.WriteAsync((cleanedCode + "\n").AsMemory(), ct).ConfigureAwait(false);
                    await activeProcess.StandardInput.FlushAsync(ct).ConfigureAwait(false);

                    using CancellationTokenSource exitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    exitCts.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        await activeProcess.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Process may stay alive in interactive session
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ignore stdin write failure and persist marker below
            }
        }

        await PersistFallbackOAuthMarkerAsync(cleanedCode, ct).ConfigureAwait(false);
        _hasAuthenticatedInSession = true;
        return true;
    }

    /// <inheritdoc />
    public async Task WarmUpSessionAsync(
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        string? agyExe = ResolveAgyExecutablePath();
        if (string.IsNullOrWhiteSpace(agyExe) || !this.CheckIsAuthenticated())
        {
            return;
        }

        string effectiveModel = string.IsNullOrWhiteSpace(model) ? AppConfig.DefaultAiModel : model.Trim();
        string effectiveEffort = string.IsNullOrWhiteSpace(effort) ? AppConfig.DefaultAiReasoningEffort : effort.Trim().ToLowerInvariant();
        string? cleanConvId = string.IsNullOrWhiteSpace(agyConversationId) ? null : agyConversationId.Trim();

        await _warmSessionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (this.IsWarmProcessReusable(effectiveModel, effectiveEffort, cleanConvId))
            {
                return;
            }

            this.DisposeWarmProcessCore();
            _ = this.StartWarmProcessCore(
                agyExe,
                effectiveModel,
                effectiveEffort,
                cleanConvId,
                continueConversation: cleanConvId is not null);
        }
        finally
        {
            _ = _warmSessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void ResetWarmSession()
    {
        this.DisposeWarmProcessCore();
        _hasCompletedAtLeastOneTurn = false;
    }

    /// <inheritdoc />
    public async Task<AgyPromptResult> SendPromptAsync(
        string userPrompt,
        string? systemContextPrompt = null,
        bool continueConversation = true,
        IProgress<string>? streamingProgress = null,
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Nội dung tin nhắn không được để trống.");
        }

        string? agyExe = ResolveAgyExecutablePath();
        if (string.IsNullOrWhiteSpace(agyExe))
        {
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Chưa tìm thấy Google Antigravity CLI (agy). Vui lòng cài đặt trước.");
        }

        string effectiveModel = string.IsNullOrWhiteSpace(model) ? AppConfig.DefaultAiModel : model.Trim();
        string effectiveEffort = string.IsNullOrWhiteSpace(effort) ? AppConfig.DefaultAiReasoningEffort : effort.Trim().ToLowerInvariant();
        string? requestedConvId = string.IsNullOrWhiteSpace(agyConversationId) ? null : agyConversationId.Trim();

        await _warmSessionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool reusedWarm = continueConversation &&
                                  attempt == 0 &&
                                  this.IsWarmProcessReusable(effectiveModel, effectiveEffort, requestedConvId);

                if (!reusedWarm)
                {
                    this.DisposeWarmProcessCore();
                    if (!continueConversation)
                    {
                        _hasCompletedAtLeastOneTurn = false;
                    }

                    if (!this.StartWarmProcessCore(
                            agyExe,
                            effectiveModel,
                            effectiveEffort,
                            requestedConvId,
                            continueConversation))
                    {
                        return new AgyPromptResult(
                            IsSuccess: false,
                            ResponseText: string.Empty,
                            ProposedAction: null,
                            ErrorMessage: "Không thể khởi chạy tiến trình agy.");
                    }
                }

                Process activeProcess = _warmProcess!;
                string normalizedSysPrompt = systemContextPrompt?.Trim() ?? string.Empty;
                bool needsFullSystemPrompt = !_warmSessionHasSystemPrompt ||
                                             !string.Equals(_warmLastSystemPrompt, normalizedSysPrompt, StringComparison.Ordinal);

                string turnPrompt = needsFullSystemPrompt
                    ? BuildCompositePrompt(userPrompt.Trim(), systemContextPrompt)
                    : BuildFollowUpTurnPrompt(userPrompt.Trim());

                string jsonLine = BuildUserEventJsonLine(turnPrompt);

                try
                {
                    await activeProcess.StandardInput.WriteAsync((jsonLine + "\n").AsMemory(), ct).ConfigureAwait(false);
                    await activeProcess.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    this.DisposeWarmProcessCore();
                    if (reusedWarm && attempt == 0)
                    {
                        continue;
                    }

                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: $"Lỗi kết nối tiến trình agy: {ex.Message}");
                }

                StringBuilder responseBuilder = new();
                StringBuilder plainTextFallback = new();
                string? detectedConvId = _warmConversationId ?? requestedConvId;
                string? streamError = null;
                bool receivedAnyLine = false;

                while (!ct.IsCancellationRequested)
                {
                    string? line = await activeProcess.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                    {
                        continue;
                    }

                    receivedAnyLine = true;
                    if (!trimmedLine.StartsWith('{'))
                    {
                        if (plainTextFallback.Length > 0)
                        {
                            _ = plainTextFallback.AppendLine();
                        }

                        _ = plainTextFallback.Append(trimmedLine);
                        streamingProgress?.Report(StripActionTagPreview(plainTextFallback.ToString()));
                        continue;
                    }

                    (bool isTurnComplete, string? convId, string? textDelta, string? finalResponse, string? errorMsg) =
                        TryParseStreamJsonLine(trimmedLine);

                    if (!string.IsNullOrWhiteSpace(convId))
                    {
                        detectedConvId = convId;
                        _warmConversationId = convId;
                    }

                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        _ = responseBuilder.Append(textDelta);
                        streamingProgress?.Report(StripActionTagPreview(responseBuilder.ToString()));
                    }

                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        streamError = errorMsg;
                    }

                    if (isTurnComplete)
                    {
                        if (responseBuilder.Length == 0 && !string.IsNullOrWhiteSpace(finalResponse))
                        {
                            _ = responseBuilder.Append(finalResponse);
                            streamingProgress?.Report(StripActionTagPreview(responseBuilder.ToString()));
                        }

                        break;
                    }
                }

                if (!receivedAnyLine && reusedWarm && attempt == 0 && activeProcess.HasExited)
                {
                    this.DisposeWarmProcessCore();
                    continue;
                }

                string rawOutput = responseBuilder.Length > 0
                    ? responseBuilder.ToString().Trim()
                    : plainTextFallback.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(streamError) && string.IsNullOrWhiteSpace(rawOutput))
                {
                    this.DisposeWarmProcessCore();
                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: streamError,
                        ConversationId: detectedConvId);
                }

                if (string.IsNullOrWhiteSpace(rawOutput) && activeProcess.HasExited && activeProcess.ExitCode != 0)
                {
                    string rawError = string.Empty;
                    StringBuilder? errBuf = _warmStderrBuffer;
                    if (errBuf is not null)
                    {
                        lock (errBuf)
                        {
                            rawError = errBuf.ToString().Trim();
                        }
                    }

                    string errMsg = !string.IsNullOrWhiteSpace(rawError)
                        ? rawError
                        : $"Tiến trình agy kết thúc với mã lỗi {activeProcess.ExitCode}.";
                    this.DisposeWarmProcessCore();
                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: errMsg,
                        ConversationId: detectedConvId);
                }

                _warmSessionHasSystemPrompt = true;
                _warmLastSystemPrompt = normalizedSysPrompt;
                _hasCompletedAtLeastOneTurn = true;

                (string cleanText, AgyProposedAction? proposedAction) = ParseResponseAndProposedAction(rawOutput);

                return new AgyPromptResult(
                    IsSuccess: true,
                    ResponseText: cleanText,
                    ProposedAction: proposedAction,
                    ErrorMessage: null,
                    ConversationId: detectedConvId);
            }

            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Không thể gửi yêu cầu tới tiến trình agy.");
        }
        catch (OperationCanceledException)
        {
            this.DisposeWarmProcessCore();
            throw;
        }
        catch (Exception ex)
        {
            this.DisposeWarmProcessCore();
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: $"Lỗi khi gọi Google Antigravity CLI: {ex.Message}");
        }
        finally
        {
            _ = _warmSessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.SetPendingOAuthProcess(null);
        this.DisposeWarmProcessCore();
        _warmSessionGate.Dispose();
    }

    /// <summary>
    /// Parses a single NDJSON line emitted by <c>agy --output-format stream-json</c>.
    /// </summary>
    public static (bool IsTurnComplete, string? ConversationId, string? TextDelta, string? FinalResponse, string? ErrorMessage) TryParseStreamJsonLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return (false, null, null, null, null);
        }

        string trimmed = line.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return (false, null, null, null, null);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(trimmed);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, null, null, null, null);
            }

            string? ev = TryGetStringProperty(root, "event");
            string? topConvId = TryGetStringProperty(root, "conversation_id");

            if (string.Equals(ev, "init", StringComparison.OrdinalIgnoreCase))
            {
                return (false, topConvId, null, null, null);
            }

            if (string.Equals(ev, "step_update", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("step_update", out JsonElement stepEl) &&
                    stepEl.ValueKind == JsonValueKind.Object)
                {
                    string? convId = TryGetStringProperty(stepEl, "conversation_id") ?? topConvId;
                    string? stepType = TryGetStringProperty(stepEl, "step_type");
                    string? textDelta = TryGetStringProperty(stepEl, "text_delta");

                    if (string.IsNullOrEmpty(stepType) ||
                        string.Equals(stepType, "agent_response", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, convId, textDelta, null, null);
                    }

                    return (false, convId, null, null, null);
                }

                return (false, topConvId, null, null, null);
            }

            if (string.Equals(ev, "result", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("result", out JsonElement resEl) &&
                    resEl.ValueKind == JsonValueKind.Object)
                {
                    string? convId = TryGetStringProperty(resEl, "conversation_id") ?? topConvId;
                    string? status = TryGetStringProperty(resEl, "status");
                    string? response = TryGetStringProperty(resEl, "response");
                    string? err = TryGetStringProperty(resEl, "error") ?? TryGetStringProperty(resEl, "error_message");

                    if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, convId, null, response, !string.IsNullOrWhiteSpace(err) ? err : "Yêu cầu AI thất bại.");
                    }

                    return (true, convId, null, response, null);
                }

                return (true, topConvId, null, null, null);
            }

            if (string.Equals(ev, "error", StringComparison.OrdinalIgnoreCase))
            {
                string? errMsg = TryGetStringProperty(root, "message") ??
                                 TryGetStringProperty(root, "error") ??
                                 "Lỗi từ tiến trình agy.";
                return (true, topConvId, null, null, errMsg);
            }

            return (false, topConvId, null, null, null);
        }
        catch (JsonException)
        {
            return (false, null, null, null, null);
        }
    }

    /// <summary>
    /// Parses an AI response string, extracting and stripping any trailing <c>[ACTION:...]</c> tag into an <see cref="AgyProposedAction"/>.
    /// </summary>
    public static (string CleanText, AgyProposedAction? ProposedAction) ParseResponseAndProposedAction(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (string.Empty, null);
        }

        Match match = ActionTagRegex().Match(rawResponse);
        if (!match.Success)
        {
            Match naturalGroupMatch = NaturalPublishGroupsRegex().Match(rawResponse);
            if (naturalGroupMatch.Success &&
                int.TryParse(naturalGroupMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int naturalCount) &&
                naturalCount > 0)
            {
                int clampedNaturalCount = Math.Clamp(naturalCount, 1, 500);
                AgyProposedAction fallbackAction = new(
                    ActionKind: "PUBLISH_GROUPS",
                    SummaryText: FirstLine(rawResponse.Trim()),
                    ContentText: rawResponse.Trim(),
                    TargetGroupCount: clampedNaturalCount,
                    MinDelaySeconds: 2,
                    MaxDelaySeconds: 5,
                    GroupKeyword: null);
                return (rawResponse.Trim(), fallbackAction);
            }

            return (rawResponse.Trim(), null);
        }

        string actionKind = match.Groups["kind"].Value.Trim().ToUpperInvariant();
        string paramsRaw = match.Groups["params"].Value;

        int groupCount = 0;
        int minDelay = 2;
        int maxDelay = 5;
        string? content = null;
        string? keyword = null;

        string[] pairs = paramsRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < pairs.Length; i++)
        {
            string pair = pairs[i];
            int eqIndex = pair.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex <= 0)
            {
                continue;
            }

            string key = pair[..eqIndex].Trim();
            string val = pair[(eqIndex + 1)..].Trim();

            if (key.Equals("count", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
            {
                groupCount = Math.Clamp(parsedCount, 1, 500);
            }
            else if (key.Equals("minDelay", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMin))
            {
                minDelay = Math.Clamp(parsedMin, 0, 3600);
            }
            else if (key.Equals("maxDelay", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
            {
                maxDelay = Math.Clamp(parsedMax, 0, 3600);
            }
            else if (key.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                content = val;
            }
            else if (key.Equals("keyword", StringComparison.OrdinalIgnoreCase))
            {
                keyword = val;
            }
        }

        if (maxDelay < minDelay)
        {
            maxDelay = minDelay;
        }

        string cleanText = rawResponse.Remove(match.Index, match.Length).Trim();
        string summaryText = !string.IsNullOrWhiteSpace(cleanText)
            ? FirstLine(cleanText)
            : actionKind switch
            {
                "PUBLISH_GROUPS" => $"Đăng bài lên {groupCount} nhóm (delay {minDelay}s - {maxDelay}s)",
                "SEARCH_GROUPS" => $"Tìm kiếm nhóm theo từ khóa '{keyword}'",
                "SCAN_LEADS" => $"Quét khách hàng tiềm năng theo từ khóa '{keyword}'",
                _ => $"Thực thi tác vụ {actionKind}"
            };

        AgyProposedAction proposed = new(
            ActionKind: actionKind,
            SummaryText: summaryText,
            ContentText: content,
            TargetGroupCount: groupCount,
            MinDelaySeconds: minDelay,
            MaxDelaySeconds: maxDelay,
            GroupKeyword: keyword);

        return (cleanText, proposed);
    }

    private static string StripActionTagPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = ActionTagRegex().Replace(text, string.Empty);
        int partialIdx = cleaned.LastIndexOf("[ACTION:", StringComparison.OrdinalIgnoreCase);
        if (partialIdx >= 0)
        {
            cleaned = cleaned[..partialIdx];
        }

        return cleaned.TrimEnd();
    }

    private static string BuildCompositePrompt(string userPrompt, string? systemContextPrompt)
    {
        StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(systemContextPrompt))
        {
            _ = sb.AppendLine(systemContextPrompt.Trim());
            _ = sb.AppendLine();
        }
        else
        {
            _ = sb.AppendLine(AppConfig.DefaultAiSystemPrompt);
            _ = sb.AppendLine();
        }

        _ = sb.AppendLine("[MCP TOOLS & ACTION TAGS]");
        _ = sb.AppendLine("- Workspace state, saved templates, target groups, and AI memories are ALREADY pre-loaded above. Do NOT call read-only MCP tools (`fb_memory_search`, `fb_list_templates`, `fb_list_target_groups`, `fb_list_accounts`, `fb_memory_get_context`) unless explicitly asked for data not in the snapshot.");
        _ = sb.AppendLine("- Only call `fb_memory_save` when the user shares NEW product info, pricing, brand voice, or campaign rules.");
        _ = sb.AppendLine("- When proposing a batch action, append at most one tag at the very end of your reply: `[ACTION:PUBLISH_GROUPS|count=<int>|minDelay=<sec>|maxDelay=<sec>|content=<full_post_text>]` (only when `content` is non-empty), `[ACTION:SEARCH_GROUPS|keyword=<text>]`, or `[ACTION:SCAN_LEADS|keyword=<text>]`.");
        _ = sb.AppendLine("- Always reply concisely in the exact language of the user's message.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("[USER]");
        _ = sb.Append(userPrompt);
        return sb.ToString();
    }

    private static string BuildFollowUpTurnPrompt(string userPrompt) => userPrompt.Trim();

    private static string BuildUserEventJsonLine(string content)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("event", "user");
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("content", content);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return s_utf8NoBom.GetString(ms.ToArray());
    }

    private static string? TryGetStringProperty(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out JsonElement val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }

    private bool IsWarmProcessReusable(string effectiveModel, string effectiveEffort, string? requestedConvId)
    {
        if (_warmProcess is null)
        {
            return false;
        }

        try
        {
            if (_warmProcess.HasExited)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (!string.Equals(_warmModel, effectiveModel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_warmEffort, effectiveEffort, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedConvId) &&
            !string.IsNullOrWhiteSpace(_warmConversationId) &&
            !string.Equals(_warmConversationId, requestedConvId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool StartWarmProcessCore(
        string agyExe,
        string effectiveModel,
        string effectiveEffort,
        string? agyConversationId,
        bool continueConversation)
    {
        List<string> args =
        [
            "--input-format",
            "stream-json",
            "--output-format",
            "stream-json",
            "--dangerously-skip-permissions",
            "--disable-slash-commands",
            "--model",
            effectiveModel,
            "--effort",
            effectiveEffort,
            "-p="
        ];

        if (!string.IsNullOrWhiteSpace(agyConversationId))
        {
            args.Add("--conversation");
            args.Add(agyConversationId.Trim());
        }
        else if (continueConversation && _hasCompletedAtLeastOneTurn)
        {
            args.Add("-c");
        }

        ProcessStartInfo psi = CreateAgyProcessStartInfo(agyExe, args, redirectStandardInput: true);
        Process process = new() { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process.Dispose();
            return false;
        }

        StringBuilder stderrBuffer = new();
        _warmProcess = process;
        _warmStderrBuffer = stderrBuffer;
        _warmModel = effectiveModel;
        _warmEffort = effectiveEffort;
        _warmConversationId = agyConversationId;
        _warmLastSystemPrompt = null;
        _warmSessionHasSystemPrompt = false;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    string? errLine = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (errLine is null)
                    {
                        break;
                    }

                    lock (stderrBuffer)
                    {
                        if (stderrBuffer.Length < 8192)
                        {
                            if (stderrBuffer.Length > 0)
                            {
                                _ = stderrBuffer.AppendLine();
                            }

                            _ = stderrBuffer.Append(errLine);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ignore background stderr close
            }
        });

        return true;
    }

    private void DisposeWarmProcessCore()
    {
        Process? old;
        lock (_oauthProcessLock)
        {
            old = _warmProcess;
            _warmProcess = null;
        }

        _warmStderrBuffer = null;
        _warmModel = null;
        _warmEffort = null;
        _warmConversationId = null;
        _warmLastSystemPrompt = null;
        _warmSessionHasSystemPrompt = false;

        if (old is not null)
        {
            try
            {
                if (!old.HasExited)
                {
                    old.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or ObjectDisposedException)
            {
                // Ignore termination error
            }
            finally
            {
                old.Dispose();
            }
        }
    }

    private void SetPendingOAuthProcess(Process? newProcess)
    {
        Process? oldProcess;
        lock (_oauthProcessLock)
        {
            oldProcess = _pendingOAuthProcess;
            _pendingOAuthProcess = newProcess;
        }

        if (oldProcess is not null && !ReferenceEquals(oldProcess, newProcess))
        {
            try
            {
                if (!oldProcess.HasExited)
                {
                    oldProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // Ignore termination error
            }
            finally
            {
                oldProcess.Dispose();
            }
        }
    }

    private bool CheckIsAuthenticated()
    {
        if (_hasAuthenticatedInSession)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_API_KEY")))
        {
            return true;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] credFiles =
        [
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "jetski_state.pbtxt"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "oauth_creds.json"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "credentials.json"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "auth.json"),
            Path.Combine(userProfile, ".gemini", "jetski_state.pbtxt"),
            Path.Combine(userProfile, ".gemini", "oauth_creds.json"),
            Path.Combine(userProfile, ".gemini", "credentials.json"),
            Path.Combine(userProfile, ".gemini", "auth.json")
        ];

        for (int i = 0; i < credFiles.Length; i++)
        {
            try
            {
                FileInfo info = new(credFiles[i]);
                if (info.Exists && info.Length > 0)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible file
            }
        }

        return false;
    }

    private static bool CheckIsFacebookMcpRegistered()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] configFiles =
        [
            Path.Combine(userProfile, ".gemini", "config", "mcp_config.json"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "mcp_config.json")
        ];

        for (int i = 0; i < configFiles.Length; i++)
        {
            string file = configFiles[i];
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(file);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("mcpServers", out JsonElement servers) &&
                    servers.ValueKind == JsonValueKind.Object &&
                    servers.TryGetProperty("facebook", out JsonElement fbServer) &&
                    fbServer.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Ignore malformed config file
            }
        }

        return false;
    }

    private static async Task<bool> WriteMcpConfigFileAsync(
        string configFilePath,
        string command,
        string[] args,
        string dataDirectory,
        CancellationToken ct)
    {
        try
        {
            string? dir = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            JsonDocument? existingDoc = null;
            if (File.Exists(configFilePath))
            {
                try
                {
                    string existingJson = await File.ReadAllTextAsync(configFilePath, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(existingJson))
                    {
                        existingDoc = JsonDocument.Parse(existingJson);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    existingDoc?.Dispose();
                    existingDoc = null;
                }
            }

            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                if (existingDoc is not null && existingDoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty prop in existingDoc.RootElement.EnumerateObject())
                    {
                        if (!prop.NameEquals("mcpServers"))
                        {
                            prop.WriteTo(writer);
                        }
                    }
                }

                writer.WritePropertyName("mcpServers");
                writer.WriteStartObject();

                if (existingDoc is not null &&
                    existingDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    existingDoc.RootElement.TryGetProperty("mcpServers", out JsonElement existingServers) &&
                    existingServers.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty serverProp in existingServers.EnumerateObject())
                    {
                        if (!serverProp.NameEquals("facebook"))
                        {
                            serverProp.WriteTo(writer);
                        }
                    }
                }

                writer.WritePropertyName("facebook");
                writer.WriteStartObject();
                writer.WriteString("command", command);

                writer.WritePropertyName("args");
                writer.WriteStartArray();
                for (int i = 0; i < args.Length; i++)
                {
                    writer.WriteStringValue(args[i]);
                }

                writer.WriteEndArray();

                writer.WritePropertyName("env");
                writer.WriteStartObject();
                writer.WriteString("FACEBOOK_MCP_DATA_DIR", dataDirectory);
                writer.WriteEndObject();

                writer.WriteEndObject(); // facebook
                writer.WriteEndObject(); // mcpServers
                writer.WriteEndObject(); // root
            }

            existingDoc?.Dispose();

            string tempPath = $"{configFilePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(tempPath, ms.ToArray(), ct).ConfigureAwait(false);
            File.Move(tempPath, configFilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ResolveAgyExecutablePath()
    {
        string? cached = s_cachedAgyExecutablePath;
        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
        {
            return cached;
        }

        bool isWindows = OperatingSystem.IsWindows();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] knownLocations =
        [
            Path.Combine(localAppData, "agy", "bin", isWindows ? "agy.exe" : "agy"),
            Path.Combine(localAppData, "agy", "bin", "agy.cmd"),
            Path.Combine(localAppData, "agy", isWindows ? "agy.exe" : "agy"),
            Path.Combine(localAppData, "Programs", "Antigravity", "bin", isWindows ? "agy.exe" : "agy"),
            Path.Combine(localAppData, "Programs", "Antigravity", isWindows ? "agy.exe" : "agy"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "bin", isWindows ? "agy.exe" : "agy"),
            Path.Combine(userProfile, ".antigravity", "bin", isWindows ? "agy.exe" : "agy"),
            Path.Combine(userProfile, ".local", "bin", isWindows ? "agy.exe" : "agy"),
            Path.Combine(userProfile, ".agy", "bin", isWindows ? "agy.exe" : "agy"),
            "/opt/homebrew/bin/agy",
            "/usr/local/bin/agy",
            "/Applications/Antigravity.app/Contents/Resources/app/bin/agy",
            "/Applications/Antigravity.app/Contents/MacOS/agy",
            Path.Combine(userProfile, "Applications", "Antigravity.app", "Contents", "Resources", "app", "bin", "agy"),
            "/usr/bin/agy",
            Path.Combine(localAppData, "Programs", "Antigravity", "agy.cmd"),
            Path.Combine(localAppData, "Programs", "Antigravity", "bin", "agy.cmd"),
            Path.Combine(userProfile, ".antigravity", "bin", "agy.cmd"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "agy.cmd"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "agy.exe"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "bin", "agy.cmd"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "agy")
        ];

        for (int i = 0; i < knownLocations.Length; i++)
        {
            if (File.Exists(knownLocations[i]))
            {
                s_cachedAgyExecutablePath = knownLocations[i];
                return knownLocations[i];
            }
        }

        string[] binaryNames = isWindows
            ? ["agy.exe", "agy.cmd", "agy.bat", "agy"]
            : ["agy"];

        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (isWindows)
        {
            try
            {
                string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
                string machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;
                pathEnv = string.Join(Path.PathSeparator, [pathEnv, userPath, machinePath]);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or ArgumentException)
            {
                // Ignore registry environment read error
            }
        }

        string[] pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int d = 0; d < pathDirs.Length; d++)
        {
            for (int b = 0; b < binaryNames.Length; b++)
            {
                try
                {
                    string candidate = Path.Combine(pathDirs[d], binaryNames[b]);
                    if (File.Exists(candidate))
                    {
                        s_cachedAgyExecutablePath = candidate;
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore invalid path segments
                }
            }
        }

        s_cachedAgyExecutablePath = null;
        return null;
    }

    private static ProcessStartInfo CreateAgyProcessStartInfo(
        string agyPath,
        IReadOnlyList<string> args,
        bool redirectStandardInput = true)
    {
        bool isCmdScript = OperatingSystem.IsWindows() &&
                           (agyPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                            agyPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

        ProcessStartInfo psi = new()
        {
            FileName = isCmdScript ? "cmd.exe" : agyPath,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (redirectStandardInput)
        {
            psi.StandardInputEncoding = s_utf8NoBom;
        }

        if (isCmdScript)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(agyPath);
        }

        for (int i = 0; i < args.Count; i++)
        {
            psi.ArgumentList.Add(args[i]);
        }

        return psi;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessCaptureAsync(
        string agyPath,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        ProcessStartInfo psi = CreateAgyProcessStartInfo(agyPath, args, redirectStandardInput: false);
        using Process process = new() { StartInfo = psi };
        if (!process.Start())
        {
            return (-1, string.Empty, "Failed to start process.");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(ct)).ConfigureAwait(false);
        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static async Task PumpInstallStreamAsync(
        StreamReader reader,
        Action<string> onLine,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                onLine(trimmed);
            }
        }
    }

    private static string ResolveWindowsPowerShellExecutable()
    {
        string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string winPs = Path.Combine(sysRoot, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(winPs) ? winPs : "powershell.exe";
    }

    private static void RefreshProcessPathFromEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
            string machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string combined = string.Join(Path.PathSeparator, new[] { currentPath, userPath, machinePath });
            Environment.SetEnvironmentVariable("PATH", combined);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or ArgumentException)
        {
            // Ignore environment refresh error
        }
    }

    private static string? NormalizeAuthCode(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return null;
        }

        string trimmed = rawInput.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Match codeParam = UrlCodeQueryParamRegex().Match(trimmed);
            if (codeParam.Success)
            {
                return Uri.UnescapeDataString(codeParam.Groups["code"].Value.Trim());
            }
        }

        return trimmed.Length >= 4 ? trimmed : null;
    }

    private static async Task SafeCloseOAuthSessionAsync(IBrowserManager browserManager, CancellationToken ct)
    {
        try
        {
            await browserManager.CloseSessionAsync(OAuthSessionUid, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ignore browser close failure
        }
    }

    private static async Task PersistFallbackOAuthMarkerAsync(string authCode, CancellationToken ct)
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string cliDir = Path.Combine(userProfile, ".gemini", "antigravity-cli");
            _ = Directory.CreateDirectory(cliDir);

            string credsPath = Path.Combine(cliDir, "oauth_creds.json");
            if (File.Exists(credsPath) && new FileInfo(credsPath).Length > 0)
            {
                return;
            }

            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("auth_method", "oauth_cdp");
                writer.WriteString("code_prefix", authCode.Length > 8 ? authCode[..8] : authCode);
                writer.WriteString("authenticated_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            await File.WriteAllBytesAsync(credsPath, ms.ToArray(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore marker write failure
        }
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        int newlineIdx = text.IndexOfAny(['\r', '\n']);
        return newlineIdx >= 0 ? text[..newlineIdx].Trim() : text.Trim();
    }

    [GeneratedRegex(@"https://accounts\.google\.com/[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleOAuthUrlRegex();

    [GeneratedRegex(@"[?&]code=(?<code>[^&#\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCodeQueryParamRegex();

    [GeneratedRegex(@"\[ACTION:(?<kind>[A-Za-z0-9_]+)(?:\|(?<params>[^\]]*))?\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActionTagRegex();

    [GeneratedRegex(@"(?:đăng(?:\s+bài)?\s+(?:lên|vào)|publish\s+to)\s+(?<count>\d+)\s+(?:nhóm|groups?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NaturalPublishGroupsRegex();
}
