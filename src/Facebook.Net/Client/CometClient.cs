// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;

namespace Facebook.Net.Client;

/// <summary>
/// High-performance Comet GraphQL and media upload client implementing <see cref="ICometClient"/>.
/// Split into domain-specific partial files:
/// <list type="bullet">
/// <item><description><c>CometClient.cs</c> (Core execution &amp; helpers)</description></item>
/// <item><description><c>CometClient.Posts.cs</c> (Publishing, Sales Commenting, Reactions, Feed Scraping)</description></item>
/// <item><description><c>CometClient.Groups.cs</c> (Group Search, Scraping, Joining, Questionnaires)</description></item>
/// <item><description><c>CometClient.Friending.cs</c> (PYMK, Friend Requests, Followers, Unfriend, Poke)</description></item>
/// <item><description><c>CometClient.StoriesAndPages.cs</c> (Stories, Notifications, Managed Fanpages)</description></item>
/// <item><description><c>CometClient.MediaUpload.cs</c> (UFI Photo, Story Photo, 4-Step RUpload Video)</description></item>
/// </list>
/// </summary>
public sealed partial class CometClient : ICometClient
{
    private int _requestSequence;

    /// <inheritdoc />
    public async Task<GraphQlResult> ExecuteGraphQlAsync(IBrowserSession session, GraphQlRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.PreNavigateUrl))
        {
            string fullPreUrl = ResolveFullUrl(request.PreNavigateUrl);
            await session.NavigateAsync(fullPreUrl, ct).ConfigureAwait(false);
        }

        SessionMaterial material = session.CurrentMaterial
            ?? await session.ExtractMaterialAsync(ct).ConfigureAwait(false);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            int reqNum = Interlocked.Increment(ref this._requestSequence);
            Dictionary<string, string> headers = PayloadBuilder.BuildGraphQlHeaders(material, request.FriendlyName);
            string formBody = PayloadBuilder.BuildGraphQlFormBody(material, request, reqNum);

            string rawResponse = await session.ExecuteInPagePostAsync(
                ProtocolConstants.Endpoints.GraphQl,
                headers,
                formBody,
                ct).ConfigureAwait(false);

            GraphQlResult result = ResponseParser.ParseGraphQlResult(session.AccountUid, rawResponse);

            if (!result.IsSuccess && attempt == 0 && ProtocolErrorClassifier.Classify(result) == AutomationErrorCategory.TransientRetryable)
            {
                material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
                continue;
            }

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(request.PostNavigateUrl))
            {
                string fullPostUrl = ResolveFullUrl(request.PostNavigateUrl);
                await session.NavigateAsync(fullPostUrl, ct).ConfigureAwait(false);
            }

            return result;
        }

        return new GraphQlResult(IsSuccess: false, Array.Empty<string>(), ErrorMessage: "Exhausted GraphQL execution retries.");
    }

    private static string ResolveFullUrl(string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return pathOrUrl;
        }

        return pathOrUrl.StartsWith('/')
            ? string.Concat(ProtocolConstants.Endpoints.BaseOrigin, pathOrUrl)
            : string.Concat(ProtocolConstants.Endpoints.BaseOrigin, "/", pathOrUrl);
    }

    private static string BuildJson(Action<Utf8JsonWriter> writeProperties)
    {
        using MemoryStream ms = new(256);
        using Utf8JsonWriter writer = new(ms);
        writer.WriteStartObject();
        writeProperties(writer);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
