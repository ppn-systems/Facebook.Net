// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Protocol;

/// <summary>
/// Classifies Facebook Comet GraphQL error codes, messages, and runtime exceptions into
/// <see cref="AutomationErrorCategory"/> to drive circuit-breaker and retry policies.
/// </summary>
public static class ProtocolErrorClassifier
{
    /// <summary>
    /// Classifies a <see cref="GraphQlResult"/> into an <see cref="AutomationErrorCategory"/>.
    /// </summary>
    public static AutomationErrorCategory Classify(GraphQlResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return AutomationErrorCategory.None;
        }

        return Classify(result.ErrorCode, result.ErrorMessage);
    }

    /// <summary>
    /// Classifies an exception thrown during automation into an <see cref="AutomationErrorCategory"/>.
    /// </summary>
    public static AutomationErrorCategory ClassifyException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is CheckpointException cpEx)
        {
            return cpEx.ErrorCode == 368
                ? AutomationErrorCategory.ActionBlocked368
                : AutomationErrorCategory.CheckpointOrAuthFailure;
        }

        return Classify(null, exception.Message);
    }

    /// <summary>
    /// Classifies an optional numeric Facebook error code and error message string.
    /// </summary>
    public static AutomationErrorCategory Classify(int? errorCode, string? errorMessage)
    {
        if (errorCode.HasValue)
        {
            switch (errorCode.Value)
            {
                case 368:
                case 1404078:
                    return AutomationErrorCategory.ActionBlocked368;

                case 190:
                case 282:
                case 956:
                case 1357001:
                case 1357004:
                case 1357045:
                    return AutomationErrorCategory.CheckpointOrAuthFailure;

                case 1:
                case 2:
                case 4:
                case 17:
                case 341:
                case 1357031:
                case 1675004:
                    return AutomationErrorCategory.TransientRetryable;

                default:
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            if (errorMessage.Contains("368", StringComparison.Ordinal) ||
                errorMessage.Contains("temporarily blocked", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("bị chặn tạm thời", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("spam", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.ActionBlocked368;
            }

            if (errorMessage.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("1357004", StringComparison.Ordinal) ||
                errorMessage.Contains("session expired", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.CheckpointOrAuthFailure;
            }

            if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.TransientRetryable;
            }
        }

        return AutomationErrorCategory.PermanentPayloadError;
    }
}
