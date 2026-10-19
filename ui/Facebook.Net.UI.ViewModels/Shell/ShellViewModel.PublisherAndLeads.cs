// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Protocol;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Automated Group/Timeline Publishing,
/// Spintax live preview, Post Bumping (Up-Top), Group Buyer-Intent Lead Scanning, and Visual Sales Commenting.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets a value indicating whether to publish to selected Target Groups (<c>true</c>) or Personal Timeline (<c>false</c>).</summary>
    [ObservableProperty]
    public partial bool PublishToTargetGroups { get; set; } = true;

    /// <summary>Gets or sets the Spintax post body to publish.</summary>
    [ObservableProperty]
    public partial string PostSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the live randomized Spintax preview text shown on the Publisher screen.</summary>
    [ObservableProperty]
    public partial string SpintaxPreviewText { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional local photo path for post publishing.</summary>
    [ObservableProperty]
    public partial string PostMediaFilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the currently selected template in the saved content library.</summary>
    [ObservableProperty]
    public partial CampaignTemplate? SelectedTemplate { get; set; }

    /// <summary>Gets or sets the Spintax comment text used to bump (Up-Top) recently published group posts.</summary>
    [ObservableProperty]
    public partial string BumpCommentSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the buyer-intent filter keywords when scanning group feeds (e.g. "cần mua, xin giá, ib").</summary>
    [ObservableProperty]
    public partial string BuyerIntentKeywords { get; set; } = string.Empty;

    /// <summary>Gets or sets the visually selected post card from <see cref="ScrapedPosts"/> for sales commenting.</summary>
    [ObservableProperty]
    public partial PostSummary? SelectedScrapedPost { get; set; }

    /// <summary>Gets or sets the Spintax sales comment text.</summary>
    [ObservableProperty]
    public partial string SalesCommentSpintaxText { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional local product/price photo path for sales commenting.</summary>
    [ObservableProperty]
    public partial string SalesCommentPhotoPath { get; set; } = string.Empty;

    private readonly List<string> _recentPublishedPostIds = new();

    private string _lastDefaultPostSpintax = string.Empty;
    private string _lastDefaultBumpSpintax = string.Empty;
    private string _lastDefaultSalesCommentSpintax = string.Empty;

    private void SyncLocalizedDefaultPrompts()
    {
        string nextPost = this._context.Localization["Publisher.DefaultPostSpintax"];
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText) ||
            string.Equals(this.PostSpintaxText, this._lastDefaultPostSpintax, StringComparison.Ordinal))
        {
            this.PostSpintaxText = nextPost;
        }

        this._lastDefaultPostSpintax = nextPost;

        string nextBump = this._context.Localization["Publisher.DefaultBumpSpintax"];
        if (string.IsNullOrWhiteSpace(this.BumpCommentSpintaxText) ||
            string.Equals(this.BumpCommentSpintaxText, this._lastDefaultBumpSpintax, StringComparison.Ordinal))
        {
            this.BumpCommentSpintaxText = nextBump;
        }

        this._lastDefaultBumpSpintax = nextBump;

        string nextSales = this._context.Localization["Leads.DefaultSalesCommentSpintax"];
        if (string.IsNullOrWhiteSpace(this.SalesCommentSpintaxText) ||
            string.Equals(this.SalesCommentSpintaxText, this._lastDefaultSalesCommentSpintax, StringComparison.Ordinal))
        {
            this.SalesCommentSpintaxText = nextSales;
        }

        this._lastDefaultSalesCommentSpintax = nextSales;
    }

    /// <summary>
    /// Generates a randomized variation from <see cref="PostSpintaxText"/> so the user can preview `{a|b|c}` syntax before publishing.
    /// </summary>
    public void GenerateSpintaxPreview()
    {
        string spun = PayloadBuilder.SpinText(this.PostSpintaxText);
        this.SpintaxPreviewText = $"{this._context.Localization["Publisher.SpintaxPreviewPrefix"]} {spun}";
    }

    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText))
        {
            return;
        }

        string title = this.PostSpintaxText.Length > 36
            ? string.Concat(this.PostSpintaxText[..36], "...")
            : this.PostSpintaxText;

        CampaignTemplate tpl = new(
            Id: $"tpl_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            Title: title,
            SpintaxContent: this.PostSpintaxText,
            MediaFilePath: string.IsNullOrWhiteSpace(this.PostMediaFilePath) ? null : this.PostMediaFilePath.Trim(),
            IsCommentTemplate: false);

        await this._context.AccountStore.UpsertCampaignTemplateAsync(tpl).ConfigureAwait(true);
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            this._context.Localization["Publisher.TemplatesTitle"],
            this.Msg(
                $"Đã lưu mẫu bài viết '{title}' vào kho lưu trữ.",
                $"Saved post template '{title}' to the content library.")).ConfigureAwait(true);
    }

    private async Task DeleteSelectedTemplateAsync()
    {
        if (this.SelectedTemplate is null)
        {
            return;
        }

        string title = this.SelectedTemplate.Title;
        _ = await this._context.AccountStore.DeleteCampaignTemplateAsync(this.SelectedTemplate.Id).ConfigureAwait(true);
        this.SelectedTemplate = null;
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Info,
            this._context.Localization["Publisher.TemplatesTitle"],
            this.Msg(
                $"Đã xóa mẫu '{title}' khỏi kho lưu trữ.",
                $"Deleted template '{title}' from the content library.")).ConfigureAwait(true);
    }

    private async Task PublishCampaignAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Post"],
                this.Msg(
                    "Vui lòng nhập nội dung bài đăng trước khi chạy chiến dịch.",
                    "Please enter post content before starting the campaign.")).ConfigureAwait(true);
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

            List<string>? photoIds = null;
            if (!string.IsNullOrWhiteSpace(this.PostMediaFilePath) && File.Exists(this.PostMediaFilePath.Trim()))
            {
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    this._context.Localization["Audit.Cat.Post"],
                    this.Msg(
                        "Đang tải ảnh sản phẩm lên máy chủ Facebook...",
                        "Uploading product photo to Facebook...")).ConfigureAwait(true);

                string uploadedFbid = await this._context.CometClient
                    .UploadUfiPhotoAsync(session, this.PostMediaFilePath.Trim(), ct)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(uploadedFbid))
                {
                    photoIds = new List<string>(1) { uploadedFbid };
                }
            }

            if (!this.PublishToTargetGroups)
            {
                string? postId = await this._context.CometClient
                    .PublishPostAsync(session, this.PostSpintaxText, ComposerTarget.Feed, null, photoIds, ct: ct)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(postId))
                {
                    this._recentPublishedPostIds.Add(postId);
                }

                await this._context.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        session.AccountUid,
                        "POST",
                        session.AccountUid,
                        postId,
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    this._context.Localization["Publisher.TargetModePersonal"],
                    this.Msg(
                        "Đã đăng bài lên Trang cá nhân thành công!",
                        "Published post to Personal Timeline successfully!"),
                    !string.IsNullOrWhiteSpace(postId) ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}" : null).ConfigureAwait(true);

                return;
            }

            // Determine visual target groups (either checked target groups or currently selected group)
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
                targets.Add(new SavedTargetGroup(this.SelectedGroup.GroupId, this.SelectedGroup.Name));
            }

            if (targets.Count == 0)
            {
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Warning,
                    this._context.Localization["Audit.Cat.Group"],
                    this.Msg(
                        "Chưa có nhóm nào được chọn. Hãy đồng bộ hoặc chọn nhóm ở mục 'Nhóm & Cộng đồng' trước.",
                        "No target groups selected. Please sync or check groups in 'Groups & Communities' first.")).ConfigureAwait(true);
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(targets[i].GroupName, ct).ConfigureAwait(true);
                }

                SavedTargetGroup group = targets[i];
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    group.GroupName,
                    this.Msg(
                        $"Đang đăng bài vào nhóm '{group.GroupName}' ({i + 1}/{targets.Count})...",
                        $"Publishing post to group '{group.GroupName}' ({i + 1}/{targets.Count})...")).ConfigureAwait(true);

                string? postId = await this._context.CometClient
                    .PublishPostAsync(session, this.PostSpintaxText, ComposerTarget.Group, group.GroupId, photoIds, ct: ct)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(postId))
                {
                    this._recentPublishedPostIds.Add(postId);
                }

                await this._context.AccountStore.RecordActionAsync(
                    new ActionHistoryEntry(
                        session.AccountUid,
                        "POST",
                        group.GroupId,
                        postId,
                        DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    group.GroupName,
                    string.IsNullOrWhiteSpace(postId)
                        ? this.Msg(
                            $"Đã gửi bài đăng vào nhóm '{group.GroupName}' (đang chờ quản trị viên duyệt).",
                            $"Submitted post to group '{group.GroupName}' (pending admin approval).")
                        : this.Msg(
                            $"Đăng bài thành công vào nhóm '{group.GroupName}'!",
                            $"Published post to group '{group.GroupName}' successfully!"),
                    !string.IsNullOrWhiteSpace(postId) ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}" : null).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Post"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Error,
                this._context.Localization["Audit.Cat.Post"],
                this.Msg($"Lỗi khi đăng bài: {this.SanitizeErrorMessage(ex)}", $"Publish failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task BumpRecentPostsAsync()
    {
        if (this._recentPublishedPostIds.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                this._context.Localization["Publisher.BumpTitle"],
                this.Msg(
                    "Chưa có bài viết vừa đăng nào trong phiên này để đẩy Top — hãy chạy đăng bài trước.",
                    "No recently published posts in this session to bump — please publish a post first.")).ConfigureAwait(true);
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
                ? this._context.Localization["Publisher.DefaultBumpSpintax"]
                : this.BumpCommentSpintaxText;

            for (int i = 0; i < this._recentPublishedPostIds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync("Up-Top", ct).ConfigureAwait(true);
                }

                string postId = this._recentPublishedPostIds[i];
                _ = await this._context.CometClient
                    .CreateCommentAsync(session, postId, bumpText, null, ct: ct)
                    .ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.SalesComment,
                    AuditSeverity.Success,
                    "Up-Top",
                    this.Msg(
                        $"Đã bình luận đẩy bài viết ({i + 1}/{this._recentPublishedPostIds.Count}) lên đầu nhóm thành công!",
                        $"Bumped post ({i + 1}/{this._recentPublishedPostIds.Count}) to the top of the group feed!")).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                "Up-Top",
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                "Up-Top",
                this.Msg($"Lỗi khi đẩy Top bài viết: {this.SanitizeErrorMessage(ex)}", $"Post bump failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task ScanGroupLeadsAsync()
    {
        string? targetGroupId = this.SelectedTargetGroup?.GroupId ?? this.SelectedGroup?.GroupId;
        string targetGroupName = this.SelectedTargetGroup?.GroupName ?? this.SelectedGroup?.Name ?? this._context.Localization["Audit.Cat.Group"];

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Scrape"],
                this.Msg(
                    "Vui lòng chọn một Nhóm trong danh sách trước khi quét bài viết.",
                    "Please select a Group from your catalog before scanning posts.")).ConfigureAwait(true);
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

            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Info,
                targetGroupName,
                this.Msg(
                    $"Đang thu thập các bài viết mới nhất trong nhóm '{targetGroupName}'...",
                    $"Scanning latest posts in group '{targetGroupName}'...")).ConfigureAwait(true);

            PagedResult<PostSummary> feed = await this._context.CometClient
                .GetGroupFeedAsync(session, targetGroupId, count: 15, ct: ct)
                .ConfigureAwait(true);

            string[] keywords = string.IsNullOrWhiteSpace(this.BuyerIntentKeywords)
                ? Array.Empty<string>()
                : this.BuyerIntentKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            this.ScrapedPosts.Clear();
            for (int i = 0; i < feed.Items.Count; i++)
            {
                PostSummary post = feed.Items[i];
                if (MatchesBuyerKeywords(post.MessageText, keywords))
                {
                    this.ScrapedPosts.Add(post);
                }
            }

            this.SelectedScrapedPost = this.ScrapedPosts.Count > 0 ? this.ScrapedPosts[0] : null;

            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Success,
                targetGroupName,
                this.Msg(
                    $"Đã thu thập {this.ScrapedPosts.Count} bài viết tiềm năng từ nhóm '{targetGroupName}'.",
                    $"Discovered {this.ScrapedPosts.Count} potential buyer posts in '{targetGroupName}'.")).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Warning,
                targetGroupName,
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Error,
                targetGroupName,
                this.Msg($"Lỗi quét bài viết nhóm: {this.SanitizeErrorMessage(ex)}", $"Group post scan failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SendSalesCommentOnSelectedPostAsync()
    {
        if (this.SelectedScrapedPost is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.SalesComment"],
                this.Msg(
                    "Vui lòng bấm chọn một bài viết trong danh sách bài thu thập được trước khi gửi Comment Sale.",
                    "Please click a post card from the discovered list before sending a Sales Comment.")).ConfigureAwait(true);
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

            await this.ExecuteSingleSalesCommentAsync(session, this.SelectedScrapedPost, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.SalesComment"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                this.SelectedScrapedPost.AuthorName ?? this._context.Localization["Leads.DefaultAuthor"],
                this.Msg($"Lỗi gửi Comment Sale: {this.SanitizeErrorMessage(ex)}", $"Sales comment failed: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task AutoCommentAllMatchedPostsAsync()
    {
        if (this.ScrapedPosts.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.SalesComment"],
                this.Msg(
                    "Danh sách bài viết đang trống — hãy bấm 'Quét Bài viết mới' trước.",
                    "Discovered post list is empty — please click 'Scan Recent Posts' first.")).ConfigureAwait(true);
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

            for (int i = 0; i < this.ScrapedPosts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(this.ScrapedPosts[i].AuthorName ?? this._context.Localization["Leads.DefaultAuthor"], ct).ConfigureAwait(true);
                }

                await this.ExecuteSingleSalesCommentAsync(session, this.ScrapedPosts[i], ct).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.SalesComment"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                this._context.Localization["Audit.Cat.SalesComment"],
                this.Msg($"Lỗi trong quá trình Auto Comment: {this.SanitizeErrorMessage(ex)}", $"Auto-comment error: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task OpenSelectedPostInBrowserAsync()
    {
        if (this.SelectedScrapedPost is null || string.IsNullOrWhiteSpace(this.SelectedScrapedPost.PermalinkUrl))
        {
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        await session.NavigateAsync(this.SelectedScrapedPost.PermalinkUrl).ConfigureAwait(true);
        _ = await this.LogAuditAsync(
            AuditCategory.Scrape,
            AuditSeverity.Info,
            this.SelectedScrapedPost.AuthorName ?? this._context.Localization["Leads.DefaultAuthor"],
            this.Msg(
                "Đã mở bài viết trực tiếp trên cửa sổ trình duyệt.",
                "Opened post directly in the browser window.")).ConfigureAwait(true);
    }

    private async Task WaitAntiCheckpointDelayAsync(string nextTargetName, CancellationToken ct)
    {
        int minSec = Math.Max(0, this.MinDelaySeconds);
        int maxSec = Math.Max(minSec, this.MaxDelaySeconds);
        if (maxSec <= 0)
        {
            return;
        }

        int waitSec = Random.Shared.Next(minSec, maxSec + 1);
        if (waitSec <= 0)
        {
            return;
        }

        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Info,
            nextTargetName,
            this.Msg(
                $"Đang nghỉ giãn cách an toàn {waitSec} giây trước khi xử lý '{nextTargetName}' (chống Checkpoint)...",
                $"Waiting safe anti-checkpoint delay of {waitSec}s before processing '{nextTargetName}'...")).ConfigureAwait(true);

        await Task.Delay(TimeSpan.FromSeconds(waitSec), ct).ConfigureAwait(true);
    }

    private async Task ExecuteSingleSalesCommentAsync(IBrowserSession session, PostSummary post, CancellationToken ct)
    {
        string targetLabel = !string.IsNullOrWhiteSpace(post.AuthorName)
            ? this.Msg($"Bài của {post.AuthorName}", $"Post by {post.AuthorName}")
            : this._context.Localization["Leads.DefaultAuthor"];

        bool alreadyCommented = await this._context.AccountStore
            .HasExecutedActionAsync(session.AccountUid, "COMMENT", post.PostId, ct)
            .ConfigureAwait(true);

        if (alreadyCommented)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                targetLabel,
                this.Msg(
                    $"Bỏ qua '{targetLabel}' vì tài khoản này đã từng Comment Sale vào bài đó trước đây.",
                    $"Skipped '{targetLabel}' because this account already commented on it earlier.")).ConfigureAwait(true);
            return;
        }

        string? attachmentFbid = null;
        if (!string.IsNullOrWhiteSpace(this.SalesCommentPhotoPath) && File.Exists(this.SalesCommentPhotoPath.Trim()))
        {
            attachmentFbid = await this._context.CometClient
                .UploadUfiPhotoAsync(session, this.SalesCommentPhotoPath.Trim(), ct)
                .ConfigureAwait(true);
        }

        string? commentId = await this._context.CometClient
            .CreateCommentAsync(session, post.PostId, this.SalesCommentSpintaxText, attachmentFbid, ct: ct)
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(commentId))
        {
            await this._context.AccountStore.RecordActionAsync(
                new ActionHistoryEntry(
                    session.AccountUid,
                    "COMMENT",
                    post.PostId,
                    commentId,
                    DateTimeOffset.UtcNow),
                ct).ConfigureAwait(true);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            targetLabel,
            this.Msg(
                $"Đã gửi Comment Sale thành công vào '{targetLabel}'!",
                $"Posted Sales Comment on '{targetLabel}' successfully!"),
            post.PermalinkUrl).ConfigureAwait(true);
    }

    private static bool MatchesBuyerKeywords(string? messageText, string[] keywords)
    {
        if (keywords.Length == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        for (int i = 0; i < keywords.Length; i++)
        {
            if (messageText.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
