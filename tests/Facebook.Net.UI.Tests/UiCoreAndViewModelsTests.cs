// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.Mcp;
using Facebook.Net.Mcp.Tools;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Unit tests verifying <see cref="SqliteAuditLogStore"/>, <see cref="LocalizationService"/>,
/// <see cref="ThemeService"/>, <see cref="AppConfig"/>, <see cref="BulkObservableCollection{T}"/>,
/// <see cref="VectorIcons"/>, and MCP Server tool contracts.
/// </summary>
public sealed partial class UiCoreAndViewModelsTests : IDisposable
{
    private readonly string _tempDir;

    public UiCoreAndViewModelsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"fb_ui_core_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(this._tempDir);
    }

    [GeneratedRegex(@"\bfb_[a-z0-9_]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex FbToolNameRegex();

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
    public void LocalizationAndThemeServices_HaveCompleteCatalogParity_AndSwitchCleanly()
    {
        Dictionary<string, string>.KeyCollection viKeys = LocalizationService.GetKeys(AppLanguage.ViVn);
        Dictionary<string, string>.KeyCollection enKeys = LocalizationService.GetKeys(AppLanguage.EnUs);

        HashSet<string> viSet = new(viKeys, StringComparer.Ordinal);
        HashSet<string> enSet = new(enKeys, StringComparer.Ordinal);

        Assert.Equal(viSet.Count, enSet.Count);
        Assert.Subset(viSet, enSet);
        Assert.Superset(viSet, enSet);

        LocalizationService i18n = new();
        foreach (string key in viKeys)
        {
            i18n.SetLanguage(AppLanguage.ViVn);
            string viVal = i18n[key];
            Assert.False(string.IsNullOrWhiteSpace(viVal));
            Assert.NotEqual(key, viVal);

            i18n.SetLanguage(AppLanguage.EnUs);
            string enVal = i18n[key];
            Assert.False(string.IsNullOrWhiteSpace(enVal));
            Assert.NotEqual(key, enVal);
        }

        i18n.SetLanguage(AppLanguage.ViVn);
        Assert.Equal("Khởi động hoàn tất — 3 tài khoản, 12 nhóm mục tiêu.", i18n.Format("Log.SystemInitialized", 3, 12));
        Assert.Equal("Chờ kết nối", i18n.FormatAccountStatus(AccountStatus.Idle));
        Assert.Equal("Đã tham gia", i18n.FormatGroupMembership(GroupMembershipState.Member));
        Assert.Equal("Bình luận", i18n.FormatAuditCategory(AuditCategory.SalesComment));
        Assert.Equal("Trong 24 giờ qua", i18n.FormatAuditTimeRange(AuditTimeRange.Today24Hours));

        i18n.SetLanguage(AppLanguage.EnUs);
        Assert.Equal("System initialized — 3 accounts, 12 target groups.", i18n.Format("Log.SystemInitialized", 3, 12));
        Assert.Equal("Ready to connect", i18n.FormatAccountStatus(AccountStatus.Idle));
        Assert.Equal("Joined Member", i18n.FormatGroupMembership(GroupMembershipState.Member));
        Assert.Equal("Sales Comment", i18n.FormatAuditCategory(AuditCategory.SalesComment));
        Assert.Equal("Last 24 Hours", i18n.FormatAuditTimeRange(AuditTimeRange.Today24Hours));

        ThemeService theme = new();
        Assert.Equal(AppThemeMode.Dark, theme.CurrentMode);
        theme.ToggleTheme();
        Assert.Equal(AppThemeMode.Light, theme.CurrentMode);
        theme.SetTheme(AppThemeMode.System);
        Assert.Equal(AppThemeMode.System, theme.CurrentMode);
        Assert.True(theme.EffectiveMode is AppThemeMode.Dark or AppThemeMode.Light);
        Assert.Equal("System Default", i18n.FormatThemeMode(AppThemeMode.System));
    }

    [Fact]
    public void AppConfigNormalize_And_BulkObservableCollectionSingleReset_WorkCorrectly()
    {
        AppConfig inverted = new(MinDelaySeconds: 90, MaxDelaySeconds: 20);
        AppConfig normalized = inverted.Normalize();
        Assert.Equal(90, normalized.MinDelaySeconds);
        Assert.Equal(90, normalized.MaxDelaySeconds);

        BulkObservableCollection<int> col = new();
        int eventCount = 0;
        col.CollectionChanged += (_, _) => eventCount++;
        col.ReplaceAll([1, 2, 3, 4, 5]);
        Assert.Equal(1, eventCount);
        Assert.Equal(5, col.Count);
    }

    [Fact]
    public void VectorIcons_AllDefinedSvgIconConstants_ExistAsEmbeddedResources()
    {
        FieldInfo[] iconFields = typeof(VectorIcons).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.NotEmpty(iconFields);

        Assembly compAsm = typeof(VectorIcons).Assembly;
        foreach (FieldInfo field in iconFields)
        {
            if (field.FieldType != typeof(string) || !field.IsLiteral)
            {
                continue;
            }

            string? svgName = (string?)field.GetRawConstantValue();
            Assert.False(string.IsNullOrWhiteSpace(svgName));
            Assert.EndsWith(".svg", svgName, StringComparison.OrdinalIgnoreCase);

            using Stream? resStream = compAsm.GetManifestResourceStream($"Assets.Icons.{svgName}");
            Assert.NotNull(resStream);
            using StreamReader reader = new(resStream);
            string svgXml = reader.ReadToEnd();
            Assert.Contains("<svg", svgXml, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void McpServerTools_And_SystemPrompts_HaveValidToolSchemaParity()
    {
        HashSet<string> registeredMcpTools = new(StringComparer.Ordinal);
        CollectTools<AccountTools>(registeredMcpTools);
        CollectTools<GroupTools>(registeredMcpTools);
        CollectTools<PublisherTools>(registeredMcpTools);
        CollectTools<SalesLeadsTools>(registeredMcpTools);
        CollectTools<SystemSettingsTools>(registeredMcpTools);
        CollectTools<AiMemoryTools>(registeredMcpTools);

        Assert.NotEmpty(registeredMcpTools);

        string analyzePromptText = ((TextContentBlock)Prompts.AnalyzeGroupLeads("test").Messages[0].Content).Text;
        string composePromptText = ((TextContentBlock)Prompts.ComposeSpintaxCampaign("test").Messages[0].Content).Text;

        string[] promptSources =
        [
            AppConfig.DefaultAiSystemPrompt,
            Resources.Overview(),
            Resources.SalesAgentGuide(),
            analyzePromptText,
            composePromptText
        ];

        Regex toolRegex = FbToolNameRegex();
        foreach (string source in promptSources)
        {
            foreach (Match match in toolRegex.Matches(source))
            {
                Assert.Contains(match.Value, registeredMcpTools);
            }
        }
    }

    private static void CollectTools<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        HashSet<string> target)
    {
        foreach (MethodInfo method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            McpServerToolAttribute? attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Name))
            {
                _ = target.Add(attr.Name);
            }
        }
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
