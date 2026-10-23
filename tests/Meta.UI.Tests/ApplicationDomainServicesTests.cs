// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Accounts;
using Meta.UI.Core.Groups;
using Meta.UI.Core.Importing;
using Meta.UI.Core.Leads;
using Meta.UI.Core.Publisher;
using Xunit;

namespace Meta.UI.Tests;

/// <summary>
/// Pure unit tests for <c>Meta.Application</c> domain services:
/// <see cref="LeadScoringService"/>, <see cref="GroupTagDomainService"/>,
/// <see cref="AccountVaultDomainService"/>, <see cref="UniversalSessionImporter"/>,
/// and <see cref="CampaignPublisherDomainService"/>.
/// </summary>
public sealed class ApplicationDomainServicesTests
{
    [Fact]
    public void UniversalSessionImporter_ParsesJ2TeamJson_MaxCarePipe_EmptyPlaceholder_And_DeterministicUid()
    {
        string j2TeamJson = """
            {
              "url": "https://www.facebook.com",
              "cookies": [
                { "name": "sb", "value": "sb_val" },
                { "name": "c_user", "value": "1000888999111" },
                { "name": "xs", "value": "45%3Asecret_xs" }
              ]
            }
            """;

        ImportedSessionBundle j2Bundle = UniversalSessionImporter.Parse(j2TeamJson, "Nick Bán Hàng 01");
        Assert.Equal("1000888999111", j2Bundle.AccountUid);
        Assert.Equal("Nick Bán Hàng 01", j2Bundle.SuggestedDisplayName);
        Assert.Contains("c_user=1000888999111", j2Bundle.CookieHeader, StringComparison.Ordinal);
        Assert.Contains("xs=45%3Asecret_xs", j2Bundle.CookieHeader, StringComparison.Ordinal);

        string maxCareLine = "1000555666777|Pass123|JBSWY3DPEHPK3PXP|c_user=1000555666777; xs=secret_token;|Mozilla/5.0 Custom";
        ImportedSessionBundle pipeBundle = UniversalSessionImporter.Parse(maxCareLine);
        Assert.Equal("1000555666777", pipeBundle.AccountUid);
        Assert.Equal("JBSWY3DPEHPK3PXP", pipeBundle.TwoFactorSecret);
        Assert.Equal("Mozilla/5.0 Custom", pipeBundle.UserAgent);
        Assert.Contains("xs=secret_token", pipeBundle.CookieHeader, StringComparison.Ordinal);

        ImportedSessionBundle emptyBundle = UniversalSessionImporter.Parse(string.Empty);
        Assert.StartsWith("login_", emptyBundle.AccountUid, StringComparison.Ordinal);
        Assert.Equal(string.Empty, emptyBundle.CookieHeader);

        string rawCookieNoCUser = "xs=99%3Asecret_token_value; datr=abcd1234;";
        ImportedSessionBundle det1 = UniversalSessionImporter.Parse(rawCookieNoCUser);
        ImportedSessionBundle det2 = UniversalSessionImporter.Parse(rawCookieNoCUser);
        Assert.Equal(det1.AccountUid, det2.AccountUid);
        Assert.StartsWith("acc_", det1.AccountUid, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalSessionImporter_TryParseProxySpec_HandlesHttpAndSocks5Formats()
    {
        bool okHttp = UniversalSessionImporter.TryParseProxySpec(
            "103.45.67.89:8080:myuser:mypass",
            "proxy_1",
            out ProxyConfig? httpProxy);

        Assert.True(okHttp);
        Assert.NotNull(httpProxy);
        Assert.Equal("103.45.67.89", httpProxy.Host);
        Assert.Equal(8080, httpProxy.Port);
        Assert.Equal(ProxyProtocol.Http, httpProxy.Protocol);
        Assert.Equal("myuser", httpProxy.Username);
        Assert.Equal("mypass", httpProxy.Password);

        bool okSocks = UniversalSessionImporter.TryParseProxySpec(
            "socks5://203.0.113.10:1080:socksuser:sockspass",
            "proxy_2",
            out ProxyConfig? socksProxy);

        Assert.True(okSocks);
        Assert.NotNull(socksProxy);
        Assert.Equal("203.0.113.10", socksProxy.Host);
        Assert.Equal(1080, socksProxy.Port);
        Assert.Equal(ProxyProtocol.Socks5, socksProxy.Protocol);
        Assert.Equal("socksuser", socksProxy.Username);
        Assert.Equal("sockspass", socksProxy.Password);

        string formatted = UniversalSessionImporter.FormatProxySpec(socksProxy);
        Assert.Equal("socks5://203.0.113.10:1080:socksuser:sockspass", formatted);
    }

    [Fact]
    public void LeadScoringService_ExtractVietnamesePhoneNumbers_HandlesAllObfuscations_AndRejectsFalsePositives()
    {
        LeadScoringService scorer = LeadScoringService.Instance;

        // 1. Dots, commas, spaces, hyphens, parentheses
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Liên hệ ngay: 098.765.4321 hoặc inbox"));
        Assert.Equal("0912345678", scorer.ExtractPrimaryPhoneNumber("SĐT: 0912,345,678"));
        Assert.Equal("0389998888", scorer.ExtractPrimaryPhoneNumber("Zalo (038) - 999 - 8888"));
        Assert.Equal("0776543210", scorer.ExtractPrimaryPhoneNumber("Gọi 0 7 7 6 5 4 3 2 1 0 nhé"));

        // 2. Emoji & icon separators between digit groups
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Chốt đơn 098🔥765🔥4321 giao ngay"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Hotline 098☎️765☎️4321"));
        Assert.Equal("0912345678", scorer.ExtractPrimaryPhoneNumber("Alo 0912👉345👉678"));

        // 3. Keycap emojis, circled digits, fullwidth & math bold digits
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Số zalo: 0️⃣9️⃣8️⃣7️⃣6️⃣5️⃣4️⃣3️⃣2️⃣1️⃣"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Gọi ngay: ⓪⑨⑧.⑦⑥⑤.④③②①"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Fullwidth: ０９８.７６５.４３２１"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Math bold: 𝟎𝟗𝟖𝟕𝟔𝟓𝟒𝟑𝟐𝟏"));

        // 4. Letter O/o lookalikes for zero
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Sđt O987.654.321"));
        Assert.Equal("0980123456", scorer.ExtractPrimaryPhoneNumber("Sđt 098O.123.456"));
        Assert.Equal("0389998888", scorer.ExtractPrimaryPhoneNumber("Zalo o38,999,8888"));

        // 5. Vietnamese spoken digit words
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Gọi số không chín tám 765 4321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("Alo 0987 sáu năm bốn 321 nha"));

        // 6. +84 / 84 / 0084 country code normalization
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH +84 987 654 321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH (+84) 0987.654.321"));
        Assert.Equal("0987654321", scorer.ExtractPrimaryPhoneNumber("LH 0084 987 654 321"));

        // 7. False-positive rejection: 15-digit Facebook IDs and prices like 090.000.000 / 100.000.000
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("https://www.facebook.com/groups/100098765432100/posts/998877665544332"));
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("Giá bán: 090.000.000 VNĐ thương lượng"));
        Assert.Null(scorer.ExtractPrimaryPhoneNumber("Giá chỉ 100.000.000 đồng"));

        // 8. Search keyword & accent-insensitive matching
        Assert.True(scorer.MatchesBuyerKeywords("Liên hệ O98🔥765🔥4321", ["0987654321"]));
        Assert.True(scorer.MatchesAccentInsensitive("Hội Mua Bán Bất Động Sản Quận 7", "bat dong san"));
        Assert.False(scorer.MatchesAccentInsensitive("Hội Lập Trình C# .NET", "bat dong san"));
    }

    [Fact]
    public void LeadScoringService_CalculateBuyerIntentScore_And_AnalyzeWorkspaceData_WorkCorrectly()
    {
        LeadScoringService scorer = LeadScoringService.Instance;

        int scoreWithEmptyKeywords = scorer.CalculateBuyerIntentScore(
            "Mình cần mua bàn ghế văn phòng gấp, liên hệ báo giá giúp mình qua số 0987.654.321 nhé?",
            rawKeywords: string.Empty);
        Assert.True(scoreWithEmptyKeywords >= 75);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SavedTargetGroup> groups =
        [
            new SavedTargetGroup("grp_1", "Chợ Nội Thất Sài Gòn", IsEnabled: true, TagsCsv: "tag_furniture"),
            new SavedTargetGroup("grp_2", "Hội Mua Bán Chung Cư", IsEnabled: true, TagsCsv: string.Empty)
        ];

        List<PostSummary> posts =
        [
            new PostSummary(
                PostId: "p1",
                AuthorUid: "u_buyer_1",
                AuthorName: "Nguyễn Văn An",
                MessageText: "Cần mua gấp sofa phòng khách, liên hệ 0987.654.321 báo giá giúp mình?",
                PermalinkUrl: "https://facebook.com/groups/grp_1/posts/p1",
                ReactionCount: 5,
                CommentCount: 0,
                CreatedAt: now.AddMinutes(-30),
                GroupId: "grp_1",
                GroupName: "Chợ Nội Thất Sài Gòn"),
            new PostSummary(
                PostId: "p2",
                AuthorUid: "u_seeder",
                AuthorName: "Trần Môi Giới",
                MessageText: "Xả kho sofa giá rẻ",
                PermalinkUrl: "https://facebook.com/groups/grp_1/posts/p2",
                ReactionCount: 1,
                CommentCount: 12,
                CreatedAt: now.AddHours(-2),
                GroupId: "grp_1",
                GroupName: "Chợ Nội Thất Sài Gòn"),
            new PostSummary(
                PostId: "p3",
                AuthorUid: "u_seeder",
                AuthorName: "Trần Môi Giới",
                MessageText: "Xả kho bàn ăn giá rẻ",
                PermalinkUrl: "https://facebook.com/groups/grp_2/posts/p3",
                ReactionCount: 2,
                CommentCount: 8,
                CreatedAt: now.AddHours(-3),
                GroupId: "grp_2",
                GroupName: "Hội Mua Bán Chung Cư")
        ];

        HashSet<string> commentedIds = new(StringComparer.Ordinal) { "p1" };

        WorkspaceDataAnalyticsReport report = scorer.AnalyzeWorkspaceData(
            posts,
            groups,
            commentedIds,
            "cần mua, báo giá, sofa");

        Assert.Equal(3, report.TotalScrapedPosts);
        Assert.Equal(1, report.TotalPostsWithPhone);
        Assert.Equal(1, report.TotalHighIntentPosts);
        Assert.Equal(1, report.TotalZeroCommentOpportunities);
        Assert.Equal(1, report.TotalCommentedPosts);
        Assert.Equal(2, report.TotalPendingPosts);
        Assert.NotEmpty(report.TopPerformingGroups);
        Assert.Equal("grp_1", report.TopPerformingGroups[0].GroupId);
        _ = Assert.Single(report.RepeatAuthors);
        Assert.Equal("Trần Môi Giới", report.RepeatAuthors[0].AuthorName);
        Assert.Equal(2, report.RepeatAuthors[0].PostCount);

        string formatted = scorer.FormatAnalyticsReportForAi(report);
        Assert.Contains("Chợ Nội Thất Sài Gòn", formatted, StringComparison.Ordinal);
        Assert.Contains("Trần Môi Giới", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupTag_And_AccountVault_DomainServices_PreserveImmutabilityAndFormatCorrectly()
    {
        SavedTargetGroup gA = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: "tag_bds,tag_vip");
        SavedTargetGroup gB = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: "tag_bds,tag_vip");
        Assert.True(gA.HasTag("tag_vip"));
        Assert.False(gA.HasTag("tag_job"));
        Assert.Equal(gA, gB);
        Assert.Equal(gA.GetHashCode(), gB.GetHashCode());

        GroupTagDomainService groupTagSvc = GroupTagDomainService.Instance;
        Assert.Equal("#3B82F6", groupTagSvc.NormalizeColorHex("3b82f6"));
        Assert.Equal("#F6821F", groupTagSvc.NormalizeColorHex("not_a_color"));
        GroupTag normalizedTag = groupTagSvc.CreateTag("Nhóm Công Nghệ", "10b981");
        Assert.Equal("#10B981", normalizedTag.ColorHex);

        SavedTargetGroup sampleGroup = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: null);
        (SavedTargetGroup tagged, bool changed) = groupTagSvc.ApplyTagChange(sampleGroup, "tag_vip", assignTag: true);
        Assert.True(changed);
        Assert.Equal("tag_vip", tagged.TagsCsv);

        string slug = AccountVaultDomainService.Instance.SanitizeProfileFolderSlug("Hồ sơ (Chính) — Test");
        Assert.DoesNotContain("(", slug, StringComparison.Ordinal);
        Assert.DoesNotContain(")", slug, StringComparison.Ordinal);

        string spunText = CampaignPublisherDomainService.Instance.SpinText("{Xin chào|Chào bạn}");
        Assert.True(spunText is "Xin chào" or "Chào bạn");
    }
}
