// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.AiAssistant;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Threading;

namespace Facebook.Net.UI.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action?.Invoke();
}

internal sealed class StubBrowserAdapter : IBrowserAdapter
{
    public string PlatformName => "TestStub";

    public string? LocateExecutablePath() => @"C:\Stub\chrome.exe";

    public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default) =>
        throw new NotSupportedException("Stub browser adapter does not launch real processes in unit tests.");
}

internal sealed class FakeBrowserSession : IBrowserSession
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

internal sealed class FakeBrowserManager : IBrowserManager
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

internal sealed class FakeCometClient : ICometClient
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

internal sealed class FakeAgyCliService : IAgyCliService
{
    public int CheckStatusCallCount { get; private set; }

    public int InstallCallCount { get; private set; }

    public int EnsureMcpCallCount { get; private set; }

    public int OAuthCallCount { get; private set; }

    public int SendPromptCallCount { get; private set; }

    public int WarmUpSessionCallCount { get; private set; }

    public int ResetWarmSessionCallCount { get; private set; }

    public PreferredBrowserKind? LastOAuthBrowserKind { get; private set; }

    public string? LastOAuthProfileDir { get; private set; }

    public string? LastUserPrompt { get; private set; }

    public string? LastSystemContextPrompt { get; private set; }

    public string? LastModel { get; private set; }

    public string? LastEffort { get; private set; }

    public string? LastAgyConversationId { get; private set; }

    public string? LastWarmUpAgyConversationId { get; private set; }

    public AgyStatusSnapshot StatusToReturn { get; set; } = new(
        IsInstalled: true,
        InstalledVersion: "0.2.3",
        ExecutablePath: @"C:\Users\Test\AppData\Local\agy\bin\agy.exe",
        IsAuthenticated: true,
        IsFacebookMcpRegistered: true);

    public (bool Success, string Message, string? PendingOAuthUrl) OAuthResultToReturn { get; set; } =
        (true, "Đăng nhập Google Antigravity thành công!", null);

    public AgyPromptResult PromptResultToReturn { get; set; } = new(
        IsSuccess: true,
        ResponseText: "Đã xử lý yêu cầu.");

    public Task<AgyStatusSnapshot> CheckStatusAsync(CancellationToken ct = default)
    {
        this.CheckStatusCallCount++;
        return Task.FromResult(this.StatusToReturn);
    }

    public Task<bool> InstallAgyCliAsync(
        IProgress<(int Percent, string StatusText)>? progress = null,
        CancellationToken ct = default)
    {
        this.InstallCallCount++;
        progress?.Report((100, "Đã cài đặt Google Antigravity CLI thành công!"));
        this.StatusToReturn = this.StatusToReturn with { IsInstalled = true, InstalledVersion = "0.2.3" };
        return Task.FromResult(true);
    }

    public Task<bool> EnsureFacebookMcpRegisteredAsync(string? customMcpExePath = null, CancellationToken ct = default)
    {
        this.EnsureMcpCallCount++;
        this.StatusToReturn = this.StatusToReturn with { IsFacebookMcpRegistered = true };
        return Task.FromResult(true);
    }

    public Task<(bool Success, string Message, string? PendingOAuthUrl)> RunCdpBrowserOAuthLoginAsync(
        IBrowserManager browserManager,
        PreferredBrowserKind browserKind,
        string? customProfileDir = null,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        this.OAuthCallCount++;
        this.LastOAuthBrowserKind = browserKind;
        this.LastOAuthProfileDir = customProfileDir;
        if (this.OAuthResultToReturn.Success)
        {
            this.StatusToReturn = this.StatusToReturn with { IsAuthenticated = true };
        }

        return Task.FromResult(this.OAuthResultToReturn);
    }

    public Task<bool> SubmitManualOAuthCodeAsync(string authCode, CancellationToken ct = default)
    {
        this.StatusToReturn = this.StatusToReturn with { IsAuthenticated = true };
        return Task.FromResult(true);
    }

    public Task WarmUpSessionAsync(
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        this.WarmUpSessionCallCount++;
        this.LastWarmUpAgyConversationId = agyConversationId;
        return Task.CompletedTask;
    }

    public void ResetWarmSession() => this.ResetWarmSessionCallCount++;

    public Task<AgyPromptResult> SendPromptAsync(
        string userPrompt,
        string? systemContextPrompt = null,
        bool continueConversation = true,
        IProgress<string>? streamingProgress = null,
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        this.SendPromptCallCount++;
        this.LastUserPrompt = userPrompt;
        this.LastSystemContextPrompt = systemContextPrompt;
        this.LastModel = model;
        this.LastEffort = effort;
        this.LastAgyConversationId = agyConversationId;
        streamingProgress?.Report(this.PromptResultToReturn.ResponseText);
        return Task.FromResult(this.PromptResultToReturn);
    }

    public string? ResolveFacebookMcpExecutablePath() => @"C:\Stub\Facebook.Net.Mcp.exe";
}
