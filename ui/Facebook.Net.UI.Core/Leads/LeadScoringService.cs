// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.UI.Core.Leads;

/// <summary>
/// Domain service contract for the Sales Leads Bounded Context: computes buyer-intent scores,
/// normalizes Vietnamese diacritics, and evaluates keyword matches across scraped group posts.
/// </summary>
public interface ILeadScoringService
{
    /// <summary>Computes a buyer-intent score (0..100) for a scraped group post based on matched keywords and inquiry signals.</summary>
    int CalculateBuyerIntentScore(string? messageText, string? rawKeywords);

    /// <summary>Returns whether a message matches any of the specified buyer keywords (case-insensitive and diacritic-insensitive).</summary>
    bool MatchesBuyerKeywords(string? messageText, IReadOnlyList<string> keywords);

    /// <summary>Returns whether a <see cref="PostSummary"/> matches any keyword across its message, author name, or group name.</summary>
    bool MatchesPostOrAuthorKeywords(PostSummary post, IReadOnlyList<string> keywords);

    /// <summary>Strips Vietnamese diacritical marks (`ă`, `â`, `đ`, `ê`, `ô`, `ơ`, `ư`, tones) for accent-insensitive search.</summary>
    string RemoveVietnameseDiacritics(string? input);
}

/// <summary>
/// Default stateless implementation of <see cref="ILeadScoringService"/> encapsulating all buyer-intent
/// scoring and Vietnamese diacritic normalization algorithms.
/// </summary>
public sealed class LeadScoringService : ILeadScoringService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="LeadScoringService"/>.</summary>
    public static LeadScoringService Instance { get; } = new();

    /// <inheritdoc />
    public int CalculateBuyerIntentScore(string? messageText, string? rawKeywords)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return 25;
        }

        int score = 40;
        string[] keywords = string.IsNullOrWhiteSpace(rawKeywords)
            ? Array.Empty<string>()
            : rawKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int matchedCount = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (messageText.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
            {
                matchedCount++;
            }
        }

        score += Math.Min(45, matchedCount * 20);
        if (messageText.Contains('?', StringComparison.Ordinal) ||
            messageText.Contains("cần", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("tìm", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("mua", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("tư vấn", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("looking for", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("price", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <inheritdoc />
    public bool MatchesBuyerKeywords(string? messageText, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        if (keywords.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        string normalizedMsg = this.RemoveVietnameseDiacritics(messageText);
        for (int i = 0; i < keywords.Count; i++)
        {
            string kw = keywords[i];
            if (messageText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                normalizedMsg.Contains(this.RemoveVietnameseDiacritics(kw), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool MatchesPostOrAuthorKeywords(PostSummary post, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(keywords);
        if (keywords.Count == 0)
        {
            return true;
        }

        string normMsg = !string.IsNullOrWhiteSpace(post.MessageText) ? this.RemoveVietnameseDiacritics(post.MessageText) : string.Empty;
        string normAuthor = !string.IsNullOrWhiteSpace(post.AuthorName) ? this.RemoveVietnameseDiacritics(post.AuthorName) : string.Empty;
        string normGroup = !string.IsNullOrWhiteSpace(post.GroupName) ? this.RemoveVietnameseDiacritics(post.GroupName) : string.Empty;

        for (int i = 0; i < keywords.Count; i++)
        {
            string kw = keywords[i];
            string normKw = this.RemoveVietnameseDiacritics(kw);
            if ((!string.IsNullOrWhiteSpace(post.MessageText) && (post.MessageText.Contains(kw, StringComparison.OrdinalIgnoreCase) || normMsg.Contains(normKw, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(post.AuthorName) && (post.AuthorName.Contains(kw, StringComparison.OrdinalIgnoreCase) || normAuthor.Contains(normKw, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(post.GroupName) && (post.GroupName.Contains(kw, StringComparison.OrdinalIgnoreCase) || normGroup.Contains(normKw, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string RemoveVietnameseDiacritics(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string normalized = input.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                _ = sb.Append(c switch
                {
                    'đ' => 'd',
                    'Đ' => 'D',
                    _ => c
                });
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
