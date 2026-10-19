// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Facebook.Net.Contracts;
using Facebook.Net.Contracts.Exceptions;

namespace Facebook.Net;

/// <summary>
/// Zero-reflection JSON traversal parser for extracting domain entities, cursors,
/// and upload tokens from Comet GraphQL and AJAX responses under Native AOT.
/// </summary>
public static class ResponseParser
{
    /// <summary>
    /// Parses a raw GraphQL response (single JSON or NDJSON stream) into a <see cref="GraphQlResult"/>.
    /// Automatically detects checkpoint or session expiration errors.
    /// </summary>
    /// <param name="accountUid">The account UID executing the request.</param>
    /// <param name="rawResponse">Raw response text returned by the server.</param>
    /// <returns>Parsed <see cref="GraphQlResult"/>.</returns>
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

        foreach (string line in lines)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

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

        bool isSuccess = errorCode is null && (errorMsg is null || lines.Count > 1);
        return new GraphQlResult(
            IsSuccess: isSuccess,
            JsonPayloads: lines,
            NextCursor: nextCursor,
            HasNextPage: hasNextPage,
            ErrorMessage: errorMsg,
            ErrorCode: errorCode);
    }

    /// <summary>
    /// Extracts feed or group <see cref="PostSummary"/> items from a <see cref="GraphQlResult"/>.
    /// </summary>
    /// <param name="result">Sanitized GraphQL result.</param>
    /// <returns>Paginated list of <see cref="PostSummary"/>.</returns>
    public static PagedResult<PostSummary> ParsePosts(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<PostSummary> posts = new();
        HashSet<string> seenIds = new(StringComparer.Ordinal);

        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                CollectPostsRecursive(doc.RootElement, posts, seenIds);
            }
            catch (JsonException)
            {
                // Skip invalid chunk
            }
        }

        return new PagedResult<PostSummary>(posts, result.NextCursor, result.HasNextPage);
    }

    /// <summary>
    /// Extracts <see cref="GroupSummary"/> items from search or joined groups responses.
    /// </summary>
    /// <param name="result">Sanitized GraphQL result.</param>
    /// <returns>Paginated list of <see cref="GroupSummary"/>.</returns>
    public static PagedResult<GroupSummary> ParseGroups(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<GroupSummary> groups = new();
        HashSet<string> seenIds = new(StringComparer.Ordinal);

        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                CollectGroupsRecursive(doc.RootElement, groups, seenIds);
            }
            catch (JsonException)
            {
                // Skip invalid chunk
            }
        }

        return new PagedResult<GroupSummary>(groups, result.NextCursor, result.HasNextPage);
    }

    /// <summary>
    /// Extracts <see cref="UserSummary"/> items from PYMK, friend requests, followers, reactors, or commenters.
    /// </summary>
    /// <param name="result">Sanitized GraphQL result.</param>
    /// <returns>Paginated list of <see cref="UserSummary"/>.</returns>
    public static PagedResult<UserSummary> ParseUsers(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<UserSummary> users = new();
        HashSet<string> seenUids = new(StringComparer.Ordinal);

        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                CollectUsersRecursive(doc.RootElement, users, seenUids);
            }
            catch (JsonException)
            {
                // Skip invalid chunk
            }
        }

        return new PagedResult<UserSummary>(users, result.NextCursor, result.HasNextPage);
    }

    /// <summary>
    /// Extracts <see cref="GroupQuestion"/> items from a group membership questionnaire response.
    /// </summary>
    public static List<GroupQuestion> ParseGroupQuestions(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<GroupQuestion> questions = new();
        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                CollectQuestionsRecursive(doc.RootElement, questions);
            }
            catch (JsonException)
            {
                // Ignore
            }
        }

        return questions;
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

    private static void CollectPostsRecursive(JsonElement element, List<PostSummary> posts, HashSet<string> seenIds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("post_id"u8, out JsonElement postIdEl) &&
                postIdEl.ValueKind == JsonValueKind.String)
            {
                string? postId = postIdEl.GetString();
                if (!string.IsNullOrWhiteSpace(postId) && seenIds.Add(postId))
                {
                    string? text = FindPostMessageText(element);
                    string authorUid = FindActorUid(element) ?? string.Empty;
                    string? authorName = FindActorName(element);
                    string? permalink = FindStringRecursive(element, "wwwURL")
                        ?? string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/", postId);

                    posts.Add(new PostSummary(postId, authorUid, authorName, text, permalink));
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                CollectPostsRecursive(prop.Value, posts, seenIds);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectPostsRecursive(item, posts, seenIds);
            }
        }
    }

    private static string? FindPostMessageText(JsonElement storyElement)
    {
        if (storyElement.TryGetProperty("message"u8, out JsonElement msgEl) &&
            msgEl.ValueKind == JsonValueKind.Object &&
            msgEl.TryGetProperty("text"u8, out JsonElement txtEl) &&
            txtEl.ValueKind == JsonValueKind.String)
        {
            return txtEl.GetString();
        }

        if (storyElement.TryGetProperty("comet_sections"u8, out JsonElement sectionsEl))
        {
            return FindStringRecursive(sectionsEl, "text");
        }

        return null;
    }

    private static string? FindActorUid(JsonElement storyElement)
    {
        if (storyElement.TryGetProperty("actors"u8, out JsonElement actorsEl) &&
            actorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement actor in actorsEl.EnumerateArray())
            {
                if (actor.TryGetProperty("id"u8, out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    return idEl.GetString();
                }
            }
        }

        return null;
    }

    private static string? FindActorName(JsonElement storyElement)
    {
        if (storyElement.TryGetProperty("actors"u8, out JsonElement actorsEl) &&
            actorsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement actor in actorsEl.EnumerateArray())
            {
                if (actor.TryGetProperty("name"u8, out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    return nameEl.GetString();
                }
            }
        }

        return null;
    }

    private static void CollectGroupsRecursive(JsonElement element, List<GroupSummary> groups, HashSet<string> seenIds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool isGroupType = element.TryGetProperty("__typename"u8, out JsonElement typeEl) &&
                string.Equals(typeEl.GetString(), "Group", StringComparison.Ordinal);

            if (isGroupType &&
                element.TryGetProperty("id"u8, out JsonElement idEl) &&
                idEl.ValueKind == JsonValueKind.String)
            {
                string? groupId = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(groupId) && seenIds.Add(groupId))
                {
                    string name = element.TryGetProperty("name"u8, out JsonElement nEl) && nEl.ValueKind == JsonValueKind.String
                        ? nEl.GetString() ?? groupId
                        : groupId;
                    string? rawJoin = element.TryGetProperty("viewer_join_state"u8, out JsonElement jEl)
                        ? jEl.GetString()
                        : null;

                    groups.Add(new GroupSummary(groupId, name, MapGroupMembershipState(rawJoin)));
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                CollectGroupsRecursive(prop.Value, groups, seenIds);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectGroupsRecursive(item, groups, seenIds);
            }
        }
    }

    private static void CollectUsersRecursive(JsonElement element, List<UserSummary> users, HashSet<string> seenUids)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool isUserType = element.TryGetProperty("__typename"u8, out JsonElement typeEl) &&
                string.Equals(typeEl.GetString(), "User", StringComparison.Ordinal);

            if (element.TryGetProperty("id"u8, out JsonElement idEl) &&
                idEl.ValueKind == JsonValueKind.String &&
                element.TryGetProperty("name"u8, out JsonElement nameEl) &&
                nameEl.ValueKind == JsonValueKind.String &&
                (isUserType || element.TryGetProperty("friendship_status"u8, out _) || element.TryGetProperty("profile_picture"u8, out _)))
            {
                string? uid = idEl.GetString();
                string? name = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(uid) &&
                    !string.IsNullOrWhiteSpace(name) &&
                    IsNumericUid(uid.AsSpan()) &&
                    seenUids.Add(uid))
                {
                    string? avatar = null;
                    if (element.TryGetProperty("profile_picture"u8, out JsonElement picEl) &&
                        picEl.ValueKind == JsonValueKind.Object &&
                        picEl.TryGetProperty("uri"u8, out JsonElement uriEl))
                    {
                        avatar = uriEl.GetString();
                    }

                    string? fStatus = element.TryGetProperty("friendship_status"u8, out JsonElement fsEl)
                        ? fsEl.GetString()
                        : null;

                    users.Add(new UserSummary(uid, name, avatar, MapFriendshipStatus(fStatus)));
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                CollectUsersRecursive(prop.Value, users, seenUids);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectUsersRecursive(item, users, seenUids);
            }
        }
    }

    private static void CollectQuestionsRecursive(JsonElement element, List<GroupQuestion> questions)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("question_type"u8, out JsonElement qTypeEl) &&
                element.TryGetProperty("id"u8, out JsonElement idEl) &&
                idEl.ValueKind == JsonValueKind.String)
            {
                string? qId = idEl.GetString();
                string? qType = qTypeEl.GetString() ?? "PARAGRAPH";
                string? qText = FindStringRecursive(element, "text") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(qId))
                {
                    questions.Add(new GroupQuestion(qId, qText, qType));
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                CollectQuestionsRecursive(prop.Value, questions);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectQuestionsRecursive(item, questions);
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
