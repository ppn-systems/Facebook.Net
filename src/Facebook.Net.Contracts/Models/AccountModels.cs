// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Contracts.Models;

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
/// Event payload emitted when an account or browser session changes state.
/// </summary>
public sealed record SessionStateChangedEvent(
    string AccountUid,
    BrowserState BrowserState,
    AccountStatus AccountStatus,
    string? Message = null);
