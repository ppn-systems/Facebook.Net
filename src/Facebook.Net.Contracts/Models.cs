// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace Facebook.Net.Contracts;

/// <summary>
/// Represents a managed account profile stored in local SQLite persistence.
/// </summary>
public sealed record AccountProfile(
    string Uid,
    string DisplayName,
    string CookieHeader,
    string UserAgent,
    string ProfileDirectory,
    string? ProxyId = null,
    string? TwoFactorSecret = null,
    AccountStatus Status = AccountStatus.Idle,
    DateTimeOffset? LastActiveAt = null);

/// <summary>
/// Represents a proxy configuration for network isolation per account profile.
/// </summary>
public sealed record ProxyConfig(
    string Id,
    string Host,
    int Port,
    ProxyProtocol Protocol = ProxyProtocol.Http,
    string? Username = null,
    string? Password = null);

/// <summary>
/// Dynamic anti-CSRF and session tokens extracted from an authenticated Comet DOM context.
/// </summary>
public sealed record SessionMaterial(
    string Uid,
    string CookieHeader,
    string FbDtsg,
    string Jazoest,
    string Lsd,
    string ClientRevision,
    string HasteSession,
    string WebSessionId,
    string UserAgent,
    DateTimeOffset ExtractedAt);

/// <summary>
/// Launch configuration for starting an isolated Chromium instance via OS-specific adapter.
/// </summary>
public sealed record BrowserLaunchOptions(
    string AccountUid,
    string UserDataDir,
    string? ExecutablePath = null,
    string? UserAgent = null,
    ProxyConfig? Proxy = null,
    bool Headless = false,
    int WindowWidth = 1280,
    int WindowHeight = 800,
    int? WindowX = null,
    int? WindowY = null);

/// <summary>
/// Descriptor for executing a Comet GraphQL persisted query or mutation.
/// </summary>
public sealed record GraphQlRequest(
    string FriendlyName,
    string DocId,
    string VariablesJson,
    string? ContextRoute = null,
    string? PreNavigateUrl = null,
    string? PostNavigateUrl = null,
    string CometReq = "0",
    bool IsMultipartStream = false);

/// <summary>
/// Sanitized result returned from a Comet GraphQL or AJAX execution.
/// </summary>
public sealed record GraphQlResult(
    bool IsSuccess,
    IReadOnlyList<string> JsonPayloads,
    string? NextCursor = null,
    bool HasNextPage = false,
    string? ErrorMessage = null,
    int? ErrorCode = null);

/// <summary>
/// Summary of a user node returned from friending, followers, or reaction queries.
/// </summary>
public sealed record UserSummary(
    string Uid,
    string Name,
    string? AvatarUri = null,
    FriendshipState Friendship = FriendshipState.Unknown);

/// <summary>
/// Summary of a feed or group post story.
/// </summary>
public sealed record PostSummary(
    string PostId,
    string AuthorUid,
    string? AuthorName = null,
    string? MessageText = null,
    string? PermalinkUrl = null);

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
/// Event payload emitted when an account or browser session changes state.
/// </summary>
public sealed record SessionStateChangedEvent(
    string AccountUid,
    BrowserState BrowserState,
    AccountStatus AccountStatus,
    string? Message = null);
