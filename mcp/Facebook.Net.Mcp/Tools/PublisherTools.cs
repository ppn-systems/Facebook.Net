// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;
using Facebook.Net.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Publishing Posts, Commenting, Reacting, and Managing Spintax Campaign Templates.
/// </summary>
[McpServerToolType]
public sealed class PublisherTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_publish_post")]
    [Description("Publish a new post (supporting Spintax '{a|b}' and optional local photo path) to the personal timeline ('Feed') or a community group ('Group').")]
    public async Task<string> PublishPostAsync(
        [Description("Message content or Spintax text to publish")] string messageText,
        [Description("Optional target Facebook Group ID. Leave empty/null to publish to the personal profile timeline.")] string? targetGroupId = null,
        [Description("Optional target group display name (for history logging)")] string? targetGroupName = null,
        [Description("Optional local file path to an image to attach to the post")] string? mediaFilePath = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageText);
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        List<string>? photoIds = null;
        string? trimmedMedia = string.IsNullOrWhiteSpace(mediaFilePath) ? null : mediaFilePath.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedMedia) && File.Exists(trimmedMedia))
        {
            string? uploadedFbid = await _manager.CometClient
                .UploadUfiPhotoAsync(session, trimmedMedia, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(uploadedFbid))
            {
                photoIds = [uploadedFbid];
            }
        }

        bool isGroup = !string.IsNullOrWhiteSpace(targetGroupId);
        ComposerTarget composerTarget = isGroup ? ComposerTarget.Group : ComposerTarget.Feed;
        string? cleanGroupId = isGroup ? targetGroupId!.Trim() : null;
        string groupLabel = !string.IsNullOrWhiteSpace(targetGroupName)
            ? targetGroupName.Trim()
            : (isGroup ? $"Group {cleanGroupId}" : "Trang cá nhân");

        string? postId = await _manager.CometClient
            .PublishPostAsync(session, messageText, composerTarget, cleanGroupId, photoIds, ct: ct)
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
            MessageText: PayloadBuilder.SpinText(messageText),
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

        return JsonSerializer.Serialize(new
        {
            status = "success",
            post_id = recordedPostId,
            account_uid = account.Uid,
            target = isGroup ? "Group" : "Feed",
            target_group_id = cleanGroupId,
            permalink_url = permalink
        });
    }

    [McpServerTool(Name = "fb_create_comment")]
    [Description("Post a comment (supporting Spintax '{a|b}' and optional image attachment) on a Facebook post, with duplicate-comment protection.")]
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
                return JsonSerializer.Serialize(new
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

        string? commentId = await _manager.CometClient
            .CreateCommentAsync(session, cleanPostId, commentText, attachmentFbid, ct)
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

        return JsonSerializer.Serialize(new
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

        return JsonSerializer.Serialize(new
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

        return JsonSerializer.Serialize(new
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

    [McpServerTool(Name = "fb_list_templates")]
    [Description("List all saved Spintax post and comment campaign templates from local SQLite.")]
    public async Task<string> ListTemplatesAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        IReadOnlyList<CampaignTemplate> templates = await _manager.AccountStore
            .GetAllCampaignTemplatesAsync(ct)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
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

        CampaignTemplate tpl = new(
            Id: $"tpl_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Title: title.Trim(),
            SpintaxContent: spintaxContent.Trim(),
            MediaFilePath: string.IsNullOrWhiteSpace(mediaFilePath) ? null : mediaFilePath.Trim(),
            IsCommentTemplate: isCommentTemplate);

        await _manager.AccountStore.UpsertCampaignTemplateAsync(tpl, ct).ConfigureAwait(false);
        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account: null,
            tpl.Title,
            $"MCP saved campaign template '{tpl.Title}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            id = tpl.Id,
            title = tpl.Title,
            is_comment_template = tpl.IsCommentTemplate
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

        int cap = Math.Clamp(maxPosts, 1, 30);
        List<PublishedPostRecord> eligible = recentPosts
            .Where(p => !p.IsPersonalProfilePost &&
                        !string.IsNullOrWhiteSpace(p.PostId) &&
                        !p.PostId.StartsWith("grp_", StringComparison.OrdinalIgnoreCase) &&
                        !p.PostId.StartsWith("feed_", StringComparison.OrdinalIgnoreCase))
            .Take(cap)
            .ToList();

        List<object> bumpedPosts = [];
        for (int i = 0; i < eligible.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PublishedPostRecord post = eligible[i];
            DateTimeOffset nowLocal = DateTimeOffset.Now;
            string groupLabel = !string.IsNullOrWhiteSpace(post.TargetGroupName) ? post.TargetGroupName : "Cộng đồng";
            string randomToken = Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture);

            string resolvedText = effectiveSpintax
                .Replace("{group_name}", groupLabel, StringComparison.OrdinalIgnoreCase)
                .Replace("{date}", nowLocal.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", nowLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{random_id}", randomToken, StringComparison.OrdinalIgnoreCase);

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

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            eligible_count = eligible.Count,
            bumped_count = bumpedPosts.Count,
            bumped_posts = bumpedPosts
        });
    }
}
