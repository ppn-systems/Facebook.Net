// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;

namespace Facebook.Net.UI.ViewModels.Shell;

/// <summary>
/// Partial implementation of <see cref="ShellViewModel"/> handling
/// incoming/outgoing friend request management.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly HashSet<string> _inFlightFriendOps = new(StringComparer.Ordinal);

    /// <summary>Gets the collection of incoming friend requests received by the active account.</summary>
    public BulkObservableCollection<UserSummary> IncomingFriendRequests { get; }

    /// <summary>Gets the collection of outgoing friend requests sent by the active account.</summary>
    public BulkObservableCollection<UserSummary> OutgoingFriendRequests { get; }

    /// <summary>Gets or sets the visually selected incoming friend request.</summary>
    [ObservableProperty]
    public partial UserSummary? SelectedIncomingRequest { get; set; }

    /// <summary>Gets or sets the visually selected outgoing friend request.</summary>
    [ObservableProperty]
    public partial UserSummary? SelectedOutgoingRequest { get; set; }

    /// <summary>Gets the command to refresh incoming friend requests from Facebook.</summary>
    public IAsyncRelayCommand SyncIncomingFriendRequestsCommand { get; }

    /// <summary>Gets the command to refresh outgoing friend requests from Facebook.</summary>
    public IAsyncRelayCommand SyncOutgoingFriendRequestsCommand { get; }

    /// <summary>Gets the command to accept the selected incoming friend request.</summary>
    public IAsyncRelayCommand ConfirmFriendRequestCommand { get; }

    /// <summary>Gets the command to decline the selected incoming friend request.</summary>
    public IAsyncRelayCommand DeleteFriendRequestCommand { get; }

    /// <summary>Gets the command to cancel the selected outgoing friend request.</summary>
    public IAsyncRelayCommand CancelFriendRequestCommand { get; }

    private async Task SyncIncomingFriendRequestsAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            PagedResult<UserSummary> result = await _context.CometClient
                .GetIncomingFriendRequestsAsync(session, count: 50, cursor: null, ct: ct)
                .ConfigureAwait(true);

            this.IncomingFriendRequests.ReplaceAll(result.Items);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.IncomingRequestsSynced", result.Items.Count)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled; no action needed.
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task SyncOutgoingFriendRequestsAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            PagedResult<UserSummary> result = await _context.CometClient
                .GetOutgoingFriendRequestsAsync(session, count: 50, cursor: null, ct: ct)
                .ConfigureAwait(true);

            this.OutgoingFriendRequests.ReplaceAll(result.Items);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization.Format("Log.OutgoingRequestsSynced", result.Items.Count)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled; no action needed.
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task ConfirmSelectedFriendRequestAsync()
    {
        UserSummary? selected = this.SelectedIncomingRequest;
        if (selected is null || string.IsNullOrWhiteSpace(selected.Uid) || !_inFlightFriendOps.Add(selected.Uid))
        {
            return;
        }

        // Optimistic UI: remove from list immediately for instant responsiveness.
        int originalIndex = this.IndexOfIncoming(selected);
        _ = this.IncomingFriendRequests.Remove(selected);
        this.SelectedIncomingRequest = null;

        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (session is null)
            {
                this.RollbackIncoming(selected, originalIndex);
                return;
            }

            FriendshipState result = await _context.CometClient
                .ConfirmFriendRequestAsync(session, selected.Uid, ct: CancellationToken.None)
                .ConfigureAwait(true);

            if (result != FriendshipState.AreFriends && result != FriendshipState.Unknown)
            {
                this.RollbackIncoming(selected, originalIndex);
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestAccepted", selected.Name)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            this.RollbackIncoming(selected, originalIndex);
        }
        catch (Exception ex)
        {
            this.RollbackIncoming(selected, originalIndex);
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            _ = _inFlightFriendOps.Remove(selected.Uid);
        }
    }

    private async Task DeleteSelectedFriendRequestAsync()
    {
        UserSummary? selected = this.SelectedIncomingRequest;
        if (selected is null || string.IsNullOrWhiteSpace(selected.Uid) || !_inFlightFriendOps.Add(selected.Uid))
        {
            return;
        }

        // Optimistic UI: remove from list immediately for instant responsiveness.
        int originalIndex = this.IndexOfIncoming(selected);
        _ = this.IncomingFriendRequests.Remove(selected);
        this.SelectedIncomingRequest = null;

        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (session is null)
            {
                this.RollbackIncoming(selected, originalIndex);
                return;
            }

            _ = await _context.CometClient
                .DeleteFriendRequestAsync(session, selected.Uid, isIncoming: true, ct: CancellationToken.None)
                .ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestRejected", selected.Name)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            this.RollbackIncoming(selected, originalIndex);
        }
        catch (Exception ex)
        {
            this.RollbackIncoming(selected, originalIndex);
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            _ = _inFlightFriendOps.Remove(selected.Uid);
        }
    }

    private async Task CancelSelectedOutgoingRequestAsync()
    {
        UserSummary? selected = this.SelectedOutgoingRequest;
        if (selected is null || string.IsNullOrWhiteSpace(selected.Uid) || !_inFlightFriendOps.Add(selected.Uid))
        {
            return;
        }

        // Optimistic UI: remove from list immediately for instant responsiveness.
        int originalIndex = this.IndexOfOutgoing(selected);
        _ = this.OutgoingFriendRequests.Remove(selected);
        this.SelectedOutgoingRequest = null;

        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(CancellationToken.None).ConfigureAwait(true);
            if (session is null)
            {
                this.RollbackOutgoing(selected, originalIndex);
                return;
            }

            _ = await _context.CometClient
                .DeleteFriendRequestAsync(session, selected.Uid, isIncoming: false, ct: CancellationToken.None)
                .ConfigureAwait(true);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestCancelled", selected.Name)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            this.RollbackOutgoing(selected, originalIndex);
        }
        catch (Exception ex)
        {
            this.RollbackOutgoing(selected, originalIndex);
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            _ = _inFlightFriendOps.Remove(selected.Uid);
        }
    }

    /// <summary>
    /// Batch-accepts all incoming friend requests with safe anti-checkpoint pacing delays.
    /// </summary>
    [RelayCommand]
    public async Task AcceptAllIncomingRequestsAsync()
    {
        if (this.IncomingFriendRequests.Count == 0)
        {
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            List<UserSummary> snapshot = [.. this.IncomingFriendRequests];
            int acceptedCount = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                UserSummary user = snapshot[i];
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(user.Name, ct).ConfigureAwait(true);
                }

                FriendshipState result = await _context.CometClient
                    .ConfirmFriendRequestAsync(session, user.Uid, ct: ct)
                    .ConfigureAwait(true);

                if (result == FriendshipState.AreFriends || result == FriendshipState.Unknown)
                {
                    _ = this.IncomingFriendRequests.Remove(user);
                    acceptedCount++;
                }
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.FriendBatchAccepted", acceptedCount, snapshot.Count)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.IncomingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    /// <summary>
    /// Batch-cancels all outgoing friend requests with safe anti-checkpoint pacing delays.
    /// </summary>
    [RelayCommand]
    public async Task CancelAllOutgoingRequestsAsync()
    {
        if (this.OutgoingFriendRequests.Count == 0)
        {
            return;
        }

        CancellationToken ct = this.BeginTaskScope();
        try
        {
            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            List<UserSummary> snapshot = [.. this.OutgoingFriendRequests];
            int cancelledCount = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                UserSummary user = snapshot[i];
                if (i > 0)
                {
                    await this.WaitAntiCheckpointDelayAsync(user.Name, ct).ConfigureAwait(true);
                }

                FriendshipState state = await _context.CometClient
                    .DeleteFriendRequestAsync(session, user.Uid, isIncoming: false, ct: ct)
                    .ConfigureAwait(true);

                if (state != FriendshipState.OutgoingRequest)
                {
                    _ = this.OutgoingFriendRequests.Remove(user);
                    cancelledCount++;
                }
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization.Format("Log.FriendBatchCancelled", cancelledCount, snapshot.Count)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization["Log.TaskStoppedByUser"]).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Error,
                _context.Localization["Personal.OutgoingHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    /// <summary>
    /// Opens the specified <paramref name="user"/>'s Facebook profile page in the system default browser.
    /// </summary>
    public void OpenFriendProfileInBrowser(UserSummary? user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.Uid))
        {
            return;
        }

        string url = $"https://www.facebook.com/{user.Uid}";
        _ = _context.SystemLauncher.OpenUrl(url);
    }

    // ── Optimistic UI rollback helpers ──────────────────────────────────────────

    private int IndexOfIncoming(UserSummary item)
    {
        for (int i = 0; i < this.IncomingFriendRequests.Count; i++)
        {
            if (string.Equals(this.IncomingFriendRequests[i].Uid, item.Uid, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RollbackIncoming(UserSummary item, int index)
    {
        if (index >= 0 && index <= this.IncomingFriendRequests.Count)
        {
            this.IncomingFriendRequests.Insert(index, item);
        }
        else
        {
            this.IncomingFriendRequests.Add(item);
        }
    }

    private int IndexOfOutgoing(UserSummary item)
    {
        for (int i = 0; i < this.OutgoingFriendRequests.Count; i++)
        {
            if (string.Equals(this.OutgoingFriendRequests[i].Uid, item.Uid, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RollbackOutgoing(UserSummary item, int index)
    {
        if (index >= 0 && index <= this.OutgoingFriendRequests.Count)
        {
            this.OutgoingFriendRequests.Insert(index, item);
        }
        else
        {
            this.OutgoingFriendRequests.Add(item);
        }
    }
}
