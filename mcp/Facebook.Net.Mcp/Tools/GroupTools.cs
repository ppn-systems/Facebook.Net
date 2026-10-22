// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Community Group Discovery, Joining, Target Group Management, Anti-Spam Rotation Selection, and Color Tagging.
/// Strictly adheres to the safety rule: NEVER invokes LeaveGroupAsync.
/// </summary>
[McpServerToolType]
public sealed class GroupTools(FacebookMcpSessionManager manager)
{
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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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
    [Description("Request to join a Facebook community group by Group ID, automatically fetching pending membership approval questions if the group requires admin approval.")]
    public async Task<string> JoinGroupAsync(
        [Description("Facebook Group ID to join")] string groupId,
        [Description("Optional group name for saving into Target Groups if joined")] string? groupName = null,
        [Description("If true and the join state is 'Requested', automatically fetches the group's membership approval questions so you can answer them via fb_submit_group_answers (default true)")] bool fetchQuestionsIfRequested = true,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        GroupMembershipState state = await _manager.CometClient
            .JoinGroupAsync(session, cleanGroupId, ct)
            .ConfigureAwait(false);

        await _manager.AccountStore.RecordActionAsync(
            new ActionHistoryEntry(session.AccountUid, DomainActionKinds.JoinGroup, cleanGroupId, state.ToString(), DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);

        if (state == GroupMembershipState.Member)
        {
            SavedTargetGroup target = new(
                cleanGroupId,
                !string.IsNullOrWhiteSpace(groupName) ? groupName.Trim() : $"Group {cleanGroupId}",
                IsEnabled: true,
                Notes: null);
            await _manager.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(false);
        }

        IReadOnlyList<GroupQuestion>? pendingQuestions = null;
        if (fetchQuestionsIfRequested && state == GroupMembershipState.Requested)
        {
            pendingQuestions = await _manager.CometClient
                .GetGroupQuestionsAsync(session, cleanGroupId, ct)
                .ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            account,
            groupName ?? cleanGroupId,
            $"MCP requested to join group '{groupName ?? cleanGroupId}' -> {state}.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            group_id = cleanGroupId,
            membership_state = state.ToString(),
            pending_questions_count = pendingQuestions?.Count ?? 0,
            pending_questions = pendingQuestions?.Select(q => new
            {
                question_id = q.QuestionId,
                question_text = q.QuestionText,
                question_type = q.QuestionType
            })
        });
    }

    [McpServerTool(Name = "fb_batch_join_groups")]
    [Description("Batch-join multiple Facebook community groups (either from comma-separated groupIdsCsv OR discovered via searchKeyword) with safe anti-checkpoint delay between requests, automatically saving newly joined groups to Target Groups.")]
    public async Task<string> BatchJoinGroupsAsync(
        [Description("Optional comma- or semicolon-separated Facebook Group IDs to join")] string? groupIdsCsv = null,
        [Description("Optional search keyword to find and join unjoined groups if groupIdsCsv is omitted")] string? searchKeyword = null,
        [Description("Maximum number of groups to join in this batch (1..20, default 5)")] int maxGroupsToJoin = 5,
        [Description("Anti-checkpoint delay in seconds between join requests (2..120, default 5)")] int delaySeconds = 5,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupIdsCsv) && string.IsNullOrWhiteSpace(searchKeyword))
        {
            throw new ArgumentException("Either 'groupIdsCsv' or 'searchKeyword' must be provided.");
        }

        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        int cap = Math.Clamp(maxGroupsToJoin, 1, 20);
        int delaySec = Math.Clamp(delaySeconds, 2, 120);
        List<(string GroupId, string GroupName, string? AvatarUrl)> candidates = [];

        if (!string.IsNullOrWhiteSpace(groupIdsCsv))
        {
            string[] ids = groupIdsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < ids.Length && candidates.Count < cap; i++)
            {
                candidates.Add((ids[i], $"Group {ids[i]}", null));
            }
        }
        else if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            PagedResult<GroupSummary> searchResult = await _manager.CometClient
                .SearchGroupsAsync(session, searchKeyword.Trim(), count: Math.Max(cap * 3, 20), ct: ct)
                .ConfigureAwait(false);

            for (int i = 0; i < searchResult.Items.Count && candidates.Count < cap; i++)
            {
                GroupSummary grp = searchResult.Items[i];
                if (grp.MembershipState is GroupMembershipState.CanJoin or GroupMembershipState.Unknown)
                {
                    candidates.Add((grp.GroupId, grp.Name, grp.AvatarUrl));
                }
            }
        }

        List<object> results = [];
        int joinedMemberCount = 0;
        int requestedApprovalCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            (string gid, string gname, string? avatar) = candidates[i];
            GroupMembershipState state = await _manager.CometClient
                .JoinGroupAsync(session, gid, ct)
                .ConfigureAwait(false);

            if (state == GroupMembershipState.Member)
            {
                joinedMemberCount++;
                await _manager.AccountStore.UpsertTargetGroupAsync(
                    new SavedTargetGroup(gid, gname, IsEnabled: true, Notes: null, AvatarUrl: avatar),
                    ct).ConfigureAwait(false);
            }
            else if (state == GroupMembershipState.Requested)
            {
                requestedApprovalCount++;
            }

            results.Add(new
            {
                group_id = gid,
                group_name = gname,
                membership_state = state.ToString()
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            account,
            searchKeyword ?? "Batch Join",
            $"MCP batch-joined {candidates.Count} groups ({joinedMemberCount} joined, {requestedApprovalCount} pending approval).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            attempted_count = candidates.Count,
            joined_member_count = joinedMemberCount,
            requested_approval_count = requestedApprovalCount,
            results
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

        return McpJson.Serialize(new
        {
            status = "success",
            group_id = group.GroupId,
            group_name = group.GroupName,
            is_enabled = group.IsEnabled,
            tags = group.GetTagIds()
        });
    }

    [McpServerTool(Name = "fb_remove_target_group")]
    [Description("Remove one or more Facebook groups from the local Target Groups list in SQLite without leaving the group on Facebook.")]
    public async Task<string> RemoveTargetGroupAsync(
        [Description("Comma- or semicolon-separated Facebook Group IDs to remove from Target Groups")] string groupIdsCsv,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupIdsCsv);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string[] ids = groupIdsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int removedCount = 0;

        for (int i = 0; i < ids.Length; i++)
        {
            if (await _manager.AccountStore.DeleteTargetGroupAsync(ids[i], ct).ConfigureAwait(false))
            {
                removedCount++;
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            account: null,
            "Remove Target Groups",
            $"MCP removed {removedCount} target groups from local list.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            removed_count = removedCount,
            requested_ids = ids
        });
    }

    [McpServerTool(Name = "fb_configure_target_groups_selection")]
    [Description("Configure which saved Target Groups are enabled for campaigns and scraping using modes: 'enable_all', 'disable_all', 'only_tag' (enable only groups carrying tagIdOrName), 'enable_tag', 'disable_tag', or 'random' (anti-spam Fisher-Yates random rotation of randomCount groups).")]
    public async Task<string> ConfigureTargetGroupsSelectionAsync(
        [Description("Selection mode: 'enable_all', 'disable_all', 'only_tag', 'enable_tag', 'disable_tag', or 'random'")] string mode,
        [Description("Optional group color tag ID or tag name when mode is 'only_tag', 'enable_tag', or 'disable_tag'")] string? tagIdOrName = null,
        [Description("Number of random groups to enable when mode is 'random' (default 20)")] int randomCount = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string? resolvedTagId = null;
        if (!string.IsNullOrWhiteSpace(tagIdOrName))
        {
            string cleanTag = tagIdOrName.Trim();
            string normTag = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTag);
            IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
            GroupTag? matchedTag = allTags.FirstOrDefault(t =>
                string.Equals(t.Id, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTag, StringComparison.OrdinalIgnoreCase));
            resolvedTagId = matchedTag?.Id ?? cleanTag;
        }

        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        (IReadOnlyList<SavedTargetGroup> modifiedGroups, int enabledCount, int disabledCount) =
            _manager.GroupTagService.ConfigureTargetGroupsSelection(groups, mode, resolvedTagId, randomCount);

        if (modifiedGroups.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(modifiedGroups, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            "Target Groups Selection",
            $"MCP configured target groups selection (mode='{mode}', enabled={enabledCount}, disabled={disabledCount}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            mode = mode.Trim().ToLowerInvariant(),
            resolved_tag_id = resolvedTagId,
            total_groups = groups.Count,
            enabled_count = enabledCount,
            disabled_count = disabledCount,
            modified_count = modifiedGroups.Count
        });
    }

    [McpServerTool(Name = "fb_list_group_tags")]
    [Description("List all color-coded group tags stored in local SQLite along with the total and enabled target group counts for each tag.")]
    public async Task<string> ListGroupTagsAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);

        var items = allTags.Select(t =>
        {
            int totalWithTag = 0;
            int enabledWithTag = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].HasTag(t.Id))
                {
                    totalWithTag++;
                    if (groups[i].IsEnabled)
                    {
                        enabledWithTag++;
                    }
                }
            }

            return new
            {
                id = t.Id,
                name = t.Name,
                color_hex = t.ColorHex,
                total_groups_count = totalWithTag,
                enabled_groups_count = enabledWithTag
            };
        });

        return McpJson.Serialize(new
        {
            total_tags = allTags.Count,
            total_target_groups = groups.Count,
            group_tags = items
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

        return McpJson.Serialize(new
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

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            tagId.Trim(),
            $"MCP {(assign ? "assigned" : "removed")} group tag '{tagId.Trim()}' across {updatedBatch.Count} target groups.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
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

            string defaultFallbackColor = s_defaultTagColors[createdTags.Count % s_defaultTagColors.Length];
            string resolvedColor = !string.IsNullOrWhiteSpace(colorHex)
                ? _manager.GroupTagService.NormalizeColorHex(colorHex, defaultFallbackColor)
                : defaultFallbackColor;

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

        return McpJson.Serialize(new
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

        return McpJson.Serialize(new
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

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            account: null,
            targetTag?.Name ?? cleanTarget,
            $"MCP deleted/cleared group tag '{targetTag?.Name ?? cleanTarget}' (cleared {updatedGroups.Count} groups, deletedDefinition={deletedDef}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
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

        IReadOnlyList<GroupTag> allTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
        HashSet<string> validTagIds = new(allTags.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<SavedTargetGroup> groups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);

        (IReadOnlyList<GroupTag> requiredTags, IReadOnlyList<SavedTargetGroup> modifiedGroups, IReadOnlyDictionary<string, int> categoryCounts) =
            _manager.GroupTagService.ClassifyAndCleanGroups(groups, validTagIds, resetExistingTagsFirst);

        for (int i = 0; i < requiredTags.Count; i++)
        {
            await _manager.AccountStore.UpsertGroupTagAsync(requiredTags[i], ct).ConfigureAwait(false);
        }

        if (modifiedGroups.Count > 0)
        {
            await _manager.AccountStore.UpsertTargetGroupsBatchAsync(modifiedGroups, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            "Smart Group Classification",
            $"MCP cleaned and classified {groups.Count} groups ({modifiedGroups.Count} updated).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            total_groups = groups.Count,
            updated_groups_count = modifiedGroups.Count,
            category_counts = categoryCounts
        });
    }

    [McpServerTool(Name = "fb_get_group_questions")]
    [Description("Retrieve pending membership approval questions ('GroupsCometMembershipQuestionsDialogQuery') for a private Facebook group so the AI can craft context-aware answers.")]
    public async Task<string> GetGroupQuestionsAsync(
        [Description("Target Facebook Group ID")] string groupId,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        IReadOnlyList<GroupQuestion> questions = await _manager.CometClient
            .GetGroupQuestionsAsync(session, cleanGroupId, ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            group_id = cleanGroupId,
            questions_count = questions.Count,
            questions = questions.Select(q => new
            {
                question_id = q.QuestionId,
                question_text = q.QuestionText,
                question_type = q.QuestionType
            })
        });
    }

    [McpServerTool(Name = "fb_submit_group_answers")]
    [Description("Submit answers to a Facebook group's membership approval questionnaire ('useGroupMembershipAnswersSaveMutation'). Accepts either a JSON array '[{\"question_id\":\"123\",\"answer_text\":\"...\"}]', semicolon-separated 'questionId:answerText;questionId2:answerText2' pairs, OR a single default answer string applied to all pending questions of the group.")]
    public async Task<string> SubmitGroupAnswersAsync(
        [Description("Target Facebook Group ID")] string groupId,
        [Description("Answers specification: JSON array '[{\"question_id\":\"...\",\"answer_text\":\"...\"}]', semicolon-separated 'questionId:answerText' pairs, or a single polite answer text to apply to all pending questions")] string answersSpec,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(answersSpec);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        string trimmedSpec = answersSpec.Trim();
        List<GroupQuestionAnswer> answers = [];

        if (trimmedSpec.StartsWith('['))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(trimmedSpec);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        string? qid = (elem.TryGetProperty("question_id", out JsonElement q1) ? q1.GetString() : null)
                            ?? (elem.TryGetProperty("questionId", out JsonElement q2) ? q2.GetString() : null);
                        string? ans = (elem.TryGetProperty("answer_text", out JsonElement a1) ? a1.GetString() : null)
                            ?? (elem.TryGetProperty("answerText", out JsonElement a2) ? a2.GetString() : null)
                            ?? (elem.TryGetProperty("answer", out JsonElement a3) ? a3.GetString() : null);

                        if (!string.IsNullOrWhiteSpace(qid) && !string.IsNullOrWhiteSpace(ans))
                        {
                            answers.Add(new GroupQuestionAnswer(qid.Trim(), ans.Trim()));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to delimiter parsing below
            }
        }

        if (answers.Count == 0 && trimmedSpec.Contains(':', StringComparison.Ordinal))
        {
            string[] pairs = trimmedSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                int colonIdx = pairs[i].IndexOf(':', StringComparison.Ordinal);
                if (colonIdx > 0 && colonIdx < pairs[i].Length - 1)
                {
                    string qid = pairs[i][..colonIdx].Trim();
                    string ans = pairs[i][(colonIdx + 1)..].Trim();
                    if (qid.All(char.IsDigit) && !string.IsNullOrWhiteSpace(ans))
                    {
                        answers.Add(new GroupQuestionAnswer(qid, ans));
                    }
                }
            }
        }

        if (answers.Count == 0)
        {
            IReadOnlyList<GroupQuestion> questions = await _manager.CometClient
                .GetGroupQuestionsAsync(session, cleanGroupId, ct)
                .ConfigureAwait(false);

            for (int i = 0; i < questions.Count; i++)
            {
                answers.Add(new GroupQuestionAnswer(questions[i].QuestionId, trimmedSpec));
            }
        }

        if (answers.Count == 0)
        {
            return McpJson.Serialize(new
            {
                status = "no_questions_found",
                account_uid = account.Uid,
                group_id = cleanGroupId,
                message = "No pending membership questions were found for this group."
            });
        }

        GroupMembershipState state = await _manager.CometClient
            .SubmitGroupAnswersAsync(session, cleanGroupId, answers, ct)
            .ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            account,
            cleanGroupId,
            $"MCP submitted {answers.Count} membership approval answer(s) for group '{cleanGroupId}' -> {state}.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            group_id = cleanGroupId,
            submitted_answers_count = answers.Count,
            membership_state = state.ToString()
        });
    }

    [McpServerTool(Name = "fb_get_group_new_members")]
    [Description("Scrape newly joined members of a target Facebook community group via Comet GraphQL ('GroupsCometMembersPageNewMembersSectionRefetchQuery') to discover fresh leads or community prospects.")]
    public async Task<string> GetGroupNewMembersAsync(
        [Description("Target Facebook Group ID")] string groupId,
        [Description("Maximum number of new members to fetch (1..100, default 20)")] int count = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        PagedResult<UserSummary> members = await _manager.CometClient
            .GetGroupNewMembersAsync(session, cleanGroupId, count: Math.Clamp(count, 1, 100), ct: ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            group_id = cleanGroupId,
            count = members.Items.Count,
            has_next_page = members.HasNextPage,
            new_members = members.Items.Select(m => new
            {
                uid = m.Uid,
                name = m.Name,
                avatar_uri = m.AvatarUri,
                friendship_state = m.Friendship.ToString()
            })
        });
    }

    [McpServerTool(Name = "fb_invite_friends_to_group")]
    [Description("Invite one or more Facebook friends (by comma-separated UIDs) to join a target community group ('useGroupAddMembersMutation').")]
    public async Task<string> InviteFriendsToGroupAsync(
        [Description("Target Facebook Group ID")] string groupId,
        [Description("Comma- or semicolon-separated friend UIDs to invite to the group")] string friendUidsCsv,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendUidsCsv);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanGroupId = groupId.Trim();
        string[] friendUids = friendUidsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool ok = await _manager.CometClient
            .InviteFriendsToGroupAsync(session, cleanGroupId, friendUids, ct)
            .ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Group,
            ok ? AuditSeverity.Success : AuditSeverity.Warning,
            account,
            cleanGroupId,
            $"MCP invited {friendUids.Length} friend(s) to group '{cleanGroupId}' (success={ok}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = ok ? "success" : "failed",
            account_uid = account.Uid,
            group_id = cleanGroupId,
            invited_count = friendUids.Length,
            invited_uids = friendUids
        });
    }
}
