// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.App.Services;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.App.ViewModels;

/// <summary>
/// Primary ViewModel for the desktop application, split into partial files by feature area:
/// Account Management, Group Scraping, Group/Timeline Publishing, and Sales Commenting.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServiceBootstrapper _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="services">The application service bootstrapper.</param>
    public MainWindowViewModel(AppServiceBootstrapper services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this._services = services;

        this.Accounts = new ObservableCollection<AccountProfile>();
        this.JoinedGroups = new ObservableCollection<GroupSummary>();
        this.ScrapedPosts = new ObservableCollection<PostSummary>();
        this.ActivityLogs = new ObservableCollection<string>();

        this.AddAccountCommand = new AsyncRelayCommand(this.AddAccountAsync);
        this.LaunchBrowserCommand = new AsyncRelayCommand(this.LaunchBrowserAndSyncSessionAsync);
        this.LoadJoinedGroupsCommand = new AsyncRelayCommand(this.LoadJoinedGroupsAsync);
        this.ScrapeGroupFeedCommand = new AsyncRelayCommand(this.ScrapeGroupFeedAsync);
        this.PublishPostCommand = new AsyncRelayCommand(this.PublishPostAsync);
        this.SendSalesCommentCommand = new AsyncRelayCommand(this.SendSalesCommentAsync);
    }

    /// <summary>
    /// Gets or sets the currently selected account profile.
    /// </summary>
    [ObservableProperty]
    public partial AccountProfile? SelectedAccount { get; set; }

    /// <summary>
    /// Gets or sets the UID input for adding a new account.
    /// </summary>
    [ObservableProperty]
    public partial string NewAccountUid { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name input for adding a new account.
    /// </summary>
    [ObservableProperty]
    public partial string NewAccountDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional cookie header input for adding a new account.
    /// </summary>
    [ObservableProperty]
    public partial string NewAccountCookieHeader { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target Facebook Group ID for scraping and group post publishing.
    /// </summary>
    [ObservableProperty]
    public partial string TargetGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Spintax post body to publish.
    /// </summary>
    [ObservableProperty]
    public partial string PostSpintaxContent { get; set; } = "{Xin chào cả nhà|Chào anh chị em trong nhóm}! {Cần pass nhanh|Thanh lý giá tốt} sản phẩm chính hãng, {inbox|nhắn tin} nhận báo giá ưu đãi nhé!";

    /// <summary>
    /// Gets or sets the optional local photo file path for post publishing.
    /// </summary>
    [ObservableProperty]
    public partial string PostPhotoPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to publish to the target group (<see langword="true"/>) or personal timeline (<see langword="false"/>).
    /// </summary>
    [ObservableProperty]
    public partial bool PublishToGroup { get; set; } = true;

    /// <summary>
    /// Gets or sets the target Post ID for sales commenting.
    /// </summary>
    [ObservableProperty]
    public partial string CommentTargetPostId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Spintax sales comment text.
    /// </summary>
    [ObservableProperty]
    public partial string CommentSpintaxContent { get; set; } = "{Check inbox giúp em nhé|Em đã gửi báo giá chi tiết qua tin nhắn ạ|Anh/chị kiểm tra tin nhắn chờ nhé}!";

    /// <summary>
    /// Gets or sets the optional local photo file path for sales commenting.
    /// </summary>
    [ObservableProperty]
    public partial string CommentPhotoPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an asynchronous operation is in progress.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Gets or sets the current status banner text.
    /// </summary>
    [ObservableProperty]
    public partial string StatusBanner { get; set; } = "Sẵn sàng — Chọn tài khoản để mở trình duyệt hoặc chạy tự động hóa.";

    /// <summary>
    /// Gets the collection of managed Facebook accounts.
    /// </summary>
    public ObservableCollection<AccountProfile> Accounts { get; }

    /// <summary>
    /// Gets the collection of joined Facebook groups for the active account.
    /// </summary>
    public ObservableCollection<GroupSummary> JoinedGroups { get; }

    /// <summary>
    /// Gets the collection of scraped posts from the target group or feed.
    /// </summary>
    public ObservableCollection<PostSummary> ScrapedPosts { get; }

    /// <summary>
    /// Gets the real-time activity log lines.
    /// </summary>
    public ObservableCollection<string> ActivityLogs { get; }

    /// <summary>
    /// Gets the command to add or update an account in SQLite storage.
    /// </summary>
    public IAsyncRelayCommand AddAccountCommand { get; }

    /// <summary>
    /// Gets the command to launch the Chromium browser for the selected account and extract Comet tokens.
    /// </summary>
    public IAsyncRelayCommand LaunchBrowserCommand { get; }

    /// <summary>
    /// Gets the command to fetch all joined groups for the selected account.
    /// </summary>
    public IAsyncRelayCommand LoadJoinedGroupsCommand { get; }

    /// <summary>
    /// Gets the command to scrape recent posts from the target group.
    /// </summary>
    public IAsyncRelayCommand ScrapeGroupFeedCommand { get; }

    /// <summary>
    /// Gets the command to publish a post (to a group or personal timeline) with Spintax and optional photo.
    /// </summary>
    public IAsyncRelayCommand PublishPostCommand { get; }

    /// <summary>
    /// Gets the command to post a sales/seeding comment on a target post with Spintax and optional photo.
    /// </summary>
    public IAsyncRelayCommand SendSalesCommentCommand { get; }

    /// <summary>
    /// Appends a timestamped entry to the activity log and updates the status banner.
    /// </summary>
    /// <param name="message">The log message.</param>
    public void AppendLog(string message)
    {
        string entry = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        this.StatusBanner = message;

        if (Dispatcher.UIThread.CheckAccess())
        {
            this.ActivityLogs.Insert(0, entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => this.ActivityLogs.Insert(0, entry));
        }
    }
}
