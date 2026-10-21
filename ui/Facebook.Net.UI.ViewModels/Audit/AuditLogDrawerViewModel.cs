// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.ViewModels.Services;

namespace Facebook.Net.UI.ViewModels.Audit;

/// <summary>
/// ViewModel for the Activity &amp; Audit Logs screen, backed by the dedicated
/// <c>facebook_audit.db</c> SQLite database with multi-criteria filtering (Category, Time Range, Account, Keyword).
/// </summary>
public sealed partial class AuditLogDrawerViewModel : ObservableObject, IDisposable
{
    private readonly UiServiceContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogDrawerViewModel"/> class.
    /// </summary>
    public AuditLogDrawerViewModel(UiServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        this.IsExpanded = context.Settings.Current.IsAuditDrawerExpanded;
        this.LatestTickerText = context.Localization["Audit.ReadyTicker"];
        this.FilteredLogs = new BulkObservableCollection<AuditLogRecord>();

        this.ToggleDrawerCommand = new RelayCommand(this.ToggleDrawer);
        this.RefreshFilterCommand = new AsyncRelayCommand(this.RefreshLogsAsync);
        this.ClearLogsCommand = new AsyncRelayCommand(this.ClearLogsAsync);

        _context.AuditStore.LogAppended += this.OnLogAppended;
        _context.Localization.LanguageChanged += this.OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, Facebook.Net.UI.Core.Localization.AppLanguage e)
    {
        if (this.FilteredLogs.Count == 0)
        {
            this.LatestTickerText = _context.Localization["Audit.ReadyTicker"];
        }
    }

    /// <summary>Gets or sets a value indicating whether the audit log view is expanded.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Gets or sets the latest single-line summary shown in the footer status strip.</summary>
    [ObservableProperty]
    public partial string LatestTickerText { get; set; }

    /// <summary>Gets or sets the selected category filter (<c>null</c> = All categories).</summary>
    [ObservableProperty]
    public partial AuditCategory? SelectedCategory { get; set; }

    /// <summary>Gets or sets the selected time range filter (<see cref="AuditTimeRange.AllTime"/> by default).</summary>
    [ObservableProperty]
    public partial AuditTimeRange SelectedTimeRange { get; set; } = AuditTimeRange.AllTime;

    /// <summary>Gets or sets the selected severity filter (<c>null</c> = All severities).</summary>
    [ObservableProperty]
    public partial AuditSeverity? SelectedSeverity { get; set; }

    /// <summary>Gets or sets the selected account filter (<c>null</c> = All accounts).</summary>
    [ObservableProperty]
    public partial string? SelectedAccountUid { get; set; }

    /// <summary>Gets or sets the search keyword for filtering audit logs.</summary>
    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    /// <summary>Gets or sets the count of successful actions in the current view.</summary>
    [ObservableProperty]
    public partial int SuccessCount { get; set; }

    /// <summary>Gets or sets the count of warning/skipped actions in the current view.</summary>
    [ObservableProperty]
    public partial int WarningCount { get; set; }

    /// <summary>Gets or sets the count of error actions in the current view.</summary>
    [ObservableProperty]
    public partial int ErrorCount { get; set; }

    /// <summary>Gets the filtered audit log records displayed in the screen.</summary>
    public BulkObservableCollection<AuditLogRecord> FilteredLogs { get; }

    /// <summary>Gets the command to toggle the audit view state.</summary>
    public IRelayCommand ToggleDrawerCommand { get; }

    /// <summary>Gets the command to re-query <c>facebook_audit.db</c> with active filters.</summary>
    public IAsyncRelayCommand RefreshFilterCommand { get; }

    /// <summary>Gets the command to clear stored audit logs.</summary>
    public IAsyncRelayCommand ClearLogsCommand { get; }

    /// <summary>
    /// Reloads audit logs from <c>facebook_audit.db</c> using the active filter criteria (Category, TimeRange, Account, Keyword).
    /// </summary>
    public async Task RefreshLogsAsync()
    {
        DateTimeOffset? fromTime = ResolveFromTime(this.SelectedTimeRange);
        bool hasKeyword = !string.IsNullOrWhiteSpace(this.SearchKeyword);

        AuditLogFilter filter = new(
            Category: this.SelectedCategory,
            Severity: this.SelectedSeverity,
            AccountUid: this.SelectedAccountUid,
            FromTime: fromTime,
            SearchKeyword: null,
            Limit: hasKeyword ? 600 : 250);

        List<AuditLogRecord> records = await _context.AuditStore.QueryAsync(filter).ConfigureAwait(true);
        List<AuditLogRecord> matched = new(Math.Min(records.Count, 250));

        int ok = 0;
        int warn = 0;
        int err = 0;

        for (int i = 0; i < records.Count && matched.Count < 250; i++)
        {
            AuditLogRecord item = records[i];
            if (hasKeyword && !this.MatchesKeyword(item, this.SearchKeyword))
            {
                continue;
            }

            matched.Add(item);
            switch (item.Severity)
            {
                case AuditSeverity.Success:
                    ok++;
                    break;
                case AuditSeverity.Warning:
                    warn++;
                    break;
                case AuditSeverity.Error:
                    err++;
                    break;
                case AuditSeverity.Info:
                default:
                    break;
            }
        }

        this.FilteredLogs.ReplaceAll(matched);
        this.SuccessCount = ok;
        this.WarningCount = warn;
        this.ErrorCount = err;

        if (matched.Count > 0)
        {
            AuditLogRecord latest = matched[0];
            this.LatestTickerText = $"[{latest.Timestamp.ToLocalTime():HH:mm:ss}] [{latest.AccountDisplayName}] {latest.Message}";
        }
        else
        {
            this.LatestTickerText = _context.Localization["Audit.ReadyTicker"];
        }
    }

    private static DateTimeOffset? ResolveFromTime(AuditTimeRange range) =>
        range switch
        {
            AuditTimeRange.LastHour => DateTimeOffset.UtcNow.AddHours(-1),
            AuditTimeRange.Today24Hours => DateTimeOffset.UtcNow.AddHours(-24),
            AuditTimeRange.Last7Days => DateTimeOffset.UtcNow.AddDays(-7),
            AuditTimeRange.AllTime => null,
            _ => null
        };

    private void ToggleDrawer()
    {
        this.IsExpanded = !this.IsExpanded;
        _context.Settings.Save(_context.Settings.Current with
        {
            IsAuditDrawerExpanded = this.IsExpanded
        });
    }

    private async Task ClearLogsAsync()
    {
        await _context.AuditStore.ClearAllAsync().ConfigureAwait(true);
        await this.RefreshLogsAsync().ConfigureAwait(true);
    }

    private void OnLogAppended(object? sender, AuditLogRecord record)
    {
        void UpdateUi()
        {
            this.LatestTickerText = $"[{record.Timestamp.ToLocalTime():HH:mm:ss}] [{record.AccountDisplayName}] {record.Message}";
            if (this.MatchesFilter(record))
            {
                this.FilteredLogs.Insert(0, record);
                if (this.FilteredLogs.Count > 250)
                {
                    this.FilteredLogs.RemoveAt(this.FilteredLogs.Count - 1);
                }

                switch (record.Severity)
                {
                    case AuditSeverity.Success:
                        this.SuccessCount++;
                        break;
                    case AuditSeverity.Warning:
                        this.WarningCount++;
                        break;
                    case AuditSeverity.Error:
                        this.ErrorCount++;
                        break;
                    case AuditSeverity.Info:
                    default:
                        break;
                }
            }
        }

        _context.Dispatcher.Post(UpdateUi);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.AuditStore.LogAppended -= this.OnLogAppended;
        _context.Localization.LanguageChanged -= this.OnLanguageChanged;
    }

    private bool MatchesFilter(AuditLogRecord record)
    {
        if (this.SelectedCategory.HasValue && record.Category != this.SelectedCategory.Value)
        {
            return false;
        }

        if (this.SelectedSeverity.HasValue && record.Severity != this.SelectedSeverity.Value)
        {
            return false;
        }

        DateTimeOffset? minTime = ResolveFromTime(this.SelectedTimeRange);
        if (minTime.HasValue && record.Timestamp < minTime.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(this.SelectedAccountUid) &&
            !string.Equals(record.AccountUid, this.SelectedAccountUid, StringComparison.Ordinal))
        {
            return false;
        }

        return this.MatchesKeyword(record, this.SearchKeyword);
    }

    private bool MatchesKeyword(AuditLogRecord record, string? rawKeyword)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
        {
            return true;
        }

        string kw = rawKeyword.Trim();
        if (record.Message.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            record.TargetName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            record.AccountDisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string normKw = _context.LeadScoring.RemoveVietnameseDiacritics(kw);
        if (string.IsNullOrWhiteSpace(normKw))
        {
            return false;
        }

        return _context.LeadScoring.RemoveVietnameseDiacritics(record.Message).Contains(normKw, StringComparison.OrdinalIgnoreCase) ||
               _context.LeadScoring.RemoveVietnameseDiacritics(record.TargetName).Contains(normKw, StringComparison.OrdinalIgnoreCase) ||
               _context.LeadScoring.RemoveVietnameseDiacritics(record.AccountDisplayName).Contains(normKw, StringComparison.OrdinalIgnoreCase);
    }
}
