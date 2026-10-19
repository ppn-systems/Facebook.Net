// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Facebook.Net.Contracts;

/// <summary>
/// Represents a paginated result set with a cursor for fetching subsequent pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? EndCursor = null,
    bool HasNextPage = false);

/// <summary>
/// High-level client for executing Comet GraphQL operations, group/feed scraping,
/// post publishing, sales commenting, contextual navigation, and media uploads.
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

    // -------------------------------------------------------------------------
    // 1. Posts, Sales Commenting, Reactions & Sharing (Feed & Group Publishing)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Publishes a new post to the user's personal timeline (<see cref="ComposerTarget.Feed"/>)
    /// or into a target group (<see cref="ComposerTarget.Group"/>), supporting Spintax text and multiple photo/video attachments.
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
    /// Shares an existing post onto the user's personal timeline or into a target community group.
    /// Automatically resolves link attachment preview (<c>share_scrape_data</c>) before publishing.
    /// </summary>
    Task<string?> SharePostAsync(
        IBrowserSession session,
        string postId,
        string messageText,
        string? targetGroupId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Posts a sales or seeding comment on a feed or group post, optionally attaching an uploaded product photo (<c>fbid</c>).
    /// Supports Spintax syntax and performs pre/post contextual navigation.
    /// </summary>
    Task<string?> CreateCommentAsync(
        IBrowserSession session,
        string postId,
        string messageText,
        string? attachmentFbid = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reacts to a feed or group post using the specified <see cref="ReactionKind"/>.
    /// </summary>
    Task<bool> ReactToPostAsync(IBrowserSession session, string postId, ReactionKind reaction, CancellationToken ct = default);

    /// <summary>
    /// Scrapes posts from the user's home Newsfeed (<c>CometNewsFeedPaginationQuery</c>).
    /// </summary>
    Task<PagedResult<PostSummary>> GetNewsFeedAsync(IBrowserSession session, int count = 5, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the full text content of a single post (<c>CometSinglePostDialogContentQuery</c>).
    /// </summary>
    Task<PostSummary?> GetSinglePostAsync(IBrowserSession session, string postId, CancellationToken ct = default);

    /// <summary>
    /// Scrapes users who reacted to a specific post (<c>CometUFIReactionsDialogTabContentRefetchQuery</c>).
    /// </summary>
    Task<PagedResult<UserSummary>> GetPostReactorsAsync(IBrowserSession session, string postId, int count = 10, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Scrapes users who commented on a specific post (<c>CommentsListComponentsPaginationQuery</c>).
    /// </summary>
    Task<PagedResult<UserSummary>> GetPostCommentersAsync(IBrowserSession session, string postId, string? cursor = null, CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // 2. Community Groups (Scraping, Joining, Questionnaire, Publishing)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Searches community groups by keyword (<c>SearchCometResultsPaginatedResultsQuery</c>).
    /// </summary>
    Task<PagedResult<GroupSummary>> SearchGroupsAsync(IBrowserSession session, string keyword, int count = 5, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all groups that the account has joined (<c>GroupsCometAllJoinedGroupsSectionPaginationQuery</c>).
    /// </summary>
    Task<PagedResult<GroupSummary>> GetJoinedGroupsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Scrapes regular feed stories/posts inside a specific community group (<c>GroupsCometFeedRegularStoriesPaginationQuery</c>).
    /// </summary>
    Task<PagedResult<PostSummary>> GetGroupFeedAsync(IBrowserSession session, string groupId, int count = 3, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Scrapes newly joined members of a specific group (<c>GroupsCometMembersPageNewMembersSectionRefetchQuery</c>).
    /// </summary>
    Task<PagedResult<UserSummary>> GetGroupNewMembersAsync(IBrowserSession session, string groupId, int count = 10, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Requests to join a community group (<c>GroupCometJoinForumMutation</c>).
    /// </summary>
    Task<GroupMembershipState> JoinGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves pending membership approval questions for a group (<c>GroupsCometMembershipQuestionsDialogQuery</c>).
    /// </summary>
    Task<IReadOnlyList<GroupQuestion>> GetGroupQuestionsAsync(IBrowserSession session, string groupId, CancellationToken ct = default);

    /// <summary>
    /// Submits answers to a group's membership questionnaire (<c>useGroupMembershipAnswersSaveMutation</c>).
    /// </summary>
    Task<GroupMembershipState> SubmitGroupAnswersAsync(IBrowserSession session, string groupId, IReadOnlyList<GroupQuestionAnswer> answers, CancellationToken ct = default);

    /// <summary>
    /// Leaves a community group (<c>GroupCometLeaveForumMutation</c>).
    /// </summary>
    Task<bool> LeaveGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default);

    /// <summary>
    /// Invites friends to a community group (<c>useGroupAddMembersMutation</c>).
    /// </summary>
    Task<bool> InviteFriendsToGroupAsync(IBrowserSession session, string groupId, IReadOnlyList<string> friendUids, CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // 3. Friending & Social Graph
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches "People You May Know" (PYMK) friend suggestions.
    /// </summary>
    Task<PagedResult<UserSummary>> GetFriendSuggestionsAsync(IBrowserSession session, int count = 30, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches incoming friend requests received by the viewer.
    /// </summary>
    Task<PagedResult<UserSummary>> GetIncomingFriendRequestsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches outgoing friend requests sent by the viewer.
    /// </summary>
    Task<PagedResult<UserSummary>> GetOutgoingFriendRequestsAsync(IBrowserSession session, int count = 10, string? cursor = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches followers of a target profile UID.
    /// </summary>
    Task<PagedResult<UserSummary>> GetProfileFollowersAsync(IBrowserSession session, string targetUid, int count = 8, string? cursor = null, CancellationToken ct = default);

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
    /// Unfollows a target user profile.
    /// </summary>
    Task<bool> UnfollowUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default);

    /// <summary>
    /// Pokes a target friend (<c>PokesMutatorPokeMutation</c>).
    /// </summary>
    Task<bool> PokeUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // 4. Stories, Watch, Notifications, Fanpages & Media Uploads
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches the active stories tray items (<c>StoriesTrayRectangularQuery</c>).
    /// </summary>
    Task<IReadOnlyList<StoryBucketItem>> GetStoriesTrayAsync(IBrowserSession session, CancellationToken ct = default);

    /// <summary>
    /// Marks a story as seen (<c>storiesUpdateSeenStateMutation</c>).
    /// </summary>
    Task<bool> MarkStorySeenAsync(IBrowserSession session, string bucketId, string storyId, CancellationToken ct = default);

    /// <summary>
    /// Sends a lightweight emoji reaction or reply to a story (<c>useStoriesSendReplyMutation</c>).
    /// </summary>
    Task<bool> ReplyToStoryAsync(IBrowserSession session, string storyId, string emojiOrMessage, CancellationToken ct = default);

    /// <summary>
    /// Creates a new story with a photo, video, or text preset (<c>StoriesCreateMutation</c>).
    /// </summary>
    Task<bool> CreateStoryAsync(IBrowserSession session, string? text = null, string? photoId = null, string? videoId = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches notifications from the notifications panel (<c>CometNotificationsListPaginationQuery</c>).
    /// </summary>
    Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(IBrowserSession session, CancellationToken ct = default);

    /// <summary>
    /// Marks a notification item as read (<c>CometNotificationsUpdateSeenStateMutation</c>).
    /// </summary>
    Task<bool> MarkNotificationReadAsync(IBrowserSession session, NotificationItem notification, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all managed fanpages / additional profiles associated with the account.
    /// </summary>
    Task<IReadOnlyList<ManagedPageSummary>> GetManagedPagesAsync(IBrowserSession session, CancellationToken ct = default);

    /// <summary>
    /// Uploads a photo for use in feed/group posts or sales comments and returns its <c>fbid</c>.
    /// </summary>
    Task<string> UploadUfiPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default);

    /// <summary>
    /// Uploads a photo for use in Stories and returns its <c>photoID</c>.
    /// </summary>
    Task<string> UploadStoryPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default);

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
