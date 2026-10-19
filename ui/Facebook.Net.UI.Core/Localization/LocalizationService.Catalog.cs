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

            ["Nav.Accounts"] = "Tài khoản & Phiên",
            ["Nav.Groups"] = "Nhóm & Cộng đồng",
            ["Nav.Publisher"] = "Đăng bài & Đẩy Top",
            ["Nav.SalesLeads"] = "Săn Khách & Comment Sale",
            ["Nav.Settings"] = "Cài đặt & Trình duyệt",

            ["Accounts.Header"] = "Quản lý Tài khoản Facebook",
            ["Accounts.Desc"] = "Đăng nhập trực tiếp trên trình duyệt hoặc dán nhanh từ J2Team Cookies / Get Token Cookie (hoàn toàn tự động).",
            ["Accounts.NamePlaceholder"] = "Tên gợi nhớ (VD: Nick Bán Hàng BĐS 01)",
            ["Accounts.CookiePlaceholder"] = "Dán JSON J2Team Cookies hoặc chuỗi Cookie (Tùy chọn — có thể để trống để đăng nhập trên trình duyệt)",
            ["Accounts.BtnQuickImport"] = "Nhập nhanh phiên / Cookie",
            ["Accounts.BtnPasteClipboard"] = "Dán 1-Click từ Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Mở Trình duyệt Đăng nhập & Đồng bộ",
            ["Accounts.BtnDelete"] = "Gỡ tài khoản đang chọn",
            ["Accounts.LastActivePrefix"] = "Hoạt động gần nhất:",
            ["Accounts.ReadyToConnect"] = "Sẵn sàng mở trình duyệt",

            ["Groups.Header"] = "Danh mục Nhóm Facebook đã tham gia",
            ["Groups.Desc"] = "Tự động lấy toàn bộ danh sách nhóm của tài khoản để bạn tích chọn đăng bài hoặc quét khách hàng.",
            ["Groups.SearchKeywordPlaceholder"] = "Tìm nhóm mới theo từ khóa (VD: Chợ cư dân Vinhomes, Mua bán Laptop...)",
            ["Groups.FilterPlaceholder"] = "Lọc nhanh theo tên nhóm trong danh sách bên dưới...",
            ["Groups.BtnSyncJoined"] = "Đồng bộ Nhóm từ Tài khoản đang chọn",
            ["Groups.BtnSearchNew"] = "Tìm & Khám phá Nhóm mới",
            ["Groups.BtnJoinSelected"] = "Tham gia Nhóm này",
            ["Groups.BtnSaveTarget"] = "Đưa vào Danh sách Nhóm Mục tiêu",
            ["Groups.TargetListTitle"] = "Danh sách Nhóm Mục tiêu (Tích chọn để chạy chiến dịch)",
            ["Groups.BtnSelectAllTargets"] = "Bật tất cả",
            ["Groups.BtnDeselectAllTargets"] = "Tắt tất cả",
            ["Groups.BtnRemoveTarget"] = "Xóa Nhóm khỏi Danh sách Mục tiêu",

            ["Publisher.Header"] = "Chiến dịch Đăng bài Nhóm, Trang cá nhân & Đẩy Top",
            ["Publisher.Desc"] = "Soạn nội dung xoay vòng Spintax {a|b|c}, chọn ảnh sản phẩm và đăng tự động hoặc đẩy bài (Up-Top) lên đầu nhóm.",
            ["Publisher.TargetModeGroup"] = "Đăng vào các Nhóm Mục tiêu đã tích chọn",
            ["Publisher.TargetModePersonal"] = "Đăng lên Trang cá nhân của tài khoản",
            ["Publisher.ContentPlaceholder"] = "Nội dung bài đăng (Hỗ trợ Spintax: {Xin chào cả nhà|Chào anh chị}, bên em {xả kho|thanh lý} giá tốt...)",
            ["Publisher.PhotoPlaceholder"] = "Chưa chọn ảnh đính kèm — bấm 'Chọn ảnh...' bên cạnh",
            ["Publisher.BtnPickPhoto"] = "Chọn ảnh...",
            ["Publisher.BtnSaveTemplate"] = "Lưu vào Kho Mẫu",
            ["Publisher.BtnDeleteTemplate"] = "Xóa mẫu đang chọn",
            ["Publisher.BtnPublishNow"] = "Bắt đầu Đăng bài Tự động",
            ["Publisher.TemplatesTitle"] = "Kho Mẫu Bài Viết & Kịch bản Đã lưu",
            ["Publisher.BumpTitle"] = "Tự động Đẩy Top (Up-Top) các bài đã đăng trong Nhóm",
            ["Publisher.BumpPlaceholder"] = "Nội dung bình luận đẩy bài (Spintax: {Chấm|.|Up bài|Hàng vẫn còn anh chị nhé|Quan tâm inbox em ạ})",
            ["Publisher.BtnBumpRecentPosts"] = "Đẩy Top các bài viết vừa đăng",

            ["Leads.Header"] = "Quét Khách hàng trong Nhóm & Tự động Comment Sale",
            ["Leads.Desc"] = "Quét bài viết mới nhất trong các nhóm mục tiêu, lọc theo từ khóa mua hàng và gửi Comment Sale kèm ảnh báo giá (tự động chống trùng lặp).",
            ["Leads.BuyerKeywordPlaceholder"] = "Từ khóa khách cần mua (VD: cần mua, xin giá, tư vấn, ib, tìm mua) — để trống để lấy tất cả bài mới",
            ["Leads.BtnScanGroupPosts"] = "Quét Bài viết mới từ Nhóm đang chọn",
            ["Leads.ScrapedPostsTitle"] = "Danh sách Bài viết thu thập được (Bấm chọn bài để Comment Sale)",
            ["Leads.SelectedPostBanner"] = "Bài viết đang chọn:",
            ["Leads.NoPostSelected"] = "Chưa chọn bài viết nào — hãy bấm chọn một bài viết ở danh sách bên trái",
            ["Leads.CommentContentPlaceholder"] = "Nội dung Comment Sale (Hỗ trợ Spintax: {Check inbox giúp em nhé|Em gửi bảng giá qua tin nhắn rồi ạ}!)",
            ["Leads.CommentPhotoPlaceholder"] = "Chưa chọn ảnh báo giá — bấm 'Chọn ảnh...' bên cạnh",
            ["Leads.BtnSendCommentSelected"] = "Gửi Comment Sale vào Bài đang chọn",
            ["Leads.BtnAutoCommentAllMatched"] = "Tự động Comment Sale tất cả bài khớp từ khóa",
            ["Leads.BtnOpenPostInBrowser"] = "Mở xem bài viết trên Trình duyệt",
            ["Leads.DefaultAuthor"] = "Thành viên trong nhóm",
            ["Leads.MediaOnlyPost"] = "(Bài viết đính kèm hình ảnh / video)",

            ["Settings.Header"] = "Cài đặt Hệ thống, Giao diện, Ngôn ngữ & Trình duyệt",
            ["Settings.Desc"] = "Tùy chỉnh giao diện Cloudflare (Dark / Light), ngôn ngữ hiển thị, chọn trình duyệt ưu tiên và giãn cách an toàn chống Checkpoint.",
            ["Settings.ThemeLabel"] = "Chế độ Giao diện (Theme)",
            ["Settings.ThemeDark"] = "Chế độ Tối (Cloudflare Dark)",
            ["Settings.ThemeLight"] = "Chế độ Sáng (Cloudflare Light)",
            ["Settings.LanguageLabel"] = "Ngôn ngữ hiển thị (Language)",
            ["Settings.BrowserEngineLabel"] = "Trình duyệt ưu tiên mở phiên (Browser Engine)",
            ["Settings.BrowserAuto"] = "Tự động nhận diện (Edge / Chrome / Brave)",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Đường dẫn trình duyệt tùy chỉnh (Tùy chọn — bấm 'Chọn tệp...')",
            ["Settings.HeadlessLabel"] = "Chạy trình duyệt ẩn (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Giãn cách an toàn giữa các lần đăng bài / bình luận (Giây: Tối thiểu — Tối đa)",
            ["Settings.MinDelayPrefix"] = "Tối thiểu (giây):",
            ["Settings.MaxDelayPrefix"] = "Tối đa (giây):",
            ["Settings.BtnSaveSettings"] = "Lưu Cấu hình Hệ thống",
            ["Settings.GuideTitle"] = "Hướng dẫn Sử dụng Nhanh (Không cần kỹ thuật)",
            ["Settings.GuideBody"] = "1. Kết nối Tài khoản: Vào mục 'Tài khoản & Phiên' -> Bấm 'Mở Trình duyệt Đăng nhập & Đồng bộ' -> Đăng nhập Facebook như bình thường. Phần mềm sẽ tự động lưu phiên và tải toàn bộ danh sách Nhóm của bạn.\n\n2. Nhập nhanh từ J2Team / Get Token Cookie: Nếu đã có tiện ích trên trình duyệt, chỉ cần Copy rồi bấm 'Dán 1-Click từ Clipboard'.\n\n3. Đăng bài & Săn khách: Chọn nhóm trong danh sách trực quan, phần mềm tự động giãn cách thời gian an toàn và chống bình luận trùng lặp.",

            ["Audit.TaskbarTitle"] = "Nhật ký Hoạt động (Audit Logs)",
            ["Audit.ToggleExpand"] = "Mở rộng Nhật ký ▲",
            ["Audit.ToggleCollapse"] = "Thu gọn Nhật ký ▼",
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
            ["Audit.BtnRefresh"] = "Lọc / Làm mới",
            ["Audit.BtnClear"] = "Xóa lịch sử cũ",
            ["Audit.ReadyTicker"] = "Sẵn sàng — Chưa có tác vụ nào đang chạy.",

            ["Common.BtnStopTask"] = "Dừng tác vụ đang chạy",
            ["Publisher.BtnPreviewSpintax"] = "Xem trước biến thể Spintax",
            ["Publisher.SpintaxPreviewPrefix"] = "Bản xem trước ngẫu nhiên:",
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

            ["Nav.Accounts"] = "Accounts & Sessions",
            ["Nav.Groups"] = "Groups & Communities",
            ["Nav.Publisher"] = "Publisher & Auto-Bump",
            ["Nav.SalesLeads"] = "Lead Scanner & Sales Comment",
            ["Nav.Settings"] = "Settings & Browser",

            ["Accounts.Header"] = "Facebook Account Vault",
            ["Accounts.Desc"] = "Log in directly via an isolated browser window or 1-click paste from J2Team Cookies / Get Token Cookie (fully automatic).",
            ["Accounts.NamePlaceholder"] = "Friendly account label (e.g., Main Sales Account 01)",
            ["Accounts.CookiePlaceholder"] = "Paste J2Team JSON or Cookie string (Optional — leave blank to log in interactively)",
            ["Accounts.BtnQuickImport"] = "Save / Import Session",
            ["Accounts.BtnPasteClipboard"] = "1-Click Paste from Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Open Browser to Log In & Sync",
            ["Accounts.BtnDelete"] = "Remove Selected Account",
            ["Accounts.LastActivePrefix"] = "Last active:",
            ["Accounts.ReadyToConnect"] = "Ready to launch browser",

            ["Groups.Header"] = "Joined Facebook Groups Catalog",
            ["Groups.Desc"] = "Automatically syncs all joined groups from your active account so you can visually pick targets for posting and lead scanning.",
            ["Groups.SearchKeywordPlaceholder"] = "Discover new groups by keyword (e.g., Real Estate Deals, Laptop Market...)",
            ["Groups.FilterPlaceholder"] = "Quick filter synced groups by name...",
            ["Groups.BtnSyncJoined"] = "Sync Joined Groups from Active Account",
            ["Groups.BtnSearchNew"] = "Search & Discover Groups",
            ["Groups.BtnJoinSelected"] = "Request to Join Group",
            ["Groups.BtnSaveTarget"] = "Add to Target Campaign Groups",
            ["Groups.TargetListTitle"] = "Target Campaign Groups (Check to enable)",
            ["Groups.BtnSelectAllTargets"] = "Enable All",
            ["Groups.BtnDeselectAllTargets"] = "Disable All",
            ["Groups.BtnRemoveTarget"] = "Remove Selected Target Group",

            ["Publisher.Header"] = "Automated Group, Timeline Publisher & Post Bumper",
            ["Publisher.Desc"] = "Compose Spintax {a|b|c} posts with product photos and publish or bump (Up-Top) posts across your target groups.",
            ["Publisher.TargetModeGroup"] = "Publish to Checked Target Groups",
            ["Publisher.TargetModePersonal"] = "Publish to Personal Timeline",
            ["Publisher.ContentPlaceholder"] = "Post content (Supports Spintax: {Hello everyone|Hi friends}, check out our {new arrivals|special deal}...)",
            ["Publisher.PhotoPlaceholder"] = "No photo selected — click 'Browse Photo...' next to this box",
            ["Publisher.BtnPickPhoto"] = "Browse Photo...",
            ["Publisher.BtnSaveTemplate"] = "Save to Library",
            ["Publisher.BtnDeleteTemplate"] = "Delete Selected Template",
            ["Publisher.BtnPublishNow"] = "Start Automated Publishing",
            ["Publisher.TemplatesTitle"] = "Saved Content Library & Templates",
            ["Publisher.BumpTitle"] = "Auto Up-Top (Bump) Recently Published Group Posts",
            ["Publisher.BumpPlaceholder"] = "Bump comment text (Spintax: {Up|.|Still available|DM me for special pricing})",
            ["Publisher.BtnBumpRecentPosts"] = "Bump Recent Group Posts Now",

            ["Leads.Header"] = "Group Lead Scanner & Auto Sales Commenter",
            ["Leads.Desc"] = "Scan recent posts inside target groups, filter by buyer-intent keywords, and drop sales comments with product photos (auto-deduplicated).",
            ["Leads.BuyerKeywordPlaceholder"] = "Buyer-intent keywords (e.g., looking to buy, price, dm me, need advice) — leave blank for all recent posts",
            ["Leads.BtnScanGroupPosts"] = "Scan Recent Posts in Selected Group",
            ["Leads.ScrapedPostsTitle"] = "Discovered Group Posts (Click any card to select for Sales Commenting)",
            ["Leads.SelectedPostBanner"] = "Selected Post:",
            ["Leads.NoPostSelected"] = "No post selected — click any post card on the left to target it",
            ["Leads.CommentContentPlaceholder"] = "Sales comment text (Supports Spintax: {Please check your inbox|Sent you full pricing via DM}!)",
            ["Leads.CommentPhotoPlaceholder"] = "No product photo selected — click 'Browse Photo...'",
            ["Leads.BtnSendCommentSelected"] = "Post Sales Comment on Selected Post",
            ["Leads.BtnAutoCommentAllMatched"] = "Auto-Comment on All Keyword-Matched Posts",
            ["Leads.BtnOpenPostInBrowser"] = "Open Selected Post in Browser",
            ["Leads.DefaultAuthor"] = "Group Member",
            ["Leads.MediaOnlyPost"] = "(Post contains photo / video attachment)",

            ["Settings.Header"] = "System Settings, Appearance, Language & Browser Engine",
            ["Settings.Desc"] = "Configure Cloudflare Dark/Light theme, display language (vi-VN / en-US), preferred Chromium browser, and anti-checkpoint delays.",
            ["Settings.ThemeLabel"] = "Appearance Theme",
            ["Settings.ThemeDark"] = "Cloudflare Dark Mode",
            ["Settings.ThemeLight"] = "Cloudflare Light Mode",
            ["Settings.LanguageLabel"] = "Display Language",
            ["Settings.BrowserEngineLabel"] = "Preferred Chromium Browser Engine",
            ["Settings.BrowserAuto"] = "Auto-Detect (Edge / Chrome / Brave)",
            ["Settings.BrowserEdge"] = "Microsoft Edge",
            ["Settings.BrowserChrome"] = "Google Chrome",
            ["Settings.BrowserBrave"] = "Brave Browser",
            ["Settings.BrowserCustomPathPlaceholder"] = "Custom browser executable path (Optional — click 'Browse...')",
            ["Settings.HeadlessLabel"] = "Run browser in background (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Safe human-like delay between posts / comments (Seconds: Min — Max)",
            ["Settings.MinDelayPrefix"] = "Min delay (sec):",
            ["Settings.MaxDelayPrefix"] = "Max delay (sec):",
            ["Settings.BtnSaveSettings"] = "Save System Settings",
            ["Settings.GuideTitle"] = "Quick Start Guide (Zero Technical Setup)",
            ["Settings.GuideBody"] = "1. Connect Account: Go to 'Accounts & Sessions' -> Click 'Open Browser to Log In & Sync' -> Sign in to Facebook normally. The app automatically saves your session and syncs all your joined Groups.\n\n2. 1-Click J2Team / Cookie Import: Copy cookies from your browser extension and click '1-Click Paste from Clipboard'.\n\n3. Publish & Close Sales Leads: Pick groups visually from your catalog; the app automatically enforces human-like delays and prevents duplicate comments.",

            ["Audit.TaskbarTitle"] = "Activity & Audit Logs",
            ["Audit.ToggleExpand"] = "Expand Audit Logs ▲",
            ["Audit.ToggleCollapse"] = "Collapse Audit Logs ▼",
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
            ["Audit.BtnRefresh"] = "Filter / Refresh",
            ["Audit.BtnClear"] = "Clear Old Logs",
            ["Audit.ReadyTicker"] = "Ready — No active automation tasks running.",

            ["Common.BtnStopTask"] = "Stop Active Task",
            ["Publisher.BtnPreviewSpintax"] = "Preview Spintax Variation",
            ["Publisher.SpintaxPreviewPrefix"] = "Randomized preview:",
            ["Log.TaskStoppedByUser"] = "Task stopped immediately as requested by user.",
            ["Log.SelectAccountFirst"] = "Please select or add an account before running this task.",
            ["Log.ConnectionInterrupted"] = "Connection temporarily interrupted, please try again."
        };
}
