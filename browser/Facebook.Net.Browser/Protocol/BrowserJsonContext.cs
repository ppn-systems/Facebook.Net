// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Facebook.Net.Browser.Protocol;

/// <summary>
/// Represents an outbound Chrome DevTools Protocol (CDP) JSON-RPC command payload.
/// </summary>
public sealed record CdpCommandRequest(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params = null);

/// <summary>
/// Parameters for <c>Page.navigate</c> CDP command.
/// </summary>
public sealed record CdpNavigateParams(
    [property: JsonPropertyName("url")] string Url);

/// <summary>
/// Parameters for <c>Runtime.evaluate</c> CDP command.
/// </summary>
public sealed record CdpEvaluateParams(
    [property: JsonPropertyName("expression")] string Expression,
    [property: JsonPropertyName("awaitPromise")] bool AwaitPromise = true,
    [property: JsonPropertyName("returnByValue")] bool ReturnByValue = true);

/// <summary>
/// Parameters for <c>Network.getCookies</c> CDP command.
/// </summary>
public sealed record CdpGetCookiesParams(
    [property: JsonPropertyName("urls")] IReadOnlyList<string> Urls);

/// <summary>
/// Parameters for <c>DOM.getDocument</c> CDP command.
/// </summary>
public sealed record CdpGetDocumentParams(
    [property: JsonPropertyName("depth")] int Depth = 1);

/// <summary>
/// Parameters for <c>DOM.querySelector</c> CDP command.
/// </summary>
public sealed record CdpQuerySelectorParams(
    [property: JsonPropertyName("nodeId")] int NodeId,
    [property: JsonPropertyName("selector")] string Selector);

/// <summary>
/// Parameters for <c>DOM.setFileInputFiles</c> CDP command (Hidden DOM File Bridge).
/// </summary>
public sealed record CdpSetFileInputFilesParams(
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files,
    [property: JsonPropertyName("nodeId")] int NodeId);

/// <summary>
/// Represents a target page entry returned by <c>http://127.0.0.1:{port}/json/list</c>.
/// </summary>
public sealed record CdpTargetInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("webSocketDebuggerUrl")] string? WebSocketDebuggerUrl);

/// <summary>
/// Represents a browser cookie returned by <c>Network.getCookies</c>.
/// </summary>
public sealed record CdpCookie(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("domain")] string? Domain = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("httpOnly")] bool HttpOnly = false,
    [property: JsonPropertyName("secure")] bool Secure = false);

/// <summary>
/// Internal payload extracted from the page DOM via JavaScript evaluation.
/// </summary>
public sealed record DomMaterialSnapshot(
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("fbDtsg")] string FbDtsg,
    [property: JsonPropertyName("lsd")] string Lsd,
    [property: JsonPropertyName("clientRevision")] string ClientRevision,
    [property: JsonPropertyName("hasteSession")] string HasteSession,
    [property: JsonPropertyName("webSessionId")] string WebSessionId,
    [property: JsonPropertyName("userAgent")] string UserAgent);

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for CDP protocol messages under Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CdpCommandRequest))]
[JsonSerializable(typeof(CdpNavigateParams))]
[JsonSerializable(typeof(CdpEvaluateParams))]
[JsonSerializable(typeof(CdpGetCookiesParams))]
[JsonSerializable(typeof(CdpGetDocumentParams))]
[JsonSerializable(typeof(CdpQuerySelectorParams))]
[JsonSerializable(typeof(CdpSetFileInputFilesParams))]
[JsonSerializable(typeof(CdpTargetInfo))]
[JsonSerializable(typeof(CdpTargetInfo[]))]
[JsonSerializable(typeof(List<CdpTargetInfo>))]
[JsonSerializable(typeof(CdpCookie))]
[JsonSerializable(typeof(CdpCookie[]))]
[JsonSerializable(typeof(DomMaterialSnapshot))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
public partial class BrowserJsonContext : JsonSerializerContext
{
}
