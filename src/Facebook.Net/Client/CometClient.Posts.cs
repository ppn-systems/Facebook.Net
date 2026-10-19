// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;

namespace Facebook.Net.Client;

public sealed partial class CometClient
{
    /// <inheritdoc />
    public async Task<string?> PublishPostAsync(
        IBrowserSession session,
        string messageText,
        ComposerTarget target = ComposerTarget.Feed,
        string? targetGroupId = null,
        IReadOnlyList<string>? photoIds = null,
        IReadOnlyList<string>? videoIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);

        if (target == ComposerTarget.Group && string.IsNullOrWhiteSpace(targetGroupId))
        {
            throw new ArgumentException("Target group ID is required when publishing to a group.", nameof(targetGroupId));
        }

        string preNavUrl = target == ComposerTarget.Group ? $"/{targetGroupId}" : "/";
        await session.NavigateAsync(ResolveFullUrl(preNavUrl), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string spunText = PayloadBuilder.SpinText(messageText);
        string sessionId = PayloadBuilder.NewUuidV4();
        string variablesJson = BuildPublishPostVariablesJson(material.Uid, spunText, sessionId, target, targetGroupId, photoIds, videoIds);

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.ComposerStoryCreateName,
            DocId: ProtocolConstants.Queries.PublishStoryCreateDocId,
            VariablesJson: variablesJson,
            ContextRoute: target == ComposerTarget.Group ? "comet.fbweb.CometGroupDiscussionRoute" : "comet.fbweb.CometHomeRoute");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        string? createdPostId = ResponseParser.FindFirstStringProperty(res, "post_id")
            ?? ResponseParser.FindFirstStringProperty(res, "legacy_story_hideable_id");

        if (!string.IsNullOrWhiteSpace(createdPostId))
        {
            await session.NavigateAsync(ResolveFullUrl($"/{createdPostId}"), ct).ConfigureAwait(false);
        }

        return createdPostId;
    }

    /// <inheritdoc />
    public async Task<string?> SharePostAsync(
        IBrowserSession session,
        string postId,
        string messageText,
        string? targetGroupId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        await session.NavigateAsync(ResolveFullUrl($"/{postId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string postUrl = ResolveFullUrl($"/{postId}");
        string previewVars = BuildJson(w =>
        {
            w.WritePropertyName("params"u8);
            w.WriteStartObject();
            w.WriteString("url"u8, postUrl);
            w.WriteEndObject();
        });

        GraphQlRequest previewReq = new(
            FriendlyName: ProtocolConstants.Queries.LinkAttachmentPreviewName,
            DocId: ProtocolConstants.Queries.LinkAttachmentPreviewDocId,
            VariablesJson: previewVars);

        GraphQlResult previewRes = await this.ExecuteGraphQlAsync(session, previewReq, ct).ConfigureAwait(false);
        string shareScrapeData = ResponseParser.FindFirstStringProperty(previewRes, "share_scrape_data") ?? "{}";

        string spunText = PayloadBuilder.SpinText(messageText ?? string.Empty);
        string sessionId = PayloadBuilder.NewUuidV4();
        string shareVars = BuildSharePostVariablesJson(material.Uid, spunText, sessionId, shareScrapeData, targetGroupId);

        GraphQlRequest shareReq = new(
            FriendlyName: ProtocolConstants.Queries.ComposerStoryCreateName,
            DocId: ProtocolConstants.Queries.ShareStoryCreateDocId,
            VariablesJson: shareVars);

        GraphQlResult shareRes = await this.ExecuteGraphQlAsync(session, shareReq, ct).ConfigureAwait(false);
        return ResponseParser.FindFirstStringProperty(shareRes, "post_id");
    }

    /// <inheritdoc />
    public async Task<string?> CreateCommentAsync(
        IBrowserSession session,
        string postId,
        string messageText,
        string? attachmentFbid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);

        await session.NavigateAsync(ResolveFullUrl($"/{postId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string spunText = PayloadBuilder.SpinText(messageText);
        string feedbackId = ProtocolCodec.EncodeFeedbackId(postId);
        string idempotenceToken = string.Concat("client:", PayloadBuilder.NewUuidV4());

        string variablesJson = BuildJson(w =>
        {
            w.WriteString("feedLocation"u8, "PERMALINK");
            w.WriteNumber("feedbackSource"u8, 2);
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("feedback_id"u8, feedbackId);
            w.WriteString("idempotence_token"u8, idempotenceToken);
            w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2());

            w.WritePropertyName("message"u8);
            w.WriteStartObject();
            w.WritePropertyName("ranges"u8);
            w.WriteStartArray();
            w.WriteEndArray();
            w.WriteString("text"u8, spunText);
            w.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(attachmentFbid))
            {
                w.WritePropertyName("attachments"u8);
                w.WriteStartArray();
                w.WriteStartObject();
                w.WritePropertyName("media"u8);
                w.WriteStartObject();
                w.WriteString("id"u8, attachmentFbid);
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndArray();
            }

            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.CreateCommentName,
            DocId: ProtocolConstants.Queries.CreateCommentDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        string? commentId = ExtractCreatedCommentId(res);

        if (!string.IsNullOrWhiteSpace(commentId))
        {
            await session.NavigateAsync(ResolveFullUrl($"/{commentId}"), ct).ConfigureAwait(false);
        }

        return commentId;
    }

    /// <inheritdoc />
    public async Task<bool> ReactToPostAsync(IBrowserSession session, string postId, ReactionKind reaction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        await session.NavigateAsync(ResolveFullUrl($"/{postId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string feedbackId = ProtocolCodec.EncodeFeedbackId(postId);
        string reactionId = ProtocolCodec.GetReactionId(reaction);
        string sessionId = PayloadBuilder.NewUuidV4();

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2());
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("feedback_id"u8, feedbackId);
            w.WriteString("feedback_reaction_id"u8, reactionId);
            w.WriteString("feedback_source"u8, "NEWS_FEED");
            w.WriteString("session_id"u8, sessionId);
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.PostReactName,
            DocId: ProtocolConstants.Queries.PostReactDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<PagedResult<PostSummary>> GetNewsFeedAsync(IBrowserSession session, int count = 5, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        string variablesJson = BuildJson(w =>
        {
            w.WriteNumber("count"u8, count);
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                w.WriteString("cursor"u8, cursor);
            }
            else
            {
                w.WriteNull("cursor"u8);
            }

            w.WriteString("feedLocation"u8, "NEWSFEED");
            w.WritePropertyName("orderby"u8);
            w.WriteStartArray();
            w.WriteStringValue("TOP_STORIES");
            w.WriteEndArray();
            w.WriteBoolean("RELAY_INCREMENTAL_DELIVERY"u8, true);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.NewsFeedPaginationName,
            DocId: ProtocolConstants.Queries.NewsFeedPaginationDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? "/" : null,
            IsMultipartStream: true);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParsePosts(res);
    }

    /// <inheritdoc />
    public async Task<PostSummary?> GetSinglePostAsync(IBrowserSession session, string postId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        await session.NavigateAsync(ResolveFullUrl($"/{postId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string storyId = ProtocolCodec.EncodeSinglePostStoryId(material.Uid, postId);
        string variablesJson = BuildJson(w =>
        {
            w.WriteString("storyID"u8, storyId);
            w.WriteString("feedLocation"u8, "PERMALINK");
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.SinglePostContentName,
            DocId: ProtocolConstants.Queries.SinglePostContentDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        PagedResult<PostSummary> parsed = ResponseParser.ParsePosts(res);
        if (parsed.Items.Count > 0)
        {
            return parsed.Items[0];
        }

        string? text = ResponseParser.FindFirstStringProperty(res, "text");
        return text is not null ? new PostSummary(postId, string.Empty, null, text, ResolveFullUrl($"/{postId}")) : null;
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetPostReactorsAsync(IBrowserSession session, string postId, int count = 10, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        string feedbackId = ProtocolCodec.EncodeFeedbackId(postId);
        string variablesJson = BuildJson(w =>
        {
            w.WriteNumber("count"u8, count);
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                w.WriteString("cursor"u8, cursor);
            }
            else
            {
                w.WriteNull("cursor"u8);
            }

            w.WriteString("id"u8, feedbackId);
            w.WriteString("feedbackTargetID"u8, feedbackId);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.PostReactorsName,
            DocId: ProtocolConstants.Queries.PostReactorsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/{postId}" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetPostCommentersAsync(IBrowserSession session, string postId, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);

        string feedbackId = ProtocolCodec.EncodeFeedbackId(postId);
        string variablesJson = BuildJson(w =>
        {
            w.WriteNumber("commentsAfterCount"u8, -1);
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                w.WriteString("commentsAfterCursor"u8, cursor);
            }
            else
            {
                w.WriteNull("commentsAfterCursor"u8);
            }

            w.WriteString("id"u8, feedbackId);
            w.WriteString("commentsIntentToken"u8, "RANKED_UNFILTERED_CHRONOLOGICAL_REPLIES_INTENT_V1");
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.PostCommentsName,
            DocId: ProtocolConstants.Queries.PostCommentsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/{postId}" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    private static string BuildPublishPostVariablesJson(
        string actorUid,
        string messageText,
        string sessionId,
        ComposerTarget target,
        string? targetGroupId,
        IReadOnlyList<string>? photoIds,
        IReadOnlyList<string>? videoIds) =>
        BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, actorUid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("composer_session_id"u8, sessionId);
            w.WriteString("idempotence_token"u8, string.Concat(sessionId, "_FEED"));
            w.WriteString("composer_type"u8, target == ComposerTarget.Group ? "group" : "feed");
            w.WriteString("source"u8, "WWW");

            w.WritePropertyName("message"u8);
            w.WriteStartObject();
            w.WritePropertyName("ranges"u8);
            w.WriteStartArray();
            w.WriteEndArray();
            w.WriteString("text"u8, messageText);
            w.WriteEndObject();

            w.WritePropertyName("audience"u8);
            w.WriteStartObject();
            if (target == ComposerTarget.Group && !string.IsNullOrWhiteSpace(targetGroupId))
            {
                w.WriteString("to_id"u8, targetGroupId);
            }
            else
            {
                w.WritePropertyName("privacy"u8);
                w.WriteStartObject();
                w.WriteString("base_state"u8, "EVERYONE");
                w.WriteEndObject();
            }

            w.WriteEndObject();

            w.WritePropertyName("attachments"u8);
            w.WriteStartArray();
            if (photoIds is not null)
            {
                foreach (string pid in photoIds)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("photo"u8);
                    w.WriteStartObject();
                    w.WriteString("id"u8, pid);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
            }

            if (videoIds is not null)
            {
                foreach (string vid in videoIds)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("video"u8);
                    w.WriteStartObject();
                    w.WriteString("id"u8, vid);
                    w.WriteBoolean("notify_when_processed"u8, true);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });

    private static string BuildSharePostVariablesJson(
        string actorUid,
        string messageText,
        string sessionId,
        string shareScrapeData,
        string? targetGroupId) =>
        BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, actorUid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("composer_session_id"u8, sessionId);
            w.WriteString("idempotence_token"u8, sessionId);
            w.WriteString("composer_type"u8, string.IsNullOrWhiteSpace(targetGroupId) ? "share" : "group");

            w.WritePropertyName("message"u8);
            w.WriteStartObject();
            w.WritePropertyName("ranges"u8);
            w.WriteStartArray();
            w.WriteEndArray();
            w.WriteString("text"u8, messageText);
            w.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(targetGroupId))
            {
                w.WritePropertyName("audience"u8);
                w.WriteStartObject();
                w.WriteString("to_id"u8, targetGroupId);
                w.WriteEndObject();
            }

            w.WritePropertyName("attachments"u8);
            w.WriteStartArray();
            w.WriteStartObject();
            w.WritePropertyName("link"u8);
            w.WriteStartObject();
            w.WriteString("share_scrape_data"u8, shareScrapeData);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteEndObject();
        });

    private static string? ExtractCreatedCommentId(GraphQlResult result)
    {
        string? rawNodeId = ResponseParser.FindFirstStringProperty(result, "id");
        if (string.IsNullOrWhiteSpace(rawNodeId))
        {
            return null;
        }

        try
        {
            byte[] decodedBytes = Convert.FromBase64String(rawNodeId);
            string decoded = Encoding.UTF8.GetString(decodedBytes);
            int underscoreIdx = decoded.LastIndexOf('_');
            if (underscoreIdx >= 0 && underscoreIdx + 1 < decoded.Length)
            {
                return decoded[(underscoreIdx + 1)..];
            }
        }
        catch (FormatException)
        {
            // Fallback to raw ID if not Base64
        }

        return rawNodeId;
    }
}
