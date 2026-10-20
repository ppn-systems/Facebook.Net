// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace Facebook.Net.Contracts.Models;

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
    string CometReq = "15",
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
/// Represents a paginated result set with a cursor for fetching subsequent pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? EndCursor = null,
    bool HasNextPage = false);
