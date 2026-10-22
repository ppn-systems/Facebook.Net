// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Meta.UI.Core.Configuration;

namespace Meta.UI.Core.AiAssistant;

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
    string? GroupKeyword = null,
    string? TargetId = null);

/// <summary>
/// Represents a context memory item silently learned by the AI assistant via <c>[LEARN:Category|Key|Content]</c>.
/// </summary>
public sealed record AgyLearnedMemoryItem(
    AiMemoryCategory Category,
    string Key,
    string Content,
    int Importance = 4);

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
    string? ConversationId = null,
    IReadOnlyList<AgyLearnedMemoryItem>? LearnedMemories = null);

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

    private static readonly string s_agyOAuthClientId = string.Concat(
        "1071006060591",
        "-",
        "tmhssin2h21lcre235vtolojh4g403ep",
        ".apps.",
        "googleusercontent.com");
    private static readonly string s_agyOAuthClientSecret = string.Concat(
        "GOC",
        "SPX-",
        "K58FWR486LdLJ1m",
        "LB8sXC4z6qDAf");
    private const string AgyOAuthRedirectUri = "https://antigravity.google/oauth-callback";
    private const string AgyOAuthTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string AgyKeyringTargetName = "gemini:antigravity";
    private const string AgyKeyringUserName = "antigravity";

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly HttpClient s_oauthHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _dataDirectory;
    private readonly Lock _oauthProcessLock = new();
    private readonly SemaphoreSlim _warmSessionGate = new(1, 1);
    private Process? _pendingOAuthProcess;
    private string? _pendingPkceCodeVerifier;
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

        string[] unifiedCandidates = OperatingSystem.IsWindows()
            ? ["FacebookOneClient.exe"]
            : ["FacebookOneClient", "FacebookOneClient.exe"];

        if (!string.IsNullOrWhiteSpace(processDir))
        {
            for (int i = 0; i < unifiedCandidates.Length; i++)
            {
                string candidate = Path.Combine(processDir, unifiedCandidates[i]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        for (int i = 0; i < unifiedCandidates.Length; i++)
        {
            string candidate = Path.Combine(baseDir, unifiedCandidates[i]);
            if (File.Exists(candidate))
            {
                return candidate;
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
                OperatingSystem.IsWindows() ? "FacebookOneClient.exe" : "FacebookOneClient");
        }

        bool isDll = resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        bool isUnifiedClient = string.Equals(
            Path.GetFileNameWithoutExtension(resolvedPath),
            "FacebookOneClient",
            StringComparison.OrdinalIgnoreCase);
        string command = isDll ? "dotnet" : resolvedPath;
        string[] args = isDll ? [resolvedPath] : (isUnifiedClient ? ["--mcp"] : []);

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
                    : (isUnifiedClient
                        ? ["mcp", "add", "facebook", resolvedPath, "--mcp"]
                        : ["mcp", "add", "facebook", resolvedPath]);
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

        string codeVerifier = GeneratePkceCodeVerifier();
        string codeChallenge = ComputePkceCodeChallenge(codeVerifier);
        string state = GeneratePkceState();
        lock (_oauthProcessLock)
        {
            _pendingPkceCodeVerifier = codeVerifier;
        }

        string oauthUrl = BuildGoogleOAuthAuthorizeUrl(codeChallenge, state);
        string? baselineClipboard = TryReadWindowsClipboardText();

        statusProgress?.Report("Đang mở liên kết xác thực Google trên trình duyệt của bạn...");
        OpenUrlInDefaultSystemBrowser(oauthUrl);

        statusProgress?.Report("Chọn tài khoản Google trên trình duyệt và sao chép hoặc dán mã xác thực vào bên dưới...");

        TimeSpan maxWait = TimeSpan.FromSeconds(180);
        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed < maxWait && !ct.IsCancellationRequested)
        {
            if (_hasAuthenticatedInSession || this.CheckIsAuthenticated())
            {
                _hasAuthenticatedInSession = true;
                return (true, "Đăng nhập Google Antigravity thành công!", null);
            }

            string? extractedCode = TryExtractOAuthCodeFromClipboard(ref baselineClipboard);
            if (string.IsNullOrWhiteSpace(extractedCode))
            {
                extractedCode = TryExtractOAuthCodeFromAllSystemProfiles(customProfileDir, state);
            }

            string? cleanedCode = NormalizeAuthCode(extractedCode);
            if (!string.IsNullOrWhiteSpace(cleanedCode))
            {
                statusProgress?.Report("Đã nhận mã OAuth! Đang trao đổi token với Google...");
                (bool exchanged, string? exchangeError) = await this.ExchangeCodeAndSaveToKeyringAsync(
                    cleanedCode,
                    codeVerifier,
                    ct).ConfigureAwait(false);

                if (exchanged)
                {
                    _hasAuthenticatedInSession = true;
                    this.ResetWarmSession();
                    statusProgress?.Report("Đăng nhập Google Antigravity thành công!");
                    return (true, "Đăng nhập Google Antigravity thành công!", null);
                }

                string errMsg = !string.IsNullOrWhiteSpace(exchangeError)
                    ? $"Không thể đổi mã xác thực OAuth: {exchangeError}"
                    : "Không thể đổi mã xác thực OAuth. Vui lòng thử lại.";
                statusProgress?.Report(errMsg);
                return (false, errMsg, oauthUrl);
            }

            await Task.Delay(450, ct).ConfigureAwait(false);
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

        string? codeVerifier;
        Process? activeProcess;
        lock (_oauthProcessLock)
        {
            codeVerifier = _pendingPkceCodeVerifier;
            activeProcess = _pendingOAuthProcess ?? _warmProcess;
        }

        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            (bool exchanged, _) = await this.ExchangeCodeAndSaveToKeyringAsync(
                cleanedCode,
                codeVerifier,
                ct).ConfigureAwait(false);
            if (exchanged)
            {
                _hasAuthenticatedInSession = true;
                this.ResetWarmSession();
                return true;
            }
        }

        if (activeProcess is not null)
        {
            try
            {
                if (!activeProcess.HasExited)
                {
                    _ = TryWriteLineToProcessConIn((uint)activeProcess.Id, cleanedCode);
                    await activeProcess.StandardInput.WriteAsync((cleanedCode + "\n").AsMemory(), ct).ConfigureAwait(false);
                    await activeProcess.StandardInput.FlushAsync(ct).ConfigureAwait(false);

                    for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
                    {
                        await Task.Delay(400, ct).ConfigureAwait(false);
                        if (this.CheckIsAuthenticated())
                        {
                            _hasAuthenticatedInSession = true;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ignore stdin write failure
            }
        }

        if (this.CheckIsAuthenticated())
        {
            _hasAuthenticatedInSession = true;
            return true;
        }

        return false;
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

        if (LooksLikeGoogleOAuthCodeOrCallbackUrl(userPrompt))
        {
            bool manualAuthOk = await this.SubmitManualOAuthCodeAsync(userPrompt, ct).ConfigureAwait(false);
            if (manualAuthOk)
            {
                return new AgyPromptResult(
                    IsSuccess: true,
                    ResponseText: "Đã xác thực Google Antigravity CLI thành công bằng mã OAuth vừa nhập! Bạn có thể tiếp tục trò chuyện ngay bây giờ.",
                    ProposedAction: null,
                    ErrorMessage: null);
            }
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

        if (!this.CheckIsAuthenticated())
        {
            this.DisposeWarmProcessCore();
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Phiên đăng nhập Google Antigravity CLI chưa được xác thực hoặc đã đăng xuất. Vui lòng đăng nhập Google OAuth để tiếp tục.");
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
                bool systemPromptChanged = !string.Equals(_warmLastSystemPrompt, normalizedSysPrompt, StringComparison.Ordinal);

                string turnPrompt;
                if (!_warmSessionHasSystemPrompt)
                {
                    turnPrompt = BuildCompositePrompt(userPrompt.Trim(), systemContextPrompt);
                }
                else if (systemPromptChanged && !string.IsNullOrWhiteSpace(normalizedSysPrompt))
                {
                    turnPrompt = BuildUpdatedContextFollowUpPrompt(userPrompt.Trim(), normalizedSysPrompt);
                }
                else
                {
                    turnPrompt = BuildFollowUpTurnPrompt(userPrompt.Trim());
                }

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

                (string cleanText, AgyProposedAction? proposedAction, IReadOnlyList<AgyLearnedMemoryItem> learnedMemories) =
                    ParseResponseActionAndMemories(rawOutput);

                return new AgyPromptResult(
                    IsSuccess: true,
                    ResponseText: cleanText,
                    ProposedAction: proposedAction,
                    ErrorMessage: null,
                    ConversationId: detectedConvId,
                    LearnedMemories: learnedMemories);
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
    /// Parses an AI response string, extracting and stripping any silent <c>[LEARN:Category|Key|Content]</c> tags
    /// and trailing <c>[ACTION:...]</c> tag into structured objects so neither tag is ever visible to the user.
    /// </summary>
    public static (string CleanText, AgyProposedAction? ProposedAction, IReadOnlyList<AgyLearnedMemoryItem> LearnedMemories) ParseResponseActionAndMemories(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (string.Empty, null, Array.Empty<AgyLearnedMemoryItem>());
        }

        List<AgyLearnedMemoryItem> learned = new();
        MatchCollection learnMatches = LearnTagRegex().Matches(rawResponse);
        for (int i = 0; i < learnMatches.Count; i++)
        {
            Match m = learnMatches[i];
            if (!m.Success)
            {
                continue;
            }

            string catRaw = m.Groups["category"].Value.Trim();
            string key = m.Groups["key"].Value.Trim();
            string content = m.Groups["content"].Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (!Enum.TryParse<AiMemoryCategory>(catRaw, ignoreCase: true, out AiMemoryCategory category))
            {
                category = AiMemoryCategory.General;
            }

            learned.Add(new AgyLearnedMemoryItem(category, key, content, Importance: 4));
        }

        string withoutLearnTags = learnMatches.Count > 0
            ? LearnTagRegex().Replace(rawResponse, string.Empty)
            : rawResponse;

        (string cleanText, AgyProposedAction? proposedAction) = ParseResponseAndProposedAction(withoutLearnTags);
        return (cleanText, proposedAction, learned);
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

        string strippedLearn = LearnTagRegex().Replace(rawResponse, string.Empty);
        Match match = ActionTagRegex().Match(strippedLearn);
        if (!match.Success)
        {
            Match naturalGroupMatch = NaturalPublishGroupsRegex().Match(strippedLearn);
            if (naturalGroupMatch.Success &&
                int.TryParse(naturalGroupMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int naturalCount) &&
                naturalCount > 0)
            {
                int clampedNaturalCount = Math.Clamp(naturalCount, 1, 500);
                AgyProposedAction fallbackAction = new(
                    ActionKind: "PUBLISH_GROUPS",
                    SummaryText: FirstLine(strippedLearn.Trim()),
                    ContentText: strippedLearn.Trim(),
                    TargetGroupCount: clampedNaturalCount,
                    MinDelaySeconds: 2,
                    MaxDelaySeconds: 5,
                    GroupKeyword: null,
                    TargetId: null);
                return (strippedLearn.Trim(), fallbackAction);
            }

            return (strippedLearn.Trim(), null);
        }

        string actionKind = match.Groups["kind"].Value.Trim().ToUpperInvariant();
        string paramsRaw = match.Groups["params"].Value;

        int groupCount = 0;
        int minDelay = 2;
        int maxDelay = 5;
        string? content = null;
        string? keyword = null;
        string? targetId = null;

        List<string> pairs = SplitActionTagParameters(paramsRaw);
        for (int i = 0; i < pairs.Count; i++)
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
            else if (key.Equals("targetId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("postId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("tagId", StringComparison.OrdinalIgnoreCase))
            {
                targetId = val;
            }
        }

        if (maxDelay < minDelay)
        {
            maxDelay = minDelay;
        }

        string cleanText = strippedLearn.Remove(match.Index, match.Length).Trim();
        string summaryText = !string.IsNullOrWhiteSpace(cleanText)
            ? FirstLine(cleanText)
            : actionKind switch
            {
                "PUBLISH_GROUPS" => $"Đăng bài lên {groupCount} nhóm (delay {minDelay}s - {maxDelay}s)",
                "SEARCH_GROUPS" => $"Tìm kiếm nhóm theo từ khóa '{keyword}'",
                "SCAN_LEADS" => $"Quét khách hàng tiềm năng theo từ khóa '{keyword}'",
                "FILTER_LEADS" => $"Lọc danh sách khách hàng trên bảng ({keyword ?? targetId})",
                "COMMENT_LEAD" => $"Gửi bình luận tư vấn cho bài {targetId}",
                "OPEN_GROUP_TAGS" => "Mở Quản lý Nhãn màu Hội nhóm",
                "BUMP_POSTS" => "Đẩy Top các bài viết vừa đăng",
                _ => $"Thực thi tác vụ {actionKind}"
            };

        AgyProposedAction proposed = new(
            ActionKind: actionKind,
            SummaryText: summaryText,
            ContentText: content,
            TargetGroupCount: groupCount,
            MinDelaySeconds: minDelay,
            MaxDelaySeconds: maxDelay,
            GroupKeyword: keyword,
            TargetId: targetId);

        return (cleanText, proposed);
    }

    private static List<string> SplitActionTagParameters(string paramsRaw)
    {
        List<string> results = new();
        if (string.IsNullOrWhiteSpace(paramsRaw))
        {
            return results;
        }

        int braceDepth = 0;
        int segmentStart = 0;
        for (int i = 0; i < paramsRaw.Length; i++)
        {
            char c = paramsRaw[i];
            if (c == '{')
            {
                braceDepth++;
            }
            else if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (c == '|' && braceDepth == 0)
            {
                string segment = paramsRaw[segmentStart..i].Trim();
                if (!string.IsNullOrEmpty(segment))
                {
                    results.Add(segment);
                }

                segmentStart = i + 1;
            }
        }

        if (segmentStart < paramsRaw.Length)
        {
            string tail = paramsRaw[segmentStart..].Trim();
            if (!string.IsNullOrEmpty(tail))
            {
                results.Add(tail);
            }
        }

        return results;
    }

    private static string StripActionTagPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = ActionTagRegex().Replace(text, string.Empty);
        cleaned = LearnTagRegex().Replace(cleaned, string.Empty);

        int partialActionIdx = cleaned.LastIndexOf("[ACTION:", StringComparison.OrdinalIgnoreCase);
        if (partialActionIdx >= 0)
        {
            cleaned = cleaned[..partialActionIdx];
        }

        int partialLearnIdx = cleaned.LastIndexOf("[LEARN:", StringComparison.OrdinalIgnoreCase);
        if (partialLearnIdx >= 0)
        {
            cleaned = cleaned[..partialLearnIdx];
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

        _ = sb.AppendLine("[MCP TOOLS, SILENT SELF-LEARNING & ACTION TAGS]");
        _ = sb.AppendLine("- Workspace state, saved templates, target groups, group tags, lead tags, and AI memories are ALREADY pre-loaded above. Do NOT call read-only MCP tools (`fb_memory_search`, `fb_list_templates`, `fb_list_target_groups`, `fb_list_accounts`, `fb_memory_get_context`) unless explicitly asked for data not in the snapshot (or `fb_memory_save` / `fb_memory_delete` when explicitly managing memories).");
        _ = sb.AppendLine("- SILENT SELF-LEARNING: Whenever the user shares product info, pricing, target audience, tone preferences, or when you discover high-performing groups/keywords during analysis, silently append `[LEARN:Category|Key|Content]` (`ProductKnowledge`, `WritingStyle`, `CampaignRule`, `GroupStrategy`, `LeadPlaybook`, or `UserPreference`) at the end of your reply. NEVER tell the user you saved or learned something into memory.");
        _ = sb.AppendLine("- ACCOUNTS & PROFILES: `fb_list_accounts`, `fb_switch_account`, `fb_import_session`, `fb_check_account_health`, `fb_set_account_proxy`, `fb_delete_account`, `fb_warmup_account`, `fb_open_browser_login`, `fb_list_browser_profiles`, `fb_create_or_assign_browser_profile`, `fb_delete_browser_profile`.");
        _ = sb.AppendLine("- GROUP & CUSTOMER CRM TAGGING: `fb_clean_and_classify_all_groups`, `fb_list_target_groups`, `fb_save_target_group`, `fb_remove_target_group`, `fb_configure_target_groups_selection`, `fb_sync_joined_groups`, `fb_search_groups`, `fb_join_group`, `fb_batch_join_groups`, `fb_list_group_tags`, `fb_create_group_tag`, `fb_create_group_tags_batch`, `fb_auto_tag_groups_by_keywords`, `fb_assign_group_tag`, `fb_delete_group_tag` for groups; `fb_list_lead_tags`, `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, `fb_auto_tag_leads_by_rules`, `fb_delete_lead_tag`, `fb_update_lead_notes` for customer/lead tags and CRM notes.");
        _ = sb.AppendLine("- ANALYTICS, LEADS, CAMPAIGNS & SETTINGS: `fb_analyze_workspace_data`, `fb_scrape_group_feed`, `fb_scrape_target_groups_batch`, `fb_query_scraped_leads`, `fb_search_leads_advanced`, `fb_auto_comment_leads_batch`, `fb_delete_scraped_leads`, `fb_get_single_post`, `fb_publish_post`, `fb_publish_campaign_batch`, `fb_sync_personal_timeline_posts`, `fb_repost_to_groups`, `fb_create_comment`, `fb_react_to_post`, `fb_list_published_posts`, `fb_delete_published_post`, `fb_list_templates`, `fb_save_template`, `fb_delete_template`, `fb_bump_recent_posts`, `fb_get_app_settings`, `fb_update_app_settings`, `fb_get_storage_usage`, `fb_clean_browser_cache`, `fb_get_audit_logs`, `fb_clear_audit_logs`, `fb_check_app_updates`.");
        _ = sb.AppendLine("- INTERACTIVE ACTION TAGS: Append at most one `[ACTION:...]` tag at the very end of your reply: `[ACTION:PUBLISH_GROUPS|count=<int>|minDelay=<sec>|maxDelay=<sec>|content=<full_post_text>]`, `[ACTION:SEARCH_GROUPS|keyword=<text>]`, `[ACTION:SCAN_LEADS|keyword=<text>]`, `[ACTION:FILTER_LEADS|tagId=<lead_tag_id>|keyword=<text>]`, `[ACTION:COMMENT_LEAD|targetId=<post_id>|content=<comment_spintax>]`, `[ACTION:OPEN_GROUP_TAGS|targetId=<group_tag_id>]`, or `[ACTION:BUMP_POSTS|content=<bump_spintax>]`.");
        _ = sb.AppendLine("- Always reply concisely in the exact language of the user's message.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("[USER]");
        _ = sb.Append(userPrompt);
        return sb.ToString();
    }

    private static string BuildFollowUpTurnPrompt(string userPrompt) => userPrompt.Trim();

    private static string BuildUpdatedContextFollowUpPrompt(string userPrompt, string updatedSystemContextPrompt)
    {
        StringBuilder sb = new();
        int workspaceMarkerIdx = updatedSystemContextPrompt.IndexOf("[CURRENT WORKSPACE STATE]", StringComparison.Ordinal);
        if (workspaceMarkerIdx >= 0)
        {
            _ = sb.AppendLine(updatedSystemContextPrompt[workspaceMarkerIdx..].Trim());
        }
        else
        {
            _ = sb.AppendLine(updatedSystemContextPrompt.Trim());
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine("[USER]");
        _ = sb.Append(userPrompt.Trim());
        return sb.ToString();
    }

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

                    if (errLine.Contains("Authentication required", StringComparison.OrdinalIgnoreCase) ||
                        errLine.Contains("paste the authorization code", StringComparison.OrdinalIgnoreCase) ||
                        errLine.Contains("You are not logged into Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        _hasAuthenticatedInSession = false;
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or ObjectDisposedException)
                        {
                            // Ignore termination error
                        }

                        break;
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
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_API_KEY")))
        {
            return true;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        CleanupLegacyFakeOAuthMarker(Path.Combine(userProfile, ".gemini", "antigravity-cli", "oauth_creds.json"));

        if (OperatingSystem.IsWindows())
        {
            if (HasWindowsKeyringAgyToken())
            {
                return true;
            }
        }
        else if (_hasAuthenticatedInSession)
        {
            return true;
        }

        string[] credFiles =
        [
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "oauth_creds.json"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "credentials.json"),
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "auth.json"),
            Path.Combine(userProfile, ".gemini", "oauth_creds.json"),
            Path.Combine(userProfile, ".gemini", "credentials.json"),
            Path.Combine(userProfile, ".gemini", "auth.json")
        ];

        for (int i = 0; i < credFiles.Length; i++)
        {
            try
            {
                FileInfo info = new(credFiles[i]);
                if (info.Exists && info.Length > 20)
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
        string schemaCacheDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "mcp", "facebook");
        if (Directory.Exists(schemaCacheDir))
        {
            string requiredAnalyticsSchema = Path.Combine(schemaCacheDir, "fb_analyze_workspace_data.json");
            string requiredClassifySchema = Path.Combine(schemaCacheDir, "fb_clean_and_classify_all_groups.json");
            string requiredSettingsSchema = Path.Combine(schemaCacheDir, "fb_update_app_settings.json");
            string requiredBatchPublishSchema = Path.Combine(schemaCacheDir, "fb_publish_campaign_batch.json");
            if (!File.Exists(requiredAnalyticsSchema) ||
                !File.Exists(requiredClassifySchema) ||
                !File.Exists(requiredSettingsSchema) ||
                !File.Exists(requiredBatchPublishSchema))
            {
                return false;
            }
        }

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

    private static void OpenUrlInDefaultSystemBrowser(string oauthUrl)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(oauthUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    ProcessStartInfo cmdPsi = new()
                    {
                        FileName = "cmd.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    cmdPsi.ArgumentList.Add("/c");
                    cmdPsi.ArgumentList.Add("start");
                    cmdPsi.ArgumentList.Add(string.Empty);
                    cmdPsi.ArgumentList.Add(oauthUrl);
                    using Process? fallback = Process.Start(cmdPsi);
                }
                catch (Exception innerEx) when (innerEx is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ignore browser open failure
                }
            }
        }
    }

    private static string? TryExtractOAuthCodeFromAllSystemProfiles(string? preferredProfileDir, string expectedState)
    {
        if (!string.IsNullOrWhiteSpace(preferredProfileDir))
        {
            string? fromPreferred = TryExtractOAuthCodeFromSystemProfile(preferredProfileDir.Trim(), expectedState);
            if (!string.IsNullOrWhiteSpace(fromPreferred))
            {
                return fromPreferred;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] roots =
            [
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")
            ];

            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root))
                {
                    continue;
                }

                string defDir = Path.Combine(root, "Default");
                if (Directory.Exists(defDir))
                {
                    string? code = TryExtractOAuthCodeFromSystemProfile(defDir, expectedState);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        return code;
                    }
                }

                try
                {
                    string[] profileDirs = Directory.GetDirectories(root, "Profile *");
                    for (int p = 0; p < profileDirs.Length; p++)
                    {
                        string? code = TryExtractOAuthCodeFromSystemProfile(profileDirs[p], expectedState);
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            return code;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore inaccessible profile folders
                }
            }
        }

        return null;
    }

    private static string? TryExtractOAuthCodeFromSystemProfile(string profileDir, string expectedState)
    {
        if (string.IsNullOrWhiteSpace(profileDir) || string.IsNullOrWhiteSpace(expectedState) || !Directory.Exists(profileDir))
        {
            return null;
        }

        string searchProfileDir = profileDir;
        if (!Directory.Exists(Path.Combine(searchProfileDir, "Sessions")) &&
            Directory.Exists(Path.Combine(searchProfileDir, "Default", "Sessions")))
        {
            searchProfileDir = Path.Combine(searchProfileDir, "Default");
        }

        List<string> candidateFiles = new(8);
        string sessionsDir = Path.Combine(searchProfileDir, "Sessions");
        if (Directory.Exists(sessionsDir))
        {
            try
            {
                string[] sessionFiles = Directory.GetFiles(sessionsDir);
                Array.Sort(sessionFiles, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                for (int i = 0; i < sessionFiles.Length && i < 4; i++)
                {
                    candidateFiles.Add(sessionFiles[i]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible Sessions directory
            }
        }

        string histJournal = Path.Combine(searchProfileDir, "History-journal");
        if (File.Exists(histJournal))
        {
            candidateFiles.Add(histJournal);
        }

        string histWal = Path.Combine(searchProfileDir, "History-wal");
        if (File.Exists(histWal))
        {
            candidateFiles.Add(histWal);
        }

        string histMain = Path.Combine(searchProfileDir, "History");
        if (File.Exists(histMain))
        {
            candidateFiles.Add(histMain);
        }

        for (int i = 0; i < candidateFiles.Count; i++)
        {
            string? code = TryScanFileTailForOAuthCallbackCode(candidateFiles[i], expectedState);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        return null;
    }

    private static string? TryScanFileTailForOAuthCallbackCode(string filePath, string expectedState)
    {
        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = fs.Length;
            if (length <= 0)
            {
                return null;
            }

            const int maxReadBytes = 2 * 1024 * 1024; // Read at most the last 2 MB
            int bytesToRead = (int)Math.Min(length, maxReadBytes);
            if (length > bytesToRead)
            {
                _ = fs.Seek(length - bytesToRead, SeekOrigin.Begin);
            }

            byte[] buffer = new byte[bytesToRead];
            int totalRead = 0;
            while (totalRead < bytesToRead)
            {
                int read = fs.Read(buffer, totalRead, bytesToRead - totalRead);
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            string rawLatin1 = Encoding.Latin1.GetString(buffer, 0, totalRead);
            if (!rawLatin1.Contains(expectedState, StringComparison.Ordinal))
            {
                return null;
            }

            MatchCollection matches = OAuthCallbackUrlInBinaryRegex().Matches(rawLatin1);
            for (int m = matches.Count - 1; m >= 0; m--)
            {
                string url = matches[m].Value;
                if (!url.Contains(expectedState, StringComparison.Ordinal))
                {
                    continue;
                }

                Match codeMatch = UrlCodeQueryParamRegex().Match(url);
                if (codeMatch.Success)
                {
                    string rawCode = Uri.UnescapeDataString(codeMatch.Groups["code"].Value.Trim());
                    if (rawCode.StartsWith("4/", StringComparison.Ordinal) && rawCode.Length >= 20)
                    {
                        return rawCode;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Ignore locked or transient file errors
        }

        return null;
    }

    private static string? TryExtractOAuthCodeFromClipboard(ref string? baselineClipboard)
    {
        string? currentClip = TryReadWindowsClipboardText();
        if (string.IsNullOrWhiteSpace(currentClip) ||
            string.Equals(currentClip, baselineClipboard, StringComparison.Ordinal))
        {
            return null;
        }

        string trimmed = currentClip.Trim();
        if (LooksLikeGoogleOAuthCodeOrCallbackUrl(trimmed))
        {
            baselineClipboard = currentClip;
            return NormalizeAuthCode(trimmed);
        }

        return null;
    }

    private static string? TryReadWindowsClipboardText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            IntPtr hData = GetClipboardData(13u); // CF_UNICODETEXT
            if (hData == IntPtr.Zero)
            {
                return null;
            }

            IntPtr pText = GlobalLock(hData);
            if (pText == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pText);
            }
            finally
            {
                _ = GlobalUnlock(hData);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or AccessViolationException)
        {
            return null;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool LooksLikeGoogleOAuthCodeOrCallbackUrl(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        string trimmed = rawInput.Trim();
        if (trimmed.Contains(' ', StringComparison.Ordinal) ||
            trimmed.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith("4/0A", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 24)
        {
            return true;
        }

        if ((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            trimmed.Contains("code=4/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string GeneratePkceCodeVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return ToBase64Url(bytes);
    }

    private static string ComputePkceCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return ToBase64Url(hash);
    }

    private static string GeneratePkceState()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(16);
        return ToBase64Url(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string BuildGoogleOAuthAuthorizeUrl(string codeChallenge, string state)
    {
        const string scopes =
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fcloud-platform+" +
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fuserinfo.email+" +
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fuserinfo.profile+" +
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fcclog+" +
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fexperimentsandconfigs+" +
            "https%3A%2F%2Fwww.googleapis.com%2Fauth%2Faicode+openid";

        return "https://accounts.google.com/o/oauth2/auth" +
               "?access_type=offline" +
               $"&client_id={Uri.EscapeDataString(s_agyOAuthClientId)}" +
               $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
               "&code_challenge_method=S256" +
               "&prompt=consent" +
               $"&redirect_uri={Uri.EscapeDataString(AgyOAuthRedirectUri)}" +
               "&response_type=code" +
               $"&scope={scopes}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    private async Task<(bool Success, string? ErrorMessage)> ExchangeCodeAndSaveToKeyringAsync(
        string authCode,
        string codeVerifier,
        CancellationToken ct)
    {
        try
        {
            KeyValuePair<string, string>[] formFields =
            [
                new("client_id", s_agyOAuthClientId),
                new("client_secret", s_agyOAuthClientSecret),
                new("code", authCode),
                new("code_verifier", codeVerifier),
                new("grant_type", "authorization_code"),
                new("redirect_uri", AgyOAuthRedirectUri)
            ];

            using FormUrlEncodedContent content = new(formFields);
            using HttpResponseMessage response = await s_oauthHttpClient
                .PostAsync(AgyOAuthTokenEndpoint, content, ct)
                .ConfigureAwait(false);

            string responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {(int)response.StatusCode}: {FirstLine(responseJson)}");
            }

            using JsonDocument doc = JsonDocument.Parse(responseJson);
            JsonElement root = doc.RootElement;
            string? accessToken = TryGetStringProperty(root, "access_token");
            string? refreshToken = TryGetStringProperty(root, "refresh_token");
            string tokenType = TryGetStringProperty(root, "token_type") ?? "Bearer";
            string idToken = TryGetStringProperty(root, "id_token") ?? string.Empty;
            int expiresIn = 3599;
            if (root.TryGetProperty("expires_in", out JsonElement expEl) && expEl.TryGetInt32(out int parsedExp) && parsedExp > 0)
            {
                expiresIn = parsedExp;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, "Phản hồi OAuth không chứa access_token.");
            }

            string expiryIso = DateTimeOffset.Now.AddSeconds(expiresIn).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("token");
                writer.WriteStartObject();
                writer.WriteString("access_token", accessToken);
                writer.WriteString("token_type", tokenType);
                writer.WriteString("refresh_token", refreshToken ?? string.Empty);
                writer.WriteString("expiry", expiryIso);
                writer.WriteEndObject();
                writer.WriteString("auth_method", "consumer");
                writer.WriteString("id_token", idToken);
                writer.WriteEndObject();
            }

            byte[] keyringBytes = ms.ToArray();
            if (OperatingSystem.IsWindows())
            {
                if (!WriteWindowsKeyringAgyToken(keyringBytes))
                {
                    return (false, "Không thể lưu token vào Windows Credential Manager.");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string jsonPayload = Encoding.UTF8.GetString(keyringBytes);
                _ = await RunProcessCaptureAsync(
                    "/usr/bin/security",
                    ["add-generic-password", "-U", "-s", AgyKeyringTargetName, "-a", AgyKeyringUserName, "-w", jsonPayload],
                    ct).ConfigureAwait(false);
            }

            lock (_oauthProcessLock)
            {
                if (string.Equals(_pendingPkceCodeVerifier, codeVerifier, StringComparison.Ordinal))
                {
                    _pendingPkceCodeVerifier = null;
                }
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return (false, ex.Message);
        }
    }

    private static void CleanupLegacyFakeOAuthMarker(string credsPath)
    {
        try
        {
            if (!File.Exists(credsPath))
            {
                return;
            }

            string json = File.ReadAllText(credsPath);
            if (json.Contains("\"oauth_cdp\"", StringComparison.Ordinal))
            {
                File.Delete(credsPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore cleanup failure
        }
    }

    private static bool HasWindowsKeyringAgyToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!CredReadW(AgyKeyringTargetName, 1u, 0u, out IntPtr credPtr) || credPtr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            NativeCredential cred = Marshal.PtrToStructure<NativeCredential>(credPtr);
            if (cred.CredentialBlobSize < 20 || cred.CredentialBlob == IntPtr.Zero)
            {
                return false;
            }

            byte[] blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, (int)cred.CredentialBlobSize);
            string json = Encoding.UTF8.GetString(blob);
            return json.Contains("\"access_token\"", StringComparison.Ordinal) ||
                   json.Contains("\"refresh_token\"", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return false;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    private static bool WriteWindowsKeyringAgyToken(byte[] utf8JsonBytes)
    {
        if (!OperatingSystem.IsWindows() || utf8JsonBytes.Length == 0)
        {
            return false;
        }

        IntPtr blobPtr = Marshal.AllocHGlobal(utf8JsonBytes.Length);
        try
        {
            Marshal.Copy(utf8JsonBytes, 0, blobPtr, utf8JsonBytes.Length);
            NativeCredential cred = new()
            {
                Flags = 0u,
                Type = 1u, // CRED_TYPE_GENERIC
                TargetName = AgyKeyringTargetName,
                Comment = null,
                LastWritten = default,
                CredentialBlobSize = (uint)utf8JsonBytes.Length,
                CredentialBlob = blobPtr,
                Persist = 2u, // CRED_PERSIST_LOCAL_MACHINE
                AttributeCount = 0u,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = AgyKeyringUserName
            };

            return CredWriteW(ref cred, 0u);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    private static bool TryWriteLineToProcessConIn(uint processId, string text)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            _ = FreeConsole();
            if (!AttachConsole(processId))
            {
                return false;
            }

            IntPtr hConIn = CreateFileW("CONIN$", 0x80000000u | 0x40000000u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
            if (hConIn == IntPtr.Zero || hConIn == new IntPtr(-1))
            {
                _ = FreeConsole();
                return false;
            }

            try
            {
                string full = text + "\r\n";
                NativeInputRecord[] records = new NativeInputRecord[full.Length * 2];
                for (int i = 0; i < full.Length; i++)
                {
                    char ch = full[i];
                    ushort vk = (ch == '\r' || ch == '\n') ? (ushort)0x0D : (ushort)0;
                    ushort scan = (ch == '\r' || ch == '\n') ? (ushort)0x1C : (ushort)0;
                    records[i * 2] = new NativeInputRecord
                    {
                        EventType = 1,
                        KeyEvent = new NativeKeyEventRecord
                        {
                            bKeyDown = 1,
                            wRepeatCount = 1,
                            wVirtualKeyCode = vk,
                            wVirtualScanCode = scan,
                            UnicodeChar = ch,
                            dwControlKeyState = 0
                        }
                    };
                    records[(i * 2) + 1] = new NativeInputRecord
                    {
                        EventType = 1,
                        KeyEvent = new NativeKeyEventRecord
                        {
                            bKeyDown = 0,
                            wRepeatCount = 1,
                            wVirtualKeyCode = vk,
                            wVirtualScanCode = scan,
                            UnicodeChar = ch,
                            dwControlKeyState = 0
                        }
                    };
                }

                return WriteConsoleInputW(hConIn, records, (uint)records.Length, out _);
            }
            finally
            {
                _ = CloseHandle(hConIn);
                _ = FreeConsole();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputRecord
    {
        [FieldOffset(0)]
        public ushort EventType;

        [FieldOffset(4)]
        public NativeKeyEventRecord KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeKeyEventRecord
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

#pragma warning disable SYSLIB1054 // Struct marshalling for CREDENTIALW and INPUT_RECORD uses classic DllImport
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref NativeCredential userCredential, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WriteConsoleInputW(
        IntPtr hConsoleInput,
        NativeInputRecord[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsWritten);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);
#pragma warning restore SYSLIB1054

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        int newlineIdx = text.IndexOfAny(['\r', '\n']);
        return newlineIdx >= 0 ? text[..newlineIdx].Trim() : text.Trim();
    }

    [GeneratedRegex(@"antigravity\.google/oauth-callback\?[^\x00-\x20""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OAuthCallbackUrlInBinaryRegex();

    [GeneratedRegex(@"[?&]code=(?<code>[^&#\s\x00-\x1f""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCodeQueryParamRegex();

    [GeneratedRegex(@"\[ACTION:(?<kind>[A-Za-z0-9_]+)(?:\|(?<params>[^\]]*))?\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActionTagRegex();

    [GeneratedRegex(@"\[LEARN:(?<category>[A-Za-z0-9_]+)\|(?<key>[^\|\]\r\n]+)\|(?<content>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LearnTagRegex();

    [GeneratedRegex(@"(?:đăng(?:\s+bài)?\s+(?:lên|vào)|publish\s+to)\s+(?<count>\d+)\s+(?:nhóm|groups?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NaturalPublishGroupsRegex();
}
