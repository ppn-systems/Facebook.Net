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
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.Core.Localization;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Independent System Settings, Activity Logs &amp; Data Analytics Page (<c>SettingsPage.axaml</c>).
/// Uses a top <see cref="SubTabBar"/> to separate:
/// 1) System, Browser Engine &amp; Anti-Checkpoint Delay Settings,
/// 2) Real-Time Activity &amp; Audit Logs (<see cref="AuditLogsPage"/>),
/// 3) System-Wide Data Analytics Hub with multi-colored <see cref="SvgDonutChart"/> widgets.
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
        Control tab3View = this.BuildDataAnalyticsTab(out Action refreshTab3);

        SubTabBar subTabBar = new(
            this._theme,
            [
                new SubTabItem(this._i18n["Settings.TabGeneral"], VectorIcons.Settings, tab1View),
                new SubTabItem(this._i18n["Settings.TabAuditLogs"], VectorIcons.AuditLog, tab2View),
                new SubTabItem(this._i18n["Settings.TabAnalytics"], VectorIcons.BrandMark, tab3View)
            ]);

        this._i18n.LanguageChanged += (_, _) =>
        {
            subTabBar.UpdateTabTitles(
            [
                this._i18n["Settings.TabGeneral"],
                this._i18n["Settings.TabAuditLogs"],
                this._i18n["Settings.TabAnalytics"]
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

        // Left Card: Appearance & Browser Engine Configuration
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

        // 2. Browser Engine Selector
        ComboBox browserCombo = FormField.CreateComboBox<PreferredBrowserKind>(
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
                TextBlock tb = SectionHeader.CreateLabel(this._theme, this._i18n.FormatBrowserKind(k), 12);
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

        StackPanel browserField = FormField.CreateLabeled(
            this._theme,
            this._i18n["Settings.BrowserEngineLabel"],
            browserCombo,
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
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMinSeconds), StringComparison.Ordinal))
            {
                scrapeMinDelayBox.Value = this._vm.ScrapeDelayMinSeconds;
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.ScrapeDelayMaxSeconds), StringComparison.Ordinal))
            {
                scrapeMaxDelayBox.Value = this._vm.ScrapeDelayMaxSeconds;
            }
        };

        refreshI18n = () =>
        {
            titleTb.Text = this._i18n["Settings.Header"];
            descTb.Text = this._i18n["Settings.Desc"];
            safetyTitleTb.Text = this._i18n["Settings.SafetyTitle"];
            safetyDescTb.Text = this._i18n["Settings.SafetyDesc"];
            themeLabel.Text = this._i18n["Settings.ThemeLabel"];
            langLabel.Text = this._i18n["Settings.LanguageLabel"];
            browserLabel.Text = this._i18n["Settings.BrowserEngineLabel"];
            customExeBox.PlaceholderText = this._i18n["Settings.BrowserCustomPathPlaceholder"];
            AppButton.SetText(browseExeBtn, this._i18n["Common.BrowseFile"]);
            headlessLabelTb.Text = this._i18n["Settings.HeadlessLabel"];
            delayRangeLabel.Text = this._i18n["Settings.DelayRangeLabel"];
            minDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            maxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            scrapeDelayLabel.Text = this._i18n["Leads.ConfigRateLimitLabel"];
            scrapeMinDelayPrefix.Text = this._i18n["Settings.MinDelayPrefix"];
            scrapeMaxDelayPrefix.Text = this._i18n["Settings.MaxDelayPrefix"];
            AppButton.SetText(saveBtn, this._i18n["Settings.BtnSaveSettings"]);
        };

        return splitGrid;
    }

    private Control BuildDataAnalyticsTab(out Action refreshI18n)
    {
        Grid analyticsGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        Grid headerRow = SectionHeader.Create(
            this._theme,
            this._i18n["Analytics.Header"],
            this._i18n["Analytics.Desc"],
            out TextBlock headerTitleTb,
            out TextBlock headerDescTb);
        Grid.SetRow(headerRow, 0);
        analyticsGrid.Children.Add(headerRow);

        // Top KPI Row: 4 Aggregated System Metrics
        Grid kpiRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };

        Border kpiAcc = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.AccountsTitle"],
            "0",
            VectorIcons.Accounts,
            out TextBlock kpiAccTitleTb,
            out TextBlock kpiAccValTb,
            this._vm.NavigateAccountsCommand);

        Border kpiGrp = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.GroupsTitle"],
            "0",
            VectorIcons.Groups,
            out TextBlock kpiGrpTitleTb,
            out TextBlock kpiGrpValTb,
            this._vm.NavigateGroupsCommand);

        Border kpiLeads = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.LeadsTitle"],
            "0",
            VectorIcons.Search,
            out TextBlock kpiLeadsTitleTb,
            out TextBlock kpiLeadsValTb,
            this._vm.NavigateSalesLeadsCommand);

        Border kpiActions = KpiStatCard.Create(
            this._theme,
            this._i18n["Kpi.TelemetryTitle"],
            "0",
            VectorIcons.CheckCircle,
            out TextBlock kpiActionsTitleTb,
            out TextBlock kpiActionsValTb);

        Grid.SetColumn(kpiAcc, 0);
        Grid.SetColumn(kpiGrp, 1);
        Grid.SetColumn(kpiLeads, 2);
        Grid.SetColumn(kpiActions, 3);
        kpiRow.Children.Add(kpiAcc);
        kpiRow.Children.Add(kpiGrp);
        kpiRow.Children.Add(kpiLeads);
        kpiRow.Children.Add(kpiActions);
        Grid.SetRow(kpiRow, 1);
        analyticsGrid.Children.Add(kpiRow);

        // 3 Multi-Color Vector SVG Donut Charts Row
        Grid chartsGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 12
        };

        SvgDonutChart accountsChart = new(
            this._theme,
            this._i18n["Analytics.ChartAccountsTitle"],
            VectorIcons.Accounts,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        SvgDonutChart leadsChart = new(
            this._theme,
            this._i18n["Analytics.ChartLeadsTitle"],
            VectorIcons.Search,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        SvgDonutChart activityChart = new(
            this._theme,
            this._i18n["Analytics.ChartActivityTitle"],
            VectorIcons.BrandMark,
            this._i18n["Analytics.TotalLabel"],
            size: 148,
            strokeThickness: 22);

        Grid.SetColumn(accountsChart, 0);
        Grid.SetColumn(leadsChart, 1);
        Grid.SetColumn(activityChart, 2);
        chartsGrid.Children.Add(accountsChart);
        chartsGrid.Children.Add(leadsChart);
        chartsGrid.Children.Add(activityChart);

        ScrollViewer chartsScroll = new()
        {
            Content = chartsGrid
        };
        Grid.SetRow(chartsScroll, 2);
        analyticsGrid.Children.Add(chartsScroll);

        void RefreshCharts()
        {
            // 1. Account Status Breakdown
            int activeAcc = 0;
            int idleAcc = 0;
            int expiredAcc = 0;
            int attentionAcc = 0;

            for (int i = 0; i < this._vm.Accounts.Count; i++)
            {
                AccountStatus st = this._vm.Accounts[i].Status;
                if (st is AccountStatus.Active or AccountStatus.Running)
                {
                    activeAcc++;
                }
                else if (st == AccountStatus.Idle)
                {
                    idleAcc++;
                }
                else if (st == AccountStatus.CookieExpired)
                {
                    expiredAcc++;
                }
                else
                {
                    attentionAcc++;
                }
            }

            accountsChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendActive"], activeAcc, "#10B981"),
                new SvgDonutSlice(this._i18n["Analytics.LegendIdle"], idleAcc, "#3B82F6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendExpired"], expiredAcc, "#F59E0B"),
                new SvgDonutSlice(this._i18n["Analytics.LegendAttention"], attentionAcc, "#EF4444")
            ]);

            // 2. Buyer Leads Care Breakdown
            int highIntentPending = 0;
            int warmPending = 0;
            int commentedLeads = 0;

            for (int i = 0; i < this._vm.ScrapedPosts.Count; i++)
            {
                PostSummary post = this._vm.ScrapedPosts[i];
                if (this._vm.IsPostCommented(post.PostId))
                {
                    commentedLeads++;
                }
                else
                {
                    int score = ShellViewModel.CalculateBuyerIntentScore(post.MessageText, this._vm.BuyerIntentKeywords);
                    if (score >= 70)
                    {
                        highIntentPending++;
                    }
                    else
                    {
                        warmPending++;
                    }
                }
            }

            leadsChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendHighIntentPending"], highIntentPending, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendWarmPending"], warmPending, "#8B5CF6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendCommented"], commentedLeads, "#10B981")
            ]);

            // 3. System Activity Breakdown
            int publishOps = 0;
            int commentOps = 0;
            int scrapeOps = 0;
            int syncOps = 0;
            int alertOps = 0;

            for (int i = 0; i < this._vm.AuditDrawer.FilteredLogs.Count; i++)
            {
                AuditLogRecord rec = this._vm.AuditDrawer.FilteredLogs[i];
                if (rec.Severity is AuditSeverity.Warning or AuditSeverity.Error)
                {
                    alertOps++;
                }

                switch (rec.Category)
                {
                    case AuditCategory.Post:
                        publishOps++;
                        break;
                    case AuditCategory.SalesComment:
                        commentOps++;
                        break;
                    case AuditCategory.Scrape:
                        scrapeOps++;
                        break;
                    case AuditCategory.Account:
                    case AuditCategory.Group:
                    case AuditCategory.System:
                    default:
                        syncOps++;
                        break;
                }
            }

            activityChart.SetSlices(
            [
                new SvgDonutSlice(this._i18n["Analytics.LegendPublish"], publishOps, "#3B82F6"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSalesComment"], commentOps, "#10B981"),
                new SvgDonutSlice(this._i18n["Analytics.LegendScrape"], scrapeOps, "#F6821F"),
                new SvgDonutSlice(this._i18n["Analytics.LegendSync"], syncOps, "#06B6D4"),
                new SvgDonutSlice(this._i18n["Analytics.LegendAlerts"], alertOps, "#EF4444")
            ]);

            int enabledTargets = 0;
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                if (this._vm.TargetGroups[i].IsEnabled)
                {
                    enabledTargets++;
                }
            }

            kpiAccValTb.Text = this._vm.Accounts.Count.ToString(CultureInfo.InvariantCulture);
            kpiGrpValTb.Text = $"{enabledTargets}/{this._vm.TargetGroups.Count}";
            kpiLeadsValTb.Text = this._vm.ScrapedPosts.Count.ToString(CultureInfo.InvariantCulture);
            kpiActionsValTb.Text = this._vm.AuditDrawer.SuccessCount.ToString(CultureInfo.InvariantCulture);
        }

        RefreshCharts();
        this._theme.ThemeChanged += (_, _) => RefreshCharts();
        this._vm.PropertyChanged += (_, _) => RefreshCharts();
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshCharts();
        this._vm.TargetGroups.CollectionChanged += (_, _) => RefreshCharts();
        this._vm.ScrapedPosts.CollectionChanged += (_, _) => RefreshCharts();
        this._vm.AuditDrawer.FilteredLogs.CollectionChanged += (_, _) => RefreshCharts();
        this._vm.AuditDrawer.PropertyChanged += (_, _) => RefreshCharts();

        refreshI18n = () =>
        {
            headerTitleTb.Text = this._i18n["Analytics.Header"];
            headerDescTb.Text = this._i18n["Analytics.Desc"];
            kpiAccTitleTb.Text = this._i18n["Kpi.AccountsTitle"];
            kpiGrpTitleTb.Text = this._i18n["Kpi.GroupsTitle"];
            kpiLeadsTitleTb.Text = this._i18n["Kpi.LeadsTitle"];
            kpiActionsTitleTb.Text = this._i18n["Kpi.TelemetryTitle"];
            accountsChart.UpdateLabels(this._i18n["Analytics.ChartAccountsTitle"], this._i18n["Analytics.TotalLabel"]);
            leadsChart.UpdateLabels(this._i18n["Analytics.ChartLeadsTitle"], this._i18n["Analytics.TotalLabel"]);
            activityChart.UpdateLabels(this._i18n["Analytics.ChartActivityTitle"], this._i18n["Analytics.TotalLabel"]);
            RefreshCharts();
        };

        return analyticsGrid;
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
