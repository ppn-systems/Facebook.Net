// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Facebook.Net.UI.Core.Configuration;

/// <summary>
/// Immutable result of an application release update check against GitHub Releases.
/// </summary>
public sealed record AppReleaseCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestTag,
    string PublishedDateText,
    string DownloadUrl,
    string? ErrorMessage = null);

/// <summary>
/// Domain service contract for checking application version and GitHub Releases updates.
/// </summary>
public interface IAppReleaseService
{
    /// <summary>Gets the current application version string (e.g., <c>v1.0.0</c>).</summary>
    string GetCurrentAppVersionString();

    /// <summary>Normalizes a release tag or version string (such as <c>v1.2.3-beta.1</c>) into a comparable 3-part <see cref="Version"/>.</summary>
    Version TryParseReleaseVersion(string? rawVersion);

    /// <summary>Queries GitHub Releases (<c>ppn-systems/Facebook.Net</c>) to check if a newer application version is available.</summary>
    Task<AppReleaseCheckResult> CheckForUpdatesAsync(CancellationToken ct = default);
}

/// <summary>
/// Default stateless implementation of <see cref="IAppReleaseService"/> querying GitHub Releases.
/// </summary>
public sealed class GitHubAppReleaseService : IAppReleaseService
{
    private const string ReleasesPageFallback = "https://github.com/ppn-systems/Facebook.Net/releases";

    /// <summary>Gets the shared stateless singleton instance of <see cref="GitHubAppReleaseService"/>.</summary>
    public static GitHubAppReleaseService Instance { get; } = new();

    /// <inheritdoc />
    public string GetCurrentAppVersionString()
    {
        Version? asmVersion = (Assembly.GetEntryAssembly() ?? typeof(GitHubAppReleaseService).Assembly).GetName().Version;
        if (asmVersion is null)
        {
            return "v1.0.0";
        }

        int build = asmVersion.Build >= 0 ? asmVersion.Build : 0;
        return $"v{asmVersion.Major}.{asmVersion.Minor}.{build}";
    }

    /// <inheritdoc />
    public Version TryParseReleaseVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return new Version(1, 0, 0);
        }

        string cleaned = rawVersion.Trim().TrimStart('v', 'V');
        int dashIdx = cleaned.IndexOfAny(['-', '+']);
        if (dashIdx > 0)
        {
            cleaned = cleaned[..dashIdx];
        }

        if (Version.TryParse(cleaned, out Version? parsed) && parsed is not null)
        {
            int build = parsed.Build >= 0 ? parsed.Build : 0;
            return new Version(parsed.Major, parsed.Minor, build);
        }

        return new Version(1, 0, 0);
    }

    /// <inheritdoc />
    public async Task<AppReleaseCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVerStr = this.GetCurrentAppVersionString();
        try
        {
            using HttpClient http = new()
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FacebookOneClient-UpdateChecker/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http
                .GetStringAsync(new Uri("https://api.github.com/repos/ppn-systems/Facebook.Net/releases?per_page=20"), ct)
                .ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return new AppReleaseCheckResult(
                    IsUpdateAvailable: false,
                    CurrentVersion: currentVerStr,
                    LatestTag: currentVerStr,
                    PublishedDateText: "—",
                    DownloadUrl: ReleasesPageFallback);
            }

            JsonElement? chosenRelease = null;
            Version? highestVersion = null;
            DateTimeOffset highestPublishedAt = DateTimeOffset.MinValue;

            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                bool isDraft = item.TryGetProperty("draft", out JsonElement draftProp) && draftProp.ValueKind == JsonValueKind.True;
                if (isDraft || !item.TryGetProperty("tag_name", out JsonElement tagCheck))
                {
                    continue;
                }

                string? rawTag = tagCheck.GetString();
                if (string.IsNullOrWhiteSpace(rawTag))
                {
                    continue;
                }

                Version candidateVer = this.TryParseReleaseVersion(rawTag);
                DateTimeOffset candidateDate = DateTimeOffset.MinValue;
                if (item.TryGetProperty("published_at", out JsonElement pubProp) &&
                    DateTimeOffset.TryParse(pubProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedPub))
                {
                    candidateDate = parsedPub;
                }

                if (highestVersion is null ||
                    candidateVer > highestVersion ||
                    (candidateVer == highestVersion && candidateDate > highestPublishedAt))
                {
                    highestVersion = candidateVer;
                    highestPublishedAt = candidateDate;
                    chosenRelease = item;
                }
            }

            chosenRelease ??= doc.RootElement[0];
            JsonElement rel = chosenRelease.Value;

            string tagName = rel.TryGetProperty("tag_name", out JsonElement tagEl) && !string.IsNullOrWhiteSpace(tagEl.GetString())
                ? tagEl.GetString()!.Trim()
                : currentVerStr;

            string htmlUrl = rel.TryGetProperty("html_url", out JsonElement htmlEl) && !string.IsNullOrWhiteSpace(htmlEl.GetString())
                ? htmlEl.GetString()!.Trim()
                : ReleasesPageFallback;

            string rawDate = string.Empty;
            if (rel.TryGetProperty("published_at", out JsonElement pubEl) && !string.IsNullOrWhiteSpace(pubEl.GetString()))
            {
                rawDate = pubEl.GetString()!;
            }
            else if (rel.TryGetProperty("created_at", out JsonElement createdEl) && !string.IsNullOrWhiteSpace(createdEl.GetString()))
            {
                rawDate = createdEl.GetString()!;
            }

            string dateText = DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)
                ? dto.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "—";

            string preferredExt = OperatingSystem.IsMacOS() ? ".dmg" : ".msi";
            string? assetUrl = null;
            if (rel.TryGetProperty("assets", out JsonElement assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsEl.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out JsonElement nameEl) ? (nameEl.GetString() ?? string.Empty) : string.Empty;
                    string dlUrl = asset.TryGetProperty("browser_download_url", out JsonElement dlEl) ? (dlEl.GetString() ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(dlUrl) && assetName.EndsWith(preferredExt, StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = dlUrl;
                        break;
                    }
                }
            }

            Version remoteVersion = this.TryParseReleaseVersion(tagName);
            Version currentVersion = this.TryParseReleaseVersion(currentVerStr);
            bool isUpdateAvailable = remoteVersion > currentVersion;

            return new AppReleaseCheckResult(
                IsUpdateAvailable: isUpdateAvailable,
                CurrentVersion: currentVerStr,
                LatestTag: tagName,
                PublishedDateText: dateText,
                DownloadUrl: isUpdateAvailable ? (assetUrl ?? htmlUrl) : htmlUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AppReleaseCheckResult(
                IsUpdateAvailable: false,
                CurrentVersion: currentVerStr,
                LatestTag: currentVerStr,
                PublishedDateText: "—",
                DownloadUrl: ReleasesPageFallback,
                ErrorMessage: ex.Message);
        }
    }
}
