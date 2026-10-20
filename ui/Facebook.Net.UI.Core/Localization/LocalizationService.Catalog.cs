// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Core.Localization;

/// <summary>
/// Friendly enum formatters and full bilingual (`vi-VN` and `en-US`) translation catalogs for <see cref="LocalizationService"/>.
/// Pure natural language without parenthetical technical annotations.
/// </summary>
public sealed partial class LocalizationService
{
    /// <summary>
    /// Formats an <see cref="AccountStatus"/> into a friendly, localized user label.
    /// </summary>
    public string FormatAccountStatus(AccountStatus status) =>
        status switch
        {
            AccountStatus.Active => this["Status.Account.Active"],
            AccountStatus.Running => this["Status.Account.Running"],
            AccountStatus.CookieExpired => this["Status.Account.Expired"],
            AccountStatus.Checkpoint => this["Status.Account.Checkpoint"],
            AccountStatus.Disabled => this["Status.Account.Disabled"],
            AccountStatus.Idle => this["Status.Account.Idle"],
            _ => this["Status.Account.Idle"]
        };

    /// <summary>
    /// Formats a <see cref="GroupMembershipState"/> into a friendly, localized user label.
    /// </summary>
    public string FormatGroupMembership(GroupMembershipState state) =>
        state switch
        {
            GroupMembershipState.Member => this["Status.Group.Member"],
            GroupMembershipState.Requested => this["Status.Group.Requested"],
            GroupMembershipState.QuestionnaireCompleted => this["Status.Group.QuestionsDone"],
            GroupMembershipState.CanJoin => this["Status.Group.CanJoin"],
            GroupMembershipState.Unknown => this["Status.Group.CanJoin"],
            _ => this["Status.Group.CanJoin"]
        };

    /// <summary>
    /// Formats an <see cref="AuditCategory"/> (or <c>null</c> for "All") into a friendly, localized label.
    /// </summary>
    public string FormatAuditCategory(AuditCategory? category) =>
        category switch
        {
            null => this["Audit.FilterCategoryAll"],
            AuditCategory.System => this["Audit.Cat.System"],
            AuditCategory.Account => this["Audit.Cat.Account"],
            AuditCategory.Group => this["Audit.Cat.Group"],
            AuditCategory.Post => this["Audit.Cat.Post"],
            AuditCategory.SalesComment => this["Audit.Cat.SalesComment"],
            AuditCategory.Scrape => this["Audit.Cat.Scrape"],
            _ => this["Audit.Cat.System"]
        };

    /// <summary>
    /// Formats an <see cref="AuditTimeRange"/> into a friendly, localized label.
    /// </summary>
    public string FormatAuditTimeRange(AuditTimeRange range) =>
        range switch
        {
            AuditTimeRange.AllTime => this["Audit.Time.All"],
            AuditTimeRange.LastHour => this["Audit.Time.LastHour"],
            AuditTimeRange.Today24Hours => this["Audit.Time.Today24h"],
            AuditTimeRange.Last7Days => this["Audit.Time.Last7Days"],
            _ => this["Audit.Time.All"]
        };

    /// <summary>
    /// Formats a <see cref="PreferredBrowserKind"/> into a friendly, localized label.
    /// </summary>
    public string FormatBrowserKind(PreferredBrowserKind kind) =>
        kind switch
        {
            PreferredBrowserKind.AutoDetect => this["Settings.BrowserAuto"],
            PreferredBrowserKind.Edge => this["Settings.BrowserEdge"],
            PreferredBrowserKind.Chrome => this["Settings.BrowserChrome"],
            PreferredBrowserKind.Brave => this["Settings.BrowserBrave"],
            _ => this["Settings.BrowserAuto"]
        };

    /// <summary>
    /// Formats an <see cref="AppThemeMode"/> into a friendly, localized label.
    /// </summary>
    public string FormatThemeMode(AppThemeMode mode) =>
        mode == AppThemeMode.Light ? this["Settings.ThemeLight"] : this["Settings.ThemeDark"];

    /// <summary>
    /// Formats an <see cref="AppLanguage"/> into a friendly display label without locale code parentheses.
    /// </summary>
    public static string FormatLanguage(AppLanguage lang) =>
        lang == AppLanguage.EnUs ? "English" : "Tiếng Việt";

    private static Dictionary<string, string> CreateVietnameseCatalog() =>
        new(StringComparer.Ordinal)
        {
            ["App.Title"] = "Facebook.Net — Hệ thống Bán hàng, Đăng Nhóm & Seeding Tự động",
            ["App.Subtitle"] = "Quản lý đa tài khoản, tự động đồng bộ nhóm, đăng bài & bình luận bán hàng thông minh",
            ["TopBar.ActiveAccount"] = "Tài khoản đang chọn:",
            ["TopBar.NoAccount"] = "Chưa chọn tài khoản",
            ["Common.BrowseFile"] = "Chọn tệp...",
            ["Common.FilterButton"] = "Bộ lọc",
            ["Common.SearchOrFilter"] = "Tìm hoặc lọc",

            ["Table.ColTime"] = "Thời gian",
            ["Table.ColAccount"] = "Tài khoản",
            ["Table.ColCategory"] = "Hạng mục",
            ["Table.ColTarget"] = "Đối tượng",
            ["Table.ColMessage"] = "Nội dung chi tiết",

            ["Status.Account.Idle"] = "Chờ kết nối",
            ["Status.Account.Active"] = "Đang hoạt động",
            ["Status.Account.Running"] = "Đang chạy tác vụ",
            ["Status.Account.Expired"] = "Cần đăng nhập lại",
            ["Status.Account.Checkpoint"] = "Cần xác minh bảo mật",
            ["Status.Account.Disabled"] = "Tạm khóa",

            ["Status.Group.Member"] = "Đã tham gia",
            ["Status.Group.Requested"] = "Đang chờ duyệt",
            ["Status.Group.QuestionsDone"] = "Đã trả lời câu hỏi",
            ["Status.Group.CanJoin"] = "Chưa tham gia",

            ["Nav.Overview"] = "Trang chủ",
            ["Nav.Accounts"] = "Tài khoản",
            ["Nav.Groups"] = "Nhóm",
            ["Nav.Publisher"] = "Đăng bài",
            ["Nav.SalesLeads"] = "Săn khách",
            ["Nav.Settings"] = "Cài đặt",
            ["Nav.AuditLogs"] = "Nhật ký",

            ["Sidebar.Collapse"] = "Thu gọn menu",
            ["Sidebar.Expand"] = "Mở rộng menu",
            ["Overview.ModeLabel"] = "Tài khoản:",
            ["Overview.StatusConnected"] = "Đã sẵn sàng",
            ["Overview.StatusDisconnected"] = "Chưa kết nối",
            ["Overview.StatusRunning"] = "Đang chạy tác vụ",
            ["Overview.SubtextConnected"] = "Phiên trình duyệt đã kết nối và sẵn sàng tự động hóa.",
            ["Overview.SubtextDisconnected"] = "Chọn một tài khoản hoặc mở trình duyệt để bắt đầu kết nối.",
            ["Overview.InfoTip"] = "Bảng điều khiển trung tâm hiển thị chỉ số trực tiếp, trạng thái sẵn sàng của chiến dịch và nhật ký hoạt động thời gian thực.",
            ["Overview.SessionCardTitle"] = "Trạng thái phiên & Kiểm tra sẵn sàng",
            ["Overview.RecentLogsTitle"] = "Hoạt động gần đây",
            ["Overview.CheckAccountReady"] = "Tài khoản đã kết nối sẵn sàng",
            ["Overview.CheckAccountMissing"] = "Chưa kết nối tài khoản hoạt động",
            ["Overview.CheckGroupsReady"] = "Đã kích hoạt nhóm mục tiêu",
            ["Overview.CheckGroupsMissing"] = "Chưa bật nhóm mục tiêu nào",
            ["Overview.QuickActionsTitle"] = "Thao tác nhanh",

            ["Accounts.Header"] = "Quản lý Tài khoản Facebook",
            ["Accounts.Desc"] = "Đăng nhập trực tiếp trên trình duyệt hoặc dán nhanh từ J2Team Cookies và tự động đồng bộ nhóm.",
            ["Accounts.NamePlaceholder"] = "Tên gợi nhớ cho tài khoản",
            ["Accounts.CookiePlaceholder"] = "Dán chuỗi Cookie hoặc JSON J2Team vào đây",
            ["Accounts.BtnQuickImport"] = "Lưu / Nhập Cookie",
            ["Accounts.BtnPasteClipboard"] = "Dán từ Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Mở Trình duyệt & Đồng bộ",
            ["Accounts.BtnDelete"] = "Gỡ tài khoản",
            ["Accounts.LastActivePrefix"] = "Hoạt động gần nhất:",
            ["Accounts.ReadyToConnect"] = "Sẵn sàng mở trình duyệt",

            ["Groups.Header"] = "Danh mục Nhóm Facebook",
            ["Groups.Desc"] = "Tự động lấy toàn bộ danh sách nhóm của tài khoản để bạn tích chọn đăng bài hoặc quét khách hàng.",
            ["Groups.SearchKeywordPlaceholder"] = "Tìm nhóm hoặc lọc theo tên nhóm...",
            ["Groups.FilterPlaceholder"] = "Lọc nhanh theo tên nhóm...",
            ["Groups.BtnSyncJoined"] = "Đồng bộ Nhóm",
            ["Groups.BtnSearchNew"] = "Tìm Nhóm mới",
            ["Groups.BtnJoinSelected"] = "Tham gia Nhóm",
            ["Groups.BtnSaveTarget"] = "Thêm vào Nhóm Đích",
            ["Groups.TargetListTitle"] = "Danh sách Nhóm Đích",
            ["Groups.BtnSelectAllTargets"] = "Bật tất cả",
            ["Groups.BtnDeselectAllTargets"] = "Tắt tất cả",
            ["Groups.BtnRemoveTarget"] = "Gỡ khỏi Nhóm Đích",

            ["Publisher.Header"] = "Chiến dịch Đăng bài & Đẩy Top",
            ["Publisher.Desc"] = "Soạn nội dung xoay vòng Spintax, chọn ảnh sản phẩm và đăng tự động hoặc đẩy bài lên đầu nhóm.",
            ["Publisher.TargetModeGroup"] = "Đăng vào Nhóm mục tiêu",
            ["Publisher.TargetModePersonal"] = "Đăng Trang cá nhân",
            ["Publisher.ContentPlaceholder"] = "Nhập nội dung bài đăng hỗ trợ cú pháp xoay vòng {a|b|c}...",
            ["Publisher.PhotoPlaceholder"] = "Chưa chọn ảnh đính kèm",
            ["Publisher.BtnPickPhoto"] = "Chọn ảnh...",
            ["Publisher.BtnSaveTemplate"] = "Lưu Mẫu",
            ["Publisher.BtnDeleteTemplate"] = "Xóa mẫu đang chọn",
            ["Publisher.BtnPublishNow"] = "Đăng bài Tự động",
            ["Publisher.TemplatesTitle"] = "Kho Mẫu Đã lưu",
            ["Publisher.BumpTitle"] = "Đẩy Top bài viết trong Nhóm",
            ["Publisher.BumpPlaceholder"] = "Nhập nội dung bình luận đẩy bài...",
            ["Publisher.BtnBumpRecentPosts"] = "Đẩy Top ngay",

            ["Leads.Header"] = "Săn Khách Nhóm & Tự động Bình luận",
            ["Leads.Desc"] = "Quét bài viết mới nhất trong các nhóm mục tiêu, lọc theo từ khóa mua hàng và gửi bình luận kèm ảnh báo giá.",
            ["Leads.BuyerKeywordPlaceholder"] = "Nhập từ khóa khách mua cần tìm hoặc lọc...",
            ["Leads.BtnScanGroupPosts"] = "Quét Bài viết Nhóm",
            ["Leads.ScrapedPostsTitle"] = "Bài viết Khách hàng thu thập được",
            ["Leads.SelectedPostBanner"] = "Bài đang chọn:",
            ["Leads.NoPostSelected"] = "Chưa chọn bài viết — bấm chọn một bài ở danh sách",
            ["Leads.CommentContentPlaceholder"] = "Nhập nội dung bình luận bán hàng...",
            ["Leads.CommentPhotoPlaceholder"] = "Chưa chọn ảnh báo giá",
            ["Leads.BtnSendCommentSelected"] = "Bình luận bài chọn",
            ["Leads.BtnAutoCommentAllMatched"] = "Bình luận tất cả",
            ["Leads.BtnOpenPostInBrowser"] = "Mở xem trên Web",
            ["Leads.DefaultAuthor"] = "Thành viên trong nhóm",
            ["Leads.MediaOnlyPost"] = "Bài viết đính kèm hình ảnh / video",

            ["Settings.Header"] = "Cài đặt Hệ thống & Trình duyệt",
            ["Settings.Desc"] = "Tùy chỉnh giao diện, ngôn ngữ hiển thị, chọn trình duyệt ưu tiên và giãn cách an toàn. Mọi thay đổi được tự động lưu xuống tệp cấu hình.",
            ["Settings.ThemeLabel"] = "Chế độ giao diện",
            ["Settings.ThemeDark"] = "Chế độ tối",
            ["Settings.ThemeLight"] = "Chế độ sáng",
            ["Settings.LanguageLabel"] = "Ngôn ngữ hiển thị",
            ["Settings.BrowserEngineLabel"] = "Trình duyệt ưu tiên mở phiên",
            ["Settings.BrowserAuto"] = "Tự động nhận diện trình duyệt",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Đường dẫn trình duyệt tùy chỉnh",
            ["Settings.HeadlessLabel"] = "Chạy trình duyệt ẩn dưới nền",
            ["Settings.DelayRangeLabel"] = "Giãn cách an toàn giữa các lần thao tác",
            ["Settings.MinDelayPrefix"] = "Tối thiểu:",
            ["Settings.MaxDelayPrefix"] = "Tối đa:",
            ["Settings.BtnSaveSettings"] = "Lưu Cấu hình",
            ["Settings.BtnOpenAuditLogs"] = "Xem nhật ký hoạt động",
            ["Audit.BtnBackToSettings"] = "Quay lại Cài đặt",
            ["Settings.GuideTitle"] = "Hướng dẫn Sử dụng Nhanh",
            ["Settings.GuideBody"] = "1. Kết nối Tài khoản: Vào mục Tài khoản, bấm Mở Trình duyệt & Đồng bộ rồi đăng nhập Facebook. Phần mềm tự động lưu phiên và đồng bộ Nhóm.\n\n2. Nhập nhanh Cookie: Sao chép từ J2Team Cookies rồi bấm Dán từ Clipboard.\n\n3. Đăng bài & Săn khách: Chọn nhóm trực quan, phần mềm tự giãn cách an toàn và chống bình luận trùng lặp.",

            ["Audit.TaskbarTitle"] = "Nhật ký Hoạt động Hệ thống",
            ["Audit.ToggleExpand"] = "Mở rộng Nhật ký",
            ["Audit.ToggleCollapse"] = "Thu gọn Nhật ký",
            ["Audit.FilterCategoryAll"] = "Tất cả sự kiện",
            ["Audit.Cat.System"] = "Hệ thống",
            ["Audit.Cat.Account"] = "Tài khoản",
            ["Audit.Cat.Group"] = "Nhóm",
            ["Audit.Cat.Post"] = "Đăng bài",
            ["Audit.Cat.SalesComment"] = "Bình luận",
            ["Audit.Cat.Scrape"] = "Quét khách",
            ["Audit.Time.All"] = "Mọi thời gian",
            ["Audit.Time.LastHour"] = "1 giờ qua",
            ["Audit.Time.Today24h"] = "Trong 24 giờ qua",
            ["Audit.Time.Last7Days"] = "7 ngày qua",
            ["Audit.FilterAccountAll"] = "Tất cả tài khoản",
            ["Audit.SearchPlaceholder"] = "Tìm hoặc lọc nhật ký hoạt động...",
            ["Audit.BtnRefresh"] = "Làm mới",
            ["Audit.BtnClear"] = "Xóa cũ",
            ["Audit.ReadyTicker"] = "Sẵn sàng — Chưa có tác vụ nào đang chạy.",

            ["Common.BtnStopTask"] = "Dừng tác vụ",
            ["Publisher.BtnPreviewSpintax"] = "Xem thử nội dung",
            ["Publisher.SpintaxPreviewPrefix"] = "Bản xem trước ngẫu nhiên:",
            ["Publisher.DefaultPostSpintax"] = "{Xin chào cả nhà|Chào anh chị em trong nhóm}! Bên mình {đang sẵn hàng|mới về lô hàng} chính hãng giá ưu đãi, {inbox|nhắn tin} nhận báo giá chi tiết nhé!",
            ["Publisher.DefaultBumpSpintax"] = "{Chấm đẩy bài ạ|Hàng vẫn sẵn kho anh chị nhé|Quan tâm nhắn tin giúp em nhé|Up cho anh chị cần ạ}!",
            ["Leads.DefaultSalesCommentSpintax"] = "{Check inbox giúp em nhé|Em đã gửi báo giá chi tiết qua tin nhắn rồi ạ|Anh/chị kiểm tra tin nhắn chờ nhé}!",
            ["Accounts.ProxyPlaceholder"] = "Nhập địa chỉ Proxy riêng dạng host:port hoặc socks5://host:port",
            ["Accounts.BtnSaveProxy"] = "Lưu Proxy",
            ["Accounts.BtnCheckHealth"] = "Kiểm tra Phiên",
            ["Accounts.DefaultBrowserLoginName"] = "Tài khoản Đăng nhập Trình duyệt",
            ["Accounts.DefaultImportedName"] = "Tài khoản FB",
            ["Accounts.ProxyBadgePrefix"] = "Proxy:",
            ["Accounts.NoProxyBadge"] = "Mạng trực tiếp",
            ["Groups.BtnAutoJoinDiscovered"] = "Tham gia hàng loạt",
            ["Leads.IntentHigh"] = "Nhu cầu cao",
            ["Leads.IntentWarm"] = "Quan tâm",
            ["Kpi.AccountsTitle"] = "TÀI KHOẢN SẴN SÀNG",
            ["Kpi.GroupsTitle"] = "NHÓM ĐÍCH ĐANG BẬT",
            ["Kpi.LeadsTitle"] = "KHÁCH HÀNG TIỀM NĂNG",
            ["Kpi.TelemetryTitle"] = "TÁC VỤ THÀNH CÔNG",
            ["Empty.Accounts"] = "Chưa có tài khoản nào — bấm Mở Trình duyệt & Đồng bộ hoặc dán Cookie.",
            ["Empty.Groups"] = "Chưa có nhóm nào — bấm Đồng bộ Nhóm hoặc tìm theo từ khóa.",
            ["Empty.TargetGroups"] = "Chưa chọn nhóm đích — hãy chọn một nhóm bên trái và bấm Thêm vào Nhóm Đích.",
            ["Empty.Templates"] = "Chưa có mẫu lưu sẵn — soạn bài viết và bấm Lưu Mẫu.",
            ["Empty.Leads"] = "Chưa có bài viết khách hàng — hãy chọn Nhóm và bấm Quét Bài viết Nhóm.",
            ["Empty.AuditLogs"] = "Chưa có nhật ký hoạt động khớp bộ lọc hiện tại.",
            ["Accounts.BtnWarmUp"] = "Nuôi tài khoản",
            ["Leads.AutoReactToggle"] = "Tự động thả cảm xúc trước khi gửi bình luận",
            ["Publisher.AutoReactOwnPost"] = "Tự động thả tim bài đăng",
            ["Publisher.ScheduleDelayLabel"] = "Hẹn giờ phút:",
            ["Log.TaskStoppedByUser"] = "Đã dừng tác vụ ngay lập tức theo yêu cầu của người dùng.",
            ["Log.SelectAccountFirst"] = "Vui lòng chọn hoặc thêm một tài khoản trước khi thực hiện tác vụ.",
            ["Log.ConnectionInterrupted"] = "Kết nối tạm thời bị gián đoạn, vui lòng thử lại."
        };

    private static Dictionary<string, string> CreateEnglishCatalog() =>
        new(StringComparer.Ordinal)
        {
            ["App.Title"] = "Facebook.Net — Automated Group Publishing & Sales Seeding Suite",
            ["App.Subtitle"] = "Multi-account vault, visual group discovery, smart publishing & sales commenting",
            ["TopBar.ActiveAccount"] = "Active Account:",
            ["TopBar.NoAccount"] = "No account selected",
            ["Common.BrowseFile"] = "Browse...",
            ["Common.FilterButton"] = "Filter",
            ["Common.SearchOrFilter"] = "Search or filter",

            ["Table.ColTime"] = "Time",
            ["Table.ColAccount"] = "Account",
            ["Table.ColCategory"] = "Category",
            ["Table.ColTarget"] = "Target",
            ["Table.ColMessage"] = "Details",

            ["Status.Account.Idle"] = "Ready to connect",
            ["Status.Account.Active"] = "Active",
            ["Status.Account.Running"] = "Running task",
            ["Status.Account.Expired"] = "Re-login required",
            ["Status.Account.Checkpoint"] = "Security verification needed",
            ["Status.Account.Disabled"] = "Disabled",

            ["Status.Group.Member"] = "Joined Member",
            ["Status.Group.Requested"] = "Pending Approval",
            ["Status.Group.QuestionsDone"] = "Questions Answered",
            ["Status.Group.CanJoin"] = "Not Joined",

            ["Nav.Overview"] = "Home",
            ["Nav.Accounts"] = "Accounts",
            ["Nav.Groups"] = "Groups",
            ["Nav.Publisher"] = "Publisher",
            ["Nav.SalesLeads"] = "Sales Leads",
            ["Nav.Settings"] = "Settings",
            ["Nav.AuditLogs"] = "Activity Logs",

            ["Sidebar.Collapse"] = "Collapse menu",
            ["Sidebar.Expand"] = "Expand menu",
            ["Overview.ModeLabel"] = "Account:",
            ["Overview.StatusConnected"] = "Connected",
            ["Overview.StatusDisconnected"] = "Not connected",
            ["Overview.StatusRunning"] = "Running task",
            ["Overview.SubtextConnected"] = "Your browser session is active and ready for automation.",
            ["Overview.SubtextDisconnected"] = "Select an account or open the browser to connect a session.",
            ["Overview.InfoTip"] = "Central dashboard displaying live KPI metrics, campaign readiness checklist, and real-time activity logs.",
            ["Overview.SessionCardTitle"] = "Session Status & Readiness Checklist",
            ["Overview.RecentLogsTitle"] = "Recent Activity",
            ["Overview.CheckAccountReady"] = "Active account connected",
            ["Overview.CheckAccountMissing"] = "No connected account yet",
            ["Overview.CheckGroupsReady"] = "Target groups enabled",
            ["Overview.CheckGroupsMissing"] = "No target groups enabled",
            ["Overview.QuickActionsTitle"] = "Quick Actions",

            ["Accounts.Header"] = "Facebook Account Vault",
            ["Accounts.Desc"] = "Log in directly via an isolated browser window or 1-click paste from J2Team Cookies.",
            ["Accounts.NamePlaceholder"] = "Friendly account label",
            ["Accounts.CookiePlaceholder"] = "Paste J2Team JSON or Cookie string here",
            ["Accounts.BtnQuickImport"] = "Save / Import Cookie",
            ["Accounts.BtnPasteClipboard"] = "Paste Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Open Browser & Sync",
            ["Accounts.BtnDelete"] = "Remove Account",
            ["Accounts.LastActivePrefix"] = "Last active:",
            ["Accounts.ReadyToConnect"] = "Ready to launch browser",

            ["Groups.Header"] = "Facebook Groups Catalog",
            ["Groups.Desc"] = "Automatically syncs all joined groups from your active account so you can visually pick targets for posting and lead scanning.",
            ["Groups.SearchKeywordPlaceholder"] = "Search new groups or filter by group name...",
            ["Groups.FilterPlaceholder"] = "Quick filter synced groups by name...",
            ["Groups.BtnSyncJoined"] = "Sync Groups",
            ["Groups.BtnSearchNew"] = "Find New Groups",
            ["Groups.BtnJoinSelected"] = "Join Group",
            ["Groups.BtnSaveTarget"] = "Add to Target Groups",
            ["Groups.TargetListTitle"] = "Target Campaign Groups",
            ["Groups.BtnSelectAllTargets"] = "Enable All",
            ["Groups.BtnDeselectAllTargets"] = "Disable All",
            ["Groups.BtnRemoveTarget"] = "Remove from Targets",

            ["Publisher.Header"] = "Automated Group & Feed Publisher",
            ["Publisher.Desc"] = "Compose dynamic Spintax content, attach product photos, and publish or bump posts across target groups.",
            ["Publisher.TargetModeGroup"] = "Publish to Target Groups",
            ["Publisher.TargetModePersonal"] = "Publish to Personal Timeline",
            ["Publisher.ContentPlaceholder"] = "Enter post content supporting {a|b|c} Spintax variations...",
            ["Publisher.PhotoPlaceholder"] = "No photo attached",
            ["Publisher.BtnPickPhoto"] = "Choose Photo...",
            ["Publisher.BtnSaveTemplate"] = "Save Template",
            ["Publisher.BtnDeleteTemplate"] = "Delete Selected Template",
            ["Publisher.BtnPublishNow"] = "Publish Campaign Now",
            ["Publisher.TemplatesTitle"] = "Saved Post Templates",
            ["Publisher.BumpTitle"] = "Bump Posts in Groups",
            ["Publisher.BumpPlaceholder"] = "Enter bump comment Spintax...",
            ["Publisher.BtnBumpRecentPosts"] = "Bump Recent Posts",

            ["Leads.Header"] = "Group Lead Scanner & Auto Commenter",
            ["Leads.Desc"] = "Scan recent posts inside target groups, filter by buyer keywords, and send sales comments with pricing photos.",
            ["Leads.BuyerKeywordPlaceholder"] = "Enter buyer keywords to scan or filter...",
            ["Leads.BtnScanGroupPosts"] = "Scan Group Posts",
            ["Leads.ScrapedPostsTitle"] = "Discovered Group Posts",
            ["Leads.SelectedPostBanner"] = "Selected Post:",
            ["Leads.NoPostSelected"] = "No post selected — click a post card in the list",
            ["Leads.CommentContentPlaceholder"] = "Enter sales comment Spintax...",
            ["Leads.CommentPhotoPlaceholder"] = "No pricing photo selected",
            ["Leads.BtnSendCommentSelected"] = "Comment Selected",
            ["Leads.BtnAutoCommentAllMatched"] = "Auto-Comment All",
            ["Leads.BtnOpenPostInBrowser"] = "Open in Browser",
            ["Leads.DefaultAuthor"] = "Group Member",
            ["Leads.MediaOnlyPost"] = "Media attachment post",

            ["Settings.Header"] = "System & Browser Settings",
            ["Settings.Desc"] = "Customize appearance, display language, preferred browser engine, and safe delays. All changes are saved automatically.",
            ["Settings.ThemeLabel"] = "Appearance Theme",
            ["Settings.ThemeDark"] = "Dark Mode",
            ["Settings.ThemeLight"] = "Light Mode",
            ["Settings.LanguageLabel"] = "Display Language",
            ["Settings.BrowserEngineLabel"] = "Preferred Browser Engine",
            ["Settings.BrowserAuto"] = "Auto-Detect Installed Browser",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Custom browser executable path",
            ["Settings.HeadlessLabel"] = "Run browser in background",
            ["Settings.DelayRangeLabel"] = "Safe human-like delay between actions",
            ["Settings.MinDelayPrefix"] = "Min:",
            ["Settings.MaxDelayPrefix"] = "Max:",
            ["Settings.BtnSaveSettings"] = "Save Settings",
            ["Settings.BtnOpenAuditLogs"] = "Open Activity Logs",
            ["Audit.BtnBackToSettings"] = "Back to Settings",
            ["Settings.GuideTitle"] = "Quick Start Guide",
            ["Settings.GuideBody"] = "1. Connect Account: Go to Accounts, click Open Browser & Sync, and sign in to Facebook. The app automatically saves your session and syncs your Groups.\n\n2. 1-Click Cookie Import: Copy cookies from J2Team Cookies and click Paste Clipboard.\n\n3. Publish & Close Sales Leads: Pick groups visually from your catalog; the app automatically enforces human-like delays and prevents duplicate comments.",

            ["Audit.TaskbarTitle"] = "System Activity & Audit Logs",
            ["Audit.ToggleExpand"] = "Expand Audit Logs",
            ["Audit.ToggleCollapse"] = "Collapse Audit Logs",
            ["Audit.FilterCategoryAll"] = "All Events",
            ["Audit.Cat.System"] = "System",
            ["Audit.Cat.Account"] = "Account",
            ["Audit.Cat.Group"] = "Group",
            ["Audit.Cat.Post"] = "Publish",
            ["Audit.Cat.SalesComment"] = "Sales Comment",
            ["Audit.Cat.Scrape"] = "Lead Scan",
            ["Audit.Time.All"] = "All Time",
            ["Audit.Time.LastHour"] = "Last 1 Hour",
            ["Audit.Time.Today24h"] = "Last 24 Hours",
            ["Audit.Time.Last7Days"] = "Last 7 Days",
            ["Audit.FilterAccountAll"] = "All Accounts",
            ["Audit.SearchPlaceholder"] = "Search or filter audit logs...",
            ["Audit.BtnRefresh"] = "Refresh",
            ["Audit.BtnClear"] = "Clear Old",
            ["Audit.ReadyTicker"] = "Ready — No active automation tasks running.",

            ["Common.BtnStopTask"] = "Stop Task",
            ["Publisher.BtnPreviewSpintax"] = "Preview Spintax",
            ["Publisher.SpintaxPreviewPrefix"] = "Randomized preview:",
            ["Publisher.DefaultPostSpintax"] = "{Hello everyone|Hi group members}! We have {new arrivals|official stock} available at special pricing, {send a message|DM us} for full details!",
            ["Publisher.DefaultBumpSpintax"] = "{Up|Still available in stock|Send a message for details|Bumping for anyone interested}!",
            ["Leads.DefaultSalesCommentSpintax"] = "{Please check your inbox|Sent you detailed pricing via message|Please check your message requests}!",
            ["Accounts.ProxyPlaceholder"] = "Dedicated Proxy address host:port or socks5://host:port",
            ["Accounts.BtnSaveProxy"] = "Save Proxy",
            ["Accounts.BtnCheckHealth"] = "Check Session",
            ["Accounts.DefaultBrowserLoginName"] = "Browser Login Account",
            ["Accounts.DefaultImportedName"] = "FB Account",
            ["Accounts.ProxyBadgePrefix"] = "Proxy:",
            ["Accounts.NoProxyBadge"] = "Direct connection",
            ["Groups.BtnAutoJoinDiscovered"] = "Batch Join",
            ["Leads.IntentHigh"] = "High Intent",
            ["Leads.IntentWarm"] = "Warm Inquiry",
            ["Kpi.AccountsTitle"] = "READY ACCOUNTS",
            ["Kpi.GroupsTitle"] = "ACTIVE TARGET GROUPS",
            ["Kpi.LeadsTitle"] = "DISCOVERED LEADS",
            ["Kpi.TelemetryTitle"] = "SUCCESSFUL ACTIONS",
            ["Empty.Accounts"] = "No accounts added yet — click Open Browser & Sync or paste a Cookie.",
            ["Empty.Groups"] = "No groups loaded yet — click Sync Groups or search by keyword.",
            ["Empty.TargetGroups"] = "No target groups selected — pick a group on the left and click Add to Target Groups.",
            ["Empty.Templates"] = "No saved templates yet — compose a post and click Save Template.",
            ["Empty.Leads"] = "No buyer posts discovered yet — select a Group and click Scan Group Posts.",
            ["Empty.AuditLogs"] = "No audit log entries match the current filters.",
            ["Accounts.BtnWarmUp"] = "Warm Up",
            ["Leads.AutoReactToggle"] = "Auto-React before sending Sales Comment",
            ["Publisher.AutoReactOwnPost"] = "Auto-Love post",
            ["Publisher.ScheduleDelayLabel"] = "Delay min:",
            ["Log.TaskStoppedByUser"] = "Task stopped immediately as requested by user.",
            ["Log.SelectAccountFirst"] = "Please select or add an account before running this task.",
            ["Log.ConnectionInterrupted"] = "Connection temporarily interrupted, please try again."
        };
}
