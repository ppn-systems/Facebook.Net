// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Scraping Group Feeds, Buyer-Intent Lead Scoring, Post Inspection, Audit Logs, and Storage Metrics.
/// </summary>
[McpServerToolType]
public sealed class SalesLeadsTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_scrape_group_feed")]
    [Description("Scrape latest posts from a Facebook group via Comet GraphQL, compute buyer-intent scores, and persist them to local SQLite.")]
    public async Task<string> ScrapeGroupFeedAsync(
        [Description("Target Facebook Group ID to scrape")] string groupId,
        [Description("Optional group name label")] string? groupName = null,
        [Description("Number of posts to scrape (5..100, default 15)")] int count = 15,
        [Description("Optional comma-separated buyer filter keywords to score and filter posts")] string? filterKeywords = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        string resolvedGroupName = !string.IsNullOrWhiteSpace(groupName) ? groupName.Trim() : $"Group {cleanGroupId}";
        int limit = Math.Clamp(count, 5, 100);

        PagedResult<PostSummary> feed = await _manager.CometClient
            .GetGroupFeedAsync(session, cleanGroupId, count: limit, ct: ct)
            .ConfigureAwait(false);

        string[] keywords = string.IsNullOrWhiteSpace(filterKeywords)
            ? []
            : filterKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<PostSummary> matched = new(feed.Items.Count);
        for (int i = 0; i < feed.Items.Count; i++)
        {
            PostSummary post = feed.Items[i] with
            {
                GroupId = cleanGroupId,
                GroupName = resolvedGroupName
            };

            if (_manager.LeadScoring.MatchesBuyerKeywords(post.MessageText, keywords))
            {
                matched.Add(post);
            }
        }

        if (matched.Count > 0)
        {
            await _manager.AccountStore.UpsertScrapedPostsAsync(matched, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Success,
            account,
            resolvedGroupName,
            $"MCP scraped {matched.Count} posts from '{resolvedGroupName}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            group_id = cleanGroupId,
            group_name = resolvedGroupName,
            scraped_count = matched.Count,
            posts = matched.Select(p => new
            {
                post_id = p.PostId,
                author_name = p.AuthorName,
                message_text = p.MessageText,
                buyer_intent_score = _manager.LeadScoring.CalculateBuyerIntentScore(p.MessageText, filterKeywords),
                reaction_count = p.ReactionCount,
                comment_count = p.CommentCount,
                permalink_url = p.PermalinkUrl,
                created_at = p.CreatedAt?.ToString("o")
            })
        });
    }

    [McpServerTool(Name = "fb_query_scraped_leads")]
    [Description("Search and filter scraped group posts from local SQLite (zero network calls) with accent-insensitive Vietnamese keyword matching and buyer-intent ranking.")]
    public async Task<string> QueryScrapedLeadsAsync(
        [Description("Optional comma-separated keywords to filter and rank buyer intent")] string? keywords = null,
        [Description("Optional Facebook Group ID filter")] string? groupId = null,
        [Description("Comment status filter: 'all', 'pending' (not yet commented), or 'commented' (default 'all')")] string commentStatus = "all",
        [Description("Maximum number of posts to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<ActionHistoryEntry> actions = await _manager.AccountStore
            .GetRecentActionsAsync(500, ct)
            .ConfigureAwait(false);

        HashSet<string> commentedPostIds = new(StringComparer.Ordinal);
        for (int i = 0; i < actions.Count; i++)
        {
            if (string.Equals(actions[i].ActionKind, "COMMENT", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(actions[i].TargetId))
            {
                _ = commentedPostIds.Add(actions[i].TargetId);
            }
        }

        IReadOnlyList<PostSummary> dbPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId, searchKeyword: null, limit: 300, ct: ct)
            .ConfigureAwait(false);

        string[] kwArray = string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string normStatus = commentStatus?.Trim().ToLowerInvariant() ?? "all";
        List<PostSummary> filtered = [];

        for (int i = 0; i < dbPosts.Count; i++)
        {
            PostSummary post = dbPosts[i];
            bool isCommented = commentedPostIds.Contains(post.PostId);
            if (normStatus == "pending" && isCommented)
            {
                continue;
            }

            if (normStatus == "commented" && !isCommented)
            {
                continue;
            }

            if (_manager.LeadScoring.MatchesPostOrAuthorKeywords(post, kwArray))
            {
                filtered.Add(post);
            }
        }

        filtered.Sort((a, b) =>
            _manager.LeadScoring.CalculateBuyerIntentScore(b.MessageText, keywords)
                .CompareTo(_manager.LeadScoring.CalculateBuyerIntentScore(a.MessageText, keywords)));

        int take = Math.Clamp(limit, 1, 200);
        var items = filtered.Take(take).Select(p => new
        {
            post_id = p.PostId,
            group_id = p.GroupId,
            group_name = p.GroupName,
            author_name = p.AuthorName,
            message_text = p.MessageText,
            buyer_intent_score = _manager.LeadScoring.CalculateBuyerIntentScore(p.MessageText, keywords),
            is_commented = commentedPostIds.Contains(p.PostId),
            reaction_count = p.ReactionCount,
            comment_count = p.CommentCount,
            permalink_url = p.PermalinkUrl,
            created_at = p.CreatedAt?.ToString("o")
        });

        return JsonSerializer.Serialize(new
        {
            total_matched = filtered.Count,
            leads = items
        });
    }

    [McpServerTool(Name = "fb_get_single_post")]
    [Description("Retrieve full content of a single Facebook post by Post ID, optionally including reactors and commenters.")]
    public async Task<string> GetSinglePostAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("Whether to also fetch recent users who reacted or commented on the post")] bool includeEngagementUsers = false,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        (_, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanId = postId.Trim();
        PostSummary? post = await _manager.CometClient.GetSinglePostAsync(session, cleanId, ct).ConfigureAwait(false);

        IReadOnlyList<UserSummary>? reactors = null;
        IReadOnlyList<UserSummary>? commenters = null;
        if (includeEngagementUsers)
        {
            PagedResult<UserSummary> r = await _manager.CometClient.GetPostReactorsAsync(session, cleanId, count: 20, ct: ct).ConfigureAwait(false);
            PagedResult<UserSummary> c = await _manager.CometClient.GetPostCommentersAsync(session, cleanId, ct: ct).ConfigureAwait(false);
            reactors = r.Items;
            commenters = c.Items;
        }

        return JsonSerializer.Serialize(new
        {
            post,
            reactors,
            commenters
        });
    }

    [McpServerTool(Name = "fb_get_audit_logs")]
    [Description("Query recent activity and Personal/Campaign audit logs from facebook_audit.db.")]
    public async Task<string> GetAuditLogsAsync(
        [Description("Optional category filter: 'System', 'Account', 'Group', 'Post', 'SalesComment', 'Scrape', 'Personal'")] string? category = null,
        [Description("Optional search keyword")] string? searchKeyword = null,
        [Description("Optional account UID filter")] string? accountUid = null,
        [Description("Maximum number of log entries to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        AuditCategory? parsedCat = !string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<AuditCategory>(category.Trim(), ignoreCase: true, out AuditCategory cat)
            ? cat
            : null;

        AuditLogFilter filter = new(
            Category: parsedCat,
            AccountUid: string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim(),
            SearchKeyword: string.IsNullOrWhiteSpace(searchKeyword) ? null : searchKeyword.Trim(),
            Limit: Math.Clamp(limit, 1, 500));

        IReadOnlyList<AuditLogRecord> logs = await _manager.AuditStore.QueryAsync(filter, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            count = logs.Count,
            logs = logs.Select(l => new
            {
                id = l.Id,
                timestamp = l.Timestamp.ToString("o"),
                category = l.Category.ToString(),
                severity = l.Severity.ToString(),
                account_uid = l.AccountUid,
                account_name = l.AccountDisplayName,
                target_name = l.TargetName,
                message = l.Message,
                permalink_url = l.PermalinkUrl
            })
        });
    }

    [McpServerTool(Name = "fb_get_storage_usage")]
    [Description("Get real-time disk and memory storage breakdown for the Facebook.Net data directory and isolated Chromium profiles.")]
    public async Task<string> GetStorageUsageAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        StorageUsageSnapshot snap = _manager.StorageUsage.CalculateSnapshot(
            _manager.DataDirectory,
            _manager.ProfilesRootDirectory);

        return JsonSerializer.Serialize(new
        {
            total_used_formatted = _manager.StorageUsage.FormatByteSize(snap.TotalUsedBytes),
            total_used_bytes = snap.TotalUsedBytes,
            main_db_formatted = _manager.StorageUsage.FormatByteSize(snap.MainDatabaseBytes),
            audit_db_formatted = _manager.StorageUsage.FormatByteSize(snap.AuditDatabaseBytes),
            browser_profiles_formatted = _manager.StorageUsage.FormatByteSize(snap.BrowserProfilesBytes),
            process_ram_formatted = _manager.StorageUsage.FormatByteSize(snap.ProcessRamBytes),
            drive_free_formatted = _manager.StorageUsage.FormatByteSize(snap.DriveFreeBytes),
            profiles = snap.ProfileBreakdown.Select(p => new
            {
                profile_name = p.ProfileName,
                size_formatted = _manager.StorageUsage.FormatByteSize(p.SizeBytes),
                size_bytes = p.SizeBytes
            })
        });
    }
}
