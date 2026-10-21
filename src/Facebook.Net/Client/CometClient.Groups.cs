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
    public async Task<PagedResult<GroupSummary>> SearchGroupsAsync(IBrowserSession session, string keyword, int count = 30, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        string bsid = PayloadBuilder.NewUuidV4();
        List<GroupSummary> combined = new(Math.Max(count, 16));
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        string? nextCursor = cursor;
        bool hasNext = true;

        int maxPages = cursor is null && count > 5 ? 5 : 1;
        for (int page = 0; page < maxPages && combined.Count < count && (page == 0 || (hasNext && !string.IsNullOrWhiteSpace(nextCursor))); page++)
        {
            if (page > 0)
            {
                await Task.Delay(40, ct).ConfigureAwait(false);
            }

            string? currentCursor = nextCursor;
            int pageCount = Math.Clamp(count - combined.Count, 5, 20);
            string variablesJson = BuildJson(w =>
            {
                w.WriteNumber("count"u8, pageCount);
                w.WriteBoolean("allow_streaming"u8, false);
                w.WritePropertyName("args"u8);
                w.WriteStartObject();
                w.WriteString("callsite"u8, "COMET_GLOBAL_SEARCH");
                w.WritePropertyName("config"u8);
                w.WriteStartObject();
                w.WriteBoolean("exact_match"u8, false);
                w.WriteNull("high_confidence_config"u8);
                w.WriteNull("intercept_config"u8);
                w.WriteNull("sts_disambiguation"u8);
                w.WriteNull("watch_config"u8);
                w.WriteEndObject();
                w.WritePropertyName("context"u8);
                w.WriteStartObject();
                w.WriteString("bsid"u8, bsid);
                w.WriteNull("tsid"u8);
                w.WriteEndObject();
                w.WritePropertyName("experience"u8);
                w.WriteStartObject();
                w.WriteNull("encoded_server_defined_params"u8);
                w.WriteNull("fbid"u8);
                w.WriteString("type"u8, "GROUPS_TAB");
                w.WriteEndObject();
                w.WritePropertyName("filters"u8);
                w.WriteStartArray();
                w.WriteEndArray();
                w.WriteString("text"u8, keyword);
                w.WriteEndObject();
                if (!string.IsNullOrWhiteSpace(currentCursor))
                {
                    w.WriteString("cursor"u8, currentCursor);
                }
                else
                {
                    w.WriteNull("cursor"u8);
                }

                w.WriteBoolean("feedbackSource"u8, false);
                w.WriteBoolean("fetch_filters"u8, false);
                w.WriteBoolean("renderLocation"u8, false);
                w.WriteNumber("scale"u8, 2);
                w.WriteNumber("stream_initial_count"u8, 0);
                w.WriteBoolean("useDefaultActor"u8, false);
            });

            GraphQlRequest request = new(
                FriendlyName: ProtocolConstants.Queries.SearchGroupsName,
                DocId: ProtocolConstants.Queries.SearchGroupsDocId,
                VariablesJson: variablesJson,
                PreNavigateUrl: page == 0 && cursor is null ? $"/search/groups/?q={Uri.EscapeDataString(keyword)}" : null);

            GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
            PagedResult<GroupSummary> parsed = ResponseParser.ParseGroups(res);

            int addedThisPage = 0;
            for (int i = 0; i < parsed.Items.Count && combined.Count < count; i++)
            {
                GroupSummary item = parsed.Items[i];
                if (!string.Equals(item.Name, item.GroupId, StringComparison.Ordinal) && seenIds.Add(item.GroupId))
                {
                    combined.Add(item);
                    addedThisPage++;
                }
            }

            nextCursor = parsed.EndCursor;
            hasNext = parsed.HasNextPage;

            if (addedThisPage == 0 && string.IsNullOrWhiteSpace(nextCursor))
            {
                break;
            }
        }

        if (cursor is null && combined.Count == 0)
        {
            PagedResult<GroupSummary> ssrGroups = await this.TryFetchGroupsSsrAsync(
                session,
                $"/search/groups/?q={Uri.EscapeDataString(keyword)}",
                ct).ConfigureAwait(false);

            for (int i = 0; i < ssrGroups.Items.Count && combined.Count < count; i++)
            {
                GroupSummary item = ssrGroups.Items[i];
                if (!string.Equals(item.Name, item.GroupId, StringComparison.Ordinal) && seenIds.Add(item.GroupId))
                {
                    combined.Add(item);
                }
            }
        }

        return new PagedResult<GroupSummary>(combined, nextCursor, hasNext);
    }

    /// <inheritdoc />
    public async Task<PagedResult<GroupSummary>> GetJoinedGroupsAsync(IBrowserSession session, int count = 500, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<GroupSummary> combined = new(Math.Min(count, 256));
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        string? nextCursor = cursor;
        bool hasNext = true;

        int maxPages = cursor is null && count > 20 ? 25 : 1;
        for (int page = 0; page < maxPages && combined.Count < count && (page == 0 || (hasNext && !string.IsNullOrWhiteSpace(nextCursor))); page++)
        {
            if (page > 0)
            {
                await Task.Delay(40, ct).ConfigureAwait(false);
            }

            string? currentCursor = nextCursor;
            int pageCount = Math.Clamp(count - combined.Count, 20, 50);
            string variablesJson = BuildJson(w =>
            {
                w.WriteNumber("count"u8, pageCount);
                if (!string.IsNullOrWhiteSpace(currentCursor))
                {
                    w.WriteString("cursor"u8, currentCursor);
                }
                else
                {
                    w.WriteNull("cursor"u8);
                }

                w.WritePropertyName("ordering"u8);
                w.WriteStartArray();
                w.WriteStringValue("viewer_added");
                w.WriteEndArray();
                w.WriteNumber("scale"u8, 2);
            });

            GraphQlRequest request = new(
                FriendlyName: ProtocolConstants.Queries.JoinedGroupsName,
                DocId: ProtocolConstants.Queries.JoinedGroupsDocId,
                VariablesJson: variablesJson,
                PreNavigateUrl: page == 0 && cursor is null && combined.Count == 0
                    ? "/groups/joins?nav_source=tab&ordering=viewer_added"
                    : null);

            GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
            PagedResult<GroupSummary> parsed = ResponseParser.ParseGroups(res);

            int addedThisPage = 0;
            for (int i = 0; i < parsed.Items.Count && combined.Count < count; i++)
            {
                GroupSummary item = parsed.Items[i];
                if (!string.Equals(item.Name, item.GroupId, StringComparison.Ordinal) && seenIds.Add(item.GroupId))
                {
                    GroupSummary normalized = item.MembershipState == GroupMembershipState.Unknown
                        ? item with { MembershipState = GroupMembershipState.Member }
                        : item;
                    combined.Add(normalized);
                    addedThisPage++;
                }
            }

            nextCursor = parsed.EndCursor;
            hasNext = parsed.HasNextPage;

            if (addedThisPage == 0 && string.IsNullOrWhiteSpace(nextCursor))
            {
                break;
            }
        }

        if (cursor is null && combined.Count == 0)
        {
            PagedResult<GroupSummary> ssrGroups = await this.TryFetchGroupsSsrAsync(
                session,
                "/groups/joins?nav_source=tab&ordering=viewer_added",
                ct).ConfigureAwait(false);

            for (int i = 0; i < ssrGroups.Items.Count && combined.Count < count; i++)
            {
                GroupSummary item = ssrGroups.Items[i];
                if (!string.Equals(item.Name, item.GroupId, StringComparison.Ordinal) && seenIds.Add(item.GroupId))
                {
                    GroupSummary normalized = item.MembershipState == GroupMembershipState.Unknown
                        ? item with { MembershipState = GroupMembershipState.Member }
                        : item;
                    combined.Add(normalized);
                }
            }
        }

        return new PagedResult<GroupSummary>(combined, nextCursor, hasNext);
    }

    private async Task<PagedResult<GroupSummary>> TryFetchGroupsSsrAsync(
        IBrowserSession session,
        string relativePath,
        CancellationToken ct)
    {
        try
        {
            string fullUrl = ResolveFullUrl(relativePath);
            string rawScripts = await session.ExecuteInPagePostAsync(
                fullUrl,
                new Dictionary<string, string>(0),
                "__SSR_RELAY_JSON__",
                ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(rawScripts))
            {
                return new PagedResult<GroupSummary>(Array.Empty<GroupSummary>(), null, false);
            }

            GraphQlResult ssrRes = ResponseParser.ParseGraphQlResult(session.AccountUid, rawScripts);
            return ResponseParser.ParseGroups(ssrRes);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new PagedResult<GroupSummary>(Array.Empty<GroupSummary>(), null, false);
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<PostSummary>> GetGroupFeedAsync(IBrowserSession session, string groupId, int count = 3, string? cursor = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        string activeSorting = "CHRONOLOGICAL";
        PagedResult<PostSummary> firstPage = await this.FetchGroupFeedPageAsync(session, groupId, count, cursor, activeSorting, ct).ConfigureAwait(false);

        if (cursor is null && firstPage.Items.Count == 0)
        {
            activeSorting = "RECENT_ACTIVITY";
            firstPage = await this.FetchGroupFeedPageAsync(session, groupId, count, cursor: null, activeSorting, ct).ConfigureAwait(false);
        }

        if (cursor is null && firstPage.Items.Count == 0)
        {
            activeSorting = "TOP_POSTS";
            firstPage = await this.FetchGroupFeedPageAsync(session, groupId, count, cursor: null, activeSorting, ct).ConfigureAwait(false);
        }

        if (cursor is null && firstPage.Items.Count == 0)
        {
            PagedResult<PostSummary> ssrPage = await this.TryFetchGroupSsrStoriesAsync(session, groupId, activeSorting, ct).ConfigureAwait(false);
            if (ssrPage.Items.Count > 0)
            {
                List<PostSummary> mergedInitial = new(firstPage.Items.Count + ssrPage.Items.Count);
                HashSet<string> initialSeen = new(StringComparer.Ordinal);
                for (int i = 0; i < ssrPage.Items.Count; i++)
                {
                    if (initialSeen.Add(ssrPage.Items[i].PostId))
                    {
                        mergedInitial.Add(ssrPage.Items[i]);
                    }
                }

                for (int i = 0; i < firstPage.Items.Count; i++)
                {
                    if (initialSeen.Add(firstPage.Items[i].PostId))
                    {
                        mergedInitial.Add(firstPage.Items[i]);
                    }
                }

                string? mergedCursor = firstPage.EndCursor ?? ssrPage.EndCursor;
                bool mergedHasNext = firstPage.HasNextPage || ssrPage.HasNextPage;
                firstPage = new PagedResult<PostSummary>(mergedInitial, mergedCursor, mergedHasNext);
            }
        }

        if (cursor is not null ||
            count <= 5 ||
            firstPage.Items.Count == 0 ||
            firstPage.Items.Count >= count ||
            !firstPage.HasNextPage ||
            string.IsNullOrWhiteSpace(firstPage.EndCursor))
        {
            return firstPage;
        }

        List<PostSummary> combined = new(count);
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        for (int i = 0; i < firstPage.Items.Count; i++)
        {
            if (seenIds.Add(firstPage.Items[i].PostId))
            {
                combined.Add(firstPage.Items[i]);
            }
        }

        string? nextCursor = firstPage.EndCursor;
        bool hasNext = firstPage.HasNextPage;
        int pageIndex = 1;

        while (combined.Count < count && hasNext && !string.IsNullOrWhiteSpace(nextCursor) && pageIndex < 4)
        {
            try
            {
                await Task.Delay(40, ct).ConfigureAwait(false);
                PagedResult<PostSummary> nextPage = await this.FetchGroupFeedPageAsync(
                    session,
                    groupId,
                    count - combined.Count,
                    nextCursor,
                    activeSorting,
                    ct).ConfigureAwait(false);

                if (nextPage.Items.Count == 0)
                {
                    break;
                }

                int addedThisPage = 0;
                for (int i = 0; i < nextPage.Items.Count && combined.Count < count; i++)
                {
                    if (seenIds.Add(nextPage.Items[i].PostId))
                    {
                        combined.Add(nextPage.Items[i]);
                        addedThisPage++;
                    }
                }

                nextCursor = nextPage.EndCursor ?? nextCursor;
                hasNext = nextPage.HasNextPage;
                pageIndex++;

                if (addedThisPage == 0)
                {
                    break;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }

        return new PagedResult<PostSummary>(combined, nextCursor, hasNext);
    }

    private async Task<PagedResult<PostSummary>> FetchGroupFeedPageAsync(
        IBrowserSession session,
        string groupId,
        int count,
        string? cursor,
        string sortingSetting,
        CancellationToken ct)
    {
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

            w.WriteString("feedLocation"u8, "GROUP");
            w.WriteString("feedType"u8, "DISCUSSION");
            w.WriteNull("focusCommentID"u8);
            w.WriteString("id"u8, groupId);
            w.WriteString("privacySelectorRenderLocation"u8, "COMET_STREAM");
            w.WriteString("renderLocation"u8, "group");
            w.WriteString("sortingSetting"u8, sortingSetting);
            w.WriteBoolean("useDefaultActor"u8, false);
            w.WriteBoolean("RELAY_INCREMENTAL_DELIVERY"u8, true);
            w.WriteNumber("scale"u8, 2);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupFeedName,
            DocId: ProtocolConstants.Queries.GroupFeedDocId,
            VariablesJson: variablesJson,
            PreNavigateUrl: cursor is null ? $"/groups/{groupId}?sorting_setting={sortingSetting}" : null,
            IsMultipartStream: true);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        return ResponseParser.ParsePosts(res);
    }

    private async Task<PagedResult<PostSummary>> TryFetchGroupSsrStoriesAsync(
        IBrowserSession session,
        string groupId,
        string sortingSetting,
        CancellationToken ct)
    {
        try
        {
            string groupPageUrl = ResolveFullUrl($"/groups/{groupId}?sorting_setting={sortingSetting}");
            string rawScripts = await session.ExecuteInPagePostAsync(
                groupPageUrl,
                new Dictionary<string, string>(0),
                "__SSR_RELAY_JSON__",
                ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(rawScripts))
            {
                return new PagedResult<PostSummary>(Array.Empty<PostSummary>(), null, false);
            }

            GraphQlResult ssrRes = ResponseParser.ParseGraphQlResult(session.AccountUid, rawScripts);
            return ResponseParser.ParsePosts(ssrRes);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new PagedResult<PostSummary>(Array.Empty<PostSummary>(), null, false);
        }
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

        if (session.CurrentMaterial is null)
        {
            await session.NavigateAsync(ResolveFullUrl($"/groups/{groupId}"), ct).ConfigureAwait(false);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string variablesJson = BuildJson(w =>
        {
            w.WriteString("feedType"u8, "DISCUSSION");
            w.WriteString("groupID"u8, groupId);
            w.WriteString("imageMediaType"u8, "image/x-auto");
            w.WritePropertyName("input"u8);
            w.WriteStartObject();
            w.WriteString("action_source"u8, "GROUP_MALL");
            w.WriteString("attribution_id_v2"u8, PayloadBuilder.CreateAttributionIdV2("CometGroupDiscussionRoot.react", "comet.fbweb.CometGroupDiscussionRoute"));
            w.WriteString("actor_id"u8, material.Uid);
            w.WriteString("client_mutation_id"u8, "1");
            w.WriteString("group_id"u8, groupId);
            w.WriteString("source"u8, "group_mall");
            w.WriteEndObject();
            w.WriteBoolean("inviteShortLinkKey"u8, false);
            w.WriteBoolean("isChainingRecommendationUnit"u8, false);
            w.WriteBoolean("isEntityMenu"u8, false);
            w.WriteNumber("scale"u8, 2);
            w.WriteString("source"u8, "GROUP_MALL");
            w.WriteBoolean("renderLocation"u8, false);
        });

        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.GroupJoinName,
            DocId: ProtocolConstants.Queries.GroupJoinDocId,
            VariablesJson: variablesJson);

        GraphQlResult res = await this.ExecuteGraphQlAsync(session, request, ct).ConfigureAwait(false);
        string? rawState = ResponseParser.FindFirstStringProperty(res, "viewer_join_state")
            ?? ResponseParser.FindFirstStringProperty(res, "join_state");
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
            PreNavigateUrl: $"/groups/{groupId}");

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

        if (session.CurrentMaterial is null)
        {
            await session.NavigateAsync(ResolveFullUrl($"/{groupId}"), ct).ConfigureAwait(false);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

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

        if (session.CurrentMaterial is null)
        {
            await session.NavigateAsync(ResolveFullUrl($"/{groupId}"), ct).ConfigureAwait(false);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

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
}
