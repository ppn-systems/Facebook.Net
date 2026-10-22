// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Facebook.Net.Contracts.Models;

namespace Meta.UI.Core.Groups;

/// <summary>
/// Domain service contract for the Groups &amp; Color Tags Bounded Context: encapsulates tag CSV
/// manipulation on <see cref="SavedTargetGroup"/> entities and Fisher-Yates anti-spam random group selection.
/// </summary>
public interface IGroupTagDomainService
{
    /// <summary>Normalizes a raw hex color string into a canonical <c>#RRGGBB</c> string, falling back to <paramref name="fallback"/> when invalid.</summary>
    string NormalizeColorHex(string? colorHex, string fallback = "#F6821F");

    /// <summary>Builds a new <see cref="GroupTag"/> with a normalized name and hex color.</summary>
    GroupTag CreateTag(string name, string? colorHex);

    /// <summary>Applies or removes <paramref name="tagId"/> on a <see cref="SavedTargetGroup"/> and returns whether the group's <c>TagsCsv</c> changed.</summary>
    (SavedTargetGroup UpdatedGroup, bool Changed) ApplyTagChange(SavedTargetGroup group, string tagId, bool assignTag);

    /// <summary>Toggles <paramref name="tagId"/> on a <see cref="SavedTargetGroup"/> and returns the updated entity plus whether the tag was removed.</summary>
    (SavedTargetGroup UpdatedGroup, bool WasRemoved) ToggleTagOnGroup(
        SavedTargetGroup existing,
        string groupName,
        string? avatarUrl,
        string tagId);

    /// <summary>Selects a random subset of <paramref name="randomCount"/> indices out of <paramref name="totalCount"/> using Fisher-Yates shuffle.</summary>
    HashSet<int> SelectRandomIndices(int totalCount, int randomCount);

    /// <summary>Returns <see langword="true"/> when at least 10 groups exist and at least 80% carry <c>tag_bds</c> (indicating accidental mass-tag pollution).</summary>
    bool IsMassTagBdsPolluted(IReadOnlyList<SavedTargetGroup> groups);

    /// <summary>Classifies target groups by Vietnamese topic keywords, cleans polluted or legacy tags, and returns the required tag definitions, modified groups, and per-tag counts.</summary>
    (IReadOnlyList<GroupTag> RequiredTags, IReadOnlyList<SavedTargetGroup> ModifiedGroups, IReadOnlyDictionary<string, int> CategoryCounts) ClassifyAndCleanGroups(
        IReadOnlyList<SavedTargetGroup> groups,
        IReadOnlySet<string> validTagIds,
        bool resetExistingTagsFirst = true);

    /// <summary>Configures the <see cref="SavedTargetGroup.IsEnabled"/> state across <paramref name="groups"/> by mode (<c>enable_all</c>, <c>disable_all</c>, <c>only_tag</c>, <c>enable_tag</c>, <c>disable_tag</c>, <c>random</c>).</summary>
    (IReadOnlyList<SavedTargetGroup> ModifiedGroups, int EnabledCount, int DisabledCount) ConfigureTargetGroupsSelection(
        IReadOnlyList<SavedTargetGroup> groups,
        string mode,
        string? tagId = null,
        int randomCount = 20);
}

/// <summary>
/// Default stateless implementation of <see cref="IGroupTagDomainService"/>.
/// </summary>
public sealed class GroupTagDomainService : IGroupTagDomainService
{
    private static readonly GroupTag[] s_standardTopicTags =
    [
        new("tag_bds", "Bất động sản", "#3B82F6"),
        new("tag_job", "Việc làm & Tuyển dụng", "#10B981"),
        new("tag_tech", "Công nghệ & Lập trình", "#06B6D4"),
        new("tag_market", "Mua bán & Chợ", "#F6821F"),
        new("tag_community", "Cộng đồng & Giao lưu", "#8B5CF6")
    ];

    private static readonly string[] s_bdsKeywords =
    [
        "bất động sản", "bds", "bđs", "nhà đất", "căn hộ", "chung cư", "phòng trọ",
        "nhà trọ", "mặt bằng", "cho thuê nhà", "mua bán nhà", "vinhomes", "đất nền", "real estate"
    ];

    private static readonly string[] s_jobKeywords =
    [
        "việc làm", "tuyển dụng", "tìm việc", "freelance", "nhân sự", "job",
        "hr ", "hiring", "intern", "thực tập", "part time", "full time"
    ];

    private static readonly string[] s_techKeywords =
    [
        "lập trình", "code", "developer", "dev", ".net", "c#", "dotnet", "javascript",
        "python", "react", "vue", "angular", "ai ", "claude", "chatgpt", "gpt", "cursor",
        "antigravity", "vibe coding", "cntt", "it ", "phần mềm", "software", "tech",
        "công nghệ", "linux", "docker", "aws", "cloud", "data", "sql", "mobile",
        "android", "ios", "flutter", "java", "golang", "rust", "php", "wordpress", "オープン", "voz"
    ];

    private static readonly string[] s_marketKeywords =
    [
        "mua bán", "chợ", "thanh lý", "pass ", "sỉ", "lẻ", "kinh doanh", "buôn bán",
        "thương mại", "shop", "order", "nguồn hàng", "đồ cũ", "rao vặt", "sàn ", "trao đổi"
    ];

    /// <summary>Gets the shared stateless singleton instance of <see cref="GroupTagDomainService"/>.</summary>
    public static GroupTagDomainService Instance { get; } = new();

    /// <inheritdoc />
    public string NormalizeColorHex(string? colorHex, string fallback = "#F6821F")
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return fallback;
        }

        string trimmed = colorHex.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = $"#{trimmed}";
        }

        if (trimmed.Length == 7)
        {
            bool allHex = true;
            for (int i = 1; i < 7; i++)
            {
                if (!Uri.IsHexDigit(trimmed[i]))
                {
                    allHex = false;
                    break;
                }
            }

            if (allHex)
            {
                return trimmed.ToUpperInvariant();
            }
        }

        return fallback;
    }

    /// <inheritdoc />
    public GroupTag CreateTag(string name, string? colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string color = this.NormalizeColorHex(colorHex, "#F6821F");
        return new GroupTag(
            Id: $"tag_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name: name.Trim(),
            ColorHex: color);
    }

    /// <inheritdoc />
    public (SavedTargetGroup UpdatedGroup, bool Changed) ApplyTagChange(SavedTargetGroup group, string tagId, bool assignTag)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return (group, false);
        }

        List<string> ids = [.. group.GetTagIds()];
        bool changed = false;

        if (assignTag)
        {
            bool exists = false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                ids.Add(tagId);
                changed = true;
            }
        }
        else
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    ids.RemoveAt(i);
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return (group, false);
        }

        SavedTargetGroup updated = group with
        {
            TagsCsv = ids.Count > 0 ? string.Join(',', ids) : null
        };
        return (updated, true);
    }

    /// <inheritdoc />
    public (SavedTargetGroup UpdatedGroup, bool WasRemoved) ToggleTagOnGroup(
        SavedTargetGroup existing,
        string groupName,
        string? avatarUrl,
        string tagId)
    {
        ArgumentNullException.ThrowIfNull(existing);
        List<string> ids = [.. existing.GetTagIds()];
        bool removed = false;
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
            {
                ids.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            ids.Add(tagId);
        }

        SavedTargetGroup updated = existing with
        {
            GroupName = !string.IsNullOrWhiteSpace(groupName) ? groupName : existing.GroupName,
            AvatarUrl = existing.AvatarUrl ?? avatarUrl,
            TagsCsv = ids.Count > 0 ? string.Join(',', ids) : null
        };

        return (updated, removed);
    }

    /// <inheritdoc />
    public HashSet<int> SelectRandomIndices(int totalCount, int randomCount)
    {
        HashSet<int> chosen = new();
        if (totalCount <= 0 || randomCount <= 0)
        {
            return chosen;
        }

        int count = Math.Clamp(randomCount, 1, totalCount);
        List<int> indices = new(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            indices.Add(i);
        }

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int swapIdx = Random.Shared.Next(i + 1);
            (indices[i], indices[swapIdx]) = (indices[swapIdx], indices[i]);
        }

        for (int i = 0; i < count; i++)
        {
            _ = chosen.Add(indices[i]);
        }

        return chosen;
    }

    /// <inheritdoc />
    public bool IsMassTagBdsPolluted(IReadOnlyList<SavedTargetGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count < 10)
        {
            return false;
        }

        int bdsCount = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].HasTag("tag_bds"))
            {
                bdsCount++;
            }
        }

        return (bdsCount * 100 / groups.Count) >= 80;
    }

    /// <inheritdoc />
    public (IReadOnlyList<GroupTag> RequiredTags, IReadOnlyList<SavedTargetGroup> ModifiedGroups, IReadOnlyDictionary<string, int> CategoryCounts) ClassifyAndCleanGroups(
        IReadOnlyList<SavedTargetGroup> groups,
        IReadOnlySet<string> validTagIds,
        bool resetExistingTagsFirst = true)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(validTagIds);

        List<SavedTargetGroup> modified = new();
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["tag_bds"] = 0,
            ["tag_job"] = 0,
            ["tag_tech"] = 0,
            ["tag_market"] = 0,
            ["tag_community"] = 0
        };

        for (int i = 0; i < groups.Count; i++)
        {
            SavedTargetGroup g = groups[i];
            string lower = (g.GroupName ?? string.Empty).ToLowerInvariant();
            bool isBds = ContainsAny(lower, s_bdsKeywords);
            bool isJob = ContainsAny(lower, s_jobKeywords);
            bool isTech = ContainsAny(lower, s_techKeywords);
            bool isMarket = ContainsAny(lower, s_marketKeywords);

            List<string> newTags = new(4);
            if (!resetExistingTagsFirst)
            {
                IReadOnlyList<string> existing = g.GetTagIds();
                for (int t = 0; t < existing.Count; t++)
                {
                    string part = NormalizeLegacyTagId(existing[t]);
                    if (string.Equals(part, "tag_bds", StringComparison.OrdinalIgnoreCase) && !isBds)
                    {
                        continue;
                    }

                    if (validTagIds.Count > 0 && !validTagIds.Contains(part) && !IsStandardTopicTag(part))
                    {
                        continue;
                    }

                    if (!ContainsTagIgnoreCase(newTags, part))
                    {
                        newTags.Add(part);
                    }
                }
            }

            if (isBds && !ContainsTagIgnoreCase(newTags, "tag_bds"))
            {
                newTags.Add("tag_bds");
            }

            if (isJob && !ContainsTagIgnoreCase(newTags, "tag_job"))
            {
                newTags.Add("tag_job");
            }

            if (isTech && !ContainsTagIgnoreCase(newTags, "tag_tech"))
            {
                newTags.Add("tag_tech");
            }

            if (isMarket && !ContainsTagIgnoreCase(newTags, "tag_market"))
            {
                newTags.Add("tag_market");
            }

            if (newTags.Count == 0)
            {
                newTags.Add("tag_community");
            }

            for (int t = 0; t < newTags.Count; t++)
            {
                string tagKey = newTags[t];
                counts[tagKey] = counts.TryGetValue(tagKey, out int prev) ? prev + 1 : 1;
            }

            string updatedCsv = string.Join(",", newTags);
            if (!string.Equals(updatedCsv, g.TagsCsv ?? string.Empty, StringComparison.Ordinal))
            {
                modified.Add(g with { TagsCsv = updatedCsv });
            }
        }

        return (s_standardTopicTags, modified, counts);
    }

    /// <inheritdoc />
    public (IReadOnlyList<SavedTargetGroup> ModifiedGroups, int EnabledCount, int DisabledCount) ConfigureTargetGroupsSelection(
        IReadOnlyList<SavedTargetGroup> groups,
        string mode,
        string? tagId = null,
        int randomCount = 20)
    {
        ArgumentNullException.ThrowIfNull(groups);
        string normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        string? trimmedTag = string.IsNullOrWhiteSpace(tagId) ? null : tagId.Trim();

        HashSet<int>? randomChosen = string.Equals(normalizedMode, "random", StringComparison.Ordinal)
            ? this.SelectRandomIndices(groups.Count, randomCount)
            : null;

        List<SavedTargetGroup> modified = new();
        int enabledCount = 0;
        int disabledCount = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            SavedTargetGroup g = groups[i];
            bool hasTag = !string.IsNullOrEmpty(trimmedTag) && g.HasTag(trimmedTag);
            bool shouldEnable = normalizedMode switch
            {
                "enable_all" => true,
                "disable_all" => false,
                "only_tag" => hasTag,
                "enable_tag" => hasTag || g.IsEnabled,
                "disable_tag" => !hasTag && g.IsEnabled,
                "random" => randomChosen is not null && randomChosen.Contains(i),
                _ => g.IsEnabled
            };

            if (shouldEnable)
            {
                enabledCount++;
            }
            else
            {
                disabledCount++;
            }

            if (g.IsEnabled != shouldEnable)
            {
                modified.Add(g with { IsEnabled = shouldEnable });
            }
        }

        return (modified, enabledCount, disabledCount);
    }

    private static string NormalizeLegacyTagId(string rawTagId)
    {
        if (string.Equals(rawTagId, "tag_jobs", StringComparison.OrdinalIgnoreCase))
        {
            return "tag_job";
        }

        if (string.Equals(rawTagId, "tag_sales", StringComparison.OrdinalIgnoreCase))
        {
            return "tag_market";
        }

        return rawTagId;
    }

    private static bool IsStandardTopicTag(string tagId) =>
        string.Equals(tagId, "tag_bds", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagId, "tag_job", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagId, "tag_tech", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagId, "tag_market", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagId, "tag_community", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tagId, "tag_vip", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsTagIgnoreCase(List<string> tags, string candidate)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, ReadOnlySpan<string> keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
        {
            if (text.Contains(keywords[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
