// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Publisher;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Automated Group/Timeline Publishing,
/// Spintax live preview, Content Template Library, and Post Bumping (Up-Top).
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets a value indicating whether to publish to selected Target Groups (<c>true</c>) or Personal Timeline (<c>false</c>).</summary>
    [ObservableProperty]
    public partial bool PublishToTargetGroups { get; set; } = true;

    /// <summary>
    /// Gets or sets the publish target mode:
    /// <c>0</c> = Personal Timeline only,
    /// <c>1</c> = Selected Target Groups only,
    /// <c>2</c> = Both Personal Timeline and Selected Target Groups.
    /// </summary>
    [ObservableProperty]
    public partial int PublishTargetMode { get; set; } = 1;

    partial void OnPublishToTargetGroupsChanged(bool value)
    {
        if (!value && this.PublishTargetMode != 0)
        {
            this.PublishTargetMode = 0;
        }
        else if (value && this.PublishTargetMode == 0)
        {
            this.PublishTargetMode = 1;
        }
    }

    partial void OnPublishTargetModeChanged(int value)
    {
        bool shouldTargetGroups = value != 0;
        if (this.PublishToTargetGroups != shouldTargetGroups)
        {
            this.PublishToTargetGroups = shouldTargetGroups;
        }
    }

    /// <summary>Gets or sets the Spintax post body to publish.</summary>
    [ObservableProperty]
    public partial string PostSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the live randomized Spintax preview text shown on the Publisher screen.</summary>
    [ObservableProperty]
    public partial string SpintaxPreviewText { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional local photo path (or semicolon-separated multi-photo paths) for post publishing.</summary>
    [ObservableProperty]
    public partial string PostMediaFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Splits a single or semicolon/pipe-delimited multi-photo path string into trimmed, deduplicated file path entries.
    /// </summary>
    public static List<string> SplitMediaFilePaths(string? rawPaths) =>
        CampaignPublisherDomainService.Instance.SplitMediaFilePaths(rawPaths);

    /// <summary>
    /// Parses a single or semicolon-delimited multi-photo path string into existing local file paths.
    /// </summary>
    public static List<string> ParseExistingMediaFilePaths(string? rawPaths) =>
        CampaignPublisherDomainService.Instance.ParseExistingMediaFilePaths(rawPaths);

    /// <summary>Gets or sets the currently selected template in the saved content library.</summary>
    [ObservableProperty]
    public partial CampaignTemplate? SelectedTemplate { get; set; }

    /// <summary>Gets or sets the currently selected published/synced post in the Post Manager tab.</summary>
    [ObservableProperty]
    public partial PublishedPostRecord? SelectedPublishedPost { get; set; }

    /// <summary>Gets or sets the customizable caption/Spintax text used when reposting <see cref="SelectedPublishedPost"/> to groups.</summary>
    [ObservableProperty]
    public partial string RepostCustomSpintaxText { get; set; } = string.Empty;

    partial void OnSelectedPublishedPostChanged(PublishedPostRecord? value)
    {
        if (value is not null)
        {
            this.RepostCustomSpintaxText = value.MessageText;
        }
    }

    /// <summary>Gets or sets the Spintax comment text used to bump (Up-Top) recently published group posts.</summary>
    [ObservableProperty]
    public partial string BumpCommentSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional countdown delay in minutes before starting a publishing campaign (0 = start immediately).</summary>
    [ObservableProperty]
    public partial int ScheduleDelayMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether to automatically react (Love/Like) to newly published posts.</summary>
    [ObservableProperty]
    public partial bool AutoReactPublishedPost { get; set; } = true;

    /// <summary>Gets or sets the live campaign progress text displayed during multi-group publishing or reposting.</summary>
    [ObservableProperty]
    public partial string CampaignProgressText { get; set; } = string.Empty;

    private readonly List<string> _recentPublishedPostIds = new();

    private string _lastDefaultPostSpintax = string.Empty;
    private string _lastDefaultBumpSpintax = string.Empty;
    private string _lastDefaultSalesCommentSpintax = string.Empty;
    private string _lastDefaultUpdateStatusIdle = string.Empty;

    private void SyncLocalizedDefaultPrompts()
    {
        string nextIdleUpdate = _context.Localization["Settings.UpdateStatusIdle"];
        if (string.IsNullOrWhiteSpace(this.UpdateStatusMessage) ||
            string.Equals(this.UpdateStatusMessage, _lastDefaultUpdateStatusIdle, StringComparison.Ordinal))
        {
            this.UpdateStatusMessage = nextIdleUpdate;
        }

        _lastDefaultUpdateStatusIdle = nextIdleUpdate;

        string nextPost = _context.Localization["Publisher.DefaultPostSpintax"];
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText) ||
            string.Equals(this.PostSpintaxText, _lastDefaultPostSpintax, StringComparison.Ordinal))
        {
            this.PostSpintaxText = nextPost;
        }

        _lastDefaultPostSpintax = nextPost;

        string nextBump = _context.Localization["Publisher.DefaultBumpSpintax"];
        if (string.IsNullOrWhiteSpace(this.BumpCommentSpintaxText) ||
            string.Equals(this.BumpCommentSpintaxText, _lastDefaultBumpSpintax, StringComparison.Ordinal))
        {
            this.BumpCommentSpintaxText = nextBump;
        }

        _lastDefaultBumpSpintax = nextBump;

        string nextSales = _context.Localization["Leads.DefaultSalesCommentSpintax"];
        if (string.IsNullOrWhiteSpace(this.SalesCommentSpintaxText) ||
            string.Equals(this.SalesCommentSpintaxText, _lastDefaultSalesCommentSpintax, StringComparison.Ordinal))
        {
            this.SalesCommentSpintaxText = nextSales;
        }

        _lastDefaultSalesCommentSpintax = nextSales;
        this.GenerateSpintaxPreview();
    }

    /// <summary>
    /// Generates a randomized variation from <see cref="PostSpintaxText"/> so the user can preview `{a|b|c}` syntax before publishing.
    /// </summary>
    public void GenerateSpintaxPreview()
    {
        string spun = _context.CampaignPublisher.SpinText(this.PostSpintaxText);
        this.SpintaxPreviewText = $"{_context.Localization["Publisher.SpintaxPreviewPrefix"]} {spun}";
    }

    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText))
        {
            return;
        }

        CampaignTemplate tpl = _context.CampaignPublisher.CreateTemplate(
            title: null,
            spintaxContent: this.PostSpintaxText,
            mediaFilePath: this.PostMediaFilePath,
            isCommentTemplate: false);

        await _context.AccountStore.UpsertCampaignTemplateAsync(tpl).ConfigureAwait(true);
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
            "brand_voice",
            $"tpl_{tpl.Id}",
            _context.Localization.Format("Ai.LearnSavedTemplate", tpl.Title, TruncateForAiLearning(tpl.SpintaxContent, 220)));

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Publisher.TemplatesTitle"],
            _context.Localization.Format("Log.TemplateSaved", tpl.Title)).ConfigureAwait(true);
    }

    private async Task SaveBumpTemplateAsync()
    {
        string raw = this.BumpCommentSpintaxText.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        CampaignTemplate tpl = _context.CampaignPublisher.CreateTemplate(
            title: null,
            spintaxContent: raw,
            mediaFilePath: null,
            isCommentTemplate: true);

        await _context.AccountStore.UpsertCampaignTemplateAsync(tpl).ConfigureAwait(true);
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
            "brand_voice",
            $"tpl_{tpl.Id}",
            _context.Localization.Format("Ai.LearnSavedTemplate", tpl.Title, TruncateForAiLearning(tpl.SpintaxContent, 220)));

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Publisher.TemplatesTitle"],
            _context.Localization.Format("Log.TemplateSaved", tpl.Title)).ConfigureAwait(true);
    }

    private async Task DeleteSelectedTemplateAsync()
    {
        if (this.SelectedTemplate is null)
        {
            return;
        }

        string title = this.SelectedTemplate.Title;
        _ = await _context.AccountStore.DeleteCampaignTemplateAsync(this.SelectedTemplate.Id).ConfigureAwait(true);
        this.SelectedTemplate = null;
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            _context.Localization["Publisher.TemplatesTitle"],
            _context.Localization.Format("Log.TemplateDeleted", title)).ConfigureAwait(true);
    }

    /// <summary>
    /// Updates an existing <see cref="CampaignTemplate"/> title and Spintax content in-place and persists it to SQLite.
    /// </summary>
    public async Task UpdateTemplateContentAsync(CampaignTemplate template, string newTitle, string newSpintaxContent)
    {
        if (template is null || string.IsNullOrWhiteSpace(newSpintaxContent))
        {
            return;
        }

        string resolvedTitle = string.IsNullOrWhiteSpace(newTitle) ? template.Title : newTitle.Trim();
        CampaignTemplate updated = template with
        {
            Title = resolvedTitle,
            SpintaxContent = newSpintaxContent.Trim()
        };

        await _context.AccountStore.UpsertCampaignTemplateAsync(updated).ConfigureAwait(true);
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            _context.Localization["Publisher.TemplatesTitle"],
            _context.Localization.Format("Log.TemplateSaved", updated.Title)).ConfigureAwait(true);
    }

    private async Task<List<string>?> UploadMediaFilesParallelAsync(
        IBrowserSession session,
        List<string> mediaFiles,
        CancellationToken ct)
    {
        if (mediaFiles.Count == 0)
        {
            return null;
        }

        _ = await this.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Info,
            _context.Localization["Audit.Cat.Post"],
            mediaFiles.Count == 1
                ? _context.Localization["Log.UploadingPhoto"]
                : _context.Localization.Format("Log.UploadingMultiplePhotos", mediaFiles.Count)).ConfigureAwait(true);

        if (mediaFiles.Count == 1)
        {
            ct.ThrowIfCancellationRequested();
            string singleFbid = await _context.CometClient
                .UploadUfiPhotoAsync(session, mediaFiles[0], ct)
                .ConfigureAwait(true);
            return !string.IsNullOrWhiteSpace(singleFbid) ? [singleFbid] : null;
        }

        string?[] uploadedByIndex = new string?[mediaFiles.Count];
        using SemaphoreSlim gate = new(3, 3);
        Task[] uploadTasks = new Task[mediaFiles.Count];
        for (int i = 0; i < mediaFiles.Count; i++)
        {
            int idx = i;
            string filePath = mediaFiles[idx];
            uploadTasks[idx] = Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    uploadedByIndex[idx] = await _context.CometClient
                        .UploadUfiPhotoAsync(session, filePath, ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _ = gate.Release();
                }
            }, ct);
        }

        await Task.WhenAll(uploadTasks).ConfigureAwait(true);
        List<string> uploadedIds = new(mediaFiles.Count);
        for (int i = 0; i < uploadedByIndex.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(uploadedByIndex[i]))
            {
                uploadedIds.Add(uploadedByIndex[i]!);
            }
        }

        return uploadedIds.Count > 0 ? uploadedIds : null;
    }

    private async Task PublishCampaignAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization["Log.EnterPostContent"]).ConfigureAwait(true);
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            if (this.ScheduleDelayMinutes > 0)
            {
                int waitMins = Math.Clamp(this.ScheduleDelayMinutes, 1, 1440);
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    _context.Localization["Audit.Cat.Post"],
                    _context.Localization.Format("Log.CampaignScheduled", waitMins)).ConfigureAwait(true);

                TimeSpan remaining = TimeSpan.FromMinutes(waitMins);
                while (remaining > TimeSpan.Zero)
                {
                    this.CampaignProgressText = _context.Localization.Format(
                        "Publisher.ScheduleCountdownFormat",
                        remaining.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture));
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(true);
                    remaining -= TimeSpan.FromSeconds(1);
                }
            }

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            List<string> mediaFiles = ParseExistingMediaFilePaths(this.PostMediaFilePath);
            List<string>? photoIds = await this.UploadMediaFilesParallelAsync(session, mediaFiles, ct).ConfigureAwait(true);

            bool publishToFeed = !this.PublishToTargetGroups || this.PublishTargetMode == 0 || this.PublishTargetMode == 2;
            bool publishToGroups = this.PublishToTargetGroups && (this.PublishTargetMode == 1 || this.PublishTargetMode == 2);

            string accountName = this.SelectedAccount?.DisplayName ?? session.AccountUid;
            string? accountAvatar = this.SelectedAccount?.AvatarUrl;
            string? trimmedMediaPath = mediaFiles.Count > 0
                ? string.Join(";", mediaFiles)
                : (string.IsNullOrWhiteSpace(this.PostMediaFilePath) ? null : this.PostMediaFilePath.Trim());

            if (publishToFeed)
            {
                string feedSpintax = _context.CampaignPublisher.ResolveDynamicVariables(this.PostSpintaxText, accountName, accountName);
                string? postId = await _context.CometClient
                    .PublishPostAsync(session, feedSpintax, ComposerTarget.Feed, null, photoIds, ct: ct)
                    .ConfigureAwait(true);

                string recordedPostId = !string.IsNullOrWhiteSpace(postId)
                    ? postId
                    : $"feed_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                if (!string.IsNullOrWhiteSpace(postId))
                {
                    _recentPublishedPostIds.Add(postId);
                    if (this.AutoReactPublishedPost)
                    {
                        _ = await _context.CometClient
                            .ReactToPostAsync(session, postId, ReactionKind.Love, ct)
                            .ConfigureAwait(true);
                    }
                }

                await _context.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        session.AccountUid,
                        DomainActionKinds.Post,
                        session.AccountUid,
                        postId,
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(true);

                PublishedPostRecord feedRecord = new(
                    PostId: recordedPostId,
                    AccountUid: session.AccountUid,
                    AuthorName: accountName,
                    AuthorAvatarUrl: accountAvatar,
                    MessageText: _context.CampaignPublisher.SpinText(feedSpintax),
                    MediaPreviewUrl: null,
                    LocalMediaFilePath: trimmedMediaPath,
                    TargetGroupId: null,
                    TargetGroupName: _context.Localization["Publisher.TargetModePersonal"],
                    TargetGroupAvatarUrl: accountAvatar,
                    PermalinkUrl: !string.IsNullOrWhiteSpace(postId) ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}" : null,
                    CreatedAt: DateTimeOffset.UtcNow,
                    IsPersonalProfilePost: true);

                await _context.AccountStore.UpsertPublishedPostAsync(feedRecord, ct).ConfigureAwait(true);
                await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    _context.Localization["Publisher.TargetModePersonal"],
                    _context.Localization["Log.PublishedPersonalTimeline"],
                    feedRecord.PermalinkUrl).ConfigureAwait(true);

                if (!publishToGroups)
                {
                    return;
                }
            }

            List<SavedTargetGroup> targets = new();
            for (int i = 0; i < this.TargetGroups.Count; i++)
            {
                if (this.TargetGroups[i].IsEnabled)
                {
                    targets.Add(this.TargetGroups[i]);
                }
            }

            if (targets.Count == 0 && this.SelectedGroup is not null)
            {
                targets.Add(new SavedTargetGroup(this.SelectedGroup.GroupId, this.SelectedGroup.Name, IsEnabled: true, Notes: null, AvatarUrl: this.SelectedGroup.AvatarUrl));
            }

            if (targets.Count == 0)
            {
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Warning,
                    _context.Localization["Audit.Cat.Group"],
                    _context.Localization["Log.NoTargetGroupsForPost"]).ConfigureAwait(true);
                return;
            }

            int successCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0 || publishToFeed)
                {
                    await this.WaitAntiCheckpointDelayAsync(targets[i].GroupName, ct).ConfigureAwait(true);
                }

                SavedTargetGroup group = targets[i];
                this.CampaignProgressText = _context.Localization.Format("Publisher.CampaignProgressFormat", i + 1, targets.Count, group.GroupName);
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    group.GroupName,
                    _context.Localization.Format("Log.PublishingToGroup", group.GroupName, i + 1, targets.Count)).ConfigureAwait(true);

                string groupSpintax = _context.CampaignPublisher.ResolveDynamicVariables(this.PostSpintaxText, accountName, group.GroupName);
                string? postId = await _context.CometClient
                    .PublishPostAsync(session, groupSpintax, ComposerTarget.Group, group.GroupId, photoIds, ct: ct)
                    .ConfigureAwait(true);

                string recordedPostId = !string.IsNullOrWhiteSpace(postId)
                    ? postId
                    : $"grp_{group.GroupId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                if (!string.IsNullOrWhiteSpace(postId))
                {
                    successCount++;
                    _recentPublishedPostIds.Add(postId);
                    if (this.AutoReactPublishedPost)
                    {
                        _ = await _context.CometClient
                            .ReactToPostAsync(session, postId, ReactionKind.Love, ct)
                            .ConfigureAwait(true);
                    }
                }

                await _context.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        session.AccountUid,
                        DomainActionKinds.Post,
                        group.GroupId,
                        postId,
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(true);

                PublishedPostRecord groupRecord = new(
                    PostId: recordedPostId,
                    AccountUid: session.AccountUid,
                    AuthorName: accountName,
                    AuthorAvatarUrl: accountAvatar,
                    MessageText: _context.CampaignPublisher.SpinText(groupSpintax),
                    MediaPreviewUrl: null,
                    LocalMediaFilePath: trimmedMediaPath,
                    TargetGroupId: group.GroupId,
                    TargetGroupName: group.GroupName,
                    TargetGroupAvatarUrl: group.AvatarUrl,
                    PermalinkUrl: !string.IsNullOrWhiteSpace(postId)
                        ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}"
                        : $"{ProtocolConstants.Endpoints.BaseOrigin}/groups/{group.GroupId}",
                    CreatedAt: DateTimeOffset.UtcNow,
                    IsPersonalProfilePost: false);

                await _context.AccountStore.UpsertPublishedPostAsync(groupRecord, ct).ConfigureAwait(true);
                this.PublishedPosts.Insert(0, groupRecord);
                this.SelectedPublishedPost ??= groupRecord;

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    group.GroupName,
                    string.IsNullOrWhiteSpace(postId)
                        ? _context.Localization.Format("Log.PostSubmittedPending", group.GroupName)
                        : _context.Localization.Format("Log.PostPublishedSuccess", group.GroupName),
                    groupRecord.PermalinkUrl).ConfigureAwait(true);
            }

            await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);

            _ = this.AiAssistant?.LearnFromUserActionSilentlyAsync(
                "campaign_rules",
                "recent_publish_campaign",
                _context.Localization.Format("Ai.LearnPublishCampaign", successCount, targets.Count, this.MinDelaySeconds, this.MaxDelaySeconds),
                ct);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await this.HandleClassifiedCampaignExceptionAsync(AuditCategory.Post, _context.Localization["Audit.Cat.Post"], ex, ct).ConfigureAwait(true);
        }
        finally
        {
            this.CampaignProgressText = string.Empty;
            this.EndTaskScope();
        }
    }

    private async Task ReloadPublishedPostsOnlyAsync()
    {
        IReadOnlyList<PublishedPostRecord> posts = await _context.AccountStore
            .GetPublishedPostsAsync(accountUid: null, limit: 250)
            .ConfigureAwait(true);

        this.PublishedPosts.ReplaceAll(posts);
        if (this.SelectedPublishedPost is null && this.PublishedPosts.Count > 0)
        {
            this.SelectedPublishedPost = this.PublishedPosts[0];
        }
    }

    /// <summary>
    /// Syncs recent posts from the active account's personal Facebook timeline into <see cref="PublishedPosts"/>
    /// so the user can view and repost them into target groups with 1 click.
    /// </summary>
    public async Task SyncPersonalTimelinePostsAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            string accountName = this.SelectedAccount?.DisplayName ?? session.AccountUid;
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Info,
                accountName,
                _context.Localization.Format("Log.SyncingTimelinePosts", accountName)).ConfigureAwait(true);

            PagedResult<PostSummary> timeline = await _context.CometClient
                .GetTimelinePostsAsync(session, count: 25, ct: ct)
                .ConfigureAwait(true);

            if (this.SelectedAccount is not null && session.CurrentMaterial is not null)
            {
                SessionMaterial mat = session.CurrentMaterial;
                bool isGenericName =
                    this.SelectedAccount.DisplayName.StartsWith("Tài khoản", StringComparison.OrdinalIgnoreCase) ||
                    this.SelectedAccount.DisplayName.StartsWith("Account", StringComparison.OrdinalIgnoreCase) ||
                    this.SelectedAccount.DisplayName.StartsWith("Facebook", StringComparison.OrdinalIgnoreCase);

                string nextName = !string.IsNullOrWhiteSpace(mat.DisplayName) && isGenericName
                    ? mat.DisplayName
                    : this.SelectedAccount.DisplayName;
                string? nextAvatar = !string.IsNullOrWhiteSpace(mat.AvatarUrl)
                    ? mat.AvatarUrl
                    : this.SelectedAccount.AvatarUrl;

                if (!string.Equals(nextName, this.SelectedAccount.DisplayName, StringComparison.Ordinal) ||
                    !string.Equals(nextAvatar, this.SelectedAccount.AvatarUrl, StringComparison.Ordinal))
                {
                    AccountProfile updatedAcc = this.SelectedAccount with
                    {
                        DisplayName = nextName,
                        AvatarUrl = nextAvatar
                    };
                    await _context.AccountStore.UpsertAccountAsync(updatedAcc, ct).ConfigureAwait(true);
                    this.SelectedAccount = updatedAcc;
                    accountName = updatedAcc.DisplayName;
                }
            }

            List<PublishedPostRecord> syncedRecords = new(timeline.Items.Count);
            for (int i = 0; i < timeline.Items.Count; i++)
            {
                PostSummary p = timeline.Items[i];
                if (string.IsNullOrWhiteSpace(p.PostId))
                {
                    continue;
                }

                string text = !string.IsNullOrWhiteSpace(p.MessageText)
                    ? p.MessageText
                    : _context.Localization["Publisher.SyncedTimelinePostFallback"];

                PublishedPostRecord rec = new(
                    PostId: p.PostId,
                    AccountUid: session.AccountUid,
                    AuthorName: !string.IsNullOrWhiteSpace(p.AuthorName) ? p.AuthorName : accountName,
                    AuthorAvatarUrl: p.AuthorAvatarUrl ?? this.SelectedAccount?.AvatarUrl,
                    MessageText: text,
                    MediaPreviewUrl: p.MediaPreviewUrl,
                    LocalMediaFilePath: null,
                    TargetGroupId: null,
                    TargetGroupName: _context.Localization["Publisher.TargetModePersonal"],
                    TargetGroupAvatarUrl: p.AuthorAvatarUrl ?? this.SelectedAccount?.AvatarUrl,
                    PermalinkUrl: p.PermalinkUrl ?? $"{ProtocolConstants.Endpoints.BaseOrigin}/{p.PostId}",
                    ReactionCount: p.ReactionCount,
                    CommentCount: p.CommentCount,
                    CreatedAt: p.CreatedAt ?? DateTimeOffset.UtcNow,
                    IsPersonalProfilePost: true);

                syncedRecords.Add(rec);
            }

            if (syncedRecords.Count > 0)
            {
                await _context.AccountStore.UpsertPublishedPostsBatchAsync(syncedRecords, ct).ConfigureAwait(true);
            }

            await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Success,
                accountName,
                _context.Localization.Format("Log.SyncedTimelinePostsCount", syncedRecords.Count, accountName)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Error,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization.Format("Log.SyncTimelinePostsFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    /// <summary>
    /// Reposts the currently selected <see cref="SelectedPublishedPost"/> into all enabled <see cref="TargetGroups"/>.
    /// </summary>
    public async Task RepostSelectedPostToGroupsAsync()
    {
        if (this.SelectedPublishedPost is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization["Log.SelectPostToRepostFirst"]).ConfigureAwait(true);
            return;
        }

        List<SavedTargetGroup> targets = new();
        for (int i = 0; i < this.TargetGroups.Count; i++)
        {
            if (this.TargetGroups[i].IsEnabled)
            {
                targets.Add(this.TargetGroups[i]);
            }
        }

        if (targets.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Group"],
                _context.Localization["Log.NoTargetGroupsForPost"]).ConfigureAwait(true);
            return;
        }

        PublishedPostRecord sourcePost = this.SelectedPublishedPost;
        string rawSpintax = !string.IsNullOrWhiteSpace(this.RepostCustomSpintaxText)
            ? this.RepostCustomSpintaxText
            : sourcePost.MessageText;

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            List<string> mediaFiles = ParseExistingMediaFilePaths(sourcePost.LocalMediaFilePath);
            List<string>? photoIds = await this.UploadMediaFilesParallelAsync(session, mediaFiles, ct).ConfigureAwait(true);

            string accountName = this.SelectedAccount?.DisplayName ?? session.AccountUid;
            string? accountAvatar = this.SelectedAccount?.AvatarUrl;
            bool isRealFbPostId = !sourcePost.PostId.StartsWith("feed_", StringComparison.Ordinal) &&
                                  !sourcePost.PostId.StartsWith("grp_", StringComparison.Ordinal);

            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(targets[i].GroupName, ct).ConfigureAwait(true);
                }

                SavedTargetGroup group = targets[i];
                this.CampaignProgressText = _context.Localization.Format("Publisher.CampaignProgressFormat", i + 1, targets.Count, group.GroupName);
                string groupText = _context.CampaignPublisher.ResolveDynamicVariables(rawSpintax, accountName, group.GroupName);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    group.GroupName,
                    _context.Localization.Format("Log.RepostingToGroup", group.GroupName, i + 1, targets.Count)).ConfigureAwait(true);

                string? newPostId = null;
                if (isRealFbPostId && photoIds is null && sourcePost.IsPersonalProfilePost)
                {
                    try
                    {
                        newPostId = await _context.CometClient
                            .SharePostAsync(session, sourcePost.PostId, groupText, group.GroupId, ct)
                            .ConfigureAwait(true);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // Fallback to native group post below
                    }
                }

                if (string.IsNullOrWhiteSpace(newPostId))
                {
                    newPostId = await _context.CometClient
                        .PublishPostAsync(session, groupText, ComposerTarget.Group, group.GroupId, photoIds, ct: ct)
                        .ConfigureAwait(true);
                }

                if (!string.IsNullOrWhiteSpace(newPostId))
                {
                    _recentPublishedPostIds.Add(newPostId);
                    if (this.AutoReactPublishedPost)
                    {
                        _ = await _context.CometClient
                            .ReactToPostAsync(session, newPostId, ReactionKind.Love, ct)
                            .ConfigureAwait(true);
                    }
                }

                string recordedId = !string.IsNullOrWhiteSpace(newPostId)
                    ? newPostId
                    : $"grp_{group.GroupId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                PublishedPostRecord repostRecord = new(
                    PostId: recordedId,
                    AccountUid: session.AccountUid,
                    AuthorName: accountName,
                    AuthorAvatarUrl: accountAvatar,
                    MessageText: _context.CampaignPublisher.SpinText(groupText),
                    MediaPreviewUrl: sourcePost.MediaPreviewUrl,
                    LocalMediaFilePath: sourcePost.LocalMediaFilePath,
                    TargetGroupId: group.GroupId,
                    TargetGroupName: group.GroupName,
                    TargetGroupAvatarUrl: group.AvatarUrl,
                    PermalinkUrl: !string.IsNullOrWhiteSpace(newPostId)
                        ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{newPostId}"
                        : $"{ProtocolConstants.Endpoints.BaseOrigin}/groups/{group.GroupId}",
                    CreatedAt: DateTimeOffset.UtcNow,
                    IsPersonalProfilePost: false);

                await _context.AccountStore.UpsertPublishedPostAsync(repostRecord, ct).ConfigureAwait(true);
                this.PublishedPosts.Insert(0, repostRecord);
                this.SelectedPublishedPost ??= repostRecord;

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    group.GroupName,
                    _context.Localization.Format("Log.PostPublishedSuccess", group.GroupName),
                    repostRecord.PermalinkUrl).ConfigureAwait(true);
            }

            await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                _context.Localization["Audit.Cat.Post"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await this.HandleClassifiedCampaignExceptionAsync(AuditCategory.Post, _context.Localization["Audit.Cat.Post"], ex, ct).ConfigureAwait(true);
        }
        finally
        {
            this.CampaignProgressText = string.Empty;
            this.EndTaskScope();
        }
    }

    private async Task DeleteSelectedPublishedPostAsync()
    {
        if (this.SelectedPublishedPost is null)
        {
            return;
        }

        PublishedPostRecord removed = this.SelectedPublishedPost;
        string preview = removed.MessageText.Length > 36
            ? string.Concat(removed.MessageText[..36], "...")
            : removed.MessageText;
        string targetLabel = removed.TargetGroupName ?? _context.Localization["Publisher.TargetModePersonal"];

        _ = await _context.AccountStore.DeletePublishedPostAsync(removed.PostId).ConfigureAwait(true);
        this.SelectedPublishedPost = null;
        await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            targetLabel,
            _context.Localization.Format("Log.Personal.PublishedPostDeleted", preview)).ConfigureAwait(true);
    }

    private async Task OpenSelectedPublishedPostInBrowserAsync()
    {
        if (this.SelectedPublishedPost is null || string.IsNullOrWhiteSpace(this.SelectedPublishedPost.PermalinkUrl))
        {
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(forceVisible: true).ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        await session.NavigateAsync(this.SelectedPublishedPost.PermalinkUrl).ConfigureAwait(true);
    }

    /// <summary>
    /// Clones a published post's text and local media path (if still present on disk) into the Publisher Composer.
    /// </summary>
    /// <param name="post">Published post record to clone, or <c>null</c> to use <see cref="ShellViewModel.SelectedPublishedPost"/>.</param>
    public void ClonePublishedPostToComposer(PublishedPostRecord? post)
    {
        PublishedPostRecord? target = post ?? this.SelectedPublishedPost;
        if (target is null)
        {
            return;
        }

        this.PostSpintaxText = target.MessageText;
        List<string> existingMedia = ParseExistingMediaFilePaths(target.LocalMediaFilePath);
        this.PostMediaFilePath = existingMedia.Count > 0 ? string.Join(";", existingMedia) : string.Empty;
        this.GenerateSpintaxPreview();
    }

    /// <summary>
    /// Immediately bumps a single published post with a comment using the active account session.
    /// </summary>
    /// <param name="post">Published post record to bump, or <c>null</c> to use <see cref="ShellViewModel.SelectedPublishedPost"/>.</param>
    public async Task BumpSinglePublishedPostAsync(PublishedPostRecord? post)
    {
        PublishedPostRecord? target = post ?? this.SelectedPublishedPost;
        if (target is null || string.IsNullOrWhiteSpace(target.PostId))
        {
            return;
        }

        CancellationToken scopeCt = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(scopeCt).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            string bumpText = !string.IsNullOrWhiteSpace(this.BumpCommentSpintaxText)
                ? this.BumpCommentSpintaxText
                : _context.Localization["Publisher.DefaultBumpSpintax"];

            string resolvedBumpText = _context.CampaignPublisher.ResolveDynamicVariables(
                bumpText,
                this.SelectedAccount?.DisplayName,
                target.TargetGroupName);

            _ = await _context.CometClient
                .CreateCommentAsync(session, target.PostId, resolvedBumpText, null, ct: scopeCt)
                .ConfigureAwait(true);

            PublishedPostRecord updated = target with
            {
                CommentCount = target.CommentCount + 1
            };
            await _context.AccountStore.UpsertPublishedPostAsync(updated, scopeCt).ConfigureAwait(true);
            await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);
            this.SelectedPublishedPost = updated;

            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Success,
                target.TargetGroupName ?? _context.Localization["Publisher.TabBump"],
                _context.Localization.Format("Log.PostBumpedSuccess", 1, 1),
                target.PermalinkUrl).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Publisher.TabBump"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                _context.Localization["Publisher.TabBump"],
                _context.Localization.Format("Log.PostBumpFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task BumpRecentPostsAsync()
    {
        List<(string PostId, string? GroupName)> bumpTargets = new();
        HashSet<string> seenPostIds = new(StringComparer.Ordinal);
        if (_recentPublishedPostIds.Count > 0)
        {
            for (int i = 0; i < _recentPublishedPostIds.Count; i++)
            {
                string postId = _recentPublishedPostIds[i];
                if (string.IsNullOrWhiteSpace(postId) || !seenPostIds.Add(postId))
                {
                    continue;
                }

                string? groupName = null;
                for (int j = 0; j < this.PublishedPosts.Count; j++)
                {
                    if (string.Equals(this.PublishedPosts[j].PostId, postId, StringComparison.Ordinal))
                    {
                        groupName = this.PublishedPosts[j].TargetGroupName;
                        break;
                    }
                }

                bumpTargets.Add((postId, groupName));
            }
        }
        else
        {
            List<PublishedPostRecord> eligible = _context.CampaignPublisher.SelectEligibleBumpPosts(
                this.PublishedPosts,
                this.SelectedAccount?.Uid,
                maxPosts: 25);
            for (int i = 0; i < eligible.Count; i++)
            {
                bumpTargets.Add((eligible[i].PostId, eligible[i].TargetGroupName));
            }
        }

        if (bumpTargets.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Publisher.BumpTitle"],
                _context.Localization["Log.NoRecentPostsToBump"]).ConfigureAwait(true);
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            string bumpText = string.IsNullOrWhiteSpace(this.BumpCommentSpintaxText)
                ? _context.Localization["Publisher.DefaultBumpSpintax"]
                : this.BumpCommentSpintaxText;

            for (int i = 0; i < bumpTargets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(_context.Localization["Publisher.TabBump"], ct).ConfigureAwait(true);
                }

                (string postId, string? groupName) = bumpTargets[i];
                string resolvedBumpText = _context.CampaignPublisher.ResolveDynamicVariables(bumpText, this.SelectedAccount?.DisplayName, groupName);
                _ = await _context.CometClient
                    .CreateCommentAsync(session, postId, resolvedBumpText, null, ct: ct)
                    .ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.SalesComment,
                    AuditSeverity.Success,
                    _context.Localization["Publisher.TabBump"],
                    _context.Localization.Format("Log.PostBumpedSuccess", i + 1, bumpTargets.Count)).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                _context.Localization["Publisher.TabBump"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                _context.Localization["Publisher.TabBump"],
                _context.Localization.Format("Log.PostBumpFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private static string TruncateForAiLearning(string? raw, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string oneLine = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length > maxChars ? string.Concat(oneLine[..maxChars], "...") : oneLine;
    }
}
