// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.Services;
using Meta.UI.ViewModels.Shell;
using Xunit;

namespace Meta.UI.Tests;

/// <summary>
/// Unit and integration tests for <see cref="ShellViewModel"/> commands, circuit breaker,
/// navigation back-stack, personal audit logging, CRM lead management, and settings persistence.
/// </summary>
public sealed class ShellViewModelCommandsTests : IDisposable
{
    private readonly string _tempDir;

    public ShellViewModelCommandsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_shell_cmd_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    [Fact]
    public async Task ShellViewModel_WarmUp_HealthCheck_Groups_Publish_Bump_And_Leads_Commands_ExecuteEndToEnd()
    {
        string workspaceDir = Path.Combine(this._tempDir, "vm_commands");
        FakeBrowserManager browserManager = new();
        FakeCometClient cometClient = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            sessionManager: browserManager,
            cometClient: cometClient);
        using ShellViewModel vm = new(context);

        await vm.InitializeAsync();
        vm.MinDelaySeconds = 0;
        vm.MaxDelaySeconds = 0;

        // 1. Quick Import Account
        vm.AccountFriendlyNameInput = "Nick Bán Hàng Chủ Lực";
        vm.SessionImportRawInput = "c_user=1000777888999; xs=42%3Alive_session_xs;";
        await vm.QuickImportSessionCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedAccount);
        Assert.Equal("1000777888999", vm.SelectedAccount.Uid);

        // 2. Live Health Check Command
        await vm.CheckAccountHealthCommand.ExecuteAsync(null);
        Assert.Equal(AccountStatus.Active, vm.SelectedAccount.Status);

        // 3. Organic Session Warm-Up Command
        await vm.WarmUpAccountCommand.ExecuteAsync(null);
        Assert.True(cometClient.ReactionsCount >= 2);

        // 4. Sync Joined Groups & verify TargetGroups auto-seeded
        await vm.SyncJoinedGroupsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.JoinedGroups.Count);
        vm.SelectedGroup = vm.JoinedGroups[0];
        await vm.AddSelectedGroupToTargetsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.TargetGroups.Count);

        // 5. Publish Campaign to Target Group + Auto-React Own Post + Up-Top Bump
        vm.PublishToTargetGroups = true;
        vm.AutoReactPublishedPost = true;
        vm.PostSpintaxText = "{Chào cả nhà|Xin chào} ngày {date} mã {random_id}!";
        await vm.PublishCampaignCommand.ExecuteAsync(null);
        Assert.Equal(2, cometClient.PublishedPostsCount);

        await vm.BumpRecentPostsCommand.ExecuteAsync(null);
        Assert.Equal(2, cometClient.CreatedCommentsCount);

        // 6. Scan Buyer-Intent Group Leads, Score, Comment Sale
        vm.BuyerIntentKeywords = "cần mua, xin giá";
        await vm.ScanGroupLeadsCommand.ExecuteAsync(null);
        _ = Assert.Single(vm.ScrapedPosts);
        Assert.Equal("lead_1", vm.ScrapedPosts[0].PostId);

        int highScore = ShellViewModel.CalculateBuyerIntentScore(vm.ScrapedPosts[0].MessageText, vm.BuyerIntentKeywords);
        Assert.True(highScore >= 75);

        await vm.SendSalesCommentOnSelectedPostCommand.ExecuteAsync(null);
        Assert.Equal(3, cometClient.CreatedCommentsCount);

        // Duplicate comment prevention check
        await vm.SendSalesCommentOnSelectedPostCommand.ExecuteAsync(null);
        Assert.Equal(3, cometClient.CreatedCommentsCount);

        // 7. Custom Color Group Tags + Tag Filtering + 1-Click Tag Selection
        Assert.True(vm.GroupTags.Count >= 5);
        vm.NewGroupTagName = "Nhóm BĐS VIP";
        vm.NewGroupTagColorHex = "#EC4899";
        await vm.CreateGroupTagAsync();
        GroupTag? customTag = null;
        for (int i = 0; i < vm.GroupTags.Count; i++)
        {
            if (vm.GroupTags[i].Name == "Nhóm BĐS VIP")
            {
                customTag = vm.GroupTags[i];
                break;
            }
        }

        Assert.NotNull(customTag);
        await vm.ToggleGroupTagAsync("grp_101", "Chợ Cư Dân Vinhomes", null, customTag.Id);
        SavedTargetGroup? taggedGrp = vm.FindTargetGroupById("grp_101");
        Assert.NotNull(taggedGrp);
        Assert.True(taggedGrp.HasTag(customTag.Id));

        // 8. Sync Personal Profile Timeline Posts & Repost to Groups
        await vm.SyncPersonalTimelinePostsAsync();
        Assert.Contains(vm.PublishedPosts, p => p.PostId == "tl_1" && p.IsPersonalProfilePost);
        vm.SelectedPublishedPost = vm.PublishedPosts[0];
        await vm.RepostSelectedPostToGroupsAsync();
        Assert.True(vm.PublishedPosts.Count >= 3);
    }

    [Fact]
    public async Task ShellViewModel_CircuitBreaker_HaltsCampaignAndMarksCheckpoint_OnError190()
    {
        string workspaceDir = Path.Combine(this._tempDir, "vm_circuit_breaker");
        FakeBrowserManager browserManager = new();
        FakeCometClient cometClient = new();

        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher(),
            sessionManager: browserManager,
            cometClient: cometClient);
        using ShellViewModel vm = new(context);

        await vm.InitializeAsync();
        vm.AccountFriendlyNameInput = "Nick Test Checkpoint";
        vm.SessionImportRawInput = "c_user=1000111222333; xs=11%3Atest_xs;";
        await vm.QuickImportSessionCommand.ExecuteAsync(null);

        cometClient.NextExceptionToThrow = new InvalidOperationException("Facebook error 190: checkpoint required");
        vm.PublishToTargetGroups = false;
        vm.PostSpintaxText = "Test post";
        await vm.PublishCampaignCommand.ExecuteAsync(null);

        Assert.NotNull(vm.SelectedAccount);
        Assert.Equal(AccountStatus.Checkpoint, vm.SelectedAccount.Status);
    }

    [Fact]
    public void NavigationService_ShouldNavigateBetweenPagesAndSupportBackStackFromAuditLogsToSettings()
    {
        using UiServiceContext ctx = new(
            new StubBrowserAdapter(),
            Path.Combine(this._tempDir, "nav_test"),
            new ImmediateDispatcher());
        using ShellViewModel vm = new(ctx);

        Assert.Equal(NavigationSection.Overview, vm.CurrentSection);
        Assert.Equal(NavigationSection.Overview, vm.Navigation.CurrentSection);
        Assert.False(vm.Navigation.CanGoBack);

        vm.NavigateSettingsCommand.Execute(null);
        Assert.Equal(NavigationSection.Settings, vm.CurrentSection);
        Assert.True(vm.Navigation.CanGoBack);

        vm.NavigateAuditLogsCommand.Execute(null);
        Assert.Equal(NavigationSection.AuditLogs, vm.CurrentSection);

        vm.NavigateBackCommand.Execute(null);
        Assert.Equal(NavigationSection.Settings, vm.CurrentSection);
    }

    [Fact]
    public async Task ShellViewModel_PersonalAuditLogs_RecordsAllUserInitiatedCrudAndSettingActions()
    {
        string workspaceDir = Path.Combine(this._tempDir, "personal_audit_test");
        using UiServiceContext context = new(
            new StubBrowserAdapter(),
            workspaceDir,
            new ImmediateDispatcher());
        using ShellViewModel vm = new(context);

        await vm.InitializeAsync();
        await vm.AuditDrawer.ClearLogsCommand.ExecuteAsync(null);

        // 1. Account Import & Proxy Update
        vm.AccountFriendlyNameInput = "Nick Test Audit Cá Nhân";
        vm.SessionImportRawInput = "c_user=1000333444555; xs=55%3Apersonal_audit_xs;";
        await vm.QuickImportSessionCommand.ExecuteAsync(null);

        vm.AccountProxyInput = "127.0.0.1:8080";
        await vm.SaveAccountProxyCommand.ExecuteAsync(null);

        // 2. Group Tag Create, Edit, Assign, Batch Assign, Delete
        vm.NewGroupTagName = "Nhãn Cá Nhân 01";
        vm.NewGroupTagColorHex = "#3B82F6";
        await vm.CreateGroupTagAsync();
        GroupTag createdTag = vm.GroupTags[^1];

        await vm.UpdateGroupTagAsync(createdTag.Id, "Nhãn Cá Nhân Đã Sửa", "#10B981");

        vm.SelectedGroup = new GroupSummary("grp_audit_1", "Nhóm Kiểm Thử Audit", GroupMembershipState.Member);
        await vm.AddSelectedGroupToTargetsCommand.ExecuteAsync(null);
        SavedTargetGroup? tg = vm.FindTargetGroupById("grp_audit_1");
        Assert.NotNull(tg);

        await vm.ToggleTargetGroupEnabledAsync(tg, false);
        await vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, null, createdTag.Id);
        await vm.BatchSetTagOnGroupsAsync(createdTag.Id, [tg], assignTag: false);
        await vm.DeleteGroupTagAsync(createdTag);

        // 3. Template Save (Post & Bump) + Delete
        vm.PostSpintaxText = "Nội dung mẫu bài viết cá nhân";
        await vm.SaveTemplateCommand.ExecuteAsync(null);
        vm.BumpCommentSpintaxText = "Nội dung mẫu đẩy bài cá nhân";
        await vm.SaveBumpTemplateCommand.ExecuteAsync(null);
        Assert.True(vm.SavedTemplates.Count >= 2);
        vm.SelectedTemplate = vm.SavedTemplates[0];
        await vm.DeleteSelectedTemplateCommand.ExecuteAsync(null);

        // 4. Browser Profile Create, Assign, Cache Clean, Delete & Settings Changes
        string? newProfPath = await vm.CreateAndAssignBrowserProfileAsync("Profile Audit Test");
        Assert.NotNull(newProfPath);
        _ = await vm.CleanBrowserProfilesCacheAsync();
        bool deletedProf = await vm.DeleteManagedBrowserProfileAsync(
            new BrowserProfileOption("Profile Audit Test", newProfPath, IsManagedByApp: true));
        Assert.True(deletedProf);

        vm.SelectedThemeMode = AppThemeMode.Light;
        vm.SelectedLanguage = AppLanguage.EnUs;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // 5. Query strictly by AuditCategory.Personal ("Cá nhân")
        vm.AuditDrawer.SelectedCategory = AuditCategory.Personal;
        await vm.AuditDrawer.RefreshLogsAsync();

        Assert.True(vm.AuditDrawer.FilteredLogs.Count >= 14, $"Expected at least 14 Personal audit logs, got {vm.AuditDrawer.FilteredLogs.Count}.");
        Assert.All(vm.AuditDrawer.FilteredLogs, r => Assert.Equal(AuditCategory.Personal, r.Category));
    }

    [Fact]
    public async Task ShellViewModel_PersistsSettings_ReloadsCrossProcess_AndManagesLeadCrmAndGroupTagRepair()
    {
        string persistDir = Path.Combine(this._tempDir, "settings_persist_test");

        using (UiServiceContext ctx1 = new(new StubBrowserAdapter(), persistDir, new ImmediateDispatcher()))
        using (ShellViewModel vm1 = new(ctx1))
        {
            vm1.SelectedThemeMode = AppThemeMode.Light;
            vm1.SelectedLanguage = AppLanguage.EnUs;
            vm1.SelectedBrowserKind = PreferredBrowserKind.Brave;
            vm1.CustomBrowserExecutablePath = @"C:\CustomBrowser\brave.exe";
            vm1.RunBrowserHeadless = true;
            vm1.MinDelaySeconds = 25;
            vm1.MaxDelaySeconds = 90;
            vm1.IsSidebarCollapsed = true;
        }

        using UiServiceContext ctx2 = new(new StubBrowserAdapter(), persistDir, new ImmediateDispatcher());
        await ctx2.AccountStore.InitializeAsync();

        // Seed polluted target groups and a customer lead post before ShellViewModel initialization
        await ctx2.AccountStore.UpsertTargetGroupAsync(new SavedTargetGroup(
            GroupId: "grp_dotnet",
            GroupName: "Cộng đồng C# .NET Việt Nam",
            IsEnabled: true,
            TagsCsv: "tag_bds,tag_jobs,tag_sales"));
        await ctx2.AccountStore.UpsertTargetGroupAsync(new SavedTargetGroup(
            GroupId: "grp_bds",
            GroupName: "Mua Bán Nhà Đất Bất Động Sản Quận 7",
            IsEnabled: true,
            TagsCsv: "tag_bds"));

        PostSummary sampleLead = new(
            PostId: "lead_post_101",
            AuthorUid: "uid_khach_1",
            AuthorName: "Lê Hoàng Nam",
            MessageText: "Mình cần tìm căn hộ 2PN Quận 7 giá tầm 3 tỷ, sđt 0912345678",
            PermalinkUrl: "https://facebook.com/groups/grp_bds/posts/lead_post_101",
            ReactionCount: 3,
            CommentCount: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            GroupId: "grp_bds",
            GroupName: "Mua Bán Nhà Đất Bất Động Sản Quận 7");
        await ctx2.AccountStore.UpsertScrapedPostsAsync([sampleLead]);

        using ShellViewModel vm2 = new(ctx2);

        // Verify properties are loaded synchronously in ShellViewModel constructor
        Assert.Equal(AppThemeMode.Light, vm2.SelectedThemeMode);
        Assert.Equal(AppLanguage.EnUs, vm2.SelectedLanguage);
        Assert.Equal(PreferredBrowserKind.Brave, vm2.SelectedBrowserKind);
        Assert.Equal(@"C:\CustomBrowser\brave.exe", vm2.CustomBrowserExecutablePath);
        Assert.True(vm2.RunBrowserHeadless);
        Assert.Equal(25, vm2.MinDelaySeconds);
        Assert.Equal(90, vm2.MaxDelaySeconds);
        Assert.True(vm2.IsSidebarCollapsed);

        await vm2.InitializeAsync();

        // Customer CRM tags & notes
        GroupTag? createdLeadTag = await vm2.CreateQuickLeadTagAsync("Khách VIP Quận 7", "#EC4899");
        Assert.NotNull(createdLeadTag);
        await vm2.ToggleLeadPostTagAsync(sampleLead, createdLeadTag.Id);
        await vm2.UpdateLeadPostNotesAsync(sampleLead, "Đã gọi điện tư vấn căn hộ Sunrise City");

        IReadOnlyList<PostSummary> reloadedLeads = await ctx2.AccountStore.QueryScrapedPostsAsync(groupId: "grp_bds");
        PostSummary updatedPost = Assert.Single(reloadedLeads);
        Assert.True(updatedPost.HasTag(createdLeadTag.Id));
        Assert.Equal("Đã gọi điện tư vấn căn hộ Sunrise City", updatedPost.Notes);

        // Simulate cross-process appsettings.json update (e.g. from Facebook.Net.Mcp.exe)
        string settingsPath = Path.Combine(persistDir, "appsettings.json");
        AppSettingsService externalProcessSettings = new(settingsPath);
        await externalProcessSettings.SaveAsync(externalProcessSettings.Current with
        {
            MinDelaySeconds = 4.5,
            MaxDelaySeconds = 9.5,
            ScrapePostsPerGroup = 45
        });

        vm2.ReloadSettingsFromDisk();
        Assert.Equal(4.5, vm2.MinDelaySeconds);
        Assert.Equal(9.5, vm2.MaxDelaySeconds);
        Assert.Equal(45, vm2.ScrapePostsPerGroup);
    }

    [Fact]
    public void ShellViewModel_MultiPhotoAndUpdateVersionParsing_WorkCorrectly()
    {
        string img1 = Path.Combine(this._tempDir, "photo1.jpg");
        string img2 = Path.Combine(this._tempDir, "photo2.png");
        string missingImg = Path.Combine(this._tempDir, "missing.jpg");
        File.WriteAllBytes(img1, [0xFF, 0xD8, 0xFF]);
        File.WriteAllBytes(img2, [0x89, 0x50, 0x4E, 0x47]);

        string rawPaths = $"{img1} ; {img2} ; {missingImg} ; {img1}";
        List<string> existing = ShellViewModel.ParseExistingMediaFilePaths(rawPaths);

        Assert.Equal(2, existing.Count);
        Assert.Equal(img1, existing[0]);
        Assert.Equal(img2, existing[1]);

        Version v1 = ShellViewModel.TryParseReleaseVersion("v1.2.4");
        Version v2 = ShellViewModel.TryParseReleaseVersion("v1.3.0-preview.1");
        Assert.True(v2 > v1);
        Assert.Equal(new Version(1, 2, 4), v1);
        Assert.Equal(new Version(1, 3, 0), v2);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._tempDir))
            {
                Directory.Delete(this._tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore locked temp files
        }
    }
}
