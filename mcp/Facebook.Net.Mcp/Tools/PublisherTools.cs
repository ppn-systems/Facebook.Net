// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;
using Meta.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Publishing Posts, Batch Group Campaigns, Timeline Synchronization, Reposting, Commenting, Reacting, Up-Top Bumping, and Managing Spintax Campaign Templates.
/// </summary>
[McpServerToolType]
public sealed class PublisherTools(FacebookMcpSessionManager manager)
{
    private static readonly HashSet<string> s_videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".m4v",
        ".webm",
        ".avi"
    };

    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_publish_post")]
    [Description("Publish a new post (supporting Spintax '{a|b}', dynamic variables '{group_name}', '{date}', '{time}', '{random_id}', and single or semicolon-separated multi-photo or .mp4 video local paths) to the personal timeline ('Feed') or a community group ('Group').")]
    public async Task<string> PublishPostAsync(
        [Description("Message content or Spintax text to publish")] string messageText,
        [Description("Optional target Facebook Group ID. Leave empty/null to publish to the personal profile timeline.")] string? targetGroupId = null,
        [Description("Optional target group display name (for history logging and {group_name} variable)")] string? targetGroupName = null,
        [Description("Optional single or semicolon-separated local image or .mp4 video file paths to attach to the post")] string? mediaFilePath = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string? trimmedMedia = string.IsNullOrWhiteSpace(mediaFilePath) ? null : mediaFilePath.Trim();
        (List<string>? photoIds, List<string>? videoIds) = await this.UploadMediaFilesOnceAsync(session, trimmedMedia, ct).ConfigureAwait(false);

        bool isGroup = !string.IsNullOrWhiteSpace(targetGroupId);
        ComposerTarget composerTarget = isGroup ? ComposerTarget.Group : ComposerTarget.Feed;
        string? cleanGroupId = isGroup ? targetGroupId!.Trim() : null;
        string groupLabel = !string.IsNullOrWhiteSpace(targetGroupName)
            ? targetGroupName.Trim()
            : (isGroup ? $"Group {cleanGroupId}" : "Trang cá nhân");

        string resolvedMessage = _manager.CampaignPublisher.ResolveDynamicVariables(messageText, account.DisplayName, groupLabel);

        string? postId = await _manager.CometClient
            .PublishPostAsync(session, resolvedMessage, composerTarget, cleanGroupId, photoIds, videoIds, ct: ct)
            .ConfigureAwait(false);

        string recordedPostId = !string.IsNullOrWhiteSpace(postId)
            ? postId
            : $"post_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        string? permalink = !string.IsNullOrWhiteSpace(postId)
            ? (isGroup
                ? $"{ProtocolConstants.Endpoints.BaseOrigin}/groups/{cleanGroupId}/posts/{postId}/"
                : $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}")
            : null;

        await _manager.AccountStore.RecordActionAsync(
            new ActionHistoryEntry(
                session.AccountUid,
                "POST",
                cleanGroupId ?? session.AccountUid,
                postId,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);

        PublishedPostRecord record = new(
            PostId: recordedPostId,
            AccountUid: session.AccountUid,
            AuthorName: account.DisplayName,
            AuthorAvatarUrl: account.AvatarUrl,
            MessageText: PayloadBuilder.SpinText(resolvedMessage),
            MediaPreviewUrl: null,
            LocalMediaFilePath: trimmedMedia,
            TargetGroupId: cleanGroupId,
            TargetGroupName: groupLabel,
            TargetGroupAvatarUrl: null,
            PermalinkUrl: permalink,
            CreatedAt: DateTimeOffset.UtcNow,
            IsPersonalProfilePost: !isGroup);

        await _manager.AccountStore.UpsertPublishedPostAsync(record, ct).ConfigureAwait(false);
        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            account,
            groupLabel,
            $"MCP published post to '{groupLabel}'.",
            permalink,
            ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            post_id = recordedPostId,
            account_uid = account.Uid,
            target = isGroup ? "Group" : "Feed",
            target_group_id = cleanGroupId,
            attached_photos_count = photoIds?.Count ?? 0,
            attached_videos_count = videoIds?.Count ?? 0,
            permalink_url = permalink
        });
    }

    [McpServerTool(Name = "fb_publish_campaign_batch")]
    [Description("Execute a multi-group publishing campaign with Spintax and dynamic variables ('{group_name}', '{date}', '{time}', '{random_id}'), single/multi-photo or .mp4 video upload reuse, optional Group Tag filter, multi-account round-robin rotation ('accountUidsCsv'), anti-checkpoint delay, and optional auto-reaction.")]
    public async Task<string> PublishCampaignBatchAsync(
        [Description("Spintax post template to publish across target groups")] string messageSpintax,
        [Description("Optional single or semicolon-separated local image or .mp4 video file paths to upload once per account and attach to all posts")] string? mediaFilePaths = null,
        [Description("Optional group color tag ID or tag name to filter target groups (if omitted, publishes to enabled target groups)")] string? filterByTagIdOrName = null,
        [Description("Maximum number of target groups to publish to (1..100, default 20)")] int maxGroups = 20,
        [Description("Anti-checkpoint delay in seconds between posts (2..120, default 5)")] int delaySeconds = 5,
        [Description("Whether to also publish the post to the account's personal timeline before posting to groups (default false)")] bool alsoPublishToPersonalFeed = false,
        [Description("Whether to automatically react ('Love') to each newly published post for initial engagement (default true)")] bool autoReactAfterPost = true,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        [Description("Optional comma-separated Facebook account UIDs (or 'all') to rotate across target groups in round-robin fashion for anti-checkpoint load balancing")] string? accountUidsCsv = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageSpintax);

        IReadOnlyList<string> rotationUids = await _manager
            .ResolveRotationAccountUidsAsync(accountUidsCsv, accountUid, ct)
            .ConfigureAwait(false);

        (AccountProfile primaryAccount, IBrowserSession primarySession) = await _manager
            .AcquireBrowserSessionAsync(rotationUids[0], forceVisible: false, ct)
            .ConfigureAwait(false);

        List<SavedTargetGroup> targetGroups = await this.ResolveCampaignTargetGroupsAsync(filterByTagIdOrName, Math.Clamp(maxGroups, 1, 100), ct)
            .ConfigureAwait(false);

        string? trimmedMedia = string.IsNullOrWhiteSpace(mediaFilePaths) ? null : mediaFilePaths.Trim();
        Dictionary<string, (List<string>? PhotoIds, List<string>? VideoIds)> mediaCacheByUid = new(StringComparer.Ordinal);

        (List<string>? primaryPhotoIds, List<string>? primaryVideoIds) = await this.UploadMediaFilesOnceAsync(primarySession, trimmedMedia, ct).ConfigureAwait(false);
        mediaCacheByUid[primaryAccount.Uid] = (primaryPhotoIds, primaryVideoIds);

        int delaySec = Math.Clamp(delaySeconds, 2, 120);
        List<object> publishedResults = [];

        if (alsoPublishToPersonalFeed)
        {
            string feedMsg = _manager.CampaignPublisher.ResolveDynamicVariables(messageSpintax, primaryAccount.DisplayName, "Trang cá nhân");
            string? feedPostId = await _manager.CometClient
                .PublishPostAsync(primarySession, feedMsg, ComposerTarget.Feed, null, primaryPhotoIds, primaryVideoIds, ct: ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(feedPostId))
            {
                if (autoReactAfterPost)
                {
                    _ = await _manager.CometClient.ReactToPostAsync(primarySession, feedPostId, ReactionKind.Love, ct).ConfigureAwait(false);
                }

                string feedPermalink = $"{ProtocolConstants.Endpoints.BaseOrigin}/{feedPostId}";
                await _manager.AccountStore.UpsertPublishedPostAsync(
                    new PublishedPostRecord(
                        PostId: feedPostId,
                        AccountUid: primarySession.AccountUid,
                        AuthorName: primaryAccount.DisplayName,
                        AuthorAvatarUrl: primaryAccount.AvatarUrl,
                        MessageText: PayloadBuilder.SpinText(feedMsg),
                        MediaPreviewUrl: null,
                        LocalMediaFilePath: trimmedMedia,
                        TargetGroupId: null,
                        TargetGroupName: "Trang cá nhân",
                        TargetGroupAvatarUrl: null,
                        PermalinkUrl: feedPermalink,
                        CreatedAt: DateTimeOffset.UtcNow,
                        IsPersonalProfilePost: true),
                    ct).ConfigureAwait(false);

                publishedResults.Add(new
                {
                    target = "Feed",
                    account_uid = primaryAccount.Uid,
                    group_id = (string?)null,
                    group_name = "Trang cá nhân",
                    post_id = feedPostId,
                    permalink_url = feedPermalink
                });
            }
        }

        for (int i = 0; i < targetGroups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0 || alsoPublishToPersonalFeed)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            string currentUid = rotationUids[i % rotationUids.Count];
            (AccountProfile account, IBrowserSession session) = string.Equals(currentUid, primaryAccount.Uid, StringComparison.Ordinal)
                ? (primaryAccount, primarySession)
                : await _manager.AcquireBrowserSessionAsync(currentUid, forceVisible: false, ct).ConfigureAwait(false);

            if (!mediaCacheByUid.TryGetValue(account.Uid, out (List<string>? PhotoIds, List<string>? VideoIds) mediaForAccount))
            {
                mediaForAccount = await this.UploadMediaFilesOnceAsync(session, trimmedMedia, ct).ConfigureAwait(false);
                mediaCacheByUid[account.Uid] = mediaForAccount;
            }

            SavedTargetGroup grp = targetGroups[i];
            string personalized = _manager.CampaignPublisher.ResolveDynamicVariables(messageSpintax, account.DisplayName, grp.GroupName);
            string? postId = await _manager.CometClient
                .PublishPostAsync(session, personalized, ComposerTarget.Group, grp.GroupId, mediaForAccount.PhotoIds, mediaForAccount.VideoIds, ct: ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(postId))
            {
                if (autoReactAfterPost)
                {
                    _ = await _manager.CometClient.ReactToPostAsync(session, postId, ReactionKind.Love, ct).ConfigureAwait(false);
                }

                string permalink = $"{ProtocolConstants.Endpoints.BaseOrigin}/groups/{grp.GroupId}/posts/{postId}/";
                await _manager.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(session.AccountUid, "POST", grp.GroupId, postId, DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);

                await _manager.AccountStore.UpsertPublishedPostAsync(
                    new PublishedPostRecord(
                        PostId: postId,
                        AccountUid: session.AccountUid,
                        AuthorName: account.DisplayName,
                        AuthorAvatarUrl: account.AvatarUrl,
                        MessageText: PayloadBuilder.SpinText(personalized),
                        MediaPreviewUrl: null,
                        LocalMediaFilePath: trimmedMedia,
                        TargetGroupId: grp.GroupId,
                        TargetGroupName: grp.GroupName,
                        TargetGroupAvatarUrl: grp.AvatarUrl,
                        PermalinkUrl: permalink,
                        CreatedAt: DateTimeOffset.UtcNow,
                        IsPersonalProfilePost: false),
                    ct).ConfigureAwait(false);

                publishedResults.Add(new
                {
                    target = "Group",
                    account_uid = account.Uid,
                    group_id = grp.GroupId,
                    group_name = grp.GroupName,
                    post_id = postId,
                    permalink_url = permalink
                });
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            primaryAccount,
            "Campaign Batch",
            $"MCP published campaign batch ({publishedResults.Count} posts across {targetGroups.Count} target groups using {rotationUids.Count} account(s)).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = primaryAccount.Uid,
            accounts_rotated = rotationUids,
            target_groups_selected = targetGroups.Count,
            published_count = publishedResults.Count,
            attached_photos_count = primaryPhotoIds?.Count ?? 0,
            attached_videos_count = primaryVideoIds?.Count ?? 0,
            posts = publishedResults
        });
    }

    [McpServerTool(Name = "fb_sync_personal_timeline_posts")]
    [Description("Synchronize recent posts from the active account's personal Facebook timeline via Comet GraphQL into local SQLite so they can be inspected or reposted to community groups.")]
    public async Task<string> SyncPersonalTimelinePostsAsync(
        [Description("Number of recent personal timeline posts to fetch (1..50, default 25)")] int count = 25,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        PagedResult<PostSummary> timeline = await _manager.CometClient
            .GetTimelinePostsAsync(session, count: Math.Clamp(count, 1, 50), ct: ct)
            .ConfigureAwait(false);

        List<PublishedPostRecord> records = new(timeline.Items.Count);
        for (int i = 0; i < timeline.Items.Count; i++)
        {
            PostSummary p = timeline.Items[i];
            if (string.IsNullOrWhiteSpace(p.PostId))
            {
                continue;
            }

            records.Add(new PublishedPostRecord(
                PostId: p.PostId,
                AccountUid: account.Uid,
                AuthorName: !string.IsNullOrWhiteSpace(p.AuthorName) ? p.AuthorName : account.DisplayName,
                AuthorAvatarUrl: p.AuthorAvatarUrl ?? account.AvatarUrl,
                MessageText: p.MessageText ?? string.Empty,
                MediaPreviewUrl: p.MediaPreviewUrl,
                LocalMediaFilePath: null,
                TargetGroupId: null,
                TargetGroupName: "Trang cá nhân",
                TargetGroupAvatarUrl: null,
                PermalinkUrl: p.PermalinkUrl ?? $"{ProtocolConstants.Endpoints.BaseOrigin}/{p.PostId}",
                CreatedAt: p.CreatedAt ?? DateTimeOffset.UtcNow,
                IsPersonalProfilePost: true));
        }

        if (records.Count > 0)
        {
            await _manager.AccountStore.UpsertPublishedPostsBatchAsync(records, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account,
            "Personal Timeline",
            $"MCP synced {records.Count} personal timeline posts.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            synced_count = records.Count,
            posts = records.Select(r => new
            {
                post_id = r.PostId,
                message_text = r.MessageText,
                media_preview_url = r.MediaPreviewUrl,
                permalink_url = r.PermalinkUrl,
                created_at = r.CreatedAt.ToString("o")
            })
        });
    }

    [McpServerTool(Name = "fb_repost_to_groups")]
    [Description("Repost or share an existing published/personal post across enabled Target Groups (or groups matching filterByTagIdOrName) with Spintax caption personalization and anti-checkpoint delay.")]
    public async Task<string> RepostToGroupsAsync(
        [Description("Source Post ID from published_posts to share/repost to groups")] string sourcePostId,
        [Description("Optional custom Spintax caption (if omitted, reuses the source post's message text)")] string? customCaptionSpintax = null,
        [Description("Optional group color tag ID or tag name to filter target groups")] string? filterByTagIdOrName = null,
        [Description("Maximum number of target groups to repost to (1..50, default 15)")] int maxGroups = 15,
        [Description("Anti-checkpoint delay in seconds between reposts (2..120, default 5)")] int delaySeconds = 5,
        [Description("Whether to automatically react ('Love') to each reposted group post (default true)")] bool autoReactAfterPost = true,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePostId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanSourceId = sourcePostId.Trim();
        IReadOnlyList<PublishedPostRecord> savedPosts = await _manager.AccountStore
            .GetPublishedPostsAsync(null, 200, ct)
            .ConfigureAwait(false);
        PublishedPostRecord? sourceRecord = savedPosts.FirstOrDefault(p => string.Equals(p.PostId, cleanSourceId, StringComparison.Ordinal));

        string baseTemplate = !string.IsNullOrWhiteSpace(customCaptionSpintax)
            ? customCaptionSpintax.Trim()
            : (sourceRecord?.MessageText ?? string.Empty);

        List<SavedTargetGroup> targetGroups = await this.ResolveCampaignTargetGroupsAsync(filterByTagIdOrName, Math.Clamp(maxGroups, 1, 50), ct)
            .ConfigureAwait(false);

        List<string>? fallbackPhotoIds = null;
        List<string>? fallbackVideoIds = null;
        if (!string.IsNullOrWhiteSpace(sourceRecord?.LocalMediaFilePath))
        {
            (fallbackPhotoIds, fallbackVideoIds) = await this.UploadMediaFilesOnceAsync(session, sourceRecord.LocalMediaFilePath, ct).ConfigureAwait(false);
        }

        int delaySec = Math.Clamp(delaySeconds, 2, 120);
        List<object> reposted = [];

        for (int i = 0; i < targetGroups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            SavedTargetGroup grp = targetGroups[i];
            string caption = _manager.CampaignPublisher.ResolveDynamicVariables(baseTemplate, account.DisplayName, grp.GroupName);

            string? newPostId = await _manager.CometClient
                .SharePostAsync(session, cleanSourceId, caption, grp.GroupId, ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(newPostId) && !string.IsNullOrWhiteSpace(caption))
            {
                newPostId = await _manager.CometClient
                    .PublishPostAsync(session, caption, ComposerTarget.Group, grp.GroupId, fallbackPhotoIds, fallbackVideoIds, ct: ct)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(newPostId))
            {
                if (autoReactAfterPost)
                {
                    _ = await _manager.CometClient.ReactToPostAsync(session, newPostId, ReactionKind.Love, ct).ConfigureAwait(false);
                }

                string permalink = $"{ProtocolConstants.Endpoints.BaseOrigin}/groups/{grp.GroupId}/posts/{newPostId}/";
                await _manager.AccountStore.UpsertPublishedPostAsync(
                    new PublishedPostRecord(
                        PostId: newPostId,
                        AccountUid: session.AccountUid,
                        AuthorName: account.DisplayName,
                        AuthorAvatarUrl: account.AvatarUrl,
                        MessageText: PayloadBuilder.SpinText(caption),
                        MediaPreviewUrl: sourceRecord?.MediaPreviewUrl,
                        LocalMediaFilePath: sourceRecord?.LocalMediaFilePath,
                        TargetGroupId: grp.GroupId,
                        TargetGroupName: grp.GroupName,
                        TargetGroupAvatarUrl: grp.AvatarUrl,
                        PermalinkUrl: permalink,
                        CreatedAt: DateTimeOffset.UtcNow,
                        IsPersonalProfilePost: false),
                    ct).ConfigureAwait(false);

                reposted.Add(new
                {
                    group_id = grp.GroupId,
                    group_name = grp.GroupName,
                    post_id = newPostId,
                    permalink_url = permalink
                });
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            account,
            "Repost to Groups",
            $"MCP reposted '{cleanSourceId}' to {reposted.Count}/{targetGroups.Count} groups.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            source_post_id = cleanSourceId,
            target_groups_selected = targetGroups.Count,
            reposted_count = reposted.Count,
            reposted_posts = reposted
        });
    }

    [McpServerTool(Name = "fb_create_comment")]
    [Description("Post a comment (supporting Spintax '{a|b}', dynamic variables '{author_name}', '{group_name}', '{date}', '{time}', '{random_id}', and optional image attachment) on a Facebook post, with duplicate-comment protection.")]
    public async Task<string> CreateCommentAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("Comment text or Spintax template")] string commentText,
        [Description("Optional local image file path to attach to the comment")] string? mediaFilePath = null,
        [Description("Skip commenting if this account has already commented on this post (default true)")] bool skipIfAlreadyCommented = true,
        [Description("Automatically react (Like/Love) to the post before commenting (default false)")] bool autoReactFirst = false,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commentText);

        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string cleanPostId = postId.Trim();
        if (skipIfAlreadyCommented)
        {
            bool alreadyDone = await _manager.AccountStore
                .HasExecutedActionAsync(session.AccountUid, "COMMENT", cleanPostId, ct)
                .ConfigureAwait(false);
            if (alreadyDone)
            {
                return McpJson.Serialize(new
                {
                    status = "skipped_duplicate",
                    post_id = cleanPostId,
                    account_uid = account.Uid,
                    message = "This account has already commented on the specified post."
                });
            }
        }

        if (autoReactFirst)
        {
            ReactionKind reaction = Random.Shared.NextDouble() < 0.75 ? ReactionKind.Like : ReactionKind.Love;
            _ = await _manager.CometClient.ReactToPostAsync(session, cleanPostId, reaction, ct).ConfigureAwait(false);
        }

        string? attachmentFbid = null;
        if (!string.IsNullOrWhiteSpace(mediaFilePath) && File.Exists(mediaFilePath.Trim()))
        {
            attachmentFbid = await _manager.CometClient
                .UploadUfiPhotoAsync(session, mediaFilePath.Trim(), ct)
                .ConfigureAwait(false);
        }

        IReadOnlyList<PostSummary> cachedPosts = await _manager.AccountStore
            .QueryScrapedPostsAsync(groupId: null, searchKeyword: null, limit: 500, ct: ct)
            .ConfigureAwait(false);
        PostSummary? matchedPost = cachedPosts.FirstOrDefault(p => string.Equals(p.PostId, cleanPostId, StringComparison.Ordinal));
        string resolvedComment = _manager.CampaignPublisher.ResolveDynamicVariables(commentText, matchedPost?.AuthorName, matchedPost?.GroupName);

        string? commentId = await _manager.CometClient
            .CreateCommentAsync(session, cleanPostId, resolvedComment, attachmentFbid, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(commentId))
        {
            await _manager.AccountStore.RecordActionAsync(
                new ActionHistoryEntry(
                    session.AccountUid,
                    "COMMENT",
                    cleanPostId,
                    commentId,
                    DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            account,
            cleanPostId,
            $"MCP posted comment on post '{cleanPostId}'.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            post_id = cleanPostId,
            comment_id = commentId,
            account_uid = account.Uid
        });
    }

    [McpServerTool(Name = "fb_react_to_post")]
    [Description("React to a Facebook post with Like, Love, Care, Haha, Wow, Sad, or Angry.")]
    public async Task<string> ReactToPostAsync(
        [Description("Target Facebook Post ID")] string postId,
        [Description("Reaction kind: 'Like', 'Love', 'Care', 'Haha', 'Wow', 'Sad', 'Angry' (default 'Like')")] string reaction = "Like",
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        ReactionKind parsedReaction = Enum.TryParse<ReactionKind>(reaction, ignoreCase: true, out ReactionKind r)
            ? r
            : ReactionKind.Like;

        bool ok = await _manager.CometClient
            .ReactToPostAsync(session, postId.Trim(), parsedReaction, ct)
            .ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            ok ? AuditSeverity.Success : AuditSeverity.Warning,
            account,
            postId.Trim(),
            $"MCP reacted '{parsedReaction}' on post '{postId.Trim()}' (ok={ok}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = ok ? "success" : "failed",
            post_id = postId.Trim(),
            reaction = parsedReaction.ToString(),
            account_uid = account.Uid
        });
    }

    [McpServerTool(Name = "fb_list_published_posts")]
    [Description("List published posts and synchronized personal timeline posts stored in local SQLite.")]
    public async Task<string> ListPublishedPostsAsync(
        [Description("Optional account UID filter")] string? accountUid = null,
        [Description("Maximum number of posts to return (default 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        IReadOnlyList<PublishedPostRecord> posts = await _manager.AccountStore
            .GetPublishedPostsAsync(accountUid, Math.Clamp(limit, 1, 200), ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            count = posts.Count,
            posts = posts.Select(p => new
            {
                post_id = p.PostId,
                account_uid = p.AccountUid,
                author_name = p.AuthorName,
                message_text = p.MessageText,
                target_group_id = p.TargetGroupId,
                target_group_name = p.TargetGroupName,
                is_personal_profile = p.IsPersonalProfilePost,
                permalink_url = p.PermalinkUrl,
                created_at = p.CreatedAt.ToString("o")
            })
        });
    }

    [McpServerTool(Name = "fb_delete_published_post")]
    [Description("Delete a published post record from the local SQLite history table by Post ID.")]
    public async Task<string> DeletePublishedPostAsync(
        [Description("Published Post ID to delete from local SQLite")] string postId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        bool deleted = await _manager.AccountStore.DeletePublishedPostAsync(postId.Trim(), ct).ConfigureAwait(false);
        if (deleted)
        {
            _ = await _manager.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Info,
                account: null,
                postId.Trim(),
                $"MCP deleted published post record '{postId.Trim()}' from local SQLite.",
                ct: ct).ConfigureAwait(false);
        }

        return McpJson.Serialize(new
        {
            status = deleted ? "deleted" : "not_found",
            post_id = postId.Trim()
        });
    }

    [McpServerTool(Name = "fb_list_templates")]
    [Description("List all saved Spintax post and comment campaign templates from local SQLite.")]
    public async Task<string> ListTemplatesAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        IReadOnlyList<CampaignTemplate> templates = await _manager.AccountStore
            .GetAllCampaignTemplatesAsync(ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            count = templates.Count,
            templates = templates.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                spintax_content = t.SpintaxContent,
                media_file_path = t.MediaFilePath,
                is_comment_template = t.IsCommentTemplate
            })
        });
    }

    [McpServerTool(Name = "fb_save_template")]
    [Description("Create or update a Spintax post or comment campaign template in local SQLite.")]
    public async Task<string> SaveTemplateAsync(
        [Description("Template title")] string title,
        [Description("Spintax content (e.g. '{Xin chào|Chào mọi người}, ...')")] string spintaxContent,
        [Description("True for a bump/sales comment template, false for a post template")] bool isCommentTemplate = false,
        [Description("Optional local media file path")] string? mediaFilePath = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(spintaxContent);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        CampaignTemplate tpl = _manager.CampaignPublisher.CreateTemplate(title, spintaxContent, mediaFilePath, isCommentTemplate);

        await _manager.AccountStore.UpsertCampaignTemplateAsync(tpl, ct).ConfigureAwait(false);
        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            tpl.Title,
            $"MCP saved campaign template '{tpl.Title}'.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            id = tpl.Id,
            title = tpl.Title,
            is_comment_template = tpl.IsCommentTemplate
        });
    }

    [McpServerTool(Name = "fb_delete_template")]
    [Description("Delete a saved Spintax post or comment template from local SQLite by its template ID or title.")]
    public async Task<string> DeleteTemplateAsync(
        [Description("Template ID or template title to delete")] string templateIdOrTitle,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdOrTitle);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanTarget = templateIdOrTitle.Trim();
        IReadOnlyList<CampaignTemplate> allTemplates = await _manager.AccountStore.GetAllCampaignTemplatesAsync(ct).ConfigureAwait(false);
        CampaignTemplate? matched = allTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Title, cleanTarget, StringComparison.OrdinalIgnoreCase));

        string targetId = matched?.Id ?? cleanTarget;
        bool deleted = await _manager.AccountStore.DeleteCampaignTemplateAsync(targetId, ct).ConfigureAwait(false);
        if (deleted)
        {
            _ = await _manager.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                account: null,
                matched?.Title ?? targetId,
                $"MCP deleted campaign template '{matched?.Title ?? targetId}'.",
                ct: ct).ConfigureAwait(false);
        }

        return McpJson.Serialize(new
        {
            status = deleted ? "deleted" : "not_found",
            template_id = targetId,
            title = matched?.Title
        });
    }

    [McpServerTool(Name = "fb_bump_recent_posts")]
    [Description("Up-top / bump recent published community group posts by posting a Spintax comment (supporting {group_name}, {date}, {time}, {random_id} placeholders) on up to maxPosts posts.")]
    public async Task<string> BumpRecentPostsAsync(
        [Description("Spintax comment template to bump posts (default '{Up|Chấm|Quan tâm|Báo giá chi tiết inbox nhé}')")] string bumpCommentSpintax = "{Up|Chấm|Quan tâm|Báo giá chi tiết inbox nhé}",
        [Description("Maximum number of recent group posts to bump (1..30, default 10)")] int maxPosts = 10,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        string effectiveSpintax = string.IsNullOrWhiteSpace(bumpCommentSpintax)
            ? "{Up|Chấm|Quan tâm|Báo giá chi tiết inbox nhé}"
            : bumpCommentSpintax.Trim();

        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<PublishedPostRecord> recentPosts = await _manager.AccountStore
            .GetPublishedPostsAsync(account.Uid, 50, ct)
            .ConfigureAwait(false);

        List<PublishedPostRecord> eligible = _manager.CampaignPublisher
            .SelectEligibleBumpPosts(recentPosts, account.Uid, Math.Clamp(maxPosts, 1, 30));

        List<object> bumpedPosts = [];
        for (int i = 0; i < eligible.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PublishedPostRecord post = eligible[i];
            string groupLabel = !string.IsNullOrWhiteSpace(post.TargetGroupName) ? post.TargetGroupName : "Cộng đồng";
            string resolvedText = _manager.CampaignPublisher.ResolveDynamicVariables(effectiveSpintax, account.DisplayName, groupLabel);

            string? commentId = await _manager.CometClient
                .CreateCommentAsync(session, post.PostId, resolvedText, null, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(commentId))
            {
                await _manager.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        session.AccountUid,
                        "BUMP_COMMENT",
                        post.PostId,
                        commentId,
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);

                bumpedPosts.Add(new
                {
                    post_id = post.PostId,
                    comment_id = commentId,
                    target_group_id = post.TargetGroupId,
                    target_group_name = post.TargetGroupName,
                    permalink_url = post.PermalinkUrl
                });
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            account,
            "Up-Top Bump",
            $"MCP bumped {bumpedPosts.Count}/{eligible.Count} recent group posts.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            eligible_count = eligible.Count,
            bumped_count = bumpedPosts.Count,
            bumped_posts = bumpedPosts
        });
    }

    [McpServerTool(Name = "fb_share_post_to_groups_batch")]
    [Description("Native GraphQL Share Campaign: share an existing Facebook post ('postId' from a Fanpage, Personal Timeline, or Group) into multiple target groups via 'SharePostAsync' while preserving original post engagement, with Spintax caption, anti-checkpoint random delay, and optional multi-account round-robin rotation ('accountUidsCsv').")]
    public async Task<string> SharePostToGroupsBatchAsync(
        [Description("Target Facebook Post ID to share natively into groups")] string postId,
        [Description("Optional Spintax commentary/caption added above the shared post (supports {group_name}, {date}, {time}, {random_id})")] string? messageSpintax = null,
        [Description("Optional comma- or semicolon-separated target group IDs. Leave empty to use enabled target groups or filterByTagIdOrName.")] string? groupIdsCsv = null,
        [Description("Optional group color tag ID or name filter when groupIdsCsv is omitted")] string? filterByTagIdOrName = null,
        [Description("Maximum number of target groups to share into (1..50, default 10)")] int maxGroups = 10,
        [Description("Minimum anti-checkpoint delay in seconds between group shares (default 8)")] int minDelaySeconds = 8,
        [Description("Maximum anti-checkpoint delay in seconds between group shares (default 18)")] int maxDelaySeconds = 18,
        [Description("Optional comma-separated Facebook account UIDs (or 'all' / 'smart') for coolest-first multi-account rotation across groups.")] string? accountUidsCsv = null,
        [Description("Target Facebook account UID when not rotating (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanPostId = postId.Trim();
        int cap = Math.Clamp(maxGroups, 1, 50);
        int minSec = Math.Clamp(minDelaySeconds, 1, 600);
        int maxSec = Math.Max(minSec, Math.Clamp(maxDelaySeconds, 1, 600));

        List<SavedTargetGroup> targetGroups = [];
        if (!string.IsNullOrWhiteSpace(groupIdsCsv))
        {
            string[] rawIds = groupIdsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IReadOnlyList<SavedTargetGroup> savedGroups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
            Dictionary<string, SavedTargetGroup> savedById = new(StringComparer.Ordinal);
            for (int i = 0; i < savedGroups.Count; i++)
            {
                savedById[savedGroups[i].GroupId] = savedGroups[i];
            }

            for (int i = 0; i < rawIds.Length && targetGroups.Count < cap; i++)
            {
                string gid = rawIds[i];
                if (savedById.TryGetValue(gid, out SavedTargetGroup? existing))
                {
                    targetGroups.Add(existing);
                }
                else
                {
                    targetGroups.Add(new SavedTargetGroup(gid, $"Group {gid}", IsEnabled: true));
                }
            }
        }
        else
        {
            targetGroups = await this.ResolveCampaignTargetGroupsAsync(filterByTagIdOrName, cap, ct).ConfigureAwait(false);
        }

        if (targetGroups.Count == 0)
        {
            return McpJson.Serialize(new
            {
                status = "no_target_groups",
                post_id = cleanPostId,
                message = "No matching target groups found to share the post into."
            });
        }

        IReadOnlyList<string> rotationUids = await _manager
            .ResolveRotationAccountUidsAsync(accountUidsCsv, accountUid, ct)
            .ConfigureAwait(false);

        int succeeded = 0;
        int failed = 0;
        List<object> results = [];

        for (int i = 0; i < targetGroups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            SavedTargetGroup group = targetGroups[i];
            string currentUid = rotationUids[i % rotationUids.Count];

            (AccountProfile account, IBrowserSession session) = await _manager
                .AcquireBrowserSessionAsync(currentUid, forceVisible: false, ct)
                .ConfigureAwait(false);

            string resolvedCaption = string.IsNullOrWhiteSpace(messageSpintax)
                ? string.Empty
                : PayloadBuilder.SpinText(
                    _manager.CampaignPublisher.ResolveDynamicVariables(
                        messageSpintax,
                        account.DisplayName,
                        group.GroupName));

            string? sharedStoryId = await _manager.CometClient.SharePostAsync(
                session,
                cleanPostId,
                resolvedCaption,
                targetGroupId: group.GroupId,
                ct: ct).ConfigureAwait(false);

            bool ok = !string.IsNullOrWhiteSpace(sharedStoryId);
            if (ok)
            {
                succeeded++;
                await _manager.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        account.Uid,
                        "POST",
                        group.GroupId,
                        $"Shared post {cleanPostId} to {group.GroupName}",
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);
            }
            else
            {
                failed++;
            }

            results.Add(new
            {
                group_id = group.GroupId,
                group_name = group.GroupName,
                account_uid = account.Uid,
                success = ok,
                shared_story_id = sharedStoryId
            });

            if (i < targetGroups.Count - 1)
            {
                int delayMs = Random.Shared.Next(minSec * 1000, (maxSec * 1000) + 1);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            succeeded > 0 ? AuditSeverity.Success : AuditSeverity.Warning,
            account: null,
            cleanPostId,
            $"MCP shared post '{cleanPostId}' across {targetGroups.Count} group(s) (succeeded={succeeded}, failed={failed}, accounts={rotationUids.Count}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = failed == 0 ? "success" : (succeeded > 0 ? "partial_success" : "failed"),
            source_post_id = cleanPostId,
            accounts_rotated = rotationUids.Count,
            total_groups = targetGroups.Count,
            succeeded,
            failed,
            results
        });
    }

    private async Task<(List<string>? PhotoIds, List<string>? VideoIds)> UploadMediaFilesOnceAsync(
        IBrowserSession session,
        string? rawPaths,
        CancellationToken ct)
    {
        List<string> existingPaths = _manager.CampaignPublisher.ParseExistingMediaFilePaths(rawPaths);
        if (existingPaths.Count == 0)
        {
            return (null, null);
        }

        List<string> uploadedPhotoIds = [];
        List<string> uploadedVideoIds = [];

        for (int i = 0; i < existingPaths.Count; i++)
        {
            string filePath = existingPaths[i];
            string ext = Path.GetExtension(filePath);
            if (s_videoExtensions.Contains(ext))
            {
                string? videoId = await _manager.CometClient
                    .UploadVideoAsync(session, filePath, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    uploadedVideoIds.Add(videoId);
                }
            }
            else
            {
                string? uploadedFbid = await _manager.CometClient
                    .UploadUfiPhotoAsync(session, filePath, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(uploadedFbid))
                {
                    uploadedPhotoIds.Add(uploadedFbid);
                }
            }
        }

        return (
            uploadedPhotoIds.Count > 0 ? uploadedPhotoIds : null,
            uploadedVideoIds.Count > 0 ? uploadedVideoIds : null);
    }

    private async Task<List<SavedTargetGroup>> ResolveCampaignTargetGroupsAsync(
        string? filterByTagIdOrName,
        int maxGroups,
        CancellationToken ct)
    {
        IReadOnlyList<SavedTargetGroup> allGroups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
        string? effectiveTagId = null;

        if (!string.IsNullOrWhiteSpace(filterByTagIdOrName))
        {
            string cleanTag = filterByTagIdOrName.Trim();
            string normTag = _manager.LeadScoring.RemoveVietnameseDiacritics(cleanTag);
            IReadOnlyList<GroupTag> groupTags = await _manager.AccountStore.GetAllGroupTagsAsync(ct).ConfigureAwait(false);
            GroupTag? matchedTag = groupTags.FirstOrDefault(t =>
                string.Equals(t.Id, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, cleanTag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_manager.LeadScoring.RemoveVietnameseDiacritics(t.Name), normTag, StringComparison.OrdinalIgnoreCase));
            effectiveTagId = matchedTag?.Id ?? cleanTag;
        }

        List<SavedTargetGroup> filtered = [];
        for (int i = 0; i < allGroups.Count && filtered.Count < maxGroups; i++)
        {
            SavedTargetGroup g = allGroups[i];
            if (!string.IsNullOrWhiteSpace(effectiveTagId))
            {
                if (g.HasTag(effectiveTagId))
                {
                    filtered.Add(g);
                }
            }
            else if (g.IsEnabled)
            {
                filtered.Add(g);
            }
        }

        return filtered;
    }
}
