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

    private async Task SyncJoinedGroupsAsync()
    {
        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            await this.SyncJoinedGroupsCoreAsync(session).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                "Đồng bộ Nhóm",
                $"Lỗi tải danh sách nhóm: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task SyncJoinedGroupsCoreAsync(IBrowserSession session)
    {
        PagedResult<GroupSummary> result = await this._context.CometClient
            .GetJoinedGroupsAsync(session, count: 30)
            .ConfigureAwait(true);

        this.JoinedGroups.Clear();
        for (int i = 0; i < result.Items.Count; i++)
        {
            GroupSummary group = result.Items[i];
            this.JoinedGroups.Add(group);

            // Auto-save into SQLite TargetGroups so they persist across app restarts
            SavedTargetGroup target = new(group.GroupId, group.Name, IsEnabled: true);
            await this._context.AccountStore.UpsertTargetGroupAsync(target).ConfigureAwait(true);
        }

        if (this.JoinedGroups.Count > 0)
        {
            this.SelectedGroup = this.JoinedGroups[0];
        }

        await this.ReloadTargetGroupsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Group,
            AuditSeverity.Success,
            "Danh mục Nhóm",
            $"Đã tự động đồng bộ {this.JoinedGroups.Count} nhóm đã tham gia vào danh sách chọn trực quan.").ConfigureAwait(true);
    }

    private async Task SearchGroupsByKeywordAsync()
    {
        if (string.IsNullOrWhiteSpace(this.GroupSearchKeyword))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Warning,
                "Tìm Nhóm",
                "Vui lòng nhập từ khóa tìm nhóm (VD: Chợ cư dân, Mua bán nhà đất...).").ConfigureAwait(true);
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            string keyword = this.GroupSearchKeyword.Trim();
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Info,
                keyword,
                $"Đang tìm kiếm các nhóm Facebook theo từ khóa '{keyword}'...").ConfigureAwait(true);

            PagedResult<GroupSummary> result = await this._context.CometClient
                .SearchGroupsAsync(session, keyword, count: 15)
                .ConfigureAwait(true);

            this.JoinedGroups.Clear();
            for (int i = 0; i < result.Items.Count; i++)
            {
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
                $"Tìm thấy {this.JoinedGroups.Count} nhóm khớp từ khóa '{keyword}'.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.GroupSearchKeyword,
                $"Lỗi tìm kiếm nhóm: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
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
            $"Đã đưa nhóm '{target.GroupName}' vào danh sách Nhóm Mục tiêu.").ConfigureAwait(true);
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
            $"Đã xóa nhóm '{name}' khỏi danh sách Nhóm Mục tiêu.").ConfigureAwait(true);
    }

    private async Task JoinSelectedGroupAsync()
    {
        if (this.SelectedGroup is null)
        {
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            GroupSummary group = this.SelectedGroup;
            GroupMembershipState state = await this._context.CometClient
                .JoinGroupAsync(session, group.GroupId)
                .ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Success,
                group.Name,
                $"Đã gửi yêu cầu tham gia nhóm '{group.Name}' (Trạng thái: {state}).").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Group,
                AuditSeverity.Error,
                this.SelectedGroup?.Name ?? "Nhóm",
                $"Lỗi tham gia nhóm: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
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
