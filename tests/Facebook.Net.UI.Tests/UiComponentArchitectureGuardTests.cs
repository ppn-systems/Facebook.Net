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
