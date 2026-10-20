// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone circular avatar badge displaying the 1–2 letter initials of an Account or Group name.
/// </summary>
public static class AvatarBadge
{
    /// <summary>
    /// Creates a circular avatar badge displaying the remote avatar image if available, or 1–2 letter initials of an Account or Group name.
    /// </summary>
    public static Border Create(ThemeService theme, string displayName, double size = 34, string? imageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        string initials = ExtractInitials(displayName);
        TextBlock text = new()
        {
            Text = initials,
            FontSize = Math.Max(11, size * 0.36),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Panel rootPanel = new()
        {
            Width = size,
            Height = size
        };
        rootPanel.Children.Add(text);

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            Image avatarImg = new()
            {
                Width = size,
                Height = size,
                Stretch = Stretch.UniformToFill,
                IsVisible = false
            };
            Border imgClip = new()
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                ClipToBounds = true,
                Child = avatarImg,
                IsVisible = false
            };
            rootPanel.Children.Add(imgClip);
            RemoteImageLoader.Attach(avatarImg, imageUrl, imgClip, text);
        }

        Border badge = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = rootPanel,
            VerticalAlignment = VerticalAlignment.Center
        };

        void ApplyColors()
        {
            ThemePalette p = theme.Palette;
            badge.Background = p.BrandSubtleFill;
            badge.BorderBrush = p.BrandPrimary;
            text.Foreground = p.BrandPrimary;
        }

        ApplyColors();
        ThemeLifecycle.Bind(badge, theme, ApplyColors);
        return badge;
    }

    private static string ExtractInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "FB";
        }

        string[] parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0][..2].ToUpperInvariant()
                : parts[0].ToUpperInvariant();
        }

        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }
}
