// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.UI.Core.Publisher;

/// <summary>
/// Domain service contract for the Campaign Publisher Bounded Context: encapsulates dynamic variable
/// personalization, multi-photo path parsing, Up-Top bump post eligibility, and template creation.
/// </summary>
public interface ICampaignPublisherDomainService
{
    /// <summary>Resolves dynamic personalization variables (<c>{author_name}</c>, <c>{group_name}</c>, <c>{date}</c>, <c>{time}</c>, <c>{random_id}</c>) in a template.</summary>
    string ResolveDynamicVariables(string? rawTemplate, string? authorName, string? groupName);

    /// <summary>Splits a single or semicolon/pipe-delimited multi-photo path string into trimmed, deduplicated file path entries.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    List<string> SplitMediaFilePaths(string? rawPaths);

    /// <summary>Parses a single or semicolon/pipe-delimited multi-photo path string into existing local file paths.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    List<string> ParseExistingMediaFilePaths(string? rawPaths);

    /// <summary>Selects deduplicated, non-placeholder group posts eligible for Up-Top comment bumping.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    List<PublishedPostRecord> SelectEligibleBumpPosts(IReadOnlyList<PublishedPostRecord> posts, string? accountUid, int maxPosts = 25);

    /// <summary>Builds a <see cref="CampaignTemplate"/> with an auto-generated 36-character title when <paramref name="title"/> is omitted.</summary>
    CampaignTemplate CreateTemplate(string? title, string spintaxContent, string? mediaFilePath, bool isCommentTemplate);
}

/// <summary>
/// Default stateless implementation of <see cref="ICampaignPublisherDomainService"/>.
/// </summary>
public sealed class CampaignPublisherDomainService : ICampaignPublisherDomainService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="CampaignPublisherDomainService"/>.</summary>
    public static CampaignPublisherDomainService Instance { get; } = new();

    /// <inheritdoc />
    public string ResolveDynamicVariables(string? rawTemplate, string? authorName, string? groupName)
    {
        if (string.IsNullOrWhiteSpace(rawTemplate))
        {
            return string.Empty;
        }

        string safeAuthor = !string.IsNullOrWhiteSpace(authorName) ? authorName.Trim() : "bạn";
        string safeGroup = !string.IsNullOrWhiteSpace(groupName) ? groupName.Trim() : "nhóm";
        DateTime now = DateTime.Now;
        return rawTemplate
            .Replace("{author_name}", safeAuthor, StringComparison.OrdinalIgnoreCase)
            .Replace("{group_name}", safeGroup, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{random_id}", Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    public List<string> SplitMediaFilePaths(string? rawPaths)
    {
        List<string> results = new();
        if (string.IsNullOrWhiteSpace(rawPaths))
        {
            return results;
        }

        string[] parts = rawPaths.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < parts.Length; i++)
        {
            string candidate = parts[i].Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    public List<string> ParseExistingMediaFilePaths(string? rawPaths)
    {
        List<string> candidates = this.SplitMediaFilePaths(rawPaths);
        List<string> existing = new(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (File.Exists(candidates[i]))
            {
                existing.Add(candidates[i]);
            }
        }

        return existing;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Direct List return avoids interface dispatch overhead.")]
    public List<PublishedPostRecord> SelectEligibleBumpPosts(
        IReadOnlyList<PublishedPostRecord> posts,
        string? accountUid,
        int maxPosts = 25)
    {
        ArgumentNullException.ThrowIfNull(posts);
        int limit = Math.Clamp(maxPosts, 1, 50);
        List<PublishedPostRecord> eligible = new();
        HashSet<string> seenPostIds = new(StringComparer.Ordinal);

        for (int i = 0; i < posts.Count && eligible.Count < limit; i++)
        {
            PublishedPostRecord p = posts[i];
            if (p.IsPersonalProfilePost ||
                string.IsNullOrWhiteSpace(p.PostId) ||
                p.PostId.StartsWith("grp_", StringComparison.OrdinalIgnoreCase) ||
                p.PostId.StartsWith("feed_", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(accountUid) && !string.Equals(p.AccountUid, accountUid, StringComparison.Ordinal)))
            {
                continue;
            }

            if (seenPostIds.Add(p.PostId))
            {
                eligible.Add(p);
            }
        }

        return eligible;
    }

    /// <inheritdoc />
    public CampaignTemplate CreateTemplate(
        string? title,
        string spintaxContent,
        string? mediaFilePath,
        bool isCommentTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spintaxContent);
        string trimmedContent = spintaxContent.Trim();
        string resolvedTitle = !string.IsNullOrWhiteSpace(title)
            ? title.Trim()
            : (trimmedContent.Length > 36 ? string.Concat(trimmedContent[..36], "...") : trimmedContent);

        string prefix = isCommentTemplate ? "tpl_bump" : "tpl";
        return new CampaignTemplate(
            Id: $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Title: resolvedTitle,
            SpintaxContent: spintaxContent,
            MediaFilePath: string.IsNullOrWhiteSpace(mediaFilePath) ? null : mediaFilePath.Trim(),
            IsCommentTemplate: isCommentTemplate);
    }
}
