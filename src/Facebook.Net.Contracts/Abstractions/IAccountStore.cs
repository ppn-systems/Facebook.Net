// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.Contracts.Abstractions;

/// <summary>
/// Persistence store for managing <see cref="AccountProfile"/>, <see cref="ProxyConfig"/>,
/// <see cref="SavedTargetGroup"/>, <see cref="CampaignTemplate"/>, and <see cref="ActionHistoryEntry"/> records in SQLite.
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// Initializes database schema tables if they do not exist.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all stored account profiles.
    /// </summary>
    Task<IReadOnlyList<AccountProfile>> GetAllAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific account profile by UID.
    /// </summary>
    Task<AccountProfile?> GetAccountByUidAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an account profile.
    /// </summary>
    Task UpsertAccountAsync(AccountProfile account, CancellationToken ct = default);

    /// <summary>
    /// Deletes an account profile by UID.
    /// </summary>
    Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all configured proxies.
    /// </summary>
    Task<IReadOnlyList<ProxyConfig>> GetAllProxiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a proxy configuration.
    /// </summary>
    Task UpsertProxyAsync(ProxyConfig proxy, CancellationToken ct = default);

    /// <summary>
    /// Deletes a proxy configuration by ID.
    /// </summary>
    Task<bool> DeleteProxyAsync(string proxyId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all saved target groups for automated posting and scraping.
    /// </summary>
    Task<IReadOnlyList<SavedTargetGroup>> GetAllTargetGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a saved target group.
    /// </summary>
    Task UpsertTargetGroupAsync(SavedTargetGroup group, CancellationToken ct = default);

    /// <summary>
    /// Deletes a saved target group by ID.
    /// </summary>
    Task<bool> DeleteTargetGroupAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all saved Spintax campaign templates.
    /// </summary>
    Task<IReadOnlyList<CampaignTemplate>> GetAllCampaignTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a Spintax campaign template.
    /// </summary>
    Task UpsertCampaignTemplateAsync(CampaignTemplate campaignTemplate, CancellationToken ct = default);

    /// <summary>
    /// Deletes a campaign template by ID.
    /// </summary>
    Task<bool> DeleteCampaignTemplateAsync(string templateId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the specified account has already executed an action (e.g., <c>"COMMENT"</c> or <c>"POST"</c>)
    /// on the given target ID (Post ID or Group ID) to prevent duplicate seeding.
    /// </summary>
    Task<bool> HasExecutedActionAsync(string accountUid, string actionKind, string targetId, CancellationToken ct = default);

    /// <summary>
    /// Records a completed post or sales comment action in the SQLite history log.
    /// </summary>
    Task RecordActionAsync(ActionHistoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent action history entries.
    /// </summary>
    Task<IReadOnlyList<ActionHistoryEntry>> GetRecentActionsAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a batch of scraped group posts in the local SQLite database.
    /// </summary>
    Task UpsertScrapedPostsAsync(IReadOnlyList<PostSummary> posts, CancellationToken ct = default);

    /// <summary>
    /// Queries scraped group posts from the local SQLite database with optional group and keyword filters.
    /// Does not perform any network calls to Facebook.
    /// </summary>
    Task<IReadOnlyList<PostSummary>> QueryScrapedPostsAsync(
        string? groupId = null,
        string? searchKeyword = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes scraped posts from the local SQLite database (all posts or only posts for a specific group).
    /// </summary>
    Task<int> ClearScrapedPostsAsync(string? groupId = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all user-defined color-coded group tags.
    /// </summary>
    Task<IReadOnlyList<GroupTag>> GetAllGroupTagsAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a user-defined group tag.
    /// </summary>
    Task UpsertGroupTagAsync(GroupTag tag, CancellationToken ct = default);

    /// <summary>
    /// Deletes a user-defined group tag by ID.
    /// </summary>
    Task<bool> DeleteGroupTagAsync(string tagId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves stored published posts and synchronized personal profile posts.
    /// </summary>
    Task<IReadOnlyList<PublishedPostRecord>> GetPublishedPostsAsync(string? accountUid = null, int limit = 200, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a published post or synchronized personal profile post record.
    /// </summary>
    Task UpsertPublishedPostAsync(PublishedPostRecord post, CancellationToken ct = default);

    /// <summary>
    /// Deletes a published post record by ID.
    /// </summary>
    Task<bool> DeletePublishedPostAsync(string postId, CancellationToken ct = default);
}
