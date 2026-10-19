// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Automated Group/Timeline Publishing,
/// Group Buyer-Intent Lead Scanning, and Visual Sales Commenting (zero manual Post ID or Group ID entry).
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets a value indicating whether to publish to selected Target Groups (<c>true</c>) or Personal Timeline (<c>false</c>).</summary>
    [ObservableProperty]
    public partial bool PublishToTargetGroups { get; set; } = true;

    /// <summary>Gets or sets the Spintax post body to publish.</summary>
    [ObservableProperty]
    public partial string PostSpintaxText { get; set; } = "{Xin chào cả nhà|Chào anh chị em trong nhóm}! Bên mình {đang sẵn hàng|mới về lô hàng} chính hãng giá ưu đãi, {inbox|nhắn tin} nhận báo giá chi tiết nhé!";

    /// <summary>Gets or sets the optional local photo path for post publishing.</summary>
    [ObservableProperty]
    public partial string PostMediaFilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the buyer-intent filter keywords when scanning group feeds (e.g. "cần mua, xin giá, ib").</summary>
    [ObservableProperty]
    public partial string BuyerIntentKeywords { get; set; } = string.Empty;

    /// <summary>Gets or sets the visually selected post card from <see cref="ScrapedPosts"/> for sales commenting.</summary>
    [ObservableProperty]
    public partial PostSummary? SelectedScrapedPost { get; set; }

    /// <summary>Gets or sets the Spintax sales comment text.</summary>
    [ObservableProperty]
    public partial string SalesCommentSpintaxText { get; set; } = "{Check inbox giúp em nhé|Em đã gửi báo giá chi tiết qua tin nhắn rồi ạ|Anh/chị kiểm tra tin nhắn chờ nhé}!";

    /// <summary>Gets or sets the optional local product/price photo path for sales commenting.</summary>
    [ObservableProperty]
    public partial string SalesCommentPhotoPath { get; set; } = string.Empty;

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
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            "Kho Nội dung",
            $"Đã lưu mẫu bài viết '{title}' vào kho lưu trữ.").ConfigureAwait(true);
    }

    private async Task PublishCampaignAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxText))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Warning,
                "Đăng bài",
                "Vui lòng nhập nội dung bài đăng trước khi chạy chiến dịch.").ConfigureAwait(true);
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            List<string>? photoIds = null;
            if (!string.IsNullOrWhiteSpace(this.PostMediaFilePath) && File.Exists(this.PostMediaFilePath.Trim()))
            {
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    "Tải ảnh",
                    "Đang tải ảnh sản phẩm lên máy chủ Facebook...").ConfigureAwait(true);

                string uploadedFbid = await this._context.CometClient
                    .UploadUfiPhotoAsync(session, this.PostMediaFilePath.Trim())
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(uploadedFbid))
                {
                    photoIds = new List<string>(1) { uploadedFbid };
                }
            }

            if (!this.PublishToTargetGroups)
            {
                string? postId = await this._context.CometClient
                    .PublishPostAsync(session, this.PostSpintaxText, ComposerTarget.Feed, null, photoIds)
                    .ConfigureAwait(true);

                await this._context.AccountStore.RecordActionAsync(new ActionHistoryEntry(
                    session.AccountUid,
                    "POST",
                    session.AccountUid,
                    postId,
                    DateTimeOffset.UtcNow)).ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    "Trang cá nhân",
                    "Đã đăng bài lên Trang cá nhân thành công!",
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
                    "Nhóm Mục tiêu",
                    "Chưa có nhóm nào được chọn. Hãy đồng bộ hoặc chọn nhóm ở mục 'Nhóm & Cộng đồng' trước.").ConfigureAwait(true);
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                SavedTargetGroup group = targets[i];
                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Info,
                    group.GroupName,
                    $"Đang đăng bài vào nhóm '{group.GroupName}' ({i + 1}/{targets.Count})...").ConfigureAwait(true);

                string? postId = await this._context.CometClient
                    .PublishPostAsync(session, this.PostSpintaxText, ComposerTarget.Group, group.GroupId, photoIds)
                    .ConfigureAwait(true);

                await this._context.AccountStore.RecordActionAsync(new ActionHistoryEntry(
                    session.AccountUid,
                    "POST",
                    group.GroupId,
                    postId,
                    DateTimeOffset.UtcNow)).ConfigureAwait(true);

                _ = await this.LogAuditAsync(
                    AuditCategory.Post,
                    AuditSeverity.Success,
                    group.GroupName,
                    string.IsNullOrWhiteSpace(postId)
                        ? $"Đã gửi bài đăng vào nhóm '{group.GroupName}' (đang chờ quản trị viên duyệt)."
                        : $"Đăng bài thành công vào nhóm '{group.GroupName}'!",
                    !string.IsNullOrWhiteSpace(postId) ? $"{ProtocolConstants.Endpoints.BaseOrigin}/{postId}" : null).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Post,
                AuditSeverity.Error,
                "Đăng bài",
                $"Lỗi khi đăng bài: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task ScanGroupLeadsAsync()
    {
        string? targetGroupId = this.SelectedTargetGroup?.GroupId ?? this.SelectedGroup?.GroupId;
        string targetGroupName = this.SelectedTargetGroup?.GroupName ?? this.SelectedGroup?.Name ?? "Nhóm đã chọn";

        if (string.IsNullOrWhiteSpace(targetGroupId))
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Warning,
                "Quét Bài Nhóm",
                "Vui lòng chọn một Nhóm trong danh sách trước khi quét bài viết.").ConfigureAwait(true);
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Info,
                targetGroupName,
                $"Đang thu thập các bài viết mới nhất trong nhóm '{targetGroupName}'...").ConfigureAwait(true);

            PagedResult<PostSummary> feed = await this._context.CometClient
                .GetGroupFeedAsync(session, targetGroupId, count: 15)
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

            if (this.ScrapedPosts.Count > 0)
            {
                this.SelectedScrapedPost = this.ScrapedPosts[0];
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Success,
                targetGroupName,
                $"Đã thu thập {this.ScrapedPosts.Count} bài viết tiềm năng từ nhóm '{targetGroupName}'.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Scrape,
                AuditSeverity.Error,
                targetGroupName,
                $"Lỗi quét bài viết nhóm: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task SendSalesCommentOnSelectedPostAsync()
    {
        if (this.SelectedScrapedPost is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                "Comment Sale",
                "Vui lòng bấm chọn một bài viết trong danh sách bài thu thập được trước khi gửi Comment Sale.").ConfigureAwait(true);
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            await this.ExecuteSingleSalesCommentAsync(session, this.SelectedScrapedPost).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                this.SelectedScrapedPost.AuthorName ?? "Bài viết",
                $"Lỗi gửi Comment Sale: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task AutoCommentAllMatchedPostsAsync()
    {
        if (this.ScrapedPosts.Count == 0)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                "Auto Comment Sale",
                "Danh sách bài viết đang trống — hãy bấm 'Quét Bài viết mới' trước.").ConfigureAwait(true);
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            for (int i = 0; i < this.ScrapedPosts.Count; i++)
            {
                await this.ExecuteSingleSalesCommentAsync(session, this.ScrapedPosts[i]).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Error,
                "Auto Comment Sale",
                $"Lỗi trong quá trình Auto Comment: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task ExecuteSingleSalesCommentAsync(IBrowserSession session, PostSummary post)
    {
        string targetLabel = !string.IsNullOrWhiteSpace(post.AuthorName)
            ? $"Bài của {post.AuthorName}"
            : "Bài viết trong nhóm";

        bool alreadyCommented = await this._context.AccountStore
            .HasExecutedActionAsync(session.AccountUid, "COMMENT", post.PostId)
            .ConfigureAwait(true);

        if (alreadyCommented)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.SalesComment,
                AuditSeverity.Warning,
                targetLabel,
                $"Bỏ qua '{targetLabel}' vì tài khoản này đã từng Comment Sale vào bài đó trước đây.").ConfigureAwait(true);
            return;
        }

        string? attachmentFbid = null;
        if (!string.IsNullOrWhiteSpace(this.SalesCommentPhotoPath) && File.Exists(this.SalesCommentPhotoPath.Trim()))
        {
            attachmentFbid = await this._context.CometClient
                .UploadUfiPhotoAsync(session, this.SalesCommentPhotoPath.Trim())
                .ConfigureAwait(true);
        }

        string? commentId = await this._context.CometClient
            .CreateCommentAsync(session, post.PostId, this.SalesCommentSpintaxText, attachmentFbid)
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(commentId))
        {
            await this._context.AccountStore.RecordActionAsync(new ActionHistoryEntry(
                session.AccountUid,
                "COMMENT",
                post.PostId,
                commentId,
                DateTimeOffset.UtcNow)).ConfigureAwait(true);
        }

        _ = await this.LogAuditAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Success,
            targetLabel,
            $"Đã gửi Comment Sale thành công vào '{targetLabel}'!",
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
