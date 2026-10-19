// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Text;

namespace Facebook.Net.Contracts;

/// <summary>
/// Provides zero/low-allocation helper routines for Comet token checksums (<c>jazoest</c>),
/// Base64 Relay Node ID encoding, and response payload sanitization.
/// </summary>
public static class ProtocolCodec
{
    /// <summary>
    /// Computes the <c>jazoest</c> integrity checksum from an <c>fb_dtsg</c> token
    /// by summing the ASCII/Unicode code points of all characters and prefixing with <c>'2'</c>.
    /// </summary>
    /// <param name="fbDtsg">The dynamic <c>fb_dtsg</c> token string.</param>
    /// <returns>The computed <c>jazoest</c> checksum (e.g., <c>"22415"</c>).</returns>
    public static string ComputeJazoest(ReadOnlySpan<char> fbDtsg)
    {
        int sum = 0;
        for (int i = 0; i < fbDtsg.Length; i++)
        {
            sum += fbDtsg[i];
        }

        return string.Concat("2", sum.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Encodes a UFI feedback target identifier (<c>feedback:&lt;postId&gt;</c>) as Base64.
    /// </summary>
    /// <param name="postId">The numeric or string post ID.</param>
    /// <returns>Base64-encoded Relay feedback node ID.</returns>
    public static string EncodeFeedbackId(string postId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        return EncodeBase64Utf8(string.Concat("feedback:", postId));
    }

    /// <summary>
    /// Encodes a single post permalink story identifier (<c>S:_I&lt;viewerUid&gt;:&lt;postId&gt;:&lt;postId&gt;</c>) as Base64.
    /// </summary>
    /// <param name="viewerUid">The current authenticated user UID.</param>
    /// <param name="postId">The target post ID.</param>
    /// <returns>Base64-encoded Relay story node ID.</returns>
    public static string EncodeSinglePostStoryId(string viewerUid, string postId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewerUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        return EncodeBase64Utf8(string.Concat("S:_I", viewerUid, ":", postId, ":", postId));
    }

    /// <summary>
    /// Encodes a profile followers collection identifier (<c>app_collection:&lt;targetUid&gt;:2356318349:32</c>) as Base64.
    /// </summary>
    /// <param name="targetUid">The target user UID.</param>
    /// <returns>Base64-encoded Relay collection node ID.</returns>
    public static string EncodeFollowersCollectionId(string targetUid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid);
        return EncodeBase64Utf8(string.Concat("app_collection:", targetUid, ":2356318349:32"));
    }

    /// <summary>
    /// Encodes a story seen/reply node identifier (<c>S:_ISC:&lt;storyId&gt;</c>) as Base64.
    /// </summary>
    /// <param name="storyId">The story ID.</param>
    /// <returns>Base64-encoded Relay story node ID.</returns>
    public static string EncodeStoryCardId(string storyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        return EncodeBase64Utf8(string.Concat("S:_ISC:", storyId));
    }

    /// <summary>
    /// Maps a <see cref="ReactionKind"/> enum value to its fixed Comet <c>feedback_reaction_id</c>.
    /// </summary>
    /// <param name="reaction">The reaction kind.</param>
    /// <returns>The numeric reaction ID string.</returns>
    public static string GetReactionId(ReactionKind reaction) =>
        reaction switch
        {
            ReactionKind.Like => ProtocolConstants.Reactions.Like,
            ReactionKind.Love => ProtocolConstants.Reactions.Love,
            ReactionKind.Care => ProtocolConstants.Reactions.Care,
            ReactionKind.Haha => ProtocolConstants.Reactions.Haha,
            ReactionKind.Wow => ProtocolConstants.Reactions.Wow,
            ReactionKind.Sad => ProtocolConstants.Reactions.Sad,
            ReactionKind.Angry => ProtocolConstants.Reactions.Angry,
            _ => ProtocolConstants.Reactions.Like
        };

    /// <summary>
    /// Strips the leading <c>for (;;);</c> anti-JSONP prefix from an AJAX or GraphQL response if present.
    /// </summary>
    /// <param name="rawResponse">The raw response string.</param>
    /// <returns>A sanitized slice with the prefix removed.</returns>
    public static ReadOnlySpan<char> StripAntiJsonpPrefix(ReadOnlySpan<char> rawResponse)
    {
        ReadOnlySpan<char> trimmed = rawResponse.TrimStart();
        ReadOnlySpan<char> prefix = ProtocolConstants.Protocol.AntiJsonpPrefix.AsSpan();
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return trimmed[prefix.Length..].TrimStart();
        }

        return trimmed;
    }

    /// <summary>
    /// Splits a newline-delimited JSON (NDJSON) response stream (<c>RELAY_INCREMENTAL_DELIVERY</c>)
    /// into individual sanitized JSON object strings, stripping any <c>for (;;);</c> prefixes.
    /// </summary>
    /// <param name="rawStream">The raw response stream text.</param>
    /// <returns>An array of non-empty JSON payload lines.</returns>
    public static System.Collections.Generic.IReadOnlyList<string> SplitNdjsonPayloads(ReadOnlySpan<char> rawStream)
    {
        ReadOnlySpan<char> sanitized = StripAntiJsonpPrefix(rawStream);
        if (sanitized.IsEmpty)
        {
            return Array.Empty<string>();
        }

        System.Collections.Generic.List<string> lines = new(4);
        foreach (Range range in sanitized.Split('\n'))
        {
            ReadOnlySpan<char> line = sanitized[range].Trim();
            if (!line.IsEmpty)
            {
                lines.Add(StripAntiJsonpPrefix(line).ToString());
            }
        }

        return lines;
    }

    private static string EncodeBase64Utf8(string value)
    {
        int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);
        byte[]? rented = null;
        Span<byte> buffer = maxByteCount <= 256
            ? stackalloc byte[256]
            : (rented = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            int bytesWritten = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            return Convert.ToBase64String(buffer[..bytesWritten]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
