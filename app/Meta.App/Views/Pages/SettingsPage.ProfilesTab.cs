// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Components.Controls;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Collections;
using Meta.UI.Core.Configuration;
using Meta.UI.ViewModels.Shell;

namespace Meta.App.Views.Pages;

/// <summary>
/// Dedicated Tab 2 of <see cref="SettingsPage"/>: Browser Profile Manager (<c>SettingsPage.ProfilesTab.cs</c>).
/// Displays profiles strictly matching the selected browser family alongside isolated <c>One Client</c> profiles,
/// with modal profile creation and confirmation-guarded deletion of app-created profiles only.
/// </summary>
public sealed partial class SettingsPage
{
    private Control BuildBrowserProfilesTab(out Action refreshI18n)
    {
        Grid tabOverlayHost = new();

        Grid pageGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        // Row 0: Header + Rescan + Browse Folder + Open Create Profile Modal
        Grid topBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid header = SectionHeader.Create(
            this._theme,
            this._i18n["Settings.ProfilesHeader"],
            this._i18n["Settings.ProfilesDesc"],
            out TextBlock headerTitleTb,
            out TextBlock headerDescTb);

        Button rescanProfilesBtn = AppButton.CreateIcon(
            this._theme,
            VectorIcons.Sync,
            this._i18n["Settings.BtnRescanBrowsers"],
            size: 34,
            iconSize: 16);

        Button browseFolderBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Settings.BtnBrowseProfileFolder"],
            null,
            VectorIcons.Folder);

        Button openCreateModalBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Settings.BtnCreateProfile"],
            null,
            VectorIcons.Plus);

        Grid.SetColumn(header, 0);
        Grid.SetColumn(rescanProfilesBtn, 1);
        Grid.SetColumn(browseFolderBtn, 2);
        Grid.SetColumn(openCreateModalBtn, 3);
        topBar.Children.Add(header);
        topBar.Children.Add(rescanProfilesBtn);
        topBar.Children.Add(browseFolderBtn);
        topBar.Children.Add(openCreateModalBtn);
        Grid.SetRow(topBar, 0);
        pageGrid.Children.Add(topBar);

        // Row 1: Unified 2-Level Search & Filter Bar
        BulkObservableCollection<BrowserProfileOption> filteredProfiles = new();
        List<BrowserProfileOption> cachedProfiles = new();
        Dictionary<string, long> profileSizeCache = new(StringComparer.OrdinalIgnoreCase);
        string? sourceFilter = null; // "managed", "system", or null
        SearchFilterBar profileSearchBar = null!;

        void UpdateHeaderWithActiveBrowser()
        {
            PreferredBrowserKind effectiveKind = this._vm.Context.Settings.ResolveEffectiveBrowserKind();
            string browserLabel = this._i18n.FormatBrowserKind(effectiveKind);
            headerTitleTb.Text = $"{this._i18n["Settings.ProfilesHeader"]} — {browserLabel}";
        }

        void ReloadProfilesFromDisk() =>
            cachedProfiles = this._vm.GetAvailableBrowserProfiles();

        void RefreshFilteredProfiles()
        {
            UpdateHeaderWithActiveBrowser();
            string q = (profileSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<BrowserProfileOption> matches = new(cachedProfiles.Count);

            for (int i = 0; i < cachedProfiles.Count; i++)
            {
                BrowserProfileOption opt = cachedProfiles[i];
                if (string.Equals(sourceFilter, "managed", StringComparison.OrdinalIgnoreCase) && !opt.IsManagedByApp)
                {
                    continue;
                }

                if (string.Equals(sourceFilter, "system", StringComparison.OrdinalIgnoreCase) && opt.IsManagedByApp)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(q) &&
                    !opt.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !opt.DirectoryPath.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(opt);
            }

            filteredProfiles.ReplaceAll(matches);
        }

        FilterCategoryDefinition sourceCategory = new(
            "source",
            this._i18n["Settings.FilterCatProfileSource"],
            VectorIcons.Folder,
            () =>
            [
                new FilterOptionItem("managed", this._i18n["Settings.FilterProfileManaged"]),
                new FilterOptionItem("system", this._i18n["Settings.FilterProfileSystem"])
            ],
            selectedIds =>
            {
                sourceFilter = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshFilteredProfiles();
            },
            allowMultiple: false);

        profileSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Settings.SearchProfilesPlaceholder"]);
        profileSearchBar.SetFilterCategories(
            [sourceCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);
        profileSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshFilteredProfiles();
        profileSearchBar.SearchButton.Click += (_, _) => RefreshFilteredProfiles();

        Grid.SetRow(profileSearchBar, 1);
        pageGrid.Children.Add(profileSearchBar);

        ReloadProfilesFromDisk();
        RefreshFilteredProfiles();

        // Row 2: Profiles ListBox with Per-Row Use & Conditional Delete (App-Managed Only)
        ListBox profilesList = FormField.CreateListBox<BrowserProfileOption>(
            this._theme,
            filteredProfiles,
            opt =>
            {
                bool isCurrentlyAssigned = !string.IsNullOrWhiteSpace(this._vm.SelectedAccount?.ProfileDirectory) &&
                    string.Equals(this._vm.SelectedAccount!.ProfileDirectory, opt.DirectoryPath, StringComparison.OrdinalIgnoreCase);

                Action? deleteAction = null;
                if (opt.IsManagedByApp)
                {
                    deleteAction = () =>
                    {
                        ConfirmDialog.ShowAsync(
                            this,
                            this._theme,
                            this._i18n["Confirm.DeleteProfileTitle"],
                            this._i18n.Format("Confirm.DeleteProfileMessage", opt.DisplayName),
                            this._i18n["Confirm.BtnConfirmDelete"],
                            this._i18n["Groups.BtnCancelEditTag"],
                            async () =>
                            {
                                _ = await this._vm.DeleteManagedBrowserProfileAsync(opt).ConfigureAwait(true);
                                ReloadProfilesFromDisk();
                                RefreshFilteredProfiles();
                            });
                    };
                }

                return BrowserProfileCard.Create(
                    this._theme,
                    this._i18n,
                    opt,
                    isCurrentlyAssigned,
                    profileSizeCache,
                    ShellViewModel.GetProfileDirectorySizeBytes,
                    ShellViewModel.FormatByteSize,
                    onUseProfileAsync: async () =>
                    {
                        await this._vm.AssignBrowserProfileToSelectedAccountAsync(opt.DirectoryPath, opt.DisplayName).ConfigureAwait(true);
                        ReloadProfilesFromDisk();
                        RefreshFilteredProfiles();
                    },
                    onOpenFolder: () => ShellViewModel.OpenProfileFolderInExplorer(opt.DirectoryPath),
                    onDeleteManagedProfile: deleteAction);
            },
            isVisible: filteredProfiles.Count > 0);

        Border emptyProfilesBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Storage.SliceNoProfiles"],
            VectorIcons.Browser,
            out TextBlock emptyProfilesTb);
        emptyProfilesBanner.IsVisible = filteredProfiles.Count == 0;

        filteredProfiles.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredProfiles.Count == 0;
            emptyProfilesBanner.IsVisible = isEmpty;
            profilesList.IsVisible = !isEmpty;
        };

        Grid listHost = EmptyStateBanner.CreateHost(profilesList, emptyProfilesBanner);
        Grid.SetRow(listHost, 2);
        pageGrid.Children.Add(listHost);

        // ───────────────────────────── Modal Popup Overlay (Create New Profile) ─────────────────────────────
        StackPanel modalStack = new() { Spacing = 14 };

        TextBlock modalTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Settings.BtnCreateProfile"],
            16,
            FontWeight.Bold);
        TextBlock modalHintTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Settings.CreateProfileModalHint"],
            12.5,
            FontWeight.Normal,
            isSecondary: true);
        modalHintTb.TextWrapping = TextWrapping.Wrap;

        TextBox newProfileNameBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Settings.NewProfileNamePlaceholder"]);

        Grid modalBtnRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };

        Button cancelCreateBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnCancelEditTag"],
            null,
            VectorIcons.StopCircle);

        Button confirmCreateBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Settings.BtnConfirmCreateProfile"],
            null,
            VectorIcons.CheckCircle);

        Border createModalBackdrop = null!;

        void CloseCreateModal()
        {
            createModalBackdrop.IsVisible = false;
            newProfileNameBox.Text = string.Empty;
        }

        openCreateModalBtn.Click += (_, _) =>
        {
            newProfileNameBox.Text = string.Empty;
            createModalBackdrop.IsVisible = true;
            _ = newProfileNameBox.Focus();
        };

        cancelCreateBtn.Click += (_, _) => CloseCreateModal();

        confirmCreateBtn.Click += async (_, _) =>
        {
            string rawName = newProfileNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                _ = newProfileNameBox.Focus();
                return;
            }

            CloseCreateModal();
            _ = await this._vm.CreateAndAssignBrowserProfileAsync(rawName).ConfigureAwait(true);
            ReloadProfilesFromDisk();
            RefreshFilteredProfiles();
        };

        Grid.SetColumn(cancelCreateBtn, 1);
        Grid.SetColumn(confirmCreateBtn, 2);
        modalBtnRow.Children.Add(cancelCreateBtn);
        modalBtnRow.Children.Add(confirmCreateBtn);

        modalStack.Children.Add(modalTitleTb);
        modalStack.Children.Add(modalHintTb);
        modalStack.Children.Add(newProfileNameBox);
        modalStack.Children.Add(modalBtnRow);

        createModalBackdrop = ModalOverlay.Create(this._theme, modalStack, CloseCreateModal);

        rescanProfilesBtn.Click += (_, _) =>
        {
            profileSizeCache.Clear();
            StorageUsageAnalyzer.InvalidateCache();
            ReloadProfilesFromDisk();
            RefreshFilteredProfiles();
        };
        browseFolderBtn.Click += async (_, _) =>
        {
            string? folderPath = await this.PickSingleFolderAsync(this._i18n["Settings.BtnBrowseProfileFolder"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                await this._vm.AssignBrowserProfileToSelectedAccountAsync(folderPath).ConfigureAwait(true);
                ReloadProfilesFromDisk();
                RefreshFilteredProfiles();
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedBrowserKind), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ShellViewModel.CustomBrowserExecutablePath), StringComparison.Ordinal))
            {
                ReloadProfilesFromDisk();
                RefreshFilteredProfiles();
            }
            else if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshFilteredProfiles();
            }
        };
        this._vm.Accounts.CollectionChanged += (_, _) =>
        {
            ReloadProfilesFromDisk();
            RefreshFilteredProfiles();
        };

        tabOverlayHost.Children.Add(pageGrid);
        tabOverlayHost.Children.Add(createModalBackdrop);

        refreshI18n = () =>
        {
            headerDescTb.Text = this._i18n["Settings.ProfilesDesc"];
            AppButton.SetText(rescanProfilesBtn, this._i18n["Settings.BtnRescanBrowsers"]);
            AppButton.SetText(browseFolderBtn, this._i18n["Settings.BtnBrowseProfileFolder"]);
            AppButton.SetText(openCreateModalBtn, this._i18n["Settings.BtnCreateProfile"]);
            sourceCategory.Title = this._i18n["Settings.FilterCatProfileSource"];
            profileSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Settings.SearchProfilesPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);
            emptyProfilesTb.Text = this._i18n["Storage.SliceNoProfiles"];
            modalTitleTb.Text = this._i18n["Settings.BtnCreateProfile"];
            modalHintTb.Text = this._i18n["Settings.CreateProfileModalHint"];
            newProfileNameBox.PlaceholderText = this._i18n["Settings.NewProfileNamePlaceholder"];
            AppButton.SetText(cancelCreateBtn, this._i18n["Groups.BtnCancelEditTag"]);
            AppButton.SetText(confirmCreateBtn, this._i18n["Settings.BtnConfirmCreateProfile"]);
            RefreshFilteredProfiles();
        };

        return tabOverlayHost;
    }
}
