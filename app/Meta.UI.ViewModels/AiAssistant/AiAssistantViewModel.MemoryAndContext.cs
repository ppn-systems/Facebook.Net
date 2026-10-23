// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Meta.UI.Core.AiAssistant;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Leads;

namespace Meta.UI.ViewModels.AiAssistant;

public sealed partial class AiAssistantViewModel
{
    /// <summary>
    /// Applies a built-in specialized System Prompt preset (strictly in token-efficient English) and persists AI settings.
    /// </summary>
    public void ApplySystemPromptPreset(string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            return;
        }

        string presetPrompt = presetKey.Trim().ToLowerInvariant() switch
        {
            "closing_expert" =>
                "You are a Sales Closing and Customer Consultation Expert for Facebook Groups. Always reply in the exact language used by the user. When analyzing a customer post, evaluate genuine buyer intent, distinguish real buyers from brokers or seeders, suggest an appropriate customer color tag, and draft a natural personalized consultation comment. Silently record product info, pricing, hotline, or consultation rules via [LEARN:Category|Key|Content] at the end of your reply. When proposing a consultation comment, append [ACTION:COMMENT_LEAD|targetId=<postId>|content=<comment text>].",
            "viral_copywriter" =>
                "You are a Viral Sales Copywriter and Anti-Checkpoint Spintax Specialist for Facebook Groups. Always reply in the exact language used by the user. Craft engaging, well-structured posts using natural Spintax {Option 1|Option 2|Option 3} across the hook, body, and call-to-action for safe multi-group publishing. Silently remember brand voice, product USPs, and hotline details via [LEARN:WritingStyle|Key|Content]. When a post draft is ready, append [ACTION:PUBLISH_GROUPS|count=<groups>|minDelay=<sec>|maxDelay=<sec>|content=<Spintax post>].",
            "data_analyst" =>
                "You are a Workspace Data Analyst and Auto-Tagging Specialist. Always reply in the exact language used by the user. Proactively analyze deep workspace metrics, evaluate group ROI, detect repeat authors or seeders, and identify peak posting hours and top inquiry terms. When asked to create or assign color tags for Groups or Customers, use the MCP tools and append [ACTION:FILTER_LEADS|keyword=<keyword>] or [ACTION:OPEN_GROUP_TAGS|targetId=<tagId>].",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(presetPrompt))
        {
            return;
        }

        this.SystemPromptText = presetPrompt;
        _ = this.SaveSystemPromptAsync();
    }

    /// <summary>
    /// Silently records a learned insight from user behavior in the background without any UI banner or popup noise.
    /// </summary>
    public Task LearnFromUserActionSilentlyAsync(
        string category,
        string key,
        string content,
        CancellationToken ct = default)
    {
        AiMemoryCategory mapped = MapStringCategory(category);
        return this.LearnFromUserActionSilentlyAsync(mapped, key, content, ct);
    }

    /// <summary>
    /// Silently records a learned insight from user behavior in the background without any UI banner or popup noise.
    /// </summary>
    public async Task LearnFromUserActionSilentlyAsync(
        AiMemoryCategory category,
        string key,
        string content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        try
        {
            if (!_isMemoryStoreInitialized)
            {
                await _context.AiMemoryStore.InitializeAsync(ct).ConfigureAwait(true);
                _isMemoryStoreInitialized = true;
            }

            string? activeUid = _context.Settings.Current.LastSelectedAccountUid;
            _ = await _context.AiMemoryStore
                .SaveMemoryAsync(category, key.Trim(), content.Trim(), importance: 4, accountUid: activeUid, ct: ct)
                .ConfigureAwait(true);

            await this.ReloadMemoriesAsync(ct).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Silent background learning must never interrupt the user workflow.
        }
    }

    private static AiMemoryCategory MapStringCategory(string? rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
        {
            return AiMemoryCategory.General;
        }

        if (Enum.TryParse(rawCategory.Trim(), ignoreCase: true, out AiMemoryCategory parsed))
        {
            return parsed;
        }

        return rawCategory.Trim().ToLowerInvariant() switch
        {
            "lead_tag_preference" or "lead_preferences" or "lead_playbook" or "lead" => AiMemoryCategory.LeadPlaybook,
            "brand_voice" or "writing_style" or "style" => AiMemoryCategory.WritingStyle,
            "campaign_rules" or "campaign_rule" or "rule" => AiMemoryCategory.CampaignRule,
            "group_strategy" or "group" => AiMemoryCategory.GroupStrategy,
            "product_knowledge" or "product" => AiMemoryCategory.ProductKnowledge,
            "user_preference" or "preference" => AiMemoryCategory.UserPreference,
            _ => AiMemoryCategory.General
        };
    }

    private async Task ClearAllMemoriesAsync()
    {
        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        _ = await _context.AiMemoryStore.ClearMemoriesAsync().ConfigureAwait(true);
        this.Memories.Clear();
    }

    private async Task SaveSystemPromptAsync()
    {
        string normalized = this.SystemPromptText?.Trim() ?? string.Empty;
        this.SystemPromptText = normalized;
        await _context.Settings.SaveAsync(_context.Settings.Current with
        {
            AiSystemPrompt = normalized,
            AiModel = this.SelectedAiModel,
            AiReasoningEffort = this.SelectedAiEffort
        }).ConfigureAwait(true);
        this.StatusBannerText = _context.Localization["Ai.SystemPromptSavedStatus"];
    }

    private async Task ResetSystemPromptAsync()
    {
        this.SystemPromptText = string.Empty;
        await _context.Settings.SaveAsync(_context.Settings.Current with { AiSystemPrompt = string.Empty }).ConfigureAwait(true);
        this.StatusBannerText = _context.Localization["Ai.SystemPromptSavedStatus"];
    }

    private async Task SaveMemoryAsync()
    {
        string key = this.NewMemoryKey.Trim();
        string content = this.NewMemoryContent.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (!_isMemoryStoreInitialized)
        {
            await _context.AiMemoryStore.InitializeAsync().ConfigureAwait(true);
            _isMemoryStoreInitialized = true;
        }

        string? activeUid = _context.Settings.Current.LastSelectedAccountUid;
        _ = await _context.AiMemoryStore
            .SaveMemoryAsync(this.NewMemoryCategory, key, content, importance: 4, accountUid: activeUid)
            .ConfigureAwait(true);

        this.NewMemoryKey = string.Empty;
        this.NewMemoryContent = string.Empty;
        await this.ReloadMemoriesAsync().ConfigureAwait(true);
    }

    private async Task DeleteMemoryAsync(AiMemoryRecord? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        _ = await _context.AiMemoryStore.DeleteMemoryAsync(entry.Id).ConfigureAwait(true);
        await this.ReloadMemoriesAsync().ConfigureAwait(true);
    }

    private async Task ReloadMemoriesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AiMemoryRecord> list = await _context.AiMemoryStore
            .QueryMemoriesAsync(limit: int.MaxValue, ct: ct)
            .ConfigureAwait(true);
        this.Memories.ReplaceAll(list);
    }

    private async Task TryExtractExplicitMemoryHintAsync(string prompt)
    {
        string? activeUid = _context.Settings.Current.LastSelectedAccountUid;

        string[] prefixes = ["hãy nhớ:", "ghi nhớ:", "nhớ rằng:", "remember:", "lưu vào bộ nhớ:"];
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (prompt.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                string body = prompt[prefixes[i].Length..].Trim();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    string key = body.Length > 36 ? body[..36].Trim() : body;
                    _ = await _context.AiMemoryStore.SaveMemoryAsync(
                        AiMemoryCategory.UserPreference,
                        key,
                        body,
                        importance: 4,
                        accountUid: activeUid).ConfigureAwait(true);
                }

                break;
            }
        }

        if (ContainsSafeDelayRule(prompt))
        {
            _ = await _context.AiMemoryStore.SaveMemoryAsync(
                AiMemoryCategory.CampaignRule,
                "safe_delay_rule",
                prompt,
                importance: 4,
                accountUid: activeUid).ConfigureAwait(true);
        }

        if (IsPublishPromptIntent(prompt))
        {
            string? quotedPost = TryExtractQuotedText(prompt);
            if (!string.IsNullOrWhiteSpace(quotedPost))
            {
                _ = await _context.AiMemoryStore.SaveMemoryAsync(
                    AiMemoryCategory.ProductKnowledge,
                    "recent_campaign",
                    quotedPost,
                    importance: 4,
                    accountUid: activeUid).ConfigureAwait(true);
            }
        }
    }

    private async Task TryExtractContactAndPricingHintAsync(string prompt, string? activeUid)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        try
        {
            string? phone = _context.LeadScoring.ExtractPrimaryPhoneNumber(prompt);
            bool mentionsContact = prompt.Contains("zalo", StringComparison.OrdinalIgnoreCase) ||
                                   prompt.Contains("hotline", StringComparison.OrdinalIgnoreCase) ||
                                   prompt.Contains("sđt", StringComparison.OrdinalIgnoreCase) ||
                                   prompt.Contains("liên hệ", StringComparison.OrdinalIgnoreCase) ||
                                   prompt.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
                                   prompt.Contains("contact", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(phone) && mentionsContact)
            {
                _ = await _context.AiMemoryStore.SaveMemoryAsync(
                    AiMemoryCategory.ProductKnowledge,
                    "contact_hotline",
                    prompt.Length > 200 ? prompt[..200].Trim() : prompt,
                    importance: 4,
                    accountUid: activeUid).ConfigureAwait(true);
            }
            else if (prompt.Contains("bảng giá", StringComparison.OrdinalIgnoreCase) ||
                     prompt.Contains("giá bán", StringComparison.OrdinalIgnoreCase) ||
                     prompt.Contains("pricing", StringComparison.OrdinalIgnoreCase) ||
                     prompt.Contains("price list", StringComparison.OrdinalIgnoreCase))
            {
                _ = await _context.AiMemoryStore.SaveMemoryAsync(
                    AiMemoryCategory.ProductKnowledge,
                    "pricing_policy",
                    prompt.Length > 220 ? prompt[..220].Trim() : prompt,
                    importance: 4,
                    accountUid: activeUid).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Silent extraction fallback
        }
    }

    private async Task<(string? ActiveUid, string? ActiveName, int EnabledTargetGroups, int MinDelay, int MaxDelay, string WorkspaceSnapshot)> BuildRichWorkspaceSnapshotAsync(CancellationToken ct = default)
    {
        AppConfig cfg = _context.Settings.Current;
        string? activeUid = cfg.LastSelectedAccountUid;
        string? activeName = null;
        string activeStatus = "None";

        IReadOnlyList<AccountProfile> accounts = await _context.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(true);
        for (int i = 0; i < accounts.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(activeUid) &&
                string.Equals(accounts[i].Uid, activeUid, StringComparison.Ordinal))
            {
                activeName = accounts[i].DisplayName;
                activeStatus = accounts[i].Status.ToString();
                break;
            }
        }

        if (activeName is null && accounts.Count > 0)
        {
            activeUid = accounts[0].Uid;
            activeName = accounts[0].DisplayName;
            activeStatus = accounts[0].Status.ToString();
        }

        IReadOnlyList<SavedTargetGroup> targets = await _context.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(true);
        int enabledCount = 0;
        List<string> sampleGroupNames = new(8);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].IsEnabled)
            {
                enabledCount++;
                if (sampleGroupNames.Count < 8 && !string.IsNullOrWhiteSpace(targets[i].GroupName))
                {
                    sampleGroupNames.Add(targets[i].GroupName);
                }
            }
        }

        IReadOnlyList<GroupTag> tags = await _context.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(true);
        List<string> tagNames = new(tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(tags[i].Name))
            {
                tagNames.Add($"{tags[i].Name} ({tags[i].Id})");
            }
        }

        IReadOnlyList<GroupTag> leadTags = await _context.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(true);
        List<string> leadTagNames = new(leadTags.Count);
        for (int i = 0; i < leadTags.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(leadTags[i].Name))
            {
                leadTagNames.Add($"{leadTags[i].Name} ({leadTags[i].Id})");
            }
        }

        IReadOnlyList<CampaignTemplate> templates = await _context.AccountStore.GetAllCampaignTemplatesAsync(ct).ConfigureAwait(true);

        IReadOnlyList<PostSummary> allScraped = this.Shell is { AllScrapedPosts.Count: > 0 }
            ? this.Shell.AllScrapedPosts
            : await _context.AccountStore.QueryScrapedPostsAsync(null, null, 300, ct).ConfigureAwait(true);

        IReadOnlySet<string>? commentedSet = this.Shell?.CommentedPostIds;
        string? buyerKeywords = this.Shell?.BuyerIntentKeywords;
        WorkspaceDataAnalyticsReport analyticsReport = _context.LeadScoring.AnalyzeWorkspaceData(
            allScraped,
            targets,
            commentedSet,
            buyerKeywords);

        StringBuilder sb = new();
        _ = sb.AppendLine("[CURRENT WORKSPACE STATE]");
        _ = sb.Append(CultureInfo.InvariantCulture, $"- Active Facebook account: {activeName ?? "None"} (Status: {activeStatus})").AppendLine();
        _ = sb.Append(CultureInfo.InvariantCulture, $"- Enabled target groups count: {enabledCount} / {targets.Count} total").AppendLine();
        if (sampleGroupNames.Count > 0)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"- Sample enabled target groups: {string.Join(", ", sampleGroupNames)}").AppendLine();
        }

        if (tagNames.Count > 0)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"- Group color tags: {string.Join(", ", tagNames)}").AppendLine();
        }

        if (leadTagNames.Count > 0)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"- Customer/Lead color tags: {string.Join(", ", leadTagNames)}").AppendLine();
        }

        _ = sb.Append(CultureInfo.InvariantCulture, $"- Scraped customer leads summary: {analyticsReport.TotalScrapedPosts} total ({analyticsReport.TotalPostsWithPhone} with phone, {analyticsReport.TotalHighIntentPosts} high-intent, {analyticsReport.TotalPendingPosts} pending reply)").AppendLine();
        _ = sb.Append(CultureInfo.InvariantCulture, $"- Safe anti-checkpoint delay: {cfg.MinDelaySeconds}s - {cfg.MaxDelaySeconds}s");

        if (templates.Count > 0)
        {
            _ = sb.AppendLine().AppendLine().AppendLine("[SAVED TEMPLATES]");
            int maxTemplates = Math.Min(6, templates.Count);
            for (int i = 0; i < maxTemplates; i++)
            {
                CampaignTemplate tpl = templates[i];
                string snippet = (tpl.SpintaxContent ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (snippet.Length > 140)
                {
                    snippet = string.Concat(snippet[..140], "...");
                }

                _ = sb.Append(CultureInfo.InvariantCulture, $"- {tpl.Title}: {snippet}");
                if (i + 1 < maxTemplates)
                {
                    _ = sb.AppendLine();
                }
            }
        }

        // Attached Active Context Packs (+ Nạp Context)
        if (this.IsContextPackEnabled("selected_lead"))
        {
            PostSummary? selectedPost = this.Shell?.SelectedScrapedPost;
            if (selectedPost is null && allScraped.Count > 0)
            {
                selectedPost = allScraped[0];
            }

            if (selectedPost is not null)
            {
                IReadOnlyList<string> phones = _context.LeadScoring.ExtractVietnamesePhoneNumbers(selectedPost.MessageText);
                int score = _context.LeadScoring.CalculateBuyerIntentScore(selectedPost.MessageText, buyerKeywords);
                _ = sb.AppendLine().AppendLine().AppendLine("[ATTACHED CONTEXT: SELECTED CUSTOMER POST]");
                _ = sb.Append(CultureInfo.InvariantCulture, $"- PostId: {selectedPost.PostId}").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Author: {selectedPost.AuthorName} (UID: {selectedPost.AuthorUid ?? "N/A"})").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Group: {selectedPost.GroupName} (GroupId: {selectedPost.GroupId})").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Buyer Intent Score: {score}/100 | Phones: {(phones.Count > 0 ? string.Join(", ", phones) : "None")}").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Assigned Lead Tags: {(string.IsNullOrWhiteSpace(selectedPost.TagsCsv) ? "None" : selectedPost.TagsCsv)}").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Customer CRM Notes: {(string.IsNullOrWhiteSpace(selectedPost.Notes) ? "None" : selectedPost.Notes)}").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Engagement: {selectedPost.ReactionCount} reactions, {selectedPost.CommentCount} comments").AppendLine();
                _ = sb.Append(CultureInfo.InvariantCulture, $"- Full Message: {selectedPost.MessageText}");
            }
        }

        if (this.IsContextPackEnabled("top_hot_leads") && allScraped.Count > 0)
        {
            _ = sb.AppendLine().AppendLine().AppendLine("[ATTACHED CONTEXT: TOP 25 HOT CUSTOMER LEADS]");
            List<(PostSummary Post, int Score, string? Phone)> ranked = new(allScraped.Count);
            for (int i = 0; i < allScraped.Count; i++)
            {
                PostSummary p = allScraped[i];
                int score = _context.LeadScoring.CalculateBuyerIntentScore(p.MessageText, buyerKeywords);
                string? phone = _context.LeadScoring.ExtractPrimaryPhoneNumber(p.MessageText);
                ranked.Add((p, score, phone));
            }

            ranked.Sort((a, b) =>
            {
                int hasPhoneA = string.IsNullOrWhiteSpace(a.Phone) ? 0 : 1;
                int hasPhoneB = string.IsNullOrWhiteSpace(b.Phone) ? 0 : 1;
                int cmp = hasPhoneB.CompareTo(hasPhoneA);
                return cmp != 0 ? cmp : b.Score.CompareTo(a.Score);
            });

            int maxLeads = Math.Min(25, ranked.Count);
            for (int i = 0; i < maxLeads; i++)
            {
                (PostSummary p, int score, string? phone) = ranked[i];
                string snippet = (p.MessageText ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (snippet.Length > 120)
                {
                    snippet = string.Concat(snippet[..120], "...");
                }

                _ = sb.Append(
                    CultureInfo.InvariantCulture,
                    $"- [{p.PostId}] {p.AuthorName} @ {p.GroupName} | Score={score} | Phone={phone ?? "None"} | Tags={p.TagsCsv}: {snippet}").AppendLine();
            }
        }

        if (this.IsContextPackEnabled("deep_analytics"))
        {
            _ = sb.AppendLine().AppendLine().Append(_context.LeadScoring.FormatAnalyticsReportForAi(analyticsReport));
        }

        if (this.IsContextPackEnabled("all_groups_tags"))
        {
            _ = sb.AppendLine().AppendLine().AppendLine("[ATTACHED CONTEXT: ALL TARGET GROUPS & TAGS]");
            int maxGroups = Math.Min(80, targets.Count);
            for (int i = 0; i < maxGroups; i++)
            {
                SavedTargetGroup g = targets[i];
                _ = sb.Append(
                    CultureInfo.InvariantCulture,
                    $"- GroupId={g.GroupId} | Name={g.GroupName} | Enabled={g.IsEnabled} | Tags={g.TagsCsv}").AppendLine();
            }
        }

        if (this.IsContextPackEnabled("templates_posts"))
        {
            _ = sb.AppendLine().AppendLine().AppendLine("[ATTACHED CONTEXT: SPINTAX TEMPLATES & RECENT PUBLISHED POSTS]");
            int maxTpl = Math.Min(15, templates.Count);
            for (int i = 0; i < maxTpl; i++)
            {
                CampaignTemplate tpl = templates[i];
                string body = (tpl.SpintaxContent ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (body.Length > 200)
                {
                    body = string.Concat(body[..200], "...");
                }

                _ = sb.Append(CultureInfo.InvariantCulture, $"- Template [{tpl.Id}] {tpl.Title}: {body}").AppendLine();
            }

            IReadOnlyList<PublishedPostRecord> pubPosts = await _context.AccountStore
                .GetPublishedPostsAsync(activeUid, 15, ct)
                .ConfigureAwait(true);
            for (int i = 0; i < pubPosts.Count; i++)
            {
                PublishedPostRecord pub = pubPosts[i];
                string preview = (pub.MessageText ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (preview.Length > 140)
                {
                    preview = string.Concat(preview[..140], "...");
                }

                _ = sb.Append(
                    CultureInfo.InvariantCulture,
                    $"- Published [{pub.PostId}] @ {pub.TargetGroupName ?? "Profile"} ({pub.CreatedAt:dd/MM HH:mm}): {preview}").AppendLine();
            }
        }

        int minDelayInt = Math.Max(0, (int)Math.Round(cfg.MinDelaySeconds));
        int maxDelayInt = Math.Max(minDelayInt, (int)Math.Round(cfg.MaxDelaySeconds));
        return (activeUid, activeName, enabledCount, minDelayInt, maxDelayInt, sb.ToString());
    }

    private AgyProposedAction? DetectFallbackProposedAction(
        string userPrompt,
        string assistantResponse,
        int enabledTargetGroups,
        int minDelay,
        int maxDelay)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || string.IsNullOrWhiteSpace(assistantResponse))
        {
            return null;
        }

        if (IsPublishPromptIntent(userPrompt))
        {
            string? quotedContent = TryExtractQuotedText(userPrompt);
            bool hasConcreteContent = !string.IsNullOrWhiteSpace(quotedContent) ||
                                      (userPrompt.Trim().Length > 35 && !IsClarifyingQuestionResponse(assistantResponse));

            if (!hasConcreteContent)
            {
                return null;
            }

            int targetCount = TryExtractGroupCount(userPrompt, out int promptGroupCount)
                ? promptGroupCount
                : TryExtractGroupCount(assistantResponse, out int responseGroupCount)
                    ? responseGroupCount
                    : Math.Max(1, enabledTargetGroups);

            string contentText = !string.IsNullOrWhiteSpace(quotedContent)
                ? quotedContent
                : assistantResponse.Trim();

            return new AgyProposedAction(
                ActionKind: "PUBLISH_GROUPS",
                SummaryText: _context.Localization.Format("Ai.FallbackPublishSummary", targetCount, minDelay, maxDelay),
                ContentText: contentText,
                TargetGroupCount: targetCount,
                MinDelaySeconds: minDelay,
                MaxDelaySeconds: maxDelay,
                GroupKeyword: null);
        }

        if (this.Shell?.SelectedScrapedPost is { } selectedLead &&
            !string.IsNullOrWhiteSpace(selectedLead.PostId) &&
            IsCommentLeadPromptIntent(userPrompt) &&
            !IsClarifyingQuestionResponse(assistantResponse))
        {
            string? quotedReply = TryExtractQuotedText(assistantResponse);
            string replyContent = !string.IsNullOrWhiteSpace(quotedReply)
                ? quotedReply
                : assistantResponse.Trim();

            return new AgyProposedAction(
                ActionKind: "COMMENT_LEAD",
                SummaryText: _context.Localization["Ai.ActionCommentLead"],
                ContentText: replyContent,
                TargetGroupCount: 0,
                MinDelaySeconds: minDelay,
                MaxDelaySeconds: maxDelay,
                GroupKeyword: null,
                TargetId: selectedLead.PostId);
        }

        return null;
    }

    private static bool IsCommentLeadPromptIntent(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("bình luận", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("tư vấn", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("phản hồi", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("trả lời", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("chốt khách", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("reply", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("lead", StringComparison.OrdinalIgnoreCase));

    private static bool IsClarifyingQuestionResponse(string assistantResponse) =>
        !string.IsNullOrWhiteSpace(assistantResponse) &&
        assistantResponse.TrimEnd().EndsWith('?');

    private static bool IsPublishPromptIntent(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("đăng bài", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("đăng vào", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("đăng lên", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("publish", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("post to group", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsSafeDelayRule(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool mentionsDelayKeyword =
            text.Contains("delay", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("nghỉ", StringComparison.OrdinalIgnoreCase);

        if (!mentionsDelayKeyword)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExtractQuotedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int firstStraight = text.IndexOf('"', StringComparison.Ordinal);
        if (firstStraight >= 0 && firstStraight + 1 < text.Length)
        {
            int secondStraight = text.IndexOf('"', firstStraight + 1);
            if (secondStraight > firstStraight + 1)
            {
                string extracted = text[(firstStraight + 1)..secondStraight].Trim();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
        }

        int firstCurly = text.IndexOf('\u201C', StringComparison.Ordinal);
        if (firstCurly >= 0 && firstCurly + 1 < text.Length)
        {
            int secondCurly = text.IndexOf('\u201D', firstCurly + 1);
            if (secondCurly > firstCurly + 1)
            {
                string extracted = text[(firstCurly + 1)..secondCurly].Trim();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
        }

        return null;
    }

    private static bool TryExtractGroupCount(string text, out int groupCount)
    {
        groupCount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsDigit(text[i]))
            {
                i++;
                continue;
            }

            int numStart = i;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            ReadOnlySpan<char> numberSpan = text.AsSpan(numStart, i - numStart);
            int afterNumber = i;
            while (afterNumber < text.Length && char.IsWhiteSpace(text[afterNumber]))
            {
                afterNumber++;
            }

            if (afterNumber > i && afterNumber < text.Length)
            {
                ReadOnlySpan<char> tail = text.AsSpan(afterNumber);
                if (tail.StartsWith("group", StringComparison.OrdinalIgnoreCase) ||
                    tail.StartsWith("nhóm", StringComparison.OrdinalIgnoreCase) ||
                    tail.StartsWith("hội nhóm", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(numberSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                        parsed > 0)
                    {
                        groupCount = parsed;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string SerializeProposedAction(AgyProposedAction action)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("actionKind", action.ActionKind);
            writer.WriteString("summaryText", action.SummaryText);
            if (action.ContentText is not null)
            {
                writer.WriteString("contentText", action.ContentText);
            }

            writer.WriteNumber("targetGroupCount", action.TargetGroupCount);
            writer.WriteNumber("minDelaySeconds", action.MinDelaySeconds);
            writer.WriteNumber("maxDelaySeconds", action.MaxDelaySeconds);
            if (action.GroupKeyword is not null)
            {
                writer.WriteString("groupKeyword", action.GroupKeyword);
            }

            if (action.TargetId is not null)
            {
                writer.WriteString("targetId", action.TargetId);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static AgyProposedAction? DeserializeProposedAction(string? actionKind, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(actionKind) && string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new AgyProposedAction(actionKind!, actionKind!);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AgyProposedAction(actionKind ?? "PUBLISH_GROUPS", payloadJson);
            }

            string kind = root.TryGetProperty("actionKind", out JsonElement k) && k.ValueKind == JsonValueKind.String
                ? k.GetString() ?? actionKind ?? "PUBLISH_GROUPS"
                : actionKind ?? "PUBLISH_GROUPS";
            string summary = root.TryGetProperty("summaryText", out JsonElement s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() ?? kind
                : kind;
            string? content = root.TryGetProperty("contentText", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            int groupCount = root.TryGetProperty("targetGroupCount", out JsonElement gc) && gc.TryGetInt32(out int parsedCount)
                ? parsedCount
                : 0;
            int minDelay = root.TryGetProperty("minDelaySeconds", out JsonElement minD) && minD.TryGetInt32(out int parsedMin)
                ? parsedMin
                : 2;
            int maxDelay = root.TryGetProperty("maxDelaySeconds", out JsonElement maxD) && maxD.TryGetInt32(out int parsedMax)
                ? parsedMax
                : 5;
            string? keyword = root.TryGetProperty("groupKeyword", out JsonElement kw) && kw.ValueKind == JsonValueKind.String
                ? kw.GetString()
                : null;
            string? targetId = root.TryGetProperty("targetId", out JsonElement tid) && tid.ValueKind == JsonValueKind.String
                ? tid.GetString()
                : null;

            return new AgyProposedAction(kind, summary, content, groupCount, minDelay, maxDelay, keyword, targetId);
        }
        catch (JsonException)
        {
            return new AgyProposedAction(actionKind ?? "PUBLISH_GROUPS", payloadJson);
        }
    }
}
