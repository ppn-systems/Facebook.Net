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

    private readonly System.Collections.Generic.List<GroupSummary> _allCatalogGroups = new();

    /// <summary>
    /// Filters the displayed <see cref="JoinedGroups"/> catalog locally by <see cref="GroupFilterText"/>.
    /// </summary>
    public void ApplyLocalGroupFilter(string? filterText)
    {
        this.GroupFilterText = filterText ?? string.Empty;
        string kw = this.GroupFilterText.Trim();

        if (string.IsNullOrWhiteSpace(kw))
        {
            this.JoinedGroups.ReplaceAll(this._allCatalogGroups);
            return;
        }

        System.Collections.Generic.List<GroupSummary> filtered = new(this._allCatalogGroups.Count);
        for (int i = 0; i < this._allCatalogGroups.Count; i++)
        {
            GroupSummary grp = this._allCatalogGroups[i];
            if (grp.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(grp);
            }
        }

        this.JoinedGroups.ReplaceAll(filtered);
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
                ? this.Msg($"Đã bật tất cả {this.TargetGroups.Count} nhóm mục tiêu.", $"Enabled all {this.TargetGroups.Count} target groups.")
                : this.Msg($"Đã tạm tắt tất cả {this.TargetGroups.Count} nhóm mục tiêu.", $"Disabled all {this.TargetGroups.Count} target groups.")).ConfigureAwait(true);
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
                this.Msg($"Lỗi tải danh sách nhóm: {this.SanitizeErrorMessage(ex)}", $"Failed to sync groups: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SyncJoinedGroupsCoreAsync(IBrowserSession session, System.Threading.CancellationToken ct = default)
    {
        PagedResult<GroupSummary> result = await this._context.CometClient
            .GetJoinedGroupsAsync(session, count: 30, ct: ct)
            .ConfigureAwait(true);

        this._allCatalogGroups.Clear();
        this.JoinedGroups.Clear();
        for (int i = 0; i < result.Items.Count; i++)
        {
            GroupSummary group = result.Items[i];
            this._allCatalogGroups.Add(group);
            this.JoinedGroups.Add(group);

            // Auto-save into SQLite TargetGroups so they persist across app restarts
            SavedTargetGroup target = new(group.GroupId, group.Name, IsEnabled: true);
            await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
        }

        if (this.JoinedGroups.Count > 0)
        {
            this.SelectedGroup = this.JoinedGroups[0];
        }

        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            this._context.Localization["Audit.Cat.Group"],
            this.Msg(
                $"Đã tự động đồng bộ {this.JoinedGroups.Count} nhóm đã tham gia vào danh sách chọn trực quan.",
                $"Automatically synced {this.JoinedGroups.Count} joined groups into the visual catalog.")).ConfigureAwait(true);
    }

    private async Task SearchGroupsByKeywordAsync()
    {
        if (string.IsNullOrWhiteSpace(this.GroupSearchKeyword))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Group"],
                this.Msg(
                    "Vui lòng nhập từ khóa tìm nhóm (VD: Chợ cư dân, Mua bán nhà đất...).",
                    "Please enter a group search keyword (e.g., Real Estate Deals, Laptop Market...).")).ConfigureAwait(true);
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
                this.Msg(
                    $"Đang tìm kiếm các nhóm Facebook theo từ khóa '{keyword}'...",
                    $"Searching Facebook groups matching '{keyword}'...")).ConfigureAwait(true);

            PagedResult<GroupSummary> result = await this._context.CometClient
                .SearchGroupsAsync(session, keyword, count: 15, ct: ct)
                .ConfigureAwait(true);

            this._allCatalogGroups.Clear();
            this.JoinedGroups.Clear();
            for (int i = 0; i < result.Items.Count; i++)
            {
                this._allCatalogGroups.Add(result.Items[i]);
                this.JoinedGroups.Add(result.Items[i]);
            }

            if (this.JoinedGroups.Count > 0)
            {
                this.SelectedGroup = this.JoinedGroups[0];
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                keyword,
                this.Msg(
                    $"Tìm thấy {this.JoinedGroups.Count} nhóm khớp từ khóa '{keyword}'.",
                    $"Found {this.JoinedGroups.Count} groups matching '{keyword}'.")).ConfigureAwait(true);
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
                this.Msg($"Lỗi tìm kiếm nhóm: {this.SanitizeErrorMessage(ex)}", $"Group search failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
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

        SavedTargetGroup target = new(this.SelectedGroup.GroupId, this.SelectedGroup.Name, IsEnabled: true);
        await this._context.AccountStore.UpsertTargetGroupAsync(target).ConfigureAwait(true);
        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            target.GroupName,
            this.Msg(
                $"Đã đưa nhóm '{target.GroupName}' vào danh sách Nhóm Mục tiêu.",
                $"Added group '{target.GroupName}' to Target Campaign Groups.")).ConfigureAwait(true);
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
            this.Msg(
                $"Đã xóa nhóm '{name}' khỏi danh sách Nhóm Mục tiêu.",
                $"Removed group '{name}' from Target Campaign Groups.")).ConfigureAwait(true);
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
                SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true);
                await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);
            }

            string friendlyState = this._context.Localization.FormatGroupMembership(state);
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                group.Name,
                this.Msg(
                    $"Đã gửi yêu cầu tham gia nhóm '{group.Name}' ({friendlyState}).",
                    $"Sent request to join group '{group.Name}' ({friendlyState}).")).ConfigureAwait(true);
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
                this.Msg($"Lỗi tham gia nhóm: {this.SanitizeErrorMessage(ex)}", $"Failed to join group: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
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
                this.Msg(
                    "Không có nhóm mới nào chưa tham gia trong danh sách hiện tại — hãy tìm kiếm nhóm theo từ khóa trước.",
                    "No unjoined groups in the current catalog — please search for groups by keyword first.")).ConfigureAwait(true);
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
                    SavedTargetGroup target = new(updatedGroup.GroupId, updatedGroup.Name, IsEnabled: true);
                    await this._context.AccountStore.UpsertTargetGroupAsync(target, ct).ConfigureAwait(true);
                }

                string friendlyState = this._context.Localization.FormatGroupMembership(state);
                _ = await this.LogAuditAsync(
                    AuditCategory.Group,
                    AuditSeverity.Success,
                    group.Name,
                    this.Msg(
                        $"[{joinedOrRequested}/{candidates.Count}] Đã tham gia nhóm '{group.Name}' ({friendlyState}).",
                        $"[{joinedOrRequested}/{candidates.Count}] Joined group '{group.Name}' ({friendlyState}).")).ConfigureAwait(true);
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
                this.Msg($"Lỗi tham gia nhóm hàng loạt: {this.SanitizeErrorMessage(ex)}", $"Batch group join failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
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
