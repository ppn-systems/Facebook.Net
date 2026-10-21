// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Leads;
using Facebook.Net.UI.Core.Publisher;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Group Buyer-Intent Lead Scanning,
/// Lead Scoring, Visual Sales Commenting, Anti-Checkpoint Circuit Breaker, and Lead Export.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets the local database search/filter keywords on the Sales Leads page (does not call Facebook).</summary>
    [ObservableProperty]
    public partial string BuyerIntentKeywords { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional Group ID filter when querying stored posts from the local SQLite database (<see langword="null"/> or empty = all groups).</summary>
    [ObservableProperty]
    public partial string? FilterScrapedGroupId { get; set; }

    /// <summary>Gets or sets the selective keywords used to filter which scraped posts are persisted into the SQLite database (empty = save all posts).</summary>
    [ObservableProperty]
    public partial string ScrapeFilterKeywords { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of posts to scrape per group during each collection cycle (5..100).</summary>
    [ObservableProperty]
    public partial int ScrapePostsPerGroup { get; set; } = 15;

    /// <summary>Gets or sets a value indicating whether to scrape all enabled Target Groups (or only the currently selected Target Group).</summary>
    [ObservableProperty]
    public partial bool ScrapeAllEnabledTargetGroups { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the periodic background scraper is enabled.</summary>
    [ObservableProperty]
    public partial bool AutoBackgroundScrapeEnabled { get; set; }

    /// <summary>Gets or sets the interval in minutes between background auto-scrape runs (1..120).</summary>
    [ObservableProperty]
    public partial int AutoScrapeIntervalMinutes { get; set; } = 5;

    /// <summary>Gets or sets the minimum anti-ban rate-limit delay in seconds between scraping two groups (0..600).</summary>
    [ObservableProperty]
    public partial double ScrapeDelayMinSeconds { get; set; } = 1.0;

    /// <summary>Gets or sets the maximum anti-ban rate-limit delay in seconds between scraping two groups (0..600).</summary>
    [ObservableProperty]
    public partial double ScrapeDelayMaxSeconds { get; set; } = 2.0;

    /// <summary>Gets or sets the maximum number of groups scraped in a single cycle using round-robin rotation (1..500).</summary>
    [ObservableProperty]
    public partial int ScrapeMaxGroupsPerCycle { get; set; } = 30;

    /// <summary>Gets or sets the comment status filter for the local DB leads table (<c>0</c> = All, <c>1</c> = Uncommented, <c>2</c> = Commented, <c>3</c> = Has Phone).</summary>
    [ObservableProperty]
    public partial int LeadCommentStatusFilter { get; set; }

    /// <summary>Gets or sets a value indicating whether to filter the local DB leads table to only posts containing a Vietnamese phone number.</summary>
    [ObservableProperty]
    public partial bool FilterOnlyPostsWithPhone { get; set; }

    /// <summary>Gets or sets the visually selected post card from <see cref="ScrapedPosts"/> for sales commenting.</summary>
    [ObservableProperty]
    public partial PostSummary? SelectedScrapedPost { get; set; }

    /// <summary>Gets or sets the Spintax sales comment text.</summary>
    [ObservableProperty]
    public partial string SalesCommentSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional local product/price photo path for sales commenting.</summary>
    [ObservableProperty]
    public partial string SalesCommentPhotoPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to automatically react (Like/Love) to a post before dropping a sales comment.</summary>
    [ObservableProperty]
    public partial bool AutoReactBeforeComment { get; set; } = true;

    /// <summary>Gets the collection of user-defined or AI-created color-coded customer/lead tags.</summary>
    public BulkObservableCollection<GroupTag> LeadTags { get; } = new();

    internal IReadOnlyList<PostSummary> AllScrapedPosts => _allScrapedPosts;

    internal IReadOnlySet<string> CommentedPostIds => _commentedPostIds;

    private readonly List<PostSummary> _allScrapedPosts = new();
    private readonly HashSet<string> _commentedPostIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _leadIntentFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _leadGroupTagFilterIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _leadCustomerTagFilterIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _leadGroupFilterIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupTag> _leadTagIndex = new(StringComparer.OrdinalIgnoreCase);
    private string? _leadCompetitionFilter;
    private string? _leadTimeRangeFilter;
    private string? _leadMediaFilter;
    private CancellationTokenSource? _backgroundScrapeCts;
    private int _nextScrapeGroupOffset;

    partial void OnScrapeFilterKeywordsChanged(string value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnScrapePostsPerGroupChanged(int value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnScrapeAllEnabledTargetGroupsChanged(bool value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnScrapeDelayMinSecondsChanged(double value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnScrapeDelayMaxSecondsChanged(double value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnScrapeMaxGroupsPerCycleChanged(int value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }
    }

    partial void OnLeadCommentStatusFilterChanged(int value) =>
        this.FilterCachedPostsFromDb();

    partial void OnFilterOnlyPostsWithPhoneChanged(bool value) =>
        this.FilterCachedPostsFromDb();

    partial void OnAutoBackgroundScrapeEnabledChanged(bool value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
            this.SyncBackgroundScraperLoop();
        }
    }

    partial void OnAutoScrapeIntervalMinutesChanged(int value)
    {
        if (!this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
            this.SyncBackgroundScraperLoop();
        }
    }

    partial void OnFilterScrapedGroupIdChanged(string? value) =>
        _ = this.QueryLocalScrapedPostsAsync();

    /// <summary>
    /// Returns whether the specified <paramref name="postId"/> has already been commented on by any account in the local action history.
    /// </summary>
    public bool IsPostCommented(string? postId) =>
        !string.IsNullOrWhiteSpace(postId) && _commentedPostIds.Contains(postId);

    /// <summary>
    /// Extracts the primary normalized Vietnamese phone number from a scraped post's message text, or <see langword="null"/> if none is found.
    /// </summary>
    public string? ExtractPostPhoneNumber(PostSummary? post) =>
        post is null ? null : _context.LeadScoring.ExtractPrimaryPhoneNumber(post.MessageText);

    /// <summary>
    /// Extracts all normalized Vietnamese phone numbers from a scraped post's message text.
    /// </summary>
    public IReadOnlyList<string> ExtractAllPostPhoneNumbers(PostSummary? post) =>
        post is null ? [] : _context.LeadScoring.ExtractVietnamesePhoneNumbers(post.MessageText);

    /// <summary>
    /// Queries stored posts from the local SQLite database (<c>scraped_posts</c>) and applies the local search/filter without calling Facebook.
    /// </summary>
    public async Task QueryLocalScrapedPostsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ActionHistoryEntry> recentActions = await _context.AccountStore
            .GetRecentActionsAsync(500, ct)
            .ConfigureAwait(true);

        _commentedPostIds.Clear();
        for (int i = 0; i < recentActions.Count; i++)
        {
            ActionHistoryEntry entry = recentActions[i];
            if (string.Equals(entry.ActionKind, "COMMENT", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.TargetId))
            {
                _ = _commentedPostIds.Add(entry.TargetId);
            }
        }

        IReadOnlyList<GroupTag> storedLeadTags = await _context.AccountStore
            .GetAllLeadTagsAsync(ct)
            .ConfigureAwait(true);
        this.LeadTags.ReplaceAll(storedLeadTags);
        this.RebuildLeadTagIndex();

        IReadOnlyList<PostSummary> dbPosts = await _context.AccountStore
            .QueryScrapedPostsAsync(this.FilterScrapedGroupId, searchKeyword: null, limit: 300, ct: ct)
            .ConfigureAwait(true);

        _allScrapedPosts.Clear();
        _allScrapedPosts.AddRange(dbPosts);
        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Filters the cached SQLite <see cref="ScrapedPosts"/> locally by buyer-intent keywords without calling Facebook.
    /// </summary>
    public void ApplyLocalLeadFilter(string? keywordsText)
    {
        this.BuyerIntentKeywords = keywordsText ?? string.Empty;
        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the multi-select buyer intent and contact filters (<c>has_phone</c>, <c>no_phone</c>, <c>high_intent</c>, <c>inquiry</c>) and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadIntentFilters(IReadOnlyList<string> selectedIds)
    {
        _leadIntentFilters.Clear();
        if (selectedIds is not null)
        {
            for (int i = 0; i < selectedIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(selectedIds[i]))
                {
                    _ = _leadIntentFilters.Add(selectedIds[i]);
                }
            }
        }

        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the competition and engagement filter (<c>zero_comments</c>, <c>low_comments</c>, <c>hot_engagement</c>, or <see langword="null"/>) and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadCompetitionFilter(string? filterId)
    {
        _leadCompetitionFilter = string.IsNullOrWhiteSpace(filterId) ? null : filterId.Trim();
        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the post time range filter (<c>last_1h</c>, <c>last_24h</c>, <c>last_3d</c>, <c>last_7d</c>, or <see langword="null"/>) and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadTimeRangeFilter(string? filterId)
    {
        _leadTimeRangeFilter = string.IsNullOrWhiteSpace(filterId) ? null : filterId.Trim();
        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the post attachment/media filter (<c>has_media</c>, <c>has_photo</c>, <c>has_video</c>, <c>text_only</c>, or <see langword="null"/>) and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadMediaFilter(string? filterId)
    {
        _leadMediaFilter = string.IsNullOrWhiteSpace(filterId) ? null : filterId.Trim();
        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the multi-select group color tag filters (including <c>__untagged__</c>) for scraped leads and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadGroupTagFilters(IReadOnlyList<string> tagIds)
    {
        _leadGroupTagFilterIds.Clear();
        if (tagIds is not null)
        {
            for (int i = 0; i < tagIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(tagIds[i]))
                {
                    _ = _leadGroupTagFilterIds.Add(tagIds[i]);
                }
            }
        }

        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Updates the multi-select customer/lead color tag filters (including <c>__untagged__</c>) for scraped leads and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadCustomerTagFilters(IReadOnlyList<string> tagIds)
    {
        _leadCustomerTagFilterIds.Clear();
        if (tagIds is not null)
        {
            for (int i = 0; i < tagIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(tagIds[i]))
                {
                    _ = _leadCustomerTagFilterIds.Add(tagIds[i]);
                }
            }
        }

        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Reloads the user-defined and AI-created customer/lead tags from SQLite into <see cref="LeadTags"/>.
    /// </summary>
    public async Task ReloadLeadTagsOnlyAsync(CancellationToken ct = default)
    {
        IReadOnlyList<GroupTag> tags = await _context.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(true);
        this.LeadTags.ReplaceAll(tags);
        this.RebuildLeadTagIndex();
    }

    private void RebuildLeadTagIndex()
    {
        _leadTagIndex.Clear();
        for (int i = 0; i < this.LeadTags.Count; i++)
        {
            GroupTag tag = this.LeadTags[i];
            if (!string.IsNullOrWhiteSpace(tag.Id))
            {
                _leadTagIndex[tag.Id] = tag;
            }
        }
    }

    /// <summary>
    /// Finds a customer/lead tag by <paramref name="tagId"/> in O(1) time.
    /// </summary>
    public GroupTag? FindLeadTagById(string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return null;
        }

        if (_leadTagIndex.Count == this.LeadTags.Count &&
            _leadTagIndex.TryGetValue(tagId, out GroupTag? fastHit))
        {
            return fastHit;
        }

        this.RebuildLeadTagIndex();
        return _leadTagIndex.TryGetValue(tagId, out GroupTag? val) ? val : null;
    }

    /// <summary>
    /// Resolves the assigned <see cref="GroupTag"/> badges on a scraped <paramref name="post"/> against <see cref="LeadTags"/>.
    /// </summary>
    public IReadOnlyList<GroupTag> ResolveLeadTags(PostSummary? post)
    {
        if (post is null || string.IsNullOrWhiteSpace(post.TagsCsv))
        {
            return Array.Empty<GroupTag>();
        }

        IReadOnlyList<string> tagIds = post.GetTagIds();
        if (tagIds.Count == 0)
        {
            return Array.Empty<GroupTag>();
        }

        List<GroupTag> resolved = new(tagIds.Count);
        for (int i = 0; i < tagIds.Count; i++)
        {
            GroupTag? matched = this.FindLeadTagById(tagIds[i]);
            if (matched is not null)
            {
                resolved.Add(matched);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Toggles a customer/lead <paramref name="tagId"/> on <paramref name="post"/> in SQLite and updates <see cref="ScrapedPosts"/> in-place without resetting scroll position.
    /// </summary>
    public async Task ToggleLeadPostTagAsync(PostSummary post, string tagId, CancellationToken ct = default)
    {
        if (post is null || string.IsNullOrWhiteSpace(post.PostId) || string.IsNullOrWhiteSpace(tagId))
        {
            return;
        }

        PostSummary current = this.GetLatestCachedPost(post);
        string trimmedTagId = tagId.Trim();
        IReadOnlyList<string> existingIds = current.GetTagIds();
        bool removed = current.HasTag(trimmedTagId);
        (string? nextCsv, _) = _context.LeadScoring.ApplyLeadTagChange(existingIds, trimmedTagId, assign: !removed);
        string updatedTagsCsv = nextCsv ?? string.Empty;
        await _context.AccountStore
            .UpdateScrapedPostTagsAndNotesAsync(current.PostId, updatedTagsCsv, current.Notes, ct)
            .ConfigureAwait(true);

        PostSummary updatedPost = current with { TagsCsv = updatedTagsCsv };
        this.ReplaceScrapedPostInPlace(updatedPost);

        if (!removed)
        {
            string tagName = this.FindLeadTagById(trimmedTagId)?.Name ?? trimmedTagId;
            _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
                "lead_tag_preference",
                $"lead_tag_{trimmedTagId}",
                _context.Localization.Format(
                    "Ai.LearnLeadTagAssigned",
                    tagName,
                    current.AuthorName ?? _context.Localization["Leads.DefaultAuthor"],
                    current.GroupName ?? _context.Localization["Audit.Cat.Group"]),
                ct);
        }
    }

    /// <summary>
    /// Creates a new color-coded customer/lead tag in SQLite, reloads <see cref="LeadTags"/>, and returns the created tag.
    /// </summary>
    public async Task<GroupTag?> CreateQuickLeadTagAsync(string tagName, string colorHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        string trimmedName = tagName.Trim();
        string resolvedColor = string.IsNullOrWhiteSpace(colorHex) ? "#F6821F" : colorHex.Trim();
        GroupTag tag = _context.GroupTagService.CreateTag(trimmedName, resolvedColor);
        await _context.AccountStore.UpsertLeadTagAsync(tag, ct).ConfigureAwait(true);
        await this.ReloadLeadTagsOnlyAsync(ct).ConfigureAwait(true);

        _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
            "lead_preferences",
            $"lead_tag_def_{tag.Id}",
            _context.Localization.Format("Ai.LearnLeadTagCreated", tag.Name, tag.ColorHex),
            ct);

        return tag;
    }

    /// <summary>
    /// Updates CRM <paramref name="notes"/> on a scraped <paramref name="post"/> in SQLite and updates <see cref="ScrapedPosts"/> in-place.
    /// </summary>
    public async Task UpdateLeadPostNotesAsync(PostSummary post, string notes, CancellationToken ct = default)
    {
        if (post is null || string.IsNullOrWhiteSpace(post.PostId))
        {
            return;
        }

        PostSummary current = this.GetLatestCachedPost(post);
        string normalizedNotes = notes?.Trim() ?? string.Empty;
        if (string.Equals(current.Notes ?? string.Empty, normalizedNotes, StringComparison.Ordinal))
        {
            return;
        }

        await _context.AccountStore
            .UpdateScrapedPostTagsAndNotesAsync(current.PostId, current.TagsCsv, normalizedNotes, ct)
            .ConfigureAwait(true);

        PostSummary updatedPost = current with { Notes = normalizedNotes };
        this.ReplaceScrapedPostInPlace(updatedPost);
    }

    private PostSummary GetLatestCachedPost(PostSummary fallback)
    {
        for (int i = 0; i < _allScrapedPosts.Count; i++)
        {
            if (string.Equals(_allScrapedPosts[i].PostId, fallback.PostId, StringComparison.Ordinal))
            {
                return _allScrapedPosts[i];
            }
        }

        return fallback;
    }

    /// <summary>
    /// Selects <paramref name="post"/>, opens the AI Assistant drawer, attaches the <c>selected_lead</c> context pack,
    /// and requests AI buyer-intent analysis and consultation comment drafting.
    /// </summary>
    public void AskAiToAnalyzeAndConsultLead(PostSummary? post)
    {
        if (post is null)
        {
            return;
        }

        this.SelectedScrapedPost = post;
        this.IsAiAssistantOpen = true;
        _ = this.AiAssistant.RequestLeadAnalysisAndConsultationAsync(post);
    }

    private void ReplaceScrapedPostInPlace(PostSummary updatedPost)
    {
        for (int i = 0; i < _allScrapedPosts.Count; i++)
        {
            if (string.Equals(_allScrapedPosts[i].PostId, updatedPost.PostId, StringComparison.Ordinal))
            {
                _allScrapedPosts[i] = updatedPost;
                break;
            }
        }

        for (int i = 0; i < this.ScrapedPosts.Count; i++)
        {
            if (string.Equals(this.ScrapedPosts[i].PostId, updatedPost.PostId, StringComparison.Ordinal))
            {
                this.ScrapedPosts[i] = updatedPost;
                break;
            }
        }

        if (this.SelectedScrapedPost is not null &&
            string.Equals(this.SelectedScrapedPost.PostId, updatedPost.PostId, StringComparison.Ordinal))
        {
            this.SelectedScrapedPost = updatedPost;
        }
    }

    /// <summary>
    /// Updates the multi-select group ID filters for scraped leads and refreshes <see cref="ScrapedPosts"/>.
    /// </summary>
    public void SetLeadGroupFilters(IReadOnlyList<string> groupIds)
    {
        _leadGroupFilterIds.Clear();
        if (groupIds is not null)
        {
            for (int i = 0; i < groupIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(groupIds[i]))
                {
                    _ = _leadGroupFilterIds.Add(groupIds[i]);
                }
            }
        }

        this.FilterCachedPostsFromDb();
    }

    /// <summary>
    /// Returns all unique <c>(GroupId, GroupName)</c> pairs across both <see cref="TargetGroups"/> and stored <see cref="ScrapedPosts"/>
    /// so every scraped group is always selectable in the Sales Leads filter bar.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    public List<(string GroupId, string GroupName)> GetAvailableScrapedGroupsForFilter()
    {
        List<(string GroupId, string GroupName)> list = new(this.TargetGroups.Count + 8);
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (!string.IsNullOrWhiteSpace(tg.GroupId) && seen.Add(tg.GroupId))
            {
                string label = !string.IsNullOrWhiteSpace(tg.GroupName) ? tg.GroupName : tg.GroupId;
                list.Add((tg.GroupId, label));
            }
        }

        for (int i = 0; i < _allScrapedPosts.Count; i++)
        {
            PostSummary p = _allScrapedPosts[i];
            if (!string.IsNullOrWhiteSpace(p.GroupId) && seen.Add(p.GroupId))
            {
                string label = !string.IsNullOrWhiteSpace(p.GroupName) ? p.GroupName : p.GroupId;
                list.Add((p.GroupId, label));
            }
        }

        return list;
    }

    private void FilterCachedPostsFromDb()
    {
        string[] keywords = string.IsNullOrWhiteSpace(this.BuyerIntentKeywords)
            ? Array.Empty<string>()
            : this.BuyerIntentKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        List<PostSummary> matched = new(_allScrapedPosts.Count);
        for (int i = 0; i < _allScrapedPosts.Count; i++)
        {
            PostSummary post = _allScrapedPosts[i];
            if (!string.IsNullOrWhiteSpace(this.FilterScrapedGroupId) &&
                !string.Equals(post.GroupId, this.FilterScrapedGroupId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_leadGroupFilterIds.Count > 0 &&
                (string.IsNullOrWhiteSpace(post.GroupId) || !_leadGroupFilterIds.Contains(post.GroupId)))
            {
                continue;
            }

            if (_leadGroupTagFilterIds.Count > 0)
            {
                SavedTargetGroup? tg = this.FindTargetGroupById(post.GroupId);
                IReadOnlyList<string> tgTags = tg?.GetTagIds() ?? [];
                bool matchesUntagged = _leadGroupTagFilterIds.Contains("__untagged__") && tgTags.Count == 0;
                bool matchesTag = false;
                for (int t = 0; t < tgTags.Count; t++)
                {
                    if (_leadGroupTagFilterIds.Contains(tgTags[t]))
                    {
                        matchesTag = true;
                        break;
                    }
                }

                if (!matchesUntagged && !matchesTag)
                {
                    continue;
                }
            }

            if (_leadCustomerTagFilterIds.Count > 0)
            {
                IReadOnlyList<string> postTags = post.GetTagIds();
                bool matchesUntaggedLead = _leadCustomerTagFilterIds.Contains("__untagged__") && postTags.Count == 0;
                bool matchesLeadTag = false;
                for (int t = 0; t < postTags.Count; t++)
                {
                    if (_leadCustomerTagFilterIds.Contains(postTags[t]))
                    {
                        matchesLeadTag = true;
                        break;
                    }
                }

                if (!matchesUntaggedLead && !matchesLeadTag)
                {
                    continue;
                }
            }

            bool isCommented = this.IsPostCommented(post.PostId);
            if (this.LeadCommentStatusFilter == 1 && isCommented)
            {
                continue;
            }

            if (this.LeadCommentStatusFilter == 2 && !isCommented)
            {
                continue;
            }

            string? extractedPhone = null;
            bool phoneEvaluated = false;

            if (this.FilterOnlyPostsWithPhone ||
                this.LeadCommentStatusFilter == 3 ||
                _leadIntentFilters.Contains("has_phone"))
            {
                extractedPhone = this.ExtractPostPhoneNumber(post);
                phoneEvaluated = true;
                if (string.IsNullOrWhiteSpace(extractedPhone))
                {
                    continue;
                }
            }

            if (_leadIntentFilters.Contains("no_phone"))
            {
                if (!phoneEvaluated)
                {
                    extractedPhone = this.ExtractPostPhoneNumber(post);
                }

                if (!string.IsNullOrWhiteSpace(extractedPhone))
                {
                    continue;
                }
            }

            if (_leadIntentFilters.Contains("high_intent") &&
                _context.LeadScoring.CalculateBuyerIntentScore(post.MessageText, this.BuyerIntentKeywords) < 75)
            {
                continue;
            }

            if (_leadIntentFilters.Contains("inquiry") &&
                !_context.LeadScoring.HasBuyerInquirySignal(post.MessageText))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(_leadCompetitionFilter))
            {
                if (string.Equals(_leadCompetitionFilter, "zero_comments", StringComparison.OrdinalIgnoreCase) &&
                    post.CommentCount != 0)
                {
                    continue;
                }

                if (string.Equals(_leadCompetitionFilter, "low_comments", StringComparison.OrdinalIgnoreCase) &&
                    post.CommentCount > 2)
                {
                    continue;
                }

                if (string.Equals(_leadCompetitionFilter, "hot_engagement", StringComparison.OrdinalIgnoreCase) &&
                    post.CommentCount < 5 &&
                    post.ReactionCount < 5)
                {
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(_leadTimeRangeFilter))
            {
                if (!post.CreatedAt.HasValue)
                {
                    continue;
                }

                TimeSpan age = nowUtc - post.CreatedAt.Value;
                if (string.Equals(_leadTimeRangeFilter, "last_1h", StringComparison.OrdinalIgnoreCase) &&
                    age > TimeSpan.FromHours(1))
                {
                    continue;
                }

                if (string.Equals(_leadTimeRangeFilter, "last_24h", StringComparison.OrdinalIgnoreCase) &&
                    age > TimeSpan.FromHours(24))
                {
                    continue;
                }

                if (string.Equals(_leadTimeRangeFilter, "last_3d", StringComparison.OrdinalIgnoreCase) &&
                    age > TimeSpan.FromDays(3))
                {
                    continue;
                }

                if (string.Equals(_leadTimeRangeFilter, "last_7d", StringComparison.OrdinalIgnoreCase) &&
                    age > TimeSpan.FromDays(7))
                {
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(_leadMediaFilter))
            {
                bool hasMediaPreview = !string.IsNullOrWhiteSpace(post.MediaPreviewUrl);
                if (string.Equals(_leadMediaFilter, "has_media", StringComparison.OrdinalIgnoreCase) &&
                    !hasMediaPreview &&
                    !post.IsVideo)
                {
                    continue;
                }

                if (string.Equals(_leadMediaFilter, "has_photo", StringComparison.OrdinalIgnoreCase) &&
                    (!hasMediaPreview || post.IsVideo))
                {
                    continue;
                }

                if (string.Equals(_leadMediaFilter, "has_video", StringComparison.OrdinalIgnoreCase) &&
                    !post.IsVideo)
                {
                    continue;
                }

                if (string.Equals(_leadMediaFilter, "text_only", StringComparison.OrdinalIgnoreCase) &&
                    (hasMediaPreview || post.IsVideo))
                {
                    continue;
                }
            }

            if (_context.LeadScoring.MatchesPostOrAuthorKeywords(post, keywords))
            {
                matched.Add(post);
            }
        }

        if (keywords.Length > 0 && matched.Count > 1)
        {
            (PostSummary Post, int Score)[] scored = new (PostSummary Post, int Score)[matched.Count];
            for (int i = 0; i < matched.Count; i++)
            {
                PostSummary p = matched[i];
                scored[i] = (p, _context.LeadScoring.CalculateBuyerIntentScore(p.MessageText, this.BuyerIntentKeywords));
            }

            Array.Sort(scored, static (a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < scored.Length; i++)
            {
                matched[i] = scored[i].Post;
            }
        }

        this.ScrapedPosts.ReplaceAll(matched);
        this.SelectedScrapedPost = this.ScrapedPosts.Count > 0 ? this.ScrapedPosts[0] : null;
    }

    private void SyncBackgroundScraperLoop()
    {
        this.StopBackgroundScraperLoop();
        if (!this.AutoBackgroundScrapeEnabled)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _backgroundScrapeCts = cts;
        int intervalMin = Math.Clamp(this.AutoScrapeIntervalMinutes <= 0 ? 10 : this.AutoScrapeIntervalMinutes, 1, 120);
        _ = this.RunBackgroundScraperLoopAsync(TimeSpan.FromMinutes(intervalMin), cts.Token);
    }

    private void StopBackgroundScraperLoop()
    {
        _backgroundScrapeCts?.Cancel();
        _backgroundScrapeCts?.Dispose();
        _backgroundScrapeCts = null;
    }

    private async Task RunBackgroundScraperLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            using PeriodicTimer timer = new(interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(true))
            {
                if (!this.IsBusy && this.SelectedAccount is not null)
                {
                    await this.ScanGroupLeadsAsync().ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Background scraper stopped cleanly
        }
    }

    private async Task ClearScrapedPostsDbAsync()
    {
        int removed = await _context.AccountStore
            .ClearScrapedPostsAsync(this.FilterScrapedGroupId)
            .ConfigureAwait(true);

        await this.QueryLocalScrapedPostsAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            _context.Localization["Audit.Cat.Scrape"],
            _context.Localization.Format("Log.ScrapedPostsCleared", removed)).ConfigureAwait(true);
    }

    private async Task ScanGroupLeadsAsync()
    {
        if (this.SelectedTargetGroup is null && this.TargetGroups.Count > 0)
        {
            this.SelectedTargetGroup = this.TargetGroups[0];
        }

        List<(string GroupId, string GroupName, string? GroupAvatarUrl)> targets = new();
        if (this.ScrapeAllEnabledTargetGroups && this.TargetGroups.Count > 0)
        {
            for (int i = 0; i < this.TargetGroups.Count; i++)
            {
                SavedTargetGroup tg = this.TargetGroups[i];
                if (tg.IsEnabled && !string.IsNullOrWhiteSpace(tg.GroupId))
                {
                    targets.Add((tg.GroupId, tg.GroupName, tg.AvatarUrl));
                }
            }
        }

        if (targets.Count == 0)
        {
            string? singleId = this.SelectedTargetGroup?.GroupId ?? this.SelectedGroup?.GroupId;
            string singleName = this.SelectedTargetGroup?.GroupName ?? this.SelectedGroup?.Name ?? _context.Localization["Audit.Cat.Group"];
            string? singleAvatar = this.SelectedTargetGroup?.AvatarUrl ?? this.SelectedGroup?.AvatarUrl;
            if (!string.IsNullOrWhiteSpace(singleId))
            {
                targets.Add((singleId, singleName, singleAvatar));
            }
        }

        if (targets.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Scrape"],
                _context.Localization["Log.NoTargetGroupsForScrape"]).ConfigureAwait(true);
            return;
        }

        // Apply Round-Robin batch limit (ScrapeMaxGroupsPerCycle) to prevent anti-ban rate-limit spikes when many groups are enabled
        int maxPerCycle = Math.Clamp(this.ScrapeMaxGroupsPerCycle <= 0 ? 30 : this.ScrapeMaxGroupsPerCycle, 1, 500);
        if (targets.Count > maxPerCycle)
        {
            List<(string GroupId, string GroupName, string? GroupAvatarUrl)> batch = new(maxPerCycle);
            for (int i = 0; i < maxPerCycle; i++)
            {
                int idx = (_nextScrapeGroupOffset + i) % targets.Count;
                batch.Add(targets[idx]);
            }

            _nextScrapeGroupOffset = (_nextScrapeGroupOffset + maxPerCycle) % targets.Count;
            targets = batch;
        }
        else
        {
            _nextScrapeGroupOffset = 0;
        }

        string firstGroupName = targets[0].GroupName;
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            string[] selectiveKeywords = string.IsNullOrWhiteSpace(this.ScrapeFilterKeywords)
                ? Array.Empty<string>()
                : this.ScrapeFilterKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int perGroupLimit = Math.Clamp(this.ScrapePostsPerGroup <= 0 ? 15 : this.ScrapePostsPerGroup, 5, 100);
            List<PostSummary> collectedForDb = new();

            if (targets.Count > 1 && session.CurrentMaterial is null)
            {
                _ = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);
            }

            int parallelChunkSize = this.ScrapeDelayMinSeconds <= 1.0 ? 3 : 1;
            for (int chunkStart = 0; chunkStart < targets.Count; chunkStart += parallelChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                int chunkCount = Math.Min(parallelChunkSize, targets.Count - chunkStart);

                if (chunkStart > 0)
                {
                    await this.WaitScraperRateLimitDelayAsync(targets[chunkStart].GroupName, ct).ConfigureAwait(true);
                }

                for (int offset = 0; offset < chunkCount; offset++)
                {
                    int gIdx = chunkStart + offset;
                    string groupName = targets[gIdx].GroupName;
                    _ = await this.LogAuditAsync(
                        AuditCategory.Scrape,
                        AuditSeverity.Info,
                        groupName,
                        _context.Localization.Format("Log.ScrapingGroupProgress", gIdx + 1, targets.Count, groupName)).ConfigureAwait(true);
                }

                PagedResult<PostSummary>[] feeds;
                if (chunkCount == 1)
                {
                    (string groupId, _, _) = targets[chunkStart];
                    PagedResult<PostSummary> singleFeed = await _context.CometClient
                        .GetGroupFeedAsync(session, groupId, count: perGroupLimit, ct: ct)
                        .ConfigureAwait(true);
                    feeds = [singleFeed];
                }
                else
                {
                    Task<PagedResult<PostSummary>>[] feedTasks = new Task<PagedResult<PostSummary>>[chunkCount];
                    for (int offset = 0; offset < chunkCount; offset++)
                    {
                        string groupId = targets[chunkStart + offset].GroupId;
                        feedTasks[offset] = _context.CometClient.GetGroupFeedAsync(session, groupId, count: perGroupLimit, ct: ct);
                    }

                    feeds = await Task.WhenAll(feedTasks).ConfigureAwait(true);
                }

                List<PostSummary> chunkCollected = new();
                for (int offset = 0; offset < chunkCount; offset++)
                {
                    (string groupId, string groupName, string? groupAvatarUrl) = targets[chunkStart + offset];
                    PagedResult<PostSummary> feed = feeds[offset];
                    for (int i = 0; i < feed.Items.Count; i++)
                    {
                        PostSummary post = feed.Items[i] with
                        {
                            GroupId = groupId,
                            GroupName = groupName,
                            GroupAvatarUrl = feed.Items[i].GroupAvatarUrl ?? groupAvatarUrl
                        };

                        if (_context.LeadScoring.MatchesBuyerKeywords(post.MessageText, selectiveKeywords))
                        {
                            chunkCollected.Add(post);
                            collectedForDb.Add(post);
                        }
                    }
                }

                if (chunkCollected.Count > 0)
                {
                    await _context.AccountStore
                        .UpsertScrapedPostsAsync(chunkCollected, ct)
                        .ConfigureAwait(true);

                    for (int i = chunkCollected.Count - 1; i >= 0; i--)
                    {
                        PostSummary incoming = chunkCollected[i];
                        int existingIdx = _allScrapedPosts.FindIndex(p => string.Equals(p.PostId, incoming.PostId, StringComparison.Ordinal));
                        if (existingIdx >= 0)
                        {
                            _allScrapedPosts[existingIdx] = incoming;
                        }
                        else
                        {
                            _allScrapedPosts.Insert(0, incoming);
                        }
                    }

                    this.FilterCachedPostsFromDb();
                }
            }

            await this.QueryLocalScrapedPostsAsync(ct).ConfigureAwait(true);

            // Backwards-compatible fallback when running in-memory tests where QueryScrapedPostsAsync isn't seeded by a mock store
            if (this.ScrapedPosts.Count == 0 && collectedForDb.Count > 0 && string.IsNullOrWhiteSpace(this.BuyerIntentKeywords))
            {
                _allScrapedPosts.Clear();
                _allScrapedPosts.AddRange(collectedForDb);
                this.FilterCachedPostsFromDb();
            }

            string summaryTarget = targets.Count == 1
                ? firstGroupName
                : _context.Localization.Format("Log.TargetGroupsSummaryCount", targets.Count);

            string logMessage = selectiveKeywords.Length > 0
                ? _context.Localization.Format("Log.ScrapedSelectiveSuccess", collectedForDb.Count, this.ScrapeFilterKeywords, summaryTarget, this.ScrapedPosts.Count)
                : _context.Localization.Format("Log.ScrapedLatestSuccess", collectedForDb.Count, summaryTarget, this.ScrapedPosts.Count);

            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Success,
                summaryTarget,
                logMessage).ConfigureAwait(true);

            _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
                "lead_preferences",
                "buyer_intent_keywords",
                _context.Localization.Format(
                    "Ai.LearnLeadScanHabit",
                    string.IsNullOrWhiteSpace(this.BuyerIntentKeywords) ? this.ScrapeFilterKeywords : this.BuyerIntentKeywords,
                    collectedForDb.Count,
                    targets.Count),
                ct);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Warning,
                firstGroupName,
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Error,
                firstGroupName,
                _context.Localization.Format("Log.ScrapeFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SendSalesCommentOnSelectedPostAsync()
    {
        if (this.SelectedScrapedPost is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.SalesComment"],
                _context.Localization["Log.SelectScrapedPostFirst"]).ConfigureAwait(true);
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            await this.ExecuteSingleSalesCommentAsync(session, this.SelectedScrapedPost, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.SalesComment"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                this.SelectedScrapedPost.AuthorName ?? _context.Localization["Leads.DefaultAuthor"],
                _context.Localization.Format("Log.SalesCommentFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task AutoCommentAllMatchedPostsAsync()
    {
        if (this.ScrapedPosts.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.SalesComment"],
                _context.Localization["Log.ScrapedListEmpty"]).ConfigureAwait(true);
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            List<PostSummary> postsToComment = [.. this.ScrapedPosts];
            string? batchAttachmentFbid = null;
            if (!string.IsNullOrWhiteSpace(this.SalesCommentPhotoPath) && File.Exists(this.SalesCommentPhotoPath.Trim()))
            {
                batchAttachmentFbid = await _context.CometClient
                    .UploadUfiPhotoAsync(session, this.SalesCommentPhotoPath.Trim(), ct)
                    .ConfigureAwait(true);
            }

            try
            {
                for (int i = 0; i < postsToComment.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (i > 0)
                    {
                        await this.WaitAntiCheckpointDelayAsync(postsToComment[i].AuthorName ?? _context.Localization["Leads.DefaultAuthor"], ct).ConfigureAwait(true);
                    }

                    await this.ExecuteSingleSalesCommentAsync(
                        session,
                        postsToComment[i],
                        ct,
                        refreshListAfterComment: false,
                        preUploadedAttachmentFbid: batchAttachmentFbid).ConfigureAwait(true);
                }
            }
            finally
            {
                this.FilterCachedPostsFromDb();
            }
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.SalesComment"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await this.HandleClassifiedCampaignExceptionAsync(AuditCategory.SalesComment, _context.Localization["Audit.Cat.SalesComment"], ex, ct).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task HandleClassifiedCampaignExceptionAsync(
        AuditCategory category,
        string targetName,
        Exception ex,
        CancellationToken ct)
    {
        AutomationErrorCategory errorCategory = ProtocolErrorClassifier.ClassifyException(ex);
        if (errorCategory == AutomationErrorCategory.CheckpointOrAuthFailure && this.SelectedAccount is not null)
        {
            AccountProfile updated = this.SelectedAccount with { Status = AccountStatus.Checkpoint };
            await _context.AccountStore
                .UpsertAccountAsync(updated, CancellationToken.None)
                .ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                category,
                AuditSeverity.Error,
                targetName,
                _context.Localization["Log.CircuitBreakerAuthFailure"]).ConfigureAwait(true);
            return;
        }

        if (errorCategory == AutomationErrorCategory.ActionBlocked368)
        {
            _ = await this.LogAuditAsync(
                category,
                AuditSeverity.Error,
                targetName,
                _context.Localization["Log.CircuitBreakerRateLimit"]).ConfigureAwait(true);
            return;
        }

        _ = await this.LogAuditAsync(
            category,
            AuditSeverity.Error,
            targetName,
            _context.Localization.Format("Log.TaskError", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        _ = ct;
    }

    private async Task OpenSelectedPostInBrowserAsync()
    {
        if (this.SelectedScrapedPost is null || string.IsNullOrWhiteSpace(this.SelectedScrapedPost.PermalinkUrl))
        {
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(forceVisible: true).ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        await session.NavigateAsync(this.SelectedScrapedPost.PermalinkUrl).ConfigureAwait(true);
        _ = await this.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Info,
            this.SelectedScrapedPost.AuthorName ?? _context.Localization["Leads.DefaultAuthor"],
            _context.Localization["Log.PostOpenedInBrowser"]).ConfigureAwait(true);
    }

    private async Task WaitAntiCheckpointDelayAsync(string nextTargetName, CancellationToken ct)
    {
        TimeSpan delay = HumanJitter.NextGaussianDelay(this.MinDelaySeconds, this.MaxDelaySeconds);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        double totalSec = Math.Round(delay.TotalSeconds, 1);
        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Info,
            nextTargetName,
            _context.Localization.Format("Log.WaitingSafeDelay", totalSec, nextTargetName)).ConfigureAwait(true);

        await Task.Delay(delay, ct).ConfigureAwait(true);
    }

    private async Task WaitScraperRateLimitDelayAsync(string nextGroupName, CancellationToken ct)
    {
        double minSec = Math.Clamp(this.ScrapeDelayMinSeconds, 0.0, 600.0);
        double maxSec = Math.Clamp(this.ScrapeDelayMaxSeconds, minSec, 600.0);
        TimeSpan delay = HumanJitter.NextGaussianDelay(minSec, maxSec);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        double totalSec = Math.Round(delay.TotalSeconds, 1);
        _ = await this.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Info,
            nextGroupName,
            _context.Localization.Format("Log.WaitingScraperDelay", totalSec, nextGroupName)).ConfigureAwait(true);

        await Task.Delay(delay, ct).ConfigureAwait(true);
    }

    private async Task ExecuteSingleSalesCommentAsync(
        IBrowserSession session,
        PostSummary post,
        CancellationToken ct,
        bool refreshListAfterComment = true,
        string? preUploadedAttachmentFbid = null)
    {
        string targetLabel = !string.IsNullOrWhiteSpace(post.AuthorName)
            ? _context.Localization.Format("Log.PostAuthorLabel", post.AuthorName)
            : _context.Localization["Leads.DefaultAuthor"];

        bool alreadyCommented = _commentedPostIds.Contains(post.PostId) ||
            await _context.AccountStore
                .HasExecutedActionAsync(session.AccountUid, "COMMENT", post.PostId, ct)
                .ConfigureAwait(true);

        if (alreadyCommented)
        {
            _ = _commentedPostIds.Add(post.PostId);
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                targetLabel,
                _context.Localization.Format("Log.SkippedDuplicateComment", targetLabel)).ConfigureAwait(true);
            return;
        }

        if (this.AutoReactBeforeComment)
        {
            ReactionKind reaction = Random.Shared.NextDouble() < 0.75 ? ReactionKind.Like : ReactionKind.Love;
            _ = await _context.CometClient
                .ReactToPostAsync(session, post.PostId, reaction, ct)
                .ConfigureAwait(true);
        }

        string? attachmentFbid = preUploadedAttachmentFbid;
        if (string.IsNullOrWhiteSpace(attachmentFbid) &&
            !string.IsNullOrWhiteSpace(this.SalesCommentPhotoPath) &&
            File.Exists(this.SalesCommentPhotoPath.Trim()))
        {
            attachmentFbid = await _context.CometClient
                .UploadUfiPhotoAsync(session, this.SalesCommentPhotoPath.Trim(), ct)
                .ConfigureAwait(true);
        }

        string resolvedCommentSpintax = ResolveDynamicCommentVariables(this.SalesCommentSpintaxText, post.AuthorName, post.GroupName);
        string? commentId = await _context.CometClient
            .CreateCommentAsync(session, post.PostId, resolvedCommentSpintax, attachmentFbid, ct: ct)
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(commentId))
        {
            _ = _commentedPostIds.Add(post.PostId);
            await _context.AccountStore.RecordActionAsync(
                new ActionHistoryEntry(
                    session.AccountUid,
                    "COMMENT",
                    post.PostId,
                    commentId,
                    DateTimeOffset.UtcNow),
                ct).ConfigureAwait(true);
            if (refreshListAfterComment)
            {
                this.FilterCachedPostsFromDb();
            }

            _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
                "brand_voice",
                "sales_comment_style",
                _context.Localization.Format("Ai.LearnSalesCommentStyle", TruncateForAiLearning(this.SalesCommentSpintaxText, 200)),
                ct);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            targetLabel,
            _context.Localization.Format("Log.SalesCommentSent", targetLabel),
            post.PermalinkUrl).ConfigureAwait(true);
    }

    /// <summary>
    /// Resolves dynamic personalization variables (<c>{author_name}</c>, <c>{group_name}</c>, <c>{date}</c>, <c>{time}</c>, <c>{random_id}</c>) in a sales comment or post template.
    /// </summary>
    public static string ResolveDynamicCommentVariables(string? rawTemplate, string? authorName, string? groupName) =>
        CampaignPublisherDomainService.Instance.ResolveDynamicVariables(rawTemplate, authorName, groupName);

    /// <summary>
    /// Computes a buyer-intent score (0..100) for a scraped group post based on matched keywords and question/inquiry signals.
    /// Delegates to <see cref="LeadScoringService"/>.
    /// </summary>
    public static int CalculateBuyerIntentScore(string? messageText, string? rawKeywords) =>
        LeadScoringService.Instance.CalculateBuyerIntentScore(messageText, rawKeywords);

    /// <summary>
    /// Formats a localized buyer-intent badge label for a scraped post card.
    /// </summary>
    public string FormatBuyerIntentBadge(PostSummary post)
    {
        ArgumentNullException.ThrowIfNull(post);
        int score = _context.LeadScoring.CalculateBuyerIntentScore(post.MessageText, this.BuyerIntentKeywords);
        string prefix = score >= 75
            ? _context.Localization["Leads.IntentHigh"]
            : _context.Localization["Leads.IntentWarm"];
        return $"{prefix} • {score}%";
    }
}
