// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;

namespace Facebook.Net.Client;

public sealed partial class CometClient
{
    /// <inheritdoc />
    public async Task<string> UploadUfiPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string uploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.UfiPhotoUpload}?av={material.Uid}&__user={material.Uid}&__a=1&fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&profile_id={material.Uid}&source=19&target_id={material.Uid}&__crn=comet.fbweb.CometProfileTimelineListViewRoute");

        string raw = await session.UploadViaDomBridgeAsync(uploadUrl, localFilePath, "file", null, ct).ConfigureAwait(false);
        ReadOnlySpan<char> sanitized = ProtocolCodec.StripAntiJsonpPrefix(raw.AsSpan());

        using JsonDocument doc = JsonDocument.Parse(sanitized.ToString());
        if (doc.RootElement.TryGetProperty("payload"u8, out JsonElement payloadEl) &&
            payloadEl.TryGetProperty("fbid"u8, out JsonElement fbidEl))
        {
            string? fbid = fbidEl.ValueKind == JsonValueKind.String
                ? fbidEl.GetString()
                : fbidEl.GetRawText();

            if (!string.IsNullOrWhiteSpace(fbid))
            {
                return fbid;
            }
        }

        throw new ProtocolException("Failed to extract 'payload.fbid' from UFI photo upload response.");
    }

    /// <inheritdoc />
    public async Task<string> UploadStoryPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        string uploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.StoryPhotoUpload}?av={material.Uid}&__user={material.Uid}&__a=1&fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&__crn=comet.fbweb.CometStoriesCreateRoute");

        Dictionary<string, string> fields = new(4, StringComparer.Ordinal)
        {
            ["source"] = "8",
            ["profile_id"] = material.Uid,
            ["waterfallxapp"] = "comet_stories",
            ["upload_id"] = "jsc_c_i"
        };

        string raw = await session.UploadViaDomBridgeAsync(uploadUrl, localFilePath, "file", fields, ct).ConfigureAwait(false);
        ReadOnlySpan<char> sanitized = ProtocolCodec.StripAntiJsonpPrefix(raw.AsSpan());

        using JsonDocument doc = JsonDocument.Parse(sanitized.ToString());
        if (doc.RootElement.TryGetProperty("payload"u8, out JsonElement payloadEl) &&
            payloadEl.TryGetProperty("photoID"u8, out JsonElement photoIdEl))
        {
            string? photoId = photoIdEl.ValueKind == JsonValueKind.String
                ? photoIdEl.GetString()
                : photoIdEl.GetRawText();

            if (!string.IsNullOrWhiteSpace(photoId))
            {
                return photoId;
            }
        }

        throw new ProtocolException("Failed to extract 'payload.photoID' from Story photo upload response.");
    }

    /// <inheritdoc />
    public async Task<string> UploadVideoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        FileInfo fileInfo = new(localFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Video file not found.", localFilePath);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        GraphQlRequest configReq = new(
            FriendlyName: ProtocolConstants.Queries.MediaUploadConfigName,
            DocId: ProtocolConstants.Queries.MediaUploadConfigDocId,
            VariablesJson: /*lang=json,strict*/ "{\"source_type\":\"composer\"}");

        GraphQlResult configRes = await this.ExecuteGraphQlAsync(session, configReq, ct).ConfigureAwait(false);
        string serviceName = ResponseParser.FindFirstStringProperty(configRes, "service_name")
            ?? ProtocolConstants.Protocol.DefaultVideoUploadService;

        string waterfallId = PayloadBuilder.NewUuidV4();
        string startUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.VideoUploadStart}?av={material.Uid}&__user={material.Uid}&__a=1");

        string startBody = string.Create(
            CultureInfo.InvariantCulture,
            $"fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&file_size={fileInfo.Length}&source=newsfeed_composer&waterfall_id={waterfallId}");

        Dictionary<string, string> formHeaders = new(1, StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = "application/x-www-form-urlencoded"
        };

        string startRaw = await session.ExecuteInPagePostAsync(startUrl, formHeaders, startBody, ct).ConfigureAwait(false);
        GraphQlResult startParsed = ResponseParser.ParseGraphQlResult(session.AccountUid, startRaw);
        string videoId = ResponseParser.FindFirstStringProperty(startParsed, "video_id")
            ?? throw new ProtocolException("Failed to obtain 'video_id' from video upload start endpoint.");

        string streamToken = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string ruploadUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"https://{serviceName}.facebook.com/fb_video/{streamToken}-0-1048576?av={material.Uid}&__user={material.Uid}&__a=1&product_media_id={videoId}");

        string ruploadRaw = await session.UploadViaDomBridgeAsync(ruploadUrl, localFilePath, "file", null, ct).ConfigureAwait(false);
        GraphQlResult ruploadParsed = ResponseParser.ParseGraphQlResult(session.AccountUid, ruploadRaw);
        string chunkHandle = ResponseParser.FindFirstStringProperty(ruploadParsed, "h")
            ?? throw new ProtocolException("Failed to obtain chunk handle 'h' from RUpload video server.");

        string receiveUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"{ProtocolConstants.Endpoints.VideoUploadReceive}?av={material.Uid}&__user={material.Uid}&__a=1");

        string receiveBody = string.Create(
            CultureInfo.InvariantCulture,
            $"fb_dtsg={Uri.EscapeDataString(material.FbDtsg)}&jazoest={material.Jazoest}&video_id={videoId}&fbuploader_video_file_chunk={Uri.EscapeDataString(chunkHandle)}&source=newsfeed_composer");

        _ = await session.ExecuteInPagePostAsync(receiveUrl, formHeaders, receiveBody, ct).ConfigureAwait(false);
        return videoId;
    }
}
