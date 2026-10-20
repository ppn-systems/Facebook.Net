// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent System Settings Page (<c>SettingsPage.axaml</c>).
/// Includes Theme, Language, Browser Engine, Anti-Checkpoint Delay, and the bottom entry point to navigate to <see cref="AuditLogsPage"/>.
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
        this._vm = null!;
        this._theme = null!;
        this._i18n = null!;
        this.InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class bound to <see cref="ShellViewModel"/>.
    /// </summary>
    public SettingsPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this._vm = viewModel;
        this._theme = viewModel.Context.Theme;
        this._i18n = viewModel.Context.Localization;
        this.DataContext = viewModel;

        this.InitializeComponent();
        ContentControl root = this.FindControl<ContentControl>("PageRoot")!;
        root.Content = this.BuildLayout();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private Control BuildLayout()
    {
        StackPanel formStack = new()
        {
            Spacing = 12,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

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

        ComboBox themeCombo = UiControls.CreateComboBox<AppThemeMode>(
            this._theme,
            new AppThemeMode[] { AppThemeMode.Dark, AppThemeMode.Light },
            m =>
            {
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatThemeMode(m), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatThemeMode(m);
                return tb;
            },
            selectedItem: this._vm.SelectedThemeMode);
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is AppThemeMode mode && this._vm.SelectedThemeMode != mode)
            {
                this._vm.SelectedThemeMode = mode;
            }
        };

        StackPanel themeCol = UiControls.CreateLabeledField(
            this._theme,
            this._i18n["Settings.ThemeLabel"],
            themeCombo,
            out TextBlock themeLabel);

        ComboBox langCombo = UiControls.CreateComboBox<AppLanguage>(
            this._theme,
            new AppLanguage[] { AppLanguage.ViVn, AppLanguage.EnUs },
            l => UiControls.CreateThemedLabel(this._theme, LocalizationService.FormatLanguage(l), 12),
            selectedItem: this._vm.SelectedLanguage);
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is AppLanguage lang && this._vm.SelectedLanguage != lang)
            {
                this._vm.SelectedLanguage = lang;
            }
        };

        StackPanel langCol = UiControls.CreateLabeledField(
            this._theme,
            this._i18n["Settings.LanguageLabel"],
            langCombo,
            out TextBlock langLabel);

        Grid.SetColumn(themeCol, 0);
        Grid.SetColumn(langCol, 1);
        appearanceRow.Children.Add(themeCol);
        appearanceRow.Children.Add(langCol);
        formStack.Children.Add(appearanceRow);

        // 2. Browser Engine Selector
        ComboBox browserCombo = UiControls.CreateComboBox<PreferredBrowserKind>(
            this._theme,
            new PreferredBrowserKind[]
            {
                PreferredBrowserKind.AutoDetect,
                PreferredBrowserKind.Edge,
                PreferredBrowserKind.Chrome,
                PreferredBrowserKind.Brave
            },
            k =>
            {
                TextBlock tb = UiControls.CreateThemedLabel(this._theme, this._i18n.FormatBrowserKind(k), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatBrowserKind(k);
                return tb;
            },
            selectedItem: this._vm.SelectedBrowserKind);
        browserCombo.SelectionChanged += (_, _) =>
        {
            if (browserCombo.SelectedItem is PreferredBrowserKind kind && this._vm.SelectedBrowserKind != kind)
            {
                this._vm.SelectedBrowserKind = kind;
            }
        };

        StackPanel browserField = UiControls.CreateLabeledField(
            this._theme,
            this._i18n["Settings.BrowserEngineLabel"],
            browserCombo,
            out TextBlock browserLabel);

        Grid customExeRow = UiControls.CreateFilePickerRow(
            this._theme,
            this._i18n["Settings.BrowserCustomPathPlaceholder"],
            this._vm.CustomBrowserExecutablePath,
            this._i18n["Common.BrowseFile"],
            out TextBox customExeBox,
            out Button browseExeBtn);
        customExeBox.TextChanged += (_, _) =>
        {
            string next = customExeBox.Text ?? string.Empty;
            if (!string.Equals(this._vm.CustomBrowserExecutablePath, next, StringComparison.Ordinal))
            {
                this._vm.CustomBrowserExecutablePath = next;
            }
        };
        browseExeBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Common.BrowseFile"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                customExeBox.Text = path;
                this._vm.CustomBrowserExecutablePath = path;
            }
        };

        CheckBox headlessCheck = UiControls.CreateCheckBox(
            this._theme,
            this._i18n["Settings.HeadlessLabel"],
            this._vm.RunBrowserHeadless,
            out TextBlock headlessLabelTb);
        headlessCheck.IsCheckedChanged += (_, _) =>
        {
            bool next = headlessCheck.IsChecked ?? false;
            if (this._vm.RunBrowserHeadless != next)
            {
                this._vm.RunBrowserHeadless = next;
            }
        };

        // 3. Anti-Checkpoint Delay Range Controls
        Grid delayRow = UiControls.CreateDelayRangeRow(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            this._vm.MinDelaySeconds,
            this._i18n["Settings.MaxDelayPrefix"],
            this._vm.MaxDelaySeconds,
            out TextBlock minDelayPrefix,
            out NumericUpDown minDelayBox,
            out TextBlock maxDelayPrefix,
            out NumericUpDown maxDelayBox);

        minDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(minDelayBox.Value ?? 15);
            if (this._vm.MinDelaySeconds != next)
            {
                this._vm.MinDelaySeconds = next;
            }
        };

        maxDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(maxDelayBox.Value ?? 45);
            if (this._vm.MaxDelaySeconds != next)
            {
                this._vm.MaxDelaySeconds = next;
            }
        };

        StackPanel delayField = UiControls.CreateLabeledField(
            this._theme,
            this._i18n["Settings.DelayRangeLabel"],
            delayRow,
            out TextBlock delayRangeLabel);

        Button saveBtn = UiControls.CreatePrimaryButton(
            this._theme,
            this._i18n["Settings.BtnSaveSettings"],
            this._vm.SaveSettingsCommand,
            VectorIcons.Settings);

        // 4. Bottom Section: Compact Activity & Audit Logs Entry Card (tucked at the bottom of Settings)
        Grid auditEntryRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel auditCounters = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border okPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.CheckCircle,
            this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Success,
            out TextBlock okCountTb);

        Border warnPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.Warning,
            this._vm.AuditDrawer.WarningCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Warning,
            out TextBlock warnCountTb);

        Border errPill = UiControls.CreateIconMetricPill(
            this._theme,
            VectorIcons.ErrorCircle,
            this._vm.AuditDrawer.ErrorCount.ToString(CultureInfo.InvariantCulture),
            AuditSeverity.Error,
            out TextBlock errCountTb);

        auditCounters.Children.Add(okPill);
        auditCounters.Children.Add(warnPill);
        auditCounters.Children.Add(errPill);

        Button openAuditLogsBtn = UiControls.CreateSecondaryButton(
            this._theme,
            this._i18n["Settings.BtnOpenAuditLogs"],
            this._vm.NavigateAuditLogsCommand,
            VectorIcons.AuditLog);

        Grid.SetColumn(auditCounters, 0);
        Grid.SetColumn(openAuditLogsBtn, 2);
        auditEntryRow.Children.Add(auditCounters);
        auditEntryRow.Children.Add(openAuditLogsBtn);

        Border auditEntryWell = UiControls.CreateInsetWell(this._theme, auditEntryRow, new Thickness(12, 8));

        this._vm.AuditDrawer.PropertyChanged += (_, _) =>
        {
            okCountTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);
            warnCountTb.Text = this._vm.AuditDrawer.WarningCount.ToString(CultureInfo.InvariantCulture);
            errCountTb.Text = this._vm.AuditDrawer.ErrorCount.ToString(CultureInfo.InvariantCulture);
        };

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

        formStack.Children.Add(browserField);
        formStack.Children.Add(customExeRow);
        formStack.Children.Add(headlessCheck);
        formStack.Children.Add(delayField);
        formStack.Children.Add(saveBtn);
        formStack.Children.Add(auditEntryWell);

        this._i18n.LanguageChanged += (_, _) =>
        {
            titleTb.Text = this._i18n["Settings.Header"];
            descTb.Text = CombinedSettingsInfo();
            themeLabel.Text = this._i18n["Settings.ThemeLabel"];
            langLabel.Text = this._i18n["Settings.LanguageLabel"];
            browserLabel.Text = this._i18n["Settings.BrowserEngineLabel"];
            customExeBox.PlaceholderText = this._i18n["Settings.BrowserCustomPathPlaceholder"];
            UiControls.SetButtonText(browseExeBtn, this._i18n["Common.BrowseFile"]);
            headlessLabelTb.Text = this._i18n["Settings.HeadlessLabel"];
            delayRangeLabel.Text = this._i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            UiControls.SetButtonText(saveBtn, this._i18n["Settings.BtnSaveSettings"]);
            UiControls.SetButtonText(openAuditLogsBtn, this._i18n["Settings.BtnOpenAuditLogs"]);
        };

        return UiControls.CreateCard(this._theme, new ScrollViewer { Content = formStack });
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
}
