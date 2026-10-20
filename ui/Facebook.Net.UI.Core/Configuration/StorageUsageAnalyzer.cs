// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Facebook.Net.UI.Core.Configuration;

/// <summary>
/// Immutable domain value object representing a snapshot of disk storage and memory usage
/// for the application data directory and isolated Chromium profiles.
/// </summary>
public sealed record StorageUsageSnapshot(
    long TotalUsedBytes,
    long MainDatabaseBytes,
    long AuditDatabaseBytes,
    long BrowserProfilesBytes,
    long ConfigAndOtherBytes,
    long ProcessRamBytes,
    long DriveFreeBytes,
    IReadOnlyList<(string ProfileName, long SizeBytes)> ProfileBreakdown);

/// <summary>
/// Domain service contract for analyzing application disk/RAM usage, validating managed
/// profile path boundaries, and cleaning transient Chromium cache directories.
/// </summary>
public interface IStorageUsageService
{
    /// <summary>Formats a byte count into a concise human-readable string (<c>KB</c>, <c>MB</c>, or <c>GB</c>).</summary>
    string FormatByteSize(long bytes);

    /// <summary>Invalidates all cached profile directory size entries so subsequent queries re-scan disk.</summary>
    void InvalidateCache();

    /// <summary>Computes the total disk size in bytes of a specific profile folder using an in-memory TTL cache.</summary>
    long GetProfileDirectorySizeBytes(string? directoryPath, bool forceRefresh = false);

    /// <summary>Checks whether the specified <paramref name="directoryPath"/> is strictly inside the application's managed <c>Profiles</c> root directory.</summary>
    bool IsProfileInsideManagedRoot(string? directoryPath, string? profilesRootDirectory);

    /// <summary>Scans the application's local data directory and isolated Chromium profile folders to compute a <see cref="StorageUsageSnapshot"/>.</summary>
    StorageUsageSnapshot CalculateSnapshot(
        string dataDir,
        string profilesRoot,
        Func<string, string, string>? resolveFriendlyProfileLabel = null);

    /// <summary>Deletes transient Chromium disk caches across all managed profile folders under <paramref name="profilesRoot"/>.</summary>
    void CleanProfileCaches(string? profilesRoot);
}

/// <summary>
/// Default injectable implementation of <see cref="IStorageUsageService"/> delegating to <see cref="StorageUsageAnalyzer"/>.
/// </summary>
public sealed class StorageUsageService : IStorageUsageService
{
    /// <summary>Gets the shared stateless instance of <see cref="StorageUsageService"/>.</summary>
    public static StorageUsageService Instance { get; } = new();

    /// <inheritdoc />
    public string FormatByteSize(long bytes) =>
        StorageUsageAnalyzer.FormatByteSize(bytes);

    /// <inheritdoc />
    public void InvalidateCache() =>
        StorageUsageAnalyzer.InvalidateCache();

    /// <inheritdoc />
    public long GetProfileDirectorySizeBytes(string? directoryPath, bool forceRefresh = false) =>
        StorageUsageAnalyzer.GetProfileDirectorySizeBytes(directoryPath, forceRefresh);

    /// <inheritdoc />
    public bool IsProfileInsideManagedRoot(string? directoryPath, string? profilesRootDirectory) =>
        StorageUsageAnalyzer.IsProfileInsideManagedRoot(directoryPath, profilesRootDirectory);

    /// <inheritdoc />
    public StorageUsageSnapshot CalculateSnapshot(
        string dataDir,
        string profilesRoot,
        Func<string, string, string>? resolveFriendlyProfileLabel = null) =>
        StorageUsageAnalyzer.CalculateSnapshot(dataDir, profilesRoot, resolveFriendlyProfileLabel);

    /// <inheritdoc />
    public void CleanProfileCaches(string? profilesRoot) =>
        StorageUsageAnalyzer.CleanProfileCaches(profilesRoot);
}

/// <summary>
/// Single-responsibility domain service for analyzing application disk/RAM usage, validating managed
/// profile path boundaries, cleaning transient Chromium cache directories, and caching directory sizes
/// with a short TTL to prevent UI-thread disk I/O during real-time list filtering.
/// </summary>
public static class StorageUsageAnalyzer
{
    private static readonly long s_cacheTtlTicks = Stopwatch.Frequency * 15; // 15-second TTL
    private static readonly ConcurrentDictionary<string, (long Bytes, long TimestampTicks)> s_directorySizeCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] s_chromiumCacheRelativePaths =
    [
        Path.Combine("Default", "Cache"),
        Path.Combine("Default", "Code Cache"),
        Path.Combine("Default", "GPUCache"),
        Path.Combine("Default", "DawnCache"),
        Path.Combine("Default", "Service Worker", "CacheStorage"),
        "ShaderCache",
        "GrShaderCache",
        "GraphiteDawnCache"
    ];

    /// <summary>
    /// Formats a byte count into a concise human-readable string (<c>KB</c>, <c>MB</c>, or <c>GB</c>).
    /// </summary>
    public static string FormatByteSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 KB";
        }

        double kb = bytes / 1024.0;
        if (kb < 1024.0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1.0, Math.Round(kb)):N0} KB");
        }

        double mb = kb / 1024.0;
        if (mb < 1024.0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{mb:F1} MB");
        }

        double gb = mb / 1024.0;
        return string.Create(CultureInfo.InvariantCulture, $"{gb:F2} GB");
    }

    /// <summary>
    /// Invalidates all cached profile directory size entries so subsequent queries re-scan disk.
    /// </summary>
    public static void InvalidateCache() => s_directorySizeCache.Clear();

    /// <summary>
    /// Computes the total disk size in bytes of a specific profile folder, using an in-memory TTL cache
    /// when <paramref name="forceRefresh"/> is <see langword="false"/> to keep UI list filtering O(1).
    /// </summary>
    public static long GetProfileDirectorySizeBytes(string? directoryPath, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return 0;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        if (!forceRefresh &&
            s_directorySizeCache.TryGetValue(directoryPath, out (long Bytes, long TimestampTicks) cached) &&
            nowTicks - cached.TimestampTicks <= s_cacheTtlTicks)
        {
            return cached.Bytes;
        }

        long computed = CalculateDirectoryBytesCore(directoryPath);
        s_directorySizeCache[directoryPath] = (computed, nowTicks);
        return computed;
    }

    /// <summary>
    /// Checks whether the specified <paramref name="directoryPath"/> is strictly inside the application's managed <c>Profiles</c> root directory.
    /// </summary>
    public static bool IsProfileInsideManagedRoot(string? directoryPath, string? profilesRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(profilesRootDirectory))
        {
            return false;
        }

        try
        {
            string fullRoot = Path.GetFullPath(profilesRootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullTarget = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(fullTarget + Path.DirectorySeparatorChar, fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Scans the application's local data directory and isolated Chromium profile folders to compute a <see cref="StorageUsageSnapshot"/>.
    /// </summary>
    public static StorageUsageSnapshot CalculateSnapshot(
        string dataDir,
        string profilesRoot,
        Func<string, string, string>? resolveFriendlyProfileLabel = null)
    {
        long mainDbBytes = GetFileFamilyBytes(dataDir, "facebook_net.db");
        long auditDbBytes = GetFileFamilyBytes(dataDir, "facebook_audit.db");
        long configBytes = 0;

        if (!string.IsNullOrWhiteSpace(dataDir) && Directory.Exists(dataDir))
        {
            try
            {
                string[] topFiles = Directory.GetFiles(dataDir, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < topFiles.Length; i++)
                {
                    string fileName = Path.GetFileName(topFiles[i]);
                    if (fileName.StartsWith("facebook_net.db", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("facebook_audit.db", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    configBytes += SafeGetFileSize(topFiles[i]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible files
            }
        }

        long totalProfilesBytes = 0;
        List<(string ProfileName, long SizeBytes)> profileBreakdown = new();

        if (!string.IsNullOrWhiteSpace(profilesRoot) && Directory.Exists(profilesRoot))
        {
            try
            {
                string[] profileDirs = Directory.GetDirectories(profilesRoot);
                for (int i = 0; i < profileDirs.Length; i++)
                {
                    string dir = profileDirs[i];
                    string folderName = Path.GetFileName(dir);
                    long dirSize = GetProfileDirectorySizeBytes(dir, forceRefresh: true);
                    totalProfilesBytes += dirSize;
                    string friendlyName = resolveFriendlyProfileLabel is not null
                        ? resolveFriendlyProfileLabel(folderName, dir)
                        : folderName;
                    profileBreakdown.Add((friendlyName, dirSize));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible profile directories
            }
        }

        profileBreakdown.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        long totalUsed = mainDbBytes + auditDbBytes + totalProfilesBytes + configBytes;
        long processRam = Environment.WorkingSet;
        long driveFree = 0;

        try
        {
            string? rootPath = Path.GetPathRoot(dataDir);
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                DriveInfo drive = new(rootPath);
                if (drive.IsReady)
                {
                    driveFree = drive.AvailableFreeSpace;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Ignore drive query failure
        }

        return new StorageUsageSnapshot(
            TotalUsedBytes: totalUsed,
            MainDatabaseBytes: mainDbBytes,
            AuditDatabaseBytes: auditDbBytes,
            BrowserProfilesBytes: totalProfilesBytes,
            ConfigAndOtherBytes: configBytes,
            ProcessRamBytes: processRam,
            DriveFreeBytes: driveFree,
            ProfileBreakdown: profileBreakdown);
    }

    /// <summary>
    /// Deletes transient Chromium disk caches across all managed profile folders under <paramref name="profilesRoot"/>
    /// while preserving login cookies and session state.
    /// </summary>
    public static void CleanProfileCaches(string? profilesRoot)
    {
        if (string.IsNullOrWhiteSpace(profilesRoot) || !Directory.Exists(profilesRoot))
        {
            return;
        }

        try
        {
            string[] profileDirs = Directory.GetDirectories(profilesRoot);
            for (int i = 0; i < profileDirs.Length; i++)
            {
                string pDir = profileDirs[i];
                for (int c = 0; c < s_chromiumCacheRelativePaths.Length; c++)
                {
                    string targetCacheDir = Path.Combine(pDir, s_chromiumCacheRelativePaths[c]);
                    if (Directory.Exists(targetCacheDir))
                    {
                        try
                        {
                            Directory.Delete(targetCacheDir, recursive: true);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Ignore files currently locked by an active browser process
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore inaccessible root
        }
        finally
        {
            InvalidateCache();
        }
    }

    private static long GetFileFamilyBytes(string dir, string baseFileName)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return 0;
        }

        long sum = SafeGetFileSize(Path.Combine(dir, baseFileName));
        sum += SafeGetFileSize(Path.Combine(dir, $"{baseFileName}-wal"));
        sum += SafeGetFileSize(Path.Combine(dir, $"{baseFileName}-shm"));
        return sum;
    }

    private static long SafeGetFileSize(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                return new FileInfo(filePath).Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore locked or inaccessible file
        }

        return 0;
    }

    private static long CalculateDirectoryBytesCore(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                total += SafeGetFileSize(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore inaccessible subdirectories
        }

        return total;
    }
}
