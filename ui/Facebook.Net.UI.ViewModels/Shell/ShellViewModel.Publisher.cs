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
/// Spintax live preview, Content Template Library, and Post Bumping (Up-Top).
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

    /// <summary>Gets or sets the optional countdown delay in minutes before starting a publishing campaign (0 = start immediately).</summary>
    [ObservableProperty]
    public partial int ScheduleDelayMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether to automatically react (Love/Like) to newly published posts.</summary>
    [ObservableProperty]
    public partial bool AutoReactPublishedPost { get; set; } = true;

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
            if (this.ScheduleDelayMinutes > 0)
            {
                int waitMins = Math.Clamp(this.ScheduleDelayMinutes, 1, 1440);
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    this._context.Localization["Audit.Cat.Post"],
                    this.Msg(
                        $"Đã hẹn giờ chiến dịch đăng bài — sẽ tự động bắt đầu sau {waitMins} phút nữa...",
                        $"Scheduled publishing campaign — starting automatically in {waitMins} minute(s)...")).ConfigureAwait(true);
                await Task.Delay(TimeSpan.FromMinutes(waitMins), ct).ConfigureAwait(true);
            }

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
                    if (this.AutoReactPublishedPost)
                    {
                        _ = await this._context.CometClient
                            .ReactToPostAsync(session, postId, ReactionKind.Love, ct)
                            .ConfigureAwait(true);
                    }
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
                    if (this.AutoReactPublishedPost)
                    {
                        _ = await this._context.CometClient
                            .ReactToPostAsync(session, postId, ReactionKind.Love, ct)
                            .ConfigureAwait(true);
                    }
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
            await this.HandleClassifiedCampaignExceptionAsync(AuditCategory.Post, this._context.Localization["Audit.Cat.Post"], ex, ct).ConfigureAwait(true);
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
}
