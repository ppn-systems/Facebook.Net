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

    /// <summary>Extracts all normalized Vietnamese mobile (10-digit) and landline (11-digit) phone numbers from obfuscated text.</summary>
    IReadOnlyList<string> ExtractVietnamesePhoneNumbers(string? text);

    /// <summary>Extracts the primary (first) normalized Vietnamese phone number from obfuscated text, or <see langword="null"/> if none is found.</summary>
    string? ExtractPrimaryPhoneNumber(string? text);
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

        if (this.ExtractPrimaryPhoneNumber(messageText) is not null)
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
        IReadOnlyList<string> extractedPhones = this.ExtractVietnamesePhoneNumbers(messageText);
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

        string normMsg = !string.IsNullOrWhiteSpace(post.MessageText) ? this.RemoveVietnameseDiacritics(post.MessageText) : string.Empty;
        string normAuthor = !string.IsNullOrWhiteSpace(post.AuthorName) ? this.RemoveVietnameseDiacritics(post.AuthorName) : string.Empty;
        string normGroup = !string.IsNullOrWhiteSpace(post.GroupName) ? this.RemoveVietnameseDiacritics(post.GroupName) : string.Empty;
        IReadOnlyList<string> extractedPhones = this.ExtractVietnamesePhoneNumbers(post.MessageText);

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

    /// <inheritdoc />
    public string? ExtractPrimaryPhoneNumber(string? text)
    {
        IReadOnlyList<string> phones = this.ExtractVietnamesePhoneNumbers(text);
        return phones.Count > 0 ? phones[0] : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractVietnamesePhoneNumbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

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
