// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.ViewModels.Services;

namespace Facebook.Net.UI.ViewModels.Audit;

/// <summary>
/// ViewModel for the Collapsible Bottom Audit Log Drawer / Taskbar, backed by the dedicated
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
        this._context = context;

        this.IsExpanded = context.Settings.Current.IsAuditDrawerExpanded;
        this.LatestTickerText = context.Localization["Audit.ReadyTicker"];
        this.FilteredLogs = new ObservableCollection<AuditLogRecord>();

        this.ToggleDrawerCommand = new RelayCommand(this.ToggleDrawer);
        this.RefreshFilterCommand = new AsyncRelayCommand(this.RefreshLogsAsync);
        this.ClearLogsCommand = new AsyncRelayCommand(this.ClearLogsAsync);

        this._context.AuditStore.LogAppended += this.OnLogAppended;
        this._context.Localization.LanguageChanged += this.OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, Facebook.Net.UI.Core.Localization.AppLanguage e)
    {
        if (this.FilteredLogs.Count == 0)
        {
            this.LatestTickerText = this._context.Localization["Audit.ReadyTicker"];
        }
    }

    /// <summary>Gets or sets a value indicating whether the bottom audit log drawer is expanded.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Gets or sets the latest single-line summary shown in the collapsed taskbar.</summary>
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

    /// <summary>Gets the filtered audit log records displayed in the drawer.</summary>
    public ObservableCollection<AuditLogRecord> FilteredLogs { get; }

    /// <summary>Gets the command to expand or collapse the bottom taskbar drawer.</summary>
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

        AuditLogFilter filter = new(
            Category: this.SelectedCategory,
            Severity: this.SelectedSeverity,
            AccountUid: this.SelectedAccountUid,
            FromTime: fromTime,
            SearchKeyword: this.SearchKeyword,
            Limit: 250);

        List<AuditLogRecord> records = await this._context.AuditStore.QueryAsync(filter).ConfigureAwait(true);
        this.FilteredLogs.Clear();

        int ok = 0;
        int warn = 0;
        int err = 0;

        for (int i = 0; i < records.Count; i++)
        {
            AuditLogRecord item = records[i];
            this.FilteredLogs.Add(item);
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

        this.SuccessCount = ok;
        this.WarningCount = warn;
        this.ErrorCount = err;

        if (records.Count > 0)
        {
            AuditLogRecord latest = records[0];
            this.LatestTickerText = $"[{latest.Timestamp.ToLocalTime():HH:mm:ss}] [{latest.AccountDisplayName}] {latest.Message}";
        }
        else
        {
            this.LatestTickerText = this._context.Localization["Audit.ReadyTicker"];
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
        _ = this._context.Settings.SaveAsync(this._context.Settings.Current with
        {
            IsAuditDrawerExpanded = this.IsExpanded
        });
    }

    private async Task ClearLogsAsync()
    {
        await this._context.AuditStore.ClearAllAsync().ConfigureAwait(true);
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

        this._context.Dispatcher.Post(UpdateUi);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this._context.AuditStore.LogAppended -= this.OnLogAppended;
        this._context.Localization.LanguageChanged -= this.OnLanguageChanged;
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

        if (!string.IsNullOrWhiteSpace(this.SearchKeyword))
        {
            string kw = this.SearchKeyword.Trim();
            if (!record.Message.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                !record.TargetName.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                !record.AccountDisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Exports all currently displayed <see cref="FilteredLogs"/> to a UTF-8 CSV or JSON file.
    /// </summary>
    public async Task ExportFilteredLogsAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            System.Text.StringBuilder json = new(4096);
            _ = json.AppendLine("[");
            for (int i = 0; i < this.FilteredLogs.Count; i++)
            {
                AuditLogRecord r = this.FilteredLogs[i];
                string comma = i + 1 < this.FilteredLogs.Count ? "," : string.Empty;
                _ = json.Append("  {\"timestamp\":\"")
                    .Append(r.Timestamp.ToLocalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("\",\"category\":\"")
                    .Append(EscapeJson(this._context.Localization.FormatAuditCategory(r.Category)))
                    .Append("\",\"severity\":\"")
                    .Append(r.Severity)
                    .Append("\",\"account\":\"")
                    .Append(EscapeJson(r.AccountDisplayName))
                    .Append("\",\"target\":\"")
                    .Append(EscapeJson(r.TargetName))
                    .Append("\",\"message\":\"")
                    .Append(EscapeJson(r.Message))
                    .Append("\"}")
                    .AppendLine(comma);
            }

            _ = json.AppendLine("]");
            await System.IO.File.WriteAllTextAsync(filePath, json.ToString(), System.Text.Encoding.UTF8).ConfigureAwait(true);
            return;
        }

        System.Text.StringBuilder csv = new(4096);
        _ = csv.AppendLine("Timestamp,Category,Severity,Account,Target,Message");
        for (int i = 0; i < this.FilteredLogs.Count; i++)
        {
            AuditLogRecord r = this.FilteredLogs[i];
            _ = csv.Append(EscapeCsv(r.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)))
                .Append(',')
                .Append(EscapeCsv(this._context.Localization.FormatAuditCategory(r.Category)))
                .Append(',')
                .Append(EscapeCsv(r.Severity.ToString()))
                .Append(',')
                .Append(EscapeCsv(r.AccountDisplayName))
                .Append(',')
                .Append(EscapeCsv(r.TargetName))
                .Append(',')
                .AppendLine(EscapeCsv(r.Message));
        }

        await System.IO.File.WriteAllTextAsync(filePath, csv.ToString(), System.Text.Encoding.UTF8).ConfigureAwait(true);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeJson(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
