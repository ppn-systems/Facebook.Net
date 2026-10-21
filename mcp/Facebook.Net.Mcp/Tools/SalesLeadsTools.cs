// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Leads;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Scraping Group Feeds, Buyer-Intent Lead Scoring, Customer/Lead Tagging, Deep Analytics, Post Inspection, Audit Logs, and Storage Metrics.
/// </summary>
[McpServerToolType]
public sealed class SalesLeadsTools(FacebookMcpSessionManager manager)
{
    private static readonly string[] s_defaultLeadTagColors =
    [
        "#F43F5E",
        "#10B981",
        "#3B82F6",
        "#F6821F",
        "#8B5CF6",
        "#06B6D4",
        "#EAB308",
        "#EC4899"
    ];

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
                author_uid = p.AuthorUid,
                author_name = p.AuthorName,
                message_text = p.MessageText,
                extracted_phones = _manager.LeadScoring.ExtractVietnamesePhoneNumbers(p.MessageText),
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

        HashSet<string> commentedPostIds = await this.GetCommentedPostIdsAsync(ct).ConfigureAwait(false);

        IReadOnlyList<PostSummary> dbPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId, searchKeyword: null, limit: 500, ct: ct)
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
            author_uid = p.AuthorUid,
            author_name = p.AuthorName,
            message_text = p.MessageText,
            extracted_phones = _manager.LeadScoring.ExtractVietnamesePhoneNumbers(p.MessageText),
            buyer_intent_score = _manager.LeadScoring.CalculateBuyerIntentScore(p.MessageText, keywords),
            lead_tags = p.GetTagIds(),
            notes = p.Notes,
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

    [McpServerTool(Name = "fb_list_lead_tags")]
    [Description("List all customer/lead color tags stored in local SQLite along with the number of scraped customer posts currently assigned each tag.")]
    public async Task<string> ListLeadTagsAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<GroupTag> leadTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<PostSummary> allPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allPosts.Count; i++)
        {
            IReadOnlyList<string> postTagIds = allPosts[i].GetTagIds();
            for (int t = 0; t < postTagIds.Count; t++)
            {
                string tid = postTagIds[t];
                counts[tid] = counts.TryGetValue(tid, out int c) ? c + 1 : 1;
            }
        }

        return JsonSerializer.Serialize(new
        {
            total_tags = leadTags.Count,
            total_scraped_posts = allPosts.Count,
            lead_tags = leadTags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex,
                tagged_posts_count = counts.TryGetValue(t.Id, out int c) ? c : 0
            })
        });
    }

    [McpServerTool(Name = "fb_create_lead_tags_batch")]
    [Description("Create one or multiple color-coded customer/lead tags in a single call (e.g. 'Khách VIP:#F43F5E; Cần gọi ngay:#10B981; Đã báo giá:#3B82F6; Môi giới:#8B5CF6').")]
    public async Task<string> CreateLeadTagsBatchAsync(
        [Description("Semicolon- or comma-separated lead tag specifications, optionally with ':ColorHex' (e.g. 'Khách VIP:#F43F5E; Cần gọi ngay:#10B981; Đã báo giá:#3B82F6')")] string tagsSpec,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagsSpec);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<GroupTag> existingTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
        HashSet<string> usedIds = new(existingTags.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        char[] separators = tagsSpec.Contains(';', StringComparison.Ordinal) ? [';'] : [',', ';'];
        string[] rawEntries = tagsSpec.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<GroupTag> createdTags = new(rawEntries.Length);
        long baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < rawEntries.Length; i++)
        {
            string entry = rawEntries[i];
            string tagName = entry;
            string? colorHex = null;

            int colonIdx = entry.LastIndexOf(':');
            if (colonIdx > 0 && colonIdx < entry.Length - 1)
            {
                string candidateColor = entry[(colonIdx + 1)..].Trim();
                if (candidateColor.StartsWith('#') || candidateColor.Length is 6 or 7)
                {
                    tagName = entry[..colonIdx].Trim();
                    colorHex = candidateColor.StartsWith('#') ? candidateColor : $"#{candidateColor}";
                }
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            string resolvedColor = !string.IsNullOrWhiteSpace(colorHex)
                ? colorHex
                : s_defaultLeadTagColors[createdTags.Count % s_defaultLeadTagColors.Length];

            string normInputName = _manager.LeadScoring.RemoveVietnameseDiacritics(tagName);
            GroupTag? existing = existingTags.FirstOrDefault(t =>
                string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normInputName, StringComparison.OrdinalIgnoreCase));

            GroupTag tag;
            if (existing is not null)
            {
                tag = !string.IsNullOrWhiteSpace(colorHex)
                    ? existing with { Name = tagName, ColorHex = resolvedColor }
                    : existing;
            }
            else
            {
                string generatedId = $"ltag_{baseTimestamp}_{i + 1}";
                while (!usedIds.Add(generatedId))
                {
                    generatedId = $"ltag_{baseTimestamp}_{Random.Shared.Next(1000, 9999)}";
                }

                tag = new GroupTag(generatedId, tagName, resolvedColor);
            }

            await _manager.AccountStore.UpsertLeadTagAsync(tag, ct).ConfigureAwait(false);
            createdTags.Add(tag);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            "Batch Lead Tags",
            $"MCP created/updated {createdTags.Count} customer/lead tags in batch.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            created_count = createdTags.Count,
            lead_tags = createdTags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex
            })
        });
    }

    [McpServerTool(Name = "fb_assign_lead_tag")]
    [Description("Assign or remove a customer/lead tag (auto-creating the tag if a new name is given) across one or more scraped post IDs OR all posts by a specific author UID, with optional CRM notes.")]
    public async Task<string> AssignLeadTagAsync(
        [Description("Lead tag ID or tag name (auto-created in lead_tags if not found)")] string tagIdOrName,
        [Description("Optional comma-separated Facebook Post IDs to tag")] string? postIdsCsv = null,
        [Description("Optional Facebook Author UID to tag all posts by that author")] string? authorUid = null,
        [Description("True to assign the tag, false to remove it (default true)")] bool assign = true,
        [Description("Optional CRM notes to save on the matched posts")] string? notes = null,
        [Description("Hex color code if auto-creating a new tag (default '#F43F5E')")] string colorHex = "#F43F5E",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagIdOrName);
        if (string.IsNullOrWhiteSpace(postIdsCsv) && string.IsNullOrWhiteSpace(authorUid))
        {
            throw new ArgumentException("Either 'postIdsCsv' or 'authorUid' must be provided.");
        }

        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        (GroupTag resolvedTag, bool createdNewTag) = await this.ResolveOrCreateLeadTagAsync(tagIdOrName, colorHex, ct).ConfigureAwait(false);

        HashSet<string> targetPostIds = string.IsNullOrWhiteSpace(postIdsCsv)
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                postIdsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal);

        string? cleanAuthorUid = string.IsNullOrWhiteSpace(authorUid) ? null : authorUid.Trim();

        IReadOnlyList<PostSummary> allPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        List<string> modifiedPostIds = [];
        for (int i = 0; i < allPosts.Count; i++)
        {
            PostSummary post = allPosts[i];
            bool matchesPost = targetPostIds.Contains(post.PostId);
            bool matchesAuthor = !string.IsNullOrWhiteSpace(cleanAuthorUid) &&
                (string.Equals(post.AuthorUid, cleanAuthorUid, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(post.AuthorName, cleanAuthorUid, StringComparison.OrdinalIgnoreCase));

            if (!matchesPost && !matchesAuthor)
            {
                continue;
            }

            string? updatedTagsCsv = ApplyTagIdToCsv(post.GetTagIds(), resolvedTag.Id, assign, out bool tagChanged);
            bool notesChanged = !string.IsNullOrWhiteSpace(notes) && !string.Equals(post.Notes, notes.Trim(), StringComparison.Ordinal);

            if (tagChanged || notesChanged)
            {
                await _manager.AccountStore.UpdateScrapedPostTagsAndNotesAsync(
                    post.PostId,
                    updatedTagsCsv,
                    string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    ct).ConfigureAwait(false);
                modifiedPostIds.Add(post.PostId);
            }
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag = new
            {
                id = resolvedTag.Id,
                name = resolvedTag.Name,
                color_hex = resolvedTag.ColorHex,
                was_created = createdNewTag
            },
            action = assign ? "assigned" : "removed",
            modified_count = modifiedPostIds.Count,
            modified_post_ids = modifiedPostIds
        });
    }

    [McpServerTool(Name = "fb_auto_tag_leads_by_rules")]
    [Description("Automatically find scraped customer posts matching filter rules (keywords, phone presence, buyer-intent score, zero-comment, inquiry question, or repeat author/seeder) and assign a customer/lead tag in SQLite.")]
    public async Task<string> AutoTagLeadsByRulesAsync(
        [Description("Lead tag ID or tag name (auto-created if not found)")] string tagNameOrId,
        [Description("Optional comma-separated keywords to match against post message, author, or group")] string? keywords = null,
        [Description("If true, only tag posts containing extracted Vietnamese phone numbers")] bool onlyWithPhone = false,
        [Description("Minimum buyer-intent score (0..100, default 0)")] int minIntentScore = 0,
        [Description("If true, only tag posts with 0 comments (first-mover opportunities)")] bool onlyZeroComments = false,
        [Description("If true, only tag posts containing buyer inquiry/question signals")] bool onlyInquiryQuestions = false,
        [Description("If true, only tag posts from repeat authors who posted >= 2 times across scraped feeds (e.g. seeders/brokers)")] bool onlyRepeatAuthors = false,
        [Description("Hex color code if auto-creating a new tag (default '#F43F5E')")] string colorHex = "#F43F5E",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagNameOrId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        (GroupTag resolvedTag, bool createdNewTag) = await this.ResolveOrCreateLeadTagAsync(tagNameOrId, colorHex, ct).ConfigureAwait(false);

        IReadOnlyList<PostSummary> allPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        HashSet<string> repeatAuthorKeys = new(StringComparer.OrdinalIgnoreCase);
        if (onlyRepeatAuthors)
        {
            Dictionary<string, int> authorCounts = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allPosts.Count; i++)
            {
                string key = !string.IsNullOrWhiteSpace(allPosts[i].AuthorUid)
                    ? allPosts[i].AuthorUid!.Trim()
                    : (allPosts[i].AuthorName?.Trim() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    authorCounts[key] = authorCounts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            foreach (KeyValuePair<string, int> kvp in authorCounts)
            {
                if (kvp.Value >= 2)
                {
                    _ = repeatAuthorKeys.Add(kvp.Key);
                }
            }
        }

        string[] kwArray = string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<object> matchedSummary = [];
        int newlyTaggedCount = 0;

        for (int i = 0; i < allPosts.Count; i++)
        {
            PostSummary post = allPosts[i];

            if (kwArray.Length > 0 && !_manager.LeadScoring.MatchesPostOrAuthorKeywords(post, kwArray))
            {
                continue;
            }

            IReadOnlyList<string> phones = _manager.LeadScoring.ExtractVietnamesePhoneNumbers(post.MessageText);
            if (onlyWithPhone && phones.Count == 0)
            {
                continue;
            }

            int score = _manager.LeadScoring.CalculateBuyerIntentScore(post.MessageText, keywords);
            if (minIntentScore > 0 && score < minIntentScore)
            {
                continue;
            }

            if (onlyZeroComments && post.CommentCount > 0)
            {
                continue;
            }

            if (onlyInquiryQuestions && !_manager.LeadScoring.HasBuyerInquirySignal(post.MessageText))
            {
                continue;
            }

            if (onlyRepeatAuthors)
            {
                string authorKey = !string.IsNullOrWhiteSpace(post.AuthorUid)
                    ? post.AuthorUid.Trim()
                    : (post.AuthorName?.Trim() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(authorKey) || !repeatAuthorKeys.Contains(authorKey))
                {
                    continue;
                }
            }

            string? updatedCsv = ApplyTagIdToCsv(post.GetTagIds(), resolvedTag.Id, assign: true, out bool changed);
            if (changed)
            {
                await _manager.AccountStore.UpdateScrapedPostTagsAndNotesAsync(post.PostId, updatedCsv, notes: null, ct).ConfigureAwait(false);
                newlyTaggedCount++;
            }

            matchedSummary.Add(new
            {
                post_id = post.PostId,
                author_name = post.AuthorName,
                group_name = post.GroupName,
                buyer_intent_score = score,
                extracted_phones = phones,
                newly_tagged = changed
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            resolvedTag.Name,
            $"MCP auto-tagged {newlyTaggedCount} customer leads (matched {matchedSummary.Count}) with tag '{resolvedTag.Name}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag = new
            {
                id = resolvedTag.Id,
                name = resolvedTag.Name,
                color_hex = resolvedTag.ColorHex,
                was_created = createdNewTag
            },
            matched_count = matchedSummary.Count,
            newly_tagged_count = newlyTaggedCount,
            matched_leads = matchedSummary.Take(100)
        });
    }

    [McpServerTool(Name = "fb_search_leads_advanced")]
    [Description("Multi-dimensional lead search across local SQLite scraped posts supporting phone extraction, buyer-intent threshold, inquiry questions, max comments, time window, media filter, group tag, and customer lead tag.")]
    public async Task<string> SearchLeadsAdvancedAsync(
        [Description("Optional comma-separated keywords to filter and score buyer intent")] string? keywords = null,
        [Description("Comment status filter: 'all', 'pending' (not yet commented), or 'commented' (default 'all')")] string commentStatus = "all",
        [Description("If true, only return leads containing extracted Vietnamese phone numbers")] bool onlyWithPhone = false,
        [Description("Minimum buyer-intent score (0..100, default 0)")] int minIntentScore = 0,
        [Description("If true, only return leads containing buyer inquiry/question signals")] bool onlyInquiryQuestions = false,
        [Description("Optional maximum comment count (e.g. 0 for zero-comment first-mover leads, 2 for low-competition leads)")] int? maxComments = null,
        [Description("Optional recency filter in hours (e.g. 1, 24, 72, 168)")] int? hoursAgo = null,
        [Description("Optional media filter: 'has_media', 'has_photo', 'has_video', or 'text_only'")] string? mediaFilter = null,
        [Description("Optional group color tag ID or name filter")] string? groupTagId = null,
        [Description("Optional customer/lead color tag ID or name filter")] string? leadTagId = null,
        [Description("Maximum number of leads to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        HashSet<string> commentedPostIds = await this.GetCommentedPostIdsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<PostSummary> dbPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        HashSet<string>? allowedGroupIdsByTag = null;
        if (!string.IsNullOrWhiteSpace(groupTagId))
        {
            string cleanGroupTag = groupTagId.Trim();
            string normGroupTag = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanGroupTag);
            IReadOnlyList<GroupTag> groupTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
            GroupTag? matchedGroupTag = groupTags.FirstOrDefault(t =>
                string.Equals(t.Id, cleanGroupTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, cleanGroupTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normGroupTag, StringComparison.OrdinalIgnoreCase));

            string effectiveGroupTagId = matchedGroupTag?.Id ?? cleanGroupTag;
            IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
            allowedGroupIdsByTag = new HashSet<string>(
                groups.Where(g => g.HasTag(effectiveGroupTagId)).Select(g => g.GroupId),
                StringComparer.OrdinalIgnoreCase);
        }

        string? effectiveLeadTagId = null;
        if (!string.IsNullOrWhiteSpace(leadTagId))
        {
            string cleanLeadTag = leadTagId.Trim();
            string normLeadTag = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanLeadTag);
            IReadOnlyList<GroupTag> leadTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
            GroupTag? matchedLeadTag = leadTags.FirstOrDefault(t =>
                string.Equals(t.Id, cleanLeadTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, cleanLeadTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normLeadTag, StringComparison.OrdinalIgnoreCase));
            effectiveLeadTagId = matchedLeadTag?.Id ?? cleanLeadTag;
        }

        string[] kwArray = string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string normStatus = commentStatus?.Trim().ToLowerInvariant() ?? "all";
        string normMedia = mediaFilter?.Trim().ToLowerInvariant() ?? string.Empty;
        DateTimeOffset? cutoffUtc = hoursAgo.HasValue && hoursAgo.Value > 0
            ? DateTimeOffset.UtcNow.AddHours(-hoursAgo.Value)
            : null;

        List<(PostSummary Post, int Score, IReadOnlyList<string> Phones, bool HasInquiry)> matched = [];

        for (int i = 0; i < dbPosts.Count; i++)
        {
            PostSummary post = dbPosts[i];

            if (allowedGroupIdsByTag is not null &&
                (string.IsNullOrWhiteSpace(post.GroupId) || !allowedGroupIdsByTag.Contains(post.GroupId)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(effectiveLeadTagId) && !post.HasTag(effectiveLeadTagId))
            {
                continue;
            }

            bool isCommented = commentedPostIds.Contains(post.PostId);
            if (normStatus == "pending" && isCommented)
            {
                continue;
            }

            if (normStatus == "commented" && !isCommented)
            {
                continue;
            }

            if (maxComments.HasValue && post.CommentCount > maxComments.Value)
            {
                continue;
            }

            if (cutoffUtc.HasValue && (!post.CreatedAt.HasValue || post.CreatedAt.Value < cutoffUtc.Value))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(normMedia))
            {
                bool hasMedia = !string.IsNullOrWhiteSpace(post.MediaPreviewUrl) || post.IsVideo;
                bool passMedia = normMedia switch
                {
                    "has_media" => hasMedia,
                    "has_photo" => !string.IsNullOrWhiteSpace(post.MediaPreviewUrl) && !post.IsVideo,
                    "has_video" => post.IsVideo,
                    "text_only" => !hasMedia,
                    _ => true
                };
                if (!passMedia)
                {
                    continue;
                }
            }

            if (kwArray.Length > 0 && !_manager.LeadScoring.MatchesPostOrAuthorKeywords(post, kwArray))
            {
                continue;
            }

            IReadOnlyList<string> phones = _manager.LeadScoring.ExtractVietnamesePhoneNumbers(post.MessageText);
            if (onlyWithPhone && phones.Count == 0)
            {
                continue;
            }

            bool hasInquiry = _manager.LeadScoring.HasBuyerInquirySignal(post.MessageText);
            if (onlyInquiryQuestions && !hasInquiry)
            {
                continue;
            }

            int score = _manager.LeadScoring.CalculateBuyerIntentScore(post.MessageText, keywords);
            if (minIntentScore > 0 && score < minIntentScore)
            {
                continue;
            }

            matched.Add((post, score, phones, hasInquiry));
        }

        matched.Sort(static (a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0)
            {
                return cmp;
            }

            return b.Phones.Count.CompareTo(a.Phones.Count);
        });

        int take = Math.Clamp(limit, 1, 200);
        var items = matched.Take(take).Select(x => new
        {
            post_id = x.Post.PostId,
            author_uid = x.Post.AuthorUid,
            author_name = x.Post.AuthorName,
            group_id = x.Post.GroupId,
            group_name = x.Post.GroupName,
            message_text = x.Post.MessageText,
            extracted_phones = x.Phones,
            buyer_intent_score = x.Score,
            has_inquiry_signal = x.HasInquiry,
            lead_tags = x.Post.GetTagIds(),
            notes = x.Post.Notes,
            is_commented = commentedPostIds.Contains(x.Post.PostId),
            reaction_count = x.Post.ReactionCount,
            comment_count = x.Post.CommentCount,
            permalink_url = x.Post.PermalinkUrl,
            created_at = x.Post.CreatedAt?.ToString("o")
        });

        return JsonSerializer.Serialize(new
        {
            total_matched = matched.Count,
            leads = items
        });
    }

    [McpServerTool(Name = "fb_update_lead_notes")]
    [Description("Update CRM notes on a scraped customer post in local SQLite while preserving its existing lead tags.")]
    public async Task<string> UpdateLeadNotesAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("CRM notes to store on the lead post")] string notes,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanId = postId.Trim();
        IReadOnlyList<PostSummary> allPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        PostSummary? existing = allPosts.FirstOrDefault(p => string.Equals(p.PostId, cleanId, StringComparison.Ordinal));
        await _manager.AccountStore.UpdateScrapedPostTagsAndNotesAsync(
            cleanId,
            existing?.TagsCsv,
            notes?.Trim() ?? string.Empty,
            ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            post_id = cleanId,
            lead_tags = existing?.GetTagIds() ?? [],
            notes = notes?.Trim() ?? string.Empty
        });
    }

    [McpServerTool(Name = "fb_delete_lead_tag")]
    [Description("Delete a customer/lead color tag or clear it from all scraped customer posts in local SQLite.")]
    public async Task<string> DeleteLeadTagAsync(
        [Description("Customer/lead tag ID or tag name to delete or clear")] string tagNameOrId,
        [Description("If true, deletes the lead tag definition completely; if false, only removes the tag assignment from all scraped posts (default true)")] bool deleteTagDefinition = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagNameOrId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanTarget = tagNameOrId.Trim();
        string normTarget = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTarget);

        IReadOnlyList<GroupTag> leadTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
        GroupTag? targetTag = leadTags.FirstOrDefault(t =>
            string.Equals(t.Id, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTarget, StringComparison.OrdinalIgnoreCase));

        string effectiveTagId = targetTag?.Id ?? cleanTarget;
        IReadOnlyList<PostSummary> allPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);

        int clearedCount = 0;
        for (int i = 0; i < allPosts.Count; i++)
        {
            PostSummary post = allPosts[i];
            string? updatedCsv = ApplyTagIdToCsv(post.GetTagIds(), effectiveTagId, assign: false, out bool changed);
            if (changed)
            {
                await _manager.AccountStore.UpdateScrapedPostTagsAndNotesAsync(post.PostId, updatedCsv, notes: null, ct).ConfigureAwait(false);
                clearedCount++;
            }
        }

        bool deletedDef = false;
        if (deleteTagDefinition && targetTag is not null)
        {
            deletedDef = await _manager.AccountStore.DeleteLeadTagAsync(targetTag.Id, ct).ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag_id = effectiveTagId,
            tag_name = targetTag?.Name ?? cleanTarget,
            deleted_definition = deletedDef,
            cleared_posts_count = clearedCount
        });
    }

    [McpServerTool(Name = "fb_analyze_workspace_data")]
    [Description("Compute a deep multi-dimensional analytics report across all scraped group posts and target groups: Group ROI ranking, low-performing groups, repeat authors/seeders detection, top inquiry keywords, and peak posting hours.")]
    public async Task<string> AnalyzeWorkspaceDataAsync(
        [Description("Optional comma-separated buyer keywords to include in scoring and term frequency analysis")] string? buyerKeywords = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<PostSummary> scrapedPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 2000, ct: ct)
            .ConfigureAwait(false);
        IReadOnlyList<SavedTargetGroup> targetGroups = await _manager.AccountStore
            .GetAllTargetGroupsAsync(ct)
            .ConfigureAwait(false);
        HashSet<string> commentedPostIds = await this.GetCommentedPostIdsAsync(ct).ConfigureAwait(false);

        WorkspaceDataAnalyticsReport report = _manager.LeadScoring.AnalyzeWorkspaceData(
            scrapedPosts,
            targetGroups,
            commentedPostIds,
            buyerKeywords);
        string formattedSummary = _manager.LeadScoring.FormatAnalyticsReportForAi(report);

        return JsonSerializer.Serialize(new
        {
            formatted_summary = formattedSummary,
            report = new
            {
                total_scraped_posts = report.TotalScrapedPosts,
                total_posts_with_phone = report.TotalPostsWithPhone,
                total_high_intent_posts = report.TotalHighIntentPosts,
                total_zero_comment_opportunities = report.TotalZeroCommentOpportunities,
                total_commented_posts = report.TotalCommentedPosts,
                total_pending_posts = report.TotalPendingPosts,
                top_performing_groups = report.TopPerformingGroups,
                low_performing_groups = report.LowPerformingGroups,
                repeat_authors = report.RepeatAuthors,
                top_inquiry_terms = report.TopInquiryTerms.Select(t => new { term = t.Term, count = t.Count }),
                peak_posting_hours = report.PeakPostingHours.Select(h => new { hour_of_day = h.HourOfDay, post_count = h.PostCount })
            }
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

    private async Task<HashSet<string>> GetCommentedPostIdsAsync(CancellationToken ct)
    {
        IReadOnlyList<ActionHistoryEntry> actions = await _manager.AccountStore
            .GetRecentActionsAsync(1000, ct)
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

        return commentedPostIds;
    }

    private async Task<(GroupTag Tag, bool CreatedNew)> ResolveOrCreateLeadTagAsync(
        string tagIdOrName,
        string? colorHex,
        CancellationToken ct)
    {
        string cleanTarget = tagIdOrName.Trim();
        string normTarget = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTarget);

        IReadOnlyList<GroupTag> leadTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
        GroupTag? existing = leadTags.FirstOrDefault(t =>
            string.Equals(t.Id, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTarget, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return (existing, false);
        }

        string resolvedColor = string.IsNullOrWhiteSpace(colorHex) ? "#F43F5E" : colorHex.Trim();
        GroupTag created = new(
            Id: $"ltag_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name: cleanTarget,
            ColorHex: resolvedColor);

        await _manager.AccountStore.UpsertLeadTagAsync(created, ct).ConfigureAwait(false);
        return (created, true);
    }

    private static string? ApplyTagIdToCsv(IReadOnlyList<string> currentTagIds, string tagId, bool assign, out bool changed)
    {
        List<string> ids = [.. currentTagIds];
        changed = false;

        if (assign)
        {
            bool exists = false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                ids.Add(tagId);
                changed = true;
            }
        }
        else
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    ids.RemoveAt(i);
                    changed = true;
                }
            }
        }

        return ids.Count > 0 ? string.Join(',', ids) : null;
    }
}
