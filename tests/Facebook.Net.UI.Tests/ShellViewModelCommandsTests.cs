// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Importing;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Threading;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Comprehensive unit tests for <see cref="ShellViewModel"/> commands, <see cref="Facebook.Net.UI.ViewModels.Audit.AuditLogDrawerViewModel"/>,
/// <see cref="LocalizationService"/> catalog parity, <see cref="BulkObservableCollection{T}"/>, and <see cref="AppConfig"/> normalization.
/// </summary>
public sealed class ShellViewModelCommandsTests : IDisposable
{
    private readonly string _tempDir;

    public ShellViewModelCommandsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_shell_cmd_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action?.Invoke();
    }

    private sealed class StubBrowserAdapter : IBrowserAdapter
    {
        public string PlatformName => "TestStub";

        public string? LocateExecutablePath() => "C:\\Stub\\chrome.exe";

        public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException("Stub browser adapter does not launch real processes.");
    }

    private sealed class FakeBrowserSession : IBrowserSession
    {
        public FakeBrowserSession(string accountUid)
        {
            this.AccountUid = accountUid;
            this.CurrentMaterial = new SessionMaterial(
                Uid: accountUid,
                CookieHeader: $"c_user={accountUid}; xs=99%3Afake_xs;",
                FbDtsg: "AQH_fake_dtsg",
                Jazoest: "25432",
                Lsd: "LSD_fake",
                ClientRevision: "1012345678",
                HasteSession: "20745.HYP:comet_pkg.2.1...1",
                WebSessionId: "abc123:def456:ghi789",
                UserAgent: "Mozilla/5.0 Fake",
                ExtractedAt: DateTimeOffset.UtcNow);
        }

        public string AccountUid { get; }

        public BrowserState State { get; set; } = BrowserState.Running;

        public SessionMaterial? CurrentMaterial { get; set; }

        public List<string> NavigatedUrls { get; } = new();

        public Task NavigateAsync(string url, CancellationToken ct = default)
        {
            this.NavigatedUrls.Add(url);
            return Task.CompletedTask;
        }

        public Task<SessionMaterial> ExtractMaterialAsync(CancellationToken ct = default) =>
            Task.FromResult(this.CurrentMaterial!);

        public Task<int> ImportCookiesAsync(string rawCookieHeader, CancellationToken ct = default) =>
            Task.FromResult(2);

        public Task<string> ExecuteInPagePostAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string formUrlEncodedBody,
            CancellationToken ct = default) =>
            Task.FromResult("{\"data\":{}}");

        public Task<string> UploadViaDomBridgeAsync(
            string uploadUrl,
            string localFilePath,
            string fileFieldName = "file",
            IReadOnlyDictionary<string, string>? additionalFields = null,
            CancellationToken ct = default) =>
            Task.FromResult("{\"payload\":{\"fbid\":\"999111222\"}}");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeBrowserManager : IBrowserManager
    {
        private readonly Dictionary<string, FakeBrowserSession> _sessions = new(StringComparer.Ordinal);

        public event EventHandler<SessionStateChangedEvent>? SessionStateChanged;

        public FakeBrowserSession GetOrCreateFake(string uid)
        {
            if (!this._sessions.TryGetValue(uid, out FakeBrowserSession? s))
            {
                s = new FakeBrowserSession(uid);
                this._sessions[uid] = s;
            }

            return s;
        }

        public Task<IBrowserSession> AcquireSessionAsync(BrowserLaunchOptions options, CancellationToken ct = default)
        {
            FakeBrowserSession session = this.GetOrCreateFake(options.AccountUid);
            this.SessionStateChanged?.Invoke(this, new SessionStateChangedEvent(options.AccountUid, BrowserState.Running, AccountStatus.Active));
            return Task.FromResult<IBrowserSession>(session);
        }

        public IBrowserSession? TryGetSession(string accountUid) =>
            this._sessions.TryGetValue(accountUid, out FakeBrowserSession? s) ? s : null;

        public Task CloseSessionAsync(string accountUid, CancellationToken ct = default)
        {
            _ = this._sessions.Remove(accountUid);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() => this._sessions.Clear();
    }

    private sealed class FakeCometClient : ICometClient
    {
        public int PublishedPostsCount { get; private set; }

        public int CreatedCommentsCount { get; private set; }

        public int ReactionsCount { get; private set; }

        public Exception? NextExceptionToThrow { get; set; }

        public Task<GraphQlResult> ExecuteGraphQlAsync(IBrowserSession session, GraphQlRequest request, CancellationToken ct = default) =>
            Task.FromResult(new GraphQlResult(true, ["{\"data\":{}}"], null));

        public Task<string?> PublishPostAsync(
            IBrowserSession session,
            string messageText,
            ComposerTarget target = ComposerTarget.Feed,
            string? targetGroupId = null,
            IReadOnlyList<string>? photoIds = null,
            IReadOnlyList<string>? videoIds = null,
            CancellationToken ct = default)
        {
            if (this.NextExceptionToThrow is not null)
            {
                Exception ex = this.NextExceptionToThrow;
                this.NextExceptionToThrow = null;
                throw ex;
            }

            this.PublishedPostsCount++;
            return Task.FromResult<string?>($"post_{this.PublishedPostsCount}");
        }

        public Task<string?> SharePostAsync(IBrowserSession session, string postId, string messageText, string? targetGroupId = null, CancellationToken ct = default) =>
            Task.FromResult<string?>("shared_1");

        public Task<string?> CreateCommentAsync(IBrowserSession session, string postId, string messageText, string? attachmentFbid = null, CancellationToken ct = default)
        {
            if (this.NextExceptionToThrow is not null)
            {
                Exception ex = this.NextExceptionToThrow;
                this.NextExceptionToThrow = null;
                throw ex;
            }

            this.CreatedCommentsCount++;
            return Task.FromResult<string?>($"comment_{this.CreatedCommentsCount}");
        }

        public Task<bool> ReactToPostAsync(IBrowserSession session, string postId, ReactionKind reaction, CancellationToken ct = default)
        {
            this.ReactionsCount++;
            return Task.FromResult(true);
        }

        public Task<PagedResult<PostSummary>> GetNewsFeedAsync(IBrowserSession session, int count = 5, string? cursor = null, CancellationToken ct = default)
        {
            List<PostSummary> items =
            [
                new PostSummary("feed_1", "1001", "Alice", "Hello world", "https://www.facebook.com/feed_1", 10, 2, null),
                new PostSummary("feed_2", "1002", "Bob", "Morning coffee", "https://www.facebook.com/feed_2", 5, 1, null)
            ];
            return Task.FromResult(new PagedResult<PostSummary>(items, null, false));
        }

        public Task<PagedResult<PostSummary>> GetTimelinePostsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default)
        {
            List<PostSummary> items =
            [
                new PostSummary("tl_1", session.AccountUid, "Nick Bán Hàng Chủ Lực", "Bài đăng trang cá nhân số 1", "https://www.facebook.com/posts/tl_1", 15, 4, DateTimeOffset.UtcNow)
            ];
            return Task.FromResult(new PagedResult<PostSummary>(items, null, false));
        }

        public Task<PostSummary?> GetSinglePostAsync(IBrowserSession session, string postId, CancellationToken ct = default) =>
            Task.FromResult<PostSummary?>(new PostSummary(postId, "1001", "Alice", "Full post text", null, 1, 1, null));

        public Task<PagedResult<UserSummary>> GetPostReactorsAsync(IBrowserSession session, string postId, int count = 10, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<PagedResult<UserSummary>> GetPostCommentersAsync(IBrowserSession session, string postId, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<PagedResult<GroupSummary>> SearchGroupsAsync(IBrowserSession session, string keyword, int count = 5, string? cursor = null, CancellationToken ct = default)
        {
            List<GroupSummary> groups =
            [
                new GroupSummary("grp_search_1", $"Hội {keyword} Miền Nam", GroupMembershipState.CanJoin),
                new GroupSummary("grp_search_2", $"Chợ {keyword} Toàn Quốc", GroupMembershipState.CanJoin)
            ];
            return Task.FromResult(new PagedResult<GroupSummary>(groups, null, false));
        }

        public Task<PagedResult<GroupSummary>> GetJoinedGroupsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default)
        {
            List<GroupSummary> groups =
            [
                new GroupSummary("grp_101", "Chợ Cư Dân Vinhomes", GroupMembershipState.Member),
                new GroupSummary("grp_102", "Hội Mua Bán Laptop Cũ", GroupMembershipState.Member)
            ];
            return Task.FromResult(new PagedResult<GroupSummary>(groups, null, false));
        }

        public Task<PagedResult<PostSummary>> GetGroupFeedAsync(IBrowserSession session, string groupId, int count = 3, string? cursor = null, CancellationToken ct = default)
        {
            List<PostSummary> posts =
            [
                new PostSummary("lead_1", "u_1", "Nguyễn Văn Mua", "Mình cần mua laptop gaming xin giá ib nhé?", "https://www.facebook.com/groups/101/posts/1", 3, 5, null),
                new PostSummary("lead_2", "u_2", "Trần Thị Xem", "Chia sẻ kinh nghiệm vệ sinh máy tính", "https://www.facebook.com/groups/101/posts/2", 1, 0, null)
            ];
            return Task.FromResult(new PagedResult<PostSummary>(posts, null, false));
        }

        public Task<PagedResult<UserSummary>> GetGroupNewMembersAsync(IBrowserSession session, string groupId, int count = 10, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<GroupMembershipState> JoinGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default) =>
            Task.FromResult(GroupMembershipState.Member);

        public Task<IReadOnlyList<GroupQuestion>> GetGroupQuestionsAsync(IBrowserSession session, string groupId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GroupQuestion>>([]);

        public Task<GroupMembershipState> SubmitGroupAnswersAsync(IBrowserSession session, string groupId, IReadOnlyList<GroupQuestionAnswer> answers, CancellationToken ct = default) =>
            Task.FromResult(GroupMembershipState.QuestionnaireCompleted);

        public Task<bool> LeaveGroupAsync(IBrowserSession session, string groupId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> InviteFriendsToGroupAsync(IBrowserSession session, string groupId, IReadOnlyList<string> friendUids, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<PagedResult<UserSummary>> GetFriendSuggestionsAsync(IBrowserSession session, int count = 30, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<PagedResult<UserSummary>> GetIncomingFriendRequestsAsync(IBrowserSession session, int count = 20, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<PagedResult<UserSummary>> GetOutgoingFriendRequestsAsync(IBrowserSession session, int count = 10, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<PagedResult<UserSummary>> GetProfileFollowersAsync(IBrowserSession session, string targetUid, int count = 8, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<UserSummary>([], null, false));

        public Task<FriendshipState> SendFriendRequestAsync(IBrowserSession session, string targetUid, CancellationToken ct = default) =>
            Task.FromResult(FriendshipState.OutgoingRequest);

        public Task<FriendshipState> ConfirmFriendRequestAsync(IBrowserSession session, string requesterUid, CancellationToken ct = default) =>
            Task.FromResult(FriendshipState.AreFriends);

        public Task<FriendshipState> DeleteFriendRequestAsync(IBrowserSession session, string targetUid, bool isIncoming, CancellationToken ct = default) =>
            Task.FromResult(FriendshipState.CanRequest);

        public Task<FriendshipState> UnfriendAsync(IBrowserSession session, string targetUid, CancellationToken ct = default) =>
            Task.FromResult(FriendshipState.CanRequest);

        public Task<bool> UnfollowUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> PokeUserAsync(IBrowserSession session, string targetUid, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<StoryBucketItem>> GetStoriesTrayAsync(IBrowserSession session, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StoryBucketItem>>([]);

        public Task<bool> MarkStorySeenAsync(IBrowserSession session, string bucketId, string storyId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> ReplyToStoryAsync(IBrowserSession session, string storyId, string emojiOrMessage, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> CreateStoryAsync(IBrowserSession session, string? text = null, string? photoId = null, string? videoId = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync(IBrowserSession session, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationItem>>([]);

        public Task<bool> MarkNotificationReadAsync(IBrowserSession session, NotificationItem notification, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ManagedPageSummary>> GetManagedPagesAsync(IBrowserSession session, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ManagedPageSummary>>([]);

        public Task<string> UploadUfiPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default) =>
            Task.FromResult("fbid_photo_123");

        public Task<string> UploadStoryPhotoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default) =>
            Task.FromResult("story_photo_123");

        public Task<string> UploadVideoAsync(IBrowserSession session, string localFilePath, CancellationToken ct = default) =>
            Task.FromResult("video_123");
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

        // 6. Scan Buyer-Intent Group Leads, Score, Comment Sale, and Export CSV/JSON
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
    public void LocalizationService_VietnameseAndEnglishCatalogs_HaveExact100PercentKeyParity()
    {
        Dictionary<string, string>.KeyCollection viKeys = LocalizationService.GetKeys(AppLanguage.ViVn);
        Dictionary<string, string>.KeyCollection enKeys = LocalizationService.GetKeys(AppLanguage.EnUs);

        HashSet<string> viSet = new(viKeys, StringComparer.Ordinal);
        HashSet<string> enSet = new(enKeys, StringComparer.Ordinal);

        Assert.Equal(viSet.Count, enSet.Count);
        Assert.Subset(viSet, enSet);
        Assert.Superset(viSet, enSet);

        LocalizationService svc = new();
        foreach (string key in viKeys)
        {
            svc.SetLanguage(AppLanguage.ViVn);
            string viVal = svc[key];
            Assert.False(string.IsNullOrWhiteSpace(viVal));
            Assert.NotEqual(key, viVal);
            Assert.DoesNotContain("(", viVal, StringComparison.Ordinal);
            Assert.DoesNotContain(")", viVal, StringComparison.Ordinal);

            svc.SetLanguage(AppLanguage.EnUs);
            string enVal = svc[key];
            Assert.False(string.IsNullOrWhiteSpace(enVal));
            Assert.NotEqual(key, enVal);
            Assert.DoesNotContain("(", enVal, StringComparison.Ordinal);
            Assert.DoesNotContain(")", enVal, StringComparison.Ordinal);
        }

        svc.SetLanguage(AppLanguage.ViVn);
        Assert.Equal("Khởi động hoàn tất — 3 tài khoản, 12 nhóm mục tiêu.", svc.Format("Log.SystemInitialized", 3, 12));

        svc.SetLanguage(AppLanguage.EnUs);
        Assert.Equal("System initialized — 3 accounts, 12 target groups.", svc.Format("Log.SystemInitialized", 3, 12));
    }

    [Fact]
    public void Regression_DeterministicUid_AppConfigNormalize_And_BulkObservableCollectionSingleReset()
    {
        // 1. Deterministic SHA-256 UID when cookie lacks c_user
        string rawCookieNoCUser = "xs=99%3Asecret_token_value; datr=abcd1234;";
        ImportedSessionBundle b1 = UniversalSessionImporter.Parse(rawCookieNoCUser);
        ImportedSessionBundle b2 = UniversalSessionImporter.Parse(rawCookieNoCUser);
        Assert.Equal(b1.AccountUid, b2.AccountUid);
        Assert.StartsWith("acc_", b1.AccountUid, StringComparison.Ordinal);

        // 2. AppConfig.Normalize clamps delay bounds so MinDelaySeconds <= MaxDelaySeconds
        AppConfig inverted = new(MinDelaySeconds: 90, MaxDelaySeconds: 20);
        AppConfig normalized = inverted.Normalize();
        Assert.Equal(90, normalized.MinDelaySeconds);
        Assert.Equal(90, normalized.MaxDelaySeconds);

        // 3. BulkObservableCollection fires exactly 1 CollectionChanged Reset event
        BulkObservableCollection<int> col = new();
        int eventCount = 0;
        col.CollectionChanged += (_, _) => eventCount++;
        col.ReplaceAll([1, 2, 3, 4, 5]);
        Assert.Equal(1, eventCount);
        Assert.Equal(5, col.Count);
    }

    [Fact]
    public void NavigationService_ShouldNavigateBetweenPagesAndSupportBackStackFromAuditLogsToSettings()
    {
        using UiServiceContext ctx = new(
            new StubBrowserAdapter(),
            Path.Combine(this._tempDir, "nav_test"),
            new ImmediateDispatcher());
        using ShellViewModel vm = new(ctx);

        Assert.Equal(NavigationSection.Accounts, vm.CurrentSection);
        Assert.Equal(NavigationSection.Accounts, vm.Navigation.CurrentSection);
        Assert.False(vm.Navigation.CanGoBack);

        // Navigate to Settings -> then click Open Audit Logs at bottom of Settings
        vm.NavigateSettingsCommand.Execute(null);
        Assert.Equal(NavigationSection.Settings, vm.CurrentSection);
        Assert.True(vm.Navigation.CanGoBack);

        vm.NavigateAuditLogsCommand.Execute(null);
        Assert.Equal(NavigationSection.AuditLogs, vm.CurrentSection);

        // Navigate back returns to Settings
        vm.NavigateBackCommand.Execute(null);
        Assert.Equal(NavigationSection.Settings, vm.CurrentSection);
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
