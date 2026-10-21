// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.UI.Core.Leads;

/// <summary>
/// ROI and buyer-intent performance metrics for a single scraped Facebook group.
/// </summary>
public sealed record GroupRoiStat(
    string GroupId,
    string GroupName,
    int TotalPosts,
    int HighIntentPosts,
    int PostsWithPhone,
    int ZeroCommentPosts,
    double AverageIntentScore);

/// <summary>
/// Frequency and cross-group posting metrics for a repeat post author (detecting seeders, brokers, or competitors).
/// </summary>
public sealed record RepeatAuthorStat(
    string AuthorUid,
    string AuthorName,
    int PostCount,
    int DistinctGroupCount,
    bool IsLikelySeederOrCompetitor);

/// <summary>
/// Comprehensive analytics report over scraped group posts, group ROI, repeat authors, inquiry keywords, and peak posting hours.
/// </summary>
public sealed record WorkspaceDataAnalyticsReport(
    int TotalScrapedPosts,
    int TotalPostsWithPhone,
    int TotalHighIntentPosts,
    int TotalZeroCommentOpportunities,
    int TotalCommentedPosts,
    int TotalPendingPosts,
    IReadOnlyList<GroupRoiStat> TopPerformingGroups,
    IReadOnlyList<GroupRoiStat> LowPerformingGroups,
    IReadOnlyList<RepeatAuthorStat> RepeatAuthors,
    IReadOnlyList<(string Term, int Count)> TopInquiryTerms,
    IReadOnlyList<(int HourOfDay, int PostCount)> PeakPostingHours);

/// <summary>
/// Domain service contract for the Sales Leads Bounded Context: computes buyer-intent scores,
/// normalizes Vietnamese diacritics, and evaluates keyword matches across scraped group posts.
/// </summary>
public interface ILeadScoringService
{
    /// <summary>Computes a buyer-intent score (0..100) for a scraped group post based on matched keywords and inquiry signals.</summary>
    int CalculateBuyerIntentScore(string? messageText, string? rawKeywords);

    /// <summary>Returns whether a scraped post message contains buyer inquiry or question signals.</summary>
    bool HasBuyerInquirySignal(string? messageText);

    /// <summary>Returns whether a message matches any of the specified buyer keywords (case-insensitive and diacritic-insensitive).</summary>
    bool MatchesBuyerKeywords(string? messageText, IReadOnlyList<string> keywords);

    /// <summary>Returns whether a <see cref="PostSummary"/> matches any keyword across its message, author name, or group name.</summary>
    bool MatchesPostOrAuthorKeywords(PostSummary post, IReadOnlyList<string> keywords);

    /// <summary>Strips Vietnamese diacritical marks (`ă`, `â`, `đ`, `ê`, `ô`, `ơ`, `ư`, tones) for accent-insensitive search.</summary>
    string RemoveVietnameseDiacritics(string? input);

    /// <summary>Extracts all normalized Vietnamese mobile (10-digit) and landline (11-digit) phone numbers from obfuscated text.</summary>
    IReadOnlyList<string> ExtractVietnamesePhoneNumbers(string? text);

    /// <summary>Extracts the primary (first) normalized Vietnamese phone number from obfuscated text, or <see langword="null"/> if none is found.</summary>
    string? ExtractPrimaryPhoneNumber(string? text);

    /// <summary>Computes a deep multi-dimensional analytics report across all scraped group posts and target groups.</summary>
    WorkspaceDataAnalyticsReport AnalyzeWorkspaceData(
        IReadOnlyList<PostSummary> scrapedPosts,
        IReadOnlyList<SavedTargetGroup> targetGroups,
        IReadOnlySet<string>? commentedPostIds = null,
        string? buyerKeywords = null);

    /// <summary>Formats a <see cref="WorkspaceDataAnalyticsReport"/> into a structured summary for the AI Assistant.</summary>
    string FormatAnalyticsReportForAi(WorkspaceDataAnalyticsReport report);
}

/// <summary>
/// Default stateless implementation of <see cref="ILeadScoringService"/> encapsulating all buyer-intent
/// scoring and Vietnamese diacritic normalization algorithms.
/// </summary>
public sealed class LeadScoringService : ILeadScoringService
{
    private sealed record CachedTextAnalysis(
        string NormalizedText,
        IReadOnlyList<string> ExtractedPhones,
        bool HasInquirySignal,
        int BuiltInCommercialMatches);

    private static readonly string[] s_builtInCommercialSignalsNormalized =
    [
        "can mua",
        "tim mua",
        "bao gia",
        "tu van",
        "can thue",
        "sang nhuong",
        "thanh ly",
        "pass lai",
        "xin gia",
        "gia sao",
        "lien he",
        "inbox",
        "zalo",
        "chinh chu",
        "can tim",
        "looking for",
        "recommend"
    ];

    private static readonly CachedTextAnalysis s_emptyAnalysis = new(string.Empty, [], false, 0);
    private static readonly ConcurrentDictionary<string, CachedTextAnalysis> s_analysisCache =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> s_diacriticCache =
        new(StringComparer.Ordinal);

    /// <summary>Gets the shared stateless singleton instance of <see cref="LeadScoringService"/>.</summary>
    public static LeadScoringService Instance { get; } = new();

    /// <inheritdoc />
    public int CalculateBuyerIntentScore(string? messageText, string? rawKeywords)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return 25;
        }

        CachedTextAnalysis analysis = this.GetOrAnalyzeText(messageText);
        int score = 40;
        string[] keywords = string.IsNullOrWhiteSpace(rawKeywords)
            ? []
            : rawKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int matchedCount = 0;
        if (keywords.Length > 0)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                string kw = keywords[i];
                string normKw = this.RemoveVietnameseDiacritics(kw);
                if (messageText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(normKw) && analysis.NormalizedText.Contains(normKw, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedCount++;
                }
            }

            score += Math.Min(45, matchedCount * 20);
        }
        else
        {
            score += Math.Min(35, analysis.BuiltInCommercialMatches * 15);
        }

        if (analysis.HasInquirySignal)
        {
            score += 15;
        }

        if (analysis.ExtractedPhones.Count > 0)
        {
            score += 20;
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <inheritdoc />
    public bool HasBuyerInquirySignal(string? messageText) =>
        this.GetOrAnalyzeText(messageText).HasInquirySignal;

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

        CachedTextAnalysis analysis = this.GetOrAnalyzeText(messageText);
        string normalizedMsg = analysis.NormalizedText;
        IReadOnlyList<string> extractedPhones = analysis.ExtractedPhones;
        for (int i = 0; i < keywords.Count; i++)
        {
            string kw = keywords[i];
            if (messageText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                normalizedMsg.Contains(this.RemoveVietnameseDiacritics(kw), StringComparison.OrdinalIgnoreCase) ||
                MatchesExtractedPhones(extractedPhones, kw))
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

        CachedTextAnalysis msgAnalysis = this.GetOrAnalyzeText(post.MessageText);
        string normMsg = msgAnalysis.NormalizedText;
        string normAuthor = !string.IsNullOrWhiteSpace(post.AuthorName) ? this.RemoveVietnameseDiacritics(post.AuthorName) : string.Empty;
        string normGroup = !string.IsNullOrWhiteSpace(post.GroupName) ? this.RemoveVietnameseDiacritics(post.GroupName) : string.Empty;
        IReadOnlyList<string> extractedPhones = msgAnalysis.ExtractedPhones;

        for (int i = 0; i < keywords.Count; i++)
        {
            string kw = keywords[i];
            string normKw = this.RemoveVietnameseDiacritics(kw);
            if ((!string.IsNullOrWhiteSpace(post.MessageText) && (post.MessageText.Contains(kw, StringComparison.OrdinalIgnoreCase) || normMsg.Contains(normKw, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(post.AuthorName) && (post.AuthorName.Contains(kw, StringComparison.OrdinalIgnoreCase) || normAuthor.Contains(normKw, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(post.GroupName) && (post.GroupName.Contains(kw, StringComparison.OrdinalIgnoreCase) || normGroup.Contains(normKw, StringComparison.OrdinalIgnoreCase))) ||
                MatchesExtractedPhones(extractedPhones, kw))
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

        if (s_diacriticCache.TryGetValue(input, out string? cached))
        {
            return cached;
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

        string result = sb.ToString().Normalize(NormalizationForm.FormC);
        if (s_diacriticCache.Count > 2000)
        {
            int evicted = 0;
            foreach (string key in s_diacriticCache.Keys)
            {
                _ = s_diacriticCache.TryRemove(key, out _);
                if (++evicted >= 200)
                {
                    break;
                }
            }
        }

        s_diacriticCache[input] = result;
        return result;
    }

    /// <inheritdoc />
    public string? ExtractPrimaryPhoneNumber(string? text)
    {
        IReadOnlyList<string> phones = this.ExtractVietnamesePhoneNumbers(text);
        return phones.Count > 0 ? phones[0] : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractVietnamesePhoneNumbers(string? text) =>
        this.GetOrAnalyzeText(text).ExtractedPhones;

    /// <inheritdoc />
    public WorkspaceDataAnalyticsReport AnalyzeWorkspaceData(
        IReadOnlyList<PostSummary> scrapedPosts,
        IReadOnlyList<SavedTargetGroup> targetGroups,
        IReadOnlySet<string>? commentedPostIds = null,
        string? buyerKeywords = null)
    {
        ArgumentNullException.ThrowIfNull(scrapedPosts);
        ArgumentNullException.ThrowIfNull(targetGroups);

        Dictionary<string, string> groupNameLookup = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < targetGroups.Count; i++)
        {
            SavedTargetGroup tg = targetGroups[i];
            if (!string.IsNullOrWhiteSpace(tg.GroupId) && !string.IsNullOrWhiteSpace(tg.GroupName))
            {
                groupNameLookup[tg.GroupId] = tg.GroupName;
            }
        }

        List<string> candidateTerms =
        [
            "báo giá",
            "cần mua",
            "tìm mua",
            "tư vấn",
            "chính chủ",
            "cần thuê",
            "sang nhượng",
            "sỉ",
            "lẻ",
            "gấp",
            "inbox",
            "ib"
        ];

        if (!string.IsNullOrWhiteSpace(buyerKeywords))
        {
            string[] extraKeywords = buyerKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < extraKeywords.Length; i++)
            {
                string kw = extraKeywords[i];
                bool exists = false;
                for (int j = 0; j < candidateTerms.Count; j++)
                {
                    if (string.Equals(candidateTerms[j], kw, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    candidateTerms.Add(kw);
                }
            }
        }

        int[] termCounts = new int[candidateTerms.Count];
        string[] normalizedCandidateTerms = new string[candidateTerms.Count];
        for (int i = 0; i < candidateTerms.Count; i++)
        {
            normalizedCandidateTerms[i] = this.RemoveVietnameseDiacritics(candidateTerms[i]);
        }

        Dictionary<string, (string GroupName, int Total, int HighIntent, int WithPhone, int ZeroComment, long ScoreSum)> groupAccumulators =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (string AuthorUid, string AuthorName, int Count, HashSet<string> Groups)> authorAccumulators =
            new(StringComparer.OrdinalIgnoreCase);
        int[] hourBuckets = new int[24];

        int totalWithPhone = 0;
        int totalHighIntent = 0;
        int totalZeroComment = 0;
        int totalCommented = 0;

        for (int i = 0; i < scrapedPosts.Count; i++)
        {
            PostSummary post = scrapedPosts[i];
            int score = this.CalculateBuyerIntentScore(post.MessageText, buyerKeywords);
            CachedTextAnalysis analysis = this.GetOrAnalyzeText(post.MessageText);
            bool hasPhone = analysis.ExtractedPhones.Count > 0;
            bool isHighIntent = score >= 75;
            bool isZeroComment = post.CommentCount == 0;
            bool isCommented = commentedPostIds is not null && !string.IsNullOrWhiteSpace(post.PostId) && commentedPostIds.Contains(post.PostId);

            if (hasPhone)
            {
                totalWithPhone++;
            }

            if (isHighIntent)
            {
                totalHighIntent++;
            }

            if (isZeroComment)
            {
                totalZeroComment++;
            }

            if (isCommented)
            {
                totalCommented++;
            }

            string groupId = !string.IsNullOrWhiteSpace(post.GroupId) ? post.GroupId.Trim() : "unknown";
            string groupName = !string.IsNullOrWhiteSpace(post.GroupName)
                ? post.GroupName.Trim()
                : (groupNameLookup.TryGetValue(groupId, out string? mappedName) ? mappedName : groupId);

            if (!groupAccumulators.TryGetValue(groupId, out (string GroupName, int Total, int HighIntent, int WithPhone, int ZeroComment, long ScoreSum) gAcc))
            {
                gAcc = (groupName, 0, 0, 0, 0, 0L);
            }
            else if (string.Equals(gAcc.GroupName, groupId, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(groupName, groupId, StringComparison.OrdinalIgnoreCase))
            {
                gAcc.GroupName = groupName;
            }

            gAcc.Total++;
            if (isHighIntent)
            {
                gAcc.HighIntent++;
            }

            if (hasPhone)
            {
                gAcc.WithPhone++;
            }

            if (isZeroComment)
            {
                gAcc.ZeroComment++;
            }

            gAcc.ScoreSum += score;
            groupAccumulators[groupId] = gAcc;

            string authorKey = !string.IsNullOrWhiteSpace(post.AuthorUid)
                ? post.AuthorUid.Trim()
                : (!string.IsNullOrWhiteSpace(post.AuthorName) ? post.AuthorName.Trim() : string.Empty);

            if (!string.IsNullOrWhiteSpace(authorKey))
            {
                string displayAuthorName = !string.IsNullOrWhiteSpace(post.AuthorName) ? post.AuthorName.Trim() : authorKey;
                string displayAuthorUid = !string.IsNullOrWhiteSpace(post.AuthorUid) ? post.AuthorUid.Trim() : authorKey;
                if (!authorAccumulators.TryGetValue(authorKey, out (string AuthorUid, string AuthorName, int Count, HashSet<string> Groups) aAcc))
                {
                    aAcc = (displayAuthorUid, displayAuthorName, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                aAcc.Count++;
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    _ = aAcc.Groups.Add(groupId);
                }

                authorAccumulators[authorKey] = aAcc;
            }

            if (!string.IsNullOrWhiteSpace(post.MessageText))
            {
                for (int t = 0; t < candidateTerms.Count; t++)
                {
                    string term = candidateTerms[t];
                    string normTerm = normalizedCandidateTerms[t];
                    if (post.MessageText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(normTerm) && analysis.NormalizedText.Contains(normTerm, StringComparison.OrdinalIgnoreCase)))
                    {
                        termCounts[t]++;
                    }
                }
            }

            if (post.CreatedAt.HasValue)
            {
                int localHour = post.CreatedAt.Value.ToLocalTime().Hour;
                if ((uint)localHour < 24u)
                {
                    hourBuckets[localHour]++;
                }
            }
        }

        List<GroupRoiStat> allGroups = new(groupAccumulators.Count);
        foreach (KeyValuePair<string, (string GroupName, int Total, int HighIntent, int WithPhone, int ZeroComment, long ScoreSum)> kvp in groupAccumulators)
        {
            (string groupName, int total, int highIntent, int withPhone, int zeroComment, long scoreSum) = kvp.Value;
            double avg = total > 0 ? Math.Round((double)scoreSum / total, 1) : 0.0;
            allGroups.Add(new GroupRoiStat(
                GroupId: kvp.Key,
                GroupName: groupName,
                TotalPosts: total,
                HighIntentPosts: highIntent,
                PostsWithPhone: withPhone,
                ZeroCommentPosts: zeroComment,
                AverageIntentScore: avg));
        }

        List<GroupRoiStat> topGroups = [.. allGroups];
        topGroups.Sort(static (a, b) =>
        {
            int cmp = b.PostsWithPhone.CompareTo(a.PostsWithPhone);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = b.HighIntentPosts.CompareTo(a.HighIntentPosts);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = b.AverageIntentScore.CompareTo(a.AverageIntentScore);
            return cmp != 0 ? cmp : b.TotalPosts.CompareTo(a.TotalPosts);
        });
        if (topGroups.Count > 10)
        {
            topGroups.RemoveRange(10, topGroups.Count - 10);
        }

        List<GroupRoiStat> lowGroups = new();
        for (int i = 0; i < allGroups.Count; i++)
        {
            GroupRoiStat g = allGroups[i];
            if (g.TotalPosts >= 3 && g.PostsWithPhone == 0 && g.HighIntentPosts == 0)
            {
                lowGroups.Add(g);
            }
        }

        if (lowGroups.Count == 0 && allGroups.Count > 1)
        {
            for (int i = 0; i < allGroups.Count; i++)
            {
                if (allGroups[i].TotalPosts >= 2 && allGroups[i].PostsWithPhone == 0)
                {
                    lowGroups.Add(allGroups[i]);
                }
            }
        }

        lowGroups.Sort(static (a, b) =>
        {
            int cmp = a.AverageIntentScore.CompareTo(b.AverageIntentScore);
            return cmp != 0 ? cmp : b.TotalPosts.CompareTo(a.TotalPosts);
        });
        if (lowGroups.Count > 10)
        {
            lowGroups.RemoveRange(10, lowGroups.Count - 10);
        }

        List<RepeatAuthorStat> repeatAuthors = new();
        foreach (KeyValuePair<string, (string AuthorUid, string AuthorName, int Count, HashSet<string> Groups)> kvp in authorAccumulators)
        {
            (string authorUid, string authorName, int count, HashSet<string> groups) = kvp.Value;
            if (count >= 2)
            {
                int distinctGroups = groups.Count;
                bool isSeeder = distinctGroups >= 2 || count >= 3;
                repeatAuthors.Add(new RepeatAuthorStat(
                    AuthorUid: authorUid,
                    AuthorName: authorName,
                    PostCount: count,
                    DistinctGroupCount: distinctGroups,
                    IsLikelySeederOrCompetitor: isSeeder));
            }
        }

        repeatAuthors.Sort(static (a, b) =>
        {
            int cmp = b.PostCount.CompareTo(a.PostCount);
            return cmp != 0 ? cmp : b.DistinctGroupCount.CompareTo(a.DistinctGroupCount);
        });
        if (repeatAuthors.Count > 10)
        {
            repeatAuthors.RemoveRange(10, repeatAuthors.Count - 10);
        }

        List<(string Term, int Count)> topTerms = new();
        for (int i = 0; i < candidateTerms.Count; i++)
        {
            if (termCounts[i] > 0)
            {
                topTerms.Add((candidateTerms[i], termCounts[i]));
            }
        }

        topTerms.Sort(static (a, b) => b.Count.CompareTo(a.Count));
        if (topTerms.Count > 10)
        {
            topTerms.RemoveRange(10, topTerms.Count - 10);
        }

        List<(int HourOfDay, int PostCount)> peakHours = new();
        for (int h = 0; h < 24; h++)
        {
            if (hourBuckets[h] > 0)
            {
                peakHours.Add((h, hourBuckets[h]));
            }
        }

        peakHours.Sort(static (a, b) => b.PostCount.CompareTo(a.PostCount));
        if (peakHours.Count > 6)
        {
            peakHours.RemoveRange(6, peakHours.Count - 6);
        }

        int totalPending = Math.Max(0, scrapedPosts.Count - totalCommented);

        return new WorkspaceDataAnalyticsReport(
            TotalScrapedPosts: scrapedPosts.Count,
            TotalPostsWithPhone: totalWithPhone,
            TotalHighIntentPosts: totalHighIntent,
            TotalZeroCommentOpportunities: totalZeroComment,
            TotalCommentedPosts: totalCommented,
            TotalPendingPosts: totalPending,
            TopPerformingGroups: topGroups,
            LowPerformingGroups: lowGroups,
            RepeatAuthors: repeatAuthors,
            TopInquiryTerms: topTerms,
            PeakPostingHours: peakHours);
    }

    /// <inheritdoc />
    public string FormatAnalyticsReportForAi(WorkspaceDataAnalyticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder sb = new(1024);
        _ = sb.AppendLine("=== BÁO CÁO PHÂN TÍCH DỮ LIỆU KHÁCH HÀNG & HIỆU SUẤT HỘI NHÓM (ROI) ===");
        _ = sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Tổng bài đã quét: {report.TotalScrapedPosts} | Có SĐT: {report.TotalPostsWithPhone} | Nhu cầu cao (>=75đ): {report.TotalHighIntentPosts}"));
        _ = sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"- Cơ hội vàng (0 bình luận): {report.TotalZeroCommentOpportunities} | Đã bình luận: {report.TotalCommentedPosts} | Chờ tiếp cận: {report.TotalPendingPosts}"));

        if (report.TopPerformingGroups.Count > 0)
        {
            _ = sb.AppendLine("\n[TOP NHÓM HIỆU QUẢ NHẤT (ROI CAO)]");
            for (int i = 0; i < report.TopPerformingGroups.Count; i++)
            {
                GroupRoiStat g = report.TopPerformingGroups[i];
                _ = sb.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {i + 1}. {g.GroupName} (ID: {g.GroupId}): {g.TotalPosts} bài | {g.PostsWithPhone} bài có SĐT | {g.HighIntentPosts} bài nóng | Điểm TB: {g.AverageIntentScore:F1}"));
            }
        }

        if (report.LowPerformingGroups.Count > 0)
        {
            _ = sb.AppendLine("\n[NHÓM TƯƠNG TÁC THẤP / NHIỀU RÁC]");
            for (int i = 0; i < report.LowPerformingGroups.Count; i++)
            {
                GroupRoiStat g = report.LowPerformingGroups[i];
                _ = sb.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  - {g.GroupName} (ID: {g.GroupId}): {g.TotalPosts} bài nhưng 0 SĐT, 0 bài nóng (Điểm TB: {g.AverageIntentScore:F1})"));
            }
        }

        if (report.RepeatAuthors.Count > 0)
        {
            _ = sb.AppendLine("\n[TÁC GIẢ ĐĂNG LẶP LẠI / NGHI VẤN SEEDING & ĐỐI THỦ]");
            for (int i = 0; i < report.RepeatAuthors.Count; i++)
            {
                RepeatAuthorStat a = report.RepeatAuthors[i];
                string badge = a.IsLikelySeederOrCompetitor ? "Nghi vấn Seeding/Môi giới" : "Đăng nhiều lần";
                _ = sb.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  - {a.AuthorName} (UID: {a.AuthorUid}): {a.PostCount} bài trên {a.DistinctGroupCount} nhóm [{badge}]"));
            }
        }

        if (report.TopInquiryTerms.Count > 0)
        {
            _ = sb.AppendLine("\n[TỪ KHÓA NHU CẦU XUẤT HIỆN NHIỀU NHẤT]");
            for (int i = 0; i < report.TopInquiryTerms.Count; i++)
            {
                (string term, int count) = report.TopInquiryTerms[i];
                _ = sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  - \"{term}\": {count} bài"));
            }
        }

        if (report.PeakPostingHours.Count > 0)
        {
            _ = sb.AppendLine("\n[KHUNG GIỜ VÀNG KHÁCH ĐĂNG BÀI]");
            for (int i = 0; i < report.PeakPostingHours.Count; i++)
            {
                (int hourOfDay, int postCount) = report.PeakPostingHours[i];
                _ = sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  - {hourOfDay:D2}:00 - {hourOfDay:D2}:59: {postCount} bài"));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private CachedTextAnalysis GetOrAnalyzeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return s_emptyAnalysis;
        }

        if (s_analysisCache.TryGetValue(text, out CachedTextAnalysis? cached))
        {
            return cached;
        }

        string normalizedText = this.RemoveVietnameseDiacritics(text);
        IReadOnlyList<string> extractedPhones = ExtractVietnamesePhoneNumbersCore(text);
        bool hasInquiry =
            text.Contains('?', StringComparison.Ordinal) ||
            text.Contains("cần", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tìm", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mua", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("tư vấn", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("looking for", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("price", StringComparison.OrdinalIgnoreCase);

        int builtInMatches = 0;
        for (int i = 0; i < s_builtInCommercialSignalsNormalized.Length; i++)
        {
            if (normalizedText.Contains(s_builtInCommercialSignalsNormalized[i], StringComparison.OrdinalIgnoreCase))
            {
                builtInMatches++;
            }
        }

        CachedTextAnalysis analysis = new(normalizedText, extractedPhones, hasInquiry, builtInMatches);
        if (s_analysisCache.Count > 1500)
        {
            int evicted = 0;
            foreach (string key in s_analysisCache.Keys)
            {
                _ = s_analysisCache.TryRemove(key, out _);
                if (++evicted >= 150)
                {
                    break;
                }
            }
        }

        s_analysisCache[text] = analysis;
        return analysis;
    }

    private static IReadOnlyList<string> ExtractVietnamesePhoneNumbersCore(string text)
    {

        // Step 1: Normalize Unicode fancy/enclosed/keycap/fullwidth/mathematical digits to ASCII '0'..'9'
        // and strip keycap variation selectors / zero-width joiners.
        StringBuilder normSb = new(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            int val = rune.Value;
            if (val is 0xFE0F or 0xFE0E or 0x20E3 or 0x200B or 0x200C or 0x200D)
            {
                continue;
            }

            if (TryMapRuneToAsciiDigit(rune, out char asciiDigit))
            {
                _ = normSb.Append(asciiDigit);
            }
            else if (rune.IsBmp)
            {
                _ = normSb.Append((char)val);
            }
            else
            {
                // Non-BMP emoji / icon separator (e.g. 🔥, 👉, 🔸)
                _ = normSb.Append('~');
            }
        }

        // Step 2: Convert letter 'O' / 'o' lookalikes to '0' when they form a standalone 'O'/'o' token
        // or are part of a letter-run consisting solely of 'O'/'o'.
        char[] buf = normSb.ToString().ToCharArray();
        int idx = 0;
        while (idx < buf.Length)
        {
            if (!char.IsLetter(buf[idx]))
            {
                idx++;
                continue;
            }

            int start = idx;
            bool allOh = true;
            while (idx < buf.Length && char.IsLetter(buf[idx]))
            {
                if (buf[idx] is not ('O' or 'o'))
                {
                    allOh = false;
                }

                idx++;
            }

            if (allOh)
            {
                for (int k = start; k < idx; k++)
                {
                    buf[k] = '0';
                }
            }
        }

        // Step 3: Tokenize into Digits (including Vietnamese spoken digit words), SoftSeparators, and HardBoundaries.
        List<PhoneToken> tokens = new(16);
        int pos = 0;
        while (pos < buf.Length)
        {
            if (char.IsAsciiDigit(buf[pos]))
            {
                int start = pos;
                while (pos < buf.Length && char.IsAsciiDigit(buf[pos]))
                {
                    pos++;
                }

                AppendDigitToken(tokens, new string(buf, start, pos - start));
            }
            else if (char.IsLetter(buf[pos]))
            {
                int start = pos;
                while (pos < buf.Length && char.IsLetter(buf[pos]))
                {
                    pos++;
                }

                string word = new(buf, start, pos - start);
                if (TryParseVietnameseSpokenDigit(word, out char spokenDigit))
                {
                    AppendDigitToken(tokens, spokenDigit.ToString());
                }
                else
                {
                    tokens.Add(new PhoneToken(PhoneTokenKind.HardBoundary, word));
                }
            }
            else
            {
                int start = pos;
                bool hasNewline = false;
                while (pos < buf.Length && !char.IsAsciiDigit(buf[pos]) && !char.IsLetter(buf[pos]))
                {
                    if (buf[pos] is '\r' or '\n')
                    {
                        hasNewline = true;
                    }

                    pos++;
                }

                string sep = new(buf, start, pos - start);
                PhoneTokenKind kind = (hasNewline || sep.Length > 8)
                    ? PhoneTokenKind.HardBoundary
                    : PhoneTokenKind.SoftSeparator;
                tokens.Add(new PhoneToken(kind, sep));
            }
        }

        // Step 4: Scan token stream for valid 10-digit mobile and 11-digit landline numbers with false-positive guards.
        List<string> results = [];
        bool rawHasPriceZeroes = text.Contains(".000.000", StringComparison.Ordinal) ||
                                 text.Contains(",000,000", StringComparison.Ordinal);

        StringBuilder rawDigitsSb = new(16);
        StringBuilder rawSliceSb = new(24);

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != PhoneTokenKind.Digits)
            {
                continue;
            }

            _ = rawDigitsSb.Clear();
            _ = rawSliceSb.Clear();

            for (int j = i; j < tokens.Count; j++)
            {
                if (j > i)
                {
                    if (tokens[j].Kind == PhoneTokenKind.HardBoundary)
                    {
                        break;
                    }

                    if (tokens[j].Kind == PhoneTokenKind.SoftSeparator)
                    {
                        _ = rawSliceSb.Append(tokens[j].Text);
                        continue;
                    }
                }

                if (tokens[j].Kind != PhoneTokenKind.Digits)
                {
                    break;
                }

                _ = rawDigitsSb.Append(tokens[j].Text);
                _ = rawSliceSb.Append(tokens[j].Text);

                CandidateState state = EvaluatePhoneCandidate(rawDigitsSb.ToString(), out string? normalizedPhone);
                if (state == CandidateState.Invalid)
                {
                    break;
                }

                if (state == CandidateState.Incomplete)
                {
                    continue;
                }

                // state == CandidateState.Valid
                string candidate = normalizedPhone!;

                // Guard 1: Check if followed by trailing digits that are part of the same longer number (not a separate phone number)
                if (j + 2 < tokens.Count &&
                    tokens[j + 1].Kind == PhoneTokenKind.SoftSeparator &&
                    tokens[j + 2].Kind == PhoneTokenKind.Digits &&
                    !CanStartValidPhoneAt(tokens, j + 2))
                {
                    string trailingSep = tokens[j + 1].Text;
                    bool tightContinuation = trailingSep is "." or "," or "_" or "" ||
                                             (j > i && trailingSep == " ");
                    if (tightContinuation)
                    {
                        i = j + 2;
                        break;
                    }
                }

                // Guard 2: Reject price patterns like 090.000.000 / 0900.000.000
                string rawSlice = rawSliceSb.ToString();
                if (candidate.EndsWith("000000", StringComparison.Ordinal) &&
                    (rawHasPriceZeroes ||
                     rawSlice.Contains(".000.000", StringComparison.Ordinal) ||
                     rawSlice.Contains(",000,000", StringComparison.Ordinal)))
                {
                    break;
                }

                if (!results.Contains(candidate))
                {
                    results.Add(candidate);
                }

                i = j;
                break;
            }
        }

        return results;
    }

    private enum PhoneTokenKind
    {
        Digits,
        SoftSeparator,
        HardBoundary
    }

    private enum CandidateState
    {
        Incomplete,
        Valid,
        Invalid
    }

    private readonly record struct PhoneToken(PhoneTokenKind Kind, string Text);

    private static void AppendDigitToken(List<PhoneToken> tokens, string digits)
    {
        if (tokens.Count > 0 && tokens[^1].Kind == PhoneTokenKind.Digits)
        {
            tokens.Add(new PhoneToken(PhoneTokenKind.SoftSeparator, string.Empty));
        }

        tokens.Add(new PhoneToken(PhoneTokenKind.Digits, digits));
    }

    private static bool CanStartValidPhoneAt(List<PhoneToken> tokens, int startIndex)
    {
        StringBuilder sb = new(16);
        for (int j = startIndex; j < tokens.Count; j++)
        {
            if (j > startIndex)
            {
                if (tokens[j].Kind == PhoneTokenKind.HardBoundary)
                {
                    break;
                }

                if (tokens[j].Kind == PhoneTokenKind.SoftSeparator)
                {
                    continue;
                }
            }

            if (tokens[j].Kind != PhoneTokenKind.Digits)
            {
                break;
            }

            _ = sb.Append(tokens[j].Text);
            CandidateState state = EvaluatePhoneCandidate(sb.ToString(), out _);
            if (state == CandidateState.Valid)
            {
                return true;
            }

            if (state == CandidateState.Invalid)
            {
                return false;
            }
        }

        return false;
    }

    private static CandidateState EvaluatePhoneCandidate(string digits, out string? normalizedPhone)
    {
        normalizedPhone = null;
        if (string.IsNullOrEmpty(digits))
        {
            return CandidateState.Invalid;
        }

        if (digits[0] == '0')
        {
            if (digits.Length == 1)
            {
                return CandidateState.Incomplete;
            }

            char second = digits[1];
            if (second is '3' or '5' or '7' or '8' or '9')
            {
                if (digits.Length < 10)
                {
                    return CandidateState.Incomplete;
                }

                if (digits.Length == 10)
                {
                    normalizedPhone = digits;
                    return CandidateState.Valid;
                }

                return CandidateState.Invalid;
            }

            if (second == '2')
            {
                if (digits.Length < 11)
                {
                    return CandidateState.Incomplete;
                }

                if (digits.Length == 11)
                {
                    normalizedPhone = digits;
                    return CandidateState.Valid;
                }

                return CandidateState.Invalid;
            }

            if (second == '0')
            {
                const string intlPrefix = "0084";
                int prefixCheckLen = Math.Min(digits.Length, intlPrefix.Length);
                if (!digits.AsSpan(0, prefixCheckLen).SequenceEqual(intlPrefix.AsSpan(0, prefixCheckLen)))
                {
                    return CandidateState.Invalid;
                }

                if (digits.Length <= intlPrefix.Length)
                {
                    return CandidateState.Incomplete;
                }

                return EvaluateSubscriberAfterCountryCode(digits[intlPrefix.Length..], out normalizedPhone);
            }

            return CandidateState.Invalid;
        }

        if (digits[0] == '8')
        {
            if (digits.Length == 1)
            {
                return CandidateState.Incomplete;
            }

            if (digits[1] != '4')
            {
                return CandidateState.Invalid;
            }

            if (digits.Length == 2)
            {
                return CandidateState.Incomplete;
            }

            return EvaluateSubscriberAfterCountryCode(digits[2..], out normalizedPhone);
        }

        return CandidateState.Invalid;
    }

    private static CandidateState EvaluateSubscriberAfterCountryCode(string rest, out string? normalizedPhone)
    {
        normalizedPhone = null;
        if (rest.Length == 0)
        {
            return CandidateState.Incomplete;
        }

        if (rest[0] == '0')
        {
            if (rest.Length == 1)
            {
                return CandidateState.Incomplete;
            }

            char sub = rest[1];
            if (sub is '3' or '5' or '7' or '8' or '9')
            {
                if (rest.Length < 10)
                {
                    return CandidateState.Incomplete;
                }

                if (rest.Length == 10)
                {
                    normalizedPhone = rest;
                    return CandidateState.Valid;
                }

                return CandidateState.Invalid;
            }

            if (sub == '2')
            {
                if (rest.Length < 11)
                {
                    return CandidateState.Incomplete;
                }

                if (rest.Length == 11)
                {
                    normalizedPhone = rest;
                    return CandidateState.Valid;
                }

                return CandidateState.Invalid;
            }

            return CandidateState.Invalid;
        }

        if (rest[0] is '3' or '5' or '7' or '8' or '9')
        {
            if (rest.Length < 9)
            {
                return CandidateState.Incomplete;
            }

            if (rest.Length == 9)
            {
                normalizedPhone = "0" + rest;
                return CandidateState.Valid;
            }

            return CandidateState.Invalid;
        }

        if (rest[0] == '2')
        {
            if (rest.Length < 10)
            {
                return CandidateState.Incomplete;
            }

            if (rest.Length == 10)
            {
                normalizedPhone = "0" + rest;
                return CandidateState.Valid;
            }

            return CandidateState.Invalid;
        }

        return CandidateState.Invalid;
    }

    private static bool TryMapRuneToAsciiDigit(Rune rune, out char digit)
    {
        int val = rune.Value;
        if (val is >= '0' and <= '9')
        {
            digit = (char)val;
            return true;
        }

        if (val is >= 0xFF10 and <= 0xFF19)
        {
            digit = (char)('0' + (val - 0xFF10));
            return true;
        }

        if (val is 0x24EA or 0x24FF)
        {
            digit = '0';
            return true;
        }

        if (val is >= 0x2460 and <= 0x2468)
        {
            digit = (char)('1' + (val - 0x2460));
            return true;
        }

        if (val is >= 0x24F5 and <= 0x24FD)
        {
            digit = (char)('1' + (val - 0x24F5));
            return true;
        }

        if (val is >= 0x2776 and <= 0x277E)
        {
            digit = (char)('1' + (val - 0x2776));
            return true;
        }

        if (val is >= 0x2780 and <= 0x2788)
        {
            digit = (char)('1' + (val - 0x2780));
            return true;
        }

        if (val is >= 0x278A and <= 0x2792)
        {
            digit = (char)('1' + (val - 0x278A));
            return true;
        }

        if (val is >= 0x1D7CE and <= 0x1D7FF)
        {
            digit = (char)('0' + ((val - 0x1D7CE) % 10));
            return true;
        }

        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber)
        {
            int dec = CharUnicodeInfo.GetDecimalDigitValue(rune.ToString(), 0);
            if (dec is >= 0 and <= 9)
            {
                digit = (char)('0' + dec);
                return true;
            }
        }

        digit = '\0';
        return false;
    }

    private static bool TryParseVietnameseSpokenDigit(string word, out char digit)
    {
        switch (word.ToLowerInvariant())
        {
            case "không":
            case "khong":
            case "ko":
                digit = '0';
                return true;
            case "một":
            case "mot":
            case "mốt":
                digit = '1';
                return true;
            case "hai":
                digit = '2';
                return true;
            case "ba":
                digit = '3';
                return true;
            case "bốn":
            case "bon":
            case "tư":
                digit = '4';
                return true;
            case "năm":
            case "nam":
            case "lăm":
            case "lam":
                digit = '5';
                return true;
            case "sáu":
            case "sau":
                digit = '6';
                return true;
            case "bảy":
            case "bay":
            case "bẩy":
                digit = '7';
                return true;
            case "tám":
            case "tam":
                digit = '8';
                return true;
            case "chín":
            case "chin":
                digit = '9';
                return true;
            default:
                digit = '\0';
                return false;
        }
    }

    private static bool MatchesExtractedPhones(IReadOnlyList<string> extractedPhones, string keyword)
    {
        if (extractedPhones.Count == 0 || string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        StringBuilder kwDigitsSb = new(keyword.Length);
        for (int i = 0; i < keyword.Length; i++)
        {
            if (char.IsAsciiDigit(keyword[i]))
            {
                _ = kwDigitsSb.Append(keyword[i]);
            }
            else if (char.IsLetter(keyword[i]))
            {
                return false;
            }
        }

        string kwDigits = kwDigitsSb.ToString();
        if (kwDigits.Length < 3)
        {
            return false;
        }

        for (int i = 0; i < extractedPhones.Count; i++)
        {
            if (extractedPhones[i].Contains(kwDigits, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
