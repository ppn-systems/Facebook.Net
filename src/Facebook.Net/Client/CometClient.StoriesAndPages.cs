// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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
}
