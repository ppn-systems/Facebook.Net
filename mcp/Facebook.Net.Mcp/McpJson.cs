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

    /// <summary>
    /// Normalizes excessive blank lines and truncates long post text for list-returning MCP tools
    /// to save AI context tokens while preserving full text when <paramref name="maxLength"/> is <c>&lt;= 0</c>.
    /// </summary>
    public static string? TruncateText(string? text, int maxLength = 450)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string trimmed = text.Trim();
        if (maxLength <= 0 || trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return string.Concat(trimmed.AsSpan(0, maxLength).TrimEnd(), "...");
    }
}
