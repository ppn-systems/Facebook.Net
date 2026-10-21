// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Text.Json;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Importing;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp.Tools;

/// <summary>
/// MCP Tools for Facebook Account Vault, Session Importing, Browser Login, Proxy Management, and Health Checks.
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
}
