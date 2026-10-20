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
    /// <summary>Inspects the isolated Chromium <c>Cookies</c> database on disk to determine whether an authenticated <c>c_user</c> + <c>xs</c> session exists.</summary>
    bool HasProfileAuthenticatedCookieStore(string? profileDirectory);

    /// <summary>Normalizes an <see cref="AccountProfile"/>'s display name (stripping legacy parentheses) and promotes status to <see cref="AccountStatus.Active"/> when cookies exist.</summary>
    (AccountProfile Normalized, bool WasModified) NormalizeAccountProfile(AccountProfile account);
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
            Path.Combine(profileDirectory, "Default", "Cookies")
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
}
