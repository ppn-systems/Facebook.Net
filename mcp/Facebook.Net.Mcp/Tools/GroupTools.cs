// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Community Group Discovery, Joining, Target Group Management, and Color Tagging.
/// Strictly adheres to the safety rule: NEVER invokes LeaveGroupAsync.
/// </summary>
[McpServerToolType]
public sealed class GroupTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_list_target_groups")]
    [Description("List saved campaign target groups from local SQLite, with optional filter by enabled state or color tag ID.")]
    public async Task<string> ListTargetGroupsAsync(
        [Description("Optional filter: true for only enabled groups, false for disabled, null for all")] bool? onlyEnabled = null,
        [Description("Optional color tag ID to filter groups by")] string? tagId = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);

        var filtered = groups
            .Where(g => !onlyEnabled.HasValue || g.IsEnabled == onlyEnabled.Value)
            .Where(g => string.IsNullOrWhiteSpace(tagId) || g.HasTag(tagId))
            .Select(g => new
            {
                group_id = g.GroupId,
                group_name = g.GroupName,
                is_enabled = g.IsEnabled,
                tags = g.GetTagIds(),
                notes = g.Notes,
                avatar_url = g.AvatarUrl
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            total_count = filtered.Count,
            available_tags = allTags.Select(t => new { id = t.Id, name = t.Name, color_hex = t.ColorHex }),
            groups = filtered
        });
    }

    [McpServerTool(Name = "fb_sync_joined_groups")]
    [Description("Fetch all Facebook community groups that the account has joined via Comet GraphQL and optionally save them as target groups.")]
    public async Task<string> SyncJoinedGroupsAsync(
        [Description("Maximum number of joined groups to fetch (default 100)")] int count = 100,
        [Description("Whether to automatically add newly discovered joined groups to the saved Target Groups list")] bool addToTargets = false,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<GroupSummary> result = await _manager.CometClient
            .GetJoinedGroupsAsync(session, count: Math.Clamp(count, 1, 500), ct: ct)
            .ConfigureAwait(false);

        int addedCount = 0;
        if (addToTargets && result.Items.Count > 0)
        {
            IReadOnlyList<SavedTargetGroup> existing = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
            HashSet<string> existingIds = new(existing.Select(x => x.GroupId), StringComparer.Ordinal);

            for (int i = 0; i < result.Items.Count; i++)
            {
                GroupSummary grp = result.Items[i];
                if (!existingIds.Contains(grp.GroupId))
                {
                    await _manager.AccountStore.UpsertTargetGroupAsync(
                        new SavedTargetGroup(grp.GroupId, grp.Name, IsEnabled: true, Notes: null, AvatarUrl: grp.AvatarUrl),
                        ct).ConfigureAwait(false);
                    addedCount++;
                }
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            account,
            "Joined Groups",
            $"MCP synced {result.Items.Count} joined groups.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            account_uid = account.Uid,
            fetched_count = result.Items.Count,
            added_to_targets = addedCount,
            groups = result.Items.Select(g => new
            {
                group_id = g.GroupId,
                name = g.Name,
                membership_state = g.MembershipState.ToString(),
                avatar_url = g.AvatarUrl
            })
        });
    }

    [McpServerTool(Name = "fb_search_groups")]
    [Description("Search Facebook community groups by keyword via Comet GraphQL.")]
    public async Task<string> SearchGroupsAsync(
        [Description("Search keyword to find community groups")] string keyword,
        [Description("Maximum number of groups to return (default 20)")] int count = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<GroupSummary> result = await _manager.CometClient
            .SearchGroupsAsync(session, keyword.Trim(), count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            account_uid = account.Uid,
            keyword = keyword.Trim(),
            count = result.Items.Count,
            groups = result.Items.Select(g => new
            {
                group_id = g.GroupId,
                name = g.Name,
                membership_state = g.MembershipState.ToString(),
                avatar_url = g.AvatarUrl
            })
        });
    }

    [McpServerTool(Name = "fb_join_group")]
    [Description("Request to join a Facebook community group by Group ID.")]
    public async Task<string> JoinGroupAsync(
        [Description("Facebook Group ID to join")] string groupId,
        [Description("Optional group name for saving into Target Groups if joined")] string? groupName = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        GroupMembershipState state = await _manager.CometClient
            .JoinGroupAsync(session, groupId.Trim(), ct)
            .ConfigureAwait(false);

        if (state == GroupMembershipState.Member)
        {
            SavedTargetGroup target = new(
                groupId.Trim(),
                !string.IsNullOrWhiteSpace(groupName) ? groupName.Trim() : $"Group {groupId.Trim()}",
                IsEnabled: true,
                Notes: null);
            await _manager.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            account,
            groupName ?? groupId,
            $"MCP requested to join group '{groupName ?? groupId}' -> {state}.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            group_id = groupId.Trim(),
            membership_state = state.ToString()
        });
    }

    [McpServerTool(Name = "fb_save_target_group")]
    [Description("Add or update a Facebook group in the local Target Groups list for campaigns and scraping.")]
    public async Task<string> SaveTargetGroupAsync(
        [Description("Facebook Group ID")] string groupId,
        [Description("Display name of the group")] string groupName,
        [Description("Whether the group is enabled for campaigns and scraping (default true)")] bool isEnabled = true,
        [Description("Optional comma-separated color tag IDs")] string? tagsCsv = null,
        [Description("Optional notes")] string? notes = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        SavedTargetGroup group = new(
            GroupId: groupId.Trim(),
            GroupName: groupName.Trim(),
            IsEnabled: isEnabled,
            Notes: notes,
            AvatarUrl: null,
            TagsCsv: string.IsNullOrWhiteSpace(tagsCsv) ? null : tagsCsv.Trim());

        await _manager.AccountStore.UpsertTargetGroupAsync(group, ct).ConfigureAwait(false);
        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            group.GroupName,
            $"MCP saved target group '{group.GroupName}' ({group.GroupId}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            group_id = group.GroupId,
            group_name = group.GroupName,
            is_enabled = group.IsEnabled,
            tags = group.GetTagIds()
        });
    }

    [McpServerTool(Name = "fb_create_group_tag")]
    [Description("Create a new color-coded tag for organizing campaign target groups.")]
    public async Task<string> CreateGroupTagAsync(
        [Description("Tag name (e.g. 'Bất động sản', 'VIP', 'Công nghệ')")] string name,
        [Description("Hex color code (default '#F6821F')")] string colorHex = "#F6821F",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        GroupTag tag = _manager.GroupTagService.CreateTag(name, colorHex);
        await _manager.AccountStore.UpsertGroupTagAsync(tag, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            tag.Name,
            $"MCP created group tag '{tag.Name}' ({tag.ColorHex}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            id = tag.Id,
            name = tag.Name,
            color_hex = tag.ColorHex
        });
    }

    private static readonly string[] s_defaultTagColors =
    [
        "#F6821F",
        "#3B82F6",
        "#10B981",
        "#8B5CF6",
        "#F43F5E",
        "#06B6D4",
        "#EAB308",
        "#EC4899"
    ];

    [McpServerTool(Name = "fb_assign_group_tag")]
    [Description("Assign or remove a color tag ID across one or more saved target groups.")]
    public async Task<string> AssignGroupTagAsync(
        [Description("Color tag ID to assign or remove")] string tagId,
        [Description("Comma-separated Facebook Group IDs")] string groupIdsCsv,
        [Description("True to assign the tag, false to remove it")] bool assign = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupIdsCsv);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        HashSet<string> targetIds = new(
            groupIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        List<SavedTargetGroup> updatedBatch = [];

        for (int i = 0; i < groups.Count; i++)
        {
            if (!targetIds.Contains(groups[i].GroupId))
            {
                continue;
            }

            (SavedTargetGroup updated, bool changed) = _manager.GroupTagService.ApplyTagChange(groups[i], tagId.Trim(), assign);
            if (changed)
            {
                updatedBatch.Add(updated);
            }
        }

        if (updatedBatch.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(updatedBatch, ct).ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag_id = tagId.Trim(),
            action = assign ? "assigned" : "removed",
            modified_groups = updatedBatch.Count
        });
    }

    [McpServerTool(Name = "fb_create_group_tags_batch")]
    [Description("Create multiple color-coded group tags in a single call (e.g. 'Bất động sản:#3B82F6; Mua bán:#F6821F; Việc làm:#10B981' or comma-separated names).")]
    public async Task<string> CreateGroupTagsBatchAsync(
        [Description("Semicolon- or comma-separated tag specifications, optionally with ':ColorHex' (e.g. 'Bất động sản:#3B82F6; Mua bán:#F6821F; VIP')")] string tagsSpec,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagsSpec);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<GroupTag> existingTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        HashSet<string> usedIds = new(existingTags.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        char[] separators = tagsSpec.Contains(';', StringComparison.Ordinal) ? [';'] : [',', ';'];
        string[] rawEntries = tagsSpec.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<GroupTag> createdTags = new(rawEntries.Length);
        long baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < rawEntries.Length; i++)
        {
            string entry = rawEntries[i];
            string tagName = entry;
            string? colorHex = null;

            int colonIdx = entry.LastIndexOf(':');
            if (colonIdx > 0 && colonIdx < entry.Length - 1)
            {
                string candidateColor = entry[(colonIdx + 1)..].Trim();
                if (candidateColor.StartsWith('#') || candidateColor.Length is 6 or 7)
                {
                    tagName = entry[..colonIdx].Trim();
                    colorHex = candidateColor.StartsWith('#') ? candidateColor : $"#{candidateColor}";
                }
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            string resolvedColor = !string.IsNullOrWhiteSpace(colorHex)
                ? colorHex
                : s_defaultTagColors[createdTags.Count % s_defaultTagColors.Length];

            string normInputName = _manager.LeadScoring.RemoveVietnameseDiacritics(tagName);
            GroupTag? existing = existingTags.FirstOrDefault(t =>
                string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normInputName, StringComparison.OrdinalIgnoreCase));

            GroupTag tag;
            if (existing is not null)
            {
                tag = !string.IsNullOrWhiteSpace(colorHex)
                    ? existing with { Name = tagName, ColorHex = resolvedColor }
                    : existing;
            }
            else
            {
                tag = _manager.GroupTagService.CreateTag(tagName, resolvedColor);
                if (!usedIds.Add(tag.Id))
                {
                    tag = tag with { Id = $"tag_{baseTimestamp}_{i + 1}" };
                    _ = usedIds.Add(tag.Id);
                }
            }

            await _manager.AccountStore.UpsertGroupTagAsync(tag, ct).ConfigureAwait(false);
            createdTags.Add(tag);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            "Batch Group Tags",
            $"MCP created/updated {createdTags.Count} group tags in batch.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            created_count = createdTags.Count,
            tags = createdTags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex
            })
        });
    }

    [McpServerTool(Name = "fb_auto_tag_groups_by_keywords")]
    [Description("Automatically classify and assign a color tag to all saved target groups whose name matches any of the specified keywords (accent-insensitive Vietnamese matching). Auto-creates the tag if it does not exist, and optionally removes the tag from non-matching groups.")]
    public async Task<string> AutoTagGroupsByKeywordsAsync(
        [Description("Existing tag ID or tag name (auto-created if not found)")] string tagNameOrId,
        [Description("Comma- or semicolon-separated keywords to match against group names (e.g. 'bất động sản, nhà đất, chung cư, bds')")] string keywordsCsv,
        [Description("Hex color code if creating a new tag (default '#3B82F6')")] string colorHex = "#3B82F6",
        [Description("If true, removes this tag from any group whose name does NOT match keywordsCsv (ideal for cleaning polluted tags)")] bool removeFromNonMatching = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagNameOrId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keywordsCsv);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanTarget = tagNameOrId.Trim();
        string normTarget = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTarget);

        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        GroupTag? targetTag = allTags.FirstOrDefault(t =>
            string.Equals(t.Id, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTarget, StringComparison.OrdinalIgnoreCase));

        bool createdNewTag = false;
        if (targetTag is null)
        {
            targetTag = _manager.GroupTagService.CreateTag(cleanTarget, colorHex);
            await _manager.AccountStore.UpsertGroupTagAsync(targetTag, ct).ConfigureAwait(false);
            createdNewTag = true;
        }

        string[] rawKeywords = keywordsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] normKeywords = rawKeywords
            .Select(k => _manager.LeadScoring.RemoveVietnameseDiacritics(k))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();

        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        List<SavedTargetGroup> updatedGroups = [];
        List<object> matchedGroupsSummary = [];
        int removedFromNonMatchingCount = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            SavedTargetGroup g = groups[i];
            string normGroupName = _manager.LeadScoring.RemoveVietnameseDiacritics(g.GroupName);
            bool matches = false;

            for (int k = 0; k < rawKeywords.Length; k++)
            {
                if (g.GroupName.Contains(rawKeywords[k], StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
            {
                for (int k = 0; k < normKeywords.Length; k++)
                {
                    if (normGroupName.Contains(normKeywords[k], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (!matches)
            {
                if (removeFromNonMatching && g.HasTag(targetTag.Id))
                {
                    (SavedTargetGroup stripped, bool wasStripped) = _manager.GroupTagService.ApplyTagChange(g, targetTag.Id, assignTag: false);
                    if (wasStripped)
                    {
                        updatedGroups.Add(stripped);
                        removedFromNonMatchingCount++;
                    }
                }

                continue;
            }

            (SavedTargetGroup updated, bool changed) = _manager.GroupTagService.ApplyTagChange(g, targetTag.Id, assignTag: true);
            if (changed)
            {
                updatedGroups.Add(updated);
            }

            matchedGroupsSummary.Add(new
            {
                group_id = g.GroupId,
                group_name = g.GroupName,
                newly_tagged = changed
            });
        }

        if (updatedGroups.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(updatedGroups, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            targetTag.Name,
            $"MCP auto-tagged {matchedGroupsSummary.Count} groups (removed from {removedFromNonMatchingCount}) for tag '{targetTag.Name}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag = new
            {
                id = targetTag.Id,
                name = targetTag.Name,
                color_hex = targetTag.ColorHex,
                was_created = createdNewTag
            },
            matched_count = matchedGroupsSummary.Count,
            removed_from_non_matching_count = removedFromNonMatchingCount,
            newly_modified_count = updatedGroups.Count,
            matched_groups = matchedGroupsSummary
        });
    }

    [McpServerTool(Name = "fb_delete_group_tag")]
    [Description("Delete a group color tag or clear it from all saved target groups in local SQLite.")]
    public async Task<string> DeleteGroupTagAsync(
        [Description("Group tag ID or tag name to delete or clear")] string tagNameOrId,
        [Description("If true, deletes the tag definition completely; if false, only removes the tag assignment from all groups while keeping the tag definition (default true)")] bool deleteTagDefinition = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagNameOrId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanTarget = tagNameOrId.Trim();
        string normTarget = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTarget);

        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        GroupTag? targetTag = allTags.FirstOrDefault(t =>
            string.Equals(t.Id, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Name, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTarget, StringComparison.OrdinalIgnoreCase));

        string effectiveTagId = targetTag?.Id ?? cleanTarget;
        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        List<SavedTargetGroup> updatedGroups = [];

        for (int i = 0; i < groups.Count; i++)
        {
            (SavedTargetGroup updated, bool changed) = _manager.GroupTagService.ApplyTagChange(groups[i], effectiveTagId, assignTag: false);
            if (changed)
            {
                updatedGroups.Add(updated);
            }
        }

        if (updatedGroups.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(updatedGroups, ct).ConfigureAwait(false);
        }

        bool deletedDef = false;
        if (deleteTagDefinition && targetTag is not null)
        {
            deletedDef = await _manager.AccountStore.DeleteGroupTagAsync(targetTag.Id, ct).ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag_id = effectiveTagId,
            tag_name = targetTag?.Name ?? cleanTarget,
            deleted_definition = deletedDef,
            cleared_groups_count = updatedGroups.Count
        });
    }

    [McpServerTool(Name = "fb_clean_and_classify_all_groups")]
    [Description("Clean up polluted or orphan group tags across all saved target groups and automatically classify every group by Vietnamese topic keywords (Real Estate, Marketplace/Sales, Jobs/Recruitment, IT/Tech, Community/Gaming).")]
    public async Task<string> CleanAndClassifyAllGroupsAsync(
        [Description("If true, resets existing tags before classifying; if false, only removes polluted/orphan tags and adds matching topic tags (default true)")] bool resetExistingTagsFirst = true,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        (string Id, string Name, string ColorHex, string[] NormKeywords)[] rules =
        [
            ("tag_bds", "Bất động sản", "#3B82F6", ["bat dong san", "bds", "nha dat", "chung cu", "can ho", "phong tro", "nha tro", "mat bang", "dat nen", "thue nha", "mua ban nha"]),
            ("tag_job", "Việc làm & Tuyển dụng", "#10B981", ["viec lam", "tuyen dung", "tim viec", "intern", "fresher", "job", "freelance", "hr", "career", "cv"]),
            ("tag_tech", "Công nghệ & Lập trình", "#06B6D4", [".net", "c#", "csharp", "lap trinh", "developer", "code", "coding", "it ", "cntt", "software", "arduino", "raspberry", "esp32", "iot", "ai ", "claude", "gpt", "antigravity", "microservices", "leetcode", "dsa", "web", "app", "linux", "windows", "pc", "mini pc", "ban phim", "ssd", "hdd", "phan cung", "devops", "frontend", "backend", "react", "python", "java", "golang"]),
            ("tag_market", "Mua bán & Chợ", "#F6821F", ["mua ban", "cho ", "thanh ly", "pass ", "sang nhuong", "trao doi", "gia re", "sỉ", "buon", "kinh doanh", "ban hang", "shop", "deal"]),
            ("tag_community", "Cộng đồng & Giao lưu", "#8B5CF6", ["cong dong", "giao luu", "hoi ", "group", "clb", "voz", "hay day", "ngoc rong", "dragon city", "afk", "game", "me bau", "me don than", "chia se", "ho tro", "giup do"])
        ];

        for (int r = 0; r < rules.Length; r++)
        {
            await _manager.AccountStore.UpsertGroupTagAsync(
                new GroupTag(rules[r].Id, rules[r].Name, rules[r].ColorHex),
                ct).ConfigureAwait(false);
        }

        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        HashSet<string> validTagIds = new(allTags.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        List<SavedTargetGroup> updatedBatch = [];
        Dictionary<string, int> categoryCounts = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < groups.Count; i++)
        {
            SavedTargetGroup g = groups[i];
            string normName = $" {_manager.LeadScoring.RemoveVietnameseDiacritics(g.GroupName).ToLowerInvariant()} ";

            List<string> assigned = [];
            if (!resetExistingTagsFirst)
            {
                IReadOnlyList<string> currentIds = g.GetTagIds();
                for (int c = 0; c < currentIds.Count; c++)
                {
                    string mappedId = currentIds[c] switch
                    {
                        "tag_jobs" => "tag_job",
                        "tag_sales" => "tag_market",
                        _ => currentIds[c]
                    };
                    if (validTagIds.Contains(mappedId) && !assigned.Contains(mappedId, StringComparer.OrdinalIgnoreCase))
                    {
                        assigned.Add(mappedId);
                    }
                }
            }

            for (int r = 0; r < rules.Length; r++)
            {
                string[] kws = rules[r].NormKeywords;
                bool matchedRule = false;
                for (int k = 0; k < kws.Length; k++)
                {
                    if (normName.Contains(kws[k], StringComparison.OrdinalIgnoreCase))
                    {
                        matchedRule = true;
                        break;
                    }
                }

                if (matchedRule)
                {
                    if (!assigned.Contains(rules[r].Id, StringComparer.OrdinalIgnoreCase))
                    {
                        assigned.Add(rules[r].Id);
                    }
                }
                else if (string.Equals(rules[r].Id, "tag_bds", StringComparison.OrdinalIgnoreCase))
                {
                    _ = assigned.RemoveAll(id => string.Equals(id, "tag_bds", StringComparison.OrdinalIgnoreCase));
                }
            }

            if (assigned.Count == 0)
            {
                assigned.Add("tag_community");
            }

            for (int a = 0; a < assigned.Count; a++)
            {
                categoryCounts[assigned[a]] = categoryCounts.TryGetValue(assigned[a], out int cnt) ? cnt + 1 : 1;
            }

            string newCsv = string.Join(',', assigned);
            if (!string.Equals(g.TagsCsv ?? string.Empty, newCsv, StringComparison.Ordinal))
            {
                updatedBatch.Add(g with { TagsCsv = newCsv });
            }
        }

        if (updatedBatch.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(updatedBatch, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            "Smart Group Classification",
            $"MCP cleaned and classified {groups.Count} groups ({updatedBatch.Count} updated).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            total_groups = groups.Count,
            updated_groups_count = updatedBatch.Count,
            category_counts = categoryCounts
        });
    }
}
