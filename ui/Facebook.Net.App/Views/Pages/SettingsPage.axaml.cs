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
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent System Settings, Activity Logs &amp; Storage Usage Monitor Page (<c>SettingsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) System, Installed Browser Scanner, Browser Profile Manager &amp; Anti-Checkpoint Delay Settings,
/// 2) Real-Time Activity &amp; Audit Logs (<see cref="AuditLogsPage"/>),
/// 3) Application Disk &amp; Storage Usage Monitor with multi-colored <see cref="SvgDonutChart"/> widgets.
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
        Control tab1View = this.BuildGeneralSettingsTab(out Action refreshTab1);
        AuditLogsPage tab2View = new(this._vm);
        Control tab3View = this.BuildStorageMonitorTab(out Action refreshTab3);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Settings.TabGeneral"], VectorIcons.Settings, tab1View),
                new SubTabItem(this._i18n["Settings.TabAuditLogs"], VectorIcons.AuditLog, tab2View),
                new SubTabItem(this._i18n["Settings.TabStorage"], VectorIcons.Folder, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Settings.TabGeneral"],
                this._i18n["Settings.TabAuditLogs"],
                this._i18n["Settings.TabStorage"]
            ]);
            refreshTab1();
            refreshTab3();
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

        // Left Card: Appearance, Installed Browser Scanner & Browser Profile Manager
        StackPanel leftStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Settings.Header"],
            this._i18n["Settings.Desc"],
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
            this._theme,
            new AppThemeMode[] { AppThemeMode.Dark, AppThemeMode.Light },
            m =>
            {
                TextBlock tb = SectionHeader.CreateLabel(this._theme, this._i18n.FormatThemeMode(m), 12);
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

        StackPanel themeCol = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.ThemeLabel"],
            themeCombo,
            out TextBlock themeLabel);

        ComboBox langCombo = FormField.CreateComboBox<AppLanguage>(
            this._theme,
            new AppLanguage[] { AppLanguage.ViVn, AppLanguage.EnUs },
            l => SectionHeader.CreateLabel(this._theme, LocalizationService.FormatLanguage(l), 12),
            selectedItem: this._vm.SelectedLanguage);
        langCombo.SelectionChanged += (_, _) =>
        {
            if (langCombo.SelectedItem is AppLanguage lang && this._vm.SelectedLanguage != lang)
            {
                this._vm.SelectedLanguage = lang;
            }
        };

        StackPanel langCol = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.LanguageLabel"],
            langCombo,
            out TextBlock langLabel);

        Grid.SetColumn(themeCol, 0);
        Grid.SetColumn(langCol, 1);
        appearanceRow.Children.Add(themeCol);
        appearanceRow.Children.Add(langCol);
        leftStack.Children.Add(appearanceRow);

        // 2. Installed Browser Auto-Scanner + Selector
        IReadOnlyList<PreferredBrowserKind> installedBrowsers = this._vm.Context.Settings.GetInstalledBrowsers();
        ComboBox browserCombo = FormField.CreateComboBox<PreferredBrowserKind>(
            this._theme,
            installedBrowsers,
            k =>
            {
                TextBlock tb = SectionHeader.CreateLabel(this._theme, this._i18n.FormatBrowserKind(k), 12);
                this._i18n.LanguageChanged += (_, _) => tb.Text = this._i18n.FormatBrowserKind(k);
                return tb;
            },
            selectedItem: this._vm.SelectedBrowserKind);

        Button rescanBrowsersBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Settings.BtnRescanBrowsers"],
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

        TextBlock detectedBrowserInfoTb = SectionHeader.CreateLabel(
            this._theme,
            string.Empty,
            11.5,
            FontWeight.Normal,
            isSecondary: true);
        detectedBrowserInfoTb.TextTrimming = TextTrimming.CharacterEllipsis;
        detectedBrowserInfoTb.MaxLines = 1;

        void UpdateDetectedBrowserStatus()
        {
            string? resolvedExe = this._vm.Context.BrowserAdapter.LocateExecutablePath();
            detectedBrowserInfoTb.Text = !string.IsNullOrWhiteSpace(resolvedExe)
                ? $"{this._i18n["Settings.BrowserDetectedPrefix"]}: {resolvedExe}"
                : this._i18n["Settings.BrowserNotDetectedWarning"];
        }

        void RefreshInstalledBrowsersList()
        {
            IReadOnlyList<PreferredBrowserKind> currentInstalled = this._vm.Context.Settings.GetInstalledBrowsers();
            browserCombo.ItemsSource = currentInstalled;
            browserCombo.SelectedItem = this._vm.SelectedBrowserKind;
            UpdateDetectedBrowserStatus();
        }

        browserCombo.SelectionChanged += (_, _) =>
        {
            if (browserCombo.SelectedItem is PreferredBrowserKind kind && this._vm.SelectedBrowserKind != kind)
            {
                this._vm.SelectedBrowserKind = kind;
                UpdateDetectedBrowserStatus();
            }
        };

        rescanBrowsersBtn.Click += (_, _) => RefreshInstalledBrowsersList();
        UpdateDetectedBrowserStatus();

        StackPanel browserStack = new() { Spacing = 5 };
        browserStack.Children.Add(browserSelectorRow);
        browserStack.Children.Add(detectedBrowserInfoTb);

        StackPanel browserField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.BrowserEngineLabel"],
            browserStack,
            out TextBlock browserLabel);

        Grid customExeRow = FilePickerInput.Create(
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
                UpdateDetectedBrowserStatus();
            }
        };
        browseExeBtn.Click += async (_, _) =>
        {
            string? path = await this.PickSingleFileAsync(this._i18n["Common.BrowseFile"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                customExeBox.Text = path;
                this._vm.CustomBrowserExecutablePath = path;
                UpdateDetectedBrowserStatus();
            }
        };

        CheckBox headlessCheck = FormField.CreateCheckBox(
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

        leftStack.Children.Add(browserField);
        leftStack.Children.Add(customExeRow);
        leftStack.Children.Add(headlessCheck);

        // 3. Browser Profile Manager (Select Existing Profile or Create New Profile)
        StackPanel profileSectionStack = new() { Spacing = 8 };

        IReadOnlyList<BrowserProfileOption> initialProfiles = this._vm.GetAvailableBrowserProfiles();
        BrowserProfileOption? currentSelectedProfile = FindMatchingProfileOption(
            initialProfiles,
            this._vm.SelectedAccount?.ProfileDirectory);

        bool isUpdatingProfileCombo = false;
        ComboBox profileCombo = FormField.CreateComboBox<BrowserProfileOption>(
            this._theme,
            initialProfiles,
            opt => SectionHeader.CreateLabel(this._theme, opt.DisplayName, 12),
            selectedItem: currentSelectedProfile);

        Button browseProfileFolderBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Settings.BtnBrowseProfileFolder"],
            null,
            VectorIcons.Folder);

        Grid profilePickerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(profileCombo, 0);
        Grid.SetColumn(browseProfileFolderBtn, 1);
        profilePickerRow.Children.Add(profileCombo);
        profilePickerRow.Children.Add(browseProfileFolderBtn);

        TextBlock activeProfilePathTb = SectionHeader.CreateLabel(
            this._theme,
            string.Empty,
            11.5,
            FontWeight.Normal,
            isSecondary: true);
        activeProfilePathTb.TextTrimming = TextTrimming.CharacterEllipsis;
        activeProfilePathTb.MaxLines = 1;

        TextBox newProfileNameBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Settings.NewProfileNamePlaceholder"]);

        Button createProfileBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Settings.BtnCreateProfile"],
            null,
            VectorIcons.Plus);

        Grid createProfileRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(newProfileNameBox, 0);
        Grid.SetColumn(createProfileBtn, 1);
        createProfileRow.Children.Add(newProfileNameBox);
        createProfileRow.Children.Add(createProfileBtn);

        void RefreshProfileOptions()
        {
            isUpdatingProfileCombo = true;
            try
            {
                IReadOnlyList<BrowserProfileOption> options = this._vm.GetAvailableBrowserProfiles();
                profileCombo.ItemsSource = options;
                string? activeDir = this._vm.SelectedAccount?.ProfileDirectory;
                profileCombo.SelectedItem = FindMatchingProfileOption(options, activeDir);
                activeProfilePathTb.Text = !string.IsNullOrWhiteSpace(activeDir)
                    ? $"{this._i18n["Settings.ActiveProfilePathPrefix"]}: {activeDir}"
                    : this._i18n["Settings.ActiveProfileNoneHint"];
            }
            finally
            {
                isUpdatingProfileCombo = false;
            }
        }

        profileCombo.SelectionChanged += async (_, _) =>
        {
            if (isUpdatingProfileCombo || profileCombo.SelectedItem is not BrowserProfileOption selectedOpt)
            {
                return;
            }

            if (!string.Equals(this._vm.SelectedAccount?.ProfileDirectory, selectedOpt.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                await this._vm.AssignBrowserProfileToSelectedAccountAsync(selectedOpt.DirectoryPath, selectedOpt.DisplayName).ConfigureAwait(true);
                RefreshProfileOptions();
            }
        };

        browseProfileFolderBtn.Click += async (_, _) =>
        {
            string? folderPath = await this.PickSingleFolderAsync(this._i18n["Settings.BtnBrowseProfileFolder"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                await this._vm.AssignBrowserProfileToSelectedAccountAsync(folderPath).ConfigureAwait(true);
                RefreshProfileOptions();
            }
        };

        createProfileBtn.Click += async (_, _) =>
        {
            string rawName = newProfileNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                rawName = $"Profile_{DateTimeOffset.Now:HHmmss}";
            }

            string? createdDir = await this._vm.CreateAndAssignBrowserProfileAsync(rawName).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(createdDir))
            {
                newProfileNameBox.Text = string.Empty;
                RefreshProfileOptions();
            }
        };

        RefreshProfileOptions();

        profileSectionStack.Children.Add(profilePickerRow);
        profileSectionStack.Children.Add(activeProfilePathTb);
        profileSectionStack.Children.Add(createProfileRow);

        StackPanel profileField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.ProfileManagerLabel"],
            profileSectionStack,
            out TextBlock profileManagerLabel);
        leftStack.Children.Add(profileField);

        Border leftCard = SurfaceCard.Create(this._theme, new ScrollViewer { Content = leftStack }, new Thickness(18));

        // Right Card: Action Rate & Account Safety Configuration
        StackPanel rightStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid safetyHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Settings.SafetyTitle"],
            this._i18n["Settings.SafetyDesc"],
            out TextBlock safetyTitleTb,
            out TextBlock safetyDescTb);
        rightStack.Children.Add(safetyHeader);

        // 1. Publishing & Commenting Delay Range Controls
        Grid delayRow = DelayRangeSelector.Create(
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

        StackPanel delayField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.DelayRangeLabel"],
            delayRow,
            out TextBlock delayRangeLabel);
        rightStack.Children.Add(delayField);

        // 2. Group Scraping Delay Range Controls
        Grid scrapeDelayRow = DelayRangeSelector.Create(
            this._theme,
            this._i18n["Settings.MinDelayPrefix"],
            this._vm.ScrapeDelayMinSeconds,
            this._i18n["Settings.MaxDelayPrefix"],
            this._vm.ScrapeDelayMaxSeconds,
            out TextBlock scrapeMinDelayPrefix,
            out NumericUpDown scrapeMinDelayBox,
            out TextBlock scrapeMaxDelayPrefix,
            out NumericUpDown scrapeMaxDelayBox);
        scrapeMinDelayBox.Minimum = 2;
        scrapeMaxDelayBox.Minimum = 2;

        scrapeMinDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(scrapeMinDelayBox.Value ?? 8);
            if (this._vm.ScrapeDelayMinSeconds != next)
            {
                this._vm.ScrapeDelayMinSeconds = next;
            }
        };

        scrapeMaxDelayBox.ValueChanged += (_, _) =>
        {
            int next = (int)(scrapeMaxDelayBox.Value ?? 18);
            if (this._vm.ScrapeDelayMaxSeconds != next)
            {
                this._vm.ScrapeDelayMaxSeconds = next;
            }
        };

        StackPanel scrapeDelayField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Leads.ConfigRateLimitLabel"],
            scrapeDelayRow,
            out TextBlock scrapeDelayLabel);
        rightStack.Children.Add(scrapeDelayField);

        Button saveBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Settings.BtnSaveSettings"],
            this._vm.SaveSettingsCommand,
            VectorIcons.Settings);
        rightStack.Children.Add(saveBtn);

        Border rightCard = SurfaceCard.Create(this._theme, new ScrollViewer { Content = rightStack }, new Thickness(18));

        Grid.SetColumn(leftCard, 0);
        Grid.SetColumn(rightCard, 1);
        splitGrid.Children.Add(leftCard);
        splitGrid.Children.Add(rightCard);

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
                RefreshInstalledBrowsersList();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.CustomBrowserExecutablePath), StringComparison.Ordinal) &&
                     !string.Equals(customExeBox.Text, this._vm.CustomBrowserExecutablePath, StringComparison.Ordinal))
            {
                customExeBox.Text = this._vm.CustomBrowserExecutablePath;
                UpdateDetectedBrowserStatus();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.RunBrowserHeadless), StringComparison.Ordinal))
            {
                headlessCheck.IsChecked = this._vm.RunBrowserHeadless;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshProfileOptions();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MinDelaySeconds), StringComparison.Ordinal))
            {
                minDelayBox.Value = this._vm.MinDelaySeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.MaxDelaySeconds), StringComparison.Ordinal))
            {
                maxDelayBox.Value = this._vm.MaxDelaySeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMinSeconds), StringComparison.Ordinal))
            {
                scrapeMinDelayBox.Value = this._vm.ScrapeDelayMinSeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMaxSeconds), StringComparison.Ordinal))
            {
                scrapeMaxDelayBox.Value = this._vm.ScrapeDelayMaxSeconds;
            }
        };

        this._vm.Accounts.CollectionChanged += (_, _) => RefreshProfileOptions();

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Settings.Header"];
            descTb.Text = this._i18n["Settings.Desc"];
            safetyTitleTb.Text = this._i18n["Settings.SafetyTitle"];
            safetyDescTb.Text = this._i18n["Settings.SafetyDesc"];
            themeLabel.Text = this._i18n["Settings.ThemeLabel"];
            langLabel.Text = this._i18n["Settings.LanguageLabel"];
            browserLabel.Text = this._i18n["Settings.BrowserEngineLabel"];
            AppButton.SetText(rescanBrowsersBtn, this._i18n["Settings.BtnRescanBrowsers"]);
            customExeBox.PlaceholderText = this._i18n["Settings.BrowserCustomPathPlaceholder"];
            AppButton.SetText(browseExeBtn, this._i18n["Common.BrowseFile"]);
            headlessLabelTb.Text = this._i18n["Settings.HeadlessLabel"];
            profileManagerLabel.Text = this._i18n["Settings.ProfileManagerLabel"];
            AppButton.SetText(browseProfileFolderBtn, this._i18n["Settings.BtnBrowseProfileFolder"]);
            newProfileNameBox.PlaceholderText = this._i18n["Settings.NewProfileNamePlaceholder"];
            AppButton.SetText(createProfileBtn, this._i18n["Settings.BtnCreateProfile"]);
            delayRangeLabel.Text = this._i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            scrapeDelayLabel.Text = this._i18n["Leads.ConfigRateLimitLabel"];
            scrapeMinDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            scrapeMaxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            AppButton.SetText(saveBtn, this._i18n["Settings.BtnSaveSettings"]);
            UpdateDetectedBrowserStatus();
            RefreshProfileOptions();
        };

        return splitGrid;
    }

    private static BrowserProfileOption? FindMatchingProfileOption(
        IReadOnlyList<BrowserProfileOption> options,
        string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return options.Count > 0 ? options[0] : null;
        }

        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase))
            {
                return options[i];
            }
        }

        return options.Count > 0 ? options[0] : null;
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
