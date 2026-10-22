// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Meta.UI.Components.Icons;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Standardized dimmed modal confirmation popup overlay (<c>ConfirmDialogModal.cs</c>)
/// used across all destructive Delete, Remove, Clear, and Cache Cleanup actions.
/// </summary>
public static class ConfirmDialogModal
{
    /// <summary>
    /// Displays a modal confirmation dialog over the window or page containing <paramref name="anchorControl"/>
    /// and invokes <paramref name="onConfirmed"/> only when the user explicitly confirms.
    /// </summary>
    public static void Show(
        Control anchorControl,
        ThemeService theme,
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText,
        Action onConfirmed,
        string? iconSvg = null)
    {
        ArgumentNullException.ThrowIfNull(anchorControl);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(onConfirmed);

        Panel? hostPanel = ResolveOverlayHostPanel(anchorControl);
        if (hostPanel is null)
        {
            onConfirmed();
            return;
        }

        ThemePalette p = theme.Palette;

        Border backdrop = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(165, 8, 12, 22)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 9999
        };

        if (hostPanel is Grid hostGrid)
        {
            if (hostGrid.ColumnDefinitions.Count > 1)
            {
                Grid.SetColumn(backdrop, 0);
                Grid.SetColumnSpan(backdrop, hostGrid.ColumnDefinitions.Count);
            }

            if (hostGrid.RowDefinitions.Count > 1)
            {
                Grid.SetRow(backdrop, 0);
                Grid.SetRowSpan(backdrop, hostGrid.RowDefinitions.Count);
            }
        }

        StackPanel cardStack = new()
        {
            Spacing = 14
        };

        Grid headerRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        Control dangerIcon = VectorIcons.Create(iconSvg ?? VectorIcons.Trash, 20, p.DangerText);
        Border iconBadge = new()
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = p.DangerFill,
            BorderBrush = p.DangerText,
            BorderThickness = new Thickness(1),
            Child = dangerIcon,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock titleTb = SectionHeader.CreateLabel(theme, title, 15.5, FontWeight.Bold);
        titleTb.TextWrapping = TextWrapping.Wrap;
        titleTb.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetColumn(iconBadge, 0);
        Grid.SetColumn(titleTb, 1);
        headerRow.Children.Add(iconBadge);
        headerRow.Children.Add(titleTb);
        cardStack.Children.Add(headerRow);

        TextBlock messageTb = SectionHeader.CreateLabel(theme, message, 13, FontWeight.Normal, isSecondary: true);
        messageTb.TextWrapping = TextWrapping.Wrap;
        messageTb.LineHeight = 19;
        cardStack.Children.Add(messageTb);

        Grid actionsRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };

        Button cancelBtn = AppButton.CreateSecondary(
            theme,
            cancelButtonText,
            null,
            VectorIcons.StopCircle);

        Button confirmBtn = AppButton.CreateDanger(
            theme,
            confirmButtonText,
            null,
            iconSvg ?? VectorIcons.Trash);

        void CloseModal() => hostPanel.Children.Remove(backdrop);

        cancelBtn.Click += (_, _) => CloseModal();
        confirmBtn.Click += (_, _) =>
        {
            CloseModal();
            onConfirmed();
        };

        Grid.SetColumn(cancelBtn, 1);
        Grid.SetColumn(confirmBtn, 2);
        actionsRow.Children.Add(cancelBtn);
        actionsRow.Children.Add(confirmBtn);
        cardStack.Children.Add(actionsRow);

        Border dialogCard = SurfaceCard.Create(theme, cardStack, new Thickness(22, 18));
        dialogCard.Width = 430;
        dialogCard.CornerRadius = new CornerRadius(14);
        dialogCard.BorderBrush = p.InteractiveBorder;
        dialogCard.HorizontalAlignment = HorizontalAlignment.Center;
        dialogCard.VerticalAlignment = VerticalAlignment.Center;
        dialogCard.PointerPressed += (_, e) => e.Handled = true;

        backdrop.PointerPressed += (_, _) => CloseModal();
        backdrop.Child = dialogCard;

        hostPanel.Children.Add(backdrop);
    }

    /// <summary>
    /// Asynchronous overload of <see cref="Show"/> for async deletion/cleanup handlers.
    /// </summary>
    public static void ShowAsync(
        Control anchorControl,
        ThemeService theme,
        string title,
        string message,
        string confirmButtonText,
        string cancelButtonText,
        Func<Task> onConfirmedAsync,
        string? iconSvg = null)
    {
        ArgumentNullException.ThrowIfNull(onConfirmedAsync);
        Show(
            anchorControl,
            theme,
            title,
            message,
            confirmButtonText,
            cancelButtonText,
            () => _ = onConfirmedAsync(),
            iconSvg);
    }

    private static Panel? ResolveOverlayHostPanel(Control anchorControl)
    {
        if (TopLevel.GetTopLevel(anchorControl) is Window window && window.Content is Panel windowRootPanel)
        {
            return windowRootPanel;
        }

        Panel? deepestPanel = null;
        Visual? current = anchorControl;
        while (current is not null)
        {
            if (current is Panel panel)
            {
                deepestPanel = panel;
            }

            current = current.GetVisualParent();
        }

        return deepestPanel;
    }
}
