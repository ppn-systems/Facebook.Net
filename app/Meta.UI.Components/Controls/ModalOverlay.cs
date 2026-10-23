// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable dimmed modal overlay container (<c>ModalOverlay.cs</c>) that wraps a custom form or dialog card,
/// blocks click-through inside the card, and invokes a close callback when clicking the backdrop.
/// </summary>
public static class ModalOverlay
{
    /// <summary>
    /// Creates a hidden-by-default dimmed modal backdrop containing a centered <see cref="SurfaceCard"/>.
    /// </summary>
    public static Border Create(
        ThemeService theme,
        Control dialogContent,
        Action onBackdropPressed,
        double cardWidth = 420)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(dialogContent);
        ArgumentNullException.ThrowIfNull(onBackdropPressed);

        Border modalBackdrop = new()
        {
            IsVisible = false,
            Background = ThemeLifecycle.GetCachedBrush(Color.FromArgb(152, 8, 12, 22)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Border modalCard = SurfaceCard.Create(theme, dialogContent, new Thickness(22, 18));
        modalCard.Width = cardWidth;
        modalCard.CornerRadius = new CornerRadius(14);
        modalCard.HorizontalAlignment = HorizontalAlignment.Center;
        modalCard.VerticalAlignment = VerticalAlignment.Center;
        modalCard.PointerPressed += (_, e) => e.Handled = true;

        modalBackdrop.PointerPressed += (_, _) => onBackdropPressed();
        modalBackdrop.Child = modalCard;

        return modalBackdrop;
    }
}
