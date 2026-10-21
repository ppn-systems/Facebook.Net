// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Configuration;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for AI Context Memory persistence (<c>facebook_ai_memory.db</c>) and campaign context snapshots.
/// </summary>
[McpServerToolType]
public sealed class AiMemoryTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_memory_save")]
    [Description("Save or update a persistent AI context memory record (product info, writing style, campaign rules, group strategy, lead playbook, or user preferences) in SQLite.")]
    public async Task<string> SaveMemoryAsync(
        [Description("Short unique key for the memory (e.g. 'main_product', 'tone_of_voice', 'pricing_policy')")] string key,
        [Description("Detailed content or rule to remember")] string content,
        [Description("Category: 'General', 'UserPreference', 'ProductKnowledge', 'WritingStyle', 'CampaignRule', 'GroupStrategy', or 'LeadPlaybook' (default 'General')")] string category = "General",
        [Description("Importance level from 1 (low) to 5 (critical, default 3)")] int importance = 3,
        [Description("Optional Facebook account UID if this memory is specific to one account")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        AiMemoryCategory parsedCategory = Enum.TryParse<AiMemoryCategory>(category?.Trim(), ignoreCase: true, out AiMemoryCategory cat)
            ? cat
            : AiMemoryCategory.General;

        AiMemoryRecord record = await _manager.AiMemoryStore.SaveMemoryAsync(
            category: parsedCategory,
            key: key.Trim(),
            content: content.Trim(),
            importance: Math.Clamp(importance, 1, 5),
            accountUid: string.IsNullOrWhiteSpace(accountUid) ? null : accountUid.Trim(),
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "saved",
            id = record.Id,
            category = record.Category.ToString(),
            key = record.Key,
            content = record.Content,
            importance = record.Importance,
            account_uid = record.AccountUid,
            updated_at_utc = record.UpdatedAtUtc.ToString("o")
        });
    }

    [McpServerTool(Name = "fb_memory_search")]
    [Description("Search persistent AI context memory records stored in SQLite by keyword, category, or Facebook account UID.")]
    public async Task<string> SearchMemoriesAsync(
        [Description("Optional keyword to match against memory keys or content")] string? keyword = null,
        [Description("Optional category filter: 'General', 'UserPreference', 'ProductKnowledge', 'WritingStyle', 'CampaignRule', 'GroupStrategy', 'LeadPlaybook'")] string? category = null,
        [Description("Optional Facebook account UID filter")] string? accountUid = null,
        [Description("Maximum number of memory records to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        AiMemoryCategory? parsedCategory = !string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<AiMemoryCategory>(category.Trim(), ignoreCase: true, out AiMemoryCategory cat)
            ? cat
            : null;

        IReadOnlyList<AiMemoryRecord> memories = await _manager.AiMemoryStore.QueryMemoriesAsync(
            keyword: keyword,
            category: parsedCategory,
            accountUid: accountUid,
            limit: Math.Clamp(limit, 1, 200),
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            count = memories.Count,
            memories = memories.Select(m => new
            {
                id = m.Id,
                category = m.Category.ToString(),
                key = m.Key,
                content = m.Content,
                importance = m.Importance,
                account_uid = m.AccountUid,
                updated_at_utc = m.UpdatedAtUtc.ToString("o")
            })
        });
    }

    [McpServerTool(Name = "fb_memory_delete")]
    [Description("Delete a persistent AI context memory record by its ID or key.")]
    public async Task<string> DeleteMemoryAsync(
        [Description("Memory ID or exact key to delete")] string idOrKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrKey);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        bool deleted = await _manager.AiMemoryStore.DeleteMemoryAsync(idOrKey.Trim(), ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            status = deleted ? "deleted" : "not_found",
            target = idOrKey.Trim()
        });
    }

    [McpServerTool(Name = "fb_memory_get_context")]
    [Description("Get a full JSON context snapshot of the active Facebook account, stored AI Memory records, target groups, group tags, campaign templates, and safety delay settings.")]
    public async Task<string> GetContextSnapshotAsync(
        [Description("Optional Facebook account UID (defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<AccountProfile> accounts = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
        string? effectiveUid = !string.IsNullOrWhiteSpace(accountUid) ? accountUid.Trim() : _manager.ActiveAccountUid;
        AccountProfile? activeAccount = accounts.FirstOrDefault(a => string.Equals(a.Uid, effectiveUid, StringComparison.Ordinal))
            ?? (accounts.Count > 0 ? accounts[0] : null);

        IReadOnlyList<AiMemoryRecord> memories = await _manager.AiMemoryStore.QueryMemoriesAsync(
            keyword: null,
            category: null,
            accountUid: activeAccount?.Uid,
            limit: 50,
            ct: ct).ConfigureAwait(false);

        IReadOnlyList<SavedTargetGroup> targetGroups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<GroupTag> groupTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<GroupTag> leadTags = await _manager.AccountStore.GetAllLeadTagsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<CampaignTemplate> templates = await _manager.AccountStore.GetAllCampaignTemplatesAsync(ct).ConfigureAwait(false);
        AppConfig cfg = _manager.Settings.Current;

        return JsonSerializer.Serialize(new
        {
            active_account = activeAccount is null
                ? null
                : new
                {
                    uid = activeAccount.Uid,
                    display_name = activeAccount.DisplayName,
                    status = activeAccount.Status.ToString()
                },
            ai_system_prompt = cfg.AiSystemPrompt,
            delay_config = new
            {
                min_delay_seconds = cfg.MinDelaySeconds,
                max_delay_seconds = cfg.MaxDelaySeconds,
                scrape_delay_min_seconds = cfg.ScrapeDelayMinSeconds,
                scrape_delay_max_seconds = cfg.ScrapeDelayMaxSeconds,
                scrape_filter_keywords = cfg.ScrapeFilterKeywords
            },
            memories = memories.Select(m => new
            {
                id = m.Id,
                category = m.Category.ToString(),
                key = m.Key,
                content = m.Content,
                importance = m.Importance
            }),
            target_groups_summary = new
            {
                total_count = targetGroups.Count,
                enabled_count = targetGroups.Count(g => g.IsEnabled),
                groups = targetGroups.Take(50).Select(g => new
                {
                    group_id = g.GroupId,
                    group_name = g.GroupName,
                    is_enabled = g.IsEnabled,
                    tags = g.GetTagIds()
                })
            },
            group_tags = groupTags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex
            }),
            lead_tags = leadTags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex
            }),
            templates = templates.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                is_comment_template = t.IsCommentTemplate
            })
        });
    }
}
