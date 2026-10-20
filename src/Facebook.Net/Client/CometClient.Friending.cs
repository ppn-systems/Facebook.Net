// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
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
}
