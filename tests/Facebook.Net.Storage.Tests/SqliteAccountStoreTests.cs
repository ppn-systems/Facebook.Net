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

    [Fact]
    public async Task UpsertTargetGroupsBatchAsync_InsertsAndUpdatesBatchInSingleTransaction()
    {
        await this._store.InitializeAsync();

        List<SavedTargetGroup> batch = new(50);
        for (int i = 0; i < 50; i++)
        {
            batch.Add(new SavedTargetGroup(
                GroupId: $"grp_{i:D3}",
                GroupName: $"Nhóm Số {i:D3}",
                IsEnabled: i % 2 == 0,
                Notes: "Batch test",
                AvatarUrl: $"https://example.com/{i}.png",
                TagsCsv: "tag_a"));
        }

        await this._store.UpsertTargetGroupsBatchAsync(batch);

        IReadOnlyList<SavedTargetGroup> stored = await this._store.GetAllTargetGroupsAsync();
        Assert.Equal(50, stored.Count);

        List<SavedTargetGroup> disableAll = new(50);
        for (int i = 0; i < stored.Count; i++)
        {
            disableAll.Add(stored[i] with { IsEnabled = false, TagsCsv = "tag_b" });
        }

        await this._store.UpsertTargetGroupsBatchAsync(disableAll);

        IReadOnlyList<SavedTargetGroup> afterUpdate = await this._store.GetAllTargetGroupsAsync();
        Assert.Equal(50, afterUpdate.Count);
        Assert.All(afterUpdate, g =>
        {
            Assert.False(g.IsEnabled);
            Assert.Equal("tag_b", g.TagsCsv);
        });
    }

    [Fact]
    public async Task ScrapedPostsRepository_UpsertQueryFilterAndClear_WorksCorrectly()
    {
        await this._store.InitializeAsync();

        PostSummary post1 = new(
            PostId: "post_net_1",
            AuthorUid: "1001",
            AuthorName: "Nguyễn Văn A",
            MessageText: "Tuyển lập trình viên C# .NET lương cao tại HCM",
            PermalinkUrl: "https://www.facebook.com/groups/dotnet/posts/post_net_1",
            ReactionCount: 12,
            CommentCount: 4,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            GroupId: "grp_dotnet",
            GroupName: "Lập trình viên .Net C#");

        PostSummary post2 = new(
            PostId: "post_laptop_2",
            AuthorUid: "1002",
            AuthorName: "Trần Thị B",
            MessageText: "Cần mua MacBook Pro M3 cũ giá tốt",
            PermalinkUrl: "https://www.facebook.com/groups/laptop/posts/post_laptop_2",
            ReactionCount: 5,
            CommentCount: 9,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            GroupId: "grp_laptop",
            GroupName: "Hội Mua Bán Laptop");

        await this._store.UpsertScrapedPostsAsync([post1, post2]);

        IReadOnlyList<PostSummary> all = await this._store.QueryScrapedPostsAsync();
        Assert.Equal(2, all.Count);

        IReadOnlyList<PostSummary> byGroup = await this._store.QueryScrapedPostsAsync(groupId: "grp_dotnet");
        _ = Assert.Single(byGroup);
        Assert.Equal("post_net_1", byGroup[0].PostId);
        Assert.Equal("Lập trình viên .Net C#", byGroup[0].GroupName);

        IReadOnlyList<PostSummary> byKeyword = await this._store.QueryScrapedPostsAsync(searchKeyword: "MacBook");
        _ = Assert.Single(byKeyword);
        Assert.Equal("post_laptop_2", byKeyword[0].PostId);

        int deletedDotnet = await this._store.ClearScrapedPostsAsync(groupId: "grp_dotnet");
        Assert.Equal(1, deletedDotnet);

        int deletedRemaining = await this._store.ClearScrapedPostsAsync();
        Assert.Equal(1, deletedRemaining);
        Assert.Empty(await this._store.QueryScrapedPostsAsync());
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
