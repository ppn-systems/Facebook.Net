// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.Contracts.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for Native AOT and trimming compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AccountProfile))]
[JsonSerializable(typeof(ProxyConfig))]
[JsonSerializable(typeof(SessionMaterial))]
[JsonSerializable(typeof(BrowserLaunchOptions))]
[JsonSerializable(typeof(GraphQlRequest))]
[JsonSerializable(typeof(GraphQlResult))]
[JsonSerializable(typeof(UserSummary))]
[JsonSerializable(typeof(PostSummary))]
[JsonSerializable(typeof(GroupSummary))]
[JsonSerializable(typeof(GroupQuestion))]
[JsonSerializable(typeof(GroupQuestionAnswer))]
[JsonSerializable(typeof(StoryBucketItem))]
[JsonSerializable(typeof(NotificationItem))]
[JsonSerializable(typeof(ManagedPageSummary))]
[JsonSerializable(typeof(SavedTargetGroup))]
[JsonSerializable(typeof(GroupTag))]
[JsonSerializable(typeof(PublishedPostRecord))]
[JsonSerializable(typeof(CampaignTemplate))]
[JsonSerializable(typeof(ActionHistoryEntry))]
[JsonSerializable(typeof(SessionStateChangedEvent))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
public partial class ContractsJsonContext : JsonSerializerContext
{
}
