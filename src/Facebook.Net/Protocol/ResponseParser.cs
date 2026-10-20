// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Protocol;

/// <summary>
/// Zero-reflection JSON traversal parser for extracting domain entities, cursors,
/// and upload tokens from Comet GraphQL and AJAX responses under Native AOT.
/// </summary>
public static partial class ResponseParser
{
    /// <summary>
    /// Parses a raw GraphQL response (single JSON or NDJSON stream) into a <see cref="GraphQlResult"/>.
    /// Automatically detects checkpoint or session expiration errors.
    /// </summary>
    public static GraphQlResult ParseGraphQlResult(string accountUid, string rawResponse)
    {
        IReadOnlyList<string> lines = ProtocolCodec.SplitNdjsonPayloads(rawResponse.AsSpan());
        if (lines.Count == 0)
        {
            return new GraphQlResult(IsSuccess: false, Array.Empty<string>(), ErrorMessage: "Empty GraphQL response.");
        }

        string? nextCursor = null;
        bool hasNextPage = false;
        string? errorMsg = null;
        int? errorCode = null;
        bool hasData = false;

        foreach (string line in lines)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("data"u8, out JsonElement dataEl) &&
                    dataEl.ValueKind == JsonValueKind.Object)
                {
                    hasData = true;
                }

                if (root.TryGetProperty("error"u8, out JsonElement topError) &&
                    topError.ValueKind == JsonValueKind.Number &&
                    topError.TryGetInt32(out int topCode))
                {
                    errorCode = topCode;
                    errorMsg = root.TryGetProperty("errorSummary"u8, out JsonElement sumEl)
                        ? sumEl.GetString()
                        : "Comet top-level error";

                    if (topCode is 1357001 or 1357004 or 1357045 or 282 or 956)
                    {
                        throw new CheckpointException(accountUid, errorMsg ?? "Checkpoint or session expired", topCode);
                    }
                }

                if (root.TryGetProperty("errors"u8, out JsonElement errorsArr) &&
                    errorsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement err in errorsArr.EnumerateArray())
                    {
                        errorMsg ??= err.TryGetProperty("message"u8, out JsonElement mEl) ? mEl.GetString() : null;
                        if (err.TryGetProperty("code"u8, out JsonElement cEl) && cEl.TryGetInt32(out int c))
                        {
                            errorCode ??= c;
                        }
                    }
                }

                ExtractPageInfoRecursive(root, ref nextCursor, ref hasNextPage);
            }
            catch (CheckpointException)
            {
                throw;
            }
            catch (JsonException)
            {
                // Ignore malformed non-JSON line in stream
            }
        }

        bool isSuccess = errorCode is null && (errorMsg is null || lines.Count > 1 || hasData);
        return new GraphQlResult(
            IsSuccess: isSuccess,
            JsonPayloads: lines,
            NextCursor: nextCursor,
            HasNextPage: hasNextPage,
            ErrorMessage: errorMsg,
            ErrorCode: errorCode);
    }

    /// <summary>
    /// Extracts a string property value by name anywhere within the GraphQL result payloads.
    /// </summary>
    public static string? FindFirstStringProperty(GraphQlResult result, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                string? found = FindStringRecursive(doc.RootElement, propertyName);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
            catch (JsonException)
            {
                // Ignore
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a raw <c>friendship_status</c> string from Comet responses to <see cref="FriendshipState"/>.
    /// </summary>
    public static FriendshipState MapFriendshipStatus(string? status) =>
        status switch
        {
            "CAN_REQUEST" => FriendshipState.CanRequest,
            "OUTGOING_REQUEST" => FriendshipState.OutgoingRequest,
            "INCOMING_REQUEST" => FriendshipState.IncomingRequest,
            "ARE_FRIENDS" => FriendshipState.AreFriends,
            _ => FriendshipState.Unknown
        };

    /// <summary>
    /// Maps a raw <c>viewer_join_state</c> string from Comet responses to <see cref="GroupMembershipState"/>.
    /// </summary>
    public static GroupMembershipState MapGroupMembershipState(string? state) =>
        state switch
        {
            "CAN_JOIN" => GroupMembershipState.CanJoin,
            "REQUESTED" => GroupMembershipState.Requested,
            "QUESTIONNAIRE_COMPLETED" => GroupMembershipState.QuestionnaireCompleted,
            "MEMBER" => GroupMembershipState.Member,
            _ => GroupMembershipState.Unknown
        };

    private static void ExtractPageInfoRecursive(JsonElement element, ref string? endCursor, ref bool hasNextPage)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("page_info"u8, out JsonElement pageInfo) &&
                pageInfo.ValueKind == JsonValueKind.Object)
            {
                if (pageInfo.TryGetProperty("end_cursor"u8, out JsonElement cursorEl) &&
                    cursorEl.ValueKind == JsonValueKind.String)
                {
                    endCursor = cursorEl.GetString();
                }

                if (pageInfo.TryGetProperty("has_next_page"u8, out JsonElement nextEl) &&
                    (nextEl.ValueKind == JsonValueKind.True || nextEl.ValueKind == JsonValueKind.False))
                {
                    hasNextPage = nextEl.GetBoolean();
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                ExtractPageInfoRecursive(prop.Value, ref endCursor, ref hasNextPage);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ExtractPageInfoRecursive(item, ref endCursor, ref hasNextPage);
            }
        }
    }

    private static string? FindStringRecursive(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out JsonElement valEl) &&
                valEl.ValueKind == JsonValueKind.String)
            {
                return valEl.GetString();
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindStringRecursive(prop.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindStringRecursive(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool IsNumericUid(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsAsciiDigit(span[i]))
            {
                return false;
            }
        }

        return true;
    }
}
