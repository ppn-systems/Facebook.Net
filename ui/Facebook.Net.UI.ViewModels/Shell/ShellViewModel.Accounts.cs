// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Importing;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Zero-UID Account Management,
/// Universal J2Team/Cookie importing, and 1-Click Browser Login with automatic Group discovery.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets the currently selected account profile.</summary>
    [ObservableProperty]
    public partial AccountProfile? SelectedAccount { get; set; }

    /// <summary>Gets or sets the friendly account label entered by the user (optional).</summary>
    [ObservableProperty]
    public partial string AccountFriendlyNameInput { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional J2Team JSON or raw Cookie string pasted by the user.</summary>
    [ObservableProperty]
    public partial string SessionImportRawInput { get; set; } = string.Empty;

    private async Task ReloadAllStoredDataAsync()
    {
        IReadOnlyList<AccountProfile> storedAccounts = await this._context.AccountStore.GetAllAccountsAsync().ConfigureAwait(true);
        this.Accounts.Clear();
        for (int i = 0; i < storedAccounts.Count; i++)
        {
            this.Accounts.Add(storedAccounts[i]);
        }

        if (this.SelectedAccount is null && this.Accounts.Count > 0)
        {
            this.SelectedAccount = this.Accounts[0];
        }

        IReadOnlyList<SavedTargetGroup> storedGroups = await this._context.AccountStore.GetAllTargetGroupsAsync().ConfigureAwait(true);
        this.TargetGroups.Clear();
        this.JoinedGroups.Clear();
        for (int i = 0; i < storedGroups.Count; i++)
        {
            this.TargetGroups.Add(storedGroups[i]);
            this.JoinedGroups.Add(new GroupSummary(storedGroups[i].GroupId, storedGroups[i].GroupName, GroupMembershipState.Member));
        }

        if (this.SelectedGroup is null && this.JoinedGroups.Count > 0)
        {
            this.SelectedGroup = this.JoinedGroups[0];
        }

        IReadOnlyList<CampaignTemplate> storedTemplates = await this._context.AccountStore.GetAllCampaignTemplatesAsync().ConfigureAwait(true);
        this.SavedTemplates.Clear();
        for (int i = 0; i < storedTemplates.Count; i++)
        {
            this.SavedTemplates.Add(storedTemplates[i]);
        }
    }

    /// <summary>
    /// Imports a session from J2Team JSON, Cookie-Editor JSON, pipe-delimited string, or creates a new browser login profile.
    /// </summary>
    public async Task QuickImportSessionAsync()
    {
        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(
            this.SessionImportRawInput,
            this.AccountFriendlyNameInput);

        string profileDir = Path.Combine(this._context.ProfilesRootDirectory, bundle.AccountUid);
        _ = Directory.CreateDirectory(profileDir);

        string resolvedUa = !string.IsNullOrWhiteSpace(bundle.UserAgent)
            ? bundle.UserAgent
            : (OperatingSystem.IsMacOS()
                ? ProtocolConstants.Protocol.DefaultMacOsUserAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent);

        AccountProfile profile = new(
            Uid: bundle.AccountUid,
            DisplayName: bundle.SuggestedDisplayName,
            CookieHeader: bundle.CookieHeader,
            UserAgent: resolvedUa,
            ProfileDirectory: profileDir,
            ProxyId: null,
            TwoFactorSecret: bundle.TwoFactorSecret,
            Status: string.IsNullOrWhiteSpace(bundle.CookieHeader) ? AccountStatus.Idle : AccountStatus.Active,
            LastActiveAt: DateTimeOffset.UtcNow);

        await this._context.AccountStore.UpsertAccountAsync(profile).ConfigureAwait(true);
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);
        this.SelectedAccount = profile;

        this.AccountFriendlyNameInput = string.Empty;
        this.SessionImportRawInput = string.Empty;

        _ = await this.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Success,
            profile.DisplayName,
            $"Đã lưu phiên đăng nhập cho tài khoản '{profile.DisplayName}' thành công.").ConfigureAwait(true);
    }

    private async Task OpenBrowserAndSyncAsync()
    {
        if (this.SelectedAccount is null)
        {
            // Automatically create a fresh interactive browser profile if the user hasn't added any account yet
            await this.QuickImportSessionAsync().ConfigureAwait(true);
        }

        if (this.SelectedAccount is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            AccountProfile account = this.SelectedAccount;
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Info,
                account.DisplayName,
                $"Đang mở trình duyệt cho '{account.DisplayName}'...").ConfigureAwait(true);

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin).ConfigureAwait(true);
            SessionMaterial material = await session.ExtractMaterialAsync().ConfigureAwait(true);

            // If the account was created as a placeholder before login, update its internal session identifier cleanly
            string realUid = !string.IsNullOrWhiteSpace(material.Uid) ? material.Uid : account.Uid;
            string suffix = realUid.Length >= 4 ? realUid[^4..] : realUid;
            string displayName = account.DisplayName.StartsWith("Tài khoản Đăng nhập", StringComparison.Ordinal)
                ? $"Facebook ({suffix})"
                : account.DisplayName;

            AccountProfile updated = account with
            {
                Uid = realUid,
                DisplayName = displayName,
                CookieHeader = material.CookieHeader,
                UserAgent = material.UserAgent,
                Status = AccountStatus.Active,
                LastActiveAt = material.ExtractedAt
            };

            if (!string.Equals(account.Uid, realUid, StringComparison.Ordinal))
            {
                _ = await this._context.AccountStore.DeleteAccountAsync(account.Uid).ConfigureAwait(true);
            }

            await this._context.AccountStore.UpsertAccountAsync(updated).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync().ConfigureAwait(true);
            this.SelectedAccount = updated;

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Success,
                updated.DisplayName,
                "Đã đồng bộ phiên trình duyệt thành công! Đang tự động tải danh sách Nhóm đã tham gia...").ConfigureAwait(true);

            // Automatically sync joined groups so the user can pick groups visually without copying Group IDs
            await this.SyncJoinedGroupsCoreAsync(session).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this.SelectedAccount?.DisplayName ?? "Tài khoản",
                $"Trình duyệt đã mở — vui lòng đăng nhập Facebook trên cửa sổ trình duyệt rồi bấm lại để đồng bộ ({SanitizeErrorMessage(ex)}).").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task DeleteSelectedAccountAsync()
    {
        if (this.SelectedAccount is null)
        {
            return;
        }

        string removedName = this.SelectedAccount.DisplayName;
        string removedUid = this.SelectedAccount.Uid;

        _ = await this._context.AccountStore.DeleteAccountAsync(removedUid).ConfigureAwait(true);
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Info,
            removedName,
            $"Đã gỡ tài khoản '{removedName}' khỏi danh sách.").ConfigureAwait(true);
    }
}
