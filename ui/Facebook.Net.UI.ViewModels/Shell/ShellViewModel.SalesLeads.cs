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
/// Partial implementation of <see cref="ShellViewModel"/> handling Group Buyer-Intent Lead Scanning,
/// Lead Scoring, Visual Sales Commenting, Anti-Checkpoint Circuit Breaker, and Lead Export.
/// </summary>
public sealed partial class ShellViewModel
{
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

    /// <summary>Gets or sets a value indicating whether to automatically react (Like/Love) to a post before dropping a sales comment.</summary>
    [ObservableProperty]
    public partial bool AutoReactBeforeComment { get; set; } = true;

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

            List<PostSummary> matched = new(feed.Items.Count);
            for (int i = 0; i < feed.Items.Count; i++)
            {
                PostSummary post = feed.Items[i];
                if (MatchesBuyerKeywords(post.MessageText, keywords))
                {
                    matched.Add(post);
                }
            }

            matched.Sort((a, b) =>
                CalculateBuyerIntentScore(b.MessageText, this.BuyerIntentKeywords)
                    .CompareTo(CalculateBuyerIntentScore(a.MessageText, this.BuyerIntentKeywords)));

            this.ScrapedPosts.ReplaceAll(matched);
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
            await this.HandleClassifiedCampaignExceptionAsync(AuditCategory.SalesComment, this._context.Localization["Audit.Cat.SalesComment"], ex, ct).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task HandleClassifiedCampaignExceptionAsync(
        AuditCategory category,
        string targetName,
        Exception ex,
        CancellationToken ct)
    {
        AutomationErrorCategory errorCategory = ProtocolErrorClassifier.ClassifyException(ex);
        if (errorCategory == AutomationErrorCategory.CheckpointOrAuthFailure && this.SelectedAccount is not null)
        {
            AccountProfile updated = this.SelectedAccount with { Status = AccountStatus.Checkpoint };
            await this._context.AccountStore
                .UpsertAccountAsync(updated, CancellationToken.None)
                .ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                category,
                AuditSeverity.Error,
                targetName,
                this.Msg(
                    "Ngắt mạch an toàn (Circuit Breaker): Phát hiện phiên đăng nhập hết hạn hoặc Checkpoint. Đã dừng chiến dịch ngay lập tức để bảo vệ tài khoản!",
                    "Safety Circuit Breaker tripped: Session expired or Checkpoint detected. Campaign halted immediately to protect account!")).ConfigureAwait(true);
            return;
        }

        if (errorCategory == AutomationErrorCategory.ActionBlocked368)
        {
            _ = await this.LogAuditAsync(
                category,
                AuditSeverity.Error,
                targetName,
                this.Msg(
                    "Ngắt mạch an toàn (Circuit Breaker - Mã 368): Facebook báo giới hạn tốc độ thao tác. Đã tự động dừng chiến dịch để tránh khóa tính năng!",
                    "Safety Circuit Breaker tripped (Code 368): Facebook action rate-limit detected. Campaign paused automatically to protect account!")).ConfigureAwait(true);
            return;
        }

        _ = await this.LogAuditAsync(
            category,
            AuditSeverity.Error,
            targetName,
            this.Msg($"Lỗi tác vụ: {this.SanitizeErrorMessage(ex)}", $"Task error: {this.SanitizeErrorMessage(ex)}")).ConfigureAwait(true);
        _ = ct;
    }

    private async Task OpenSelectedPostInBrowserAsync()
    {
        if (this.SelectedScrapedPost is null || string.IsNullOrWhiteSpace(this.SelectedScrapedPost.PermalinkUrl))
        {
            return;
        }

        IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(forceVisible: true).ConfigureAwait(true);
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
        TimeSpan delay = HumanJitter.NextGaussianDelay(this.MinDelaySeconds, this.MaxDelaySeconds);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        double totalSec = Math.Round(delay.TotalSeconds, 1);
        _ = await this.LogAuditAsync(
            AuditCategory.System,
            AuditSeverity.Info,
            nextTargetName,
            this.Msg(
                $"Đang nghỉ giãn cách tự nhiên {totalSec} giây (Gaussian Jitter) trước khi xử lý '{nextTargetName}'...",
                $"Waiting humanized Gaussian delay of {totalSec}s before processing '{nextTargetName}'...")).ConfigureAwait(true);

        await Task.Delay(delay, ct).ConfigureAwait(true);
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

        if (this.AutoReactBeforeComment)
        {
            ReactionKind reaction = Random.Shared.NextDouble() < 0.75 ? ReactionKind.Like : ReactionKind.Love;
            _ = await this._context.CometClient
                .ReactToPostAsync(session, post.PostId, reaction, ct)
                .ConfigureAwait(true);
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

    /// <summary>
    /// Computes a buyer-intent score (0..100) for a scraped group post based on matched keywords and question/inquiry signals.
    /// </summary>
    public static int CalculateBuyerIntentScore(string? messageText, string? rawKeywords)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return 25;
        }

        int score = 40;
        string[] keywords = string.IsNullOrWhiteSpace(rawKeywords)
            ? Array.Empty<string>()
            : rawKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int matchedCount = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (messageText.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
            {
                matchedCount++;
            }
        }

        score += Math.Min(45, matchedCount * 20);
        if (messageText.Contains('?', StringComparison.Ordinal) ||
            messageText.Contains("cần", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("tìm", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("mua", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("tư vấn", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("looking for", StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains("price", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Formats a localized buyer-intent badge label for a scraped post card.
    /// </summary>
    public string FormatBuyerIntentBadge(PostSummary post)
    {
        ArgumentNullException.ThrowIfNull(post);
        int score = CalculateBuyerIntentScore(post.MessageText, this.BuyerIntentKeywords);
        string prefix = score >= 75
            ? this._context.Localization["Leads.IntentHigh"]
            : this._context.Localization["Leads.IntentWarm"];
        return $"{prefix} • {score}%";
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
