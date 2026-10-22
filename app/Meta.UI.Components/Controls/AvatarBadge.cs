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
            int targetDecodeWidth = Math.Max(64, (int)Math.Ceiling(size * 2.5));
            RemoteImageLoader.Attach(avatarImg, imageUrl, imgClip, text, decodeWidth: targetDecodeWidth);
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

        string[] rawParts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        char firstChar = '\0';
        char lastChar = '\0';
        string? singleValidWord = null;

        for (int i = 0; i < rawParts.Length; i++)
        {
            string word = rawParts[i];
            char ch = FirstValidLetterOrDigit(word);
            if (ch == '\0')
            {
                continue;
            }

            if (firstChar == '\0')
            {
                firstChar = ch;
                singleValidWord = word;
            }
            else
            {
                lastChar = ch;
            }
        }

        if (firstChar == '\0')
        {
            return "FB";
        }

        if (lastChar == '\0' && singleValidWord is not null)
        {
            char second = SecondValidLetterOrDigit(singleValidWord);
            return second != '\0'
                ? $"{char.ToUpperInvariant(firstChar)}{char.ToUpperInvariant(second)}"
                : char.ToUpperInvariant(firstChar).ToString();
        }

        return $"{char.ToUpperInvariant(firstChar)}{char.ToUpperInvariant(lastChar)}";
    }

    private static char FirstValidLetterOrDigit(string word)
    {
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (char.IsSurrogate(c))
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                return c;
            }
        }

        return '\0';
    }

    private static char SecondValidLetterOrDigit(string word)
    {
        bool foundFirst = false;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (char.IsSurrogate(c))
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (!foundFirst)
                {
                    foundFirst = true;
                }
                else
                {
                    return c;
                }
            }
        }

        return '\0';
    }
}
