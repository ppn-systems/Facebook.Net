// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Text.Json;
using Facebook.Net.Contracts.Exceptions;
using Xunit;

namespace Facebook.Net.Contracts.Tests;

/// <summary>
/// Unit tests verifying <see cref="ProtocolCodec"/>, <see cref="ContractsJsonContext"/>, and contract models.
/// </summary>
public sealed class ProtocolCodecTests
{
    [Fact]
    public void ComputeJazoest_ReturnsExpectedAsciiChecksumWithPrefix2()
    {
        // 'A' (65) + 'B' (66) + 'C' (67) = 198 -> "2198"
        string result = ProtocolCodec.ComputeJazoest("ABC".AsSpan());
        Assert.Equal("2198", result);
    }

    [Fact]
    public void EncodeFeedbackId_EncodesRelayNodeFormat()
    {
        string encoded = ProtocolCodec.EncodeFeedbackId("123456789");
        Assert.Equal("ZmVlZGJhY2s6MTIzNDU2Nzg5", encoded);
    }

    [Fact]
    public void EncodeSinglePostStoryId_EncodesExpectedFormat()
    {
        string encoded = ProtocolCodec.EncodeSinglePostStoryId("10001", "99999");
        string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Assert.Equal("S:_I10001:99999:99999", decoded);
    }

    [Fact]
    public void EncodeFollowersCollectionId_EncodesExpectedFormat()
    {
        string encoded = ProtocolCodec.EncodeFollowersCollectionId("10001");
        string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Assert.Equal("app_collection:10001:2356318349:32", decoded);
    }

    [Fact]
    public void EncodeStoryCardId_EncodesExpectedFormat()
    {
        string encoded = ProtocolCodec.EncodeStoryCardId("55555");
        string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Assert.Equal("S:_ISC:55555", decoded);
    }

    [Fact]
    public void StripAntiJsonpPrefix_RemovesPrefixWhenPresent()
    {
        ReadOnlySpan<char> raw = "for (;;);{\"payload\":{\"fbid\":\"123\"}}".AsSpan();
        ReadOnlySpan<char> stripped = ProtocolCodec.StripAntiJsonpPrefix(raw);
        Assert.Equal("{\"payload\":{\"fbid\":\"123\"}}", stripped.ToString());
    }

    [Fact]
    public void StripAntiJsonpPrefix_PreservesCleanJson()
    {
        ReadOnlySpan<char> raw = "{\"data\":{\"viewer\":{}}}".AsSpan();
        ReadOnlySpan<char> stripped = ProtocolCodec.StripAntiJsonpPrefix(raw);
        Assert.Equal("{\"data\":{\"viewer\":{}}}", stripped.ToString());
    }

    [Fact]
    public void GetReactionId_MapsAllReactionsCorrectly()
    {
        Assert.Equal(ProtocolConstants.Reactions.Like, ProtocolCodec.GetReactionId(ReactionKind.Like));
        Assert.Equal(ProtocolConstants.Reactions.Love, ProtocolCodec.GetReactionId(ReactionKind.Love));
        Assert.Equal(ProtocolConstants.Reactions.Care, ProtocolCodec.GetReactionId(ReactionKind.Care));
        Assert.Equal(ProtocolConstants.Reactions.Haha, ProtocolCodec.GetReactionId(ReactionKind.Haha));
        Assert.Equal(ProtocolConstants.Reactions.Wow, ProtocolCodec.GetReactionId(ReactionKind.Wow));
        Assert.Equal(ProtocolConstants.Reactions.Sad, ProtocolCodec.GetReactionId(ReactionKind.Sad));
        Assert.Equal(ProtocolConstants.Reactions.Angry, ProtocolCodec.GetReactionId(ReactionKind.Angry));
    }

    [Fact]
    public void ContractsJsonContext_SerializesAndDeserializesSessionMaterialUnderAot()
    {
        SessionMaterial material = new(
            Uid: "1000123456",
            CookieHeader: "c_user=1000123456; xs=secret;",
            FbDtsg: "NAcM123",
            Jazoest: "22415",
            Lsd: "AdQVcDLs",
            ClientRevision: "1050818501",
            HasteSession: "20745.HYP:comet_pkg.2.1...1",
            WebSessionId: "abc123:def456:ghi789",
            UserAgent: ProtocolConstants.Protocol.DefaultWindowsUserAgent,
            ExtractedAt: DateTimeOffset.UtcNow);

        string json = JsonSerializer.Serialize(material, ContractsJsonContext.Default.SessionMaterial);
        SessionMaterial? roundtrip = JsonSerializer.Deserialize(json, ContractsJsonContext.Default.SessionMaterial);

        Assert.NotNull(roundtrip);
        Assert.Equal(material.Uid, roundtrip.Uid);
        Assert.Equal(material.FbDtsg, roundtrip.FbDtsg);
        Assert.Equal(material.Jazoest, roundtrip.Jazoest);
    }

    [Fact]
    public void CheckpointException_StoresUidAndErrorCode()
    {
        CheckpointException ex = new("1000999", "Checkpoint 282 triggered", 282);
        Assert.Equal("1000999", ex.AccountUid);
        Assert.Equal(282, ex.ErrorCode);
    }

    [Fact]
    public void SplitNdjsonPayloads_SplitsIncrementalDeliveryLinesAndStripsPrefix()
    {
        string raw = "for (;;);{\"data\":{\"viewer\":{\"news_feed\":{}}}}\r\n{\"label\":\"CometNewsFeed_node\",\"data\":{\"node\":{\"id\":\"1\"}}}\n{\"data\":{\"page_info\":{\"has_next_page\":true}}}";
        System.Collections.Generic.IReadOnlyList<string> chunks = ProtocolCodec.SplitNdjsonPayloads(raw.AsSpan());

        Assert.Equal(3, chunks.Count);
        Assert.StartsWith("{\"data\":{\"viewer\"", chunks[0], StringComparison.Ordinal);
        Assert.StartsWith("{\"label\":\"CometNewsFeed_node\"", chunks[1], StringComparison.Ordinal);
        Assert.StartsWith("{\"data\":{\"page_info\"", chunks[2], StringComparison.Ordinal);
    }
}
