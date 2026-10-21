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
        int modifiedCount = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            if (!targetIds.Contains(groups[i].GroupId))
            {
                continue;
            }

            (SavedTargetGroup updated, bool changed) = _manager.GroupTagService.ApplyTagChange(groups[i], tagId.Trim(), assign);
            if (changed)
            {
                await _manager.AccountStore.UpsertTargetGroupAsync(updated, ct).ConfigureAwait(false);
                modifiedCount++;
            }
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            tag_id = tagId.Trim(),
            action = assign ? "assigned" : "removed",
            modified_groups = modifiedCount
        });
    }
}
