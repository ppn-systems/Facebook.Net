// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Facebook.Net.UI.Tests;

/// <summary>
/// Architectural guard tests that scan the UI source code to enforce unified
/// <c>SearchFilterBar</c> usage, <c>GroupsPage</c> layout rules, and complete
/// <c>vi-VN.json</c> / <c>en-US.json</c> localization coverage.
/// </summary>
public sealed partial class UiComponentArchitectureGuardTests
{
    private static readonly string[] s_requiredSearchFilterBarPages =
    [
        "AccountsPage.axaml.cs",
        "AuditLogsPage.axaml.cs",
        "GroupsPage.axaml.cs",
        "GroupsPage.TagManagerTab.cs",
        "PublisherPage.axaml.cs",
        "PublisherPage.TemplatesAndBumpTabs.cs",
        "SalesLeadsPage.axaml.cs",
        "SalesLeadsPage.ScraperConfigTab.cs",
        "SettingsPage.ProfilesTab.cs"
    ];

    private static readonly string[] s_requiredConfirmationModalPages =
    [
        "AccountsPage.axaml.cs",
        "AuditLogsPage.axaml.cs",
        "GroupsPage.axaml.cs",
        "GroupsPage.TagManagerTab.cs",
        "PublisherPage.TemplatesAndBumpTabs.cs",
        "SalesLeadsPage.ScraperConfigTab.cs",
        "SettingsPage.AnalyticsTab.cs",
        "SettingsPage.ProfilesTab.cs"
    ];

    private static readonly string[] s_forbiddenSegmentedFilterPillPages =
    [
        "AccountsPage.axaml.cs",
        "AuditLogsPage.axaml.cs",
        "GroupsPage.axaml.cs",
        "GroupsPage.TagManagerTab.cs",
        "PublisherPage.TemplatesAndBumpTabs.cs",
        "SalesLeadsPage.axaml.cs",
        "SalesLeadsPage.ScraperConfigTab.cs"
    ];

    private static readonly string[] s_uiProjectDirectories =
    [
        Path.Combine("ui", "Facebook.Net.App"),
        Path.Combine("ui", "Facebook.Net.UI.ViewModels"),
        Path.Combine("ui", "Facebook.Net.UI.Components")
    ];

    [GeneratedRegex(
        @"(?:new\s+SearchFilterBar\s*\(|\bSearchFilterBar\s+\w+\s*=\s*new\s*\()",
        RegexOptions.CultureInvariant)]
    private static partial Regex SearchFilterBarInstantiationRegex();

    [GeneratedRegex(
        @"FormField\.CreateTextBox\s*\([^;]*?(?:Search|Filter)[A-Za-z0-9_.]*Placeholder",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex RawSearchOrFilterTextBoxRegex();

    [GeneratedRegex(
        @"(?:new\s+SubTabItem\s*\(|\bSubTabItem\s+\w+\s*=\s*new\s*\()",
        RegexOptions.CultureInvariant)]
    private static partial Regex SubTabItemInstantiationRegex();

    [GeneratedRegex(
        @"(?:_i18n|\bi18n|\bLocalization)\s*(?:\[\s*""([^""]+)""\s*\]|\.Format\s*\(\s*""([^""]+)"")",
        RegexOptions.CultureInvariant)]
    private static partial Regex LocalizationKeyReferenceRegex();

    [Fact]
    public void Rule1_AllListFilteringPages_MustUseSearchFilterBarWithDrillDownCategories_AndNeverUseSegmentedFilterPillOrRawSearchTextBox()
    {
        string repoRoot = FindRepositoryRoot();
        string pagesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Pages");
        Assert.True(Directory.Exists(pagesDir), $"Pages directory not found at '{pagesDir}'.");

        List<string> violations = [];

        foreach (string fileName in s_requiredSearchFilterBarPages)
        {
            string filePath = Path.Combine(pagesDir, fileName);
            if (!File.Exists(filePath))
            {
                violations.Add($"{fileName}: expected page source file does not exist at '{filePath}'.");
                continue;
            }

            string source = File.ReadAllText(filePath);
            if (!SearchFilterBarInstantiationRegex().IsMatch(source))
            {
                violations.Add($"{fileName}: must instantiate 'SearchFilterBar' ('new SearchFilterBar(' or 'SearchFilterBar ... = new(').");
            }

            if (!source.Contains("FilterCategoryDefinition", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must configure 'FilterCategoryDefinition'.");
            }
        }

        foreach (string fileName in s_forbiddenSegmentedFilterPillPages)
        {
            string filePath = Path.Combine(pagesDir, fileName);
            if (!File.Exists(filePath))
            {
                violations.Add($"{fileName}: expected page source file does not exist at '{filePath}'.");
                continue;
            }

            string source = File.ReadAllText(filePath);
            if (source.Contains("SegmentedFilterPill", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must NOT contain 'SegmentedFilterPill'.");
            }
        }

        string[] allPageFiles = Directory.GetFiles(pagesDir, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(allPageFiles);

        foreach (string filePath in allPageFiles)
        {
            string fileName = Path.GetFileName(filePath);
            string source = File.ReadAllText(filePath);
            Match rawSearchMatch = RawSearchOrFilterTextBoxRegex().Match(source);
            if (rawSearchMatch.Success)
            {
                violations.Add($"{fileName}: must NOT create a raw search/filter TextBox via FormField.CreateTextBox (matched: '{rawSearchMatch.Value}').");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule2_GroupsPage_MustNotHaveLegacyThreeButtonBottomBar_AndMustHaveDedicatedThirdTabForTagManagement()
    {
        string repoRoot = FindRepositoryRoot();
        string pagesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Pages");
        Assert.True(Directory.Exists(pagesDir), $"Pages directory not found at '{pagesDir}'.");

        string[] groupsPageFiles = Directory.GetFiles(pagesDir, "GroupsPage*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(groupsPageFiles);

        List<string> violations = [];
        int totalSubTabItems = 0;
        bool hasCreateGroupTagCommand = false;

        foreach (string filePath in groupsPageFiles)
        {
            string fileName = Path.GetFileName(filePath);
            string source = File.ReadAllText(filePath);

            if (source.Contains("AddSelectedGroupToTargetsCommand", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must NOT reference legacy 'AddSelectedGroupToTargetsCommand'.");
            }

            if (source.Contains("JoinSelectedGroupCommand", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must NOT reference legacy 'JoinSelectedGroupCommand'.");
            }

            if (source.Contains("ColumnDefinitions(\"*,*,*\")", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must NOT contain legacy 3-button bottom bar 'ColumnDefinitions(\"*,*,*\")'.");
            }

            totalSubTabItems += SubTabItemInstantiationRegex().Count(source);
            if (source.Contains("CreateGroupTagCommand", StringComparison.Ordinal))
            {
                hasCreateGroupTagCommand = true;
            }
        }

        if (totalSubTabItems < 3)
        {
            violations.Add($"GroupsPage*.cs: expected at least 3 'new SubTabItem(' instances (found {totalSubTabItems}).");
        }

        if (!hasCreateGroupTagCommand)
        {
            violations.Add("GroupsPage*.cs: must reference 'CreateGroupTagCommand' in the dedicated Tag Management tab.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule3_AllReferencedLocalizationKeys_AcrossAppComponentsAndViewModels_MustExistInBothViVnAndEnUsJson()
    {
        string repoRoot = FindRepositoryRoot();
        string localesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.UI.Core", "Assets", "Locales");
        string viVnPath = Path.Combine(localesDir, "vi-VN.json");
        string enUsPath = Path.Combine(localesDir, "en-US.json");

        Assert.True(File.Exists(viVnPath), $"Vietnamese locale file not found at '{viVnPath}'.");
        Assert.True(File.Exists(enUsPath), $"English locale file not found at '{enUsPath}'.");

        HashSet<string> viKeys = LoadLocaleKeysFromDisk(viVnPath);
        HashSet<string> enKeys = LoadLocaleKeysFromDisk(enUsPath);

        List<string> missingKeyErrors = [];
        int scannedFiles = 0;
        int matchedReferences = 0;

        foreach (string relativeProjectDir in s_uiProjectDirectories)
        {
            string projectDir = Path.Combine(repoRoot, relativeProjectDir);
            Assert.True(Directory.Exists(projectDir), $"UI project directory not found at '{projectDir}'.");

            foreach (string filePath in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsInBuildOutputDirectory(projectDir, filePath))
                {
                    continue;
                }

                scannedFiles++;
                string source = File.ReadAllText(filePath);
                string relativeFilePath = Path.GetRelativePath(repoRoot, filePath);

                foreach (Match match in LocalizationKeyReferenceRegex().Matches(source))
                {
                    string key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    matchedReferences++;
                    if (!viKeys.Contains(key))
                    {
                        missingKeyErrors.Add($"{relativeFilePath}: key '{key}' is missing from vi-VN.json.");
                    }

                    if (!enKeys.Contains(key))
                    {
                        missingKeyErrors.Add($"{relativeFilePath}: key '{key}' is missing from en-US.json.");
                    }
                }
            }
        }

        Assert.True(scannedFiles > 0, "Expected to scan at least one .cs file across UI projects.");
        Assert.True(matchedReferences > 0, "Expected to find localization key references across UI projects.");
        Assert.True(missingKeyErrors.Count == 0, string.Join(Environment.NewLine, missingKeyErrors));
    }

    [Fact]
    public void Rule4_AiAssistantPanelAndLocales_MustNotUseEmojiIconsOrSecondBrainNaming_AndSettingsMustHostAiContextTab()
    {
        string repoRoot = FindRepositoryRoot();
        string panelPath = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Components", "AiAssistantPanel.cs");
        Assert.True(File.Exists(panelPath), $"AiAssistantPanel.cs not found at '{panelPath}'.");

        string panelSource = File.ReadAllText(panelPath);
        string[] forbiddenPanelEmojis = ["🤖", "🧠", "👤", "✅", "✏️", "🔐"];
        foreach (string emoji in forbiddenPanelEmojis)
        {
            Assert.DoesNotContain(emoji, panelSource, StringComparison.Ordinal);
        }

        string localesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.UI.Core", "Assets", "Locales");
        string viJson = File.ReadAllText(Path.Combine(localesDir, "vi-VN.json"));
        string enJson = File.ReadAllText(Path.Combine(localesDir, "en-US.json"));

        Assert.DoesNotContain("Bộ não thứ 2", viJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Second Brain", viJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bộ não thứ 2", enJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Second Brain", enJson, StringComparison.OrdinalIgnoreCase);

        string[] forbiddenLocaleEmojis = ["🤖", "🧠", "🔐"];
        foreach (string emoji in forbiddenLocaleEmojis)
        {
            Assert.DoesNotContain(emoji, viJson, StringComparison.Ordinal);
            Assert.DoesNotContain(emoji, enJson, StringComparison.Ordinal);
        }

        string aiContextTabPath = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Pages", "SettingsPage.AiContextTab.cs");
        Assert.True(File.Exists(aiContextTabPath), $"SettingsPage.AiContextTab.cs not found at '{aiContextTabPath}'.");

        string aiContextTabSource = File.ReadAllText(aiContextTabPath);
        Assert.True(
            SearchFilterBarInstantiationRegex().IsMatch(aiContextTabSource),
            "SettingsPage.AiContextTab.cs must instantiate 'SearchFilterBar'.");
        Assert.Contains("FilterCategoryDefinition", aiContextTabSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule4_AllDestructiveDeleteRemoveClearPages_MustUseConfirmDialogModal()
    {
        string repoRoot = FindRepositoryRoot();
        string pagesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Pages");
        Assert.True(Directory.Exists(pagesDir), $"Pages directory not found at '{pagesDir}'.");

        List<string> violations = [];

        foreach (string fileName in s_requiredConfirmationModalPages)
        {
            string filePath = Path.Combine(pagesDir, fileName);
            if (!File.Exists(filePath))
            {
                violations.Add($"{fileName}: expected page source file does not exist at '{filePath}'.");
                continue;
            }

            string source = File.ReadAllText(filePath);
            if (!source.Contains("ConfirmDialogModal.Show", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: must guard destructive delete/remove/clear actions with 'ConfirmDialogModal.Show' or 'ConfirmDialogModal.ShowAsync'.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule5_ViewsMustNeverAccessAccountStoreOrAuditStoreDirectly_EnforcingDddLayeredBoundaries()
    {
        string repoRoot = FindRepositoryRoot();
        string pagesDir = Path.Combine(repoRoot, "ui", "Facebook.Net.App", "Views", "Pages");
        Assert.True(Directory.Exists(pagesDir), $"Pages directory not found at '{pagesDir}'.");

        List<string> violations = [];
        foreach (string filePath in Directory.EnumerateFiles(pagesDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(filePath);
            string source = File.ReadAllText(filePath);
            if (source.Contains(".AccountStore.", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: Views must NOT call '.AccountStore.' directly; delegate through ShellViewModel commands/methods.");
            }

            if (source.Contains(".AuditStore.", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}: Views must NOT call '.AuditStore.' directly; delegate through ViewModels.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Rule6_DddAndSolidArchitecture_DomainServicesAndInterfacesMustBeImplementedAndInjected()
    {
        // Verify SOLID Dependency Inversion Principle (DIP) & Interface Segregation Principle (ISP) contracts
        Assert.True(typeof(Facebook.Net.UI.Core.Theming.IThemeService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Theming.ThemeService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Localization.ILocalizationService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Localization.LocalizationService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Configuration.IAppSettingsService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Configuration.AppSettingsService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Configuration.IStorageUsageService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Configuration.StorageUsageService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Configuration.IAppReleaseService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Configuration.GitHubAppReleaseService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Configuration.ISystemLauncherService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Configuration.SystemLauncherService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Exports.ICsvExportService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Exports.CsvExportService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Importing.IUniversalSessionImporter).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Importing.UniversalSessionImporterService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Publisher.ICampaignPublisherDomainService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Publisher.CampaignPublisherDomainService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Leads.ILeadScoringService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Leads.LeadScoringService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Accounts.IAccountVaultDomainService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Accounts.AccountVaultDomainService)));
        Assert.True(typeof(Facebook.Net.UI.Core.Groups.IGroupTagDomainService).IsAssignableFrom(typeof(Facebook.Net.UI.Core.Groups.GroupTagDomainService)));

        // Verify ISP segregated repository & protocol client interfaces
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IAccountRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IProxyRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.ITargetGroupRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.ICampaignTemplateRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IActionHistoryRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.ILeadCrmRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IPublishedPostRepository).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.IAccountStore)));

        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IFacebookPublishClient).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.ICometClient)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IFacebookFeedClient).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.ICometClient)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IFacebookGroupClient).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.ICometClient)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IFacebookFriendshipClient).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.ICometClient)));
        Assert.True(typeof(Facebook.Net.Contracts.Abstractions.IFacebookMediaClient).IsAssignableFrom(typeof(Facebook.Net.Contracts.Abstractions.ICometClient)));

        // Verify Facebook.Net.Application assembly is 100% pure (no Avalonia or SQLite reference)
        System.Reflection.Assembly appAssembly = typeof(Core.Leads.LeadScoringService).Assembly;
        Assert.Equal("Facebook.Net.Application", appAssembly.GetName().Name);

        // Verify DDD Value Object / Entity record immutability (SavedTargetGroup equality must not be broken by HasTag/GetTagIds)
        Facebook.Net.Contracts.Models.SavedTargetGroup gA = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: "tag_bds,tag_vip");
        Facebook.Net.Contracts.Models.SavedTargetGroup gB = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: "tag_bds,tag_vip");
        Assert.True(gA.HasTag("tag_vip"));
        Assert.False(gA.HasTag("tag_job"));
        Assert.Equal(gA, gB);
        Assert.Equal(gA.GetHashCode(), gB.GetHashCode());

        // Verify DDD Domain Services behavior
        Core.Leads.LeadScoringService leadScoring = Core.Leads.LeadScoringService.Instance;
        Assert.Equal("Can tim mua can ho quan 2", leadScoring.RemoveVietnameseDiacritics("Cần tìm mua căn hộ quận 2"));
        Assert.True(leadScoring.CalculateBuyerIntentScore("Cần tìm mua căn hộ giá tốt?", "căn hộ, đất nền") >= 75);

        Core.Groups.GroupTagDomainService groupTagSvc = Core.Groups.GroupTagDomainService.Instance;
        Facebook.Net.Contracts.Models.SavedTargetGroup sampleGroup = new("g1", "Group 1", IsEnabled: true, Notes: null, AvatarUrl: null, TagsCsv: null);
        (Facebook.Net.Contracts.Models.SavedTargetGroup tagged, bool changed) = groupTagSvc.ApplyTagChange(sampleGroup, "tag_vip", assignTag: true);
        Assert.True(changed);
        Assert.Equal("tag_vip", tagged.TagsCsv);

        string spunText = Core.Publisher.CampaignPublisherDomainService.Instance.SpinText("{Xin chào|Chào bạn}");
        Assert.True(spunText is "Xin chào" or "Chào bạn");

        // Verify ShellViewModel delegates to Bounded Context Domain Services instead of embedding raw domain algorithms
        string repoRoot = FindRepositoryRoot();
        string appCsprojSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Facebook.Net.Application", "Facebook.Net.Application.csproj"));
        Assert.DoesNotContain("Avalonia", appCsprojSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sqlite", appCsprojSource, StringComparison.OrdinalIgnoreCase);

        string shellDir = Path.Combine(repoRoot, "ui", "Facebook.Net.UI.ViewModels", "Shell");
        string salesLeadsSource = File.ReadAllText(Path.Combine(shellDir, "ShellViewModel.SalesLeads.cs"));
        string accountsSource = File.ReadAllText(Path.Combine(shellDir, "ShellViewModel.Accounts.cs"));
        string groupsSource = File.ReadAllText(Path.Combine(shellDir, "ShellViewModel.Groups.cs"));
        string settingsSource = File.ReadAllText(Path.Combine(shellDir, "ShellViewModel.Settings.cs"));
        string publisherSource = File.ReadAllText(Path.Combine(shellDir, "ShellViewModel.Publisher.cs"));

        Assert.Contains("_context.LeadScoring.", salesLeadsSource, StringComparison.Ordinal);
        Assert.Contains("_context.CsvExport.", salesLeadsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizationForm.FormD", salesLeadsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using Facebook.Net.Protocol;", salesLeadsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using Facebook.Net.Protocol;", publisherSource, StringComparison.Ordinal);

        Assert.Contains("_context.AccountVault.", accountsSource, StringComparison.Ordinal);
        Assert.Contains("_context.SessionImporter.", accountsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FileMode.Open", accountsSource, StringComparison.Ordinal);

        Assert.Contains("_context.GroupTagService.", groupsSource, StringComparison.Ordinal);
        Assert.Contains("_context.StorageUsage.", settingsSource, StringComparison.Ordinal);
        Assert.Contains("_context.AppRelease.", settingsSource, StringComparison.Ordinal);
        Assert.Contains("_context.SystemLauncher.", settingsSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(dir.FullName, "ui")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root starting from '{AppContext.BaseDirectory}'.");
    }

    private static HashSet<string> LoadLocaleKeysFromDisk(string jsonFilePath)
    {
        using Stream stream = File.OpenRead(jsonFilePath);
        using JsonDocument doc = JsonDocument.Parse(stream);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                _ = keys.Add(prop.Name);
            }
        }

        return keys;
    }

    private static bool IsInBuildOutputDirectory(string projectRoot, string filePath)
    {
        string relative = Path.GetRelativePath(projectRoot, filePath);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segments[i], "obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
