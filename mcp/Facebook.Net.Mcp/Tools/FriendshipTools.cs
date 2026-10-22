// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Friendship &amp; Social Graph Automation: Incoming/Outgoing Friend Requests,
/// People You May Know (PYMK) Suggestions, Profile Followers, Batch Friend Request Confirmation/Sending,
/// Unfriending/Unfollowing, and Poking for Account Warmup.
/// </summary>
[McpServerToolType]
public sealed class FriendshipTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_get_incoming_friend_requests")]
    [Description("Fetch incoming friend requests received by the Facebook account via Comet GraphQL.")]
    public async Task<string> GetIncomingFriendRequestsAsync(
        [Description("Maximum number of incoming friend requests to fetch (1..100, default 20)")] int count = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<UserSummary> result = await _manager.CometClient
            .GetIncomingFriendRequestsAsync(session, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            count = result.Items.Count,
            has_next_page = result.HasNextPage,
            requests = result.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_get_outgoing_friend_requests")]
    [Description("Fetch outgoing pending friend requests sent by the Facebook account via Comet GraphQL.")]
    public async Task<string> GetOutgoingFriendRequestsAsync(
        [Description("Maximum number of outgoing friend requests to fetch (1..100, default 20)")] int count = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<UserSummary> result = await _manager.CometClient
            .GetOutgoingFriendRequestsAsync(session, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            count = result.Items.Count,
            has_next_page = result.HasNextPage,
            outgoing_requests = result.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_get_friend_suggestions")]
    [Description("Fetch 'People You May Know' (PYMK) friend suggestions for the Facebook account via Comet GraphQL.")]
    public async Task<string> GetFriendSuggestionsAsync(
        [Description("Maximum number of friend suggestions to fetch (1..100, default 30)")] int count = 30,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<UserSummary> result = await _manager.CometClient
            .GetFriendSuggestionsAsync(session, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            count = result.Items.Count,
            suggestions = result.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_get_profile_followers")]
    [Description("Fetch followers of a target Facebook profile UID via Comet GraphQL.")]
    public async Task<string> GetProfileFollowersAsync(
        [Description("Target Facebook profile UID whose followers should be fetched (if omitted, uses active account UID)")] string? targetUid = null,
        [Description("Maximum number of followers to fetch (1..100, default 20)")] int count = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string effectiveTargetUid = !string.IsNullOrWhiteSpace(targetUid) ? targetUid.Trim() : account.Uid;
        PagedResult<UserSummary> result = await _manager.CometClient
            .GetProfileFollowersAsync(session, effectiveTargetUid, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            target_uid = effectiveTargetUid,
            count = result.Items.Count,
            followers = result.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_handle_friend_requests_batch")]
    [Description("Batch accept ('confirm') or reject ('delete_incoming') incoming friend requests, or cancel ('cancel_outgoing') outgoing friend requests with anti-checkpoint delay. If targetUidsCsv is omitted, automatically fetches the latest incoming/outgoing requests up to maxCount.")]
    public async Task<string> HandleFriendRequestsBatchAsync(
        [Description("Action mode: 'confirm' (accept incoming), 'delete_incoming' (reject incoming), or 'cancel_outgoing' (cancel sent requests)")] string action = "confirm",
        [Description("Optional comma- or semicolon-separated user UIDs to process. Leave empty to auto-fetch from incoming/outgoing queue.")] string? targetUidsCsv = null,
        [Description("Maximum number of requests to process in this batch (1..50, default 10)")] int maxCount = 10,
        [Description("Anti-checkpoint delay in seconds between requests (1..60, default 3)")] int delaySeconds = 3,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string normAction = action?.Trim().ToLowerInvariant() ?? "confirm";
        int cap = Math.Clamp(maxCount, 1, 50);
        int delaySec = Math.Clamp(delaySeconds, 1, 60);

        List<(string Uid, string Name)> targets = [];
        if (!string.IsNullOrWhiteSpace(targetUidsCsv))
        {
            string[] uids = targetUidsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < uids.Length && targets.Count < cap; i++)
            {
                targets.Add((uids[i], uids[i]));
            }
        }
        else if (normAction == "cancel_outgoing")
        {
            PagedResult<UserSummary> outgoing = await _manager.CometClient
                .GetOutgoingFriendRequestsAsync(session, count: cap, ct: ct)
                .ConfigureAwait(false);
            for (int i = 0; i < outgoing.Items.Count && targets.Count < cap; i++)
            {
                targets.Add((outgoing.Items[i].Uid, outgoing.Items[i].Name));
            }
        }
        else
        {
            PagedResult<UserSummary> incoming = await _manager.CometClient
                .GetIncomingFriendRequestsAsync(session, count: cap, ct: ct)
                .ConfigureAwait(false);
            for (int i = 0; i < incoming.Items.Count && targets.Count < cap; i++)
            {
                targets.Add((incoming.Items[i].Uid, incoming.Items[i].Name));
            }
        }

        List<object> processed = [];
        for (int i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            (string uid, string name) = targets[i];
            FriendshipState newState = normAction switch
            {
                "cancel_outgoing" => await _manager.CometClient.DeleteFriendRequestAsync(session, uid, isIncoming: false, ct).ConfigureAwait(false),
                "delete_incoming" or "reject" => await _manager.CometClient.DeleteFriendRequestAsync(session, uid, isIncoming: true, ct).ConfigureAwait(false),
                _ => await _manager.CometClient.ConfirmFriendRequestAsync(session, uid, ct).ConfigureAwait(false)
            };

            processed.Add(new
            {
                uid,
                name,
                new_friendship_state = newState.ToString()
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account,
            "Friend Requests Batch",
            $"MCP executed '{normAction}' on {processed.Count} friend request(s).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            action = normAction,
            processed_count = processed.Count,
            results = processed
        });
    }

    [McpServerTool(Name = "fb_send_friend_requests_batch")]
    [Description("Batch send friend requests to target UIDs (or automatically to high-intent scraped lead authors when autoFromHighIntentLeads = true) with anti-duplicate protection and anti-checkpoint delay.")]
    public async Task<string> SendFriendRequestsBatchAsync(
        [Description("Optional comma- or semicolon-separated target Facebook user UIDs")] string? targetUidsCsv = null,
        [Description("If true and targetUidsCsv is empty, automatically selects authors of high-intent scraped leads from SQLite")] bool autoFromHighIntentLeads = false,
        [Description("Minimum buyer-intent score when autoFromHighIntentLeads = true (default 70)")] int minLeadIntentScore = 70,
        [Description("Maximum number of friend requests to send in this batch (1..30, default 10)")] int maxRequests = 10,
        [Description("Anti-checkpoint delay in seconds between friend requests (2..120, default 5)")] int delaySeconds = 5,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetUidsCsv) && !autoFromHighIntentLeads)
        {
            throw new ArgumentException("Either provide 'targetUidsCsv' or set 'autoFromHighIntentLeads = true'.");
        }

        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        int cap = Math.Clamp(maxRequests, 1, 30);
        int delaySec = Math.Clamp(delaySeconds, 2, 120);
        List<(string Uid, string Label)> candidates = [];
        HashSet<string> seenUids = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(targetUidsCsv))
        {
            string[] rawUids = targetUidsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < rawUids.Length && candidates.Count < cap; i++)
            {
                if (seenUids.Add(rawUids[i]))
                {
                    candidates.Add((rawUids[i], rawUids[i]));
                }
            }
        }
        else
        {
            IReadOnlyList<PostSummary> posts = await _manager.AccountStore
                .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 500, ct: ct)
                .ConfigureAwait(false);

            for (int i = 0; i < posts.Count && candidates.Count < cap * 2; i++)
            {
                PostSummary p = posts[i];
                if (string.IsNullOrWhiteSpace(p.AuthorUid) || string.Equals(p.AuthorUid, "0", StringComparison.Ordinal))
                {
                    continue;
                }

                int score = _manager.LeadScoring.CalculateBuyerIntentScore(p.MessageText, null);
                if (score >= minLeadIntentScore && seenUids.Add(p.AuthorUid.Trim()))
                {
                    candidates.Add((p.AuthorUid.Trim(), p.AuthorName ?? p.AuthorUid.Trim()));
                }
            }
        }

        List<object> sentResults = [];
        for (int i = 0; i < candidates.Count && sentResults.Count < cap; i++)
        {
            ct.ThrowIfCancellationRequested();
            (string uid, string label) = candidates[i];

            bool alreadyRequested = await _manager.AccountStore
                .HasExecutedActionAsync(session.AccountUid, "FRIEND_REQUEST", uid, ct)
                .ConfigureAwait(false);
            if (alreadyRequested)
            {
                continue;
            }

            if (sentResults.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            FriendshipState state = await _manager.CometClient
                .SendFriendRequestAsync(session, uid, ct)
                .ConfigureAwait(false);

            if (state is FriendshipState.OutgoingRequest or FriendshipState.AreFriends)
            {
                await _manager.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(session.AccountUid, "FRIEND_REQUEST", uid, state.ToString(), DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);
            }

            sentResults.Add(new
            {
                uid,
                name = label,
                friendship_state = state.ToString()
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account,
            "Send Friend Requests Batch",
            $"MCP sent {sentResults.Count} friend request(s).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            sent_count = sentResults.Count,
            results = sentResults
        });
    }

    [McpServerTool(Name = "fb_unfriend_or_unfollow_batch")]
    [Description("Batch unfriend or unfollow one or more target Facebook user UIDs with anti-checkpoint delay.")]
    public async Task<string> UnfriendOrUnfollowBatchAsync(
        [Description("Comma- or semicolon-separated Facebook user UIDs")] string targetUidsCsv,
        [Description("Action mode: 'unfriend' or 'unfollow' (default 'unfollow')")] string mode = "unfollow",
        [Description("Anti-checkpoint delay in seconds between actions (1..60, default 3)")] int delaySeconds = 3,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUidsCsv);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string normMode = mode?.Trim().ToLowerInvariant() ?? "unfollow";
        int delaySec = Math.Clamp(delaySeconds, 1, 60);
        string[] uids = targetUidsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<object> results = [];
        for (int i = 0; i < uids.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            string uid = uids[i];
            if (normMode == "unfriend")
            {
                FriendshipState state = await _manager.CometClient.UnfriendAsync(session, uid, ct).ConfigureAwait(false);
                results.Add(new { uid, action = "unfriend", state = state.ToString() });
            }
            else
            {
                bool ok = await _manager.CometClient.UnfollowUserAsync(session, uid, ct).ConfigureAwait(false);
                results.Add(new { uid, action = "unfollow", success = ok });
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            account,
            "Social Graph Cleanup",
            $"MCP executed '{normMode}' on {results.Count} user(s).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            mode = normMode,
            processed_count = results.Count,
            results
        });
    }

    [McpServerTool(Name = "fb_poke_users_batch")]
    [Description("Poke one or more Facebook friends ('PokesMutatorPokeMutation') to warm up interaction and trigger profile notifications.")]
    public async Task<string> PokeUsersBatchAsync(
        [Description("Comma- or semicolon-separated Facebook user UIDs to poke")] string targetUidsCsv,
        [Description("Anti-checkpoint delay in seconds between pokes (1..60, default 2)")] int delaySeconds = 2,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUidsCsv);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        int delaySec = Math.Clamp(delaySeconds, 1, 60);
        string[] uids = targetUidsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<object> results = [];
        int pokedCount = 0;

        for (int i = 0; i < uids.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            string uid = uids[i];
            bool ok = await _manager.CometClient.PokeUserAsync(session, uid, ct).ConfigureAwait(false);
            if (ok)
            {
                pokedCount++;
            }

            results.Add(new { uid, poked = ok });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account,
            "Poke Friends",
            $"MCP poked {pokedCount}/{uids.Length} user(s).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            poked_count = pokedCount,
            results
        });
    }
}
