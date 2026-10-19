// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.App.ViewModels;

/// <summary>
/// Partial implementation of <see cref="MainWindowViewModel"/> handling SQLite account persistence and browser session launch.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Initializes SQLite storage and loads all saved accounts into <see cref="Accounts"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            await this._services.AccountStore.InitializeAsync().ConfigureAwait(true);
            await this.ReloadAccountsAsync().ConfigureAwait(true);
            this.AppendLog($"Đã khởi tạo SQLite Store ({this.Accounts.Count} tài khoản). Hệ điều hành: {this._services.BrowserAdapter.PlatformName}.");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi khởi tạo dữ liệu: {ex.Message}");
        }
    }

    private async Task ReloadAccountsAsync()
    {
        IReadOnlyList<AccountProfile> stored = await this._services.AccountStore.GetAllAccountsAsync().ConfigureAwait(true);
        this.Accounts.Clear();
        for (int i = 0; i < stored.Count; i++)
        {
            this.Accounts.Add(stored[i]);
        }

        if (this.SelectedAccount is null && this.Accounts.Count > 0)
        {
            this.SelectedAccount = this.Accounts[0];
        }
    }

    private async Task AddAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(this.NewAccountUid))
        {
            this.AppendLog("Vui lòng nhập UID tài khoản.");
            return;
        }

        string uid = this.NewAccountUid.Trim();
        string displayName = string.IsNullOrWhiteSpace(this.NewAccountDisplayName)
            ? $"FB_{uid}"
            : this.NewAccountDisplayName.Trim();

        string profileDir = Path.Combine(this._services.ProfilesRootDirectory, uid);
        _ = Directory.CreateDirectory(profileDir);

        AccountProfile profile = new(
            Uid: uid,
            DisplayName: displayName,
            CookieHeader: this.NewAccountCookieHeader.Trim(),
            UserAgent: OperatingSystem.IsMacOS()
                ? ProtocolConstants.Protocol.DefaultMacOsUserAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent,
            ProfileDirectory: profileDir,
            ProxyId: null,
            TwoFactorSecret: null,
            Status: AccountStatus.Active,
            LastActiveAt: DateTimeOffset.UtcNow);

        await this._services.AccountStore.UpsertAccountAsync(profile).ConfigureAwait(true);
        await this.ReloadAccountsAsync().ConfigureAwait(true);
        this.SelectedAccount = profile;
        this.NewAccountUid = string.Empty;
        this.NewAccountDisplayName = string.Empty;
        this.NewAccountCookieHeader = string.Empty;

        this.AppendLog($"Đã lưu tài khoản {profile.DisplayName} ({profile.Uid}).");
    }

    private async Task LaunchBrowserAndSyncSessionAsync()
    {
        if (this.SelectedAccount is null)
        {
            this.AppendLog("Vui lòng chọn một tài khoản trong danh sách trước khi mở trình duyệt.");
            return;
        }

        this.IsBusy = true;
        try
        {
            AccountProfile account = this.SelectedAccount;
            this.AppendLog($"Đang mở trình duyệt Chromium cho {account.DisplayName} ({account.Uid})...");

            BrowserLaunchOptions options = new(
                AccountUid: account.Uid,
                UserDataDir: account.ProfileDirectory,
                UserAgent: account.UserAgent,
                Headless: false);

            IBrowserSession session = await this._services.SessionManager
                .AcquireSessionAsync(options)
                .ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(account.CookieHeader))
            {
                int injectedCount = await session.ImportCookiesAsync(account.CookieHeader).ConfigureAwait(true);
                this.AppendLog($"Đã nạp {injectedCount} cookie vào trình duyệt cho tài khoản {account.Uid}.");
            }

            await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin).ConfigureAwait(true);
            SessionMaterial material = await session.ExtractMaterialAsync().ConfigureAwait(true);

            AccountProfile updated = account with
            {
                CookieHeader = material.CookieHeader,
                LastActiveAt = material.ExtractedAt,
                Status = string.IsNullOrWhiteSpace(material.FbDtsg) ? AccountStatus.CookieExpired : AccountStatus.Active
            };

            await this._services.AccountStore.UpsertAccountAsync(updated).ConfigureAwait(true);
            await this.ReloadAccountsAsync().ConfigureAwait(true);

            this.AppendLog(
                string.IsNullOrWhiteSpace(material.FbDtsg)
                    ? $"Trình duyệt đã mở cho {account.Uid}. Vui lòng đăng nhập trên cửa sổ trình duyệt rồi bấm lại để đồng bộ token."
                    : $"Đã đồng bộ Session thành công! UID={material.Uid}, jazoest={material.Jazoest}, lsd={material.Lsd}.");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi mở trình duyệt: {ex.Message}");
        }
        finally
        {
            this.IsBusy = false;
        }
    }
}
