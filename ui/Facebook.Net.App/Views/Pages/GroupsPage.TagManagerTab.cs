// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;
using Facebook.Net.UI.Components.Controls;
using Facebook.Net.UI.Components.Icons;
using Facebook.Net.UI.Core.Audit;
using Facebook.Net.UI.Core.Collections;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App.Views.Pages;

/// <summary>
/// Dedicated Tab 3 (Color-Coded Tag Manager &amp; Batch Tag Assignment) for <see cref="GroupsPage"/>.
/// </summary>
public sealed partial class GroupsPage
{
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI checkmark symbol only.")]
    private Control BuildTagManagerTab(out Action refreshI18n)
    {
        Grid tabOverlayHost = new();

        Grid rootGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1*"),
            ColumnSpacing = 14
        };

        GroupTag? editingTag = null;
        GroupTag? selectedAssignTag = this._vm.GroupTags.Count > 0 ? this._vm.GroupTags[0] : null;
        string? assignFilterMode = null; // "assigned", "unassigned", or null

        // ───────────────────────────── Right Column: Color Tags List + Trigger Modal Button ─────────────────────────────
        Grid leftGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12
        };

        Grid leftHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.TagCreatorTitle"],
            this._i18n["Groups.TagManagerDesc"],
            out TextBlock leftTitleTb,
            out TextBlock leftDescTb);
        Grid.SetRow(leftHeader, 0);
        leftGrid.Children.Add(leftHeader);

        Button openCreateModalBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnCreateTag"],
            null,
            VectorIcons.Plus);
        Grid.SetRow(openCreateModalBtn, 1);
        leftGrid.Children.Add(openCreateModalBtn);

        StackPanel tagListStack = new()
        {
            Spacing = 6
        };

        ScrollViewer tagListScroll = new()
        {
            Content = tagListStack
        };
        Grid.SetRow(tagListScroll, 2);
        leftGrid.Children.Add(tagListScroll);

        Border leftCard = SurfaceCard.Create(this._theme, leftGrid, new Thickness(14));
        Grid.SetColumn(leftCard, 1);
        rootGrid.Children.Add(leftCard);

        // ───────────────────────────── Modal Popup Overlay (Create / Edit Color Tag) ─────────────────────────────
        Border modalBackdrop = new()
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(148, 10, 14, 24)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        StackPanel modalContentStack = new()
        {
            Spacing = 14
        };

        Grid modalHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock modalTitleTb = SectionHeader.CreateLabel(
            this._theme,
            this._i18n["Groups.BtnCreateTag"],
            16,
            FontWeight.Bold);

        ContentControl modalLivePreviewHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(modalTitleTb, 0);
        Grid.SetColumn(modalLivePreviewHost, 1);
        modalHeaderRow.Children.Add(modalTitleTb);
        modalHeaderRow.Children.Add(modalLivePreviewHost);
        modalContentStack.Children.Add(modalHeaderRow);

        TextBox tagNameBox = FormField.CreateTextBox(
            this._theme,
            this._i18n["Groups.TagNamePlaceholder"],
            this._vm.NewGroupTagName);

        WrapPanel swatchesWrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 10,
            LineSpacing = 10
        };

        void RefreshModalLivePreview()
        {
            string previewName = string.IsNullOrWhiteSpace(this._vm.NewGroupTagName)
                ? this._i18n["Groups.FilterCatColorTag"]
                : this._vm.NewGroupTagName.Trim();
            GroupTag sampleTag = new("preview", previewName, this._vm.NewGroupTagColorHex);
            modalLivePreviewHost.Content = GroupRowCard.CreateTagPill(sampleTag, isActive: true);
        }

        Action rebuildSwatches = null!;
        rebuildSwatches = () =>
        {
            swatchesWrap.Children.Clear();
            for (int i = 0; i < GroupRowCard.PresetTagColors.Length; i++)
            {
                string hex = GroupRowCard.PresetTagColors[i];
                bool isChosen = string.Equals(this._vm.NewGroupTagColorHex, hex, StringComparison.OrdinalIgnoreCase);
                Color c = Color.Parse(hex);

                Border swatch = new()
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(999),
                    Background = new SolidColorBrush(c),
                    BorderBrush = isChosen ? this._theme.Palette.TextPrimary : Brushes.Transparent,
                    BorderThickness = new Thickness(isChosen ? 2.5 : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = isChosen
                        ? new TextBlock
                        {
                            Text = "✓",
                            FontSize = 13,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                        : null
                };
                swatch.PointerPressed += (_, _) =>
                {
                    this._vm.NewGroupTagColorHex = hex;
                    rebuildSwatches();
                    RefreshModalLivePreview();
                };
                swatchesWrap.Children.Add(swatch);
            }
        };

        tagNameBox.TextChanged += (_, _) =>
        {
            this._vm.NewGroupTagName = tagNameBox.Text ?? string.Empty;
            RefreshModalLivePreview();
        };

        modalContentStack.Children.Add(tagNameBox);
        modalContentStack.Children.Add(swatchesWrap);

        Grid formButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };

        Button cancelEditBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnCancelEditTag"],
            null,
            VectorIcons.StopCircle);

        Button saveOrCreateTagBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnCreateTag"],
            null,
            VectorIcons.CheckCircle);

        void UpdateEditModeUi()
        {
            bool isEditing = editingTag is not null;
            modalTitleTb.Text = isEditing ? this._i18n["Groups.BtnUpdateTag"] : this._i18n["Groups.BtnCreateTag"];
            AppButton.SetText(
                saveOrCreateTagBtn,
                isEditing ? this._i18n["Groups.BtnUpdateTag"] : this._i18n["Groups.BtnCreateTag"]);
            RefreshModalLivePreview();
        }

        void OpenTagModal(GroupTag? tagToEdit)
        {
            editingTag = tagToEdit;
            if (tagToEdit is not null)
            {
                this._vm.NewGroupTagName = tagToEdit.Name;
                this._vm.NewGroupTagColorHex = tagToEdit.ColorHex;
            }
            else
            {
                this._vm.NewGroupTagName = string.Empty;
            }

            tagNameBox.Text = this._vm.NewGroupTagName;
            rebuildSwatches();
            UpdateEditModeUi();
            modalBackdrop.IsVisible = true;
            _ = tagNameBox.Focus();
        }

        void CloseTagModal()
        {
            modalBackdrop.IsVisible = false;
            editingTag = null;
            this._vm.NewGroupTagName = string.Empty;
        }

        openCreateModalBtn.Click += (_, _) => OpenTagModal(null);

        saveOrCreateTagBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(this._vm.NewGroupTagName))
            {
                _ = tagNameBox.Focus();
                return;
            }

            if (editingTag is not null)
            {
                string tagId = editingTag.Id;
                string newName = this._vm.NewGroupTagName;
                string newColor = this._vm.NewGroupTagColorHex;
                CloseTagModal();
                await this._vm.UpdateGroupTagAsync(tagId, newName, newColor).ConfigureAwait(true);
            }
            else if (this._vm.CreateGroupTagCommand.CanExecute(null))
            {
                await this._vm.CreateGroupTagCommand.ExecuteAsync(null).ConfigureAwait(true);
                CloseTagModal();
            }
        };

        cancelEditBtn.Click += (_, _) => CloseTagModal();

        Grid.SetColumn(cancelEditBtn, 1);
        Grid.SetColumn(saveOrCreateTagBtn, 2);
        formButtonsRow.Children.Add(cancelEditBtn);
        formButtonsRow.Children.Add(saveOrCreateTagBtn);
        modalContentStack.Children.Add(formButtonsRow);

        Border modalDialogCard = SurfaceCard.Create(this._theme, modalContentStack, new Thickness(22, 18));
        modalDialogCard.Width = 420;
        modalDialogCard.CornerRadius = new CornerRadius(14);
        modalDialogCard.HorizontalAlignment = HorizontalAlignment.Center;
        modalDialogCard.VerticalAlignment = VerticalAlignment.Center;
        modalDialogCard.PointerPressed += (_, e) => e.Handled = true;

        modalBackdrop.PointerPressed += (_, _) => CloseTagModal();
        modalBackdrop.Child = modalDialogCard;
        rebuildSwatches();

        // ───────────────────────────── Right Column: Batch Tag Assignment ─────────────────────────────
        Grid rightGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 10
        };

        Grid rightHeaderRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid rightHeader = SectionHeader.Create(
            this._theme,
            this._i18n["Groups.TagAssignPanelTitle"],
            this._i18n["Groups.TagAssignPanelHint"],
            out TextBlock rightTitleTb,
            out TextBlock rightHintTb);

        ContentControl activeTagBadgeHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(rightHeader, 0);
        Grid.SetColumn(activeTagBadgeHost, 1);
        rightHeaderRow.Children.Add(rightHeader);
        rightHeaderRow.Children.Add(activeTagBadgeHost);
        Grid.SetRow(rightHeaderRow, 0);
        rightGrid.Children.Add(rightHeaderRow);

        BulkObservableCollection<SavedTargetGroup> filteredAssignGroups = new();
        SearchFilterBar assignSearchBar = null!;

        Grid batchButtonsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10
        };

        Button batchAssignBtn = AppButton.CreatePrimary(
            this._theme,
            this._i18n["Groups.BtnBatchAssignAllFiltered"],
            null,
            VectorIcons.CheckCircle);

        Button batchRemoveBtn = AppButton.CreateSecondary(
            this._theme,
            this._i18n["Groups.BtnBatchRemoveAllFiltered"],
            null,
            VectorIcons.StopCircle);

        batchAssignBtn.Click += async (_, _) =>
        {
            if (selectedAssignTag is null || filteredAssignGroups.Count == 0)
            {
                return;
            }

            List<SavedTargetGroup> snapshot = [.. filteredAssignGroups];
            await this._vm.BatchSetTagOnGroupsAsync(selectedAssignTag.Id, snapshot, assignTag: true).ConfigureAwait(true);
        };

        batchRemoveBtn.Click += (_, _) =>
        {
            if (selectedAssignTag is null || filteredAssignGroups.Count == 0)
            {
                return;
            }

            GroupTag capturedTag = selectedAssignTag;
            List<SavedTargetGroup> snapshot = [.. filteredAssignGroups];
            ConfirmDialogModal.ShowAsync(
                this,
                this._theme,
                this._i18n["Confirm.BatchUntagTitle"],
                this._i18n.Format("Confirm.BatchUntagMessage", capturedTag.Name, snapshot.Count),
                this._i18n["Groups.BtnBatchRemoveAllFiltered"],
                this._i18n["Groups.BtnCancelEditTag"],
                () => this._vm.BatchSetTagOnGroupsAsync(capturedTag.Id, snapshot, assignTag: false));
        };

        Grid.SetColumn(batchAssignBtn, 0);
        Grid.SetColumn(batchRemoveBtn, 1);
        batchButtonsRow.Children.Add(batchAssignBtn);
        batchButtonsRow.Children.Add(batchRemoveBtn);
        Grid.SetRow(batchButtonsRow, 2);
        rightGrid.Children.Add(batchButtonsRow);

        void RefreshAssignGroups()
        {
            if (selectedAssignTag is not null)
            {
                GroupTag? refreshed = null;
                for (int i = 0; i < this._vm.GroupTags.Count; i++)
                {
                    if (string.Equals(this._vm.GroupTags[i].Id, selectedAssignTag.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        refreshed = this._vm.GroupTags[i];
                        break;
                    }
                }

                selectedAssignTag = refreshed ?? (this._vm.GroupTags.Count > 0 ? this._vm.GroupTags[0] : null);
            }
            else if (this._vm.GroupTags.Count > 0)
            {
                selectedAssignTag = this._vm.GroupTags[0];
            }

            activeTagBadgeHost.Content = selectedAssignTag is not null
                ? GroupRowCard.CreateTagPill(selectedAssignTag, isActive: true)
                : null;
            batchAssignBtn.IsEnabled = selectedAssignTag is not null;
            batchRemoveBtn.IsEnabled = selectedAssignTag is not null;

            string q = (assignSearchBar?.SearchTextBox.Text ?? string.Empty).Trim();
            List<SavedTargetGroup> matches = new(this._vm.TargetGroups.Count);
            for (int i = 0; i < this._vm.TargetGroups.Count; i++)
            {
                SavedTargetGroup tg = this._vm.TargetGroups[i];
                if (selectedAssignTag is not null && !string.IsNullOrEmpty(assignFilterMode))
                {
                    bool hasSelectedTag = tg.HasTag(selectedAssignTag.Id);
                    if (string.Equals(assignFilterMode, "assigned", StringComparison.OrdinalIgnoreCase) && !hasSelectedTag)
                    {
                        continue;
                    }

                    if (string.Equals(assignFilterMode, "unassigned", StringComparison.OrdinalIgnoreCase) && hasSelectedTag)
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(q) &&
                    !tg.GroupName.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(tg);
            }

            filteredAssignGroups.ReplaceAll(matches);
        }

        assignSearchBar = new SearchFilterBar(
            this._theme,
            this._i18n["Common.FilterButton"],
            this._i18n["Groups.FilterPlaceholder"]);

        FilterCategoryDefinition assignTagCategory = new(
            key: "tag_assign",
            title: this._i18n["Groups.FilterCatColorTag"],
            iconSvg: VectorIcons.Bookmark,
            optionsProvider: () =>
            [
                new FilterOptionItem("assigned", this._i18n["Groups.FilterTagAssigned"], ColorHex: selectedAssignTag?.ColorHex),
                new FilterOptionItem("unassigned", this._i18n["Groups.FilterTagUnassigned"])
            ],
            onApplied: selectedIds =>
            {
                assignFilterMode = selectedIds.Count > 0 ? selectedIds[0] : null;
                RefreshAssignGroups();
            },
            allowMultiple: false);

        assignSearchBar.SetFilterCategories(
            [assignTagCategory],
            this._i18n["Common.SearchInList"],
            this._i18n["Common.BtnApplyFilter"]);

        assignSearchBar.SearchTextBox.TextChanged += (_, _) => RefreshAssignGroups();
        assignSearchBar.SearchButton.Click += (_, _) => RefreshAssignGroups();
        Grid.SetRow(assignSearchBar, 1);
        rightGrid.Children.Add(assignSearchBar);

        Action rebuildLeftTagList = null!;
        rebuildLeftTagList = () =>
        {
            tagListStack.Children.Clear();
            for (int i = 0; i < this._vm.GroupTags.Count; i++)
            {
                GroupTag tag = this._vm.GroupTags[i];
                int groupCount = 0;
                for (int j = 0; j < this._vm.TargetGroups.Count; j++)
                {
                    if (this._vm.TargetGroups[j].HasTag(tag.Id))
                    {
                        groupCount++;
                    }
                }

                bool isSelected = selectedAssignTag is not null &&
                                  string.Equals(selectedAssignTag.Id, tag.Id, StringComparison.OrdinalIgnoreCase);

                Grid tagRow = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("*,68,28,28"),
                    ColumnSpacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Border pill = GroupRowCard.CreateTagPill(
                    tag,
                    isActive: isSelected,
                    onClick: () =>
                    {
                        selectedAssignTag = tag;
                        rebuildLeftTagList();
                        RefreshAssignGroups();
                        assignSearchBar.RefreshFilterChips();
                    });
                pill.HorizontalAlignment = HorizontalAlignment.Left;

                TextBlock countTb = SectionHeader.CreateLabel(
                    this._theme,
                    this._i18n.Format("Groups.TagGroupCountFormat", groupCount),
                    11.5,
                    FontWeight.Medium,
                    isSecondary: true);
                countTb.VerticalAlignment = VerticalAlignment.Center;
                countTb.HorizontalAlignment = HorizontalAlignment.Right;

                Button editBtn = AppButton.CreateRowIcon(
                    this._theme,
                    VectorIcons.Edit,
                    this._i18n["Groups.BtnEditTagShort"],
                    () =>
                    {
                        selectedAssignTag = tag;
                        rebuildLeftTagList();
                        RefreshAssignGroups();
                        OpenTagModal(tag);
                    });

                Button deleteBtn = AppButton.CreateRowIcon(
                    this._theme,
                    VectorIcons.Trash,
                    this._i18n["Groups.BtnDeleteTagShort"],
                    () =>
                    {
                        ConfirmDialogModal.ShowAsync(
                            this,
                            this._theme,
                            this._i18n["Confirm.DeleteTagTitle"],
                            this._i18n.Format("Confirm.DeleteTagMessage", tag.Name),
                            this._i18n["Confirm.BtnConfirmDelete"],
                            this._i18n["Groups.BtnCancelEditTag"],
                            async () =>
                            {
                                if (editingTag is not null && string.Equals(editingTag.Id, tag.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    CloseTagModal();
                                }

                                await this._vm.DeleteGroupTagAsync(tag).ConfigureAwait(true);
                            });
                    },
                    AuditSeverity.Error);

                Grid.SetColumn(pill, 0);
                Grid.SetColumn(countTb, 1);
                Grid.SetColumn(editBtn, 2);
                Grid.SetColumn(deleteBtn, 3);
                tagRow.Children.Add(pill);
                tagRow.Children.Add(countTb);
                tagRow.Children.Add(editBtn);
                tagRow.Children.Add(deleteBtn);

                Border rowCard = SurfaceCard.CreateInset(this._theme, tagRow, new Thickness(10, 7));
                if (isSelected)
                {
                    rowCard.BorderBrush = this._theme.Palette.BrandPrimary;
                }

                rowCard.Cursor = new Cursor(StandardCursorType.Hand);
                rowCard.PointerPressed += (_, _) =>
                {
                    selectedAssignTag = tag;
                    rebuildLeftTagList();
                    RefreshAssignGroups();
                    assignSearchBar.RefreshFilterChips();
                };

                tagListStack.Children.Add(rowCard);
            }
        };

        rebuildLeftTagList();
        RefreshAssignGroups();

        ListBox assignList = FormField.CreateListBox<SavedTargetGroup>(
            this._theme,
            filteredAssignGroups,
            tg =>
            {
                bool hasActiveTag = selectedAssignTag is not null && tg.HasTag(selectedAssignTag.Id);

                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                    ColumnSpacing = 10,
                    Margin = new Thickness(4, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };

                CheckBox tagCheck = new()
                {
                    IsChecked = hasActiveTag,
                    IsEnabled = selectedAssignTag is not null,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tagCheck.IsCheckedChanged += async (_, _) =>
                {
                    if (selectedAssignTag is null)
                    {
                        return;
                    }

                    bool nextChecked = tagCheck.IsChecked ?? false;
                    if (nextChecked != tg.HasTag(selectedAssignTag.Id))
                    {
                        await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, selectedAssignTag.Id).ConfigureAwait(true);
                    }
                };

                Border avatar = AvatarBadge.Create(this._theme, tg.GroupName, 30, tg.AvatarUrl);

                StackPanel nameAndTagsStack = new()
                {
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock nameTb = SectionHeader.CreateLabel(this._theme, tg.GroupName, 13, FontWeight.SemiBold);
                nameTb.TextWrapping = TextWrapping.NoWrap;
                nameTb.TextTrimming = TextTrimming.CharacterEllipsis;
                nameAndTagsStack.Children.Add(nameTb);

                IReadOnlyList<GroupTag> assignedTags = this._vm.GetGroupTagsForGroup(tg.GroupId);
                if (assignedTags.Count > 0)
                {
                    WrapPanel tagWrap = new()
                    {
                        Orientation = Orientation.Horizontal,
                        ItemSpacing = 5,
                        LineSpacing = 4
                    };
                    for (int i = 0; i < assignedTags.Count; i++)
                    {
                        GroupTag t = assignedTags[i];
                        tagWrap.Children.Add(GroupRowCard.CreateTagPill(
                            t,
                            onClick: () =>
                            {
                                selectedAssignTag = t;
                                rebuildLeftTagList();
                                RefreshAssignGroups();
                                assignSearchBar.RefreshFilterChips();
                            },
                            onDelete: async () =>
                            {
                                await this._vm.ToggleGroupTagAsync(tg.GroupId, tg.GroupName, tg.AvatarUrl, t.Id).ConfigureAwait(true);
                            }));
                    }

                    nameAndTagsStack.Children.Add(tagWrap);
                }

                Grid.SetColumn(tagCheck, 0);
                Grid.SetColumn(avatar, 1);
                Grid.SetColumn(nameAndTagsStack, 2);
                row.Children.Add(tagCheck);
                row.Children.Add(avatar);
                row.Children.Add(nameAndTagsStack);
                return row;
            },
            isVisible: filteredAssignGroups.Count > 0);

        Border emptyAssignBanner = EmptyStateBanner.Create(
            this._theme,
            this._i18n["Empty.TargetGroups"],
            VectorIcons.Bookmark,
            out TextBlock emptyAssignTb);
        emptyAssignBanner.IsVisible = filteredAssignGroups.Count == 0;

        this._vm.TargetGroups.CollectionChanged += (_, _) =>
        {
            rebuildLeftTagList();
            RefreshAssignGroups();
            bool isEmpty = filteredAssignGroups.Count == 0;
            emptyAssignBanner.IsVisible = isEmpty;
            assignList.IsVisible = !isEmpty;
        };
        this._vm.GroupTags.CollectionChanged += (_, _) =>
        {
            rebuildLeftTagList();
            RefreshAssignGroups();
            assignSearchBar.RefreshFilterChips();
        };
        filteredAssignGroups.CollectionChanged += (_, _) =>
        {
            bool isEmpty = filteredAssignGroups.Count == 0;
            emptyAssignBanner.IsVisible = isEmpty;
            assignList.IsVisible = !isEmpty;
        };

        this._vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.NewGroupTagName), StringComparison.Ordinal) &&
                !string.Equals(tagNameBox.Text, this._vm.NewGroupTagName, StringComparison.Ordinal))
            {
                tagNameBox.Text = this._vm.NewGroupTagName;
            }
        };

        Grid assignListHost = EmptyStateBanner.CreateHost(assignList, emptyAssignBanner);
        Grid.SetRow(assignListHost, 3);
        rightGrid.Children.Add(assignListHost);

        Border rightCard = SurfaceCard.Create(this._theme, rightGrid, new Thickness(14));
        Grid.SetColumn(rightCard, 0);
        rootGrid.Children.Add(rightCard);

        tabOverlayHost.Children.Add(rootGrid);
        tabOverlayHost.Children.Add(modalBackdrop);

        refreshI18n = () =>
        {
            leftTitleTb.Text = this._i18n["Groups.TagCreatorTitle"];
            leftDescTb.Text = this._i18n["Groups.TagManagerDesc"];
            AppButton.SetText(openCreateModalBtn, this._i18n["Groups.BtnCreateTag"]);
            tagNameBox.PlaceholderText = this._i18n["Groups.TagNamePlaceholder"];
            UpdateEditModeUi();
            AppButton.SetText(cancelEditBtn, this._i18n["Groups.BtnCancelEditTag"]);

            rightTitleTb.Text = this._i18n["Groups.TagAssignPanelTitle"];
            rightHintTb.Text = this._i18n["Groups.TagAssignPanelHint"];
            AppButton.SetText(batchAssignBtn, this._i18n["Groups.BtnBatchAssignAllFiltered"]);
            AppButton.SetText(batchRemoveBtn, this._i18n["Groups.BtnBatchRemoveAllFiltered"]);

            assignTagCategory.Title = this._i18n["Groups.FilterCatColorTag"];
            assignSearchBar.UpdateLocalization(
                this._i18n["Common.FilterButton"],
                this._i18n["Groups.FilterPlaceholder"],
                this._i18n["Common.SearchInList"],
                this._i18n["Common.BtnApplyFilter"]);

            emptyAssignTb.Text = this._i18n["Empty.TargetGroups"];
            rebuildLeftTagList();
            RefreshAssignGroups();
        };

        return tabOverlayHost;
    }
}
