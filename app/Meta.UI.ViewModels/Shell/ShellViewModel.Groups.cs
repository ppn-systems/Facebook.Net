// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;

namespace Meta.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling visual Group catalog synchronization,
/// keyword group discovery, joining groups, and managing target campaign groups without typing raw Group IDs.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets the visually selected group from the synced/searched catalog.</summary>
    [ObservableProperty]
    public partial GroupSummary? SelectedGroup { get; set; }

    /// <summary>Gets or sets the visually selected target group from the saved campaign targets.</summary>
    [ObservableProperty]
    public partial SavedTargetGroup? SelectedTargetGroup { get; set; }

    /// <summary>Gets or sets the keyword used to discover new community groups on Facebook.</summary>
    [ObservableProperty]
    public partial string GroupSearchKeyword { get; set; } = string.Empty;

    /// <summary>Gets or sets the local filter text to quickly filter synced groups by name.</summary>
    [ObservableProperty]
    public partial string GroupFilterText { get; set; } = string.Empty;

    /// <summary>Gets or sets the name input for creating a new custom color-coded group tag.</summary>
    [ObservableProperty]
    public partial string NewGroupTagName { get; set; } = string.Empty;

    /// <summary>Gets or sets the selected color hex (<c>#RRGGBB</c>) when creating a new group tag.</summary>
    [ObservableProperty]
    public partial string NewGroupTagColorHex { get; set; } = "#F6821F";

    /// <summary>Gets or sets the currently active tag filter ID on the Groups page (<see langword="null"/> = all tags).</summary>
    [ObservableProperty]
    public partial string? ActiveTagFilterId { get; set; }

    private readonly System.Collections.Generic.List<GroupSummary> _allCatalogGroups = new();
    private readonly System.Collections.Generic.Dictionary<string, SavedTargetGroup> _targetGroupIndex = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, GroupTag> _groupTagIndex = new(StringComparer.OrdinalIgnoreCase);
    private GroupMembershipState? _activeStateFilter;

    /// <summary>
    /// Checks whether <paramref name="source"/> contains <paramref name="keyword"/> using Vietnamese accent-insensitive matching.
    /// </summary>
    public bool MatchesTextAccentInsensitive(string? source, string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ||
        (!string.IsNullOrWhiteSpace(source) && _context.LeadScoring.MatchesAccentInsensitive(source, keyword));

    /// <summary>
    /// Filters the displayed <see cref="JoinedGroups"/> catalog locally by <see cref="GroupFilterText"/>,
    /// optional membership state, and optional tag filter.
    /// </summary>
    public void ApplyLocalGroupFilter(string? filterText, GroupMembershipState? stateFilter = null, string? tagIdFilter = "__KEEP__")
    {
        if (_allCatalogGroups.Count == 0 && this.JoinedGroups.Count > 0)
        {
            for (int i = 0; i < this.JoinedGroups.Count; i++)
            {
                _allCatalogGroups.Add(this.JoinedGroups[i]);
            }
        }

        if (filterText is not null)
        {
            this.GroupFilterText = filterText;
        }

        if (stateFilter.HasValue || filterText is null)
        {
            _activeStateFilter = stateFilter;
        }

        if (!string.Equals(tagIdFilter, "__KEEP__", StringComparison.Ordinal))
        {
            this.ActiveTagFilterId = tagIdFilter;
        }

        string kw = this.GroupFilterText.Trim();
        GroupMembershipState? state = _activeStateFilter;
        string? tagId = this.ActiveTagFilterId;

        if (string.IsNullOrWhiteSpace(kw) && state is null && string.IsNullOrWhiteSpace(tagId))
        {
            this.JoinedGroups.ReplaceAll(_allCatalogGroups);
            return;
        }

        System.Collections.Generic.List<GroupSummary> filtered = new(_allCatalogGroups.Count);
        for (int i = 0; i < _allCatalogGroups.Count; i++)
        {
            GroupSummary grp = _allCatalogGroups[i];
            if (state.HasValue)
            {
                if (state.Value == GroupMembershipState.Member && grp.MembershipState != GroupMembershipState.Member)
                {
                    continue;
                }

                if (state.Value == GroupMembershipState.CanJoin && grp.MembershipState == GroupMembershipState.Member)
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(tagId))
            {
                SavedTargetGroup? tg = this.FindTargetGroupById(grp.GroupId);
                if (tg is null || !tg.HasTag(tagId))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(kw) &&
                !_context.LeadScoring.MatchesAccentInsensitive(grp.Name, kw))
            {
                continue;
            }

            filtered.Add(grp);
        }

        this.JoinedGroups.ReplaceAll(filtered);
    }

    /// <summary>
    /// Finds a saved target group by <paramref name="groupId"/> in O(1) time using an indexed lookup.
    /// </summary>
    public SavedTargetGroup? FindTargetGroupById(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return null;
        }

        if (_targetGroupIndex.Count == this.TargetGroups.Count &&
            _targetGroupIndex.TryGetValue(groupId, out SavedTargetGroup? fastHit))
        {
            return fastHit;
        }

        this.RebuildTargetGroupIndex();
        return _targetGroupIndex.TryGetValue(groupId, out SavedTargetGroup? val) ? val : null;
    }

    /// <summary>
    /// Finds a group tag by <paramref name="tagId"/> in O(1) time using an indexed lookup.
    /// </summary>
    public GroupTag? FindGroupTagById(string? tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return null;
        }

        if (_groupTagIndex.Count == this.GroupTags.Count &&
            _groupTagIndex.TryGetValue(tagId, out GroupTag? fastHit))
        {
            return fastHit;
        }

        this.RebuildGroupTagIndex();
        return _groupTagIndex.TryGetValue(tagId, out GroupTag? val) ? val : null;
    }

    private void RebuildTargetGroupIndex()
    {
        _targetGroupIndex.Clear();
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (!string.IsNullOrWhiteSpace(tg.GroupId))
            {
                _targetGroupIndex[tg.GroupId] = tg;
            }
        }
    }

    private void RebuildGroupTagIndex()
    {
        _groupTagIndex.Clear();
        for (int i = 0; i < this.GroupTags.Count; i++)
        {
            GroupTag tag = this.GroupTags[i];
            if (!string.IsNullOrWhiteSpace(tag.Id))
            {
                _groupTagIndex[tag.Id] = tag;
            }
        }
    }

    /// <summary>
    /// Returns the list of resolved <see cref="GroupTag"/> badges assigned to a group in O(K) time.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<GroupTag> GetGroupTagsForGroup(string? groupId)
    {
        SavedTargetGroup? tg = this.FindTargetGroupById(groupId);
        if (tg is null || string.IsNullOrWhiteSpace(tg.TagsCsv))
        {
            return Array.Empty<GroupTag>();
        }

        System.Collections.Generic.IReadOnlyList<string> tagIds = tg.GetTagIds();
        if (tagIds.Count == 0)
        {
            return Array.Empty<GroupTag>();
        }

        System.Collections.Generic.List<GroupTag> resolved = new(tagIds.Count);
        for (int i = 0; i < tagIds.Count; i++)
        {
            GroupTag? matched = this.FindGroupTagById(tagIds[i]);
            if (matched is not null)
            {
                resolved.Add(matched);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Creates a new color-coded <see cref="GroupTag"/> and persists it to SQLite.
    /// </summary>
    public async Task CreateGroupTagAsync()
    {
        string name = this.NewGroupTagName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        GroupTag tag = _context.GroupTagService.CreateTag(name, this.NewGroupTagColorHex);

        await _context.AccountStore.UpsertGroupTagAsync(tag).ConfigureAwait(true);
        this.NewGroupTagName = string.Empty;
        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);

        _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
            "campaign_rules",
            $"group_tag_{tag.Id}",
            _context.Localization.Format("Ai.LearnGroupTagClassification", tag.Name, tag.ColorHex));

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            tag.Name,
            _context.Localization.Format("Log.Personal.TagCreated", tag.Name)).ConfigureAwait(true);
    }

    /// <summary>
    /// Updates an existing <see cref="GroupTag"/>'s name and color in SQLite and reloads the tag list.
    /// </summary>
    public async Task UpdateGroupTagAsync(string tagId, string newName, string newColorHex)
    {
        if (string.IsNullOrWhiteSpace(tagId) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        string trimmedName = newName.Trim();
        string color = _context.GroupTagService.NormalizeColorHex(newColorHex);
        GroupTag updated = new(tagId, trimmedName, color);
        await _context.AccountStore.UpsertGroupTagAsync(updated).ConfigureAwait(true);
        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);

        _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
            "campaign_rules",
            $"group_tag_{updated.Id}",
            _context.Localization.Format("Ai.LearnGroupTagClassification", updated.Name, updated.ColorHex));

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            trimmedName,
            _context.Localization.Format("Log.Personal.TagUpdated", trimmedName)).ConfigureAwait(true);
    }

    /// <summary>
    /// Batch-assigns or removes <paramref name="tagId"/> across the specified <paramref name="groups"/> and persists changes to SQLite.
    /// </summary>
    public async Task BatchSetTagOnGroupsAsync(string tagId, System.Collections.Generic.IEnumerable<SavedTargetGroup> groups, bool assignTag)
    {
        if (string.IsNullOrWhiteSpace(tagId) || groups is null)
        {
            return;
        }

        System.Collections.Generic.List<SavedTargetGroup> modified = new();
        foreach (SavedTargetGroup grp in groups)
        {
            if (grp is null)
            {
                continue;
            }

            (SavedTargetGroup updated, bool changed) = _context.GroupTagService.ApplyTagChange(grp, tagId, assignTag);
            if (changed)
            {
                modified.Add(updated);
            }
        }

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
        }

        string tagName = this.FindGroupTagById(tagId)?.Name ?? tagId;
        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            tagName,
            _context.Localization.Format(
                assignTag ? "Log.Personal.BatchTagAssigned" : "Log.Personal.BatchTagRemoved",
                tagName,
                modified.Count)).ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes a <see cref="GroupTag"/> from SQLite and removes it from any tagged groups.
    /// </summary>
    public async Task DeleteGroupTagAsync(GroupTag? tag)
    {
        if (tag is null)
        {
            return;
        }

        _ = await _context.AccountStore.DeleteGroupTagAsync(tag.Id).ConfigureAwait(true);

        System.Collections.Generic.List<SavedTargetGroup> nextList = new(this.TargetGroups.Count);
        System.Collections.Generic.List<SavedTargetGroup> modified = new();
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup current = this.TargetGroups[i];
            (SavedTargetGroup updated, bool changed) = _context.GroupTagService.ApplyTagChange(
                current,
                tag.Id,
                assignTag: false);
            if (changed)
            {
                modified.Add(updated);
                nextList.Add(updated);
            }
            else
            {
                nextList.Add(current);
            }
        }

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            this.TargetGroups.ReplaceAll(nextList);
        }

        if (string.Equals(this.ActiveTagFilterId, tag.Id, StringComparison.OrdinalIgnoreCase))
        {
            this.ActiveTagFilterId = null;
        }

        this.RebuildTargetGroupIndex();
        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            tag.Name,
            _context.Localization.Format("Log.Personal.TagDeleted", tag.Name)).ConfigureAwait(true);
    }

    /// <summary>
    /// Toggles a <paramref name="tagId"/> on a group and persists the change to SQLite.
    /// </summary>
    public async Task ToggleGroupTagAsync(string groupId, string groupName, string? avatarUrl, string tagId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(tagId))
        {
            return;
        }

        SavedTargetGroup existing = this.FindTargetGroupById(groupId)
            ?? new SavedTargetGroup(groupId, groupName, IsEnabled: true, Notes: null, AvatarUrl: avatarUrl, TagsCsv: null);

        (SavedTargetGroup updated, bool removed) = _context.GroupTagService.ToggleTagOnGroup(
            existing,
            groupName,
            avatarUrl,
            tagId);

        await _context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);

        bool foundInList = false;
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (string.Equals(this.TargetGroups[i].GroupId, groupId, StringComparison.Ordinal))
            {
                this.TargetGroups[i] = updated;
                foundInList = true;
                break;
            }
        }

        if (!foundInList)
        {
            this.TargetGroups.Add(updated);
        }

        _targetGroupIndex[groupId] = updated;
        string tagName = this.FindGroupTagById(tagId)?.Name ?? tagId;
        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            updated.GroupName,
            _context.Localization.Format(
                removed ? "Log.Personal.TagRemovedFromGroup" : "Log.Personal.TagAssignedToGroup",
                tagName,
                updated.GroupName)).ConfigureAwait(true);
    }

    /// <summary>
    /// Batch-assigns <paramref name="tagId"/> to all currently displayed <see cref="JoinedGroups"/>.
    /// </summary>
    public async Task AssignTagToFilteredGroupsAsync(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId) || this.JoinedGroups.Count == 0)
        {
            return;
        }

        System.Collections.Generic.List<SavedTargetGroup> modified = new();
        for (int i = 0; i < this.JoinedGroups.Count; i++)
        {
            GroupSummary grp = this.JoinedGroups[i];
            SavedTargetGroup? tg = this.FindTargetGroupById(grp.GroupId);
            if (tg is null || !tg.HasTag(tagId))
            {
                SavedTargetGroup existing = tg ?? new SavedTargetGroup(
                    grp.GroupId,
                    grp.Name,
                    IsEnabled: true,
                    Notes: null,
                    AvatarUrl: grp.AvatarUrl,
                    TagsCsv: null);
                (SavedTargetGroup updated, _) = _context.GroupTagService.ToggleTagOnGroup(
                    existing,
                    grp.Name,
                    grp.AvatarUrl,
                    tagId);
                modified.Add(updated);
            }
        }

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Audit.Cat.Group"],
            _context.Localization.Format("Log.BatchTaggedGroups", modified.Count)).ConfigureAwait(true);
    }

    /// <summary>
    /// Enables or disables all target groups that carry the specified <paramref name="tagId"/>.
    /// If all groups with <paramref name="tagId"/> are already enabled, toggles them off; otherwise enables them.
    /// </summary>
    public async Task ToggleSelectTargetGroupsByTagAsync(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return;
        }

        int totalWithTag = 0;
        int enabledWithTag = 0;
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (this.TargetGroups[i].HasTag(tagId))
            {
                totalWithTag++;
                if (this.TargetGroups[i].IsEnabled)
                {
                    enabledWithTag++;
                }
            }
        }

        if (totalWithTag == 0)
        {
            return;
        }

        bool targetState = enabledWithTag < totalWithTag;
        System.Collections.Generic.List<SavedTargetGroup> nextList = new(this.TargetGroups.Count);
        System.Collections.Generic.List<SavedTargetGroup> modified = new();
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (tg.HasTag(tagId) && tg.IsEnabled != targetState)
            {
                SavedTargetGroup updated = tg with { IsEnabled = targetState };
                modified.Add(updated);
                nextList.Add(updated);
            }
            else
            {
                nextList.Add(tg);
            }
        }

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
        }

        this.TargetGroups.ReplaceAll(nextList);
        this.RebuildTargetGroupIndex();
    }

    /// <summary>
    /// Selects a random subset of <paramref name="randomCount"/> target groups (and unchecks the rest) for anti-spam rotation.
    /// </summary>
    public async Task SelectRandomTargetGroupsAsync(int randomCount)
    {
        if (this.TargetGroups.Count == 0)
        {
            return;
        }

        (System.Collections.Generic.IReadOnlyList<SavedTargetGroup> modified, _, _) =
            _context.GroupTagService.ConfigureTargetGroupsSelection(this.TargetGroups, "random", randomCount: randomCount);

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            this.ApplyModifiedTargetGroupsInMemory(modified);
        }
    }

    private async Task ReloadGroupTagsOnlyAsync()
    {
        System.Collections.Generic.IReadOnlyList<GroupTag> tags =
            await _context.AccountStore.GetAllGroupTagsAsync().ConfigureAwait(true);
        this.GroupTags.ReplaceAll(tags);
        this.RebuildGroupTagIndex();
    }

    /// <summary>
    /// Toggles the <see cref="SavedTargetGroup.IsEnabled"/> state of a target group and persists it to SQLite.
    /// </summary>
    public async Task ToggleTargetGroupEnabledAsync(SavedTargetGroup group, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(group);
        SavedTargetGroup updated = group with { IsEnabled = isEnabled };
        await _context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);

        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (string.Equals(this.TargetGroups[i].GroupId, group.GroupId, StringComparison.Ordinal))
            {
                this.TargetGroups[i] = updated;
                break;
            }
        }

        _targetGroupIndex[group.GroupId] = updated;
        string stateWord = isEnabled
            ? _context.Localization["Log.Personal.StateEnabled"]
            : _context.Localization["Log.Personal.StateDisabled"];

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            updated.GroupName,
            _context.Localization.Format("Log.Personal.TargetGroupToggled", stateWord, updated.GroupName)).ConfigureAwait(true);
    }

    private async Task SetAllTargetGroupsEnabledAsync(bool isEnabled)
    {
        string mode = isEnabled ? "enable_all" : "disable_all";
        (System.Collections.Generic.IReadOnlyList<SavedTargetGroup> modified, _, _) =
            _context.GroupTagService.ConfigureTargetGroupsSelection(this.TargetGroups, mode);

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            this.ApplyModifiedTargetGroupsInMemory(modified);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            _context.Localization["Audit.Cat.Group"],
            isEnabled
                ? _context.Localization.Format("Log.TargetGroupsEnabledAll", this.TargetGroups.Count)
                : _context.Localization.Format("Log.TargetGroupsDisabledAll", this.TargetGroups.Count)).ConfigureAwait(true);
    }

    /// <summary>
    /// Batch-enables or disables the specified subset of target groups by <paramref name="groupIds"/> and persists changes to SQLite.
    /// </summary>
    public async Task SetTargetGroupsEnabledBatchAsync(System.Collections.Generic.IReadOnlyList<string> groupIds, bool isEnabled)
    {
        if (groupIds is null || groupIds.Count == 0)
        {
            return;
        }

        System.Collections.Generic.HashSet<string> idSet = new(groupIds.Count, StringComparer.Ordinal);
        for (int i = 0; i < groupIds.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(groupIds[i]))
            {
                _ = idSet.Add(groupIds[i]);
            }
        }

        if (idSet.Count == 0)
        {
            return;
        }

        System.Collections.Generic.List<SavedTargetGroup> modified = new(idSet.Count);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (idSet.Contains(tg.GroupId) && tg.IsEnabled != isEnabled)
            {
                modified.Add(tg with { IsEnabled = isEnabled });
            }
        }

        if (modified.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(modified).ConfigureAwait(true);
            this.ApplyModifiedTargetGroupsInMemory(modified);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            _context.Localization["Audit.Cat.Group"],
            isEnabled
                ? _context.Localization.Format("Log.TargetGroupsEnabledAll", idSet.Count)
                : _context.Localization.Format("Log.TargetGroupsDisabledAll", idSet.Count)).ConfigureAwait(true);
    }

    /// <summary>
    /// Batch-adds the specified discovered <paramref name="groups"/> into the saved campaign target groups and persists them to SQLite.
    /// </summary>
    public async Task AddDiscoveredGroupsBatchToTargetsAsync(System.Collections.Generic.IReadOnlyList<GroupSummary> groups)
    {
        if (groups is null || groups.Count == 0)
        {
            return;
        }

        System.Collections.Generic.List<SavedTargetGroup> toUpsert = new(groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            GroupSummary grp = groups[i];
            if (grp is null || string.IsNullOrWhiteSpace(grp.GroupId))
            {
                continue;
            }

            SavedTargetGroup? existing = this.FindTargetGroupById(grp.GroupId);
            if (existing is null)
            {
                toUpsert.Add(new SavedTargetGroup(
                    grp.GroupId,
                    grp.Name,
                    IsEnabled: true,
                    Notes: null,
                    AvatarUrl: grp.AvatarUrl,
                    TagsCsv: null));
            }
            else if (!existing.IsEnabled)
            {
                toUpsert.Add(existing with
                {
                    GroupName = !string.IsNullOrWhiteSpace(grp.Name) ? grp.Name : existing.GroupName,
                    AvatarUrl = grp.AvatarUrl ?? existing.AvatarUrl,
                    IsEnabled = true
                });
            }
        }

        if (toUpsert.Count == 0)
        {
            return;
        }

        await _context.AccountStore.UpsertTargetGroupsBatchAsync(toUpsert).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Audit.Cat.Group"],
            _context.Localization.Format("Log.TargetGroupsEnabledAll", toUpsert.Count)).ConfigureAwait(true);
    }

    private void ApplyModifiedTargetGroupsInMemory(System.Collections.Generic.IReadOnlyList<SavedTargetGroup> modified)
    {
        if (modified.Count == 0)
        {
            return;
        }

        System.Collections.Generic.Dictionary<string, SavedTargetGroup> modMap =
            new(modified.Count, StringComparer.Ordinal);
        for (int i = 0; i < modified.Count; i++)
        {
            modMap[modified[i].GroupId] = modified[i];
        }

        System.Collections.Generic.List<SavedTargetGroup> nextList = new(this.TargetGroups.Count);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup current = this.TargetGroups[i];
            nextList.Add(modMap.TryGetValue(current.GroupId, out SavedTargetGroup? updated) ? updated : current);
        }

        this.TargetGroups.ReplaceAll(nextList);
        this.RebuildTargetGroupIndex();
    }

    private async Task SyncJoinedGroupsAsync()
    {
        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            await this.SyncJoinedGroupsCoreAsync(session, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization.Format("Log.GroupsSyncFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SyncJoinedGroupsCoreAsync(IBrowserSession session, System.Threading.CancellationToken ct = default)
    {
        PagedResult<GroupSummary> result = await _context.CometClient
            .GetJoinedGroupsAsync(session, count: 500, ct: ct)
            .ConfigureAwait(true);

        if (this.SelectedAccount is not null && session.CurrentMaterial is not null)
        {
            SessionMaterial mat = session.CurrentMaterial;
            bool isGenericName =
                this.SelectedAccount.DisplayName.StartsWith("Tài khoản", StringComparison.OrdinalIgnoreCase) ||
                this.SelectedAccount.DisplayName.StartsWith("Account", StringComparison.OrdinalIgnoreCase) ||
                this.SelectedAccount.DisplayName.StartsWith("Facebook", StringComparison.OrdinalIgnoreCase);

            string nextName = !string.IsNullOrWhiteSpace(mat.DisplayName) && isGenericName
                ? mat.DisplayName
                : this.SelectedAccount.DisplayName;
            string? nextAvatar = !string.IsNullOrWhiteSpace(mat.AvatarUrl)
                ? mat.AvatarUrl
                : this.SelectedAccount.AvatarUrl;

            if (!string.Equals(nextName, this.SelectedAccount.DisplayName, StringComparison.Ordinal) ||
                !string.Equals(nextAvatar, this.SelectedAccount.AvatarUrl, StringComparison.Ordinal))
            {
                AccountProfile updatedAcc = this.SelectedAccount with
                {
                    DisplayName = nextName,
                    AvatarUrl = nextAvatar
                };
                await _context.AccountStore.UpsertAccountAsync(updatedAcc, ct).ConfigureAwait(true);
                this.SelectedAccount = updatedAcc;
            }
        }

        System.Collections.Generic.Dictionary<string, SavedTargetGroup> existingTargets = new(StringComparer.Ordinal);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            existingTargets[this.TargetGroups[i].GroupId] = this.TargetGroups[i];
        }

        _allCatalogGroups.Clear();
        System.Collections.Generic.List<GroupSummary> nextCatalog = new(result.Items.Count);
        System.Collections.Generic.List<SavedTargetGroup> batchTargets = new(result.Items.Count);
        for (int i = 0; i < result.Items.Count; i++)
        {
            GroupSummary group = result.Items[i];
            _allCatalogGroups.Add(group);
            nextCatalog.Add(group);

            bool hasExisting = existingTargets.TryGetValue(group.GroupId, out SavedTargetGroup? prev);
            SavedTargetGroup target = new(
                group.GroupId,
                group.Name,
                IsEnabled: !hasExisting || prev!.IsEnabled,
                Notes: hasExisting ? prev!.Notes : null,
                AvatarUrl: group.AvatarUrl ?? prev?.AvatarUrl,
                TagsCsv: hasExisting ? prev!.TagsCsv : null);
            batchTargets.Add(target);
        }

        if (batchTargets.Count > 0)
        {
            await _context.AccountStore.UpsertTargetGroupsBatchAsync(batchTargets, ct).ConfigureAwait(true);
        }

        this.JoinedGroups.ReplaceAll(nextCatalog);

        if (this.JoinedGroups.Count > 0)
        {
            this.SelectedGroup = this.JoinedGroups[0];
        }

        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            _context.Localization["Audit.Cat.Group"],
            _context.Localization.Format("Log.GroupsSyncedCount", this.JoinedGroups.Count)).ConfigureAwait(true);
    }

    private async Task SearchGroupsByKeywordAsync()
    {
        if (string.IsNullOrWhiteSpace(this.GroupSearchKeyword))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.EnterGroupKeyword"]).ConfigureAwait(true);
            return;
        }

        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            string keyword = this.GroupSearchKeyword.Trim();
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Info,
                keyword,
                _context.Localization.Format("Log.SearchingGroups", keyword)).ConfigureAwait(true);

            PagedResult<GroupSummary> result = await _context.CometClient
                .SearchGroupsAsync(session, keyword, count: 35, ct: ct)
                .ConfigureAwait(true);

            _allCatalogGroups.Clear();
            for (int i = 0; i < result.Items.Count; i++)
            {
                _allCatalogGroups.Add(result.Items[i]);
            }

            this.JoinedGroups.ReplaceAll(_allCatalogGroups);

            if (this.JoinedGroups.Count > 0)
            {
                this.SelectedGroup = this.JoinedGroups[0];
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                keyword,
                _context.Localization.Format("Log.GroupsSearchFound", this.JoinedGroups.Count, keyword)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.GroupSearchKeyword,
                _context.Localization.Format("Log.GroupsSearchFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    /// <summary>
    /// Adds the specified <paramref name="group"/> to the saved campaign target groups.
    /// </summary>
    public async Task AddGroupToTargetsAsync(GroupSummary? group)
    {
        if (group is null)
        {
            return;
        }

        this.SelectedGroup = group;
        await this.AddSelectedGroupToTargetsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the specified <paramref name="targetGroup"/> from the saved campaign target groups.
    /// </summary>
    public async Task RemoveTargetGroupAsync(SavedTargetGroup? targetGroup)
    {
        if (targetGroup is null)
        {
            return;
        }

        this.SelectedTargetGroup = targetGroup;
        await this.RemoveSelectedTargetGroupAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Requests to join the specified <paramref name="group"/> using the active browser session.
    /// </summary>
    public async Task JoinGroupItemAsync(GroupSummary? group)
    {
        if (group is null)
        {
            return;
        }

        this.SelectedGroup = group;
        await this.JoinSelectedGroupAsync().ConfigureAwait(true);
    }

    private async Task AddSelectedGroupToTargetsAsync()
    {
        if (this.SelectedGroup is null)
        {
            return;
        }

        SavedTargetGroup target = new(this.SelectedGroup.GroupId, this.SelectedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: this.SelectedGroup.AvatarUrl);
        await _context.AccountStore.UpsertTargetGroupAsync(target).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            target.GroupName,
            _context.Localization.Format("Log.TargetGroupAdded", target.GroupName)).ConfigureAwait(true);
    }

    private async Task RemoveSelectedTargetGroupAsync()
    {
        if (this.SelectedTargetGroup is null)
        {
            return;
        }

        string name = this.SelectedTargetGroup.GroupName;
        _ = await _context.AccountStore.DeleteTargetGroupAsync(this.SelectedTargetGroup.GroupId).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            name,
            _context.Localization.Format("Log.TargetGroupRemoved", name)).ConfigureAwait(true);
    }

    private async Task JoinSelectedGroupAsync()
    {
        if (this.SelectedGroup is null)
        {
            return;
        }

        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            GroupSummary group = this.SelectedGroup;
            GroupMembershipState state = await _context.CometClient
                .JoinGroupAsync(session, group.GroupId, ct: ct)
                .ConfigureAwait(true);

            GroupSummary updatedGroup = group with { MembershipState = state };
            this.UpdateCatalogGroupState(updatedGroup);
            this.SelectedGroup = updatedGroup;

            if (state == GroupMembershipState.Member)
            {
                SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: updatedGroup.AvatarUrl);
                await _context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
            }

            string friendlyState = _context.Localization.FormatGroupMembership(state);
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                group.Name,
                _context.Localization.Format("Log.GroupJoinRequested", group.Name, friendlyState)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.SelectedGroup?.Name ?? _context.Localization["Audit.Cat.Group"],
                _context.Localization.Format("Log.GroupJoinFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    /// <summary>
    /// Batch auto-joins all discovered groups in <see cref="JoinedGroups"/> that are not yet joined,
    /// enforcing safe anti-checkpoint delays and supporting immediate cancellation.
    /// </summary>
    public async Task AutoJoinDiscoveredGroupsAsync()
    {
        System.Collections.Generic.List<GroupSummary> candidates = new();
        for (int i = 0; i < this.JoinedGroups.Count; i++)
        {
            if (this.JoinedGroups[i].MembershipState != GroupMembershipState.Member &&
                this.JoinedGroups[i].MembershipState != GroupMembershipState.Requested)
            {
                candidates.Add(this.JoinedGroups[i]);
            }
        }

        if (candidates.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.NoUnjoinedGroups"]).ConfigureAwait(true);
            return;
        }

        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            int joinedOrRequested = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(candidates[i].Name, ct).ConfigureAwait(true);
                }

                GroupSummary group = candidates[i];
                GroupMembershipState state = await _context.CometClient
                    .JoinGroupAsync(session, group.GroupId, ct: ct)
                    .ConfigureAwait(true);

                GroupSummary updatedGroup = group with { MembershipState = state };
                this.UpdateCatalogGroupState(updatedGroup);
                joinedOrRequested++;

                if (state == GroupMembershipState.Member)
                {
                    SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: updatedGroup.AvatarUrl);
                    await _context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                }

                string friendlyState = _context.Localization.FormatGroupMembership(state);
                _ = await this.LogAuditAsync(
                    AuditCategory.Group,
                    AuditSeverity.Success,
                    group.Name,
                    _context.Localization.Format("Log.BatchGroupJoined", joinedOrRequested, candidates.Count, group.Name, friendlyState)).ConfigureAwait(true);
            }

            await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization.Format("Log.BatchGroupJoinFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private void UpdateCatalogGroupState(GroupSummary updated)
    {
        for (int i = 0; i < _allCatalogGroups.Count; i++)
        {
            if (string.Equals(_allCatalogGroups[i].GroupId, updated.GroupId, StringComparison.Ordinal))
            {
                _allCatalogGroups[i] = updated;
                break;
            }
        }

        for (int i = 0; i < this.JoinedGroups.Count; i++)
        {
            if (string.Equals(this.JoinedGroups[i].GroupId, updated.GroupId, StringComparison.Ordinal))
            {
                this.JoinedGroups[i] = updated;
                break;
            }
        }
    }

    private async Task ReloadTargetGroupsOnlyAsync()
    {
        System.Collections.Generic.IReadOnlyList<SavedTargetGroup> stored =
            await _context.AccountStore.GetAllTargetGroupsAsync().ConfigureAwait(true);

        this.TargetGroups.ReplaceAll(stored);
        this.RebuildTargetGroupIndex();

        if (this.SelectedTargetGroup is null && this.TargetGroups.Count > 0)
        {
            this.SelectedTargetGroup = this.TargetGroups[0];
        }
    }
}
