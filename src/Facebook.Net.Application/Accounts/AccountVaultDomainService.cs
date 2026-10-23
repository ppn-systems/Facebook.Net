// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Meta.UI.Core.Accounts;

/// <summary>
/// Domain service contract for the Account Vault Bounded Context: inspects isolated Chromium cookie
/// stores for authenticated Facebook sessions and normalizes stored <see cref="AccountProfile"/> entities.
/// </summary>
public interface IAccountVaultDomainService
{
    /// <summary>Inspects the isolated or system Chromium <c>Cookies</c> database on disk to determine whether an authenticated <c>c_user</c> + <c>xs</c> session exists.</summary>
    bool HasProfileAuthenticatedCookieStore(string? profileDirectory);

    /// <summary>Normalizes an <see cref="AccountProfile"/>'s display name (stripping legacy parentheses) and promotes status to <see cref="AccountStatus.Active"/> when cookies exist.</summary>
    (AccountProfile Normalized, bool WasModified) NormalizeAccountProfile(AccountProfile account);

    /// <summary>
    /// Safely synchronizes a system Chromium profile subfolder (e.g. <c>User Data\Default</c> or <c>User Data\Profile 1</c>)
    /// into an isolated application-managed <c>--user-data-dir</c> root using <see cref="FileShare.ReadWrite"/> so it works
    /// even while the user's main browser is open and avoids Chromium's single-instance / default-dir CDP restrictions.
    /// </summary>
    bool TrySyncSystemProfileToIsolatedRoot(string sourceProfileDir, string targetIsolatedDir);

    /// <summary>Sanitizes a raw folder or profile name into a filesystem-safe directory slug without parentheses.</summary>
    string SanitizeProfileFolderSlug(string? raw);

    /// <summary>Resolves a human-friendly display label for a managed profile directory based on bound <see cref="AccountProfile"/> entities.</summary>
    string ResolveFriendlyProfileLabel(string folderName, string fullDirPath, IReadOnlyList<AccountProfile> accounts);

    /// <summary>Prunes unauthenticated orphan <c>login_*</c> and <c>profile_*</c> directories that are not bound to any account, returning the count of removed directories.</summary>
    int PruneOrphanPlaceholderProfiles(string? profilesRootDirectory, IReadOnlyList<AccountProfile> accounts);

    /// <summary>Prepares or synchronizes an isolated Chromium profile directory under <paramref name="profilesRootDirectory"/> from a raw folder name or existing system profile path.</summary>
    (string TargetIsolatedDir, string FriendlyLabel, bool SyncedFromSystem) PrepareIsolatedProfileDirectory(
        string rawNameOrPath,
        string profilesRootDirectory,
        string? friendlyProfileName = null);
}

/// <summary>
/// Default stateless implementation of <see cref="IAccountVaultDomainService"/>.
/// </summary>
public sealed class AccountVaultDomainService : IAccountVaultDomainService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="AccountVaultDomainService"/>.</summary>
    public static AccountVaultDomainService Instance { get; } = new();

    /// <inheritdoc />
    public bool HasProfileAuthenticatedCookieStore(string? profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory) || !Directory.Exists(profileDirectory))
        {
            return false;
        }

        string[] candidates =
        [
            Path.Combine(profileDirectory, "Default", "Network", "Cookies"),
            Path.Combine(profileDirectory, "Default", "Cookies"),
            Path.Combine(profileDirectory, "Network", "Cookies"),
            Path.Combine(profileDirectory, "Cookies")
        ];

        for (int i = 0; i < candidates.Length; i++)
        {
            string cookieDbPath = candidates[i];
            if (!File.Exists(cookieDbPath))
            {
                continue;
            }

            try
            {
                using FileStream fs = new(cookieDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length <= 0 || fs.Length > 16 * 1024 * 1024)
                {
                    continue;
                }

                byte[] bytes = new byte[(int)fs.Length];
                int read = fs.Read(bytes, 0, bytes.Length);
                string ascii = Encoding.Latin1.GetString(bytes, 0, read);
                if (ascii.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) &&
                    ascii.Contains("c_user", StringComparison.Ordinal) &&
                    ascii.Contains("xs", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore locked or unreadable cookie database
            }
        }

        return false;
    }

    /// <inheritdoc />
    public (AccountProfile Normalized, bool WasModified) NormalizeAccountProfile(AccountProfile account)
    {
        ArgumentNullException.ThrowIfNull(account);
        AccountProfile acc = account;
        bool modified = false;

        if (acc.DisplayName.Contains('(', StringComparison.Ordinal) || acc.DisplayName.Contains(')', StringComparison.Ordinal))
        {
            string cleanedName = acc.DisplayName
                .Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal)
                .Trim();
            if (cleanedName.StartsWith("Facebook ", StringComparison.OrdinalIgnoreCase))
            {
                cleanedName = $"FB {cleanedName["Facebook ".Length..].Trim()}";
            }

            acc = acc with { DisplayName = cleanedName };
            modified = true;
        }

        if (IsSystemBrowserUserDataPath(acc.ProfileDirectory))
        {
            string isolatedDir = this.ResolveSafeIsolatedProfileForAccount(acc.Uid);
            acc = acc with { ProfileDirectory = isolatedDir };
            modified = true;
        }

        if ((acc.Status == AccountStatus.CookieExpired || acc.Status == AccountStatus.Idle) &&
            (!string.IsNullOrWhiteSpace(acc.CookieHeader) || this.HasProfileAuthenticatedCookieStore(acc.ProfileDirectory)))
        {
            acc = acc with { Status = AccountStatus.Active };
            modified = true;
        }

        return (acc, modified);
    }

    private static bool IsSystemBrowserUserDataPath(string? profileDir)
    {
        if (string.IsNullOrWhiteSpace(profileDir))
        {
            return false;
        }

        string normalized = profileDir.Replace('/', '\\');
        if (normalized.Contains("\\User Data", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? parent = Path.GetDirectoryName(profileDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.IsNullOrWhiteSpace(parent) &&
               string.Equals(Path.GetFileName(parent), "User Data", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveSafeIsolatedProfileForAccount(string uid)
    {
        string profilesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Facebook.Net",
            "Profiles");
        _ = Directory.CreateDirectory(profilesRoot);

        string dedicatedDir = Path.Combine(profilesRoot, $"fb_{this.SanitizeProfileFolderSlug(uid)}");
        if (Directory.Exists(dedicatedDir) && this.HasProfileAuthenticatedCookieStore(dedicatedDir))
        {
            return dedicatedDir;
        }

        try
        {
            string[] existingDirs = Directory.GetDirectories(profilesRoot);
            for (int i = 0; i < existingDirs.Length; i++)
            {
                if (this.HasProfileAuthenticatedCookieStore(existingDirs[i]))
                {
                    return existingDirs[i];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore directory enumeration failure
        }

        _ = Directory.CreateDirectory(dedicatedDir);
        return dedicatedDir;
    }

    /// <inheritdoc />
    public bool TrySyncSystemProfileToIsolatedRoot(string sourceProfileDir, string targetIsolatedDir)
    {
        if (string.IsNullOrWhiteSpace(sourceProfileDir) ||
            string.IsNullOrWhiteSpace(targetIsolatedDir) ||
            !Directory.Exists(sourceProfileDir))
        {
            return false;
        }

        try
        {
            _ = Directory.CreateDirectory(targetIsolatedDir);
            string targetDefaultDir = Path.Combine(targetIsolatedDir, "Default");
            string targetNetworkDir = Path.Combine(targetDefaultDir, "Network");
            _ = Directory.CreateDirectory(targetDefaultDir);
            _ = Directory.CreateDirectory(targetNetworkDir);

            //Determine whether sourceProfileDir is already a root User Data folder or a profile subfolder (e.g. User Data\Default or User Data\Profile 1)
            string? parentDir = Directory.GetParent(sourceProfileDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
            string sourceLocalState = File.Exists(Path.Combine(sourceProfileDir, "Local State"))
                ? Path.Combine(sourceProfileDir, "Local State")
                : (!string.IsNullOrWhiteSpace(parentDir) ? Path.Combine(parentDir, "Local State") : string.Empty);

            if (!string.IsNullOrWhiteSpace(sourceLocalState) && File.Exists(sourceLocalState))
            {
                SafeCopyUnlocked(sourceLocalState, Path.Combine(targetIsolatedDir, "Local State"));
            }

            string sourceProfileSubDir = Directory.Exists(Path.Combine(sourceProfileDir, "Default"))
                ? Path.Combine(sourceProfileDir, "Default")
                : sourceProfileDir;

            SafeCopyUnlocked(Path.Combine(sourceProfileSubDir, "Preferences"), Path.Combine(targetDefaultDir, "Preferences"));
            SafeCopyUnlocked(Path.Combine(sourceProfileSubDir, "Secure Preferences"), Path.Combine(targetDefaultDir, "Secure Preferences"));

            string srcNetCookies = Path.Combine(sourceProfileSubDir, "Network", "Cookies");
            if (File.Exists(srcNetCookies))
            {
                SafeCopyUnlocked(srcNetCookies, Path.Combine(targetNetworkDir, "Cookies"));
                SafeCopyUnlocked($"{srcNetCookies}-wal", Path.Combine(targetNetworkDir, "Cookies-wal"));
                SafeCopyUnlocked($"{srcNetCookies}-shm", Path.Combine(targetNetworkDir, "Cookies-shm"));
            }

            string srcDirectCookies = Path.Combine(sourceProfileSubDir, "Cookies");
            if (File.Exists(srcDirectCookies))
            {
                SafeCopyUnlocked(srcDirectCookies, Path.Combine(targetDefaultDir, "Cookies"));
                SafeCopyUnlocked($"{srcDirectCookies}-wal", Path.Combine(targetDefaultDir, "Cookies-wal"));
                SafeCopyUnlocked($"{srcDirectCookies}-shm", Path.Combine(targetDefaultDir, "Cookies-shm"));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string SanitizeProfileFolderSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"Imported_{DateTimeOffset.UtcNow:yyyyMMdd}";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (Array.IndexOf(invalid, c) < 0 && c is not '(' and not ')' and not '—' and not '·')
            {
                _ = sb.Append(c == ' ' ? '_' : c);
            }
        }

        string slug = sb.ToString().Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? $"Imported_{DateTimeOffset.UtcNow:yyyyMMdd}" : slug;
    }

    /// <inheritdoc />
    public string ResolveFriendlyProfileLabel(string folderName, string fullDirPath, IReadOnlyList<AccountProfile> accounts)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(accounts);
        for (int i = 0; i < accounts.Count; i++)
        {
            AccountProfile acc = accounts[i];
            if (string.Equals(acc.ProfileDirectory, fullDirPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase))
            {
                bool isAutoGeneratedId =
                    folderName.StartsWith("login_", StringComparison.OrdinalIgnoreCase) ||
                    folderName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(acc.DisplayName, folderName, StringComparison.OrdinalIgnoreCase);

                return isAutoGeneratedId
                    ? acc.DisplayName
                    : $"{acc.DisplayName} · {folderName}";
            }
        }

        return folderName;
    }

    /// <inheritdoc />
    public int PruneOrphanPlaceholderProfiles(string? profilesRootDirectory, IReadOnlyList<AccountProfile> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (string.IsNullOrWhiteSpace(profilesRootDirectory) || !Directory.Exists(profilesRootDirectory))
        {
            return 0;
        }

        int prunedCount = 0;
        try
        {
            string[] managedDirs = Directory.GetDirectories(profilesRootDirectory);
            for (int i = 0; i < managedDirs.Length; i++)
            {
                string dir = managedDirs[i];
                string folderName = Path.GetFileName(dir);
                if (!folderName.StartsWith("login_", StringComparison.OrdinalIgnoreCase) &&
                    !folderName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isAssignedToAnyAccount = false;
                for (int a = 0; a < accounts.Count; a++)
                {
                    AccountProfile acc = accounts[a];
                    if (string.Equals(acc.ProfileDirectory, dir, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(acc.Uid, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        isAssignedToAnyAccount = true;
                        break;
                    }
                }

                if (!isAssignedToAnyAccount && !this.HasProfileAuthenticatedCookieStore(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        prunedCount++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Ignore folder locked by an active browser process
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore inaccessible directory
        }

        return prunedCount;
    }

    /// <inheritdoc />
    public (string TargetIsolatedDir, string FriendlyLabel, bool SyncedFromSystem) PrepareIsolatedProfileDirectory(
        string rawNameOrPath,
        string profilesRootDirectory,
        string? friendlyProfileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawNameOrPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);

        string trimmed = rawNameOrPath.Trim();
        string rawLeafName = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string friendlyLabel = !string.IsNullOrWhiteSpace(friendlyProfileName)
            ? friendlyProfileName.Trim()
            : (!string.IsNullOrWhiteSpace(rawLeafName) ? rawLeafName : trimmed);

        bool syncedFromSystem = false;
        string targetIsolatedDir;

        if (Path.IsPathRooted(trimmed) && Directory.Exists(trimmed))
        {
            string normalizedCandidate = Path.GetFullPath(trimmed)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(profilesRootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                targetIsolatedDir = trimmed;
            }
            else
            {
                string safeSlug = this.SanitizeProfileFolderSlug($"{friendlyLabel}_{rawLeafName}");
                targetIsolatedDir = Path.Combine(profilesRootDirectory, safeSlug);
                syncedFromSystem = this.TrySyncSystemProfileToIsolatedRoot(trimmed, targetIsolatedDir);
            }
        }
        else
        {
            string safeFolder = this.SanitizeProfileFolderSlug(trimmed);
            targetIsolatedDir = Path.Combine(profilesRootDirectory, safeFolder);
        }

        _ = Directory.CreateDirectory(targetIsolatedDir);
        return (targetIsolatedDir, friendlyLabel, syncedFromSystem);
    }

    private static void SafeCopyUnlocked(string sourceFile, string destinationFile)
    {
        if (!File.Exists(sourceFile))
        {
            return;
        }

        try
        {
            using FileStream src = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using FileStream dst = new(destinationFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            src.CopyTo(dst);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore locked or inaccessible auxiliary file
        }
    }
}
