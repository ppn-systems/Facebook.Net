// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;

namespace Facebook.Net.Contracts.Exceptions;

/// <summary>
/// Base exception thrown by Comet protocol, browser automation, or GraphQL operations.
/// </summary>
public class ProtocolException : Exception
{
    /// <summary>
    /// Gets the server error code, if returned by the endpoint.
    /// </summary>
    public int? ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Optional server error code.</param>
    public ProtocolException(string message, int? errorCode = null)
        : base(message) => this.ErrorCode = errorCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errorCode">Optional server error code.</param>
    public ProtocolException(string message, Exception innerException, int? errorCode = null)
        : base(message, innerException) => this.ErrorCode = errorCode;
}

/// <summary>
/// Exception thrown when an account encounters a security checkpoint or session expiration.
/// </summary>
public sealed class CheckpointException : ProtocolException
{
    /// <summary>
    /// Gets the UID of the affected account.
    /// </summary>
    public string AccountUid { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointException"/> class.
    /// </summary>
    /// <param name="accountUid">The affected account UID.</param>
    /// <param name="message">The checkpoint reason message.</param>
    /// <param name="errorCode">Optional checkpoint code (e.g., 282, 956).</param>
    public CheckpointException(string accountUid, string message, int? errorCode = null)
        : base(message, errorCode) => this.AccountUid = accountUid;
}
