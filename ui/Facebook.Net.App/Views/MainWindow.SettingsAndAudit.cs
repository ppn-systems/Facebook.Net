// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Audit;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the clean single-card System Settings Page
/// and the dedicated full-page Activity &amp; Audit Logs Screen.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] s_csvPatterns = ["*.csv"];
    private static readonly string[] s_jsonPatterns = ["*.json"];
    private static readonly FilePickerFileType[] s_exportFileTypeChoices =
    [
        new("CSV Report (*.csv)") { Patterns = s_csvPatterns },
        new("JSON Report (*.json)") { Patterns = s_jsonPatterns }
    ];

    private Control BuildSettingsScreen()
    {
        StackPanel formStack = new()
        {
            Spacing = 12,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Header with SVG Info (`i`) Flyout containing both the description and quick start guide
        string CombinedSettingsInfo() =>
            $"{this._i18n["Settings.Desc"]}\n\n{this._i18n["Settings.GuideTitle"]}:\n{this._i18n["Settings.GuideBody"]}";

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Settings.Header"],
            CombinedSettingsInfo(),
            out TextBlock titleTb,
            out TextBlock descTb);
        formStack.Children.Add(header);

        // 1. Theme & Language in a clean 2-column row
        Grid appearanceRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };

        StackPanel themeCol = new() { Spacing = 6 };
        TextBlock themeLabel = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.ThemeLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox themeCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AppThemeMode[] { AppThemeMode.Dark, AppThemeMode.Light },
            SelectedItem = this._vm.SelectedThemeMode,
            ItemTemplate = new FuncDataTemplate<AppThemeMode>((m, _) =>
            {
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatThemeMode(m), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatThemeMode(m);
                return tb;
            })
        };
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is AppThemeMode mode && this._vm.SelectedThemeMode != mode)
            {
                this._vm.SelectedThemeMode = mode;
            }
        };
        themeCol.Children.Add(themeLabel);
        themeCol.Children.Add(themeCombo);

        StackPanel langCol = new() { Spacing = 6 };
        TextBlock langLabel = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.LanguageLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox langCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AppLanguage[] { AppLanguage.ViVn, AppLanguage.EnUs },
            SelectedItem = this._vm.SelectedLanguage,
            ItemTemplate = new FuncDataTemplate<AppLanguage>((l, _) =>
                UiControls.CreateThemedLabel(this._theme, LocalizationService.FormatLanguage(l), 12))
        };
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is AppLanguage lang && this._vm.SelectedLanguage != lang)
            {
                this._vm.SelectedLanguage = lang;
            }
        };
        langCol.Children.Add(langLabel);
        langCol.Children.Add(langCombo);

        Grid.SetColumn(themeCol, 0);
        Grid.SetColumn(langCol, 1);
        appearanceRow.Children.Add(themeCol);
        appearanceRow.Children.Add(langCol);
        formStack.Children.Add(appearanceRow);

        // 2. Browser Engine Selector
        TextBlock browserLabel = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.BrowserEngineLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        ComboBox browserCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new PreferredBrowserKind[]
            {
                PreferredBrowserKind.AutoDetect,
                PreferredBrowserKind.Edge,
                PreferredBrowserKind.Chrome,
                PreferredBrowserKind.Brave
            },
            SelectedItem = this._vm.SelectedBrowserKind,
            ItemTemplate = new FuncDataTemplate<PreferredBrowserKind>((k, _) =>
            {
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatBrowserKind(k), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatBrowserKind(k);
                return tb;
            })
        };
        browserCombo.SelectionChanged += (_, _) =>
        {
            if (browserCombo.SelectedItem is PreferredBrowserKind kind && this._vm.SelectedBrowserKind != kind)
            {
                this._vm.SelectedBrowserKind = kind;
            }
        };

        Grid customExeRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        TextBox customExeBox = UiControls.CreateTextBox(
            this._theme,
            this._i18n["Settings.BrowserCustomPathPlaceholder"],
            this._vm.CustomBrowserExecutablePath);
        customExeBox.TextChanged += (_, _) =>
        {
            string next = customExeBox.Text ?? string.Empty;
            if (!string.Equals(this._vm.CustomBrowserExecutablePath, next, StringComparison.Ordinal))
            {
                this._vm.CustomBrowserExecutablePath = next;
            }
        };

        Button browseExeBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Common.BrowseFile"]);
        browseExeBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Common.BrowseFile"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                customExeBox.Text = path;
                this._vm.CustomBrowserExecutablePath = path;
            }
        };

        Grid.SetColumn(customExeBox, 0);
        Grid.SetColumn(browseExeBtn, 1);
        customExeRow.Children.Add(customExeBox);
        customExeRow.Children.Add(browseExeBtn);

        CheckBox headlessCheck = new()
        {
            Content = this._i18n["Settings.HeadlessLabel"],
            IsChecked = this._vm.RunBrowserHeadless
        };
        headlessCheck.IsCheckedChanged += (_, _) =>
        {
            bool next = headlessCheck.IsChecked ?? false;
            if (this._vm.RunBrowserHeadless != next)
            {
                this._vm.RunBrowserHeadless = next;
            }
        };

        // 3. Anti-Checkpoint Delay Range Controls
        TextBlock delayRangeLabel = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.DelayRangeLabel"],
            12,
            FontWeight.SemiBold,
            isSecondary: true);

        Grid delayRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"),
            ColumnSpacing = 8
        };

        TextBlock minDelayPrefix = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            12);

        NumericUpDown minDelayBox = new()
        {
            Minimum = 1,
            Maximum = 600,
            Increment = 5,
            Value = this._vm.MinDelaySeconds,
            FormatString = "0"
        };
        minDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(minDelayBox.Value ?? 15);
            if (this._vm.MinDelaySeconds != next)
            {
                this._vm.MinDelaySeconds = next;
            }
        };

        TextBlock maxDelayPrefix = UiControls.CreateThemedLabel(
            this._theme,
            this._i18n["Settings.MaxDelayPrefix"],
            12);

        NumericUpDown maxDelayBox = new()
        {
            Minimum = 1,
            Maximum = 1200,
            Increment = 5,
            Value = this._vm.MaxDelaySeconds,
            FormatString = "0"
        };
        maxDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(maxDelayBox.Value ?? 45);
            if (this._vm.MaxDelaySeconds != next)
            {
                this._vm.MaxDelaySeconds = next;
            }
        };

        Grid.SetColumn(minDelayPrefix, 0);
        Grid.SetColumn(minDelayBox, 1);
        Grid.SetColumn(maxDelayPrefix, 2);
        Grid.SetColumn(maxDelayBox, 3);
        delayRow.Children.Add(minDelayPrefix);
        delayRow.Children.Add(minDelayBox);
        delayRow.Children.Add(maxDelayPrefix);
        delayRow.Children.Add(maxDelayBox);

        Button saveBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Settings.BtnSaveSettings"],
            this._vm.SaveSettingsCommand,
            VectorIcons.Settings);

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedThemeMode), StringComparison.Ordinal))
            {
                themeCombo.SelectedItem = this._vm.SelectedThemeMode;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedLanguage), StringComparison.Ordinal))
            {
                langCombo.SelectedItem = this._vm.SelectedLanguage;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedBrowserKind), StringComparison.Ordinal))
            {
                browserCombo.SelectedItem = this._vm.SelectedBrowserKind;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.CustomBrowserExecutablePath), StringComparison.Ordinal) &&
                     !string.Equals(customExeBox.Text, this._vm.CustomBrowserExecutablePath, StringComparison.Ordinal))
            {
                customExeBox.Text = this._vm.CustomBrowserExecutablePath;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.RunBrowserHeadless), StringComparison.Ordinal))
            {
                headlessCheck.IsChecked = this._vm.RunBrowserHeadless;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MinDelaySeconds), StringComparison.Ordinal))
            {
                minDelayBox.Value = this._vm.MinDelaySeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MaxDelaySeconds), StringComparison.Ordinal))
            {
                maxDelayBox.Value = this._vm.MaxDelaySeconds;
            }
        };

        formStack.Children.Add(browserLabel);
        formStack.Children.Add(browserCombo);
        formStack.Children.Add(customExeRow);
        formStack.Children.Add(headlessCheck);
        formStack.Children.Add(delayRangeLabel);
        formStack.Children.Add(delayRow);
        formStack.Children.Add(saveBtn);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Settings.Header"];
            descTb.Text = CombinedSettingsInfo();
            themeLabel.Text = this._i18n["Settings.ThemeLabel"];
            langLabel.Text = this._i18n["Settings.LanguageLabel"];
            browserLabel.Text = this._i18n["Settings.BrowserEngineLabel"];
            customExeBox.PlaceholderText = this._i18n["Settings.BrowserCustomPathPlaceholder"];
            UiControls.SetButtonText(browseExeBtn, this._i18n["Common.BrowseFile"]);
            headlessCheck.Content = this._i18n["Settings.HeadlessLabel"];
            delayRangeLabel.Text = this._i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            UiControls.SetButtonText(saveBtn, this._i18n["Settings.BtnSaveSettings"]);
        };

        return UiControls.CreateCard(this._theme, new ScrollViewer { Content = formStack });
    }

    private Control BuildAuditLogsScreen()
    {
        AuditLogDrawerViewModel drawerVm = this._vm.AuditDrawer;

        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 8
        };

        Grid topHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = UiControls.CreateSectionHeader(
            this._theme,
            this._i18n["Audit.TaskbarTitle"],
            this._i18n["Audit.SearchPlaceholder"],
            out TextBlock titleTb,
            out TextBlock infoTb);

        TextBlock countersText = new()
        {
            Text = $"✔ {drawerVm.SuccessCount}   ⚠ {drawerVm.WarningCount}   ✖ {drawerVm.ErrorCount}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(header, 0);
        Grid.SetColumn(countersText, 1);
        topHeaderRow.Children.Add(header);
        topHeaderRow.Children.Add(countersText);
        Grid.SetRow(topHeaderRow, 0);
        pageGrid.Children.Add(topHeaderRow);

        // Row 1: 3-Way Dropdown Filters: Category (Sự kiện), Time Range (Thời gian), Account (Tài khoản)
        Grid filterDropsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };

        ComboBox categoryCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new object[]
            {
                "ALL",
                AuditCategory.System,
                AuditCategory.Account,
                AuditCategory.Group,
                AuditCategory.Post,
                AuditCategory.SalesComment,
                AuditCategory.Scrape
            },
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<object>((obj, _) =>
            {
                AuditCategory? cat = obj is AuditCategory c ? c : null;
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditCategory(cat), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditCategory(cat);
                return tb;
            })
        };
        categoryCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedCategory = categoryCombo.SelectedItem is AuditCategory cat ? cat : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(categoryCombo, 0);

        ComboBox timeRangeCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new AuditTimeRange[]
            {
                AuditTimeRange.AllTime,
                AuditTimeRange.LastHour,
                AuditTimeRange.Today24Hours,
                AuditTimeRange.Last7Days
            },
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<AuditTimeRange>((r, _) =>
            {
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatAuditTimeRange(r), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatAuditTimeRange(r);
                return tb;
            })
        };
        timeRangeCombo.SelectionChanged += async (_, _) =>
        {
            if (timeRangeCombo.SelectedItem is AuditTimeRange range)
            {
                drawerVm.SelectedTimeRange = range;
                await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
            }
        };
        Grid.SetColumn(timeRangeCombo, 1);

        ObservableCollection<object> accountFilterItems = new() { "ALL" };
        for (int i = 0; i < this._vm.Accounts.Count; i++)
        {
            accountFilterItems.Add(this._vm.Accounts[i]);
        }

        ComboBox accountFilterCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = this._i18n["Audit.FilterAccountAll"],
            ItemsSource = accountFilterItems,
            SelectedIndex = 0,
            ItemTemplate = new FuncDataTemplate<object>((obj, _) =>
            {
                if (obj is AccountProfile acc)
                {
                    return UiControls.CreateThemedLabel(this._theme, acc.DisplayName, 12);
                }

                TextBlock allTb = UiControls.CreateThemedLabel(this._theme, this._i18n["Audit.FilterAccountAll"], 12);
                this._i18n.LanguageChanged += (_, _) => allTb.Text = this._i18n["Audit.FilterAccountAll"];
                return allTb;
            })
        };

        this._vm.Accounts.CollectionChanged += (_, _) =>
        {
            object? prev = accountFilterCombo.SelectedItem;
            accountFilterItems.Clear();
            accountFilterItems.Add("ALL");
            object? restored = "ALL";
            for (int i = 0; i < this._vm.Accounts.Count; i++)
            {
                AccountProfile item = this._vm.Accounts[i];
                accountFilterItems.Add(item);
                if (prev is AccountProfile prevAcc && string.Equals(prevAcc.Uid, item.Uid, StringComparison.Ordinal))
                {
                    restored = item;
                }
            }

            accountFilterCombo.SelectedItem = restored;
        };

        accountFilterCombo.SelectionChanged += async (_, _) =>
        {
            drawerVm.SelectedAccountUid = accountFilterCombo.SelectedItem is AccountProfile acc ? acc.Uid : null;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(accountFilterCombo, 2);

        filterDropsRow.Children.Add(categoryCombo);
        filterDropsRow.Children.Add(timeRangeCombo);
        filterDropsRow.Children.Add(accountFilterCombo);
        Grid.SetRow(filterDropsRow, 1);
        pageGrid.Children.Add(filterDropsRow);

        // Row 2: Keyword Search Box + Action Buttons (Refresh, Export CSV, Clear Old)
        Grid searchAndActionRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };

        TextBox searchBox = UiControls.CreateTextBox(this._theme, this._i18n["Audit.SearchPlaceholder"]);
        searchBox.TextChanged += async (_, _) =>
        {
            drawerVm.SearchKeyword = searchBox.Text ?? string.Empty;
            await drawerVm.RefreshLogsAsync().ConfigureAwait(true);
        };
        Grid.SetColumn(searchBox, 0);

        Button refreshBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnRefresh"],
            drawerVm.RefreshFilterCommand);
        Grid.SetColumn(refreshBtn, 1);

        Button exportBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnExport"]);
        exportBtn.Click += async (_, _) =>
        {
            if (this.StorageProvider is null)
            {
                return;
            }

            IStorageFile? saveFile = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = this._i18n["Audit.BtnExport"],
                SuggestedFileName = $"facebook_audit_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = s_exportFileTypeChoices
            }).ConfigureAwait(true);

            string? localPath = saveFile?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await drawerVm.ExportFilteredLogsAsync(localPath).ConfigureAwait(true);
            }
        };
        Grid.SetColumn(exportBtn, 2);

        Button clearBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Audit.BtnClear"],
            drawerVm.ClearLogsCommand);
        Grid.SetColumn(clearBtn, 3);

        searchAndActionRow.Children.Add(searchBox);
        searchAndActionRow.Children.Add(refreshBtn);
        searchAndActionRow.Children.Add(exportBtn);
        searchAndActionRow.Children.Add(clearBtn);
        Grid.SetRow(searchAndActionRow, 2);
        pageGrid.Children.Add(searchAndActionRow);

        Border emptyLogsBanner = UiControls.CreateEmptyState(
            this._theme,
            this._i18n["Empty.AuditLogs"],
            VectorIcons.AuditLog,
            out TextBlock emptyLogsMsg);
        emptyLogsBanner.IsVisible = drawerVm.FilteredLogs.Count == 0;

        ListBox logList = new()
        {
            IsVisible = drawerVm.FilteredLogs.Count > 0,
            ItemsSource = drawerVm.FilteredLogs,
            ItemTemplate = new FuncDataTemplate<AuditLogRecord>((rec, _) =>
            {
                if (rec is null)
                {
                    return new Border();
                }

                Grid logRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("70,120,105,140,*"),
                    ColumnSpacing = 8,
                    Margin = new Thickness(2, 2)
                };

                TextBlock tsTb = UiControls.CreateThemedLabel(
                    this._theme,
                    rec.Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    11,
                    FontWeight.Normal,
                    isSecondary: true);
                tsTb.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
                logRow.Children.Add(tsTb);

                TextBlock accTb = UiControls.CreateThemedLabel(this._theme, rec.AccountDisplayName, 11, FontWeight.SemiBold);
                accTb.TextTrimming = TextTrimming.CharacterEllipsis;
                accTb.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(accTb, 1);
                logRow.Children.Add(accTb);

                Border catPill = UiControls.CreateStatusPill(
                    this._theme,
                    this._i18n.FormatAuditCategory(rec.Category),
                    rec.Severity);
                this._i18n.LanguageChanged += (_, _) =>
                    UiControls.SetPillText(catPill, this._i18n.FormatAuditCategory(rec.Category));
                Grid.SetColumn(catPill, 2);
                logRow.Children.Add(catPill);

                TextBlock targetTb = UiControls.CreateThemedLabel(this._theme, rec.TargetName, 11, FontWeight.Normal, isSecondary: true);
                targetTb.TextTrimming = TextTrimming.CharacterEllipsis;
                targetTb.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(targetTb, 3);
                logRow.Children.Add(targetTb);

                TextBlock msgTb = UiControls.CreateThemedLabel(this._theme, rec.Message, 12);
                msgTb.TextWrapping = TextWrapping.NoWrap;
                msgTb.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(msgTb, 4);
                logRow.Children.Add(msgTb);

                return logRow;
            })
        };

        drawerVm.FilteredLogs.CollectionChanged += (_, _) =>
        {
            bool isEmpty = drawerVm.FilteredLogs.Count == 0;
            emptyLogsBanner.IsVisible = isEmpty;
            logList.IsVisible = !isEmpty;
        };

        Grid logContentHost = new();
        logContentHost.Children.Add(logList);
        logContentHost.Children.Add(emptyLogsBanner);
        Grid.SetRow(logContentHost, 3);
        pageGrid.Children.Add(logContentHost);

        void ApplyColors() => countersText.Foreground = this._theme.Palette.TextPrimary;
        ApplyColors();
        this._theme.ThemeChanged += (_, _) => ApplyColors();

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Audit.TaskbarTitle"];
            infoTb.Text = this._i18n["Audit.SearchPlaceholder"];
            accountFilterCombo.PlaceholderText = this._i18n["Audit.FilterAccountAll"];
            searchBox.PlaceholderText = this._i18n["Audit.SearchPlaceholder"];
            emptyLogsMsg.Text = this._i18n["Empty.AuditLogs"];
            UiControls.SetButtonText(refreshBtn, this._i18n["Audit.BtnRefresh"]);
            UiControls.SetButtonText(exportBtn, this._i18n["Audit.BtnExport"]);
            UiControls.SetButtonText(clearBtn, this._i18n["Audit.BtnClear"]);
        };

        drawerVm.PropertyChanged += (_, _) =>
            countersText.Text = $"✔ {drawerVm.SuccessCount}   ⚠ {drawerVm.WarningCount}   ✖ {drawerVm.ErrorCount}";

        return UiControls.CreateCard(this._theme, pageGrid);
    }
}
