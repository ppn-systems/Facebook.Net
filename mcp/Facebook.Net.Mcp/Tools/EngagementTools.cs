// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Protocol;
using Meta.UI.Core.Audit;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Stories, Notifications, and Managed Fanpages / Additional Profiles.
/// Enables AI agents to monitor customer replies via notifications, warm up accounts via Story viewing/replying,
/// publish photo/video/text Stories, and inspect managed pages.
/// </summary>
[McpServerToolType]
public sealed class EngagementTools(FacebookMcpSessionManager manager)
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

    [McpServerTool(Name = "fb_get_notifications")]
    [Description("Fetch recent Facebook notifications via Comet GraphQL to monitor customer replies, group post approvals, and mentions.")]
    public async Task<string> GetNotificationsAsync(
        [Description("If true, only return unread notifications (default false)")] bool onlyUnread = false,
        [Description("Optional keyword to filter notification descriptions (accent-insensitive)")] string? keyword = null,
        [Description("Maximum number of notifications to return (1..100, default 30)")] int limit = 30,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<NotificationItem> allNotifications = await _manager.CometClient
            .GetNotificationsAsync(session, ct)
            .ConfigureAwait(false);

        int cap = Math.Clamp(limit, 1, 100);
        string? trimmedKw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        List<NotificationItem> filtered = [];

        for (int i = 0; i < allNotifications.Count && filtered.Count < cap; i++)
        {
            NotificationItem item = allNotifications[i];
            if (onlyUnread && item.IsRead)
            {
                continue;
            }

            if (trimmedKw is not null && !_manager.LeadScoring.MatchesAccentInsensitive(item.Description, trimmedKw))
            {
                continue;
            }

            filtered.Add(item);
        }

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            total_fetched = allNotifications.Count,
            unread_count = allNotifications.Count(n => !n.IsRead),
            returned_count = filtered.Count,
            notifications = filtered.Select(n => new
            {
                notification_id = n.NotificationId,
                query_id = n.QueryId,
                description = n.Description,
                target_url = n.TargetUrl,
                is_read = n.IsRead
            })
        });
    }

    [McpServerTool(Name = "fb_mark_notifications_read")]
    [Description("Mark one or all unread Facebook notifications as read via Comet GraphQL.")]
    public async Task<string> MarkNotificationsReadAsync(
        [Description("Optional comma-separated notification IDs to mark as read. Leave empty to mark all currently unread notifications as read.")] string? notificationIdsCsv = null,
        [Description("Maximum number of unread notifications to mark as read (1..50, default 20)")] int maxCount = 20,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<NotificationItem> notifications = await _manager.CometClient
            .GetNotificationsAsync(session, ct)
            .ConfigureAwait(false);

        HashSet<string>? targetIds = string.IsNullOrWhiteSpace(notificationIdsCsv)
            ? null
            : new HashSet<string>(
                notificationIdsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal);

        int cap = Math.Clamp(maxCount, 1, 50);
        int markedCount = 0;

        for (int i = 0; i < notifications.Count && markedCount < cap; i++)
        {
            NotificationItem n = notifications[i];
            if (targetIds is not null)
            {
                if (!targetIds.Contains(n.NotificationId))
                {
                    continue;
                }
            }
            else if (n.IsRead)
            {
                continue;
            }

            bool ok = await _manager.CometClient.MarkNotificationReadAsync(session, n, ct).ConfigureAwait(false);
            if (ok)
            {
                markedCount++;
            }
        }

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            marked_read_count = markedCount
        });
    }

    [McpServerTool(Name = "fb_get_stories_tray")]
    [Description("Fetch active story buckets from the Facebook Stories tray via Comet GraphQL.")]
    public async Task<string> GetStoriesTrayAsync(
        [Description("If true, only return unseen stories (default false)")] bool onlyUnseen = false,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<StoryBucketItem> tray = await _manager.CometClient
            .GetStoriesTrayAsync(session, ct)
            .ConfigureAwait(false);

        var items = tray
            .Where(s => !onlyUnseen || !s.IsSeen)
            .Select(s => new
            {
                bucket_id = s.BucketId,
                story_id = s.StoryId,
                author_uid = s.AuthorUid,
                is_seen = s.IsSeen
            })
            .ToList();

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            total_tray_count = tray.Count,
            returned_count = items.Count,
            stories = items
        });
    }

    [McpServerTool(Name = "fb_watch_and_reply_stories_batch")]
    [Description("Automated Story Warmup: watch unseen Facebook Stories in the tray ('storiesUpdateSeenStateMutation') and optionally send a Spintax emoji or message reply ('useStoriesSendReplyMutation') with anti-checkpoint delay.")]
    public async Task<string> WatchAndReplyStoriesBatchAsync(
        [Description("Maximum number of stories to mark as seen (1..30, default 5)")] int maxStoriesToWatch = 5,
        [Description("Optional Spintax emoji or reply text (e.g. '{❤️|🔥|😍|Tuyệt quá ạ}') to send on stories. Leave empty to only watch without replying.")] string? replySpintax = null,
        [Description("Anti-checkpoint delay in seconds between stories (1..60, default 3)")] int delaySeconds = 3,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<StoryBucketItem> tray = await _manager.CometClient
            .GetStoriesTrayAsync(session, ct)
            .ConfigureAwait(false);

        int cap = Math.Clamp(maxStoriesToWatch, 1, 30);
        int delaySec = Math.Clamp(delaySeconds, 1, 60);
        List<StoryBucketItem> candidates = tray.Where(s => !s.IsSeen).Take(cap).ToList();
        if (candidates.Count == 0)
        {
            candidates = tray.Take(cap).ToList();
        }

        int seenCount = 0;
        int repliedCount = 0;
        List<object> results = [];

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            }

            StoryBucketItem story = candidates[i];
            bool markedSeen = await _manager.CometClient
                .MarkStorySeenAsync(session, story.BucketId, story.StoryId, ct)
                .ConfigureAwait(false);
            if (markedSeen)
            {
                seenCount++;
            }

            bool replied = false;
            if (!string.IsNullOrWhiteSpace(replySpintax))
            {
                string message = PayloadBuilder.SpinText(replySpintax.Trim());
                replied = await _manager.CometClient
                    .ReplyToStoryAsync(session, story.StoryId, message, ct)
                    .ConfigureAwait(false);
                if (replied)
                {
                    repliedCount++;
                }
            }

            results.Add(new
            {
                bucket_id = story.BucketId,
                story_id = story.StoryId,
                author_uid = story.AuthorUid,
                marked_seen = markedSeen,
                replied
            });
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            account,
            "Story Warmup",
            $"MCP watched {seenCount} stories and replied to {repliedCount} stories.",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            watched_count = seenCount,
            replied_count = repliedCount,
            stories = results
        });
    }

    [McpServerTool(Name = "fb_create_story")]
    [Description("Publish a new Facebook Story supporting Spintax text, local photo upload ('UploadStoryPhotoAsync'), or local video upload ('UploadVideoAsync').")]
    public async Task<string> CreateStoryAsync(
        [Description("Optional Story caption or Spintax text")] string? textSpintax = null,
        [Description("Optional local image or .mp4 video file path to upload to the Story")] string? mediaFilePath = null,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textSpintax) && string.IsNullOrWhiteSpace(mediaFilePath))
        {
            throw new ArgumentException("Either 'textSpintax' or 'mediaFilePath' must be provided to create a Story.");
        }

        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        string? photoId = null;
        string? videoId = null;

        if (!string.IsNullOrWhiteSpace(mediaFilePath))
        {
            string cleanPath = mediaFilePath.Trim();
            if (!File.Exists(cleanPath))
            {
                throw new FileNotFoundException($"Story media file not found: '{cleanPath}'", cleanPath);
            }

            string ext = Path.GetExtension(cleanPath);
            if (s_videoExtensions.Contains(ext))
            {
                videoId = await _manager.CometClient.UploadVideoAsync(session, cleanPath, ct).ConfigureAwait(false);
            }
            else
            {
                photoId = await _manager.CometClient.UploadStoryPhotoAsync(session, cleanPath, ct).ConfigureAwait(false);
            }
        }

        string? resolvedText = string.IsNullOrWhiteSpace(textSpintax)
            ? null
            : PayloadBuilder.SpinText(
                _manager.CampaignPublisher.ResolveDynamicVariables(textSpintax.Trim(), account.DisplayName, "Story"));

        bool created = await _manager.CometClient
            .CreateStoryAsync(session, resolvedText, photoId, videoId, ct)
            .ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Post,
            created ? AuditSeverity.Success : AuditSeverity.Warning,
            account,
            "Facebook Story",
            $"MCP published Facebook Story (success={created}, hasPhoto={photoId is not null}, hasVideo={videoId is not null}).",
            ct: ct).ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = created ? "success" : "failed",
            account_uid = account.Uid,
            photo_id = photoId,
            video_id = videoId,
            text = resolvedText
        });
    }

    [McpServerTool(Name = "fb_get_managed_pages")]
    [Description("Retrieve all managed Facebook Fanpages and additional profiles associated with the account via Comet GraphQL.")]
    public async Task<string> GetManagedPagesAsync(
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile account, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        IReadOnlyList<ManagedPageSummary> pages = await _manager.CometClient
            .GetManagedPagesAsync(session, ct)
            .ConfigureAwait(false);

        return McpJson.Serialize(new
        {
            status = "success",
            account_uid = account.Uid,
            count = pages.Count,
            managed_pages = pages.Select(p => new
            {
                page_id = p.PageId,
                name = p.Name,
                delegate_page_id = p.DelegatePageId
            })
        });
    }
}
