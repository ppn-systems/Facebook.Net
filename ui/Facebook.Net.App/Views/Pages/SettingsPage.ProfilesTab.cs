// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.Core.Configuration;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

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
        string? sourceFilter = null; // "managed", "system", or null
        SearchFilterBar profileSearchBar = null!;

        void UpdateHeaderWithActiveBrowser()
        {
            PreferredBrowserKind effectiveKind = this._vm.Context.Settings.ResolveEffectiveBrowserKind();
            string browserLabel = this._i18n.FormatBrowserKind(effectiveKind);
            headerTitleTb.Text = $"{this._i18n["Settings.ProfilesHeader"]} — {browserLabel}";
        }

        void RefreshFilteredProfiles()
        {
            UpdateHeaderWithActiveBrowser();
            List<BrowserProfileOption> allProfiles = this._vm.GetAvailableBrowserProfiles();
            string q = (profileSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<BrowserProfileOption> matches = new(allProfiles.Count);

            for (int i = 0; i < allProfiles.Count; i++)
            {
                BrowserProfileOption opt = allProfiles[i];
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

        RefreshFilteredProfiles();

        // Row 2: Profiles ListBox with Per-Row Use & Conditional Delete (App-Managed Only)
        ListBox profilesList = FormField.CreateListBox<BrowserProfileOption>(
            this._theme,
            filteredProfiles,
            opt =>
            {
                bool isCurrentlyAssigned = !string.IsNullOrWhiteSpace(this._vm.SelectedAccount?.ProfileDirectory) &&
                    string.Equals(this._vm.SelectedAccount!.ProfileDirectory, opt.DirectoryPath, StringComparison.OrdinalIgnoreCase);

                Grid rowGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,128,142,28"),
                    ColumnSpacing = 10,
                    Margin = new Thickness(4, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };

                Border iconCircle = new()
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(10),
                    Background = opt.IsManagedByApp ? this._theme.Palette.BrandSubtleFill : this._theme.Palette.SecondarySurface,
                    BorderBrush = opt.IsManagedByApp ? this._theme.Palette.BrandPrimary : this._theme.Palette.SubtleBorder,
                    BorderThickness = new Thickness(1),
                    Child = VectorIcons.Create(
                        VectorIcons.Browser,
                        16,
                        opt.IsManagedByApp ? this._theme.Palette.BrandPrimary : this._theme.Palette.TextSecondary),
                    VerticalAlignment = VerticalAlignment.Center
                };

                StackPanel infoStack = new()
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };

                StackPanel nameAndSizeRow = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock nameTb = SectionHeader.CreateLabel(this._theme, opt.DisplayName, 13, FontWeight.SemiBold);
                nameTb.MaxLines = 1;
                nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
                nameAndSizeRow.Children.Add(nameTb);

                if (opt.IsManagedByApp)
                {
                    long bytes = ShellViewModel.GetProfileDirectorySizeBytes(opt.DirectoryPath);
                    TextBlock sizeTb = SectionHeader.CreateLabel(
                        this._theme,
                        $"•  {ShellViewModel.FormatByteSize(bytes)}",
                        11.5,
                        FontWeight.Medium,
                        isSecondary: true);
                    nameAndSizeRow.Children.Add(sizeTb);
                }

                TextBlock pathTb = SectionHeader.CreateLabel(this._theme, opt.DirectoryPath, 11.5, FontWeight.Normal, isSecondary: true);
                pathTb.MaxLines = 1;
                pathTb.TextTrimming = TextTrimming.CharacterEllipsis;

                infoStack.Children.Add(nameAndSizeRow);
                infoStack.Children.Add(pathTb);

                Border sourceBadge = StatusPill.Create(
                    this._theme,
                    opt.IsManagedByApp ? this._i18n["Settings.BadgeManagedProfile"] : this._i18n["Settings.BadgeSystemProfile"],
                    opt.IsManagedByApp ? AuditSeverity.Success : AuditSeverity.Info);
                sourceBadge.HorizontalAlignment = HorizontalAlignment.Stretch;
                sourceBadge.Height = 28;

                Grid.SetColumn(iconCircle, 0);
                Grid.SetColumn(infoStack, 1);
                Grid.SetColumn(sourceBadge, 2);
                rowGrid.Children.Add(iconCircle);
                rowGrid.Children.Add(infoStack);
                rowGrid.Children.Add(sourceBadge);

                if (isCurrentlyAssigned)
                {
                    Border activePill = StatusPill.Create(
                        this._theme,
                        this._i18n["Settings.BadgeActiveProfile"],
                        AuditSeverity.Success);
                    activePill.HorizontalAlignment = HorizontalAlignment.Stretch;
                    activePill.Height = 28;
                    Grid.SetColumn(activePill, 3);
                    rowGrid.Children.Add(activePill);
                }
                else
                {
                    Button useProfileBtn = new()
                    {
                        Content = new TextBlock
                        {
                            Text = this._i18n["Settings.BtnUseProfile"],
                            FontSize = 11,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = this._theme.Palette.BrandPrimary,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        Background = this._theme.Palette.BrandSubtleFill,
                        BorderBrush = this._theme.Palette.BrandPrimary,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Height = 28,
                        Padding = new Thickness(10, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };
                    useProfileBtn.Click += async (_, _) =>
                    {
                        await this._vm.AssignBrowserProfileToSelectedAccountAsync(opt.DirectoryPath, opt.DisplayName).ConfigureAwait(true);
                        RefreshFilteredProfiles();
                    };
                    Grid.SetColumn(useProfileBtn, 3);
                    rowGrid.Children.Add(useProfileBtn);
                }

                // Strictly allow deletion ONLY for profiles created by our app inside ProfilesRootDirectory
                if (opt.IsManagedByApp)
                {
                    Button deleteProfileBtn = AppButton.CreateRowIcon(
                        this._theme,
                        VectorIcons.Trash,
                        this._i18n["Accounts.BtnRowDelete"],
                        () =>
                        {
                            ConfirmDialogModal.ShowAsync(
                                this,
                                this._theme,
                                this._i18n["Confirm.DeleteProfileTitle"],
                                this._i18n.Format("Confirm.DeleteProfileMessage", opt.DisplayName),
                                this._i18n["Confirm.BtnConfirmDelete"],
                                this._i18n["Groups.BtnCancelEditTag"],
                                async () =>
                                {
                                    _ = await this._vm.DeleteManagedBrowserProfileAsync(opt).ConfigureAwait(true);
                                    RefreshFilteredProfiles();
                                });
                        },
                        AuditSeverity.Error);
                    Grid.SetColumn(deleteProfileBtn, 4);
                    rowGrid.Children.Add(deleteProfileBtn);
                }

                return rowGrid;
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
        Border createModalBackdrop = new()
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(155, 8, 12, 22)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

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

        Border modalCard = SurfaceCard.Create(this._theme, modalStack, new Thickness(22, 18));
        modalCard.Width = 420;
        modalCard.CornerRadius = new CornerRadius(14);
        modalCard.HorizontalAlignment = HorizontalAlignment.Center;
        modalCard.VerticalAlignment = VerticalAlignment.Center;
        modalCard.PointerPressed += (_, e) => e.Handled = true;

        createModalBackdrop.PointerPressed += (_, _) => CloseCreateModal();
        createModalBackdrop.Child = modalCard;

        rescanProfilesBtn.Click += (_, _) =>
        {
            StorageUsageAnalyzer.InvalidateCache();
            RefreshFilteredProfiles();
        };
        browseFolderBtn.Click += async (_, _) =>
        {
            string? folderPath = await this.PickSingleFolderAsync(this._i18n["Settings.BtnBrowseProfileFolder"]).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                await this._vm.AssignBrowserProfileToSelectedAccountAsync(folderPath).ConfigureAwait(true);
                RefreshFilteredProfiles();
            }
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedBrowserKind), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ShellViewModel.CustomBrowserExecutablePath), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ShellViewModel.SelectedAccount), StringComparison.Ordinal))
            {
                RefreshFilteredProfiles();
            }
        };
        this._vm.Accounts.CollectionChanged += (_, _) => RefreshFilteredProfiles();

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
