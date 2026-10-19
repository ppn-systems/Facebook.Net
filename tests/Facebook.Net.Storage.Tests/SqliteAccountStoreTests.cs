// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Xunit;

namespace Facebook.Net.Storage.Tests;

/// <summary>
/// Integration tests for <see cref="SqliteAccountStore"/> verifying WAL initialization and CRUD operations.
/// </summary>
public sealed class SqliteAccountStoreTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteAccountStore _store;

    public SqliteAccountStoreTests()
    {
        this._tempDbPath = Path.Combine(Path.GetTempPath(), $"fb_test_{Guid.NewGuid():N}.db");
        this._store = new SqliteAccountStore(this._tempDbPath);
    }

    [Fact]
    public async Task AccountCrud_UpsertsQueriesAndDeletesSuccessfully()
    {
        await this._store.InitializeAsync();

        AccountProfile profile = new(
            Uid: "1000123456789",
            DisplayName: "Test User",
            CookieHeader: "c_user=1000123456789; xs=secret;",
            UserAgent: ProtocolConstants.Protocol.DefaultWindowsUserAgent,
            ProfileDirectory: @"C:\Profiles\1000123456789",
            ProxyId: "proxy-1",
            TwoFactorSecret: "JBSWY3DPEHPK3PXP",
            Status: AccountStatus.Active,
            LastActiveAt: DateTimeOffset.UtcNow);

        await this._store.UpsertAccountAsync(profile);

        AccountProfile? fetched = await this._store.GetAccountByUidAsync("1000123456789");
        Assert.NotNull(fetched);
        Assert.Equal("Test User", fetched.DisplayName);
        Assert.Equal(AccountStatus.Active, fetched.Status);
        Assert.Equal("proxy-1", fetched.ProxyId);

        AccountProfile updated = profile with { Status = AccountStatus.Checkpoint, DisplayName = "Updated User" };
        await this._store.UpsertAccountAsync(updated);

        IReadOnlyList<AccountProfile> all = await this._store.GetAllAccountsAsync();
        _ = Assert.Single(all);
        Assert.Equal("Updated User", all[0].DisplayName);
        Assert.Equal(AccountStatus.Checkpoint, all[0].Status);

        bool deleted = await this._store.DeleteAccountAsync("1000123456789");
        Assert.True(deleted);
        Assert.Null(await this._store.GetAccountByUidAsync("1000123456789"));
    }

    [Fact]
    public async Task ProxyCrud_UpsertsQueriesAndDeletesSuccessfully()
    {
        await this._store.InitializeAsync();

        ProxyConfig proxy = new(
            Id: "p1",
            Host: "192.168.1.100",
            Port: 8080,
            Protocol: ProxyProtocol.Socks5,
            Username: "user1",
            Password: "pw1");

        await this._store.UpsertProxyAsync(proxy);

        IReadOnlyList<ProxyConfig> proxies = await this._store.GetAllProxiesAsync();
        _ = Assert.Single(proxies);
        Assert.Equal("192.168.1.100", proxies[0].Host);
        Assert.Equal(ProxyProtocol.Socks5, proxies[0].Protocol);

        Assert.True(await this._store.DeleteProxyAsync("p1"));
        Assert.Empty(await this._store.GetAllProxiesAsync());
    }

    [Fact]
    public async Task TargetGroupAndCampaignCrud_AndActionHistoryDeduplication_WorksCorrectly()
    {
        await this._store.InitializeAsync();

        SavedTargetGroup group = new("555666", "Hội Mua Bán Laptop", IsEnabled: true, Notes: "Nhóm tương tác cao");
        await this._store.UpsertTargetGroupAsync(group);
        IReadOnlyList<SavedTargetGroup> groups = await this._store.GetAllTargetGroupsAsync();
        _ = Assert.Single(groups);
        Assert.Equal("Hội Mua Bán Laptop", groups[0].GroupName);
        Assert.True(await this._store.DeleteTargetGroupAsync("555666"));

        CampaignTemplate tpl = new("tpl-1", "Sale iPhone", "{Bán|Pass} iPhone 16", @"C:\img.jpg", IsCommentTemplate: true);
        await this._store.UpsertCampaignTemplateAsync(tpl);
        IReadOnlyList<CampaignTemplate> templates = await this._store.GetAllCampaignTemplatesAsync();
        _ = Assert.Single(templates);
        Assert.True(templates[0].IsCommentTemplate);
        Assert.True(await this._store.DeleteCampaignTemplateAsync("tpl-1"));

        Assert.False(await this._store.HasExecutedActionAsync("10001", "COMMENT", "post_999"));
        await this._store.RecordActionAsync(new ActionHistoryEntry("10001", "COMMENT", "post_999", "cmt_111", DateTimeOffset.UtcNow));
        Assert.True(await this._store.HasExecutedActionAsync("10001", "COMMENT", "post_999"));

        IReadOnlyList<ActionHistoryEntry> recent = await this._store.GetRecentActionsAsync(10);
        _ = Assert.Single(recent);
        Assert.Equal("cmt_111", recent[0].ResultId);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(this._tempDbPath))
            {
                File.Delete(this._tempDbPath);
            }
        }
        catch
        {
            // Ignore locked temp file cleanup
        }
    }
}
