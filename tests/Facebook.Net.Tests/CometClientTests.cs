// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Client;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;
using Xunit;

namespace Facebook.Net.Tests;

/// <summary>
/// Unit and mock-session integration tests for <see cref="CometClient"/>, <see cref="PayloadBuilder"/>,
/// and <see cref="ResponseParser"/> covering group publishing, sales commenting, and scraping.
/// </summary>
public sealed class CometClientTests
{
    [Fact]
    public void SpinText_ResolvesNestedAndFlatSpintaxOptions()
    {
        string input = "{Xin chào|Chào bạn|Hello} {inbox|nhắn tin} báo giá {nhé|nha}!";
        string spun = PayloadBuilder.SpinText(input);

        Assert.DoesNotContain("{", spun, StringComparison.Ordinal);
        Assert.DoesNotContain("}", spun, StringComparison.Ordinal);
        Assert.DoesNotContain("|", spun, StringComparison.Ordinal);
        Assert.EndsWith("!", spun, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGraphQlFormBody_IncludesAllBaseParametersAndHeaders()
    {
        SessionMaterial material = CreateDummyMaterial();
        GraphQlRequest request = new(
            FriendlyName: ProtocolConstants.Queries.ComposerStoryCreateName,
            DocId: ProtocolConstants.Queries.PublishStoryCreateDocId,
            VariablesJson: "{\"input\":{}}",
            ContextRoute: "comet.fbweb.CometGroupDiscussionRoute");

        Dictionary<string, string> headers = PayloadBuilder.BuildGraphQlHeaders(material, request.FriendlyName);
        string body = PayloadBuilder.BuildGraphQlFormBody(material, request, requestCounter: 5);

        Assert.Equal("359341", headers["x-asbd-id"]);
        Assert.Equal(material.Lsd, headers["x-fb-lsd"]);
        Assert.Equal(request.FriendlyName, headers["x-fb-friendly-name"]);

        Assert.Contains("av=1000123456789", body, StringComparison.Ordinal);
        Assert.Contains("__user=1000123456789", body, StringComparison.Ordinal);
        Assert.Contains("fb_dtsg=NAcM_test_token", body, StringComparison.Ordinal);
        Assert.Contains($"doc_id={ProtocolConstants.Queries.PublishStoryCreateDocId}", body, StringComparison.Ordinal);
        Assert.Contains("__crn=comet.fbweb.CometGroupDiscussionRoute", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishPostAsync_ToTargetGroup_NavigatesAndSendsGroupComposerMutation()
    {
        FakeBrowserSession session = new(
            "{\"data\":{\"story_create\":{\"story\":{\"post_id\":\"987654321\"}}}}");

        CometClient client = new();
        string? postId = await client.PublishPostAsync(
            session,
            "{Cần bán|Thanh lý} iPhone 16 Pro Max giá tốt!",
            ComposerTarget.Group,
            targetGroupId: "555666777",
            photoIds: new List<string> { "111222333" });

        Assert.Equal("987654321", postId);
        Assert.Contains("https://www.facebook.com/555666777", session.NavigatedUrls);
        Assert.Contains("https://www.facebook.com/987654321", session.NavigatedUrls);
        Assert.Contains(ProtocolConstants.Queries.PublishStoryCreateDocId, session.LastPostedBody, StringComparison.Ordinal);
        Assert.Contains("555666777", session.LastPostedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCommentAsync_WithSalesPhoto_EncodesFeedbackIdAndExtractsCommentId()
    {
        string encodedCommentNode = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("comment:12345_777888999"));
        FakeBrowserSession session = new(
            $"{{\"data\":{{\"comment_create\":{{\"feedback_comment_edge\":{{\"node\":{{\"id\":\"{encodedCommentNode}\"}}}}}}}}}}");

        CometClient client = new();
        string? commentId = await client.CreateCommentAsync(
            session,
            postId: "12345",
            messageText: "{Check inbox|Em gửi bảng giá} ạ!",
            attachmentFbid: "photo_999");

        Assert.Equal("777888999", commentId);
        Assert.Contains("https://www.facebook.com/12345", session.NavigatedUrls);
        Assert.Contains("https://www.facebook.com/777888999", session.NavigatedUrls);
        Assert.Contains(ProtocolConstants.Queries.CreateCommentDocId, session.LastPostedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetGroupFeedAsync_ParsesMultilineNdjsonPostsEngagementAndPaginationCursor()
    {
        string ndjson =
            "for (;;);{\"data\":{\"node\":{\"group_feed\":{\"edges\":[{\"node\":{\"post_id\":\"101\",\"creation_time\":1730000000,\"actors\":[{\"id\":\"10001\",\"name\":\"Seller A\"}],\"message\":{\"text\":\"Bán laptop gaming\"},\"feedback\":{\"reaction_count\":{\"count\":15},\"total_comment_count\":{\"count\":4}}}}]}}}}\n" +
            "{\"label\":\"GroupsCometFeedRegularStoriesPaginationQuery$defer\",\"data\":{\"node\":{\"post_id\":\"102\",\"actors\":[{\"id\":\"10002\",\"name\":\"Seller B\"}],\"message\":{\"text\":\"Pass màn hình 27 inch\"}}}}\n" +
            "{\"data\":{\"page_info\":{\"end_cursor\":\"CURSOR_NEXT_2\",\"has_next_page\":true}}}";

        FakeBrowserSession session = new(ndjson);
        CometClient client = new();

        PagedResult<PostSummary> feed = await client.GetGroupFeedAsync(session, groupId: "888999", count: 3);

        Assert.Equal(2, feed.Items.Count);
        Assert.Equal("101", feed.Items[0].PostId);
        Assert.Equal("Bán laptop gaming", feed.Items[0].MessageText);
        Assert.Equal(15, feed.Items[0].ReactionCount);
        Assert.Equal(4, feed.Items[0].CommentCount);
        _ = Assert.NotNull(feed.Items[0].CreatedAt);
        Assert.Equal("102", feed.Items[1].PostId);
        Assert.True(feed.HasNextPage);
        Assert.Equal("CURSOR_NEXT_2", feed.EndCursor);
    }

    [Fact]
    public void SpinText_ExpandsDynamicMacros_Date_Time_Day_And_RandomId()
    {
        string input = "Ưu đãi ngày {date} lúc {time} ({day}) mã #{random_id}!";
        string spun = PayloadBuilder.SpinText(input);

        Assert.DoesNotContain("{date}", spun, StringComparison.Ordinal);
        Assert.DoesNotContain("{time}", spun, StringComparison.Ordinal);
        Assert.DoesNotContain("{day}", spun, StringComparison.Ordinal);
        Assert.DoesNotContain("{random_id}", spun, StringComparison.Ordinal);
        Assert.Contains("#", spun, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtocolErrorClassifier_ClassifiesActionBlocked368_Checkpoint190_And_TransientRateLimit()
    {
        Assert.Equal(AutomationErrorCategory.ActionBlocked368, ProtocolErrorClassifier.Classify(368, "You're Temporarily Blocked"));
        Assert.Equal(AutomationErrorCategory.CheckpointOrAuthFailure, ProtocolErrorClassifier.Classify(190, "Error validating access token: checkpoint"));
        Assert.Equal(AutomationErrorCategory.TransientRetryable, ProtocolErrorClassifier.Classify(1675004, "Rate limit exceeded"));

        InvalidOperationException ex368 = new("GraphQL error 368: SentryBlock");
        Assert.Equal(AutomationErrorCategory.ActionBlocked368, ProtocolErrorClassifier.ClassifyException(ex368));
    }

    [Fact]
    public void HumanJitter_NextGaussianDelay_AlwaysStaysWithinConfiguredBounds()
    {
        for (int i = 0; i < 50; i++)
        {
            TimeSpan delay = HumanJitter.NextGaussianDelay(10, 25);
            Assert.InRange(delay.TotalMilliseconds, 10000, 26000);
        }

        Assert.Equal(TimeSpan.Zero, HumanJitter.NextGaussianDelay(0, 0));
    }

    [Fact]
    public async Task CometClient_JoinGroup_SendFriendRequest_And_ReactToPost_SucceedWithMockSession()
    {
        FakeBrowserSession joinSession = new("{\"data\":{\"group_join\":{\"group\":{\"viewer_join_state\":\"MEMBER\"}}}}");
        CometClient client = new();

        GroupMembershipState state = await client.JoinGroupAsync(joinSession, "777888");
        Assert.Equal(GroupMembershipState.Member, state);

        FakeBrowserSession friendSession = new("{\"data\":{\"friend_request_send\":{\"friend_requestees\":[{\"friendship_status\":\"OUTGOING_REQUEST\"}]}}}");
        FriendshipState fState = await client.SendFriendRequestAsync(friendSession, "1000999");
        Assert.Equal(FriendshipState.OutgoingRequest, fState);

        FakeBrowserSession reactSession = new("{\"data\":{\"feedback_react\":{\"feedback\":{\"id\":\"fb_1\"}}}}");
        bool reacted = await client.ReactToPostAsync(reactSession, "post_555", ReactionKind.Love);
        Assert.True(reacted);
    }

    private static SessionMaterial CreateDummyMaterial() =>
        new(
            Uid: "1000123456789",
            CookieHeader: "c_user=1000123456789; xs=secret;",
            FbDtsg: "NAcM_test_token",
            Jazoest: ProtocolCodec.ComputeJazoest("NAcM_test_token".AsSpan()),
            Lsd: "AdQVcDLs12345678901234",
            ClientRevision: "1050818501",
            HasteSession: "20745.HYP:comet_pkg.2.1...1",
            WebSessionId: "abc123:def456:ghi789",
            UserAgent: ProtocolConstants.Protocol.DefaultWindowsUserAgent,
            ExtractedAt: DateTimeOffset.UtcNow);

    private sealed class FakeBrowserSession : IBrowserSession
    {
        private readonly string _cannedResponse;

        public FakeBrowserSession(string cannedResponse)
        {
            this._cannedResponse = cannedResponse;
            this.CurrentMaterial = CreateDummyMaterial();
        }

        public string AccountUid => "1000123456789";

        public BrowserState State => BrowserState.Running;

        public SessionMaterial? CurrentMaterial { get; }

        public List<string> NavigatedUrls { get; } = new();

        public string LastPostedBody { get; private set; } = string.Empty;

        public Task NavigateAsync(string url, CancellationToken ct = default)
        {
            this.NavigatedUrls.Add(url);
            return Task.CompletedTask;
        }

        public Task<SessionMaterial> ExtractMaterialAsync(CancellationToken ct = default) =>
            Task.FromResult(this.CurrentMaterial!);

        public Task<int> ImportCookiesAsync(string rawCookieHeader, CancellationToken ct = default) =>
            Task.FromResult(1);

        public Task<string> ExecuteInPagePostAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            string formUrlEncodedBody,
            CancellationToken ct = default)
        {
            this.LastPostedBody = formUrlEncodedBody;
            return Task.FromResult(this._cannedResponse);
        }

        public Task<string> UploadViaDomBridgeAsync(
            string uploadUrl,
            string localFilePath,
            string fileFieldName = "file",
            IReadOnlyDictionary<string, string>? additionalFields = null,
            CancellationToken ct = default) =>
            Task.FromResult("for (;;);{\"payload\":{\"fbid\":\"999888777\"}}");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void LocalSessionFile_WhenPresentLocally_BuildsCompleteGraphQlRequestPayload()
    {
        string? localPath = TryFindLocalSessionFile();
        if (localPath is null)
        {
            // CI/CD environment without .local/test-session.json -> skip gracefully
            return;
        }

        string json = System.IO.File.ReadAllText(localPath);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = doc.RootElement;

        SessionMaterial material = new(
            Uid: root.GetProperty("uid").GetString()!,
            CookieHeader: root.GetProperty("cookieHeader").GetString()!,
            FbDtsg: root.GetProperty("fbDtsg").GetString()!,
            Jazoest: root.GetProperty("jazoest").GetString()!,
            Lsd: root.GetProperty("lsd").GetString()!,
            ClientRevision: root.GetProperty("clientRevision").GetString()!,
            HasteSession: root.GetProperty("hasteSession").GetString()!,
            WebSessionId: "abc123:def456:ghi789",
            UserAgent: root.GetProperty("userAgent").GetString()!,
            ExtractedAt: DateTimeOffset.UtcNow);

        GraphQlRequest req = new(
            FriendlyName: ProtocolConstants.Queries.JoinedGroupsName,
            DocId: ProtocolConstants.Queries.JoinedGroupsDocId,
            VariablesJson: "{\"count\":20,\"scale\":2}");

        string formBody = PayloadBuilder.BuildGraphQlFormBody(material, req, 1);
        Assert.Contains($"av={material.Uid}", formBody, StringComparison.Ordinal);
        Assert.Contains($"jazoest={material.Jazoest}", formBody, StringComparison.Ordinal);
        Assert.Contains($"doc_id={ProtocolConstants.Queries.JoinedGroupsDocId}", formBody, StringComparison.Ordinal);
    }

    private static string? TryFindLocalSessionFile()
    {
        System.IO.DirectoryInfo? dir = new(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, ".local", "test-session.json");
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
