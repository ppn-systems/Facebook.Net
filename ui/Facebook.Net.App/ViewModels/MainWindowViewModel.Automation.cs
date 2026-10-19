// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.App.ViewModels;

/// <summary>
/// Partial implementation of <see cref="MainWindowViewModel"/> handling Group Scraping, Group/Timeline Post Publishing, and Sales Commenting.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private async Task LoadJoinedGroupsAsync()
    {
        IBrowserSession? session = await this.TryGetActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            this.AppendLog("Đang tải danh sách nhóm Facebook đã tham gia...");
            PagedResult<GroupSummary> groups = await this._services.CometClient
                .GetJoinedGroupsAsync(session)
                .ConfigureAwait(true);

            this.JoinedGroups.Clear();
            for (int i = 0; i < groups.Items.Count; i++)
            {
                this.JoinedGroups.Add(groups.Items[i]);
            }

            if (this.JoinedGroups.Count > 0 && string.IsNullOrWhiteSpace(this.TargetGroupId))
            {
                this.TargetGroupId = this.JoinedGroups[0].GroupId;
            }

            this.AppendLog($"Đã thu thập {this.JoinedGroups.Count} nhóm đã tham gia.");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi tải danh sách nhóm: {ex.Message}");
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task ScrapeGroupFeedAsync()
    {
        if (string.IsNullOrWhiteSpace(this.TargetGroupId))
        {
            this.AppendLog("Vui lòng nhập Group ID cần thu thập bài viết.");
            return;
        }

        IBrowserSession? session = await this.TryGetActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            string groupId = this.TargetGroupId.Trim();
            this.AppendLog($"Đang thu thập bài viết mới trong nhóm {groupId}...");

            PagedResult<PostSummary> result = await this._services.CometClient
                .GetGroupFeedAsync(session, groupId, count: 10)
                .ConfigureAwait(true);

            this.ScrapedPosts.Clear();
            for (int i = 0; i < result.Items.Count; i++)
            {
                this.ScrapedPosts.Add(result.Items[i]);
            }

            if (this.ScrapedPosts.Count > 0 && string.IsNullOrWhiteSpace(this.CommentTargetPostId))
            {
                this.CommentTargetPostId = this.ScrapedPosts[0].PostId;
            }

            this.AppendLog($"Đã thu thập {this.ScrapedPosts.Count} bài viết từ nhóm {groupId}.");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi thu thập bài trong nhóm: {ex.Message}");
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task PublishPostAsync()
    {
        if (string.IsNullOrWhiteSpace(this.PostSpintaxContent))
        {
            this.AppendLog("Vui lòng nhập nội dung bài đăng (hỗ trợ Spintax {a|b}).");
            return;
        }

        if (this.PublishToGroup && string.IsNullOrWhiteSpace(this.TargetGroupId))
        {
            this.AppendLog("Vui lòng nhập Group ID khi chọn chế độ đăng bài vào Nhóm.");
            return;
        }

        IBrowserSession? session = await this.TryGetActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            List<string>? photoIds = null;
            if (!string.IsNullOrWhiteSpace(this.PostPhotoPath) && File.Exists(this.PostPhotoPath.Trim()))
            {
                this.AppendLog("Đang tải ảnh đính kèm lên Facebook...");
                string uploadedFbid = await this._services.CometClient
                    .UploadUfiPhotoAsync(session, this.PostPhotoPath.Trim())
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(uploadedFbid))
                {
                    photoIds = new List<string>(1) { uploadedFbid };
                    this.AppendLog($"Tải ảnh thành công (fbid={uploadedFbid}).");
                }
            }

            ComposerTarget target = this.PublishToGroup ? ComposerTarget.Group : ComposerTarget.Feed;
            string? groupId = this.PublishToGroup ? this.TargetGroupId.Trim() : null;

            this.AppendLog(this.PublishToGroup
                ? $"Đang đăng bài vào nhóm {groupId}..."
                : "Đang đăng bài lên trang cá nhân...");

            string? postId = await this._services.CometClient
                .PublishPostAsync(session, this.PostSpintaxContent, target, groupId, photoIds)
                .ConfigureAwait(true);

            this.AppendLog(string.IsNullOrWhiteSpace(postId)
                ? "Đã gửi lệnh đăng bài (bài có thể đang chờ quản trị viên duyệt)."
                : $"Đăng bài thành công! Post ID: {postId}");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi đăng bài: {ex.Message}");
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task SendSalesCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(this.CommentTargetPostId) || string.IsNullOrWhiteSpace(this.CommentSpintaxContent))
        {
            this.AppendLog("Vui lòng nhập Post ID và nội dung Comment Sale.");
            return;
        }

        IBrowserSession? session = await this.TryGetActiveSessionAsync().ConfigureAwait(true);
        if (session is null)
        {
            return;
        }

        this.IsBusy = true;
        try
        {
            string? attachmentFbid = null;
            if (!string.IsNullOrWhiteSpace(this.CommentPhotoPath) && File.Exists(this.CommentPhotoPath.Trim()))
            {
                this.AppendLog("Đang tải ảnh sản phẩm cho Comment Sale...");
                attachmentFbid = await this._services.CometClient
                    .UploadUfiPhotoAsync(session, this.CommentPhotoPath.Trim())
                    .ConfigureAwait(true);
            }

            string targetPost = this.CommentTargetPostId.Trim();
            this.AppendLog($"Đang gửi Comment Sale vào bài viết {targetPost}...");

            string? commentId = await this._services.CometClient
                .CreateCommentAsync(session, targetPost, this.CommentSpintaxContent, attachmentFbid)
                .ConfigureAwait(true);

            this.AppendLog(string.IsNullOrWhiteSpace(commentId)
                ? "Không lấy được Comment ID phản hồi."
                : $"Comment Sale thành công! Comment ID: {commentId}");
        }
        catch (Exception ex)
        {
            this.AppendLog($"Lỗi gửi Comment Sale: {ex.Message}");
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task<IBrowserSession?> TryGetActiveSessionAsync()
    {
        if (this.SelectedAccount is null)
        {
            this.AppendLog("Vui lòng chọn một tài khoản trước.");
            return null;
        }

        IBrowserSession? existing = this._services.SessionManager.TryGetSession(this.SelectedAccount.Uid);
        if (existing is not null)
        {
            return existing;
        }

        BrowserLaunchOptions options = new(
            AccountUid: this.SelectedAccount.Uid,
            UserDataDir: this.SelectedAccount.ProfileDirectory,
            UserAgent: this.SelectedAccount.UserAgent,
            Headless: false);

        return await this._services.SessionManager.AcquireSessionAsync(options).ConfigureAwait(true);
    }
}
