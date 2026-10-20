// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Tab 3 of <see cref="SettingsPage"/>: Application Disk &amp; Storage Usage Monitor with <see cref="SvgDonutChart"/> breakdowns.
/// </summary>
public sealed partial class SettingsPage
{
    private static readonly string[] s_profileSlicePalette =
    [
        "#F6821F",
        "#3B82F6",
        "#10B981",
        "#8B5CF6",
        "#06B6D4",
        "#EC4899",
        "#F59E0B"
    ];

    private Control BuildStorageMonitorTab(out Action refreshI18n)
    {
        Grid storageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        Grid topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid headerRow = SectionHeader.Create(
            this._theme,
            this._i18n["Storage.Header"],
            this._i18n["Storage.Desc"],
            out TextBlock headerTitleTb,
            out TextBlock headerDescTb);

        Button rescanBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Storage.BtnRefresh"],
            null,
            VectorIcons.Sync);

        Button cleanCacheBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Storage.BtnCleanCache"],
            null,
            VectorIcons.Trash);

        Grid.SetColumn(headerRow, 0);
        Grid.SetColumn(rescanBtn, 1);
        Grid.SetColumn(cleanCacheBtn, 2);
        topBar.Children.Add(headerRow);
        topBar.Children.Add(rescanBtn);
        topBar.Children.Add(cleanCacheBtn);
        Grid.SetRow(topBar, 0);
        storageGrid.Children.Add(topBar);

        // Row 1: 4 Storage & Memory KPI Stat Cards
        Grid kpiRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };

        Border kpiTotal = KpiStatCard.Create(
            this._theme,
            this._i18n["Storage.KpiTotalAppTitle"],
            "0 KB",
            VectorIcons.Folder,
            out TextBlock kpiTotalTitleTb,
            out TextBlock kpiTotalValTb);

        Border kpiDb = KpiStatCard.Create(
            this._theme,
            this._i18n["Storage.KpiDatabaseTitle"],
            "0 KB",
            VectorIcons.Shield,
            out TextBlock kpiDbTitleTb,
            out TextBlock kpiDbValTb);

        Border kpiProfiles = KpiStatCard.Create(
            this._theme,
            this._i18n["Storage.KpiProfilesTitle"],
            "0 KB",
            VectorIcons.Browser,
            out TextBlock kpiProfilesTitleTb,
            out TextBlock kpiProfilesValTb);

        Border kpiMemory = KpiStatCard.Create(
            this._theme,
            this._i18n["Storage.KpiMemoryDiskTitle"],
            "0 MB",
            VectorIcons.CheckCircle,
            out TextBlock kpiMemoryTitleTb,
            out TextBlock kpiMemoryValTb);

        Grid.SetColumn(kpiTotal, 0);
        Grid.SetColumn(kpiDb, 1);
        Grid.SetColumn(kpiProfiles, 2);
        Grid.SetColumn(kpiMemory, 3);
        kpiRow.Children.Add(kpiTotal);
        kpiRow.Children.Add(kpiDb);
        kpiRow.Children.Add(kpiProfiles);
        kpiRow.Children.Add(kpiMemory);
        Grid.SetRow(kpiRow, 1);
        storageGrid.Children.Add(kpiRow);

        // Row 2: 2 Vector SVG Donut Charts (Component Breakdown + Per-Profile Breakdown)
        Grid chartsGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };

        SvgDonutChart componentsChart = new(
            this._theme,
            this._i18n["Storage.ChartComponentsTitle"],
            VectorIcons.Folder,
            this._i18n["Storage.TotalUsedLabel"],
            size: 156,
            strokeThickness: 22);

        SvgDonutChart profilesChart = new(
            this._theme,
            this._i18n["Storage.ChartProfilesTitle"],
            VectorIcons.Browser,
            this._i18n["Storage.TotalProfilesLabel"],
            size: 156,
            strokeThickness: 22);

        Grid.SetColumn(componentsChart, 0);
        Grid.SetColumn(profilesChart, 1);
        chartsGrid.Children.Add(componentsChart);
        chartsGrid.Children.Add(profilesChart);

        ScrollViewer chartsScroll = new()
        {
            Content = chartsGrid
        };
        Grid.SetRow(chartsScroll, 2);
        storageGrid.Children.Add(chartsScroll);

        static int ToKilobytesSliceValue(long bytes) =>
            bytes <= 0 ? 0 : (int)Math.Clamp((bytes + 1023L) / 1024L, 1L, int.MaxValue / 8);

        static string FormatKbSlice(int kb) =>
            ShellViewModel.FormatByteSize(kb * 1024L);

        void RefreshStorageStats()
        {
            StorageUsageSnapshot snap = this._vm.CalculateStorageUsageSnapshot();

            kpiTotalValTb.Text = ShellViewModel.FormatByteSize(snap.TotalUsedBytes);
            kpiDbValTb.Text = ShellViewModel.FormatByteSize(snap.MainDatabaseBytes + snap.AuditDatabaseBytes);
            kpiProfilesValTb.Text = ShellViewModel.FormatByteSize(snap.BrowserProfilesBytes);
            kpiMemoryValTb.Text = $"{ShellViewModel.FormatByteSize(snap.ProcessRamBytes)} / {ShellViewModel.FormatByteSize(snap.DriveFreeBytes)}";

            componentsChart.SetSlices(
                [
                    new SvgDonutSlice(this._i18n["Storage.SliceProfiles"], ToKilobytesSliceValue(snap.BrowserProfilesBytes), "#3B82F6"),
                    new SvgDonutSlice(this._i18n["Storage.SliceMainDb"], ToKilobytesSliceValue(snap.MainDatabaseBytes), "#10B981"),
                    new SvgDonutSlice(this._i18n["Storage.SliceAuditDb"], ToKilobytesSliceValue(snap.AuditDatabaseBytes), "#F6821F"),
                    new SvgDonutSlice(this._i18n["Storage.SliceConfig"], ToKilobytesSliceValue(snap.ConfigAndOtherBytes), "#8B5CF6")
                ],
                customTotalText: ShellViewModel.FormatByteSize(snap.TotalUsedBytes),
                valueFormatter: FormatKbSlice);

            List<SvgDonutSlice> profileSlices = new();
            for (int i = 0; i < snap.ProfileBreakdown.Count; i++)
            {
                (string profileName, long sizeBytes) = snap.ProfileBreakdown[i];
                string colorHex = s_profileSlicePalette[i % s_profileSlicePalette.Length];
                profileSlices.Add(new SvgDonutSlice(profileName, ToKilobytesSliceValue(sizeBytes), colorHex));
            }

            if (profileSlices.Count == 0)
            {
                profileSlices.Add(new SvgDonutSlice(this._i18n["Storage.SliceNoProfiles"], 0, "#64748B"));
            }

            profilesChart.SetSlices(
                profileSlices,
                customTotalText: ShellViewModel.FormatByteSize(snap.BrowserProfilesBytes),
                valueFormatter: FormatKbSlice);
        }

        rescanBtn.Click += (_, _) => RefreshStorageStats();
        cleanCacheBtn.Click += (_, _) =>
        {
            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Confirm.CleanCacheTitle"],
                this._i18n["Confirm.CleanCacheMessage"],
                this._i18n["Storage.BtnCleanCache"],
                this._i18n["Groups.BtnCancelEditTag"],
                async () =>
                {
                    _ = await this._vm.CleanBrowserProfilesCacheAsync().ConfigureAwait(true);
                    RefreshStorageStats();
                });
        };

        RefreshStorageStats();
        this._theme.ThemeChanged += (_, _) => RefreshStorageStats();
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshStorageStats();

        refreshI18n = () =>
        {
            headerTitleTb.Text = this._i18n["Storage.Header"];
            headerDescTb.Text = this._i18n["Storage.Desc"];
            AppButton.SetText(rescanBtn, this._i18n["Storage.BtnRefresh"]);
            AppButton.SetText(cleanCacheBtn, this._i18n["Storage.BtnCleanCache"]);
            kpiTotalTitleTb.Text = this._i18n["Storage.KpiTotalAppTitle"];
            kpiDbTitleTb.Text = this._i18n["Storage.KpiDatabaseTitle"];
            kpiProfilesTitleTb.Text = this._i18n["Storage.KpiProfilesTitle"];
            kpiMemoryTitleTb.Text = this._i18n["Storage.KpiMemoryDiskTitle"];
            componentsChart.UpdateLabels(this._i18n["Storage.ChartComponentsTitle"], this._i18n["Storage.TotalUsedLabel"]);
            profilesChart.UpdateLabels(this._i18n["Storage.ChartProfilesTitle"], this._i18n["Storage.TotalProfilesLabel"]);
            RefreshStorageStats();
        };

        return storageGrid;
    }
}
