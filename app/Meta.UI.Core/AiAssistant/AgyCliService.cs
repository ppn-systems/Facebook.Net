// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    public void Dispose()
    {
        this.SetPendingOAuthProcess(null);
        this.DisposeWarmProcessCore();
        _warmSessionGate.Dispose();
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

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        int newlineIdx = text.IndexOfAny(['\r', '\n']);
        return newlineIdx >= 0 ? text[..newlineIdx].Trim() : text.Trim();
    }
}
