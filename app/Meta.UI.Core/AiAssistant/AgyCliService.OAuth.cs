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

public sealed partial class AgyCliService
{
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

    [GeneratedRegex(@"antigravity\.google/oauth-callback\?[^\x00-\x20""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OAuthCallbackUrlInBinaryRegex();

    [GeneratedRegex(@"[?&]code=(?<code>[^&#\s\x00-\x1f""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCodeQueryParamRegex();
}
