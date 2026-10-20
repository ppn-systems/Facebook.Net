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
            ["App.Title"] = "Facebook.Net — Hệ thống Quản lý Bán hàng & Chăm sóc Cộng đồng",
            ["App.Subtitle"] = "Quản lý đa tài khoản, đồng bộ nhóm, đăng bài và chăm sóc khách hàng tự động",
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
            ["Nav.SalesLeads"] = "Khách hàng",
            ["Nav.Settings"] = "Cài đặt",
            ["Nav.AuditLogs"] = "Nhật ký",

            ["Sidebar.Collapse"] = "Thu gọn thanh điều hướng",
            ["Sidebar.Expand"] = "Mở rộng thanh điều hướng",
            ["Overview.ModeLabel"] = "Tài khoản:",
            ["Overview.StatusConnected"] = "Đã sẵn sàng",
            ["Overview.StatusDisconnected"] = "Chưa kết nối",
            ["Overview.StatusRunning"] = "Đang chạy tác vụ",
            ["Overview.SubtextConnected"] = "Phiên trình duyệt đã kết nối và sẵn sàng thực hiện chiến dịch.",
            ["Overview.SubtextDisconnected"] = "Chọn một tài khoản hoặc mở trình duyệt để bắt đầu kết nối.",
            ["Overview.InfoTip"] = "Bảng điều khiển trung tâm hiển thị chỉ số trực tiếp, trạng thái sẵn sàng của chiến dịch và nhật ký hoạt động thời gian thực.",
            ["Overview.SessionCardTitle"] = "Trạng thái phiên & Kiểm tra sẵn sàng",
            ["Overview.RecentLogsTitle"] = "Hoạt động gần đây",
            ["Overview.CheckAccountReady"] = "Tài khoản đã kết nối sẵn sàng",
            ["Overview.CheckAccountMissing"] = "Chưa kết nối tài khoản hoạt động",
            ["Overview.CheckGroupsReady"] = "Đã kích hoạt nhóm mục tiêu",
            ["Overview.CheckGroupsMissing"] = "Chưa bật nhóm mục tiêu nào",
            ["Overview.CheckDelayReady"] = "Giãn cách an toàn",
            ["Overview.QuickActionsTitle"] = "Thao tác nhanh",
            ["Overview.QuickOpenBrowser"] = "Mở trình duyệt",

            ["Accounts.Header"] = "Quản lý Tài khoản",
            ["Accounts.Desc"] = "Đăng nhập trực tiếp trên trình duyệt hoặc nhập dữ liệu phiên đăng nhập để tự động đồng bộ danh sách nhóm.",
            ["Accounts.SearchPlaceholder"] = "Tìm kiếm theo tên tài khoản...",
            ["Accounts.NamePlaceholder"] = "Tên gợi nhớ cho tài khoản",
            ["Accounts.CookiePlaceholder"] = "Dán dữ liệu phiên đăng nhập vào đây",
            ["Accounts.BtnQuickImport"] = "Lưu phiên đăng nhập",
            ["Accounts.BtnPasteClipboard"] = "Dán từ bộ nhớ tạm",
            ["Accounts.BtnOpenBrowserLogin"] = "Mở trình duyệt & Đồng bộ",
            ["Accounts.BtnDelete"] = "Gỡ tài khoản",
            ["Accounts.LastActivePrefix"] = "Hoạt động gần nhất:",
            ["Accounts.ReadyToConnect"] = "Sẵn sàng mở trình duyệt",

            ["Groups.Header"] = "Danh mục Nhóm",
            ["Groups.Desc"] = "Tự động lấy toàn bộ danh sách nhóm của tài khoản để bạn tích chọn đăng bài hoặc thu thập khách hàng.",
            ["Groups.SearchKeywordPlaceholder"] = "Nhập tên nhóm cần tìm kiếm...",
            ["Groups.FilterPlaceholder"] = "Lọc nhanh theo tên nhóm...",
            ["Groups.BtnSyncJoined"] = "Đồng bộ Nhóm",
            ["Groups.BtnSearchNew"] = "Tìm Nhóm mới",
            ["Groups.BtnJoinSelected"] = "Tham gia",
            ["Groups.BtnSaveTarget"] = "Thêm vào nhóm đích",
            ["Groups.TargetListTitle"] = "Danh sách Nhóm Đích",
            ["Groups.BtnSelectAllTargets"] = "Bật tất cả",
            ["Groups.BtnDeselectAllTargets"] = "Tắt tất cả",
            ["Groups.BtnRemoveTarget"] = "Gỡ khỏi nhóm đích",

            ["Publisher.Header"] = "Chiến dịch Đăng bài & Đẩy bài viết",
            ["Publisher.Desc"] = "Soạn nội dung đa dạng, chọn ảnh sản phẩm và đăng tự động hoặc bình luận đẩy bài lên đầu nhóm.",
            ["Publisher.TargetModeGroup"] = "Đăng vào nhóm mục tiêu",
            ["Publisher.TargetModePersonal"] = "Đăng trang cá nhân",
            ["Publisher.ContentPlaceholder"] = "Nhập nội dung bài đăng, hỗ trợ cú pháp xoay vòng nội dung {lựa chọn 1|lựa chọn 2}...",
            ["Publisher.PhotoPlaceholder"] = "Chưa chọn ảnh đính kèm",
            ["Publisher.BtnPickPhoto"] = "Chọn ảnh...",
            ["Publisher.BtnSaveTemplate"] = "Lưu mẫu",
            ["Publisher.BtnDeleteTemplate"] = "Xóa mẫu đang chọn",
            ["Publisher.BtnPublishNow"] = "Đăng bài tự động",
            ["Publisher.TemplatesTitle"] = "Kho mẫu nội dung đã lưu",
            ["Publisher.BumpTitle"] = "Đẩy bài viết lên đầu nhóm",
            ["Publisher.BumpPlaceholder"] = "Nhập nội dung bình luận đẩy bài...",
            ["Publisher.BtnBumpRecentPosts"] = "Đẩy bài ngay",

            ["Leads.Header"] = "Thu thập Bài viết & Tư vấn Khách hàng",
            ["Leads.Desc"] = "Thu thập bài viết mới nhất từ các nhóm vào kho lưu trữ và lọc khách hàng tiềm năng tức thì.",
            ["Leads.BuyerKeywordPlaceholder"] = "Nhập từ khóa để lọc bài viết hoặc tên tác giả...",
            ["Leads.BtnScanGroupPosts"] = "Thu thập ngay",
            ["Leads.BtnConfigScraper"] = "Cấu hình thu thập",
            ["Leads.ConfigTitle"] = "Thiết lập Thu thập Bài viết",
            ["Leads.ConfigKeywordsLabel"] = "Từ khóa lọc bài khi thu thập, bỏ trống nếu lấy toàn bộ",
            ["Leads.ConfigKeywordsPlaceholder"] = "Ví dụ: cần mua, tìm nguồn hàng, tư vấn, báo giá...",
            ["Leads.ConfigPostsPerGroupLabel"] = "Số bài mỗi nhóm",
            ["Leads.ConfigIntervalMinutesLabel"] = "Chu kỳ lặp lại theo phút",
            ["Leads.ConfigAllGroupsToggle"] = "Thu thập toàn bộ các nhóm đích đang bật",
            ["Leads.ConfigAutoBackgroundToggle"] = "Tự động thu thập định kỳ dưới nền",
            ["Leads.BtnClearScrapedDb"] = "Xóa bài lưu trữ cũ",
            ["Leads.FilterAllGroups"] = "Tất cả các nhóm đã thu thập",
            ["Leads.ScrapedPostsTitle"] = "Bài viết khách hàng thu thập được",
            ["Leads.SelectedPostBanner"] = "Bài đang chọn:",
            ["Leads.NoPostSelected"] = "Chưa chọn bài viết — hãy bấm chọn một dòng trong bảng",
            ["Leads.CommentContentPlaceholder"] = "Nhập nội dung bình luận tư vấn khách hàng...",
            ["Leads.CommentPhotoPlaceholder"] = "Chưa chọn ảnh minh họa hoặc bảng giá",
            ["Leads.BtnSendCommentSelected"] = "Bình luận bài đang chọn",
            ["Leads.BtnAutoCommentAllMatched"] = "Tự động bình luận tất cả",
            ["Leads.BtnOpenPostInBrowser"] = "Xem bài viết gốc",
            ["Leads.DefaultAuthor"] = "Thành viên nhóm",
            ["Leads.MediaOnlyPost"] = "Bài viết đính kèm hình ảnh hoặc video",
            ["Leads.MediaVideoBadge"] = "Có video",
            ["Leads.MediaPhotoBadge"] = "Có hình ảnh",

            ["Settings.Header"] = "Giao diện & Trình duyệt",
            ["Settings.Desc"] = "Tùy chỉnh chế độ hiển thị, ngôn ngữ và trình duyệt điều khiển. Mọi thay đổi được lưu tự động.",
            ["Settings.SafetyTitle"] = "Tốc độ Thao tác & Bảo vệ Tài khoản",
            ["Settings.SafetyDesc"] = "Thiết lập thời gian giãn cách tự nhiên giữa các thao tác để tài khoản hoạt động ổn định và an toàn lâu dài.",
            ["Settings.ThemeLabel"] = "Chế độ giao diện",
            ["Settings.ThemeDark"] = "Giao diện tối",
            ["Settings.ThemeLight"] = "Giao diện sáng",
            ["Settings.LanguageLabel"] = "Ngôn ngữ hiển thị",
            ["Settings.BrowserEngineLabel"] = "Trình duyệt ưu tiên",
            ["Settings.BrowserAuto"] = "Tự động nhận diện trình duyệt",
            ["Settings.BrowserEdge"] = "Trình duyệt Microsoft Edge",
            ["Settings.BrowserChrome"] = "Trình duyệt Google Chrome",
            ["Settings.BrowserBrave"] = "Trình duyệt Brave",
            ["Settings.BrowserCustomPathPlaceholder"] = "Đường dẫn tệp thực thi trình duyệt riêng",
            ["Settings.HeadlessLabel"] = "Chạy trình duyệt ẩn dưới nền để tiết kiệm bộ nhớ",
            ["Settings.DelayRangeLabel"] = "Thời gian nghỉ giữa các lần đăng bài và bình luận tính theo giây",
            ["Settings.MinDelayPrefix"] = "Từ:",
            ["Settings.MaxDelayPrefix"] = "Đến:",
            ["Settings.BtnSaveSettings"] = "Lưu thiết lập",
            ["Settings.BtnOpenAuditLogs"] = "Xem nhật ký hoạt động",
            ["Audit.BtnBackToSettings"] = "Quay lại cài đặt",
            ["Settings.GuideTitle"] = "Tốc độ Thao tác & Bảo vệ Tài khoản",
            ["Settings.GuideBody"] = "Thiết lập thời gian giãn cách tự nhiên giữa các thao tác để tài khoản hoạt động ổn định và an toàn lâu dài.",

            ["Audit.TaskbarTitle"] = "Nhật ký Hoạt động Hệ thống",
            ["Audit.ToggleExpand"] = "Mở rộng nhật ký",
            ["Audit.ToggleCollapse"] = "Thu gọn nhật ký",
            ["Audit.FilterCategoryAll"] = "Tất cả sự kiện",
            ["Audit.Cat.System"] = "Hệ thống",
            ["Audit.Cat.Account"] = "Tài khoản",
            ["Audit.Cat.Group"] = "Nhóm",
            ["Audit.Cat.Post"] = "Đăng bài",
            ["Audit.Cat.SalesComment"] = "Bình luận",
            ["Audit.Cat.Scrape"] = "Thu thập",
            ["Audit.Time.All"] = "Mọi thời gian",
            ["Audit.Time.LastHour"] = "1 giờ qua",
            ["Audit.Time.Today24h"] = "Trong 24 giờ qua",
            ["Audit.Time.Last7Days"] = "7 ngày qua",
            ["Audit.FilterAccountAll"] = "Tất cả tài khoản",
            ["Audit.SearchPlaceholder"] = "Nhập từ khóa cần tìm trong nhật ký...",
            ["Audit.BtnRefresh"] = "Làm mới",
            ["Audit.BtnClear"] = "Xóa nhật ký cũ",
            ["Audit.ReadyTicker"] = "Hệ thống sẵn sàng — Chưa có tác vụ nào đang chạy.",

            ["Common.BtnStopTask"] = "Dừng tác vụ",
            ["Publisher.BtnPreviewSpintax"] = "Xem thử nội dung",
            ["Publisher.SpintaxPreviewPrefix"] = "Nội dung xem trước:",
            ["Publisher.DefaultPostSpintax"] = "{Xin chào cả nhà|Chào anh chị em trong nhóm}! Bên mình {đang sẵn hàng|mới về lô hàng} chính hãng giá ưu đãi, {liên hệ|nhắn tin} nhận báo giá chi tiết nhé!",
            ["Publisher.DefaultBumpSpintax"] = "{Chấm đẩy bài ạ|Hàng vẫn sẵn kho anh chị nhé|Quan tâm nhắn tin giúp em nhé|Mời anh chị tham khảo ạ}!",
            ["Leads.DefaultSalesCommentSpintax"] = "{Anh chị kiểm tra tin nhắn giúp em nhé|Em đã gửi báo giá chi tiết qua tin nhắn rồi ạ|Anh chị xem tin nhắn chờ giúp em nhé}!",
            ["Accounts.ProxyPlaceholder"] = "Nhập địa chỉ máy chủ trung gian theo định dạng địa_chỉ:cổng",
            ["Accounts.BtnSaveProxy"] = "Lưu kết nối mạng",
            ["Accounts.BtnCheckHealth"] = "Kiểm tra kết nối",
            ["Accounts.DefaultBrowserLoginName"] = "Tài khoản trình duyệt",
            ["Accounts.DefaultImportedName"] = "Tài khoản",
            ["Accounts.ProxyBadgePrefix"] = "Kết nối qua máy chủ riêng",
            ["Accounts.NoProxyBadge"] = "Kết nối mạng trực tiếp",
            ["Groups.BtnAutoJoinDiscovered"] = "Tự động tham gia",
            ["Leads.IntentHigh"] = "Nhu cầu cao",
            ["Leads.IntentWarm"] = "Quan tâm",
            ["Kpi.AccountsTitle"] = "TÀI KHOẢN SẴN SÀNG",
            ["Kpi.GroupsTitle"] = "NHÓM ĐÍCH ĐANG BẬT",
            ["Kpi.LeadsTitle"] = "BÀI VIẾT THU THẬP",
            ["Kpi.TelemetryTitle"] = "TÁC VỤ THÀNH CÔNG",
            ["Empty.Accounts"] = "Chưa có tài khoản nào — hãy bấm Mở trình duyệt & Đồng bộ hoặc nhập phiên đăng nhập.",
            ["Empty.Groups"] = "Chưa có nhóm nào — hãy bấm Đồng bộ Nhóm hoặc tìm kiếm theo từ khóa.",
            ["Empty.TargetGroups"] = "Chưa chọn nhóm đích — hãy chọn nhóm từ danh mục để đưa vào chiến dịch.",
            ["Empty.Templates"] = "Chưa có mẫu nội dung lưu sẵn — hãy soạn bài viết và bấm Lưu mẫu.",
            ["Empty.Leads"] = "Kho lưu trữ chưa có bài viết phù hợp — hãy bấm Thu thập bài mới hoặc bật tự động thu thập định kỳ.",
            ["Empty.AuditLogs"] = "Chưa có nhật ký hoạt động nào khớp với bộ lọc hiện tại.",
            ["Accounts.BtnWarmUp"] = "Tương tác tự nhiên",
            ["Leads.AutoReactToggle"] = "Tự động bày tỏ cảm xúc trước khi gửi bình luận",
            ["Publisher.AutoReactOwnPost"] = "Tự động thả tim bài viết vừa đăng",
            ["Publisher.ScheduleDelayLabel"] = "Hẹn giờ đăng sau số phút:",
            ["Log.TaskStoppedByUser"] = "Đã dừng tác vụ theo yêu cầu của người dùng.",
            ["Log.SelectAccountFirst"] = "Vui lòng chọn hoặc thêm một tài khoản trước khi thực hiện tác vụ.",
            ["Log.ConnectionInterrupted"] = "Kết nối tạm thời bị gián đoạn, vui lòng thử lại.",

            ["Leads.BtnScanPosts"] = "Thu thập bài mới",
            ["Leads.EmptyPostText"] = "Bài viết đính kèm hình ảnh hoặc video",
            ["Leads.TimeRecent"] = "Mới đây",
            ["Leads.StatusCommented"] = "✓ Đã bình luận",
            ["Leads.StatusPending"] = "● Chưa bình luận",
            ["Leads.FilterSegAll"] = "Tất cả",
            ["Leads.FilterSegPending"] = "Chưa bình luận",
            ["Leads.FilterSegCommented"] = "Đã bình luận",
            ["Leads.FilterGroupLabel"] = "Nhóm đã thu thập",
            ["Leads.FilterKeywordLabel"] = "Từ khóa lọc bài viết trong kho lưu trữ",
            ["Leads.TableTitlePrefix"] = "Tổng số bài viết hiển thị",
            ["Leads.ColAuthor"] = "NGƯỜI ĐĂNG",
            ["Leads.ColGroup"] = "NHÓM",
            ["Leads.ColMessage"] = "NỘI DUNG & HÌNH ẢNH",
            ["Leads.ColIntent"] = "MỨC ĐỘ QUAN TÂM",
            ["Leads.ColTime"] = "THỜI GIAN",
            ["Leads.ColStatus"] = "TRẠNG THÁI",
            ["Leads.TabRepository"] = "Kho Bài viết & Lọc khách",
            ["Leads.TabScraperConfig"] = "Chọn Nhóm & Cấu hình Thu thập",
            ["Leads.TabAutoComment"] = "Bình luận Tư vấn Tự động",
            ["Leads.ConfigMultiGroupTitle"] = "Danh sách Nhóm Thu thập Bài viết",
            ["Leads.ConfigMultiGroupDesc"] = "Tích chọn các nhóm cần thu thập bài viết mới. Hệ thống tự động chia đợt xoay vòng và nghỉ giãn cách an toàn.",
            ["Leads.ConfigRateLimitLabel"] = "Thời gian nghỉ giữa hai nhóm khi thu thập tính theo giây",
            ["Leads.ConfigMaxGroupsPerCycleLabel"] = "Số nhóm tối đa mỗi đợt",
            ["Leads.ConfigChronologicalBadge"] = "Luôn ưu tiên lấy bài viết vừa đăng mới nhất",

            ["Accounts.TabVault"] = "Danh sách Tài khoản",
            ["Accounts.TabImportAndProxy"] = "Thêm Tài khoản & Cấu hình Mạng",
            ["Accounts.FilterSegAll"] = "Tất cả",
            ["Accounts.FilterSegActive"] = "Đang hoạt động",
            ["Accounts.FilterSegAttention"] = "Cần kiểm tra",
            ["Accounts.TableTitlePrefix"] = "Tổng số tài khoản",

            ["Groups.TabTargets"] = "Nhóm Chiến dịch Đích",
            ["Groups.TabDiscover"] = "Khám phá & Tham gia Nhóm mới",

            ["Publisher.TabCompose"] = "Soạn & Đăng bài Chiến dịch",
            ["Publisher.TabTemplates"] = "Kho Mẫu Nội dung",
            ["Publisher.TabBump"] = "Đẩy Bài viết Lên đầu",

            ["Settings.TabGeneral"] = "Thiết lập Hệ thống & Trình duyệt",
            ["Settings.TabAuditLogs"] = "Nhật ký Hoạt động"
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
            ["Overview.CheckDelayReady"] = "Safe delay",
            ["Overview.QuickActionsTitle"] = "Quick Actions",
            ["Overview.QuickOpenBrowser"] = "Open Browser",

            ["Accounts.Header"] = "Facebook Account Vault",
            ["Accounts.Desc"] = "Log in directly via an isolated browser window or 1-click paste from J2Team Cookies.",
            ["Accounts.SearchPlaceholder"] = "Search or filter accounts...",
            ["Accounts.NamePlaceholder"] = "Friendly account label",
            ["Accounts.CookiePlaceholder"] = "Paste J2Team JSON or Cookie string here",
            ["Accounts.BtnQuickImport"] = "Save / Import Cookie",
            ["Accounts.BtnPasteClipboard"] = "Paste Clipboard",
            ["Accounts.BtnOpenBrowserLogin"] = "Open Browser & Sync",
            ["Accounts.BtnDelete"] = "Remove Account",
            ["Accounts.LastActivePrefix"] = "Last active:",
            ["Accounts.ReadyToConnect"] = "Ready to launch browser",

            ["Groups.Header"] = "Groups Catalog",
            ["Groups.Desc"] = "Automatically syncs all joined groups from your active account so you can visually pick targets for posting and lead scanning.",
            ["Groups.SearchKeywordPlaceholder"] = "Search new groups or filter by group name...",
            ["Groups.FilterPlaceholder"] = "Quick filter synced groups by name...",
            ["Groups.BtnSyncJoined"] = "Sync Groups",
            ["Groups.BtnSearchNew"] = "Find New Groups",
            ["Groups.BtnJoinSelected"] = "Join",
            ["Groups.BtnSaveTarget"] = "Add Target",
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
            ["Leads.Desc"] = "Selectively scrape group posts into your local SQLite database and search/filter instantaneously without re-querying Facebook.",
            ["Leads.BuyerKeywordPlaceholder"] = "Search or filter posts in local database...",
            ["Leads.BtnScanGroupPosts"] = "Scrape Now",
            ["Leads.BtnConfigScraper"] = "Scrape Config",
            ["Leads.ConfigTitle"] = "Selective Scraper Configuration",
            ["Leads.ConfigKeywordsLabel"] = "Selective scrape keywords (empty = save all):",
            ["Leads.ConfigKeywordsPlaceholder"] = "e.g. looking for, hiring, price, C#...",
            ["Leads.ConfigPostsPerGroupLabel"] = "Posts / group:",
            ["Leads.ConfigIntervalMinutesLabel"] = "Interval (min):",
            ["Leads.ConfigAllGroupsToggle"] = "Scrape all enabled Target Groups",
            ["Leads.ConfigAutoBackgroundToggle"] = "Run periodic background auto-scraper",
            ["Leads.BtnClearScrapedDb"] = "Clear Stored Posts",
            ["Leads.FilterAllGroups"] = "All Scraped Groups",
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
            ["Leads.MediaVideoBadge"] = "Video",
            ["Leads.MediaPhotoBadge"] = "Photo",

            ["Settings.Header"] = "System & Browser Settings",
            ["Settings.Desc"] = "Customize appearance, display language, preferred browser engine, and safe delays. All changes are saved automatically.",
            ["Settings.SafetyTitle"] = "Action Pacing & Account Safety",
            ["Settings.SafetyDesc"] = "Configure humanized pacing between actions to keep accounts healthy and safe.",
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
            ["Accounts.ProxyBadgePrefix"] = "Dedicated Proxy",
            ["Accounts.NoProxyBadge"] = "Direct connection",
            ["Groups.BtnAutoJoinDiscovered"] = "Auto Join",
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
            ["Empty.Leads"] = "No stored posts match the filter — click 'Scrape Now' or enable 'Background Auto-Scraper' in Scrape Config.",
            ["Empty.AuditLogs"] = "No audit log entries match the current filters.",
            ["Accounts.BtnWarmUp"] = "Warm Up",
            ["Leads.AutoReactToggle"] = "Auto-React before sending Sales Comment",
            ["Publisher.AutoReactOwnPost"] = "Auto-Love post",
            ["Publisher.ScheduleDelayLabel"] = "Delay min:",
            ["Log.TaskStoppedByUser"] = "Task stopped immediately as requested by user.",
            ["Log.SelectAccountFirst"] = "Please select or add an account before running this task.",
            ["Log.ConnectionInterrupted"] = "Connection temporarily interrupted, please try again.",

            ["Leads.BtnScanPosts"] = "Scrape New Posts",
            ["Leads.EmptyPostText"] = "Media attachment post",
            ["Leads.TimeRecent"] = "Just now",
            ["Leads.StatusCommented"] = "✓ Commented",
            ["Leads.StatusPending"] = "● Pending",
            ["Leads.FilterSegAll"] = "All",
            ["Leads.FilterSegPending"] = "Pending",
            ["Leads.FilterSegCommented"] = "Commented",
            ["Leads.FilterGroupLabel"] = "Scraped Group",
            ["Leads.FilterKeywordLabel"] = "Database Search Keyword",
            ["Leads.TableTitlePrefix"] = "Stored Posts",
            ["Leads.ColAuthor"] = "AUTHOR",
            ["Leads.ColGroup"] = "GROUP",
            ["Leads.ColMessage"] = "POST CONTENT",
            ["Leads.ColIntent"] = "INTENT",
            ["Leads.ColTime"] = "TIME",
            ["Leads.ColStatus"] = "STATUS",
            ["Leads.TabRepository"] = "Post Repository & Filter",
            ["Leads.TabScraperConfig"] = "Target Groups & Scraper Config",
            ["Leads.TabAutoComment"] = "Sales Script & Auto-Comment",
            ["Leads.ConfigMultiGroupTitle"] = "Select Groups to Scrape (Multi-Group Selection)",
            ["Leads.ConfigMultiGroupDesc"] = "Check the groups you want to scrape for new posts. The engine rotates in safe batches with humanized delays.",
            ["Leads.ConfigRateLimitLabel"] = "Anti-Ban Rate-Limit delay between groups (sec):",
            ["Leads.ConfigMaxGroupsPerCycleLabel"] = "Max groups / round-robin batch:",
            ["Leads.ConfigChronologicalBadge"] = "Prioritize Newest Posts (Chronological) so new buyers are never missed",

            ["Accounts.TabVault"] = "Account Vault",
            ["Accounts.TabImportAndProxy"] = "Import Cookie & Proxy Setup",
            ["Accounts.FilterSegAll"] = "All",
            ["Accounts.FilterSegActive"] = "Active",
            ["Accounts.FilterSegAttention"] = "Needs Attention",
            ["Accounts.TableTitlePrefix"] = "Managed Accounts",

            ["Groups.TabTargets"] = "Target Campaign Groups",
            ["Groups.TabDiscover"] = "Discover & Join Groups",

            ["Publisher.TabCompose"] = "Compose & Publish",
            ["Publisher.TabTemplates"] = "Saved Templates",
            ["Publisher.TabBump"] = "Bump Posts",

            ["Settings.TabGeneral"] = "System & Browser Settings",
            ["Settings.TabAuditLogs"] = "Activity Logs"
        };
}
