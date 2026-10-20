// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Importing;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Audit;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests verifying <see cref="UniversalSessionImporter"/>, <see cref="SqliteAuditLogStore"/>,
/// <see cref="LocalizationService"/>, <see cref="ThemeService"/>, <see cref="ShellViewModel"/>,
/// and <see cref="AuditLogDrawerViewModel"/>.
/// </summary>
public sealed class UiCoreAndViewModelsTests : IDisposable
{
    private readonly string _tempDir;

    public UiCoreAndViewModelsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_ui_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    private sealed class StubBrowserAdapter : IBrowserAdapter
    {
        public string PlatformName => "TestStub";

        public string? LocateExecutablePath() => null;

        public Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException("Stub browser adapter does not launch real processes in unit tests.");
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
        Assert.Equal(Facebook.Net.Contracts.Primitives.ProxyProtocol.Http, httpProxy.Protocol);
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
        Assert.Equal(Facebook.Net.Contracts.Primitives.ProxyProtocol.Socks5, socksProxy.Protocol);
        Assert.Equal("socksuser", socksProxy.Username);
        Assert.Equal("sockspass", socksProxy.Password);

        string formatted = UniversalSessionImporter.FormatProxySpec(socksProxy);
        Assert.Equal("socks5://203.0.113.10:1080:socksuser:sockspass", formatted);
    }

    [Fact]
    public void PayloadBuilder_SpinText_HandlesNestedAndStrayBracesSafely()
    {
        string nested = Facebook.Net.Protocol.PayloadBuilder.SpinText("{A|{B|B}}");
        Assert.True(nested == "A" || nested == "B");

        string strayClosingBeforeBlock = Facebook.Net.Protocol.PayloadBuilder.SpinText("Price: } 100k {deal|sale}");
        Assert.True(
            strayClosingBeforeBlock == "Price: } 100k deal" ||
            strayClosingBeforeBlock == "Price: } 100k sale");

        string emptyBraces = Facebook.Net.Protocol.PayloadBuilder.SpinText("Hello {}world");
        Assert.Equal("Hello world", emptyBraces);
    }

    [Fact]
    public async Task ShellViewModel_And_AuditLogDrawerViewModel_EndToEndWorkflow_WorksWithoutJargon()
    {
        string e2eDir = Path.Combine(this._tempDir, "e2e_workspace");
        using UiServiceContext context = new(new StubBrowserAdapter(), e2eDir);
        using ShellViewModel shell = new(context);

        await shell.InitializeAsync();

        // Quick import J2Team cookie bundle without manual UID
        shell.AccountFriendlyNameInput = "Nick Seeding VIP";
        shell.SessionImportRawInput = "c_user=1000999888777; xs=99%3Atest_session_xs;";
        await shell.QuickImportSessionCommand.ExecuteAsync(null);

        Assert.NotNull(shell.SelectedAccount);
        Assert.Equal("Nick Seeding VIP", shell.SelectedAccount.DisplayName);
        Assert.Equal("1000999888777", shell.SelectedAccount.Uid);

        // Save per-account SOCKS5 proxy
        shell.AccountProxyInput = "socks5://198.51.100.7:1080:vipuser:vippass";
        await shell.SaveAccountProxyCommand.ExecuteAsync(null);
        Assert.NotNull(shell.SelectedAccount.ProxyId);
        Assert.Equal("socks5://198.51.100.7:1080:vipuser:vippass", shell.AccountProxyInput);

        // Preserve custom user-edited Spintax across language switches
        string customSpintax = "{Chào cả nhà|Hello anh chị}, bên em chuyên sỉ lẻ gia dụng!";
        shell.PostSpintaxText = customSpintax;
        shell.ToggleLanguageQuickCommand.Execute(null);
        Assert.Equal(customSpintax, shell.PostSpintaxText);

        // Refresh and export Audit Logs to JSON and CSV
        await shell.AuditDrawer.RefreshLogsAsync();
        string jsonExportPath = Path.Combine(this._tempDir, "audit_export.json");
        string csvExportPath = Path.Combine(this._tempDir, "audit_export.csv");
        await shell.AuditDrawer.ExportFilteredLogsAsync(jsonExportPath);
        await shell.AuditDrawer.ExportFilteredLogsAsync(csvExportPath);

        Assert.True(File.Exists(jsonExportPath));
        Assert.True(File.Exists(csvExportPath));
        Assert.Contains("Nick Seeding VIP", await File.ReadAllTextAsync(jsonExportPath), StringComparison.Ordinal);
        Assert.Contains("Nick Seeding VIP", await File.ReadAllTextAsync(csvExportPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellViewModel_PersistsAllSettingsImmediately_AndReloadsOnRestart()
    {
        string persistDir = Path.Combine(this._tempDir, "settings_persist_test");

        using (UiServiceContext ctx1 = new(new StubBrowserAdapter(), persistDir))
        using (ShellViewModel vm1 = new(ctx1))
        {
            vm1.SelectedThemeMode = AppThemeMode.Light;
            vm1.SelectedLanguage = AppLanguage.EnUs;
            vm1.SelectedBrowserKind = Facebook.Net.UI.Core.Configuration.PreferredBrowserKind.Brave;
            vm1.CustomBrowserExecutablePath = @"C:\CustomBrowser\brave.exe";
            vm1.RunBrowserHeadless = true;
            vm1.MinDelaySeconds = 25;
            vm1.MaxDelaySeconds = 90;
            vm1.IsSidebarCollapsed = true;
        }

        // Simulate closing and reopening the application from the same data directory
        using UiServiceContext ctx2 = new(new StubBrowserAdapter(), persistDir);
        using ShellViewModel vm2 = new(ctx2);

        // Verify properties are loaded synchronously in ShellViewModel constructor BEFORE InitializeAsync()
        Assert.Equal(AppThemeMode.Light, vm2.SelectedThemeMode);
        Assert.Equal(AppThemeMode.Light, ctx2.Theme.CurrentMode);
        Assert.Equal(AppLanguage.EnUs, vm2.SelectedLanguage);
        Assert.Equal(AppLanguage.EnUs, ctx2.Localization.CurrentLanguage);
        Assert.Equal(Facebook.Net.UI.Core.Configuration.PreferredBrowserKind.Brave, vm2.SelectedBrowserKind);
        Assert.Equal(@"C:\CustomBrowser\brave.exe", vm2.CustomBrowserExecutablePath);
        Assert.True(vm2.RunBrowserHeadless);
        Assert.Equal(25, vm2.MinDelaySeconds);
        Assert.Equal(90, vm2.MaxDelaySeconds);
        Assert.True(vm2.IsSidebarCollapsed);
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
