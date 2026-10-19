// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Facebook.Net.Contracts;

/// <summary>
/// High-level client for executing Comet GraphQL operations, contextual navigation, and media uploads.
/// </summary>
public interface ICometClient
{
    /// <summary>
    /// Executes a raw or custom <see cref="GraphQlRequest"/> with contextual pre/post navigation and response sanitization.
    /// </summary>
    /// <param name="session">Active browser session for the account.</param>
    /// <param name="request">GraphQL request descriptor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sanitized <see cref="GraphQlResult"/>.</returns>
    Task<GraphQlResult> ExecuteGraphQlAsync(IBrowserSession session, GraphQlRequest request, CancellationToken ct = default);

    /// <summary>
    /// Fetches "People You May Know" (PYMK) friend suggestions.
    /// </summary>
    Task<IReadOnlyList<UserSummary>> GetFriendSuggestionsAsync(IBrowserSession session, int count = 30, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Sends a friend request to the specified target UID.
    /// </summary>
    Task<FriendshipState> SendFriendRequestAsync(IBrowserSession session, string targetUid, CancellationToken ct = default);

    /// <summary>
    /// Confirms an incoming friend request from the specified requester UID.
    /// </summary>
    Task<FriendshipState> ConfirmFriendRequestAsync(IBrowserSession session, string requesterUid, CancellationToken ct = default);

    /// <summary>
    /// Rejects an incoming friend request or cancels an outgoing request.
    /// </summary>
    Task<FriendshipState> DeleteFriendRequestAsync(IBrowserSession session, string targetUid, bool isIncoming, CancellationToken ct = default);

    /// <summary>
    /// Unfriends an existing friend.
    /// </summary>
    Task<FriendshipState> UnfriendAsync(IBrowserSession session, string targetUid, CancellationToken ct = default);

    /// <summary>
    /// Reacts to a feed or group post using the specified <see cref="ReactionKind"/>.
    /// </summary>
    Task<bool> ReactToPostAsync(IBrowserSession session, string postId, ReactionKind reaction, CancellationToken ct = default);

    /// <summary>
    /// Creates a comment on a post, optionally attaching an uploaded photo ID.
    /// </summary>
    Task<string?> CreateCommentAsync(IBrowserSession session, string postId, string messageText, string? attachmentFbid = null, CancellationToken ct = default);

    /// <summary>
    /// Publishes a new post to the user's timeline or a target group.
    /// </summary>
    Task<string?> PublishPostAsync(
        IBrowserSession session,
        string messageText,
        ComposerTarget target = ComposerTarget.Feed,
        string? targetGroupId = null,
        IReadOnlyList<string>? photoIds = null,
        IReadOnlyList<string>? videoIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Uploads a photo for use in feed posts or comments and returns its <c>fbid</c>.
    /// </summary>
    Task<string> UploadUfiPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default);

    /// <summary>
    /// Uploads a video (<c>.mp4</c>) using the 4-step RUpload protocol and returns its <c>video_id</c>.
    /// </summary>
    Task<string> UploadVideoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default);
}

/// <summary>
/// Persistence store for managing <see cref="AccountProfile"/> and <see cref="ProxyConfig"/> records in SQLite.
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
}
