// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Facebook.Net.Mcp;

/// <summary>
/// Shared token-efficient JSON serializer configuration for <c>Facebook.Net.Mcp</c>.
/// Preserves raw UTF-8 Vietnamese diacritics (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>)
/// instead of escaping to <c>\uXXXX</c> sequences and omits <see langword="null"/> properties to minimize AI context window usage.
/// </summary>
public static class McpJson
{
    /// <summary>
    /// Shared <see cref="JsonSerializerOptions"/> instance for all MCP tool responses.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes the specified value to a compact, unescaped UTF-8 JSON string.
    /// </summary>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);
}
