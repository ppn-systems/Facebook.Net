// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Meta.UI.Core.Publisher;

/// <summary>
/// Domain service contract for the Campaign Publisher Bounded Context: encapsulates Spintax variation,
/// dynamic variable personalization, multi-photo path parsing, Up-Top bump post eligibility, and template creation.
/// </summary>
public interface ICampaignPublisherDomainService
{
    /// <summary>Resolves nested or flat Spintax expressions (<c>{option A|option B|option C}</c>) and dynamic macros using thread-safe random selection.</summary>
    string SpinText(string? spintaxTemplate);

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

    /// <summary>Samples a truncated Box-Muller Gaussian delay bounded within <c>[minSeconds, maxSeconds]</c> with millisecond resolution.</summary>
    TimeSpan NextGaussianDelay(double minSeconds, double maxSeconds);

    /// <summary>Classifies an exception thrown during automation into an <see cref="AutomationErrorCategory"/> to drive circuit-breaker policies.</summary>
    AutomationErrorCategory ClassifyAutomationException(Exception exception);
}

/// <summary>
/// Default stateless implementation of <see cref="ICampaignPublisherDomainService"/>.
/// </summary>
public sealed class CampaignPublisherDomainService : ICampaignPublisherDomainService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="CampaignPublisherDomainService"/>.</summary>
    public static CampaignPublisherDomainService Instance { get; } = new();

    /// <inheritdoc />
    public string SpinText(string? spintaxTemplate)
    {
        if (string.IsNullOrEmpty(spintaxTemplate) || !spintaxTemplate.Contains('{', StringComparison.Ordinal))
        {
            return spintaxTemplate ?? string.Empty;
        }

        string current = spintaxTemplate;
        int maxIterations = 256;

        while (maxIterations-- > 0)
        {
            int searchFrom = 0;
            int openIdx = -1;
            int closeIdx = -1;

            while (searchFrom < current.Length)
            {
                int candidateClose = current.IndexOf('}', searchFrom);
                if (candidateClose < 0)
                {
                    break;
                }

                int candidateOpen = current.LastIndexOf('{', candidateClose);
                if (candidateOpen >= 0)
                {
                    openIdx = candidateOpen;
                    closeIdx = candidateClose;
                    break;
                }

                searchFrom = candidateClose + 1;
            }

            if (openIdx < 0 || closeIdx < 0)
            {
                break;
            }

            ReadOnlySpan<char> inner = current.AsSpan(openIdx + 1, closeIdx - openIdx - 1);
            int choiceCount = 1;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '|')
                {
                    choiceCount++;
                }
            }

            string replacement;
            if (choiceCount == 1 && TryExpandDynamicMacro(inner, out string? macroValue))
            {
                replacement = macroValue;
            }
            else
            {
                int targetChoice = Random.Shared.Next(choiceCount);
                int currentChoice = 0;
                int segStart = 0;
                ReadOnlySpan<char> picked = inner;

                for (int i = 0; i <= inner.Length; i++)
                {
                    if (i == inner.Length || inner[i] == '|')
                    {
                        if (currentChoice == targetChoice)
                        {
                            picked = inner[segStart..i];
                            break;
                        }

                        currentChoice++;
                        segStart = i + 1;
                    }
                }

                replacement = picked.ToString();
            }

            current = string.Concat(
                current.AsSpan(0, openIdx),
                replacement.AsSpan(),
                current.AsSpan(closeIdx + 1));
        }

        return current;
    }

    private static bool TryExpandDynamicMacro(ReadOnlySpan<char> token, [NotNullWhen(true)] out string? expanded)
    {
        ReadOnlySpan<char> trimmed = token.Trim();
        if (trimmed.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            expanded = DateTimeOffset.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            return true;
        }

        if (trimmed.Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            expanded = DateTimeOffset.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        if (trimmed.Equals("datetime", StringComparison.OrdinalIgnoreCase))
        {
            expanded = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        if (trimmed.Equals("day", StringComparison.OrdinalIgnoreCase))
        {
            expanded = DateTimeOffset.Now.ToString("dddd", CultureInfo.CurrentCulture);
            return true;
        }

        if (trimmed.Equals("random_id", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ref", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            int code = Random.Shared.Next(1000, 9999);
            expanded = string.Create(CultureInfo.InvariantCulture, $"#{code}");
            return true;
        }

        expanded = null;
        return false;
    }

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

    /// <inheritdoc />
    public TimeSpan NextGaussianDelay(double minSeconds, double maxSeconds)
    {
        double clampedMin = Math.Max(0.0, minSeconds);
        double clampedMax = Math.Max(clampedMin, maxSeconds);
        if (clampedMax <= 0.0)
        {
            return TimeSpan.Zero;
        }

        double minMs = clampedMin * 1000.0;
        double maxMs = clampedMax * 1000.0;
        if (Math.Abs(maxMs - minMs) < 1.0)
        {
            return TimeSpan.FromMilliseconds(Math.Round(minMs));
        }

        double mean = (minMs + maxMs) / 2.0;
        double stdDev = Math.Max(50.0, (maxMs - minMs) / 4.0);

        double u1 = 1.0 - Random.Shared.NextDouble();
        double u2 = 1.0 - Random.Shared.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        double sampledMs = mean + (stdDev * randStdNormal);
        double boundedMs = Math.Clamp(sampledMs, minMs, maxMs);
        return TimeSpan.FromMilliseconds(Math.Round(boundedMs));
    }

    /// <inheritdoc />
    public AutomationErrorCategory ClassifyAutomationException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is CheckpointException cpEx)
        {
            return cpEx.ErrorCode == 368
                ? AutomationErrorCategory.ActionBlocked368
                : AutomationErrorCategory.CheckpointOrAuthFailure;
        }

        string? errorMessage = exception.Message;
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            if (errorMessage.Contains("368", StringComparison.Ordinal) ||
                errorMessage.Contains("temporarily blocked", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("bị chặn tạm thời", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("spam", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.ActionBlocked368;
            }

            if (errorMessage.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("1357004", StringComparison.Ordinal) ||
                errorMessage.Contains("session expired", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.CheckpointOrAuthFailure;
            }

            if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                return AutomationErrorCategory.TransientRetryable;
            }
        }

        return AutomationErrorCategory.PermanentPayloadError;
    }
}
