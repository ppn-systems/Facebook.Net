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
/// Partial implementation of <see cref="LocalizationService"/> containing complete <c>vi-VN</c> and <c>en-US</c> translation dictionaries
/// and enum-to-human-label formatters. Zero developer jargon (never asks end users for raw UIDs, UUIDs, or Group IDs).
/// </summary>
public sealed partial class LocalizationService
{
    /// <summary>Formats an <see cref="AccountStatus"/> into a user-friendly localized string.</summary>
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

    /// <summary>Formats a <see cref="GroupMembershipState"/> into a user-friendly localized string.</summary>
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

    /// <summary>Formats an <see cref="AuditCategory"/> into a user-friendly localized string.</summary>
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

    /// <summary>Formats an <see cref="AuditTimeRange"/> into a user-friendly localized string.</summary>
    public string FormatAuditTimeRange(AuditTimeRange range) =>
        range switch
        {
            AuditTimeRange.LastHour => this["Audit.Time.LastHour"],
            AuditTimeRange.Today24Hours => this["Audit.Time.Today24h"],
            AuditTimeRange.Last7Days => this["Audit.Time.Last7Days"],
            AuditTimeRange.AllTime => this["Audit.Time.All"],
            _ => this["Audit.Time.All"]
        };

    /// <summary>Formats a <see cref="PreferredBrowserKind"/> into a user-friendly localized string.</summary>
    public string FormatBrowserKind(PreferredBrowserKind kind) =>
        kind switch
        {
            PreferredBrowserKind.Edge => this["Settings.BrowserEdge"],
            PreferredBrowserKind.Chrome => this["Settings.BrowserChrome"],
            PreferredBrowserKind.Brave => this["Settings.BrowserBrave"],
            PreferredBrowserKind.AutoDetect => this["Settings.BrowserAuto"],
            _ => this["Settings.BrowserAuto"]
        };

    /// <summary>Formats an <see cref="AppThemeMode"/> into a user-friendly localized string.</summary>
    public string FormatThemeMode(AppThemeMode mode) =>
        mode == AppThemeMode.Light ? this["Settings.ThemeLight"] : this["Settings.ThemeDark"];

    /// <summary>Formats an <see cref="AppLanguage"/> into a user-friendly localized string.</summary>
    public static string FormatLanguage(AppLanguage lang) =>
        lang == AppLanguage.EnUs ? "English (en-US)" : "Tiếng Việt (vi-VN)";

    private static Dictionary<string, string> CreateVietnameseCatalog() =>
        new(StringComparer.Ordinal)
        {
            ["App.Title"] = "Facebook.Net — Hệ thống Bán hàng, Đăng Nhóm & Seeding Tự động",
            ["App.Subtitle"] = "Quản lý đa tài khoản, tự động đồng bộ nhóm, đăng bài & comment sale thông minh",
            ["TopBar.ActiveAccount"] = "Tài khoản đang chọn:",
            ["TopBar.NoAccount"] = "Chưa chọn tài khoản",
            ["Common.BrowseFile"] = "Chọn tệp...",

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
            ["Overview.InfoTip"] = "Trang chủ hiển thị trạng thái kết nối thực tế của tài khoản đang chọn. Bạn có thể chuyển đổi nhanh tài khoản, kiểm tra phiên Live hoặc mở trình duyệt đồng bộ chỉ với 1 cú nhấp chuột.",

            ["Accounts.Header"] = "Quản lý Tài khoản Facebook",
            ["Accounts.Desc"] = "Đăng nhập trực tiếp trên trình duyệt hoặc dán nhanh từ J2Team Cookies / Get Token Cookie (hoàn toàn tự động).",
            ["Accounts.NamePlaceholder"] = "Tên gợi nhớ (VD: Nick Bán Hàng BĐS 01)",
            ["Accounts.CookiePlaceholder"] = "Dán JSON J2Team Cookies hoặc chuỗi Cookie (Tùy chọn — để trống để đăng nhập trình duyệt)",
            ["Accounts.BtnQuickImport"] = "Lưu / Nhập Cookie",
            ["Accounts.BtnPasteClipboard"] = "Dán từ Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Mở Trình duyệt Đăng nhập & Đồng bộ",
            ["Accounts.BtnDelete"] = "Gỡ tài khoản",
            ["Accounts.LastActivePrefix"] = "Hoạt động gần nhất:",
            ["Accounts.ReadyToConnect"] = "Sẵn sàng mở trình duyệt",

            ["Groups.Header"] = "Danh mục Nhóm Facebook",
            ["Groups.Desc"] = "Tự động lấy toàn bộ danh sách nhóm của tài khoản để bạn tích chọn đăng bài hoặc quét khách hàng.",
            ["Groups.SearchKeywordPlaceholder"] = "Tìm nhóm mới theo từ khóa (VD: Chợ cư dân Vinhomes, Mua bán Laptop...)",
            ["Groups.FilterPlaceholder"] = "Lọc nhanh theo tên nhóm...",
            ["Groups.BtnSyncJoined"] = "Đồng bộ Nhóm đã tham gia",
            ["Groups.BtnSearchNew"] = "Tìm Nhóm mới",
            ["Groups.BtnJoinSelected"] = "Tham gia Nhóm",
            ["Groups.BtnSaveTarget"] = "Thêm vào Nhóm Đích",
            ["Groups.TargetListTitle"] = "Danh sách Nhóm Đích (Tích chọn để chạy)",
            ["Groups.BtnSelectAllTargets"] = "Bật tất cả",
            ["Groups.BtnDeselectAllTargets"] = "Tắt tất cả",
            ["Groups.BtnRemoveTarget"] = "Gỡ khỏi Nhóm Đích",

            ["Publisher.Header"] = "Chiến dịch Đăng bài & Đẩy Top",
            ["Publisher.Desc"] = "Soạn nội dung xoay vòng Spintax {a|b|c}, chọn ảnh sản phẩm và đăng tự động hoặc đẩy bài (Up-Top) lên đầu nhóm.",
            ["Publisher.TargetModeGroup"] = "Đăng vào Nhóm mục tiêu",
            ["Publisher.TargetModePersonal"] = "Đăng Trang cá nhân",
            ["Publisher.ContentPlaceholder"] = "Nội dung bài đăng (Hỗ trợ Spintax: {Xin chào cả nhà|Chào anh chị}, bên em {xả kho|thanh lý} giá tốt...)",
            ["Publisher.PhotoPlaceholder"] = "Chưa chọn ảnh đính kèm",
            ["Publisher.BtnPickPhoto"] = "Chọn ảnh...",
            ["Publisher.BtnSaveTemplate"] = "Lưu Mẫu",
            ["Publisher.BtnDeleteTemplate"] = "Xóa mẫu đang chọn",
            ["Publisher.BtnPublishNow"] = "Đăng bài Tự động",
            ["Publisher.TemplatesTitle"] = "Kho Mẫu Bài Viết Đã lưu",
            ["Publisher.BumpTitle"] = "Đẩy Top (Up-Top) bài viết trong Nhóm",
            ["Publisher.BumpPlaceholder"] = "Nội dung bình luận đẩy bài (Spintax: {Chấm|.|Up bài|Hàng vẫn còn anh chị nhé})",
            ["Publisher.BtnBumpRecentPosts"] = "Đẩy Top ngay",

            ["Leads.Header"] = "Săn Khách Nhóm & Auto Comment Sale",
            ["Leads.Desc"] = "Quét bài viết mới nhất trong các nhóm mục tiêu, lọc theo từ khóa mua hàng và gửi Comment Sale kèm ảnh báo giá (tự động chống trùng lặp).",
            ["Leads.BuyerKeywordPlaceholder"] = "Từ khóa khách mua (VD: cần mua, xin giá, tư vấn, ib) — để trống để lấy tất cả",
            ["Leads.BtnScanGroupPosts"] = "Quét Bài viết Nhóm",
            ["Leads.ScrapedPostsTitle"] = "Bài viết Khách hàng thu thập được",
            ["Leads.SelectedPostBanner"] = "Bài đang chọn:",
            ["Leads.NoPostSelected"] = "Chưa chọn bài viết — bấm chọn một bài ở cột trái",
            ["Leads.CommentContentPlaceholder"] = "Nội dung Comment Sale (Spintax: {Check inbox giúp em nhé|Em gửi bảng giá qua tin nhắn rồi ạ}!)",
            ["Leads.CommentPhotoPlaceholder"] = "Chưa chọn ảnh báo giá",
            ["Leads.BtnSendCommentSelected"] = "Comment bài đang chọn",
            ["Leads.BtnAutoCommentAllMatched"] = "Auto-Comment tất cả",
            ["Leads.BtnOpenPostInBrowser"] = "Mở xem bài trên Web",
            ["Leads.DefaultAuthor"] = "Thành viên trong nhóm",
            ["Leads.MediaOnlyPost"] = "(Bài viết đính kèm hình ảnh / video)",

            ["Settings.Header"] = "Cài đặt Hệ thống & Trình duyệt",
            ["Settings.Desc"] = "Tùy chỉnh giao diện Sáng / Tối, ngôn ngữ hiển thị, chọn trình duyệt ưu tiên và giãn cách an toàn chống Checkpoint. Mọi thay đổi được tự động lưu ngay lập tức.",
            ["Settings.ThemeLabel"] = "Chế độ Giao diện (Theme)",
            ["Settings.ThemeDark"] = "Chế độ Tối (Dark Mode)",
            ["Settings.ThemeLight"] = "Chế độ Sáng (Light Mode)",
            ["Settings.LanguageLabel"] = "Ngôn ngữ hiển thị (Language)",
            ["Settings.BrowserEngineLabel"] = "Trình duyệt ưu tiên mở phiên (Browser Engine)",
            ["Settings.BrowserAuto"] = "Tự động nhận diện (Edge / Chrome / Brave)",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Đường dẫn trình duyệt tùy chỉnh (Tùy chọn)",
            ["Settings.HeadlessLabel"] = "Chạy trình duyệt ẩn (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Giãn cách an toàn giữa các lần đăng / bình luận (Giây)",
            ["Settings.MinDelayPrefix"] = "Tối thiểu (s):",
            ["Settings.MaxDelayPrefix"] = "Tối đa (s):",
            ["Settings.BtnSaveSettings"] = "Lưu Cấu hình",
            ["Settings.BtnOpenAuditLogs"] = "Xem Nhật ký Hoạt động (Audit Logs)",
            ["Audit.BtnBackToSettings"] = "Quay lại Cài đặt",
            ["Settings.GuideTitle"] = "Hướng dẫn Sử dụng Nhanh",
            ["Settings.GuideBody"] = "1. Kết nối Tài khoản: Vào mục 'Tài khoản' -> Bấm 'Mở Trình duyệt Đăng nhập & Đồng bộ' -> Đăng nhập Facebook như bình thường. Phần mềm tự động lưu phiên và đồng bộ Nhóm.\n\n2. Nhập nhanh Cookie: Sao chép từ J2Team Cookies rồi bấm 'Dán từ Clipboard'.\n\n3. Đăng bài & Săn khách: Chọn nhóm trực quan, phần mềm tự giãn cách an toàn và chống bình luận trùng lặp.",

            ["Audit.TaskbarTitle"] = "Nhật ký Hoạt động (Audit Logs)",
            ["Audit.ToggleExpand"] = "Mở rộng Nhật ký",
            ["Audit.ToggleCollapse"] = "Thu gọn Nhật ký",
            ["Audit.FilterCategoryAll"] = "Tất cả sự kiện",
            ["Audit.Cat.System"] = "Hệ thống",
            ["Audit.Cat.Account"] = "Tài khoản",
            ["Audit.Cat.Group"] = "Nhóm",
            ["Audit.Cat.Post"] = "Đăng bài",
            ["Audit.Cat.SalesComment"] = "Comment Sale",
            ["Audit.Cat.Scrape"] = "Quét khách",
            ["Audit.Time.All"] = "Mọi thời gian",
            ["Audit.Time.LastHour"] = "1 giờ qua",
            ["Audit.Time.Today24h"] = "Trong 24 giờ qua",
            ["Audit.Time.Last7Days"] = "7 ngày qua",
            ["Audit.FilterAccountAll"] = "Tất cả tài khoản",
            ["Audit.SearchPlaceholder"] = "Tìm trong nhật ký (tên nhóm, nội dung, trạng thái...)",
            ["Audit.BtnRefresh"] = "Làm mới",
            ["Audit.BtnClear"] = "Xóa cũ",
            ["Audit.ReadyTicker"] = "Sẵn sàng — Chưa có tác vụ nào đang chạy.",

            ["Common.BtnStopTask"] = "Dừng tác vụ",
            ["Publisher.BtnPreviewSpintax"] = "Xem thử Spintax",
            ["Publisher.SpintaxPreviewPrefix"] = "Bản xem trước ngẫu nhiên:",
            ["Publisher.DefaultPostSpintax"] = "{Xin chào cả nhà|Chào anh chị em trong nhóm}! Bên mình {đang sẵn hàng|mới về lô hàng} chính hãng giá ưu đãi, {inbox|nhắn tin} nhận báo giá chi tiết nhé!",
            ["Publisher.DefaultBumpSpintax"] = "{Chấm đẩy bài ạ|Hàng vẫn sẵn kho anh chị nhé|Quan tâm nhắn tin giúp em nhé|Up cho anh chị cần ạ}!",
            ["Leads.DefaultSalesCommentSpintax"] = "{Check inbox giúp em nhé|Em đã gửi báo giá chi tiết qua tin nhắn rồi ạ|Anh/chị kiểm tra tin nhắn chờ nhé}!",
            ["Accounts.ProxyPlaceholder"] = "Proxy riêng (Tùy chọn: host:port hoặc socks5://host:port:user:pass)",
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
            ["Empty.Accounts"] = "Chưa có tài khoản nào — bấm 'Mở Trình duyệt Đăng nhập & Đồng bộ' hoặc dán Cookie ở cột trái.",
            ["Empty.Groups"] = "Chưa có nhóm nào — bấm 'Đồng bộ Nhóm đã tham gia' hoặc tìm theo từ khóa.",
            ["Empty.TargetGroups"] = "Chưa chọn nhóm đích — hãy chọn một nhóm bên trái và bấm 'Thêm vào Nhóm Đích'.",
            ["Empty.Templates"] = "Chưa có mẫu lưu sẵn — soạn bài viết bên trái và bấm 'Lưu Mẫu'.",
            ["Empty.Leads"] = "Chưa có bài viết khách hàng — hãy chọn Nhóm và bấm 'Quét Bài viết Nhóm'.",
            ["Empty.AuditLogs"] = "Chưa có nhật ký hoạt động khớp bộ lọc hiện tại.",
            ["Accounts.BtnWarmUp"] = "Nuôi tài khoản",
            ["Leads.AutoReactToggle"] = "Tự động Like/Tim vào bài viết trước khi gửi Comment Sale",
            ["Publisher.AutoReactOwnPost"] = "Tự động thả tim bài đăng",
            ["Publisher.ScheduleDelayLabel"] = "Hẹn giờ (phút):",
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
            ["Overview.InfoTip"] = "The Home screen shows the live session status of your active account. Quickly switch accounts, verify live session health, or launch the browser with one click.",

            ["Accounts.Header"] = "Facebook Account Vault",
            ["Accounts.Desc"] = "Log in directly via an isolated browser window or 1-click paste from J2Team Cookies / Get Token Cookie (fully automatic).",
            ["Accounts.NamePlaceholder"] = "Friendly account label (e.g., Main Sales Account 01)",
            ["Accounts.CookiePlaceholder"] = "Paste J2Team JSON or Cookie string (Optional — leave blank to log in interactively)",
            ["Accounts.BtnQuickImport"] = "Save / Import Cookie",
            ["Accounts.BtnPasteClipboard"] = "Paste Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Open Browser to Log In & Sync",
            ["Accounts.BtnDelete"] = "Remove Account",
            ["Accounts.LastActivePrefix"] = "Last active:",
            ["Accounts.ReadyToConnect"] = "Ready to launch browser",

            ["Groups.Header"] = "Facebook Groups Catalog",
            ["Groups.Desc"] = "Automatically syncs all joined groups from your active account so you can visually pick targets for posting and lead scanning.",
            ["Groups.SearchKeywordPlaceholder"] = "Discover new groups by keyword (e.g., Real Estate Deals, Laptop Market...)",
            ["Groups.FilterPlaceholder"] = "Quick filter synced groups by name...",
            ["Groups.BtnSyncJoined"] = "Sync Joined Groups",
            ["Groups.BtnSearchNew"] = "Search Groups",
            ["Groups.BtnJoinSelected"] = "Join Group",
            ["Groups.BtnSaveTarget"] = "Add to Target Groups",
            ["Groups.TargetListTitle"] = "Target Campaign Groups (Check to enable)",
            ["Groups.BtnSelectAllTargets"] = "Enable All",
            ["Groups.BtnDeselectAllTargets"] = "Disable All",
            ["Groups.BtnRemoveTarget"] = "Remove Target",

            ["Publisher.Header"] = "Group Publisher & Post Bumper",
            ["Publisher.Desc"] = "Compose Spintax {a|b|c} posts with product photos and publish or bump (Up-Top) posts across your target groups.",
            ["Publisher.TargetModeGroup"] = "Post to Target Groups",
            ["Publisher.TargetModePersonal"] = "Personal Timeline",
            ["Publisher.ContentPlaceholder"] = "Post content (Supports Spintax: {Hello everyone|Hi friends}, check out our {new arrivals|special deal}...)",
            ["Publisher.PhotoPlaceholder"] = "No photo selected",
            ["Publisher.BtnPickPhoto"] = "Browse Photo...",
            ["Publisher.BtnSaveTemplate"] = "Save Template",
            ["Publisher.BtnDeleteTemplate"] = "Delete Template",
            ["Publisher.BtnPublishNow"] = "Start Publishing",
            ["Publisher.TemplatesTitle"] = "Saved Content Templates",
            ["Publisher.BumpTitle"] = "Auto Up-Top (Bump) Group Posts",
            ["Publisher.BumpPlaceholder"] = "Bump comment text (Spintax: {Up|.|Still available|DM me for special pricing})",
            ["Publisher.BtnBumpRecentPosts"] = "Bump Posts Now",

            ["Leads.Header"] = "Group Lead Scanner & Auto Sales Comment",
            ["Leads.Desc"] = "Scan recent posts inside target groups, filter by buyer-intent keywords, and drop sales comments with product photos (auto-deduplicated).",
            ["Leads.BuyerKeywordPlaceholder"] = "Buyer-intent keywords (e.g., looking to buy, price, dm me) — leave blank for all",
            ["Leads.BtnScanGroupPosts"] = "Scan Group Posts",
            ["Leads.ScrapedPostsTitle"] = "Discovered Buyer Posts",
            ["Leads.SelectedPostBanner"] = "Selected Post:",
            ["Leads.NoPostSelected"] = "No post selected — click any post card on the left",
            ["Leads.CommentContentPlaceholder"] = "Sales comment text (Spintax: {Please check your inbox|Sent you full pricing via DM}!)",
            ["Leads.CommentPhotoPlaceholder"] = "No product photo selected",
            ["Leads.BtnSendCommentSelected"] = "Comment Selected",
            ["Leads.BtnAutoCommentAllMatched"] = "Auto-Comment All",
            ["Leads.BtnOpenPostInBrowser"] = "Open in Browser",
            ["Leads.DefaultAuthor"] = "Group Member",
            ["Leads.MediaOnlyPost"] = "(Post contains photo / video attachment)",

            ["Settings.Header"] = "System & Browser Settings",
            ["Settings.Desc"] = "Configure Dark/Light theme, display language (vi-VN / en-US), preferred Chromium browser, and anti-checkpoint delays. All changes are saved automatically.",
            ["Settings.ThemeLabel"] = "Appearance Theme",
            ["Settings.ThemeDark"] = "Dark Mode",
            ["Settings.ThemeLight"] = "Light Mode",
            ["Settings.LanguageLabel"] = "Display Language",
            ["Settings.BrowserEngineLabel"] = "Preferred Chromium Browser Engine",
            ["Settings.BrowserAuto"] = "Auto-Detect (Edge / Chrome / Brave)",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Custom browser executable path (Optional)",
            ["Settings.HeadlessLabel"] = "Run browser in background (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Safe human-like delay between posts / comments (Seconds)",
            ["Settings.MinDelayPrefix"] = "Min (s):",
            ["Settings.MaxDelayPrefix"] = "Max (s):",
            ["Settings.BtnSaveSettings"] = "Save Settings",
            ["Settings.BtnOpenAuditLogs"] = "Open Activity & Audit Logs",
            ["Audit.BtnBackToSettings"] = "Back to Settings",
            ["Settings.GuideTitle"] = "Quick Start Guide",
            ["Settings.GuideBody"] = "1. Connect Account: Go to 'Accounts' -> Click 'Open Browser to Log In & Sync' -> Sign in to Facebook normally. The app automatically saves your session and syncs your Groups.\n\n2. 1-Click Cookie Import: Copy cookies from J2Team Cookies and click 'Paste Clipboard'.\n\n3. Publish & Close Sales Leads: Pick groups visually from your catalog; the app automatically enforces human-like delays and prevents duplicate comments.",

            ["Audit.TaskbarTitle"] = "Activity & Audit Logs",
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
            ["Audit.SearchPlaceholder"] = "Search audit logs (group name, post snippet, status...)",
            ["Audit.BtnRefresh"] = "Refresh",
            ["Audit.BtnClear"] = "Clear Old",
            ["Audit.ReadyTicker"] = "Ready — No active automation tasks running.",

            ["Common.BtnStopTask"] = "Stop Task",
            ["Publisher.BtnPreviewSpintax"] = "Preview Spintax",
            ["Publisher.SpintaxPreviewPrefix"] = "Randomized preview:",
            ["Publisher.DefaultPostSpintax"] = "{Hello everyone|Hi group members}! We have {new arrivals|official stock} available at special pricing, {send a message|DM us} for full details!",
            ["Publisher.DefaultBumpSpintax"] = "{Up|Still available in stock|Send a message for details|Bumping for anyone interested}!",
            ["Leads.DefaultSalesCommentSpintax"] = "{Please check your inbox|Sent you detailed pricing via message|Please check your message requests}!",
            ["Accounts.ProxyPlaceholder"] = "Dedicated Proxy (Optional: host:port or socks5://host:port:user:pass)",
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
            ["Empty.Accounts"] = "No accounts added yet — click 'Open Browser to Log In & Sync' or paste a Cookie on the left.",
            ["Empty.Groups"] = "No groups loaded yet — click 'Sync Joined Groups' or search by keyword.",
            ["Empty.TargetGroups"] = "No target groups selected — pick a group on the left and click 'Add to Target Groups'.",
            ["Empty.Templates"] = "No saved templates yet — compose a Spintax post on the left and click 'Save Template'.",
            ["Empty.Leads"] = "No buyer posts discovered yet — select a Group and click 'Scan Group Posts'.",
            ["Empty.AuditLogs"] = "No audit log entries match the current filters.",
            ["Accounts.BtnWarmUp"] = "Warm Up",
            ["Leads.AutoReactToggle"] = "Auto-React (Like/Love) to post before sending Sales Comment",
            ["Publisher.AutoReactOwnPost"] = "Auto-Love post",
            ["Publisher.ScheduleDelayLabel"] = "Delay (min):",
            ["Log.TaskStoppedByUser"] = "Task stopped immediately as requested by user.",
            ["Log.SelectAccountFirst"] = "Please select or add an account before running this task.",
            ["Log.ConnectionInterrupted"] = "Connection temporarily interrupted, please try again."
        };
}
