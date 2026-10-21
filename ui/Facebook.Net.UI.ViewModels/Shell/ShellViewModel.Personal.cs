// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
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
/// Partial implementation of <see cref="ShellViewModel"/> handling Personal Profile information,
/// incoming/outgoing friend request management, and "People You May Know" friend suggestions.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Gets the collection of incoming friend requests received by the active account.</summary>
    public BulkObservableCollection<UserSummary> IncomingFriendRequests { get; }

    /// <summary>Gets the collection of outgoing friend requests sent by the active account.</summary>
    public BulkObservableCollection<UserSummary> OutgoingFriendRequests { get; }

    /// <summary>Gets the collection of "People You May Know" friend suggestions.</summary>
    public BulkObservableCollection<UserSummary> FriendSuggestions { get; }

    /// <summary>Gets or sets the visually selected incoming friend request.</summary>
    [ObservableProperty]
    public partial UserSummary? SelectedIncomingRequest { get; set; }

    /// <summary>Gets or sets the visually selected outgoing friend request.</summary>
    [ObservableProperty]
    public partial UserSummary? SelectedOutgoingRequest { get; set; }

    /// <summary>Gets or sets the visually selected friend suggestion.</summary>
    [ObservableProperty]
    public partial UserSummary? SelectedSuggestion { get; set; }

    /// <summary>Gets the command to refresh incoming friend requests from Facebook.</summary>
    public IAsyncRelayCommand SyncIncomingFriendRequestsCommand { get; }

    /// <summary>Gets the command to refresh outgoing friend requests from Facebook.</summary>
    public IAsyncRelayCommand SyncOutgoingFriendRequestsCommand { get; }

    /// <summary>Gets the command to refresh friend suggestions from Facebook.</summary>
    public IAsyncRelayCommand SyncFriendSuggestionsCommand { get; }

    /// <summary>Gets the command to accept the selected incoming friend request.</summary>
    public IAsyncRelayCommand ConfirmFriendRequestCommand { get; }

    /// <summary>Gets the command to decline the selected incoming friend request.</summary>
    public IAsyncRelayCommand DeleteFriendRequestCommand { get; }

    /// <summary>Gets the command to cancel the selected outgoing friend request.</summary>
    public IAsyncRelayCommand CancelFriendRequestCommand { get; }

    /// <summary>Gets the command to send a friend request to the selected suggestion.</summary>
    public IAsyncRelayCommand SendFriendRequestCommand { get; }

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

    private async Task SyncFriendSuggestionsAsync()
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
                .GetFriendSuggestionsAsync(session, count: 50, cursor: null, ct: ct)
                .ConfigureAwait(true);

            this.FriendSuggestions.ReplaceAll(result.Items);

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Info,
                _context.Localization["Personal.SuggestionsHeader"],
                _context.Localization.Format("Log.FriendSuggestionsSynced", result.Items.Count)).ConfigureAwait(true);
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
                _context.Localization["Personal.SuggestionsHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }

    private async Task ConfirmSelectedFriendRequestAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            UserSummary? selected = this.SelectedIncomingRequest;
            if (selected is null)
            {
                return;
            }

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            FriendshipState result = await _context.CometClient
                .ConfirmFriendRequestAsync(session, selected.Uid, ct: ct)
                .ConfigureAwait(true);

            if (result == FriendshipState.AreFriends)
            {
                _ = this.IncomingFriendRequests.Remove(selected);
                this.SelectedIncomingRequest = null;
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestAccepted", selected.Name)).ConfigureAwait(true);
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

    private async Task DeleteSelectedFriendRequestAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            UserSummary? selected = this.SelectedIncomingRequest;
            if (selected is null)
            {
                return;
            }

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            _ = await _context.CometClient
                .DeleteFriendRequestAsync(session, selected.Uid, isIncoming: true, ct: ct)
                .ConfigureAwait(true);

            _ = this.IncomingFriendRequests.Remove(selected);
            this.SelectedIncomingRequest = null;

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestRejected", selected.Name)).ConfigureAwait(true);
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

    private async Task CancelSelectedOutgoingRequestAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            UserSummary? selected = this.SelectedOutgoingRequest;
            if (selected is null)
            {
                return;
            }

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            _ = await _context.CometClient
                .DeleteFriendRequestAsync(session, selected.Uid, isIncoming: false, ct: ct)
                .ConfigureAwait(true);

            _ = this.OutgoingFriendRequests.Remove(selected);
            this.SelectedOutgoingRequest = null;

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Warning,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestCancelled", selected.Name)).ConfigureAwait(true);
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

    private async Task SendFriendRequestToSelectedAsync()
    {
        CancellationToken ct = this.BeginTaskScope();
        try
        {
            UserSummary? selected = this.SelectedSuggestion;
            if (selected is null)
            {
                return;
            }

            IBrowserSession? session = await this.TryGetOrLaunchActiveSessionAsync(ct).ConfigureAwait(true);
            if (session is null)
            {
                return;
            }

            FriendshipState result = await _context.CometClient
                .SendFriendRequestAsync(session, selected.Uid, ct: ct)
                .ConfigureAwait(true);

            if (result == FriendshipState.OutgoingRequest || result == FriendshipState.AreFriends)
            {
                _ = this.FriendSuggestions.Remove(selected);
                this.SelectedSuggestion = null;
            }

            _ = await this.LogAuditAsync(
                AuditCategory.Personal,
                AuditSeverity.Success,
                selected.Name,
                _context.Localization.Format("Log.FriendRequestSent", selected.Name)).ConfigureAwait(true);
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
                _context.Localization["Personal.SuggestionsHeader"],
                _context.Localization.Format("Log.FriendRequestFailed", this.SanitizeErrorMessage(ex))).ConfigureAwait(true);
        }
        finally
        {
            this.EndTaskScope();
        }
    }
}
