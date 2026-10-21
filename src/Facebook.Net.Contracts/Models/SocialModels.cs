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
/// Summary of a feed or group post story, including engagement metrics, creation timestamp, and source group metadata.
/// </summary>
public sealed record PostSummary(
    string PostId,
    string AuthorUid,
    string? AuthorName = null,
    string? MessageText = null,
    string? PermalinkUrl = null,
    int ReactionCount = 0,
    int CommentCount = 0,
    DateTimeOffset? CreatedAt = null,
    string? GroupId = null,
    string? GroupName = null,
    string? MediaPreviewUrl = null,
    bool IsVideo = false,
    string? AuthorAvatarUrl = null,
    string? GroupAvatarUrl = null,
    string? TagsCsv = null,
    string? Notes = null)
{
    /// <summary>
    /// Returns the list of assigned customer/lead tag identifiers on this post.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> GetTagIds() =>
        string.IsNullOrWhiteSpace(this.TagsCsv)
            ? Array.Empty<string>()
            : this.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Checks whether this post is assigned the specified customer/lead <paramref name="tagId"/>.
    /// </summary>
    public bool HasTag(string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId) || string.IsNullOrWhiteSpace(this.TagsCsv))
        {
            return false;
        }

        string[] parts = this.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], tagId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Summary of a community group.
/// </summary>
public sealed record GroupSummary(
    string GroupId,
    string Name,
    GroupMembershipState MembershipState = GroupMembershipState.Unknown,
    string? AvatarUrl = null);

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
/// Represents a user-defined, color-coded tag for categorizing and batch-selecting target groups.
/// </summary>
public sealed record GroupTag(
    string Id,
    string Name,
    string ColorHex);

/// <summary>
/// Represents a saved target group configured for automated group posting or feed scraping/commenting.
/// </summary>
public sealed record SavedTargetGroup(
    string GroupId,
    string GroupName,
    bool IsEnabled = true,
    string? Notes = null,
    string? AvatarUrl = null,
    string? TagsCsv = null)
{
    /// <summary>
    /// Returns the list of assigned <see cref="GroupTag.Id"/> identifiers on this group.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> GetTagIds() =>
        string.IsNullOrWhiteSpace(this.TagsCsv)
            ? Array.Empty<string>()
            : this.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Checks whether this target group is assigned the specified <paramref name="tagId"/>.
    /// </summary>
    public bool HasTag(string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId) || string.IsNullOrWhiteSpace(this.TagsCsv))
        {
            return false;
        }

        string[] parts = this.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i], tagId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Represents a post published via Facebook One Client or synchronized from the user's personal profile timeline,
/// supporting management, inspection, and 1-click reposting to target groups or tags.
/// </summary>
public sealed record PublishedPostRecord(
    string PostId,
    string AccountUid,
    string? AuthorName,
    string? AuthorAvatarUrl,
    string MessageText,
    string? MediaPreviewUrl = null,
    string? LocalMediaFilePath = null,
    string? TargetGroupId = null,
    string? TargetGroupName = null,
    string? TargetGroupAvatarUrl = null,
    string? PermalinkUrl = null,
    int ReactionCount = 0,
    int CommentCount = 0,
    DateTimeOffset CreatedAt = default,
    bool IsPersonalProfilePost = false);

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
