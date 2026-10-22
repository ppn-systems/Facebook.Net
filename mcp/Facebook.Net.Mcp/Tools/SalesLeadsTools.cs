// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Leads;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Scraping Group Feeds, Batch Multi-Group Scraping, Buyer-Intent Lead Scoring, Customer/Lead CRM Tagging, Automated Batch Lead Commenting, Deep Analytics, and Post Inspection.
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

        return McpJson.Serialize(new
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

    [McpServerTool(Name = "fb_scrape_target_groups_batch")]
    [Description("Batch-scrape latest posts across multiple enabled Target Groups (or groups matching filterByTagIdOrName) with anti-checkpoint delay, persisting all matched posts to SQLite and returning a lead summary.")]
    public async Task<string> ScrapeTargetGroupsBatchAsync(
        [Description("Number of posts to scrape per group (5..50, default 15)")] int postsPerGroup = 15,
        [Description("Maximum number of target groups to scrape in this batch (1..50, default 15)")] int maxGroups = 15,
        [Description("Optional group color tag ID or tag name to filter target groups")] string? filterByTagIdOrName = null,
        [Description("Optional comma-separated buyer filter keywords to score and filter posts")] string? filterKeywords = null,
        [Description("Anti-checkpoint delay in seconds between group feed requests (1..60, default 2)")] int delaySeconds = 2,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<SavedTargetGroup> allGroups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        string? effectiveTagId = null;

        if (!string.IsNullOrWhiteSpace(filterByTagIdOrName))
        {
            string cleanTag = filterByTagIdOrName.Trim();
            string normTag = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTag);
            IReadOnlyList<GroupTag> groupTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
            GroupTag? matchedTag = groupTags.FirstOrDefault(t =>
                string.Equals(t.Id, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTag, StringComparison.OrdinalIgnoreCase));
            effectiveTagId = matchedTag?.Id ?? cleanTag;
        }

        int capGroups = Math.Clamp(maxGroups, 1, 50);
        int perGroup = Math.Clamp(postsPerGroup, 5, 50);
        int delaySec = Math.Clamp(delaySeconds, 1, 60);

        List<SavedTargetGroup> selectedGroups = [];
        for (int i = 0; i < allGroups.Count && selectedGroups.Count < capGroups; i++)
        {
            SavedTargetGroup g = allGroups[i];
            if (!string.IsNullOrWhiteSpace(effectiveTagId))
            {
                if (g.HasTag(effectiveTagId))
                {
                    selectedGroups.Add(g);
                }
            }
            else if (g.IsEnabled)
            {
                selectedGroups.Add(g);
            }
        }

        string[] keywords = string.IsNullOrWhiteSpace(filterKeywords)
            ? []
            : filterKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<PostSummary> allMatchedPosts = [];
        List<object> groupSummaries = [];
        int totalWithPhone = 0;
        int totalHighIntent = 0;

        for (int i = 0; i < selectedGroups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            SavedTargetGroup grp = selectedGroups[i];
            PagedResult<PostSummary> feed = await _manager.CometClient
                .GetGroupFeedAsync(session, grp.GroupId, count: perGroup, ct: ct)
                .ConfigureAwait(false);

            List<PostSummary> groupMatched = [];
            int groupPhones = 0;
            int groupHighIntent = 0;

            for (int p = 0; p < feed.Items.Count; p++)
            {
                PostSummary post = feed.Items[p] with
                {
                    GroupId = grp.GroupId,
                    GroupName = grp.GroupName
                };

                if (!_manager.LeadScoring.MatchesBuyerKeywords(post.MessageText, keywords))
                {
                    continue;
                }

                groupMatched.Add(post);
                allMatchedPosts.Add(post);

                if (_manager.LeadScoring.ExtractVietnamesePhoneNumbers(post.MessageText).Count > 0)
                {
                    groupPhones++;
                    totalWithPhone++;
                }

                if (_manager.LeadScoring.CalculateBuyerIntentScore(post.MessageText, filterKeywords) >= 75)
                {
                    groupHighIntent++;
                    totalHighIntent++;
                }
            }

            if (groupMatched.Count > 0)
            {
                await _manager.AccountStore.UpsertScrapedPostsAsync(groupMatched, ct).ConfigureAwait(false);
            }

            groupSummaries.Add(new
            {
                group_id = grp.GroupId,
                group_name = grp.GroupName,
                scraped_count = groupMatched.Count,
                with_phone_count = groupPhones,
                high_intent_count = groupHighIntent
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Success,
            account,
            "Batch Scrape",
            $"MCP batch-scraped {allMatchedPosts.Count} posts across {selectedGroups.Count} groups ({totalWithPhone} with phone).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            groups_scraped_count = selectedGroups.Count,
            total_posts_saved = allMatchedPosts.Count,
            total_posts_with_phone = totalWithPhone,
            total_high_intent_posts = totalHighIntent,
            groups = groupSummaries
        });
    }

    [McpServerTool(Name = "fb_auto_comment_leads_batch")]
    [Description("Find uncommented ('pending') scraped customer leads matching filter criteria (keywords, phone presence, minimum buyer-intent score) and automatically post personalized Spintax sales comments ('{author_name}', '{group_name}', '{date}', '{time}', '{random_id}') with duplicate-comment protection, multi-account rotation ('accountUidsCsv'), and anti-checkpoint delay.")]
    public async Task<string> AutoCommentLeadsBatchAsync(
        [Description("Spintax sales comment template (supports {author_name}, {group_name}, {date}, {time}, {random_id})")] string commentSpintax,
        [Description("Optional comma-separated keywords to filter leads")] string? keywords = null,
        [Description("If true, only comment on leads containing extracted Vietnamese phone numbers")] bool onlyWithPhone = false,
        [Description("Minimum buyer-intent score (0..100, default 60)")] int minIntentScore = 60,
        [Description("Maximum number of leads to comment on in this batch (1..25, default 5)")] int maxLeadsToComment = 5,
        [Description("Anti-checkpoint delay in seconds between comments (2..120, default 5)")] int delaySeconds = 5,
        [Description("Automatically react (Like/Love) to each post before commenting (default true)")] bool autoReactFirst = true,
        [Description("Optional local image file path to upload and attach to comments")] string? mediaFilePath = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        [Description("Optional comma-separated Facebook account UIDs (or 'all') to rotate across leads in round-robin fashion for anti-checkpoint load balancing")] string? accountUidsCsv = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commentSpintax);

        IReadOnlyList<string> rotationUids = await _manager
            .ResolveRotationAccountUidsAsync(accountUidsCsv, accountUid, ct)
            .ConfigureAwait(false);

        (AccountProfile primaryAccount, IBrowserSession primarySession) = await _manager
            .AcquireBrowserSessionAsync(rotationUids[0], forceVisible: false, ct)
            .ConfigureAwait(false);

        HashSet<string> commentedPostIds = await this.GetCommentedPostIdsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<PostSummary> dbPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 1000, ct: ct)
            .ConfigureAwait(false);

        string[] kwArray = string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<(PostSummary Post, int Score)> candidates = [];
        for (int i = 0; i < dbPosts.Count; i++)
        {
            PostSummary post = dbPosts[i];
            if (string.IsNullOrWhiteSpace(post.PostId) || commentedPostIds.Contains(post.PostId))
            {
                continue;
            }

            if (kwArray.Length > 0 && !_manager.LeadScoring.MatchesPostOrAuthorKeywords(post, kwArray))
            {
                continue;
            }

            if (onlyWithPhone && _manager.LeadScoring.ExtractVietnamesePhoneNumbers(post.MessageText).Count == 0)
            {
                continue;
            }

            int score = _manager.LeadScoring.CalculateBuyerIntentScore(post.MessageText, keywords);
            if (score < minIntentScore)
            {
                continue;
            }

            candidates.Add((post, score));
        }

        candidates.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        int cap = Math.Clamp(maxLeadsToComment, 1, 25);
        int delaySec = Math.Clamp(delaySeconds, 2, 120);

        Dictionary<string, string?> attachmentByUid = new(StringComparer.Ordinal);
        string? cleanMediaPath = !string.IsNullOrWhiteSpace(mediaFilePath) && File.Exists(mediaFilePath.Trim())
            ? mediaFilePath.Trim()
            : null;

        List<object> commentedResults = [];
        for (int i = 0; i < candidates.Count && commentedResults.Count < cap; i++)
        {
            ct.ThrowIfCancellationRequested();
            PostSummary lead = candidates[i].Post;

            string currentUid = rotationUids[commentedResults.Count % rotationUids.Count];
            (AccountProfile account, IBrowserSession session) = string.Equals(currentUid, primaryAccount.Uid, StringComparison.Ordinal)
                ? (primaryAccount, primarySession)
                : await _manager.AcquireBrowserSessionAsync(currentUid, forceVisible: false, ct).ConfigureAwait(false);

            bool alreadyDone = await _manager.AccountStore
                .HasExecutedActionAsync(session.AccountUid, "COMMENT", lead.PostId, ct)
                .ConfigureAwait(false);
            if (alreadyDone)
            {
                continue;
            }

            if (commentedResults.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            if (autoReactFirst)
            {
                ReactionKind reaction = Random.Shared.NextDouble() < 0.75 ? ReactionKind.Like : ReactionKind.Love;
                _ = await _manager.CometClient.ReactToPostAsync(session, lead.PostId, reaction, ct).ConfigureAwait(false);
            }

            string? attachmentFbid = null;
            if (cleanMediaPath is not null)
            {
                if (!attachmentByUid.TryGetValue(account.Uid, out attachmentFbid))
                {
                    attachmentFbid = await _manager.CometClient
                        .UploadUfiPhotoAsync(session, cleanMediaPath, ct)
                        .ConfigureAwait(false);
                    attachmentByUid[account.Uid] = attachmentFbid;
                }
            }

            string personalizedComment = _manager.CampaignPublisher.ResolveDynamicVariables(
                commentSpintax,
                lead.AuthorName,
                lead.GroupName);

            string? commentId = await _manager.CometClient
                .CreateCommentAsync(session, lead.PostId, personalizedComment, attachmentFbid, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(commentId))
            {
                await _manager.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(session.AccountUid, "COMMENT", lead.PostId, commentId, DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);

                commentedResults.Add(new
                {
                    post_id = lead.PostId,
                    comment_id = commentId,
                    account_uid = account.Uid,
                    author_name = lead.AuthorName,
                    group_name = lead.GroupName,
                    buyer_intent_score = candidates[i].Score,
                    permalink_url = lead.PermalinkUrl
                });
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            primaryAccount,
            "Auto Comment Leads Batch",
            $"MCP auto-commented on {commentedResults.Count} high-intent leads across {rotationUids.Count} account(s).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = primaryAccount.Uid,
            accounts_rotated = rotationUids,
            matched_pending_leads = candidates.Count,
            commented_count = commentedResults.Count,
            commented_leads = commentedResults
        });
    }

    [McpServerTool(Name = "fb_delete_scraped_leads")]
    [Description("Delete scraped customer posts from the local SQLite database (either for a specific groupId or all scraped posts when clearAll = true).")]
    public async Task<string> DeleteScrapedLeadsAsync(
        [Description("Optional Facebook Group ID whose scraped posts should be cleared")] string? groupId = null,
        [Description("Set to true to clear all scraped posts across all groups when groupId is omitted")] bool clearAll = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupId) && !clearAll)
        {
            throw new ArgumentException("Specify either 'groupId' or set 'clearAll = true' to clear all scraped leads.");
        }

        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        string? cleanGroupId = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        int deletedCount = await _manager.AccountStore.ClearScrapedPostsAsync(cleanGroupId, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Info,
            account: null,
            cleanGroupId ?? "All Scraped Leads",
            $"MCP cleared {deletedCount} scraped lead posts from SQLite.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            group_id = cleanGroupId,
            cleared_all = cleanGroupId is null,
            deleted_posts_count = deletedCount
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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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

            string defaultFallbackColor = s_defaultLeadTagColors[createdTags.Count % s_defaultLeadTagColors.Length];
            string resolvedColor = !string.IsNullOrWhiteSpace(colorHex)
                ? _manager.GroupTagService.NormalizeColorHex(colorHex, defaultFallbackColor)
                : defaultFallbackColor;

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

        return McpJson.Serialize(new
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

            (string? updatedTagsCsv, bool tagChanged) = _manager.LeadScoring.ApplyLeadTagChange(post.GetTagIds(), resolvedTag.Id, assign);
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

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            resolvedTag.Name,
            $"MCP {(assign ? "assigned" : "removed")} lead tag '{resolvedTag.Name}' on {modifiedPostIds.Count} customer post(s).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
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

            (string? updatedCsv, bool changed) = _manager.LeadScoring.ApplyLeadTagChange(post.GetTagIds(), resolvedTag.Id, assign: true);
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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            cleanId,
            $"MCP updated CRM notes on lead post '{cleanId}'.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
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
            (string? updatedCsv, bool changed) = _manager.LeadScoring.ApplyLeadTagChange(post.GetTagIds(), effectiveTagId, assign: false);
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

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            account: null,
            targetTag?.Name ?? cleanTarget,
            $"MCP deleted/cleared lead tag '{targetTag?.Name ?? cleanTarget}' (cleared {clearedCount} posts, deletedDefinition={deletedDef}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
        {
            post,
            reactors,
            commenters
        });
    }

    [McpServerTool(Name = "fb_get_post_commenters")]
    [Description("Scrape users who commented on a specific Facebook post ('CommentsListComponentsPaginationQuery') — ideal for competitor post hijacking or checking customer replies on your own published posts.")]
    public async Task<string> GetPostCommentersAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanId = postId.Trim();
        PagedResult<UserSummary> commenters = await _manager.CometClient
            .GetPostCommentersAsync(session, cleanId, ct: ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            post_id = cleanId,
            count = commenters.Items.Count,
            has_next_page = commenters.HasNextPage,
            commenters = commenters.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_get_post_reactors")]
    [Description("Scrape users who reacted to a specific Facebook post ('CometUFIReactionsDialogTabContentRefetchQuery') to build a warm audience for friend requests or outreach.")]
    public async Task<string> GetPostReactorsAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("Maximum number of reactors to fetch (1..100, default 30)")] int count = 30,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanId = postId.Trim();
        PagedResult<UserSummary> reactors = await _manager.CometClient
            .GetPostReactorsAsync(session, cleanId, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            post_id = cleanId,
            count = reactors.Items.Count,
            has_next_page = reactors.HasNextPage,
            reactors = reactors.Items.Select(u => new
            {
                uid = u.Uid,
                name = u.Name,
                avatar_uri = u.AvatarUri,
                friendship_state = u.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_scrape_news_feed")]
    [Description("Scrape posts from the active Facebook account's home News Feed ('CometNewsFeedPaginationQuery'), automatically score buyer intent, extract obfuscated Vietnamese phone numbers, and optionally save high-intent leads into the local SQLite database.")]
    public async Task<string> ScrapeNewsFeedAsync(
        [Description("Number of News Feed posts to fetch from Facebook (1..50, default 15)")] int count = 15,
        [Description("Optional comma-separated buyer-intent keywords to filter and score posts")] string? keywords = null,
        [Description("If true, only return posts containing extracted Vietnamese phone numbers (default false)")] bool onlyWithPhone = false,
        [Description("If true, persists scraped News Feed posts into the local SQLite scraped_posts vault (default true)")] bool saveToVault = true,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        int clampedCount = Math.Clamp(count, 1, 50);
        PagedResult<PostSummary> feed = await _manager.CometClient
            .GetNewsFeedAsync(session, count: clampedCount, ct: ct)
            .ConfigureAwait(false);

        if (saveToVault && feed.Items.Count > 0)
        {
            await _manager.AccountStore.UpsertScrapedPostsAsync(feed.Items, ct).ConfigureAwait(false);
        }

        string[] kwArray = string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<(int Score, object Item)> scored = [];
        for (int i = 0; i < feed.Items.Count; i++)
        {
            PostSummary post = feed.Items[i];
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
            scored.Add((score, new
            {
                post_id = post.PostId,
                group_id = post.GroupId,
                group_name = post.GroupName,
                author_uid = post.AuthorUid,
                author_name = post.AuthorName,
                buyer_intent_score = score,
                extracted_phones = phones,
                message_text = post.MessageText,
                reaction_count = post.ReactionCount,
                comment_count = post.CommentCount,
                created_at = post.CreatedAt?.ToString("o"),
                permalink = post.PermalinkUrl
            }));
        }

        scored.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        _ = await _manager.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Success,
            account,
            "News Feed",
            $"MCP scraped {feed.Items.Count} News Feed post(s) (matched={scored.Count}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            total_fetched = feed.Items.Count,
            matched_count = scored.Count,
            posts = scored.Select(s => s.Item)
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
}
