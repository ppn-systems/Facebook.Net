// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.UI.Core.Accounts;

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
                cleanedName = $"Tài khoản {cleanedName["Facebook ".Length..].Trim()}";
            }

            acc = acc with { DisplayName = cleanedName };
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
