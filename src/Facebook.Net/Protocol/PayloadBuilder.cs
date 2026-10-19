// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Protocol;

/// <summary>
/// Provides Spintax content variation (<c>{opt1|opt2|opt3}</c>), Comet context token generation
/// (<c>attribution_id_v2</c>, <c>idempotence_token</c>, <c>__hsi</c>, <c>__req</c>),
/// and URL-encoded Base Parameters assembly.
/// </summary>
public static class PayloadBuilder
{
    private const string Base36Chars = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// Resolves nested or flat Spintax expressions (<c>{option A|option B|option C}</c>)
    /// using thread-safe random selection to prevent duplicate content detection when posting or commenting.
    /// </summary>
    public static string SpinText(string template)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains('{', StringComparison.Ordinal))
        {
            return template ?? string.Empty;
        }

        string current = template;
        int maxIterations = 256;

        while (maxIterations-- > 0)
        {
            int searchFrom = 0;
            int openIdx = -1;
            int closeIdx = -1;

            while (searchFrom < current.Length)
            {
                int candidateClose = current.IndexOf('}', searchFrom);
                if (candidateClose < 0)
                {
                    break;
                }

                int candidateOpen = current.LastIndexOf('{', candidateClose);
                if (candidateOpen >= 0)
                {
                    openIdx = candidateOpen;
                    closeIdx = candidateClose;
                    break;
                }

                // Skip stray '}' that has no preceding '{'
                searchFrom = candidateClose + 1;
            }

            if (openIdx < 0 || closeIdx < 0)
            {
                break;
            }

            ReadOnlySpan<char> inner = current.AsSpan(openIdx + 1, closeIdx - openIdx - 1);
            int choiceCount = 1;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '|')
                {
                    choiceCount++;
                }
            }

            int targetChoice = Random.Shared.Next(choiceCount);
            int currentChoice = 0;
            int segStart = 0;
            ReadOnlySpan<char> picked = inner;

            for (int i = 0; i <= inner.Length; i++)
            {
                if (i == inner.Length || inner[i] == '|')
                {
                    if (currentChoice == targetChoice)
                    {
                        picked = inner.Slice(segStart, i - segStart);
                        break;
                    }

                    currentChoice++;
                    segStart = i + 1;
                }
            }

            current = string.Concat(
                current.AsSpan(0, openIdx),
                picked,
                current.AsSpan(closeIdx + 1));
        }

        return current;
    }

    /// <summary>
    /// Generates a new UUID v4 string for <c>idempotence_token</c> or <c>composer_session_id</c>.
    /// </summary>
    public static string NewUuidV4() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// Generates a realistic <c>attribution_id_v2</c> navigation breadcrumb string.
    /// </summary>
    public static string CreateAttributionIdV2(
        string surfaceComponent = "CometHomeRoot.react",
        string routeName = "comet.fbweb.CometHomeRoute")
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int randId = Random.Shared.Next(100000, 999999);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{surfaceComponent},{routeName},via_cold_start,{nowMs},{randId},4748854339,,");
    }

    /// <summary>
    /// Builds the standard Comet headers (<c>content-type</c>, <c>x-asbd-id</c>, <c>x-fb-lsd</c>, <c>x-fb-friendly-name</c>).
    /// </summary>
    public static Dictionary<string, string> BuildGraphQlHeaders(SessionMaterial material, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyName);

        return new Dictionary<string, string>(4, StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = "application/x-www-form-urlencoded",
            ["x-asbd-id"] = ProtocolConstants.Protocol.AsbdId,
            ["x-fb-lsd"] = material.Lsd,
            ["x-fb-friendly-name"] = friendlyName
        };
    }

    /// <summary>
    /// Builds the complete <c>application/x-www-form-urlencoded</c> body combining all 23 Comet Base Parameters
    /// with the target <see cref="GraphQlRequest"/>.
    /// </summary>
    public static string BuildGraphQlFormBody(SessionMaterial material, GraphQlRequest request, int requestCounter)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(request);

        long spinTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long hsiRandom = Random.Shared.NextInt64(100_000_000_000_000_000L, 999_999_999_999_999_999L);
        string hsi = string.Create(CultureInfo.InvariantCulture, $"7{hsiRandom}-0");
        string reqCode = FormatBase36(Math.Max(1, requestCounter));

        StringBuilder sb = new(1024);
        AppendParam(sb, "av", material.Uid, isFirst: true);
        AppendParam(sb, "__user", material.Uid);
        AppendParam(sb, "__a", "1");
        AppendParam(sb, "__req", reqCode);
        AppendParam(sb, "__hs", material.HasteSession);
        AppendParam(sb, "dpr", "1");
        AppendParam(sb, "__ccg", ProtocolConstants.Protocol.DefaultConnectionClass);
        AppendParam(sb, "__rev", material.ClientRevision);
        AppendParam(sb, "__s", material.WebSessionId);
        AppendParam(sb, "__hsi", hsi);
        AppendParam(sb, "__comet_req", string.IsNullOrWhiteSpace(request.CometReq) ? "15" : request.CometReq);
        AppendParam(sb, "fb_dtsg", material.FbDtsg);
        AppendParam(sb, "jazoest", material.Jazoest);
        AppendParam(sb, "lsd", material.Lsd);
        AppendParam(sb, "__spin_r", material.ClientRevision);
        AppendParam(sb, "__spin_b", ProtocolConstants.Protocol.DefaultSpinBranch);
        AppendParam(sb, "__spin_t", spinTimestamp.ToString(CultureInfo.InvariantCulture));
        AppendParam(sb, "__aaid", "0");
        AppendParam(sb, "__pc", ProtocolConstants.Protocol.DefaultPackage);

        if (!string.IsNullOrWhiteSpace(request.ContextRoute))
        {
            AppendParam(sb, "__crn", request.ContextRoute);
        }

        AppendParam(sb, "fb_api_caller_class", "RelayModern");
        AppendParam(sb, "fb_api_req_friendly_name", request.FriendlyName);
        AppendParam(sb, "variables", request.VariablesJson);
        AppendParam(sb, "server_timestamps", "true");
        AppendParam(sb, "doc_id", request.DocId);

        return sb.ToString();
    }

    private static void AppendParam(StringBuilder sb, string key, string value, bool isFirst = false)
    {
        if (!isFirst)
        {
            _ = sb.Append('&');
        }

        _ = sb.Append(key).Append('=').Append(WebUtility.UrlEncode(value ?? string.Empty));
    }

    private static string FormatBase36(int value)
    {
        Span<char> buffer = stackalloc char[8];
        int pos = buffer.Length;
        int n = value;

        do
        {
            buffer[--pos] = Base36Chars[n % 36];
            n /= 36;
        }
        while (n > 0);

        return new string(buffer[pos..]);
    }
}
