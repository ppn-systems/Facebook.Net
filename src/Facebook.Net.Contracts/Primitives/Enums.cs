// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Facebook.Net.Contracts.Primitives;

/// <summary>
/// Operational state of a managed account profile.
/// </summary>
public enum AccountStatus
{
    /// <summary>Account is registered but has not been verified yet.</summary>
    Idle,

    /// <summary>Account session is valid and ready for operations.</summary>
    Active,

    /// <summary>Account is currently executing an automation task.</summary>
    Running,

    /// <summary>Session cookies have expired and require re-authentication.</summary>
    CookieExpired,

    /// <summary>Account was flagged by security checkpoint (e.g., 282, 956).</summary>
    Checkpoint,

    /// <summary>Account has been disabled or locked.</summary>
    Disabled
}

/// <summary>
/// Supported UFI reaction types for posts and stories.
/// </summary>
public enum ReactionKind
{
    /// <summary>Like reaction (👍).</summary>
    Like,

    /// <summary>Love reaction (❤️).</summary>
    Love,

    /// <summary>Care reaction (🥰).</summary>
    Care,

    /// <summary>Haha reaction (😆).</summary>
    Haha,

    /// <summary>Wow reaction (😮).</summary>
    Wow,

    /// <summary>Sad reaction (😢).</summary>
    Sad,

    /// <summary>Angry reaction (😡).</summary>
    Angry
}

/// <summary>
/// Relationship state between the active viewer and a target user profile.
/// </summary>
public enum FriendshipState
{
    /// <summary>Unknown or unresolved relationship state.</summary>
    Unknown,

    /// <summary>Viewer can send a friend request (CAN_REQUEST).</summary>
    CanRequest,

    /// <summary>Viewer has already sent an outgoing friend request (OUTGOING_REQUEST).</summary>
    OutgoingRequest,

    /// <summary>Target user has sent an incoming friend request to the viewer (INCOMING_REQUEST).</summary>
    IncomingRequest,

    /// <summary>Viewer and target user are friends (ARE_FRIENDS).</summary>
    AreFriends
}

/// <summary>
/// Membership state of the active viewer within a community group.
/// </summary>
public enum GroupMembershipState
{
    /// <summary>Unknown membership state.</summary>
    Unknown,

    /// <summary>Viewer is not a member and can request to join.</summary>
    CanJoin,

    /// <summary>Join request is pending admin approval or membership questions (REQUESTED).</summary>
    Requested,

    /// <summary>Membership questionnaire has been submitted (QUESTIONNAIRE_COMPLETED).</summary>
    QuestionnaireCompleted,

    /// <summary>Viewer is an active member of the group (MEMBER).</summary>
    Member
}

/// <summary>
/// Target destination when publishing or sharing a composer story.
/// </summary>
public enum ComposerTarget
{
    /// <summary>Publish to the user's personal timeline feed.</summary>
    Feed,

    /// <summary>Publish to a specific community group.</summary>
    Group,

    /// <summary>Share an existing post to timeline or group.</summary>
    Share
}

/// <summary>
/// Network proxy protocol type.
/// </summary>
public enum ProxyProtocol
{
    /// <summary>HTTP/HTTPS proxy.</summary>
    Http,

    /// <summary>SOCKS5 proxy.</summary>
    Socks5
}

/// <summary>
/// Lifecycle state of an isolated Chromium browser instance.
/// </summary>
public enum BrowserState
{
    /// <summary>Browser process is not running.</summary>
    Stopped,

    /// <summary>Browser process is queued or launching.</summary>
    Starting,

    /// <summary>Browser process is active and connected via CDP WebSocket.</summary>
    Running,

    /// <summary>Browser process exited unexpectedly or lost CDP connection.</summary>
    Faulted
}
