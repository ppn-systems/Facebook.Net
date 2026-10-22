// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Importing;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Account Vault, Session Importing, Browser Login, Warmup, Isolated Browser Profile Management, Proxy Management, and Health Checks.
/// </summary>
[McpServerToolType]
public sealed class AccountTools(FacebookMcpSessionManager manager)
{
    private readonly FacebookMcpSessionManager _manager = manager;

    [McpServerTool(Name = "fb_list_accounts")]
    [Description("List all stored Facebook accounts in the vault with their UID, display name, status, proxy ID, and whether they are currently active.")]
    public async Task<string> ListAccountsAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);
        IReadOnlyList<AccountProfile> accounts = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);

        var items = accounts.Select(acc => new
        {
            uid = acc.Uid,
            display_name = acc.DisplayName,
            status = acc.Status.ToString(),
            proxy_id = acc.ProxyId,
            profile_directory = acc.ProfileDirectory,
            has_cookie_header = !string.IsNullOrWhiteSpace(acc.CookieHeader),
            has_cookie_store = _manager.AccountVault.HasProfileAuthenticatedCookieStore(acc.ProfileDirectory),
            last_active_at = acc.LastActiveAt?.ToString("o"),
            is_active = string.Equals(acc.Uid, _manager.ActiveAccountUid, StringComparison.Ordinal)
        });

        return JsonSerializer.Serialize(new
        {
            active_account_uid = _manager.ActiveAccountUid,
            total_count = accounts.Count,
            accounts = items
        });
    }

    [McpServerTool(Name = "fb_switch_account")]
    [Description("Switch the active Facebook account UID used by subsequent MCP tools.")]
    public async Task<string> SwitchAccountAsync(
        [Description("UID of the Facebook account to activate")] string accountUid,
        CancellationToken ct = default)
    {
        AccountProfile? switched = await _manager.SwitchActiveAccountAsync(accountUid, ct).ConfigureAwait(false);
        if (switched is not null)
        {
            _ = await _manager.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                switched,
                switched.DisplayName,
                $"MCP switched active account to '{switched.DisplayName}' ({switched.Uid}).",
                ct: ct).ConfigureAwait(false);
        }

        return JsonSerializer.Serialize(new
        {
            success = switched is not null,
            active_uid = switched?.Uid ?? _manager.ActiveAccountUid,
            display_name = switched?.DisplayName,
            message = switched is not null
                ? $"Switched active account to '{switched.DisplayName}' ({switched.Uid})."
                : $"Account UID '{accountUid}' not found."
        });
    }

    [McpServerTool(Name = "fb_import_session")]
    [Description("Import a Facebook account session from Cookie header, J2Team JSON, Cookie-Editor JSON, or pipe-delimited string (UID|Pass|2FA|Cookie), or create a blank browser profile.")]
    public async Task<string> ImportSessionAsync(
        [Description("Raw session string (Cookie header, JSON, or UID|Pass|2FA|Cookie). Leave empty to create a browser-login profile.")] string? rawSessionInput = null,
        [Description("Optional friendly display name for the account")] string? friendlyName = null,
        [Description("Optional proxy specification (e.g. host:port or host:port:user:pass or socks5://host:port)")] string? proxySpec = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(
            rawSessionInput,
            friendlyName,
            defaultBrowserLoginName: "Tài khoản trình duyệt",
            defaultImportedPrefix: "Tài khoản");

        string profileDir = Path.Combine(_manager.ProfilesRootDirectory, bundle.AccountUid);
        _ = Directory.CreateDirectory(profileDir);

        string resolvedUa = !string.IsNullOrWhiteSpace(bundle.UserAgent)
            ? bundle.UserAgent
            : (OperatingSystem.IsMacOS()
                ? ProtocolConstants.Protocol.DefaultMacOsUserAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent);

        string? proxyId = null;
        if (!string.IsNullOrWhiteSpace(proxySpec))
        {
            string candidateId = $"proxy_{bundle.AccountUid}";
            if (UniversalSessionImporter.TryParseProxySpec(proxySpec, candidateId, out ProxyConfig? parsedProxy) &&
                parsedProxy is not null)
            {
                await _manager.AccountStore.UpsertProxyAsync(parsedProxy, ct).ConfigureAwait(false);
                proxyId = parsedProxy.Id;
            }
        }

        AccountProfile profile = new(
            Uid: bundle.AccountUid,
            DisplayName: bundle.SuggestedDisplayName,
            CookieHeader: bundle.CookieHeader,
            UserAgent: resolvedUa,
            ProfileDirectory: profileDir,
            ProxyId: proxyId,
            TwoFactorSecret: bundle.TwoFactorSecret,
            Status: string.IsNullOrWhiteSpace(bundle.CookieHeader) ? AccountStatus.Idle : AccountStatus.Active,
            LastActiveAt: DateTimeOffset.UtcNow);

        await _manager.AccountStore.UpsertAccountAsync(profile, ct).ConfigureAwait(false);
        _ = await _manager.SwitchActiveAccountAsync(profile.Uid, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            profile,
            profile.DisplayName,
            $"MCP imported account session '{profile.DisplayName}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            uid = profile.Uid,
            display_name = profile.DisplayName,
            account_status = profile.Status.ToString(),
            profile_directory = profile.ProfileDirectory,
            proxy_id = profile.ProxyId
        });
    }

    [McpServerTool(Name = "fb_check_account_health")]
    [Description("Verify live Facebook session health for an account via Comet/CDP and update its status in SQLite.")]
    public async Task<string> CheckAccountHealthAsync(
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        (AccountProfile current, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        SessionMaterial? material;
        try
        {
            material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(false);
            material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
        }

        bool isHealthy = !string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0";
        AccountStatus resolvedStatus = isHealthy ? AccountStatus.Active : AccountStatus.CookieExpired;
        string realUid = isHealthy ? material.Uid : current.Uid;

        AccountProfile updated = current with
        {
            Uid = realUid,
            DisplayName = !string.IsNullOrWhiteSpace(material.DisplayName) ? material.DisplayName : current.DisplayName,
            CookieHeader = !string.IsNullOrWhiteSpace(material.CookieHeader) ? material.CookieHeader : current.CookieHeader,
            UserAgent = !string.IsNullOrWhiteSpace(material.UserAgent) ? material.UserAgent : current.UserAgent,
            Status = resolvedStatus,
            LastActiveAt = DateTimeOffset.UtcNow,
            AvatarUrl = !string.IsNullOrWhiteSpace(material.AvatarUrl) ? material.AvatarUrl : current.AvatarUrl
        };

        if (!string.Equals(current.Uid, realUid, StringComparison.Ordinal))
        {
            _ = await _manager.AccountStore.DeleteAccountAsync(current.Uid, ct).ConfigureAwait(false);
        }

        await _manager.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        _ = await _manager.SwitchActiveAccountAsync(updated.Uid, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            isHealthy ? AuditSeverity.Success : AuditSeverity.Warning,
            updated,
            updated.DisplayName,
            $"MCP verified session health for '{updated.DisplayName}' (healthy={isHealthy}, status={updated.Status}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            is_healthy = isHealthy,
            uid = updated.Uid,
            display_name = updated.DisplayName,
            status = updated.Status.ToString(),
            avatar_url = updated.AvatarUrl
        });
    }

    [McpServerTool(Name = "fb_set_account_proxy")]
    [Description("Assign, update, or clear the dedicated HTTP/SOCKS5 proxy for a Facebook account.")]
    public async Task<string> SetAccountProxyAsync(
        [Description("Proxy specification (host:port or host:port:user:pass or socks5://host:port). Pass empty string to clear proxy.")] string proxySpec,
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        AccountProfile current = await _manager.ResolveAccountAsync(accountUid, ct).ConfigureAwait(false);
        string trimmed = proxySpec?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (!string.IsNullOrWhiteSpace(current.ProxyId))
            {
                _ = await _manager.AccountStore.DeleteProxyAsync(current.ProxyId, ct).ConfigureAwait(false);
            }

            AccountProfile cleared = current with { ProxyId = null };
            await _manager.AccountStore.UpsertAccountAsync(cleared, ct).ConfigureAwait(false);
            await _manager.SessionManager.CloseSessionAsync(current.Uid, ct).ConfigureAwait(false);

            _ = await _manager.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                cleared,
                cleared.DisplayName,
                $"MCP cleared proxy for account '{cleared.DisplayName}'.",
                ct: ct).ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                uid = cleared.Uid,
                action = "proxy_cleared"
            });
        }

        string proxyId = $"proxy_{current.Uid}";
        if (!UniversalSessionImporter.TryParseProxySpec(trimmed, proxyId, out ProxyConfig? parsed) || parsed is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                message = "Invalid proxy format. Expected host:port or host:port:user:pass."
            });
        }

        await _manager.AccountStore.UpsertProxyAsync(parsed, ct).ConfigureAwait(false);
        AccountProfile updated = current with { ProxyId = parsed.Id };
        await _manager.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        await _manager.SessionManager.CloseSessionAsync(current.Uid, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            updated,
            $"{parsed.Host}:{parsed.Port}",
            $"MCP assigned {parsed.Protocol} proxy '{parsed.Host}:{parsed.Port}' to '{updated.DisplayName}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            uid = updated.Uid,
            proxy_id = parsed.Id,
            host = parsed.Host,
            port = parsed.Port,
            protocol = parsed.Protocol.ToString()
        });
    }

    [McpServerTool(Name = "fb_delete_account")]
    [Description("Delete a Facebook account from the local SQLite vault, close its active browser session, remove its proxy, and optionally delete its managed Chromium profile folder.")]
    public async Task<string> DeleteAccountAsync(
        [Description("UID of the Facebook account to delete")] string accountUid,
        [Description("If true, also deletes the isolated Chromium profile directory if managed inside Profiles/ and not shared by another account (default true)")] bool deleteManagedProfileFolder = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string cleanUid = accountUid.Trim();
        AccountProfile? existing = await _manager.AccountStore.GetAccountByUidAsync(cleanUid, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "not_found",
                uid = cleanUid,
                message = $"Account UID '{cleanUid}' not found."
            });
        }

        await _manager.SessionManager.CloseSessionAsync(existing.Uid, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existing.ProxyId))
        {
            _ = await _manager.AccountStore.DeleteProxyAsync(existing.ProxyId, ct).ConfigureAwait(false);
        }

        bool deleted = await _manager.AccountStore.DeleteAccountAsync(existing.Uid, ct).ConfigureAwait(false);
        bool profileFolderDeleted = false;

        if (deleteManagedProfileFolder &&
            !string.IsNullOrWhiteSpace(existing.ProfileDirectory) &&
            _manager.StorageUsage.IsProfileInsideManagedRoot(existing.ProfileDirectory, _manager.ProfilesRootDirectory) &&
            Directory.Exists(existing.ProfileDirectory))
        {
            IReadOnlyList<AccountProfile> remaining = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
            bool sharedWithOther = remaining.Any(a =>
                !string.Equals(a.Uid, existing.Uid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ProfileDirectory, existing.ProfileDirectory, StringComparison.OrdinalIgnoreCase));

            if (!sharedWithOther)
            {
                try
                {
                    Directory.Delete(existing.ProfileDirectory, recursive: true);
                    profileFolderDeleted = true;
                    _manager.StorageUsage.InvalidateCache();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore locked files
                }
            }
        }

        if (string.Equals(_manager.ActiveAccountUid, existing.Uid, StringComparison.Ordinal))
        {
            IReadOnlyList<AccountProfile> allRemaining = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
            if (allRemaining.Count > 0)
            {
                _ = await _manager.SwitchActiveAccountAsync(allRemaining[0].Uid, ct).ConfigureAwait(false);
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Info,
            existing,
            existing.DisplayName,
            $"MCP deleted account '{existing.DisplayName}' ({existing.Uid}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = deleted ? "deleted" : "failed",
            uid = existing.Uid,
            display_name = existing.DisplayName,
            profile_folder_deleted = profileFolderDeleted,
            active_account_uid = _manager.ActiveAccountUid
        });
    }

    [McpServerTool(Name = "fb_warmup_account")]
    [Description("Warm up a Facebook account session by browsing the News Feed via Comet GraphQL and naturally reacting (Like/Love) to recent feed posts, updating account health and LastActiveAt.")]
    public async Task<string> WarmupAccountAsync(
        [Description("Target Facebook account UID (optional, defaults to active account)")] string? accountUid = null,
        [Description("Maximum number of News Feed posts to react to during warmup (1..5, default 2)")] int maxReactions = 2,
        CancellationToken ct = default)
    {
        (AccountProfile current, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(accountUid, forceVisible: false, ct)
            .ConfigureAwait(false);

        SessionMaterial material;
        try
        {
            material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(false);
            material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
        }

        PagedResult<PostSummary> newsFeed = await _manager.CometClient
            .GetNewsFeedAsync(session, count: 6, ct: ct)
            .ConfigureAwait(false);

        int targetReactCount = Math.Clamp(maxReactions, 1, 5);
        int reactedCount = 0;

        for (int i = 0; i < newsFeed.Items.Count && reactedCount < targetReactCount; i++)
        {
            string candidatePostId = newsFeed.Items[i].PostId;
            if (string.IsNullOrWhiteSpace(candidatePostId))
            {
                continue;
            }

            ReactionKind reaction = Random.Shared.NextDouble() < 0.75 ? ReactionKind.Like : ReactionKind.Love;
            bool ok = await _manager.CometClient.ReactToPostAsync(session, candidatePostId, reaction, ct).ConfigureAwait(false);
            if (ok)
            {
                reactedCount++;
            }
        }

        string realUid = !string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0" ? material.Uid : current.Uid;
        AccountProfile updated = current with
        {
            Uid = realUid,
            DisplayName = !string.IsNullOrWhiteSpace(material.DisplayName) ? material.DisplayName : current.DisplayName,
            CookieHeader = !string.IsNullOrWhiteSpace(material.CookieHeader) ? material.CookieHeader : current.CookieHeader,
            UserAgent = !string.IsNullOrWhiteSpace(material.UserAgent) ? material.UserAgent : current.UserAgent,
            AvatarUrl = !string.IsNullOrWhiteSpace(material.AvatarUrl) ? material.AvatarUrl : current.AvatarUrl,
            Status = AccountStatus.Active,
            LastActiveAt = DateTimeOffset.UtcNow
        };

        if (!string.Equals(current.Uid, realUid, StringComparison.Ordinal))
        {
            _ = await _manager.AccountStore.DeleteAccountAsync(current.Uid, ct).ConfigureAwait(false);
        }

        await _manager.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        _ = await _manager.SwitchActiveAccountAsync(updated.Uid, ct).ConfigureAwait(false);

        _ = await _manager.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Success,
            updated,
            updated.DisplayName,
            $"MCP warmed up account '{updated.DisplayName}' ({newsFeed.Items.Count} feed posts loaded, {reactedCount} reactions).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            uid = updated.Uid,
            display_name = updated.DisplayName,
            account_status = updated.Status.ToString(),
            news_feed_posts_loaded = newsFeed.Items.Count,
            reactions_sent = reactedCount
        });
    }

    [McpServerTool(Name = "fb_open_browser_login")]
    [Description("Open a visible Chromium browser window for interactive Facebook sign-in or session verification, extract live session tokens/UID/avatar, and optionally sync joined groups.")]
    public async Task<string> OpenBrowserLoginAsync(
        [Description("Target Facebook account UID (optional, defaults to active account or creates a new browser login profile if none exists)")] string? accountUid = null,
        [Description("Whether to automatically sync joined Facebook groups into target_groups after login succeeds (default true)")] bool syncJoinedGroupsAfterLogin = true,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        AccountProfile targetAccount;
        IReadOnlyList<AccountProfile> allAccounts = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(accountUid))
        {
            AccountProfile? found = await _manager.AccountStore.GetAccountByUidAsync(accountUid.Trim(), ct).ConfigureAwait(false);
            if (found is not null)
            {
                targetAccount = found;
            }
            else
            {
                string profileDir = Path.Combine(_manager.ProfilesRootDirectory, accountUid.Trim());
                _ = Directory.CreateDirectory(profileDir);
                targetAccount = new AccountProfile(
                    Uid: accountUid.Trim(),
                    DisplayName: $"Tài khoản {accountUid.Trim()}",
                    CookieHeader: string.Empty,
                    UserAgent: OperatingSystem.IsMacOS() ? ProtocolConstants.Protocol.DefaultMacOsUserAgent : ProtocolConstants.Protocol.DefaultWindowsUserAgent,
                    ProfileDirectory: profileDir,
                    Status: AccountStatus.Idle,
                    LastActiveAt: DateTimeOffset.UtcNow);
                await _manager.AccountStore.UpsertAccountAsync(targetAccount, ct).ConfigureAwait(false);
            }
        }
        else if (allAccounts.Count > 0)
        {
            targetAccount = await _manager.ResolveAccountAsync(null, ct).ConfigureAwait(false);
        }
        else
        {
            ImportedSessionBundle bundle = UniversalSessionImporter.Parse(
                null,
                null,
                defaultBrowserLoginName: "Tài khoản trình duyệt",
                defaultImportedPrefix: "Tài khoản");
            string profileDir = Path.Combine(_manager.ProfilesRootDirectory, bundle.AccountUid);
            _ = Directory.CreateDirectory(profileDir);
            targetAccount = new AccountProfile(
                Uid: bundle.AccountUid,
                DisplayName: bundle.SuggestedDisplayName,
                CookieHeader: string.Empty,
                UserAgent: OperatingSystem.IsMacOS() ? ProtocolConstants.Protocol.DefaultMacOsUserAgent : ProtocolConstants.Protocol.DefaultWindowsUserAgent,
                ProfileDirectory: profileDir,
                Status: AccountStatus.Idle,
                LastActiveAt: DateTimeOffset.UtcNow);
            await _manager.AccountStore.UpsertAccountAsync(targetAccount, ct).ConfigureAwait(false);
        }

        (AccountProfile current, IBrowserSession session) = await _manager
            .AcquireBrowserSessionAsync(targetAccount.Uid, forceVisible: true, ct)
            .ConfigureAwait(false);

        await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(false);

        SessionMaterial? material = null;
        for (int attempt = 0; attempt < 15 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                material = await session.ExtractMaterialAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0")
                {
                    break;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // User may still be logging in interactively
            }

            await Task.Delay(3000, ct).ConfigureAwait(false);
        }

        bool isAuthenticated = material is not null && !string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0";
        string realUid = isAuthenticated ? material!.Uid : current.Uid;

        AccountProfile updated = current with
        {
            Uid = realUid,
            DisplayName = isAuthenticated && !string.IsNullOrWhiteSpace(material!.DisplayName) ? material.DisplayName : current.DisplayName,
            CookieHeader = isAuthenticated && !string.IsNullOrWhiteSpace(material!.CookieHeader) ? material.CookieHeader : current.CookieHeader,
            UserAgent = isAuthenticated && !string.IsNullOrWhiteSpace(material!.UserAgent) ? material.UserAgent : current.UserAgent,
            AvatarUrl = isAuthenticated && !string.IsNullOrWhiteSpace(material!.AvatarUrl) ? material.AvatarUrl : current.AvatarUrl,
            Status = isAuthenticated ? AccountStatus.Active : current.Status,
            LastActiveAt = DateTimeOffset.UtcNow
        };

        if (!string.Equals(current.Uid, realUid, StringComparison.Ordinal))
        {
            _ = await _manager.AccountStore.DeleteAccountAsync(current.Uid, ct).ConfigureAwait(false);
        }

        await _manager.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        _ = await _manager.SwitchActiveAccountAsync(updated.Uid, ct).ConfigureAwait(false);

        int syncedGroupsCount = 0;
        if (isAuthenticated && syncJoinedGroupsAfterLogin)
        {
            try
            {
                PagedResult<GroupSummary> joined = await _manager.CometClient
                    .GetJoinedGroupsAsync(session, count: 100, ct: ct)
                    .ConfigureAwait(false);

                IReadOnlyList<SavedTargetGroup> existingGroups = await _manager.AccountStore.GetAllTargetGroupsAsync(ct).ConfigureAwait(false);
                HashSet<string> existingIds = new(existingGroups.Select(g => g.GroupId), StringComparer.Ordinal);

                for (int i = 0; i < joined.Items.Count; i++)
                {
                    GroupSummary g = joined.Items[i];
                    if (!existingIds.Contains(g.GroupId))
                    {
                        await _manager.AccountStore.UpsertTargetGroupAsync(
                            new SavedTargetGroup(g.GroupId, g.Name, IsEnabled: true, Notes: null, AvatarUrl: g.AvatarUrl),
                            ct).ConfigureAwait(false);
                        syncedGroupsCount++;
                    }
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Ignore group sync failure during login
            }
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            isAuthenticated ? AuditSeverity.Success : AuditSeverity.Info,
            updated,
            updated.DisplayName,
            $"MCP opened interactive browser login for '{updated.DisplayName}' (authenticated={isAuthenticated}, syncedNewGroups={syncedGroupsCount}).",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = isAuthenticated ? "authenticated" : "browser_opened_pending_login",
            uid = updated.Uid,
            display_name = updated.DisplayName,
            account_status = updated.Status.ToString(),
            synced_new_groups_count = syncedGroupsCount
        });
    }

    [McpServerTool(Name = "fb_list_browser_profiles")]
    [Description("List all isolated application-managed Chromium browser profiles under Profiles/ as well as discovered system browser profiles (Edge/Chrome/Brave) with their Facebook login status and disk size.")]
    public async Task<string> ListBrowserProfilesAsync(CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        PreferredBrowserKind effectiveKind = _manager.Settings.ResolveEffectiveBrowserKind();
        List<BrowserProfileOption> profiles = AppSettingsService.DiscoverBrowserProfiles(
            _manager.ProfilesRootDirectory,
            effectiveKind);

        var items = profiles.Select(p =>
        {
            long bytes = p.IsManagedByApp
                ? StorageUsageAnalyzer.GetProfileDirectorySizeBytes(p.DirectoryPath)
                : 0L;
            return new
            {
                display_name = p.DisplayName,
                directory_path = p.DirectoryPath,
                is_managed_by_app = p.IsManagedByApp,
                has_facebook_session = p.HasFacebookSession,
                size_bytes = bytes,
                size_formatted = p.IsManagedByApp ? _manager.StorageUsage.FormatByteSize(bytes) : "System"
            };
        });

        return JsonSerializer.Serialize(new
        {
            effective_browser_kind = effectiveKind.ToString(),
            profiles_root_directory = _manager.ProfilesRootDirectory,
            total_count = profiles.Count,
            profiles = items
        });
    }

    [McpServerTool(Name = "fb_create_or_assign_browser_profile")]
    [Description("Create a new isolated Chromium browser profile folder under Profiles/ (or clone/sync a system browser profile into an isolated folder) and bind it to a Facebook account.")]
    public async Task<string> CreateOrAssignBrowserProfileAsync(
        [Description("Profile folder name to create under Profiles/ OR full path to an existing system browser profile folder to sync")] string profileNameOrPath,
        [Description("Optional target Facebook account UID to bind the profile to (if omitted, binds to active account or creates a new account)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileNameOrPath);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        (string targetIsolatedDir, string friendlyLabel, bool syncedFromSystem) = _manager.AccountVault.PrepareIsolatedProfileDirectory(
            profileNameOrPath,
            _manager.ProfilesRootDirectory);

        _manager.StorageUsage.InvalidateCache();
        bool hasFbCookies = _manager.AccountVault.HasProfileAuthenticatedCookieStore(targetIsolatedDir);

        AccountProfile? targetAccount = null;
        if (!string.IsNullOrWhiteSpace(accountUid))
        {
            targetAccount = await _manager.AccountStore.GetAccountByUidAsync(accountUid.Trim(), ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(_manager.ActiveAccountUid))
        {
            targetAccount = await _manager.AccountStore.GetAccountByUidAsync(_manager.ActiveAccountUid, ct).ConfigureAwait(false);
        }

        if (targetAccount is not null)
        {
            await _manager.SessionManager.CloseSessionAsync(targetAccount.Uid, ct).ConfigureAwait(false);
            AccountProfile updated = targetAccount with
            {
                ProfileDirectory = targetIsolatedDir,
                Status = hasFbCookies ? AccountStatus.Active : targetAccount.Status,
                LastActiveAt = DateTimeOffset.UtcNow
            };
            await _manager.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
            targetAccount = updated;
        }
        else
        {
            string newUid = $"browser_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            targetAccount = new AccountProfile(
                Uid: newUid,
                DisplayName: friendlyLabel,
                CookieHeader: string.Empty,
                UserAgent: OperatingSystem.IsMacOS() ? ProtocolConstants.Protocol.DefaultMacOsUserAgent : ProtocolConstants.Protocol.DefaultWindowsUserAgent,
                ProfileDirectory: targetIsolatedDir,
                Status: hasFbCookies ? AccountStatus.Active : AccountStatus.Idle,
                LastActiveAt: DateTimeOffset.UtcNow);
            await _manager.AccountStore.UpsertAccountAsync(targetAccount, ct).ConfigureAwait(false);
            _ = await _manager.SwitchActiveAccountAsync(targetAccount.Uid, ct).ConfigureAwait(false);
        }

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Success,
            targetAccount,
            friendlyLabel,
            $"MCP bound browser profile '{friendlyLabel}' to account '{targetAccount.DisplayName}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            account_uid = targetAccount.Uid,
            display_name = targetAccount.DisplayName,
            profile_directory = targetIsolatedDir,
            synced_from_system = syncedFromSystem,
            has_facebook_session = hasFbCookies
        });
    }

    [McpServerTool(Name = "fb_delete_browser_profile")]
    [Description("Safely delete an isolated application-managed Chromium browser profile folder inside Profiles/ (strictly refuses to delete any system browser folder outside Profiles/).")]
    public async Task<string> DeleteBrowserProfileAsync(
        [Description("Profile folder name inside Profiles/ or full directory path inside Profiles/")] string profileNameOrDirectoryPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileNameOrDirectoryPath);
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        string trimmed = profileNameOrDirectoryPath.Trim();
        string targetDir = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(_manager.ProfilesRootDirectory, trimmed);

        if (!_manager.StorageUsage.IsProfileInsideManagedRoot(targetDir, _manager.ProfilesRootDirectory))
        {
            return JsonSerializer.Serialize(new
            {
                status = "forbidden",
                directory_path = targetDir,
                message = "Refusing to delete directory outside the application-managed Profiles/ root."
            });
        }

        if (!Directory.Exists(targetDir))
        {
            return JsonSerializer.Serialize(new
            {
                status = "not_found",
                directory_path = targetDir
            });
        }

        IReadOnlyList<AccountProfile> accounts = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
        for (int i = 0; i < accounts.Count; i++)
        {
            if (string.Equals(accounts[i].ProfileDirectory, targetDir, StringComparison.OrdinalIgnoreCase))
            {
                await _manager.SessionManager.CloseSessionAsync(accounts[i].Uid, ct).ConfigureAwait(false);
            }
        }

        Directory.Delete(targetDir, recursive: true);
        _manager.StorageUsage.InvalidateCache();

        _ = await _manager.LogAuditAsync(
            AuditCategory.Personal,
            AuditSeverity.Info,
            account: null,
            Path.GetFileName(targetDir),
            $"MCP deleted managed browser profile folder '{Path.GetFileName(targetDir)}'.",
            ct: ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            status = "deleted",
            directory_path = targetDir
        });
    }

    [McpServerTool(Name = "fb_get_account_safety_quota")]
    [Description("Evaluate anti-checkpoint safety and rate-limit quotas across all stored Facebook accounts (or a specific accountUid) based on recent action history (posts, comments, bumps, group joins, friend requests in the last 1h and 24h) and recommend the safest account for the next batch campaign.")]
    public async Task<string> GetAccountSafetyQuotaAsync(
        [Description("Optional target Facebook account UID (if omitted, evaluates all accounts in the vault)")] string? accountUid = null,
        CancellationToken ct = default)
    {
        await _manager.EnsureInitializedAsync(ct).ConfigureAwait(false);

        IReadOnlyList<AccountProfile> allAccounts = await _manager.AccountStore.GetAllAccountsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<ActionHistoryEntry> recentActions = await _manager.AccountStore.GetRecentActionsAsync(2000, ct).ConfigureAwait(false);

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset oneHourAgo = nowUtc.AddHours(-1);
        DateTimeOffset twentyFourHoursAgo = nowUtc.AddHours(-24);

        List<AccountProfile> selectedAccounts = string.IsNullOrWhiteSpace(accountUid)
            ? allAccounts.ToList()
            : allAccounts.Where(a => string.Equals(a.Uid, accountUid.Trim(), StringComparison.Ordinal)).ToList();

        List<object> evaluations = [];
        string? recommendedSafestUid = null;
        int lowestHeatScore = int.MaxValue;

        for (int i = 0; i < selectedAccounts.Count; i++)
        {
            AccountProfile acc = selectedAccounts[i];
            int posts1h = 0, posts24h = 0;
            int comments1h = 0, comments24h = 0;
            int joins1h = 0, joins24h = 0;
            int friendReqs1h = 0, friendReqs24h = 0;
            DateTimeOffset? lastActionAt = null;

            for (int a = 0; a < recentActions.Count; a++)
            {
                ActionHistoryEntry entry = recentActions[a];
                if (!string.Equals(entry.AccountUid, acc.Uid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!lastActionAt.HasValue || entry.ExecutedAt > lastActionAt.Value)
                {
                    lastActionAt = entry.ExecutedAt;
                }

                if (entry.ExecutedAt < twentyFourHoursAgo)
                {
                    continue;
                }

                bool inLastHour = entry.ExecutedAt >= oneHourAgo;
                if (string.Equals(entry.ActionKind, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    posts24h++;
                    if (inLastHour)
                    {
                        posts1h++;
                    }
                }
                else if (string.Equals(entry.ActionKind, "COMMENT", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(entry.ActionKind, "BUMP_COMMENT", StringComparison.OrdinalIgnoreCase))
                {
                    comments24h++;
                    if (inLastHour)
                    {
                        comments1h++;
                    }
                }
                else if (string.Equals(entry.ActionKind, DomainActionKinds.JoinGroup, StringComparison.OrdinalIgnoreCase))
                {
                    joins24h++;
                    if (inLastHour)
                    {
                        joins1h++;
                    }
                }
                else if (string.Equals(entry.ActionKind, "FRIEND_REQUEST", StringComparison.OrdinalIgnoreCase))
                {
                    friendReqs24h++;
                    if (inLastHour)
                    {
                        friendReqs1h++;
                    }
                }
            }

            int heatScore = (posts1h * 6) + (comments1h * 4) + (joins1h * 5) + (friendReqs1h * 3)
                          + (posts24h * 2) + comments24h + (joins24h * 2) + friendReqs24h;

            string safetyState = acc.Status is AccountStatus.Checkpoint or AccountStatus.Disabled or AccountStatus.CookieExpired
                ? "UNAVAILABLE"
                : heatScore >= 80 || posts1h >= 12 || comments1h >= 20
                    ? "COOLDOWN_RECOMMENDED"
                    : heatScore >= 35 || posts1h >= 6 || comments1h >= 10
                        ? "MODERATE"
                        : "SAFE";

            if (safetyState is "SAFE" or "MODERATE" && heatScore < lowestHeatScore)
            {
                lowestHeatScore = heatScore;
                recommendedSafestUid = acc.Uid;
            }

            evaluations.Add(new
            {
                uid = acc.Uid,
                display_name = acc.DisplayName,
                account_status = acc.Status.ToString(),
                is_active = string.Equals(acc.Uid, _manager.ActiveAccountUid, StringComparison.Ordinal),
                safety_state = safetyState,
                heat_score = heatScore,
                last_action_at = lastActionAt?.ToString("o"),
                last_1h = new
                {
                    posts = posts1h,
                    comments_and_bumps = comments1h,
                    group_joins = joins1h,
                    friend_requests = friendReqs1h
                },
                last_24h = new
                {
                    posts = posts24h,
                    comments_and_bumps = comments24h,
                    group_joins = joins24h,
                    friend_requests = friendReqs24h
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            status = "success",
            active_account_uid = _manager.ActiveAccountUid,
            recommended_safest_account_uid = recommendedSafestUid,
            accounts_evaluated = evaluations.Count,
            quotas = evaluations
        });
    }
}
