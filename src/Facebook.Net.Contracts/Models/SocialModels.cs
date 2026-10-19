// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Contracts.Models;

/// <summary>
/// Summary of a user node returned from friending, followers, or reaction queries.
/// </summary>
public sealed record UserSummary(
    string Uid,
    string Name,
    string? AvatarUri = null,
    FriendshipState Friendship = FriendshipState.Unknown);

/// <summary>
/// Summary of a feed or group post story, including engagement metrics and creation timestamp.
/// </summary>
public sealed record PostSummary(
    string PostId,
    string AuthorUid,
    string? AuthorName = null,
    string? MessageText = null,
    string? PermalinkUrl = null,
    int ReactionCount = 0,
    int CommentCount = 0,
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Summary of a community group.
/// </summary>
public sealed record GroupSummary(
    string GroupId,
    string Name,
    GroupMembershipState MembershipState = GroupMembershipState.Unknown);

/// <summary>
/// Represents a pending membership question when requesting to join a group.
/// </summary>
public sealed record GroupQuestion(
    string QuestionId,
    string QuestionText,
    string QuestionType = "PARAGRAPH");

/// <summary>
/// Represents an answer to a group membership question.
/// </summary>
public sealed record GroupQuestionAnswer(
    string QuestionId,
    string AnswerText);

/// <summary>
/// Summary of a story item in the stories tray.
/// </summary>
public sealed record StoryBucketItem(
    string BucketId,
    string StoryId,
    string AuthorUid,
    bool IsSeen);

/// <summary>
/// Summary of a user notification item.
/// </summary>
public sealed record NotificationItem(
    string NotificationId,
    string QueryId,
    string TargetUrl,
    string Description,
    bool IsRead);

/// <summary>
/// Summary of a managed fanpage or additional profile.
/// </summary>
public sealed record ManagedPageSummary(
    string PageId,
    string Name,
    string? DelegatePageId = null);

/// <summary>
/// Represents a saved target group configured for automated group posting or feed scraping/commenting.
/// </summary>
public sealed record SavedTargetGroup(
    string GroupId,
    string GroupName,
    bool IsEnabled = true,
    string? Notes = null);

/// <summary>
/// Represents a reusable Spintax post or sales comment template with an optional media attachment path.
/// </summary>
public sealed record CampaignTemplate(
    string Id,
    string Title,
    string SpintaxContent,
    string? MediaFilePath = null,
    bool IsCommentTemplate = false);

/// <summary>
/// Represents an immutable audit record of an executed post or sales comment to prevent duplicate actions.
/// </summary>
public sealed record ActionHistoryEntry(
    string AccountUid,
    string ActionKind,
    string TargetId,
    string? ResultId,
    DateTimeOffset ExecutedAt);
