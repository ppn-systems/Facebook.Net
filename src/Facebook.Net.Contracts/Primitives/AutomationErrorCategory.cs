// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Facebook.Net.Contracts.Primitives;

/// <summary>
/// Categorizes Facebook Comet GraphQL and browser automation errors so campaigns can distinguish
/// transient network/rate-limit glitches from Action Blocks (Error 368) and Checkpoints (Error 190/1357004).
/// </summary>
public enum AutomationErrorCategory
{
    /// <summary>No error occurred.</summary>
    None = 0,

    /// <summary>Transient network glitch or temporary rate limit (e.g., 1675004) that is safe to retry with backoff.</summary>
    TransientRetryable = 1,

    /// <summary>Facebook anti-spam policy block (Error 368: Temporarily Blocked) — must trip the campaign circuit breaker immediately.</summary>
    ActionBlocked368 = 2,

    /// <summary>Session expired or account entered Checkpoint (Error 190, 1357001, 1357004, 282, 956) — must halt and mark account status.</summary>
    CheckpointOrAuthFailure = 3,

    /// <summary>Permanent request/payload validation failure.</summary>
    PermanentPayloadError = 4
}
