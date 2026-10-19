// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace Facebook.Net.UI.Core.Localization;

/// <summary>
/// Partial implementation of <see cref="LocalizationService"/> containing complete <c>vi-VN</c> and <c>en-US</c> translation dictionaries.
/// Zero developer jargon (never asks end users for raw UIDs, UUIDs, or Group IDs).
/// </summary>
public sealed partial class LocalizationService
{
    private static Dictionary<string, string> CreateVietnameseCatalog() =>
        new(StringComparer.Ordinal)
        {
            ["App.Title"] = "Facebook.Net — Hệ thống Bán hàng, Đăng Nhóm & Seeding Tự động",
            ["App.Subtitle"] = "Quản lý đa tài khoản, tự động đồng bộ nhóm, đăng bài & comment sale thông minh",

            ["Nav.Accounts"] = "Tài khoản & Phiên",
            ["Nav.Groups"] = "Nhóm & Cộng đồng",
            ["Nav.Publisher"] = "Đăng bài & Đẩy Top",
            ["Nav.SalesLeads"] = "Săn Khách & Comment Sale",
            ["Nav.Settings"] = "Cài đặt & Trình duyệt",

            ["Accounts.Header"] = "Quản lý Tài khoản Facebook",
            ["Accounts.Desc"] = "Đăng nhập trực tiếp trên trình duyệt hoặc dán nhanh từ J2Team Cookies / Get Token Cookie (không cần nhập ID thủ công).",
            ["Accounts.NamePlaceholder"] = "Tên gợi nhớ (VD: Nick Bán Hàng BĐS 01)",
            ["Accounts.CookiePlaceholder"] = "Dán JSON J2Team Cookies hoặc chuỗi Cookie (Tùy chọn — có thể để trống để đăng nhập trên trình duyệt)",
            ["Accounts.BtnQuickImport"] = "Nhập nhanh phiên / Cookie",
            ["Accounts.BtnPasteClipboard"] = "Dán 1-Click từ Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Mở Trình duyệt Đăng nhập & Đồng bộ",
            ["Accounts.BtnDelete"] = "Gỡ tài khoản",
            ["Accounts.EmptyHint"] = "Chưa có tài khoản nào. Bấm 'Mở Trình duyệt Đăng nhập' hoặc 'Dán 1-Click từ Clipboard' để bắt đầu.",

            ["Groups.Header"] = "Danh mục Nhóm Facebook đã tham gia",
            ["Groups.Desc"] = "Tự động lấy toàn bộ danh sách nhóm của tài khoản để bạn tích chọn đăng bài hoặc quét khách hàng.",
            ["Groups.SearchKeywordPlaceholder"] = "Tìm nhóm mới theo từ khóa (VD: Chợ cư dân Vinhomes, Mua bán Laptop...)",
            ["Groups.FilterPlaceholder"] = "Lọc nhanh trong danh sách nhóm đã tham gia...",
            ["Groups.BtnSyncJoined"] = "Đồng bộ Nhóm từ Tài khoản đang chọn",
            ["Groups.BtnSearchNew"] = "Tìm & Khám phá Nhóm mới",
            ["Groups.BtnJoinSelected"] = "Tham gia Nhóm này",
            ["Groups.BtnSaveTarget"] = "Đưa vào Danh sách Nhóm Mục tiêu",
            ["Groups.TargetListTitle"] = "Danh sách Nhóm Mục tiêu đã chọn để chạy chiến dịch",

            ["Publisher.Header"] = "Chiến dịch Đăng bài Nhóm & Trang cá nhân",
            ["Publisher.Desc"] = "Soạn nội dung xoay vòng Spintax {a|b|c}, đính kèm ảnh/video sản phẩm và đăng tự động vào các nhóm đã chọn.",
            ["Publisher.TargetModeGroup"] = "Đăng vào các Nhóm Mục tiêu đã chọn",
            ["Publisher.TargetModePersonal"] = "Đăng lên Trang cá nhân của tài khoản",
            ["Publisher.ContentPlaceholder"] = "Nội dung bài đăng (Hỗ trợ Spintax: {Xin chào cả nhà|Chào anh chị}, bên em {xả kho|thanh lý} giá tốt...)",
            ["Publisher.PhotoPlaceholder"] = "Đường dẫn ảnh đính kèm (Tùy chọn — VD: C:\\Images\\san_pham.jpg)",
            ["Publisher.BtnSaveTemplate"] = "Lưu vào Kho Mẫu Bài Viết",
            ["Publisher.BtnPublishNow"] = "Bắt đầu Đăng bài Tự động",
            ["Publisher.TemplatesTitle"] = "Kho Mẫu Bài Viết & Kịch bản Đã lưu",

            ["Leads.Header"] = "Quét Khách hàng trong Nhóm & Tự động Comment Sale",
            ["Leads.Desc"] = "Quét bài viết mới nhất trong các nhóm mục tiêu, lọc theo từ khóa mua hàng và gửi Comment Sale kèm ảnh báo giá (tự động chống trùng lặp).",
            ["Leads.BuyerKeywordPlaceholder"] = "Từ khóa khách cần mua (VD: cần mua, xin giá, tư vấn, ib, tìm mua) — để trống để lấy tất cả bài mới",
            ["Leads.BtnScanGroupPosts"] = "Quét Bài viết mới từ Nhóm đang chọn",
            ["Leads.ScrapedPostsTitle"] = "Danh sách Bài viết thu thập được (Bấm chọn bài để Comment Sale)",
            ["Leads.SelectedPostBanner"] = "Bài viết đang chọn để Comment Sale:",
            ["Leads.NoPostSelected"] = "Chưa chọn bài viết nào — hãy bấm chọn một bài viết ở danh sách bên trái",
            ["Leads.CommentContentPlaceholder"] = "Nội dung Comment Sale (Hỗ trợ Spintax: {Check inbox giúp em nhé|Em gửi bảng giá qua tin nhắn rồi ạ}!)",
            ["Leads.CommentPhotoPlaceholder"] = "Đường dẫn ảnh sản phẩm / bảng giá đính kèm Comment (Tùy chọn)",
            ["Leads.BtnSendCommentSelected"] = "Gửi Comment Sale vào Bài đang chọn",
            ["Leads.BtnAutoCommentAllMatched"] = "Tự động Comment Sale tất cả bài khớp từ khóa",

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
            ["Settings.BrowserCustomPathPlaceholder"] = "Đường dẫn file thực thi trình duyệt tùy chỉnh (Tùy chọn)",
            ["Settings.HeadlessLabel"] = "Chạy trình duyệt ẩn (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Giãn cách an toàn giữa các hành động (Giây: Tối thiểu - Tối đa)",
            ["Settings.BtnSaveSettings"] = "Lưu Cấu hình Hệ thống",
            ["Settings.GuideTitle"] = "Hướng dẫn Kết nối Nhanh (Không cần kỹ thuật)",
            ["Settings.GuideBody"] = "Cách 1 (Khuyên dùng): Bấm 'Mở Trình duyệt Đăng nhập' ở mục Tài khoản -> Đăng nhập Facebook như bình thường -> Phần mềm tự động lưu phiên và tải danh sách Nhóm.\nCách 2: Dùng tiện ích J2Team Cookies hoặc Get Token Cookie trên trình duyệt đang dùng -> Copy -> Bấm 'Dán 1-Click từ Clipboard'.",

            ["Audit.TaskbarTitle"] = "Nhật ký Hoạt động & Kiểm toán (Audit Logs)",
            ["Audit.ToggleExpand"] = "Mở rộng Nhật ký ▲",
            ["Audit.ToggleCollapse"] = "Thu gọn Nhật ký ▼",
            ["Audit.FilterCategoryAll"] = "Tất cả sự kiện",
            ["Audit.FilterAccountAll"] = "Tất cả tài khoản",
            ["Audit.SearchPlaceholder"] = "Tìm trong nhật ký (tên nhóm, nội dung, trạng thái...)",
            ["Audit.BtnRefresh"] = "Làm mới",
            ["Audit.BtnClear"] = "Xóa lịch sử cũ"
        };

    private static Dictionary<string, string> CreateEnglishCatalog() =>
        new(StringComparer.Ordinal)
        {
            ["App.Title"] = "Facebook.Net — Automated Group Publishing & Sales Seeding Suite",
            ["App.Subtitle"] = "Multi-account vault, visual group discovery, smart publishing & sales commenting",

            ["Nav.Accounts"] = "Accounts & Sessions",
            ["Nav.Groups"] = "Groups & Communities",
            ["Nav.Publisher"] = "Group & Feed Publisher",
            ["Nav.SalesLeads"] = "Lead Scanner & Sales Comment",
            ["Nav.Settings"] = "Settings & Browser",

            ["Accounts.Header"] = "Facebook Account Vault",
            ["Accounts.Desc"] = "Log in directly via an isolated browser window or 1-click paste from J2Team Cookies / Get Token Cookie (zero manual IDs).",
            ["Accounts.NamePlaceholder"] = "Friendly account label (e.g., Main Sales Account 01)",
            ["Accounts.CookiePlaceholder"] = "Paste J2Team JSON or Cookie string (Optional — leave blank to log in interactively)",
            ["Accounts.BtnQuickImport"] = "Save / Import Session",
            ["Accounts.BtnPasteClipboard"] = "1-Click Paste from Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Open Browser to Log In & Sync",
            ["Accounts.BtnDelete"] = "Remove Account",
            ["Accounts.EmptyHint"] = "No accounts added yet. Click 'Open Browser to Log In' or '1-Click Paste from Clipboard' to start.",

            ["Groups.Header"] = "Joined Facebook Groups Catalog",
            ["Groups.Desc"] = "Automatically syncs all joined groups from your active account so you can visually pick targets for posting and lead scanning.",
            ["Groups.SearchKeywordPlaceholder"] = "Discover new groups by keyword (e.g., Real Estate Deals, Laptop Market...)",
            ["Groups.FilterPlaceholder"] = "Filter synced groups by name...",
            ["Groups.BtnSyncJoined"] = "Sync Joined Groups from Active Account",
            ["Groups.BtnSearchNew"] = "Search & Discover Groups",
            ["Groups.BtnJoinSelected"] = "Request to Join Group",
            ["Groups.BtnSaveTarget"] = "Add to Target Campaign Groups",
            ["Groups.TargetListTitle"] = "Selected Target Groups for Automation",

            ["Publisher.Header"] = "Automated Group & Timeline Publisher",
            ["Publisher.Desc"] = "Compose Spintax {a|b|c} posts with product photos/videos and publish across your selected target groups.",
            ["Publisher.TargetModeGroup"] = "Publish to Selected Target Groups",
            ["Publisher.TargetModePersonal"] = "Publish to Personal Timeline",
            ["Publisher.ContentPlaceholder"] = "Post content (Supports Spintax: {Hello everyone|Hi friends}, check out our {new arrivals|special deal}...)",
            ["Publisher.PhotoPlaceholder"] = "Optional local photo path (e.g., C:\\Images\\product.jpg)",
            ["Publisher.BtnSaveTemplate"] = "Save to Content Library",
            ["Publisher.BtnPublishNow"] = "Start Automated Publishing",
            ["Publisher.TemplatesTitle"] = "Saved Content Library & Templates",

            ["Leads.Header"] = "Group Lead Scanner & Auto Sales Commenter",
            ["Leads.Desc"] = "Scan recent posts inside target groups, filter by buyer-intent keywords, and drop sales comments with product photos (auto-deduplicated).",
            ["Leads.BuyerKeywordPlaceholder"] = "Buyer-intent keywords (e.g., looking to buy, price, dm me, need advice) — leave blank for all recent posts",
            ["Leads.BtnScanGroupPosts"] = "Scan Recent Posts in Selected Group",
            ["Leads.ScrapedPostsTitle"] = "Discovered Group Posts (Click any card to select for Sales Commenting)",
            ["Leads.SelectedPostBanner"] = "Selected Post for Sales Comment:",
            ["Leads.NoPostSelected"] = "No post selected — click any post card on the left to target it",
            ["Leads.CommentContentPlaceholder"] = "Sales comment text (Supports Spintax: {Please check your inbox|Sent you full pricing via DM}!)",
            ["Leads.CommentPhotoPlaceholder"] = "Optional product/price photo path for comment attachment",
            ["Leads.BtnSendCommentSelected"] = "Post Sales Comment on Selected Post",
            ["Leads.BtnAutoCommentAllMatched"] = "Auto-Comment on All Keyword-Matched Posts",

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
            ["Settings.BrowserCustomPathPlaceholder"] = "Custom browser executable path (Optional)",
            ["Settings.HeadlessLabel"] = "Run browser in background (Headless Mode)",
            ["Settings.DelayRangeLabel"] = "Safe human-like delay between actions (Seconds: Min - Max)",
            ["Settings.BtnSaveSettings"] = "Save System Settings",
            ["Settings.GuideTitle"] = "Quick Start Guide (Zero Technical Setup)",
            ["Settings.GuideBody"] = "Method 1 (Recommended): Click 'Open Browser to Log In' in Accounts -> Sign in to Facebook normally -> The app automatically extracts your session and syncs your Groups.\nMethod 2: Export cookies using J2Team Cookies or Get Token Cookie -> Click '1-Click Paste from Clipboard'.",

            ["Audit.TaskbarTitle"] = "Activity & Audit Log Taskbar",
            ["Audit.ToggleExpand"] = "Expand Audit Logs ▲",
            ["Audit.ToggleCollapse"] = "Collapse Audit Logs ▼",
            ["Audit.FilterCategoryAll"] = "All Categories",
            ["Audit.FilterAccountAll"] = "All Accounts",
            ["Audit.SearchPlaceholder"] = "Search audit logs (group name, post snippet, status...)",
            ["Audit.BtnRefresh"] = "Refresh",
            ["Audit.BtnClear"] = "Clear Old Logs"
        };
}
