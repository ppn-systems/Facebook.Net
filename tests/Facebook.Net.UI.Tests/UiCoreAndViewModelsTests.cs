// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Importing;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests verifying <see cref="UniversalSessionImporter"/>, <see cref="SqliteAuditLogStore"/>,
/// <see cref="LocalizationService"/>, <see cref="ThemeService"/>, and <see cref="AppSettingsService"/>.
/// </summary>
public sealed class UiCoreAndViewModelsTests : IDisposable
{
    private readonly string _tempDir;

    public UiCoreAndViewModelsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_ui_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    [Fact]
    public void UniversalSessionImporter_ParsesJ2TeamCookiesJson_WithoutRequiringManualUid()
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

        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(j2TeamJson, "Nick Bán Hàng 01");

        Assert.Equal("1000888999111", bundle.AccountUid);
        Assert.Equal("Nick Bán Hàng 01", bundle.SuggestedDisplayName);
        Assert.Contains("c_user=1000888999111", bundle.CookieHeader, StringComparison.Ordinal);
        Assert.Contains("xs=45%3Asecret_xs", bundle.CookieHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalSessionImporter_ParsesPipeDelimitedMaxCareFormat_WithoutRequiringManualUid()
    {
        string line = "1000555666777|Pass123|JBSWY3DPEHPK3PXP|c_user=1000555666777; xs=secret_token;|Mozilla/5.0 Custom";
        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(line);

        Assert.Equal("1000555666777", bundle.AccountUid);
        Assert.Equal("JBSWY3DPEHPK3PXP", bundle.TwoFactorSecret);
        Assert.Equal("Mozilla/5.0 Custom", bundle.UserAgent);
        Assert.Contains("xs=secret_token", bundle.CookieHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversalSessionImporter_WhenEmpty_CreatesInteractiveBrowserLoginPlaceholder()
    {
        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(string.Empty);

        Assert.StartsWith("login_", bundle.AccountUid, StringComparison.Ordinal);
        Assert.Equal(string.Empty, bundle.CookieHeader);
    }

    [Fact]
    public async Task SqliteAuditLogStore_FiltersByCategorySeverityAccountAndKeyword()
    {
        string auditDbPath = Path.Combine(this._tempDir, "facebook_audit.db");
        SqliteAuditLogStore store = new(auditDbPath);
        await store.InitializeAsync();

        _ = await store.AppendAsync(
            AuditCategory.Post,
            AuditSeverity.Success,
            "10001",
            "Nick BĐS 01",
            "Chợ Cư Dân Vinhomes",
            "Đăng bài thành công vào nhóm Chợ Cư Dân Vinhomes");

        _ = await store.AppendAsync(
            AuditCategory.SalesComment,
            AuditSeverity.Warning,
            "10002",
            "Nick Seeding 02",
            "Bài của Nguyễn Văn A",
            "Bỏ qua bài viết đã từng comment trước đó");

        List<AuditLogRecord> postLogs = await store.QueryAsync(new AuditLogFilter(Category: AuditCategory.Post));
        _ = Assert.Single(postLogs);
        Assert.Equal("Nick BĐS 01", postLogs[0].AccountDisplayName);

        List<AuditLogRecord> keywordLogs = await store.QueryAsync(new AuditLogFilter(SearchKeyword: "Vinhomes"));
        _ = Assert.Single(keywordLogs);

        List<AuditLogRecord> acc2Logs = await store.QueryAsync(new AuditLogFilter(AccountUid: "10002"));
        _ = Assert.Single(acc2Logs);
        Assert.Equal(AuditCategory.SalesComment, acc2Logs[0].Category);
    }

    [Fact]
    public void LocalizationAndThemeServices_SwitchLanguagesAndPalettesCleanly()
    {
        LocalizationService i18n = new();
        Assert.Equal(AppLanguage.ViVn, i18n.CurrentLanguage);
        Assert.Contains("Tài khoản", i18n["Nav.Accounts"], StringComparison.Ordinal);

        i18n.SetLanguage(AppLanguage.EnUs);
        Assert.Equal(AppLanguage.EnUs, i18n.CurrentLanguage);
        Assert.Contains("Accounts", i18n["Nav.Accounts"], StringComparison.Ordinal);

        ThemeService theme = new();
        Assert.Equal(AppThemeMode.Dark, theme.CurrentMode);
        theme.ToggleTheme();
        Assert.Equal(AppThemeMode.Light, theme.CurrentMode);

        // Verify zero-jargon localized enum formatters
        Assert.Equal("Ready to connect", i18n.FormatAccountStatus(Facebook.Net.Contracts.Primitives.AccountStatus.Idle));
        Assert.Equal("Joined Member", i18n.FormatGroupMembership(Facebook.Net.Contracts.Primitives.GroupMembershipState.Member));
        Assert.Equal("Sales Comment", i18n.FormatAuditCategory(AuditCategory.SalesComment));
        Assert.Equal("Last 24 Hours", i18n.FormatAuditTimeRange(AuditTimeRange.Today24Hours));

        i18n.SetLanguage(AppLanguage.ViVn);
        Assert.Equal("Chờ kết nối", i18n.FormatAccountStatus(Facebook.Net.Contracts.Primitives.AccountStatus.Idle));
        Assert.Equal("Đã tham gia", i18n.FormatGroupMembership(Facebook.Net.Contracts.Primitives.GroupMembershipState.Member));
        Assert.Equal("Comment Sale", i18n.FormatAuditCategory(AuditCategory.SalesComment));
        Assert.Equal("Trong 24 giờ qua", i18n.FormatAuditTimeRange(AuditTimeRange.Today24Hours));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._tempDir))
            {
                Directory.Delete(this._tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore locked temp files
        }
    }
}
