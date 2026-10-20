// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Importing;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling Zero-UID Account Management,
/// Universal J2Team/Cookie importing, and 1-Click Browser Login with automatic Group discovery.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets or sets the currently selected account profile.</summary>
    [ObservableProperty]
    public partial AccountProfile? SelectedAccount { get; set; }

    /// <summary>Gets or sets the friendly account label entered by the user (optional).</summary>
    [ObservableProperty]
    public partial string AccountFriendlyNameInput { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional J2Team JSON or raw Cookie string pasted by the user.</summary>
    [ObservableProperty]
    public partial string SessionImportRawInput { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional dedicated proxy string (<c>host:port</c> or <c>socks5://host:port:user:pass</c>) for the account.</summary>
    [ObservableProperty]
    public partial string AccountProxyInput { get; set; } = string.Empty;

    /// <summary>Gets or sets the local search/filter text for the accounts list.</summary>
    [ObservableProperty]
    public partial string AccountFilterText { get; set; } = string.Empty;

    private readonly List<AccountProfile> _allAccounts = new();

    /// <summary>
    /// Filters the displayed <see cref="Accounts"/> collection locally by keyword and optional status.
    /// </summary>
    public void ApplyLocalAccountFilter(string? filterText, AccountStatus? statusFilter = null)
    {
        if (this._allAccounts.Count == 0 && this.Accounts.Count > 0)
        {
            for (int i = 0; i < this.Accounts.Count; i++)
            {
                this._allAccounts.Add(this.Accounts[i]);
            }
        }

        this.AccountFilterText = filterText ?? string.Empty;
        string kw = this.AccountFilterText.Trim();

        if (string.IsNullOrWhiteSpace(kw) && statusFilter is null)
        {
            this.Accounts.ReplaceAll(this._allAccounts);
            return;
        }

        List<AccountProfile> filtered = new(this._allAccounts.Count);
        for (int i = 0; i < this._allAccounts.Count; i++)
        {
            AccountProfile acc = this._allAccounts[i];
            if (statusFilter is not null && acc.Status != statusFilter.Value)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(kw) &&
                !acc.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(acc);
        }

        this.Accounts.ReplaceAll(filtered);
    }

    partial void OnSelectedAccountChanged(AccountProfile? value)
    {
        if (value is not null && !this._isLoadingSettings)
        {
            this.FlushSettingsToDisk();
        }

        if (value is null || string.IsNullOrWhiteSpace(value.ProxyId))
        {
            this.AccountProxyInput = string.Empty;
            return;
        }

        _ = this.LoadSelectedAccountProxyTextAsync(value.ProxyId);
    }

    private async Task LoadSelectedAccountProxyTextAsync(string proxyId)
    {
        IReadOnlyList<ProxyConfig> proxies = await this._context.AccountStore.GetAllProxiesAsync().ConfigureAwait(true);
        for (int i = 0; i < proxies.Count; i++)
        {
            if (string.Equals(proxies[i].Id, proxyId, StringComparison.Ordinal))
            {
                this.AccountProxyInput = UniversalSessionImporter.FormatProxySpec(proxies[i]);
                return;
            }
        }

        this.AccountProxyInput = string.Empty;
    }

    private async Task ReloadAllStoredDataAsync(string? preferredUid = null)
    {
        string? targetUid = preferredUid
            ?? this.SelectedAccount?.Uid
            ?? this._context.Settings.Current.LastSelectedAccountUid;
        IReadOnlyList<AccountProfile> storedAccounts = await this._context.AccountStore.GetAllAccountsAsync().ConfigureAwait(true);

        List<AccountProfile> normalizedAccounts = new(storedAccounts.Count);
        for (int i = 0; i < storedAccounts.Count; i++)
        {
            AccountProfile acc = storedAccounts[i];
            bool modified = false;

            if (acc.DisplayName.Contains('(', StringComparison.Ordinal) || acc.DisplayName.Contains(')', StringComparison.Ordinal))
            {
                string cleanedName = acc.DisplayName.Replace("(", string.Empty, StringComparison.Ordinal).Replace(")", string.Empty, StringComparison.Ordinal).Trim();
                if (cleanedName.StartsWith("Facebook ", StringComparison.OrdinalIgnoreCase))
                {
                    cleanedName = $"Tài khoản {cleanedName["Facebook ".Length..].Trim()}";
                }

                acc = acc with { DisplayName = cleanedName };
                modified = true;
            }

            if ((acc.Status == AccountStatus.CookieExpired || acc.Status == AccountStatus.Idle) &&
                (!string.IsNullOrWhiteSpace(acc.CookieHeader) || HasProfileAuthenticatedCookieStore(acc.ProfileDirectory)))
            {
                acc = acc with { Status = AccountStatus.Active };
                modified = true;
            }

            if (modified)
            {
                await this._context.AccountStore.UpsertAccountAsync(acc).ConfigureAwait(true);
            }

            normalizedAccounts.Add(acc);
        }

        this._allAccounts.Clear();
        this._allAccounts.AddRange(normalizedAccounts);
        this.Accounts.ReplaceAll(normalizedAccounts);

        AccountProfile? matched = null;
        for (int i = 0; i < normalizedAccounts.Count; i++)
        {
            AccountProfile acc = normalizedAccounts[i];
            if (!string.IsNullOrWhiteSpace(targetUid) &&
                string.Equals(acc.Uid, targetUid, StringComparison.Ordinal))
            {
                matched = acc;
                break;
            }
        }

        if (matched is not null)
        {
            this.SelectedAccount = matched;
        }
        else if (this.Accounts.Count > 0)
        {
            this.SelectedAccount = this.Accounts[0];
        }
        else
        {
            this.SelectedAccount = null;
        }

        IReadOnlyList<SavedTargetGroup> storedGroups = await this._context.AccountStore.GetAllTargetGroupsAsync().ConfigureAwait(true);
        this.TargetGroups.ReplaceAll(storedGroups);
        if (this.SelectedTargetGroup is null && this.TargetGroups.Count > 0)
        {
            this.SelectedTargetGroup = this.TargetGroups[0];
        }

        // Only seed _allCatalogGroups and JoinedGroups from storedGroups if no live catalog has been loaded yet
        if (this._allCatalogGroups.Count == 0)
        {
            for (int i = 0; i < storedGroups.Count; i++)
            {
                GroupSummary summary = new(storedGroups[i].GroupId, storedGroups[i].GroupName, GroupMembershipState.Member, storedGroups[i].AvatarUrl);
                this._allCatalogGroups.Add(summary);
            }

            this.JoinedGroups.ReplaceAll(this._allCatalogGroups);
        }

        if (this.SelectedGroup is null && this.JoinedGroups.Count > 0)
        {
            this.SelectedGroup = this.JoinedGroups[0];
        }

        await this.ReloadGroupTagsOnlyAsync().ConfigureAwait(true);
        await this.ReloadTemplatesOnlyAsync().ConfigureAwait(true);
        await this.ReloadPublishedPostsOnlyAsync().ConfigureAwait(true);
        await this.QueryLocalScrapedPostsAsync().ConfigureAwait(true);
        this.SyncBackgroundScraperLoop();
    }

    private async Task ReloadTemplatesOnlyAsync()
    {
        IReadOnlyList<CampaignTemplate> storedTemplates = await this._context.AccountStore.GetAllCampaignTemplatesAsync().ConfigureAwait(true);
        this.SavedTemplates.Clear();
        for (int i = 0; i < storedTemplates.Count; i++)
        {
            this.SavedTemplates.Add(storedTemplates[i]);
        }
    }

    private static bool HasProfileAuthenticatedCookieStore(string? profileDirectory)
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
                string ascii = System.Text.Encoding.Latin1.GetString(bytes, 0, read);
                if (ascii.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) &&
                    ascii.Contains("c_user", StringComparison.Ordinal) &&
                    ascii.Contains("xs", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore locked or unreadable cookie database
            }
        }

        return false;
    }

    /// <summary>
    /// Imports a session from J2Team JSON, Cookie-Editor JSON, pipe-delimited string, or creates a new browser login profile.
    /// </summary>
    public async Task QuickImportSessionAsync()
    {
        ImportedSessionBundle bundle = UniversalSessionImporter.Parse(
            this.SessionImportRawInput,
            this.AccountFriendlyNameInput,
            this._context.Localization["Accounts.DefaultBrowserLoginName"],
            this._context.Localization["Accounts.DefaultImportedName"]);

        string profileDir = Path.Combine(this._context.ProfilesRootDirectory, bundle.AccountUid);
        _ = Directory.CreateDirectory(profileDir);

        string resolvedUa = !string.IsNullOrWhiteSpace(bundle.UserAgent)
            ? bundle.UserAgent
            : (OperatingSystem.IsMacOS()
                ? ProtocolConstants.Protocol.DefaultMacOsUserAgent
                : ProtocolConstants.Protocol.DefaultWindowsUserAgent);

        string? proxyId = null;
        if (!string.IsNullOrWhiteSpace(this.AccountProxyInput))
        {
            string candidateId = $"proxy_{bundle.AccountUid}";
            if (UniversalSessionImporter.TryParseProxySpec(this.AccountProxyInput, candidateId, out ProxyConfig? parsedProxy) &&
                parsedProxy is not null)
            {
                await this._context.AccountStore.UpsertProxyAsync(parsedProxy).ConfigureAwait(true);
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

        await this._context.AccountStore.UpsertAccountAsync(profile).ConfigureAwait(true);
        await this.ReloadAllStoredDataAsync(profile.Uid).ConfigureAwait(true);

        this.AccountFriendlyNameInput = string.Empty;
        this.SessionImportRawInput = string.Empty;

        _ = await this.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Success,
            profile.DisplayName,
            this._context.Localization.Format("Log.SessionSaved", profile.DisplayName)).ConfigureAwait(true);
    }

    /// <summary>
    /// Assigns, updates, or clears the dedicated HTTP/SOCKS5 proxy for the currently selected account.
    /// </summary>
    public async Task SaveAccountProxyAsync()
    {
        if (this.SelectedAccount is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Account"],
                this._context.Localization["Log.SelectAccountFirst"]).ConfigureAwait(true);
            return;
        }

        AccountProfile current = this.SelectedAccount;
        string rawProxy = this.AccountProxyInput.Trim();

        if (string.IsNullOrWhiteSpace(rawProxy))
        {
            if (!string.IsNullOrWhiteSpace(current.ProxyId))
            {
                _ = await this._context.AccountStore.DeleteProxyAsync(current.ProxyId).ConfigureAwait(true);
            }

            AccountProfile cleared = current with { ProxyId = null };
            await this._context.AccountStore.UpsertAccountAsync(cleared).ConfigureAwait(true);
            await this._context.SessionManager.CloseSessionAsync(current.Uid).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(cleared.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Info,
                cleared.DisplayName,
                this._context.Localization.Format("Log.ProxyCleared", cleared.DisplayName)).ConfigureAwait(true);
            return;
        }

        string proxyId = $"proxy_{current.Uid}";
        if (!UniversalSessionImporter.TryParseProxySpec(rawProxy, proxyId, out ProxyConfig? parsed) || parsed is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                current.DisplayName,
                this._context.Localization["Log.ProxyInvalidFormat"]).ConfigureAwait(true);
            return;
        }

        await this._context.AccountStore.UpsertProxyAsync(parsed).ConfigureAwait(true);
        AccountProfile updated = current with { ProxyId = parsed.Id };
        await this._context.AccountStore.UpsertAccountAsync(updated).ConfigureAwait(true);
        await this._context.SessionManager.CloseSessionAsync(current.Uid).ConfigureAwait(true);
        await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Success,
            updated.DisplayName,
            this._context.Localization.Format("Log.ProxySaved", parsed.Host, parsed.Port, updated.DisplayName)).ConfigureAwait(true);
    }

    /// <summary>
    /// Performs a 1-click live health check on the selected account's session cookies and updates its <see cref="AccountStatus"/>.
    /// </summary>
    public async Task CheckAccountHealthAsync()
    {
        if (this.SelectedAccount is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Account"],
                this._context.Localization["Log.SelectAccountFirst"]).ConfigureAwait(true);
            return;
        }

        AccountProfile current = this.SelectedAccount;
        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            SessionMaterial? material = null;
            try
            {
                material = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(true);
                material = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);
            }

            bool isHealthy = !string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0";
            AccountStatus resolvedStatus = isHealthy ? AccountStatus.Active : AccountStatus.CookieExpired;

            string realUid = isHealthy ? material.Uid : current.Uid;
            string suffix = realUid.Length >= 4 ? realUid[^4..] : realUid;
            bool isDefaultPlaceholder =
                current.DisplayName.StartsWith("Tài khoản", StringComparison.OrdinalIgnoreCase) ||
                current.DisplayName.StartsWith("Account", StringComparison.OrdinalIgnoreCase) ||
                current.DisplayName.StartsWith("Browser Login", StringComparison.OrdinalIgnoreCase) ||
                current.DisplayName.StartsWith("Facebook", StringComparison.OrdinalIgnoreCase);

            string displayName = !string.IsNullOrWhiteSpace(material.DisplayName) && isDefaultPlaceholder
                ? material.DisplayName
                : (isDefaultPlaceholder && isHealthy
                    ? $"Tài khoản {suffix}"
                    : current.DisplayName);

            AccountProfile checkedProfile = current with
            {
                Uid = realUid,
                DisplayName = displayName,
                CookieHeader = !string.IsNullOrWhiteSpace(material.CookieHeader) ? material.CookieHeader : current.CookieHeader,
                UserAgent = !string.IsNullOrWhiteSpace(material.UserAgent) ? material.UserAgent : current.UserAgent,
                Status = resolvedStatus,
                LastActiveAt = DateTimeOffset.UtcNow,
                AvatarUrl = !string.IsNullOrWhiteSpace(material.AvatarUrl) ? material.AvatarUrl : current.AvatarUrl
            };

            if (!string.Equals(current.Uid, realUid, StringComparison.Ordinal))
            {
                _ = await this._context.AccountStore.DeleteAccountAsync(current.Uid, ct).ConfigureAwait(true);
            }

            await this._context.AccountStore.UpsertAccountAsync(checkedProfile, ct).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(checkedProfile.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                isHealthy ? AuditSeverity.Success : AuditSeverity.Warning,
                checkedProfile.DisplayName,
                isHealthy
                    ? this._context.Localization.Format("Log.SessionHealthy", checkedProfile.DisplayName)
                    : this._context.Localization.Format("Log.SessionExpired", checkedProfile.DisplayName)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                current.DisplayName,
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            bool isActualCheckpoint =
                msg.Contains("190", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("checkpoint required", StringComparison.OrdinalIgnoreCase);

            AccountStatus faultStatus = isActualCheckpoint
                ? AccountStatus.Checkpoint
                : AccountStatus.CookieExpired;

            AccountProfile updated = current with { Status = faultStatus };
            await this._context.AccountStore.UpsertAccountAsync(updated).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                updated.DisplayName,
                this._context.Localization.Format("Log.HealthCheckFault", updated.DisplayName, this._context.Localization.FormatAccountStatus(faultStatus), this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task OpenBrowserAndSyncAsync()
    {
        if (this.SelectedAccount is null)
        {
            // Automatically create a fresh interactive browser profile if the user hasn't added any account yet
            await this.QuickImportSessionAsync().ConfigureAwait(true);
        }

        if (this.SelectedAccount is null)
        {
            return;
        }

        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            AccountProfile account = this.SelectedAccount;
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Info,
                account.DisplayName,
                this._context.Localization.Format("Log.LaunchingBrowser", account.DisplayName)).ConfigureAwait(true);

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct, forceVisible: true).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            SessionMaterial? material = null;
            try
            {
                material = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(true);
            }

            if (material is null)
            {
                Exception? lastExtractEx = null;
                for (int attempt = 0; attempt < 60 && !ct.IsCancellationRequested; attempt++)
                {
                    if (session.State != BrowserState.Running)
                    {
                        break;
                    }

                    try
                    {
                        material = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);
                        if (!string.IsNullOrWhiteSpace(material.Uid) && material.Uid != "0")
                        {
                            break;
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        lastExtractEx = ex;
                    }

                    await Task.Delay(1500, ct).ConfigureAwait(true);
                }

                if (material is null)
                {
                    throw lastExtractEx ?? new InvalidOperationException(this._context.Localization["Log.ConnectionInterrupted"]);
                }
            }

            // If the account was created as a placeholder before login, update its internal session identifier cleanly
            string realUid = !string.IsNullOrWhiteSpace(material.Uid) ? material.Uid : account.Uid;
            string suffix = realUid.Length >= 4 ? realUid[^4..] : realUid;
            bool isDefaultPlaceholder =
                account.DisplayName.StartsWith("Tài khoản", StringComparison.OrdinalIgnoreCase) ||
                account.DisplayName.StartsWith("Account", StringComparison.OrdinalIgnoreCase) ||
                account.DisplayName.StartsWith("Browser Login", StringComparison.OrdinalIgnoreCase) ||
                account.DisplayName.StartsWith("Facebook", StringComparison.OrdinalIgnoreCase);

            string displayName = !string.IsNullOrWhiteSpace(material.DisplayName) && isDefaultPlaceholder
                ? material.DisplayName
                : (isDefaultPlaceholder
                    ? $"{this._context.Localization["Accounts.DefaultImportedName"]} {suffix}"
                    : account.DisplayName);

            AccountProfile updated = account with
            {
                Uid = realUid,
                DisplayName = displayName,
                CookieHeader = material.CookieHeader,
                UserAgent = material.UserAgent,
                Status = AccountStatus.Active,
                LastActiveAt = material.ExtractedAt,
                AvatarUrl = !string.IsNullOrWhiteSpace(material.AvatarUrl) ? material.AvatarUrl : account.AvatarUrl
            };

            if (!string.Equals(account.Uid, realUid, StringComparison.Ordinal))
            {
                _ = await this._context.AccountStore.DeleteAccountAsync(account.Uid, ct).ConfigureAwait(true);
            }

            await this._context.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Success,
                updated.DisplayName,
                this._context.Localization["Log.BrowserSessionSynced"]).ConfigureAwait(true);

            // Automatically sync joined groups so the user can pick groups visually without copying Group IDs
            await this.SyncJoinedGroupsCoreAsync(session, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this.SelectedAccount?.DisplayName ?? this._context.Localization["Audit.Cat.Account"],
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this.SelectedAccount?.DisplayName ?? this._context.Localization["Audit.Cat.Account"],
                this._context.Localization.Format("Log.BrowserSignInPrompt", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task DeleteSelectedAccountAsync()
    {
        if (this.SelectedAccount is null)
        {
            return;
        }

        string removedName = this.SelectedAccount.DisplayName;
        string removedUid = this.SelectedAccount.Uid;
        string? removedProxyId = this.SelectedAccount.ProxyId;

        if (!string.IsNullOrWhiteSpace(removedProxyId))
        {
            _ = await this._context.AccountStore.DeleteProxyAsync(removedProxyId).ConfigureAwait(true);
        }

        await this._context.SessionManager.CloseSessionAsync(removedUid).ConfigureAwait(true);
        _ = await this._context.AccountStore.DeleteAccountAsync(removedUid).ConfigureAwait(true);
        this.SelectedAccount = null;
        await this.ReloadAllStoredDataAsync().ConfigureAwait(true);

        _ = await this.LogAuditAsync(
            AuditCategory.Account,
            AuditSeverity.Info,
            removedName,
            this._context.Localization.Format("Log.AccountRemoved", removedName)).ConfigureAwait(true);
    }

    private async Task WarmUpSelectedAccountAsync()
    {
        if (this.SelectedAccount is null)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                this._context.Localization["Audit.Cat.Account"],
                this._context.Localization["Log.SelectAccountFirst"]).ConfigureAwait(true);
            return;
        }

        AccountProfile account = this.SelectedAccount;
        System.Threading.CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Info,
                account.DisplayName,
                this._context.Localization.Format("Log.WarmUpStarted", account.DisplayName)).ConfigureAwait(true);

            await session.NavigateAsync(ProtocolConstants.Endpoints.BaseOrigin, ct).ConfigureAwait(true);
            _ = await session.ExtractMaterialAsync(ct).ConfigureAwait(true);

            PagedResult<PostSummary> feed = await this._context.CometClient
                .GetNewsFeedAsync(session, count: 5, ct: ct)
                .ConfigureAwait(true);

            int reactedCount = 0;
            for (int i = 0; i < feed.Items.Count && reactedCount < 2; i++)
            {
                ct.ThrowIfCancellationRequested();
                PostSummary item = feed.Items[i];
                if (string.IsNullOrWhiteSpace(item.PostId))
                {
                    continue;
                }

                ReactionKind reaction = Random.Shared.NextDouble() < 0.7 ? ReactionKind.Like : ReactionKind.Love;
                bool ok = await this._context.CometClient
                    .ReactToPostAsync(session, item.PostId, reaction, ct)
                    .ConfigureAwait(true);
                if (ok)
                {
                    reactedCount++;
                }
            }

            AccountProfile updated = account with
            {
                Status = AccountStatus.Active,
                LastActiveAt = DateTimeOffset.UtcNow
            };
            await this._context.AccountStore.UpsertAccountAsync(updated, ct).ConfigureAwait(true);
            await this.ReloadAllStoredDataAsync(updated.Uid).ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Success,
                updated.DisplayName,
                this._context.Localization.Format("Log.WarmUpCompleted", updated.DisplayName, reactedCount)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Warning,
                account.DisplayName,
                this._context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Account,
                AuditSeverity.Error,
                account.DisplayName,
                this._context.Localization.Format("Log.WarmUpFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }
}
