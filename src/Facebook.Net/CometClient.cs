// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts;
using Facebook.Net.Contracts.Exceptions;

namespace Facebook.Net;

/// <summary>
/// High-performance Comet GraphQL and media upload client implementing <see cref="ICometClient"/>.
/// Executes operations using In-Page Request Delegation, Contextual Pre/Post Navigation,
/// Spintax content variation, and Hidden DOM File Bridge.
/// </summary>
public sealed class CometClient : ICometClient
{
    private int _requestSequence;

    /// <inheritdoc />
    public async Task<GraphQlResult> ExecuteGraphQlAsync(IBrowserSession session, GraphQlRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        // 1. Contextual Pre-Navigation if specified
        if (!string.IsNullOrWhiteSpace(request.PreNavigateUrl))
        {
            string fullPreUrl = ResolveFullUrl(request.PreNavigateUrl);
            await session.NavigateAsync(fullPreUrl, ct).ConfigureAwait(false);
        }

        // 2. Ensure fresh SessionMaterial (extracts fb_dtsg, jazoest, lsd, client_revision)
        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        int reqNum = Interlocked.Increment(ref this._requestSequence);
        Dictionary<string, string> headers = PayloadBuilder.BuildGraphQlHeaders(material, request.FriendlyName);
        string formBody = PayloadBuilder.BuildGraphQlFormBody(material, request, reqNum);

        // 3. Execute POST inside the page's DOM context (In-Page Request Delegation)
        string rawResponse = await session.ExecuteInPagePostAsync(
            ProtocolConstants.Endpoints.GraphQl,
            headers,
            formBody,
            ct).ConfigureAwait(false);

        GraphQlResult result = ResponseParser.ParseGraphQlResult(session.AccountUid, rawResponse);

        // 4. Contextual Post-Navigation if specified and operation succeeded
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(request.PostNavigateUrl))
        {
            string fullPostUrl = ResolveFullUrl(request.PostNavigateUrl);
            await session.NavigateAsync(fullPostUrl, ct).ConfigureAwait(false);
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // 1. Posts, Sales Commenting, Reactions & Sharing (Feed & Group Publishing)
    // -------------------------------------------------------------------------

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

        // Step 1: Fetch share_scrape_data via ComposerLinkAttachmentPreviewQuery
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

        // Step 2: Execute ComposerStoryCreateMutation (doc_id: 9510250992417785)
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

    // -------------------------------------------------------------------------
    // 2. Community Groups (Scraping, Joining, Questionnaire, Publishing)
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<PagedResult<GroupSummary>> SearchGroupsAsync(IBrowserSession session, string keyword, int count = 5, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

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

            w.WriteString("text"u8, keyword);
            w.WritePropertyName("experience"u8);
            w.WriteStartObject();
            w.WriteString("type"u8, "GROUPS_TAB");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.SearchGroupsName,
            DocId: ProtocolConstants.Queries.SearchGroupsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/search/groups/?q={Uri.EscapeDataString(keyword)}" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseGroups(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<GroupSummary>> GetJoinedGroupsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default)
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

            w.WritePropertyName("ordering"u8);
            w.WriteStartArray();
            w.WriteStringValue("integrity_signals");
            w.WriteEndArray();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.JoinedGroupsName,
            DocId: ProtocolConstants.Queries.JoinedGroupsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? "/groups/joins?nav_source=tab&ordering=viewer_added" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseGroups(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PostSummary>> GetGroupFeedAsync(IBrowserSession session, string groupId, int count = 3, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

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

            w.WriteString("id"u8, groupId);
            w.WriteString("feedLocation"u8, "GROUP");
            w.WriteString("sortingSetting"u8, "TOP_POSTS");
            w.WriteBoolean("RELAY_INCREMENTAL_DELIVERY"u8, true);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupFeedName,
            DocId: ProtocolConstants.Queries.GroupFeedDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/{groupId}" : null,
            IsMultipartStream: true);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParsePosts(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetGroupNewMembersAsync(IBrowserSession session, string groupId, int count = 10, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

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

            w.WriteString("groupID"u8, groupId);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupNewMembersName,
            DocId: ProtocolConstants.Queries.GroupNewMembersDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/groups/{groupId}/members" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<GroupMembershipState> JoinGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        await session.NavigateAsync(ResolveFullUrl($"/{groupId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("group_id"u8, groupId);
            w.WriteString("source"u8, "group_mall");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupJoinName,
            DocId: ProtocolConstants.Queries.GroupJoinDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        string? rawState = ResponseParser.FindFirstStringProperty(res, "viewer_join_state");
        return ResponseParser.MapGroupMembershipState(rawState);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupQuestion>> GetGroupQuestionsAsync(IBrowserSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        string variablesJson = BuildJson(w =>
        {
            w.WriteString("groupID"u8, groupId);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupQuestionsName,
            DocId: ProtocolConstants.Queries.GroupQuestionsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: $"/{groupId}");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseGroupQuestions(res);
    }

    /// <inheritdoc />
    public async Task<GroupMembershipState> SubmitGroupAnswersAsync(
        IBrowserSession session,
        string groupId,
        IReadOnlyList<GroupQuestionAnswer> answers,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(answers);

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("group_id"u8, groupId);
            w.WritePropertyName("answers"u8);
            w.WriteStartArray();
            foreach (GroupQuestionAnswer ans in answers)
            {
                w.WriteStartObject();
                w.WriteString("question_id"u8, ans.QuestionId);
                w.WriteString("answer"u8, PayloadBuilder.SpinText(ans.AnswerText));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupSaveAnswersName,
            DocId: ProtocolConstants.Queries.GroupSaveAnswersDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        string? rawState = ResponseParser.FindFirstStringProperty(res, "membership_questionnaire_status")
            ?? ResponseParser.FindFirstStringProperty(res, "viewer_join_state");
        return ResponseParser.MapGroupMembershipState(rawState);
    }

    /// <inheritdoc />
    public async Task<bool> LeaveGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        await session.NavigateAsync(ResolveFullUrl($"/{groupId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("group_id"u8, groupId);
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupLeaveName,
            DocId: ProtocolConstants.Queries.GroupLeaveDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<bool> InviteFriendsToGroupAsync(IBrowserSession session, string groupId, IReadOnlyList<string> friendUids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(friendUids);

        await session.NavigateAsync(ResolveFullUrl($"/{groupId}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("group_id"u8, groupId);
            w.WriteString("source"u8, "comet_invite_friends");
            w.WritePropertyName("user_ids"u8);
            w.WriteStartArray();
            foreach (string uid in friendUids)
            {
                w.WriteStringValue(uid);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupAddMembersName,
            DocId: ProtocolConstants.Queries.GroupAddMembersDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    // -------------------------------------------------------------------------
    // 3. Friending & Social Graph
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetFriendSuggestionsAsync(IBrowserSession session, int count = 30, string? cursor = null, CancellationToken ct = default)
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

            w.WriteString("location"u8, "FRIENDS_HOME_MAIN");
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.PymkSuggestionsName,
            DocId: ProtocolConstants.Queries.PymkSuggestionsDocId,
            VariablesJson: variablesJson,
            ContextRoute: "comet.fbweb.CometPYMKSuggestionsRoute",
            PreNavigateUrl: cursor is null ? "/friends/suggestions" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetIncomingFriendRequestsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default)
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

            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.IncomingFriendRequestsName,
            DocId: ProtocolConstants.Queries.IncomingFriendRequestsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? "/friends/requests" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetOutgoingFriendRequestsAsync(IBrowserSession session, int count = 10, string? cursor = null, CancellationToken ct = default)
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

            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.OutgoingFriendRequestsName,
            DocId: ProtocolConstants.Queries.OutgoingFriendRequestsDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? "/friends/requests" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserSummary>> GetProfileFollowersAsync(IBrowserSession session, string targetUid, int count = 8, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        string collectionId = ProtocolCodec.EncodeFollowersCollectionId(targetUid);
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

            w.WriteNumber("scale"u8, 2);
            w.WriteNull("search"u8);
            w.WriteString("id"u8, collectionId);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.ProfileFollowersName,
            DocId: ProtocolConstants.Queries.ProfileFollowersDocId,
            VariablesJson: variablesJson,
            CometReq: "15",
            PreNavigateUrl: cursor is null ? $"/{targetUid}/followers" : null);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParseUsers(res);
    }

    /// <inheritdoc />
    public async Task<FriendshipState> SendFriendRequestAsync(IBrowserSession session, string targetUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        await session.NavigateAsync(ResolveFullUrl($"/{targetUid}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2());
            w.WriteNull("click_proof_validation_result"u8);
            w.WritePropertyName("friend_requestee_ids"u8);
            w.WriteStartArray();
            w.WriteStringValue(targetUid);
            w.WriteEndArray();
            w.WriteString("friending_channel"u8, "PROFILE_BUTTON");
            w.WritePropertyName("warn_ack_for_ids"u8);
            w.WriteStartArray();
            w.WriteEndArray();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "8");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.FriendRequestSendName,
            DocId: ProtocolConstants.Queries.FriendRequestSendDocId,
            VariablesJson: variablesJson,
            PostNavigateUrl: $"/{targetUid}");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.MapFriendshipStatus(ResponseParser.FindFirstStringProperty(res, "friendship_status"));
    }

    /// <inheritdoc />
    public async Task<FriendshipState> ConfirmFriendRequestAsync(IBrowserSession session, string requesterUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterUid);

        await session.NavigateAsync(ResolveFullUrl($"/{requesterUid}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("click_proof_validation_result"u8, "{\"validated\":true}");
            w.WriteString("friend_requester_id"u8, requesterUid);
            w.WriteString("friending_channel"u8, "FRIENDS_HOME_REQUESTS");
            w.WriteBoolean("warn_ack"u8, false);
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "2");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
            w.WriteNumber("refresh_num"u8, 0);
            w.WriteBoolean("should_fix_banner"u8, false);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.FriendRequestConfirmName,
            DocId: ProtocolConstants.Queries.FriendRequestConfirmDocId,
            VariablesJson: variablesJson,
            PostNavigateUrl: $"/{requesterUid}");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.MapFriendshipStatus(ResponseParser.FindFirstStringProperty(res, "friendship_status"));
    }

    /// <inheritdoc />
    public async Task<FriendshipState> DeleteFriendRequestAsync(IBrowserSession session, string targetUid, bool isIncoming, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        await session.NavigateAsync(ResolveFullUrl($"/{targetUid}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string docId = isIncoming
            ? ProtocolConstants.Queries.FriendRequestRejectDocId
            : ProtocolConstants.Queries.FriendRequestCancelDocId;

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            if (isIncoming)
            {
                w.WriteString("click_proof_validation_result"u8, "{\"validated\":true}");
                w.WriteString("friend_requester_id"u8, targetUid);
                w.WriteString("friending_channel"u8, "FRIENDS_HOME_MAIN");
                w.WriteString("actor_id"u8, material.Uid);
                w.WriteString("client_mutation_id"u8, "3");
            }
            else
            {
                w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2());
                w.WriteString("cancelled_friend_requestee_id"u8, targetUid);
                w.WriteNull("click_proof_validation_result"u8);
                w.WriteString("friending_channel"u8, "PROFILE_BUTTON");
                w.WriteString("actor_id"u8, material.Uid);
                w.WriteString("client_mutation_id"u8, "1");
            }

            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
            if (isIncoming)
            {
                w.WriteNumber("refresh_num"u8, 0);
            }
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.FriendRequestDeleteName,
            DocId: docId,
            VariablesJson: variablesJson,
            PostNavigateUrl: $"/{targetUid}");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.MapFriendshipStatus(ResponseParser.FindFirstStringProperty(res, "friendship_status"));
    }

    /// <inheritdoc />
    public async Task<FriendshipState> UnfriendAsync(IBrowserSession session, string targetUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        await session.NavigateAsync(ResolveFullUrl($"/{targetUid}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("source"u8, "bd_profile_button");
            w.WriteString("unfriended_user_id"u8, targetUid);
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.UnfriendName,
            DocId: ProtocolConstants.Queries.UnfriendDocId,
            VariablesJson: variablesJson,
            PostNavigateUrl: $"/{targetUid}");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.MapFriendshipStatus(ResponseParser.FindFirstStringProperty(res, "friendship_status"));
    }

    /// <inheritdoc />
    public async Task<bool> UnfollowUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        await session.NavigateAsync(ResolveFullUrl($"/{targetUid}"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WriteString("action_render_location"u8, "WWW_COMET_FRIEND_MENU");
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2());
            w.WriteBoolean("is_tracking_encrypted"u8, false);
            w.WriteString("subscribe_location"u8, "PROFILE");
            w.WriteNull("tracking"u8);
            w.WriteString("unsubscribee_id"u8, targetUid);
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteEndObject();
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.UnfollowUserName,
            DocId: ProtocolConstants.Queries.UnfollowUserDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return string.Equals(ResponseParser.FindFirstStringProperty(res, "subscribe_status"), "CAN_SUBSCRIBE", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> PokeUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);

        await session.NavigateAsync(ResolveFullUrl("/pokes"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("client_mutation_id"u8, "4");
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("user_id"u8, targetUid);
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.PokeUserName,
            DocId: ProtocolConstants.Queries.PokeUserDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.FindFirstStringProperty(res, "poke_status") is not null;
    }

    // -------------------------------------------------------------------------
    // 4. Stories, Watch, Notifications, Fanpages & Media Uploads
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoryBucketItem>> GetStoriesTrayAsync(IBrowserSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.StoriesTrayName,
            DocId: ProtocolConstants.Queries.StoriesTrayDocId,
            VariablesJson: "{\"scale\":2}",
            PreNavigateUrl: "/");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        List<StoryBucketItem> list = new();
        string? bucketId = ResponseParser.FindFirstStringProperty(res, "id");
        if (!string.IsNullOrWhiteSpace(bucketId))
        {
            list.Add(new StoryBucketItem(bucketId, bucketId, string.Empty, IsSeen: false));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<bool> MarkStorySeenAsync(IBrowserSession session, string bucketId, string storyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);

        await session.NavigateAsync(ResolveFullUrl($"/{storyId}?locale=en_EN"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string encodedStoryId = ProtocolCodec.EncodeStoryCardId(storyId);
        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("bucket_id"u8, bucketId);
            w.WriteString("story_id"u8, encodedStoryId);
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.StoryMarkSeenName,
            DocId: ProtocolConstants.Queries.StoryMarkSeenDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<bool> ReplyToStoryAsync(IBrowserSession session, string storyId, string emojiOrMessage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emojiOrMessage);

        await session.NavigateAsync(ResolveFullUrl($"/{storyId}?locale=en_EN"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string spunMessage = PayloadBuilder.SpinText(emojiOrMessage);
        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("story_id"u8, ProtocolCodec.EncodeStoryCardId(storyId));
            w.WriteString("story_reply_type"u8, "LIGHT_WEIGHT");
            w.WriteString("reaction"u8, spunMessage);
            w.WriteString("message"u8, spunMessage);
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.StorySendReplyName,
            DocId: ProtocolConstants.Queries.StorySendReplyDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<bool> CreateStoryAsync(
        IBrowserSession session,
        string? text = null,
        string? photoId = null,
        string? videoId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await session.NavigateAsync(ResolveFullUrl("/stories/create"), ct).ConfigureAwait(false);
        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            if (!string.IsNullOrWhiteSpace(photoId))
            {
                w.WritePropertyName("photo"u8);
                w.WriteStartObject();
                w.WriteString("id"u8, photoId);
                w.WriteEndObject();
            }
            else if (!string.IsNullOrWhiteSpace(videoId))
            {
                w.WritePropertyName("video"u8);
                w.WriteStartObject();
                w.WriteString("id"u8, videoId);
                w.WriteEndObject();
            }
            else
            {
                w.WriteString("message"u8, PayloadBuilder.SpinText(text ?? string.Empty));
                w.WriteString("text_format_preset_id"u8, ProtocolConstants.Protocol.DefaultStoryTextPresetId);
            }

            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.StoryCreateName,
            DocId: ProtocolConstants.Queries.StoryCreateDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(IBrowserSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.NotificationsListName,
            DocId: ProtocolConstants.Queries.NotificationsListDocId,
            VariablesJson: "{\"count\":10,\"scale\":2}",
            PreNavigateUrl: "/notifications");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        List<NotificationItem> list = new();
        string? notifId = ResponseParser.FindFirstStringProperty(res, "notif_id");
        if (!string.IsNullOrWhiteSpace(notifId))
        {
            string queryId = ResponseParser.FindFirstStringProperty(res, "query_id") ?? string.Empty;
            string url = ResponseParser.FindFirstStringProperty(res, "url") ?? ProtocolConstants.Endpoints.BaseOrigin;
            string desc = ResponseParser.FindFirstStringProperty(res, "text") ?? string.Empty;
            list.Add(new NotificationItem(notifId, queryId, url, desc, IsRead: false));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<bool> MarkNotificationReadAsync(IBrowserSession session, NotificationItem notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notification);

        if (!string.IsNullOrWhiteSpace(notification.TargetUrl))
        {
            await session.NavigateAsync(ResolveFullUrl(notification.TargetUrl), ct).ConfigureAwait(false);
        }

        SessionMaterial material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WritePropertyName("notif_ids"u8);
            w.WriteStartArray();
            w.WriteStringValue(notification.NotificationId);
            w.WriteEndArray();
            w.WriteString("query_id"u8, notification.QueryId);
            w.WriteString("update_type"u8, "MARK_READ");
            w.WriteEndObject();
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.NotificationMarkReadName,
            DocId: ProtocolConstants.Queries.NotificationMarkReadDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return res.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedPageSummary>> GetManagedPagesAsync(IBrowserSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.ManagedPagesName,
            DocId: ProtocolConstants.Queries.ManagedPagesDocId,
            VariablesJson: "{\"scale\":2}",
            PreNavigateUrl: "/friends/suggestions");

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        List<ManagedPageSummary> pages = new();
        string? pageId = ResponseParser.FindFirstStringProperty(res, "delegate_page_id");
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            string name = ResponseParser.FindFirstStringProperty(res, "name") ?? pageId;
            pages.Add(new ManagedPageSummary(pageId, name, pageId));
        }

        return pages;
    }

    /// <inheritdoc />
    public async Task<string> UploadUfiPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string uploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.UfiPhotoUpload}?av={material.Uid}&__user={material.Uid}&__a=1&fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&profile_id={material.Uid}&source=19&target_id={material.Uid}&__crn=comet.fbweb.CometProfileTimelineListViewRoute");

        string raw = await session.UploadViaDomBridgeAsync(uploadUrl, localFilePath, "file", null, ct).ConfigureAwait(false);
        ReadOnlySpan<char> sanitized = ProtocolCodec.StripAntiJsonpPrefix(raw.AsSpan());

        using JsonDocument doc = JsonDocument.Parse(sanitized.ToString());
        if (doc.RootElement.TryGetProperty("payload"u8, out JsonElement payloadEl) &&
            payloadEl.TryGetProperty("fbid"u8, out JsonElement fbidEl))
        {
            string? fbid = fbidEl.ValueKind == JsonValueKind.String
                ? fbidEl.GetString()
                : fbidEl.GetRawText();

            if (!string.IsNullOrWhiteSpace(fbid))
            {
                return fbid;
            }
        }

        throw new ProtocolException("Failed to extract 'payload.fbid' from UFI photo upload response.");
    }

    /// <inheritdoc />
    public async Task<string> UploadStoryPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string uploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.StoryPhotoUpload}?av={material.Uid}&__user={material.Uid}&__a=1&fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&__crn=comet.fbweb.CometStoriesCreateRoute");

        Dictionary<string, string> fields = new(4, StringComparer.Ordinal)
        {
            ["source"] = "8",
            ["profile_id"] = material.Uid,
            ["waterfallxapp"] = "comet_stories",
            ["upload_id"] = "jsc_c_i"
        };

        string raw = await session.UploadViaDomBridgeAsync(uploadUrl, localFilePath, "file", fields, ct).ConfigureAwait(false);
        ReadOnlySpan<char> sanitized = ProtocolCodec.StripAntiJsonpPrefix(raw.AsSpan());

        using JsonDocument doc = JsonDocument.Parse(sanitized.ToString());
        if (doc.RootElement.TryGetProperty("payload"u8, out JsonElement payloadEl) &&
            payloadEl.TryGetProperty("photoID"u8, out JsonElement photoIdEl))
        {
            string? photoId = photoIdEl.ValueKind == JsonValueKind.String
                ? photoIdEl.GetString()
                : photoIdEl.GetRawText();

            if (!string.IsNullOrWhiteSpace(photoId))
            {
                return photoId;
            }
        }

        throw new ProtocolException("Failed to extract 'payload.photoID' from Story photo upload response.");
    }

    /// <inheritdoc />
    public async Task<string> UploadVideoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        FileInfo fileInfo = new(localFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Video file not found.", localFilePath);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        // Step 1: Retrieve RUpload server service_name
        GraphQlRequest configReq = new(
            FriendlyName: ProtocolConstants.Queries.MediaUploadConfigName,
            DocId: ProtocolConstants.Queries.MediaUploadConfigDocId,
            VariablesJson: "{\"source_type\":\"composer\"}");

        GraphQlResult configRes = await this.ExecuteGraphQlAsync(session, configReq, ct).ConfigureAwait(false);
        string serviceName = ResponseParser.FindFirstStringProperty(configRes, "service_name")
            ?? ProtocolConstants.Protocol.DefaultVideoUploadService;

        // Step 2: Initialize video upload session
        string waterfallId = PayloadBuilder.NewUuidV4();
        string startUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.VideoUploadStart}?av={material.Uid}&__user={material.Uid}&__a=1");

        string startBody = string.Create(
            CultureInfo.InvariantCulture,
            $"fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&file_size={fileInfo.Length}&source=newsfeed_composer&waterfall_id={waterfallId}");

        Dictionary<string, string> formHeaders = new(1, StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = "application/x-www-form-urlencoded"
        };

        string startRaw = await session.ExecuteInPagePostAsync(startUrl, formHeaders, startBody, ct).ConfigureAwait(false);
        GraphQlResult startParsed = ResponseParser.ParseGraphQlResult(session.AccountUid, startRaw);
        string videoId = ResponseParser.FindFirstStringProperty(startParsed, "video_id")
            ?? throw new ProtocolException("Failed to obtain 'video_id' from video upload start endpoint.");

        // Step 3: Push binary stream to RUpload via DOM bridge
        string streamToken = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string ruploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"https://{serviceName}.facebook.com/fb_video/{streamToken}-0-1048576?av={material.Uid}&__user={material.Uid}&__a=1&product_media_id={videoId}");

        string ruploadRaw = await session.UploadViaDomBridgeAsync(ruploadUrl, localFilePath, "file", null, ct).ConfigureAwait(false);
        GraphQlResult ruploadParsed = ResponseParser.ParseGraphQlResult(session.AccountUid, ruploadRaw);
        string chunkHandle = ResponseParser.FindFirstStringProperty(ruploadParsed, "h")
            ?? throw new ProtocolException("Failed to obtain chunk handle 'h' from RUpload video server.");

        // Step 4: Finalize video upload session
        string receiveUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.VideoUploadReceive}?av={material.Uid}&__user={material.Uid}&__a=1");

        string receiveBody = string.Create(
            CultureInfo.InvariantCulture,
            $"fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&video_id={videoId}&fbuploader_video_file_chunk={Uri.EscapeDataString(chunkHandle)}&source=newsfeed_composer");

        _ = await session.ExecuteInPagePostAsync(receiveUrl, formHeaders, receiveBody, ct).ConfigureAwait(false);
        return videoId;
    }

    private static string ResolveFullUrl(string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return pathOrUrl;
        }

        return pathOrUrl.StartsWith('/')
            ? string.Concat(ProtocolConstants.Endpoints.BaseOrigin, pathOrUrl)
            : string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/", pathOrUrl);
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
        catch
        {
            // Fallback to raw ID if not Base64
        }

        return rawNodeId;
    }

    private static string BuildJson(Action<Utf8JsonWriter> writeProperties)
    {
        using MemoryStream ms = new(256);
        using Utf8JsonWriter writer = new(ms);
        writer.WriteStartObject();
        writeProperties(writer);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
