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
    private static readonly string[] s_permalinkKeys = ["wwwURL", "permalink_url", "url"];
    private static readonly string[] s_postUrlMarkers = ["/posts/", "/permalink/", "story_fbid=", "multi_permalinks="];
    private static readonly string[] s_avatarFieldNames = ["profile_picture", "icon_image", "group_icon", "avatar", "image"];
    private static readonly string[] s_mediaImageContainers = ["photo_image", "image", "preferred_thumbnail", "thumbnailImage", "viewer_image", "flexible_height_image"];
    private static readonly string[] s_singleActorKeys = ["actor", "author", "owner", "seller"];
    /// <summary>
    /// Extracts feed or group <see cref="PostSummary"/> items from a <see cref="GraphQlResult"/>.
    /// Merges fields across multiple Relay fragments sharing the same <c>post_id</c> so partial
    /// header/composer fragments never shadow full story message and author fragments.
    /// </summary>
    public static PagedResult<PostSummary> ParsePosts(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> orderedIds = new();
        Dictionary<string, PostSummary> postsById = new(StringComparer.Ordinal);

        foreach (string payload in result.JsonPayloads)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                CollectPostsRecursive(doc.RootElement, orderedIds, postsById);
            }
            catch (JsonException)
            {
                // Skip invalid chunk
            }
        }

        List<PostSummary> posts = new(orderedIds.Count);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            PostSummary candidate = postsById[orderedIds[i]];
            bool isHollowSkeleton =
                string.IsNullOrWhiteSpace(candidate.AuthorName) &&
                string.IsNullOrWhiteSpace(candidate.MessageText) &&
                string.IsNullOrWhiteSpace(candidate.MediaPreviewUrl);

            if (!isHollowSkeleton)
            {
                posts.Add(candidate);
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

    private static void CollectPostsRecursive(
        JsonElement element,
        List<string> orderedIds,
        Dictionary<string, PostSummary> postsById)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool isStoryCandidate =
                element.TryGetProperty("post_id"u8, out _) ||
                element.TryGetProperty("comet_sections"u8, out _) ||
                (element.TryGetProperty("__typename"u8, out JsonElement tnEl) &&
                 tnEl.ValueKind == JsonValueKind.String &&
                 string.Equals(tnEl.GetString(), "Story", StringComparison.Ordinal));

            if (isStoryCandidate)
            {
                string? postId = FindPostIdFromStoryNode(element);
                if (!string.IsNullOrWhiteSpace(postId))
                {
                    string? text = FindPostMessageText(element);
                    string? authorUid = FindActorUid(element);
                    string? authorName = FindActorName(element);
                    string? authorAvatar = FindActorAvatarUri(element);
                    (string? mediaPreviewUrl, bool isVideo) = FindPostMediaPreview(element);
                    string? wwwUrl = FindStringRecursive(element, "wwwURL") ?? FindPermalinkUrlRecursive(element);
                    int reactionCount = FindNestedCount(element, "reaction_count");
                    int commentCount = FindNestedCount(element, "total_comment_count");
                    DateTimeOffset? createdAt = FindCreationTime(element);

                    if (!postsById.TryGetValue(postId, out PostSummary? existing))
                    {
                        orderedIds.Add(postId);
                        string permalink = !string.IsNullOrWhiteSpace(wwwUrl)
                            ? wwwUrl
                            : string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/", postId);

                        postsById[postId] = new PostSummary(
                            postId,
                            authorUid ?? string.Empty,
                            authorName,
                            text,
                            permalink,
                            reactionCount,
                            commentCount,
                            createdAt,
                            MediaPreviewUrl: mediaPreviewUrl,
                            IsVideo: isVideo,
                            AuthorAvatarUrl: authorAvatar);
                    }
                    else
                    {
                        string mergedAuthorUid = !string.IsNullOrWhiteSpace(existing.AuthorUid)
                            ? existing.AuthorUid
                            : (authorUid ?? string.Empty);
                        string? mergedAuthorName = !string.IsNullOrWhiteSpace(existing.AuthorName)
                            ? existing.AuthorName
                            : authorName;
                        string? mergedText = !string.IsNullOrWhiteSpace(text) &&
                            (string.IsNullOrWhiteSpace(existing.MessageText) || text.Length > existing.MessageText.Length)
                            ? text
                            : existing.MessageText;
                        string? mergedPermalink = !string.IsNullOrWhiteSpace(wwwUrl)
                            ? wwwUrl
                            : existing.PermalinkUrl;
                        int mergedReactions = Math.Max(existing.ReactionCount, reactionCount);
                        int mergedComments = Math.Max(existing.CommentCount, commentCount);
                        DateTimeOffset? mergedCreatedAt = existing.CreatedAt ?? createdAt;
                        string? mergedMediaUrl = !string.IsNullOrWhiteSpace(existing.MediaPreviewUrl)
                            ? existing.MediaPreviewUrl
                            : mediaPreviewUrl;
                        bool mergedIsVideo = existing.IsVideo || isVideo;
                        string? mergedAuthorAvatar = !string.IsNullOrWhiteSpace(existing.AuthorAvatarUrl)
                            ? existing.AuthorAvatarUrl
                            : authorAvatar;

                        postsById[postId] = new PostSummary(
                            postId,
                            mergedAuthorUid,
                            mergedAuthorName,
                            mergedText,
                            mergedPermalink,
                            mergedReactions,
                            mergedComments,
                            mergedCreatedAt,
                            MediaPreviewUrl: mergedMediaUrl,
                            IsVideo: mergedIsVideo,
                            AuthorAvatarUrl: mergedAuthorAvatar);
                    }

                    return;
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                CollectPostsRecursive(prop.Value, orderedIds, postsById);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectPostsRecursive(item, orderedIds, postsById);
            }
        }
    }

    private static string? FindPostIdFromStoryNode(JsonElement storyElement)
    {
        if (storyElement.TryGetProperty("post_id"u8, out JsonElement directEl) &&
            directEl.ValueKind == JsonValueKind.String)
        {
            string? id = directEl.GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        string? nestedPostId = FindStringRecursive(storyElement, "post_id");
        if (!string.IsNullOrWhiteSpace(nestedPostId))
        {
            return nestedPostId;
        }

        string? subTargetId = FindStringRecursive(storyElement, "subscription_target_id");
        if (!string.IsNullOrWhiteSpace(subTargetId) && IsNumericUid(subTargetId.AsSpan()))
        {
            return subTargetId;
        }

        if (storyElement.TryGetProperty("id"u8, out JsonElement rawIdEl) &&
            rawIdEl.ValueKind == JsonValueKind.String)
        {
            string? decodedFromDirectId = ExtractPostIdFromStoryGraphQlId(rawIdEl.GetString());
            if (!string.IsNullOrWhiteSpace(decodedFromDirectId))
            {
                return decodedFromDirectId;
            }
        }

        string? nestedGraphId = FindStoryGraphQlIdRecursive(storyElement);
        if (!string.IsNullOrWhiteSpace(nestedGraphId))
        {
            return nestedGraphId;
        }

        string? url = FindStringRecursive(storyElement, "wwwURL") ?? FindPermalinkUrlRecursive(storyElement);
        return ExtractPostIdFromUrl(url);
    }

    private static string? FindStoryGraphQlIdRecursive(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id"u8, out JsonElement idEl) &&
                idEl.ValueKind == JsonValueKind.String)
            {
                string? extracted = ExtractPostIdFromStoryGraphQlId(idEl.GetString());
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindStoryGraphQlIdRecursive(prop.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindStoryGraphQlIdRecursive(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractPostIdFromStoryGraphQlId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return null;
        }

        string candidate = rawId.Trim();
        if (candidate.StartsWith("Uzpf", StringComparison.Ordinal) ||
            candidate.StartsWith("ZmVlZGJhY2s", StringComparison.Ordinal))
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(candidate);
                candidate = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        if (!candidate.StartsWith("S:", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("feedback:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int lastSep = candidate.LastIndexOfAny([':', '_']);
        if (lastSep >= 0 && lastSep + 1 < candidate.Length)
        {
            ReadOnlySpan<char> tail = candidate.AsSpan(lastSep + 1);
            if (tail.Length >= 5 && IsNumericUid(tail))
            {
                return tail.ToString();
            }
        }

        return null;
    }

    private static string? FindPermalinkUrlRecursive(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (string key in s_permalinkKeys)
            {
                if (element.TryGetProperty(key, out JsonElement uEl) &&
                    uEl.ValueKind == JsonValueKind.String)
                {
                    string? val = uEl.GetString();
                    if (!string.IsNullOrWhiteSpace(val) &&
                        (val.Contains("/posts/", StringComparison.OrdinalIgnoreCase) ||
                         val.Contains("/permalink/", StringComparison.OrdinalIgnoreCase) ||
                         val.Contains("story_fbid=", StringComparison.OrdinalIgnoreCase) ||
                         val.Contains("multi_permalinks=", StringComparison.OrdinalIgnoreCase)))
                    {
                        return val;
                    }
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindPermalinkUrlRecursive(prop.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindPermalinkUrlRecursive(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractPostIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        ReadOnlySpan<char> span = url.AsSpan();
        foreach (string marker in s_postUrlMarkers)
        {
            int idx = span.IndexOf(marker.AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                ReadOnlySpan<char> rest = span[(idx + marker.Length)..];
                int len = 0;
                while (len < rest.Length && char.IsAsciiDigit(rest[len]))
                {
                    len++;
                }

                if (len >= 5)
                {
                    return rest[..len].ToString();
                }
            }
        }

        return null;
    }

    private static string? FindPostMessageText(JsonElement storyElement)
    {
        string? listingTitle = FindStringRecursive(storyElement, "marketplace_listing_title")
            ?? FindNestedContainerText(storyElement, "title")
            ?? FindNestedContainerText(storyElement, "title_with_entities");

        string? messageText = FindNestedContainerText(storyElement, "message")
            ?? FindNestedContainerText(storyElement, "description")
            ?? FindNestedContainerText(storyElement, "card_description")
            ?? FindNestedContainerText(storyElement, "translation")
            ?? FindNestedContainerText(storyElement, "value");

        if (!string.IsNullOrWhiteSpace(listingTitle) && !string.IsNullOrWhiteSpace(messageText))
        {
            return messageText.Contains(listingTitle, StringComparison.OrdinalIgnoreCase)
                ? messageText
                : string.Concat(listingTitle, " — ", messageText);
        }

        if (!string.IsNullOrWhiteSpace(messageText))
        {
            return messageText;
        }

        if (!string.IsNullOrWhiteSpace(listingTitle))
        {
            return listingTitle;
        }

        if (storyElement.TryGetProperty("comet_sections"u8, out JsonElement sectionsEl))
        {
            return FindNestedContainerText(sectionsEl, "message")
                ?? FindNestedContainerText(sectionsEl, "description")
                ?? FindStringRecursive(sectionsEl, "text");
        }

        return null;
    }

    private static string? FindNestedContainerText(JsonElement element, string containerPropertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(containerPropertyName, out JsonElement containerEl) &&
                containerEl.ValueKind == JsonValueKind.Object &&
                containerEl.TryGetProperty("text"u8, out JsonElement txtEl) &&
                txtEl.ValueKind == JsonValueKind.String)
            {
                string? val = txtEl.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindNestedContainerText(prop.Value, containerPropertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindNestedContainerText(item, containerPropertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindActorUid(JsonElement storyElement) =>
        FindActorPropertyRecursive(storyElement, "id");

    private static string? FindActorName(JsonElement storyElement) =>
        FindActorPropertyRecursive(storyElement, "name");

    private static string? FindActorAvatarUri(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("actors"u8, out JsonElement actorsEl) &&
                actorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement actor in actorsEl.EnumerateArray())
                {
                    string? uri = ExtractAvatarUriFromObject(actor);
                    if (!string.IsNullOrWhiteSpace(uri))
                    {
                        return uri;
                    }
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindActorAvatarUri(prop.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindActorAvatarUri(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ExtractAvatarUriFromObject(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string fieldName in s_avatarFieldNames)
        {
            if (obj.TryGetProperty(fieldName, out JsonElement picEl) &&
                picEl.ValueKind == JsonValueKind.Object &&
                picEl.TryGetProperty("uri"u8, out JsonElement uriEl) &&
                uriEl.ValueKind == JsonValueKind.String)
            {
                string? uri = uriEl.GetString();
                if (!string.IsNullOrWhiteSpace(uri) && uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return uri;
                }
            }
        }

        return null;
    }

    private static (string? MediaPreviewUrl, bool IsVideo) FindPostMediaPreview(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? typeName = element.TryGetProperty("__typename"u8, out JsonElement tEl) && tEl.ValueKind == JsonValueKind.String
                ? tEl.GetString()
                : null;
            bool isVideoNode = string.Equals(typeName, "Video", StringComparison.OrdinalIgnoreCase) ||
                               element.TryGetProperty("playable_url"u8, out _) ||
                               (element.TryGetProperty("is_playable"u8, out JsonElement playEl) && playEl.ValueKind == JsonValueKind.True);

            // Check attachments array or media node
            if (element.TryGetProperty("media"u8, out JsonElement mediaEl) &&
                mediaEl.ValueKind == JsonValueKind.Object)
            {
                (string? mediaUrl, bool mediaIsVideo) = ExtractMediaFromNode(mediaEl);
                if (!string.IsNullOrWhiteSpace(mediaUrl))
                {
                    return (mediaUrl, mediaIsVideo || isVideoNode);
                }
            }

            if (string.Equals(typeName, "Photo", StringComparison.OrdinalIgnoreCase) || isVideoNode)
            {
                (string? nodeUrl, bool nodeIsVideo) = ExtractMediaFromNode(element);
                if (!string.IsNullOrWhiteSpace(nodeUrl))
                {
                    return (nodeUrl, nodeIsVideo || isVideoNode);
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                // Skip actors so we don't accidentally treat an actor profile_picture as post media
                if (prop.NameEquals("actors"u8) || prop.NameEquals("profile_picture"u8))
                {
                    continue;
                }

                (string? nestedUrl, bool nestedVideo) = FindPostMediaPreview(prop.Value);
                if (!string.IsNullOrWhiteSpace(nestedUrl))
                {
                    return (nestedUrl, nestedVideo);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                (string? nestedUrl, bool nestedVideo) = FindPostMediaPreview(item);
                if (!string.IsNullOrWhiteSpace(nestedUrl))
                {
                    return (nestedUrl, nestedVideo);
                }
            }
        }

        return (null, false);
    }

    private static (string? Url, bool IsVideo) ExtractMediaFromNode(JsonElement mediaNode)
    {
        string? typeName = mediaNode.TryGetProperty("__typename"u8, out JsonElement tEl) && tEl.ValueKind == JsonValueKind.String
            ? tEl.GetString()
            : null;
        bool isVideo = string.Equals(typeName, "Video", StringComparison.OrdinalIgnoreCase) ||
                       mediaNode.TryGetProperty("playable_url"u8, out _) ||
                       (mediaNode.TryGetProperty("is_playable"u8, out JsonElement playEl) && playEl.ValueKind == JsonValueKind.True);

        foreach (string container in s_mediaImageContainers)
        {
            if (mediaNode.TryGetProperty(container, out JsonElement imgEl) &&
                imgEl.ValueKind == JsonValueKind.Object)
            {
                if (imgEl.TryGetProperty("uri"u8, out JsonElement uriEl) &&
                    uriEl.ValueKind == JsonValueKind.String)
                {
                    string? uri = uriEl.GetString();
                    if (!string.IsNullOrWhiteSpace(uri) && uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return (uri, isVideo);
                    }
                }

                if (imgEl.TryGetProperty("image"u8, out JsonElement innerImgEl) &&
                    innerImgEl.ValueKind == JsonValueKind.Object &&
                    innerImgEl.TryGetProperty("uri"u8, out JsonElement innerUriEl) &&
                    innerUriEl.ValueKind == JsonValueKind.String)
                {
                    string? uri = innerUriEl.GetString();
                    if (!string.IsNullOrWhiteSpace(uri) && uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return (uri, isVideo);
                    }
                }
            }
        }

        return (null, isVideo);
    }

    private static string? FindActorPropertyRecursive(JsonElement element, string actorField)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("actors"u8, out JsonElement actorsEl) &&
                actorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement actor in actorsEl.EnumerateArray())
                {
                    if (actor.ValueKind == JsonValueKind.Object &&
                        actor.TryGetProperty(actorField, out JsonElement fieldEl) &&
                        fieldEl.ValueKind == JsonValueKind.String)
                    {
                        string? val = fieldEl.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }
                }
            }

            foreach (string singleActorKey in s_singleActorKeys)
            {
                if (element.TryGetProperty(singleActorKey, out JsonElement singleActorEl) &&
                    singleActorEl.ValueKind == JsonValueKind.Object &&
                    singleActorEl.TryGetProperty(actorField, out JsonElement fieldEl) &&
                    fieldEl.ValueKind == JsonValueKind.String)
                {
                    string? val = fieldEl.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }

            foreach (JsonProperty prop in element.EnumerateObject())
            {
                string? nested = FindActorPropertyRecursive(prop.Value, actorField);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindActorPropertyRecursive(item, actorField);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
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
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    string name = element.TryGetProperty("name"u8, out JsonElement nEl) && nEl.ValueKind == JsonValueKind.String
                        ? nEl.GetString() ?? groupId
                        : groupId;
                    string? rawJoin = element.TryGetProperty("viewer_join_state"u8, out JsonElement jEl)
                        ? jEl.GetString()
                        : null;
                    string? avatarUrl = ExtractAvatarUriFromObject(element);

                    if (seenIds.Add(groupId))
                    {
                        groups.Add(new GroupSummary(groupId, name, MapGroupMembershipState(rawJoin), avatarUrl));
                    }
                    else if (!string.IsNullOrWhiteSpace(avatarUrl) || !string.Equals(name, groupId, StringComparison.Ordinal))
                    {
                        for (int i = 0; i < groups.Count; i++)
                        {
                            if (string.Equals(groups[i].GroupId, groupId, StringComparison.Ordinal))
                            {
                                GroupSummary existing = groups[i];
                                groups[i] = new GroupSummary(
                                    groupId,
                                    string.Equals(existing.Name, groupId, StringComparison.Ordinal) ? name : existing.Name,
                                    existing.MembershipState != GroupMembershipState.Unknown ? existing.MembershipState : MapGroupMembershipState(rawJoin),
                                    existing.AvatarUrl ?? avatarUrl);
                                break;
                            }
                        }
                    }
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
