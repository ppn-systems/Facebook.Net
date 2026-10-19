// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Text;
using System.Text.Json;

namespace Facebook.Net.UI.Core.Importing;

/// <summary>
/// Result of parsing a J2Team Cookies JSON export, Cookie-Editor JSON array,
/// pipe-delimited account line (<c>UID|Pass|2FA|Cookie</c>), or raw semicolon Cookie string.
/// Automatically resolves or generates the internal account identifier so the user never has to type a UID.
/// </summary>
public sealed record ImportedSessionBundle(
    string AccountUid,
    string SuggestedDisplayName,
    string CookieHeader,
    string? UserAgent,
    string? TwoFactorSecret);

/// <summary>
/// Universal zero-UID session and cookie importer compatible with:
/// 1. J2TEAM Cookies JSON (<c>{ "url": "...", "cookies": [ { "name": "c_user", "value": "..." } ] }</c>)
/// 2. EditThisCookie / Cookie-Editor JSON array (<c>[ { "name": "c_user", "value": "..." } ]</c>)
/// 3. MaxCare / MKT / Get Token Cookie pipe format (<c>UID|Pass|2FA|c_user=...; xs=...</c>)
/// 4. Raw HTTP Cookie header string (<c>sb=...; datr=...; c_user=1000...; xs=...</c>)
/// 5. Interactive Browser Login placeholder (when input is empty, creates a clean browser profile that syncs UID on login).
/// </summary>
public static class UniversalSessionImporter
{
    /// <summary>
    /// Parses a user-friendly proxy specification string (<c>host:port</c>, <c>host:port:user:pass</c>,
    /// <c>http://host:port</c>, or <c>socks5://user:pass@host:port</c>) into a <see cref="Facebook.Net.Contracts.Models.ProxyConfig"/>.
    /// </summary>
    public static bool TryParseProxySpec(string? rawProxy, string proxyId, out Facebook.Net.Contracts.Models.ProxyConfig? proxy)
    {
        proxy = null;
        if (string.IsNullOrWhiteSpace(rawProxy))
        {
            return false;
        }

        string trimmed = rawProxy.Trim();
        Facebook.Net.Contracts.Primitives.ProxyProtocol protocol = Facebook.Net.Contracts.Primitives.ProxyProtocol.Http;

        if (trimmed.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = Facebook.Net.Contracts.Primitives.ProxyProtocol.Socks5;
            trimmed = trimmed["socks5://".Length..];
        }
        else if (trimmed.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = Facebook.Net.Contracts.Primitives.ProxyProtocol.Socks5;
            trimmed = trimmed["socks://".Length..];
        }
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["http://".Length..];
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["https://".Length..];
        }

        // Format A: user:pass@host:port
        int atIdx = trimmed.LastIndexOf('@');
        if (atIdx > 0)
        {
            string credsPart = trimmed[..atIdx];
            string hostPortPart = trimmed[(atIdx + 1)..];
            string[] hp = hostPortPart.Split(':', StringSplitOptions.TrimEntries);
            if (hp.Length == 2 &&
                !string.IsNullOrWhiteSpace(hp[0]) &&
                int.TryParse(hp[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int atPort) &&
                atPort > 0 &&
                atPort <= 65535)
            {
                string[] creds = credsPart.Split(':', 2);
                proxy = new Facebook.Net.Contracts.Models.ProxyConfig(
                    Id: proxyId,
                    Host: hp[0],
                    Port: atPort,
                    Protocol: protocol,
                    Username: !string.IsNullOrWhiteSpace(creds[0]) ? creds[0] : null,
                    Password: creds.Length > 1 && !string.IsNullOrWhiteSpace(creds[1]) ? creds[1] : null);
                return true;
            }
        }

        // Format B: host:port or host:port:user:pass
        string[] parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 &&
            !string.IsNullOrWhiteSpace(parts[0]) &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int port) &&
            port > 0 &&
            port <= 65535)
        {
            string? username = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null;
            string? password = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null;

            proxy = new Facebook.Net.Contracts.Models.ProxyConfig(
                Id: proxyId,
                Host: parts[0],
                Port: port,
                Protocol: protocol,
                Username: username,
                Password: password);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a <see cref="Facebook.Net.Contracts.Models.ProxyConfig"/> back into a clean string for editing in UI.
    /// </summary>
    public static string FormatProxySpec(Facebook.Net.Contracts.Models.ProxyConfig? proxy)
    {
        if (proxy is null)
        {
            return string.Empty;
        }

        string scheme = proxy.Protocol == Facebook.Net.Contracts.Primitives.ProxyProtocol.Socks5 ? "socks5://" : "http://";
        if (!string.IsNullOrWhiteSpace(proxy.Username))
        {
            return $"{scheme}{proxy.Host}:{proxy.Port}:{proxy.Username}:{proxy.Password}";
        }

        return $"{scheme}{proxy.Host}:{proxy.Port}";
    }

    /// <summary>
    /// Parses any supported cookie/session text or creates an interactive browser login profile if empty.
    /// </summary>
    /// <param name="rawInput">Raw JSON, pipe-delimited string, or cookie header pasted by the user.</param>
    /// <param name="friendlyName">Optional friendly display name provided by the user.</param>
    /// <param name="defaultBrowserLoginName">Optional localized fallback name when creating an interactive browser login profile.</param>
    /// <param name="defaultImportedPrefix">Optional localized fallback prefix when importing a cookie string.</param>
    /// <returns>A normalized <see cref="ImportedSessionBundle"/>.</returns>
    public static ImportedSessionBundle Parse(
        string? rawInput,
        string? friendlyName = null,
        string? defaultBrowserLoginName = null,
        string? defaultImportedPrefix = null)
    {
        string trimmed = rawInput?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            string pendingId = $"login_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            string label = string.IsNullOrWhiteSpace(friendlyName)
                ? (defaultBrowserLoginName ?? "Facebook Login Profile")
                : friendlyName.Trim();

            return new ImportedSessionBundle(pendingId, label, string.Empty, null, null);
        }

        // Case 1: JSON object (J2Team Cookies format or .local/test-session.json) or JSON array (EditThisCookie / Cookie-Editor)
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            if (TryParseJsonBundle(trimmed, friendlyName, out ImportedSessionBundle? jsonBundle) && jsonBundle is not null)
            {
                return jsonBundle;
            }
        }

        // Case 2: Pipe-delimited format (UID|Pass|2FA|Cookie... or Cookie|UserAgent)
        if (trimmed.Contains('|', StringComparison.Ordinal))
        {
            return ParsePipeDelimited(trimmed, friendlyName, defaultImportedPrefix);
        }

        // Case 3: Raw semicolon-delimited cookie header (c_user=...; xs=...)
        string extractedUid = ExtractCookieValue(trimmed.AsSpan(), "c_user".AsSpan())
            ?? $"acc_{Math.Abs(trimmed.GetHashCode(StringComparison.Ordinal))}";

        string prefix = defaultImportedPrefix ?? "FB Account";
        string display = !string.IsNullOrWhiteSpace(friendlyName)
            ? friendlyName.Trim()
            : $"{prefix} ({MaskIdentifier(extractedUid)})";

        return new ImportedSessionBundle(
            AccountUid: extractedUid,
            SuggestedDisplayName: display,
            CookieHeader: trimmed,
            UserAgent: null,
            TwoFactorSecret: null);
    }

    private static bool TryParseJsonBundle(string json, string? friendlyName, out ImportedSessionBundle? result)
    {
        result = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? userAgent = null;
            string? explicitCookieHeader = null;
            string? explicitUid = null;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("userAgent"u8, out JsonElement uaEl) && uaEl.ValueKind == JsonValueKind.String)
                {
                    userAgent = uaEl.GetString();
                }

                if (root.TryGetProperty("cookieHeader"u8, out JsonElement chEl) && chEl.ValueKind == JsonValueKind.String)
                {
                    explicitCookieHeader = chEl.GetString();
                }

                if (root.TryGetProperty("uid"u8, out JsonElement uidEl) && uidEl.ValueKind == JsonValueKind.String)
                {
                    explicitUid = uidEl.GetString();
                }

                // J2Team Cookies format: { "url": "https://www.facebook.com", "cookies": [ ... ] }
                if (root.TryGetProperty("cookies"u8, out JsonElement cookiesArr) &&
                    cookiesArr.ValueKind == JsonValueKind.Array)
                {
                    string cookieStr = BuildCookieHeaderFromArray(cookiesArr, out string? cUserFromArr);
                    string resolvedUid = !string.IsNullOrWhiteSpace(cUserFromArr)
                        ? cUserFromArr
                        : (explicitUid ?? $"j2team_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

                    string label = !string.IsNullOrWhiteSpace(friendlyName)
                        ? friendlyName.Trim()
                        : $"J2Team ({MaskIdentifier(resolvedUid)})";

                    result = new ImportedSessionBundle(resolvedUid, label, cookieStr, userAgent, null);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(explicitCookieHeader))
                {
                    string resolvedUid = !string.IsNullOrWhiteSpace(explicitUid)
                        ? explicitUid
                        : (ExtractCookieValue(explicitCookieHeader.AsSpan(), "c_user".AsSpan()) ?? "session_1");

                    string label = !string.IsNullOrWhiteSpace(friendlyName)
                        ? friendlyName.Trim()
                        : $"Facebook ({MaskIdentifier(resolvedUid)})";

                    result = new ImportedSessionBundle(resolvedUid, label, explicitCookieHeader, userAgent, null);
                    return true;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                // Cookie-Editor / EditThisCookie JSON array format
                string cookieStr = BuildCookieHeaderFromArray(root, out string? cUser);
                if (!string.IsNullOrWhiteSpace(cookieStr))
                {
                    string resolvedUid = !string.IsNullOrWhiteSpace(cUser)
                        ? cUser
                        : $"cookie_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                    string label = !string.IsNullOrWhiteSpace(friendlyName)
                        ? friendlyName.Trim()
                        : $"Cookie JSON ({MaskIdentifier(resolvedUid)})";

                    result = new ImportedSessionBundle(resolvedUid, label, cookieStr, null, null);
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static string BuildCookieHeaderFromArray(JsonElement arrayElement, out string? cUser)
    {
        cUser = null;
        StringBuilder sb = new(512);

        foreach (JsonElement item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? name = item.TryGetProperty("name"u8, out JsonElement nEl) ? nEl.GetString() : null;
            string? val = item.TryGetProperty("value"u8, out JsonElement vEl) ? vEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || val is null)
            {
                continue;
            }

            if (string.Equals(name, "c_user", StringComparison.Ordinal))
            {
                cUser = val;
            }

            if (sb.Length > 0)
            {
                _ = sb.Append("; ");
            }

            _ = sb.Append(name).Append('=').Append(val);
        }

        return sb.ToString();
    }

    private static ImportedSessionBundle ParsePipeDelimited(string input, string? friendlyName, string? defaultImportedPrefix)
    {
        string[] parts = input.Split('|', StringSplitOptions.TrimEntries);
        string? cookieHeader = null;
        string? uid = null;
        string? twoFactor = null;
        string? userAgent = null;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Contains("c_user=", StringComparison.Ordinal) || part.Contains("xs=", StringComparison.Ordinal))
            {
                cookieHeader = part;
                uid ??= ExtractCookieValue(part.AsSpan(), "c_user".AsSpan());
            }
            else if (part.StartsWith("Mozilla/", StringComparison.OrdinalIgnoreCase))
            {
                userAgent = part;
            }
            else if (uid is null && IsAllDigits(part.AsSpan()) && part.Length >= 6)
            {
                uid = part;
            }
            else if (twoFactor is null && part.Length >= 16 && part.Length <= 64 && IsBase32Like(part.AsSpan()))
            {
                twoFactor = part;
            }
        }

        string resolvedUid = uid ?? $"acc_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        string prefix = defaultImportedPrefix ?? "FB Account";
        string label = !string.IsNullOrWhiteSpace(friendlyName)
            ? friendlyName.Trim()
            : $"{prefix} ({MaskIdentifier(resolvedUid)})";

        return new ImportedSessionBundle(
            AccountUid: resolvedUid,
            SuggestedDisplayName: label,
            CookieHeader: cookieHeader ?? string.Empty,
            UserAgent: userAgent,
            TwoFactorSecret: twoFactor);
    }

    private static string? ExtractCookieValue(ReadOnlySpan<char> cookieHeader, ReadOnlySpan<char> key)
    {
        int start = 0;
        while (start < cookieHeader.Length)
        {
            int semi = cookieHeader[start..].IndexOf(';');
            ReadOnlySpan<char> pair = semi >= 0
                ? cookieHeader.Slice(start, semi).Trim()
                : cookieHeader[start..].Trim();

            start = semi >= 0 ? start + semi + 1 : cookieHeader.Length;

            int eq = pair.IndexOf('=');
            if (eq > 0)
            {
                ReadOnlySpan<char> k = pair[..eq].Trim();
                if (k.SequenceEqual(key))
                {
                    return pair[(eq + 1)..].Trim().ToString();
                }
            }
        }

        return null;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsAsciiDigit(span[i]))
            {
                return false;
            }
        }

        return !span.IsEmpty;
    }

    private static bool IsBase32Like(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (!char.IsAsciiLetterUpper(c) && (c < '2' || c > '7') && c != ' ')
            {
                return false;
            }
        }

        return !span.IsEmpty;
    }

    private static string MaskIdentifier(string id) =>
        id.Length <= 4 ? id : $"...{id[^4..]}";
}
