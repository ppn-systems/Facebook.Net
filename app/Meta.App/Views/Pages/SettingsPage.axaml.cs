// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Configuration;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Pages;

/// <summary>
/// Independent System Settings, Browser Profiles Manager, Activity Logs &amp; Storage Usage Monitor Page (<c>SettingsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) System, Installed Browser Scanner &amp; Anti-Checkpoint Delay Settings,
/// 2) Dedicated Browser Profile Manager (<c>SettingsPage.ProfilesTab.cs</c>),
/// 3) Real-Time Activity &amp; Audit Logs (<see cref="AuditLogsPage"/>),
/// 4) Application Disk &amp; Storage Usage Monitor with multi-colored <see cref="SvgDonutChart"/> widgets.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly ThemeService _theme;
    private readonly LocalizationService _i18n;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class for XAML designer/runtime loader.
    /// </summary>
    public SettingsPage()
    {
        _vm = null!;
        _theme = null!;
        _i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public SettingsPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _vm = viewModel;
        _theme = viewModel.Context.Theme;
        _i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.InitializeComponent();
        ContentControl root = this.FindControl<ContentControl>("PageRoot")!;
        root.Content = this.BuildLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private Control BuildLayout()
    {
        Action? refreshTab1 = null;
        Action? refreshTabAi = null;
        Action? refreshTab2 = null;
        Action? refreshTab4 = null;
        Action? refreshStorageStats = null;

        SubTabBar subTabBar = new(
            _theme,
            [
                new SubTabItem(_i18n["Settings.TabGeneral"], VectorIcons.Settings, () => this.BuildGeneralSettingsTab(out refreshTab1)),
                new SubTabItem(_i18n["Settings.TabAiContext"], VectorIcons.AiContext, () => this.BuildAiContextTab(out refreshTabAi)),
                new SubTabItem(_i18n["Settings.TabProfiles"], VectorIcons.Browser, () => this.BuildBrowserProfilesTab(out refreshTab2)),
                new SubTabItem(_i18n["Settings.TabAuditLogs"], VectorIcons.AuditLog, () => new AuditLogsPage(_vm)),
                new SubTabItem(_i18n["Settings.TabStorage"], VectorIcons.Folder, () => this.BuildStorageMonitorTab(out refreshTab4, out refreshStorageStats))
            ]);

        subTabBar.SelectedIndexChanged += (_, idx) =>
        {
            if (idx == 4)
            {
                refreshStorageStats?.Invoke();
            }
        };

        _i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                _i18n["Settings.TabGeneral"],
                _i18n["Settings.TabAiContext"],
                _i18n["Settings.TabProfiles"],
                _i18n["Settings.TabAuditLogs"],
                _i18n["Settings.TabStorage"]
            ]);
            refreshTab1?.Invoke();
            refreshTabAi?.Invoke();
            refreshTab2?.Invoke();
            refreshTab4?.Invoke();
        };

        return subTabBar;
    }

    private Control BuildGeneralSettingsTab(out Action refreshI18n)
    {
        Grid splitGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 14
        };

        // Left Card: Appearance & Installed Browser Scanner
        StackPanel leftStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid header = SectionHeader.Create(
            _theme,
            _i18n["Settings.Header"],
            _i18n["Settings.Desc"],
            out TextBlock titleTb,
            out TextBlock descTb);
        leftStack.Children.Add(header);

        // 1. Theme & Language in a clean 2-column row
        Grid appearanceRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };

        ComboBox themeCombo = FormField.CreateComboBox<AppThemeMode>(
            _theme,
            new AppThemeMode[] { AppThemeMode.System, AppThemeMode.Dark, AppThemeMode.Light },
            m =>
            {
                TextBlock tb = SectionHeader.CreateLabel(_theme, _i18n.FormatThemeMode(m), 12);
                ThemeLifecycle.BindLanguage(tb, _i18n, () => tb.Text = _i18n.FormatThemeMode(m));
                return tb;
            },
            selectedItem: _vm.SelectedThemeMode);
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is AppThemeMode mode && _vm.SelectedThemeMode != mode)
            {
                _vm.SelectedThemeMode = mode;
            }
        };

        StackPanel themeCol = FormField.CreateLabeled(
            _theme,
            _i18n["Settings.ThemeLabel"],
            themeCombo,
            out TextBlock themeLabel);

        ComboBox langCombo = FormField.CreateComboBox<AppLanguage>(
            _theme,
            new AppLanguage[] { AppLanguage.ViVn, AppLanguage.EnUs },
            l => SectionHeader.CreateLabel(_theme, LocalizationService.FormatLanguage(l), 12),
            selectedItem: _vm.SelectedLanguage);
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is AppLanguage lang && _vm.SelectedLanguage != lang)
            {
                _vm.SelectedLanguage = lang;
            }
        };

        StackPanel langCol = FormField.CreateLabeled(
            _theme,
            _i18n["Settings.LanguageLabel"],
            langCombo,
            out TextBlock langLabel);

        Grid.SetColumn(themeCol, 0);
        Grid.SetColumn(langCol, 1);
        appearanceRow.Children.Add(themeCol);
        appearanceRow.Children.Add(langCol);
        leftStack.Children.Add(appearanceRow);

        // 2. Installed Browser Auto-Scanner + Selector
        List<PreferredBrowserKind> installedBrowsers = _vm.Context.Settings.GetInstalledBrowsers();
        ComboBox browserCombo = FormField.CreateComboBox<PreferredBrowserKind>(
            _theme,
            installedBrowsers,
            k =>
            {
                TextBlock tb = SectionHeader.CreateLabel(_theme, _i18n.FormatBrowserKind(k), 12);
                ThemeLifecycle.BindLanguage(tb, _i18n, () => tb.Text = _i18n.FormatBrowserKind(k));
                return tb;
            },
            selectedItem: _vm.SelectedBrowserKind);

        Button rescanBrowsersBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Settings.BtnRescanBrowsers"],
            null,
            VectorIcons.Sync);

        Grid browserSelectorRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(browserCombo, 0);
        Grid.SetColumn(rescanBrowsersBtn, 1);
        browserSelectorRow.Children.Add(browserCombo);
        browserSelectorRow.Children.Add(rescanBrowsersBtn);

        TextBlock smartBrowserReasonTb = SectionHeader.CreateLabel(
            _theme,
            string.Empty,
            11.5,
            FontWeight.SemiBold);
        smartBrowserReasonTb.Foreground = _theme.Palette.BrandPrimary;
        smartBrowserReasonTb.TextTrimming = TextTrimming.CharacterEllipsis;
        smartBrowserReasonTb.MaxLines = 1;

        TextBlock detectedBrowserInfoTb = SectionHeader.CreateLabel(
            _theme,
            string.Empty,
            11.5,
            FontWeight.Normal,
            isSecondary: true);
        detectedBrowserInfoTb.TextTrimming = TextTrimming.CharacterEllipsis;
        detectedBrowserInfoTb.MaxLines = 1;

        void UpdateDetectedBrowserStatus()
        {
            string? resolvedExe = _vm.Context.BrowserAdapter.LocateExecutablePath();
            detectedBrowserInfoTb.Text = !string.IsNullOrWhiteSpace(resolvedExe)
                ? $"{_i18n["Settings.BrowserDetectedPrefix"]}: {resolvedExe}"
                : _i18n["Settings.BrowserNotDetectedWarning"];

            if (_vm.SelectedBrowserKind == PreferredBrowserKind.AutoDetect &&
                string.IsNullOrWhiteSpace(_vm.CustomBrowserExecutablePath))
            {
                SmartBrowserDetectionResult smart = AppSettingsService.DetectBestBrowser();
                string browserName = _i18n.FormatBrowserKind(smart.BrowserKind);
                System.Text.StringBuilder details = new();
                if (smart.IsOsDefaultBrowser)
                {
                    _ = details.Append(_i18n["Settings.BrowserReasonOsDefault"]);
                }

                if (smart.IsCurrentlyRunning && smart.ActiveUptime.TotalMinutes >= 1.0)
                {
                    int totalHours = (int)smart.ActiveUptime.TotalHours;
                    int mins = smart.ActiveUptime.Minutes;
                    _ = details.Append(totalHours > 0
                        ? _i18n.Format("Settings.BrowserReasonRunningHours", totalHours, mins)
                        : _i18n.Format("Settings.BrowserReasonRunningMinutes", Math.Max(1, mins)));
                }

                smartBrowserReasonTb.Text = _i18n.Format("Settings.BrowserAutoSelectedReason", browserName, details.ToString());
                smartBrowserReasonTb.IsVisible = true;
            }
            else
            {
                smartBrowserReasonTb.IsVisible = false;
            }
        }

        void RefreshInstalledBrowsersList()
        {
            List<PreferredBrowserKind> currentInstalled = _vm.Context.Settings.GetInstalledBrowsers();
            browserCombo.ItemsSource = currentInstalled;
            browserCombo.SelectedItem = _vm.SelectedBrowserKind;
            UpdateDetectedBrowserStatus();
        }

        browserCombo.SelectionChanged += (_, _) =>
        {
            if (browserCombo.SelectedItem is PreferredBrowserKind kind && _vm.SelectedBrowserKind != kind)
            {
                _vm.SelectedBrowserKind = kind;
                UpdateDetectedBrowserStatus();
            }
        };

        rescanBrowsersBtn.Click += (_, _) => RefreshInstalledBrowsersList();
        UpdateDetectedBrowserStatus();

        StackPanel browserStack = new() { Spacing = 4 };
        browserStack.Children.Add(browserSelectorRow);
        browserStack.Children.Add(smartBrowserReasonTb);
        browserStack.Children.Add(detectedBrowserInfoTb);

        StackPanel browserField = FormField.CreateLabeled(
            _theme,
            _i18n["Settings.BrowserEngineLabel"],
            browserStack,
            out TextBlock browserLabel);

        Grid customExeRow = FilePickerInput.Create(
            _theme,
            _i18n["Settings.BrowserCustomPathPlaceholder"],
            _vm.CustomBrowserExecutablePath,
            _i18n["Common.BrowseFile"],
            out TextBox customExeBox,
            out Button browseExeBtn);
        customExeBox.TextChanged += (_, _) =>
        {
            string next = customExeBox.Text ?? string.Empty;
            if (!string.Equals(_vm.CustomBrowserExecutablePath, next, StringComparison.Ordinal))
            {
                _vm.CustomBrowserExecutablePath = next;
                UpdateDetectedBrowserStatus();
            }
        };
        browseExeBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(_i18n["Common.BrowseFile"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                customExeBox.Text = path;
                _vm.CustomBrowserExecutablePath = path;
                UpdateDetectedBrowserStatus();
            }
        };

        CheckBox headlessCheck = FormField.CreateCheckBox(
            _theme,
            _i18n["Settings.HeadlessLabel"],
            _vm.RunBrowserHeadless,
            out TextBlock headlessLabelTb);
        headlessCheck.IsCheckedChanged += (_, _) =>
        {
            bool next = headlessCheck.IsChecked ?? false;
            if (_vm.RunBrowserHeadless != next)
            {
                _vm.RunBrowserHeadless = next;
            }
        };

        leftStack.Children.Add(browserField);
        leftStack.Children.Add(customExeRow);
        leftStack.Children.Add(headlessCheck);

        Border leftCard = SurfaceCard.Create(_theme, new ScrollViewer { Content = leftStack }, new Thickness(18));

        // Right Card: Action Rate & Account Safety Configuration
        StackPanel rightStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid safetyHeader = SectionHeader.Create(
            _theme,
            _i18n["Settings.SafetyTitle"],
            _i18n["Settings.SafetyDesc"],
            out TextBlock safetyTitleTb,
            out TextBlock safetyDescTb);
        rightStack.Children.Add(safetyHeader);

        // 1. Publishing & Commenting Delay Range Controls
        Grid delayRow = DelayRangeSelector.Create(
            _theme,
            _i18n["Settings.MinDelayPrefix"],
            _vm.MinDelaySeconds,
            _i18n["Settings.MaxDelayPrefix"],
            _vm.MaxDelaySeconds,
            out TextBlock minDelayPrefix,
            out NumericUpDown minDelayBox,
            out TextBlock maxDelayPrefix,
            out NumericUpDown maxDelayBox,
            _i18n);

        minDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(minDelayBox.Value ?? 2m), 0.0, 600.0), 2);
            if (Math.Abs(_vm.MinDelaySeconds - next) > 0.001)
            {
                _vm.MinDelaySeconds = next;
            }
        };

        maxDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(maxDelayBox.Value ?? 5m), 0.0, 1200.0), 2);
            if (Math.Abs(_vm.MaxDelaySeconds - next) > 0.001)
            {
                _vm.MaxDelaySeconds = next;
            }
        };

        StackPanel delayField = FormField.CreateLabeled(
            _theme,
            _i18n["Settings.DelayRangeLabel"],
            delayRow,
            out TextBlock delayRangeLabel);
        rightStack.Children.Add(delayField);

        // 2. Group Scraping Delay Range Controls
        Grid scrapeDelayRow = DelayRangeSelector.Create(
            _theme,
            _i18n["Settings.MinDelayPrefix"],
            _vm.ScrapeDelayMinSeconds,
            _i18n["Settings.MaxDelayPrefix"],
            _vm.ScrapeDelayMaxSeconds,
            out TextBlock scrapeMinDelayPrefix,
            out NumericUpDown scrapeMinDelayBox,
            out TextBlock scrapeMaxDelayPrefix,
            out NumericUpDown scrapeMaxDelayBox,
            _i18n);

        scrapeMinDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(scrapeMinDelayBox.Value ?? 1m), 0.0, 600.0), 2);
            if (Math.Abs(_vm.ScrapeDelayMinSeconds - next) > 0.001)
            {
                _vm.ScrapeDelayMinSeconds = next;
            }
        };

        scrapeMaxDelayBox.ValueChanged += (_, _) =>
        {
            double next = Math.Round(Math.Clamp((double)(scrapeMaxDelayBox.Value ?? 2m), 0.0, 1200.0), 2);
            if (Math.Abs(_vm.ScrapeDelayMaxSeconds - next) > 0.001)
            {
                _vm.ScrapeDelayMaxSeconds = next;
            }
        };

        StackPanel scrapeDelayField = FormField.CreateLabeled(
            _theme,
            _i18n["Leads.ConfigRateLimitLabel"],
            scrapeDelayRow,
            out TextBlock scrapeDelayLabel);
        rightStack.Children.Add(scrapeDelayField);

        Button saveBtn = AppButton.CreatePrimary(
            _theme,
            _i18n["Settings.BtnSaveSettings"],
            _vm.SaveSettingsCommand,
            VectorIcons.Settings);
        rightStack.Children.Add(saveBtn);

        // 3. In-App Version & GitHub Releases Update Checker Card
        StackPanel updateCardStack = new()
        {
            Spacing = 10
        };

        Grid updateHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock updateCardTitleTb = SectionHeader.CreateLabel(
            _theme,
            _i18n["Settings.UpdateCardTitle"],
            13.5,
            FontWeight.Bold);

        Border currentVersionPill = StatusPill.Create(
            _theme,
            _i18n.Format("Settings.CurrentVersionLabel", _vm.CurrentAppVersion),
            AuditSeverity.Info);

        Grid.SetColumn(updateCardTitleTb, 0);
        Grid.SetColumn(currentVersionPill, 1);
        updateHeaderRow.Children.Add(updateCardTitleTb);
        updateHeaderRow.Children.Add(currentVersionPill);
        updateCardStack.Children.Add(updateHeaderRow);

        TextBlock updateStatusTb = SectionHeader.CreateLabel(
            _theme,
            _vm.UpdateStatusMessage,
            12,
            FontWeight.Normal,
            isSecondary: true);
        updateStatusTb.TextWrapping = TextWrapping.Wrap;
        updateCardStack.Children.Add(updateStatusTb);

        WrapPanel updateBtnRow = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };

        Button checkUpdateBtn = AppButton.CreateSecondary(
            _theme,
            _i18n["Settings.BtnCheckUpdate"],
            _vm.CheckForUpdatesCommand,
            VectorIcons.Sync);

        string initialDownloadLabel = _i18n.Format(
            "Settings.BtnDownloadUpdate",
            !string.IsNullOrWhiteSpace(_vm.LatestReleaseTag) ? _vm.LatestReleaseTag : _vm.CurrentAppVersion);

        Button downloadUpdateBtn = AppButton.CreatePrimary(
            _theme,
            initialDownloadLabel,
            _vm.OpenUpdateDownloadCommand,
            VectorIcons.Download);
        downloadUpdateBtn.IsVisible = _vm.IsUpdateAvailable;

        updateBtnRow.Children.Add(checkUpdateBtn);
        updateBtnRow.Children.Add(downloadUpdateBtn);
        updateCardStack.Children.Add(updateBtnRow);

        Border updateInsetCard = SurfaceCard.CreateInset(_theme, updateCardStack, new Thickness(14, 12));
        rightStack.Children.Add(updateInsetCard);

        Border rightCard = SurfaceCard.Create(_theme, new ScrollViewer { Content = rightStack }, new Thickness(18));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightCard);

        void RefreshUpdateCheckerVisuals()
        {
            updateCardTitleTb.Text = _i18n["Settings.UpdateCardTitle"];
            StatusPill.SetText(currentVersionPill, _i18n.Format("Settings.CurrentVersionLabel", _vm.CurrentAppVersion));
            updateStatusTb.Text = _vm.UpdateStatusMessage;
            AppButton.SetText(checkUpdateBtn, _i18n["Settings.BtnCheckUpdate"]);
            checkUpdateBtn.IsEnabled = !_vm.IsCheckingForUpdate;
            string tag = !string.IsNullOrWhiteSpace(_vm.LatestReleaseTag) ? _vm.LatestReleaseTag : _vm.CurrentAppVersion;
            AppButton.SetText(downloadUpdateBtn, _i18n.Format("Settings.BtnDownloadUpdate", tag));
            downloadUpdateBtn.IsVisible = _vm.IsUpdateAvailable;
        }

        _vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedThemeMode), StringComparison.Ordinal))
            {
                themeCombo.SelectedItem = _vm.SelectedThemeMode;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedLanguage), StringComparison.Ordinal))
            {
                langCombo.SelectedItem = _vm.SelectedLanguage;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedBrowserKind), StringComparison.Ordinal))
            {
                RefreshInstalledBrowsersList();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.CustomBrowserExecutablePath), StringComparison.Ordinal) &&
                     !string.Equals(customExeBox.Text, _vm.CustomBrowserExecutablePath, StringComparison.Ordinal))
            {
                customExeBox.Text = _vm.CustomBrowserExecutablePath;
                UpdateDetectedBrowserStatus();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.RunBrowserHeadless), StringComparison.Ordinal))
            {
                headlessCheck.IsChecked = _vm.RunBrowserHeadless;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MinDelaySeconds), StringComparison.Ordinal))
            {
                minDelayBox.Value = (decimal)_vm.MinDelaySeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MaxDelaySeconds), StringComparison.Ordinal))
            {
                maxDelayBox.Value = (decimal)_vm.MaxDelaySeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMinSeconds), StringComparison.Ordinal))
            {
                scrapeMinDelayBox.Value = (decimal)_vm.ScrapeDelayMinSeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMaxSeconds), StringComparison.Ordinal))
            {
                scrapeMaxDelayBox.Value = (decimal)_vm.ScrapeDelayMaxSeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.UpdateStatusMessage), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.IsUpdateAvailable), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.IsCheckingForUpdate), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.LatestReleaseTag), StringComparison.Ordinal) ||
                     string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentAppVersion), StringComparison.Ordinal))
            {
                RefreshUpdateCheckerVisuals();
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = _i18n["Settings.Header"];
            descTb.Text = _i18n["Settings.Desc"];
            safetyTitleTb.Text = _i18n["Settings.SafetyTitle"];
            safetyDescTb.Text = _i18n["Settings.SafetyDesc"];
            themeLabel.Text = _i18n["Settings.ThemeLabel"];
            langLabel.Text = _i18n["Settings.LanguageLabel"];
            browserLabel.Text = _i18n["Settings.BrowserEngineLabel"];
            AppButton.SetText(rescanBrowsersBtn, _i18n["Settings.BtnRescanBrowsers"]);
            customExeBox.PlaceholderText = _i18n["Settings.BrowserCustomPathPlaceholder"];
            AppButton.SetText(browseExeBtn, _i18n["Common.BrowseFile"]);
            headlessLabelTb.Text = _i18n["Settings.HeadlessLabel"];
            delayRangeLabel.Text = _i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = _i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = _i18n["Settings.MaxDelayPrefix"];
            scrapeDelayLabel.Text = _i18n["Leads.ConfigRateLimitLabel"];
            scrapeMinDelayPrefix.Text = _i18n["Settings.MinDelayPrefix"];
            scrapeMaxDelayPrefix.Text = _i18n["Settings.MaxDelayPrefix"];
            AppButton.SetText(saveBtn, _i18n["Settings.BtnSaveSettings"]);
            UpdateDetectedBrowserStatus();
            RefreshUpdateCheckerVisuals();
        };

        return splitGrid;
    }

    private async Task<string?> PickSingleFileAsync(string title)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSingleFolderAsync(string title)
    {
        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
