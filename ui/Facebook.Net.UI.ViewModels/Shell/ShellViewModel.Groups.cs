// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.UI.ViewModels.Shell;

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
    private GroupMembershipState? _activeStateFilter;

    /// <summary>
    /// Filters the displayed <see cref="JoinedGroups"/> catalog locally by <see cref="GroupFilterText"/>,
    /// optional membership state, and optional tag filter.
    /// </summary>
    public void ApplyLocalGroupFilter(string? filterText, GroupMembershipState? stateFilter = null, string? tagIdFilter = "__KEEP__")
    {
        if (this._allCatalogGroups.Count == 0 && this.JoinedGroups.Count > 0)
        {
            for (int i = 0; i < this.JoinedGroups.Count; i++)
            {
                this._allCatalogGroups.Add(this.JoinedGroups[i]);
            }
        }

        if (filterText is not null)
        {
            this.GroupFilterText = filterText;
        }

        if (stateFilter.HasValue || filterText is null)
        {
            this._activeStateFilter = stateFilter;
        }

        if (!string.Equals(tagIdFilter, "__KEEP__", StringComparison.Ordinal))
        {
            this.ActiveTagFilterId = tagIdFilter;
        }

        string kw = this.GroupFilterText.Trim();
        GroupMembershipState? state = this._activeStateFilter;
        string? tagId = this.ActiveTagFilterId;

        if (string.IsNullOrWhiteSpace(kw) && state is null && string.IsNullOrWhiteSpace(tagId))
        {
            this.JoinedGroups.ReplaceAll(this._allCatalogGroups);
            return;
        }

        System.Collections.Generic.List<GroupSummary> filtered = new(this._allCatalogGroups.Count);
        for (int i = 0; i < this._allCatalogGroups.Count; i++)
        {
            GroupSummary grp = this._allCatalogGroups[i];
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

            if (!string.IsNullOrWhiteSpace(kw) && !grp.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(grp);
        }

        this.JoinedGroups.ReplaceAll(filtered);
    }

    /// <summary>
    /// Finds a saved target group by <paramref name="groupId"/> if present.
    /// </summary>
    public SavedTargetGroup? FindTargetGroupById(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return null;
        }

        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (string.Equals(this.TargetGroups[i].GroupId, groupId, StringComparison.Ordinal))
            {
                return this.TargetGroups[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the list of resolved <see cref="GroupTag"/> badges assigned to a group.
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
            string id = tagIds[i];
            for (int j = 0; j < this.GroupTags.Count; j++)
            {
                if (string.Equals(this.GroupTags[j].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    resolved.Add(this.GroupTags[j]);
                    break;
                }
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

        string color = string.IsNullOrWhiteSpace(this.NewGroupTagColorHex) ? "#F6821F" : this.NewGroupTagColorHex.Trim();
        GroupTag tag = new(
            Id: $"tag_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name: name,
            ColorHex: color);

        await this._context.AccountStore.UpsertGroupTagAsync(tag).ConfigureAwait(true);
        this.NewGroupTagName = string.Empty;
        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            tag.Name,
            this._context.Localization.Format("Log.GroupTagCreated", tag.Name)).ConfigureAwait(true);
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

        _ = await this._context.AccountStore.DeleteGroupTagAsync(tag.Id).ConfigureAwait(true);

        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (tg.HasTag(tag.Id))
            {
                System.Collections.Generic.List<string> remaining = new();
                System.Collections.Generic.IReadOnlyList<string> currentIds = tg.GetTagIds();
                for (int j = 0; j < currentIds.Count; j++)
                {
                    if (!string.Equals(currentIds[j], tag.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        remaining.Add(currentIds[j]);
                    }
                }

                SavedTargetGroup updated = tg with { TagsCsv = remaining.Count > 0 ? string.Join(',', remaining) : null };
                await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);
                this.TargetGroups[i] = updated;
            }
        }

        if (string.Equals(this.ActiveTagFilterId, tag.Id, StringComparison.OrdinalIgnoreCase))
        {
            this.ActiveTagFilterId = null;
        }

        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);
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

        System.Collections.Generic.List<string> ids = [.. existing.GetTagIds()];
        bool removed = false;
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
            {
                ids.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            ids.Add(tagId);
        }

        SavedTargetGroup updated = existing with
        {
            GroupName = !string.IsNullOrWhiteSpace(groupName) ? groupName : existing.GroupName,
            AvatarUrl = existing.AvatarUrl ?? avatarUrl,
            TagsCsv = ids.Count > 0 ? string.Join(',', ids) : null
        };

        await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);

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

        int taggedCount = 0;
        for (int i = 0; i < this.JoinedGroups.Count; i++)
        {
            GroupSummary grp = this.JoinedGroups[i];
            SavedTargetGroup? tg = this.FindTargetGroupById(grp.GroupId);
            if (tg is null || !tg.HasTag(tagId))
            {
                await this.ToggleGroupTagAsync(grp.GroupId, grp.Name, grp.AvatarUrl, tagId).ConfigureAwait(true);
                taggedCount++;
            }
        }

        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            this._context.Localization["Audit.Cat.Group"],
            this._context.Localization.Format("Log.BatchTaggedGroups", taggedCount)).ConfigureAwait(true);
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
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup tg = this.TargetGroups[i];
            if (tg.HasTag(tagId) && tg.IsEnabled != targetState)
            {
                SavedTargetGroup updated = tg with { IsEnabled = targetState };
                await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);
                nextList.Add(updated);
            }
            else
            {
                nextList.Add(tg);
            }
        }

        this.TargetGroups.ReplaceAll(nextList);
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

        int count = Math.Clamp(randomCount, 1, this.TargetGroups.Count);
        System.Collections.Generic.List<int> indices = new(this.TargetGroups.Count);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            indices.Add(i);
        }

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int swapIdx = Random.Shared.Next(i + 1);
            (indices[i], indices[swapIdx]) = (indices[swapIdx], indices[i]);
        }

        System.Collections.Generic.HashSet<int> chosen = new();
        for (int i = 0; i < count; i++)
        {
            _ = chosen.Add(indices[i]);
        }

        System.Collections.Generic.List<SavedTargetGroup> nextList = new(this.TargetGroups.Count);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            bool shouldEnable = chosen.Contains(i);
            SavedTargetGroup tg = this.TargetGroups[i];
            if (tg.IsEnabled != shouldEnable)
            {
                SavedTargetGroup updated = tg with { IsEnabled = shouldEnable };
                await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);
                nextList.Add(updated);
            }
            else
            {
                nextList.Add(tg);
            }
        }

        this.TargetGroups.ReplaceAll(nextList);
    }

    private async Task ReloadGroupTagsOnlyAsync()
    {
        System.Collections.Generic.IReadOnlyList<GroupTag> tags =
            await this._context.AccountStore.GetAllGroupTagsAsync().ConfigureAwait(true);
        this.GroupTags.ReplaceAll(tags);
    }

    /// <summary>
    /// Toggles the <see cref="SavedTargetGroup.IsEnabled"/> state of a target group and persists it to SQLite.
    /// </summary>
    public async Task ToggleTargetGroupEnabledAsync(SavedTargetGroup group, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(group);
        SavedTargetGroup updated = group with { IsEnabled = isEnabled };
        await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);

        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (string.Equals(this.TargetGroups[i].GroupId, group.GroupId, StringComparison.Ordinal))
            {
                this.TargetGroups[i] = updated;
                break;
            }
        }
    }

    private async Task SetAllTargetGroupsEnabledAsync(bool isEnabled)
    {
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            SavedTargetGroup updated = this.TargetGroups[i] with { IsEnabled = isEnabled };
            await this._context.AccountStore.UpsertTargetGroupAsync(updated).ConfigureAwait(true);
            this.TargetGroups[i] = updated;
        }

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Info,
            this._context.Localization["Audit.Cat.Group"],
            isEnabled
                ? this._context.Localization.Format("Log.TargetGroupsEnabledAll", this.TargetGroups.Count)
                : this._context.Localization.Format("Log.TargetGroupsDisabledAll", this.TargetGroups.Count)).ConfigureAwait(true);
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
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization.Format("Log.GroupsSyncFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SyncJoinedGroupsCoreAsync(IBrowserSession session, System.Threading.CancellationToken ct = default)
    {
        PagedResult<GroupSummary> result = await this._context.CometClient
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
                await this._context.AccountStore.UpsertAccountAsync(updatedAcc, ct).ConfigureAwait(true);
                this.SelectedAccount = updatedAcc;
            }
        }

        System.Collections.Generic.Dictionary<string, SavedTargetGroup> existingTargets = new(StringComparer.Ordinal);
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            existingTargets[this.TargetGroups[i].GroupId] = this.TargetGroups[i];
        }

        this._allCatalogGroups.Clear();
        System.Collections.Generic.List<GroupSummary> nextCatalog = new(result.Items.Count);
        for (int i = 0; i < result.Items.Count; i++)
        {
            GroupSummary group = result.Items[i];
            this._allCatalogGroups.Add(group);
            nextCatalog.Add(group);

            bool hasExisting = existingTargets.TryGetValue(group.GroupId, out SavedTargetGroup? prev);
            SavedTargetGroup target = new(
                group.GroupId,
                group.Name,
                IsEnabled: !hasExisting || prev!.IsEnabled,
                Notes: hasExisting ? prev!.Notes : null,
                AvatarUrl: group.AvatarUrl ?? prev?.AvatarUrl,
                TagsCsv: hasExisting ? prev!.TagsCsv : null);
            await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
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
            this._context.Localization["Audit.Cat.Group"],
            this._context.Localization.Format("Log.GroupsSyncedCount", this.JoinedGroups.Count)).ConfigureAwait(true);
    }

    private async Task SearchGroupsByKeywordAsync()
    {
        if (string.IsNullOrWhiteSpace(this.GroupSearchKeyword))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.EnterGroupKeyword"]).ConfigureAwait(true);
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
                this._context.Localization.Format("Log.SearchingGroups", keyword)).ConfigureAwait(true);

            PagedResult<GroupSummary> result = await this._context.CometClient
                .SearchGroupsAsync(session, keyword, count: 35, ct: ct)
                .ConfigureAwait(true);

            this._allCatalogGroups.Clear();
            for (int i = 0; i < result.Items.Count; i++)
            {
                this._allCatalogGroups.Add(result.Items[i]);
            }

            this.JoinedGroups.ReplaceAll(this._allCatalogGroups);

            if (this.JoinedGroups.Count > 0)
            {
                this.SelectedGroup = this.JoinedGroups[0];
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                keyword,
                this._context.Localization.Format("Log.GroupsSearchFound", this.JoinedGroups.Count, keyword)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.GroupSearchKeyword,
                this._context.Localization.Format("Log.GroupsSearchFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task AddSelectedGroupToTargetsAsync()
    {
        if (this.SelectedGroup is null)
        {
            return;
        }

        SavedTargetGroup target = new(this.SelectedGroup.GroupId, this.SelectedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: this.SelectedGroup.AvatarUrl);
        await this._context.AccountStore.UpsertTargetGroupAsync(target).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            target.GroupName,
            this._context.Localization.Format("Log.TargetGroupAdded", target.GroupName)).ConfigureAwait(true);
    }

    private async Task RemoveSelectedTargetGroupAsync()
    {
        if (this.SelectedTargetGroup is null)
        {
            return;
        }

        string name = this.SelectedTargetGroup.GroupName;
        _ = await this._context.AccountStore.DeleteTargetGroupAsync(this.SelectedTargetGroup.GroupId).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Info,
            name,
            this._context.Localization.Format("Log.TargetGroupRemoved", name)).ConfigureAwait(true);
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
            GroupMembershipState state = await this._context.CometClient
                .JoinGroupAsync(session, group.GroupId, ct: ct)
                .ConfigureAwait(true);

            GroupSummary updatedGroup = group with { MembershipState = state };
            this.UpdateCatalogGroupState(updatedGroup);
            this.SelectedGroup = updatedGroup;

            if (state == GroupMembershipState.Member)
            {
                SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: updatedGroup.AvatarUrl);
                await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
            }

            string friendlyState = this._context.Localization.FormatGroupMembership(state);
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                group.Name,
                this._context.Localization.Format("Log.GroupJoinRequested", group.Name, friendlyState)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.SelectedGroup?.Name ?? this._context.Localization["Audit.Cat.Group"],
                this._context.Localization.Format("Log.GroupJoinFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
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
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.NoUnjoinedGroups"]).ConfigureAwait(true);
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
                GroupMembershipState state = await this._context.CometClient
                    .JoinGroupAsync(session, group.GroupId, ct: ct)
                    .ConfigureAwait(true);

                GroupSummary updatedGroup = group with { MembershipState = state };
                this.UpdateCatalogGroupState(updatedGroup);
                joinedOrRequested++;

                if (state == GroupMembershipState.Member)
                {
                    SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: updatedGroup.AvatarUrl);
                    await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                }

                string friendlyState = this._context.Localization.FormatGroupMembership(state);
                _ = await this.LogAuditAsync(
                    AuditCategory.Group,
                    AuditSeverity.Success,
                    group.Name,
                    this._context.Localization.Format("Log.BatchGroupJoined", joinedOrRequested, candidates.Count, group.Name, friendlyState)).ConfigureAwait(true);
            }

            await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this._context.Localization["Audit.Cat.Group"],
                this._context.Localization.Format("Log.BatchGroupJoinFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private void UpdateCatalogGroupState(GroupSummary updated)
    {
        for (int i = 0; i < this._allCatalogGroups.Count; i++)
        {
            if (string.Equals(this._allCatalogGroups[i].GroupId, updated.GroupId, StringComparison.Ordinal))
            {
                this._allCatalogGroups[i] = updated;
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
            await this._context.AccountStore.GetAllTargetGroupsAsync().ConfigureAwait(true);

        this.TargetGroups.Clear();
        for (int i = 0; i < stored.Count; i++)
        {
            this.TargetGroups.Add(stored[i]);
        }

        if (this.SelectedTargetGroup is null && this.TargetGroups.Count > 0)
        {
            this.SelectedTargetGroup = this.TargetGroups[0];
        }
    }
}
