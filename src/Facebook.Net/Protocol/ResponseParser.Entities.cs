// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Protocol;

public static partial class ResponseParser
{
    /// <summary>
    /// Extracts feed or group <see cref="PostSummary"/> items from a <see cref="GraphQlResult"/>.
    /// </summary>
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
                    int reactionCount = FindNestedCount(element, "reaction_count");
                    int commentCount = FindNestedCount(element, "total_comment_count");
                    DateTimeOffset? createdAt = FindCreationTime(element);

                    posts.Add(new PostSummary(postId, authorUid, authorName, text, permalink, reactionCount, commentCount, createdAt));
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

    private static int FindNestedCount(JsonElement element, string containerPropertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(containerPropertyName, out JsonElement containerEl) &&
                containerEl.ValueKind == JsonValueKind.Object &&
                containerEl.TryGetProperty("count"u8, out JsonElement countEl) &&
                countEl.TryGetInt32(out int count))
            {
                return count;
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                int nested = FindNestedCount(prop.Value, containerPropertyName);
                if (nested > 0)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                int nested = FindNestedCount(item, containerPropertyName);
                if (nested > 0)
                {
                    return nested;
                }
            }
        }

        return 0;
    }

    private static DateTimeOffset? FindCreationTime(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("creation_time"u8, out JsonElement timeEl) &&
                timeEl.TryGetInt64(out long unixSeconds) &&
                unixSeconds > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                DateTimeOffset? nested = FindCreationTime(prop.Value);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                DateTimeOffset? nested = FindCreationTime(item);
                if (nested.HasValue)
                {
                    return nested;
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
}
