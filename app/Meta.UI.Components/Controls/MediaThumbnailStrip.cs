// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Meta.UI.Core.Audit;
using Meta.UI.Core.Localization;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Reusable attached-media thumbnail strip component (<c>MediaThumbnailStrip.cs</c>).
/// Decodes local image files at thumbnail resolution (<c>DecodeToWidth(..., 128)</c>) to avoid full-resolution RAM spikes
/// and automatically disposes bitmaps on refresh or detach.
/// </summary>
public static class MediaThumbnailStrip
{
    /// <summary>
    /// Creates a themed thumbnail strip container with an attached file summary and a clear button (<c>✕</c>).
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Unreadable local image files should be ignored safely.")]
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "UI close symbol only.")]
    public static Border Create(
        ThemeService theme,
        LocalizationService i18n,
        Action onClearClicked,
        out Action<IReadOnlyList<string>> refreshFiles)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(onClearClicked);

        Border container = new()
        {
            IsVisible = false,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };

        StackPanel thumbnailStrip = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock attachedFileNameTb = SectionHeader.CreateLabel(theme, string.Empty, 12, FontWeight.SemiBold);
        attachedFileNameTb.TextTrimming = TextTrimming.CharacterEllipsis;
        attachedFileNameTb.MaxLines = 1;
        attachedFileNameTb.VerticalAlignment = VerticalAlignment.Center;

        Button clearPhotoBtn = AppButton.CreateSecondary(
            theme,
            "✕",
            new CommunityToolkit.Mvvm.Input.RelayCommand(onClearClicked));

        Grid previewInnerGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(thumbnailStrip, 0);
        Grid.SetColumn(attachedFileNameTb, 1);
        Grid.SetColumn(clearPhotoBtn, 2);
        previewInnerGrid.Children.Add(thumbnailStrip);
        previewInnerGrid.Children.Add(attachedFileNameTb);
        previewInnerGrid.Children.Add(clearPhotoBtn);
        container.Child = previewInnerGrid;

        void ApplyColors()
        {
            container.BorderBrush = theme.Palette.SubtleBorder;
            container.Background = theme.Palette.SecondarySurface;
        }

        ApplyColors();
        ThemeLifecycle.Bind(container, theme, ApplyColors);

        container.DetachedFromVisualTree += (_, _) => DisposeStripBitmaps(thumbnailStrip);

        refreshFiles = files =>
        {
            DisposeStripBitmaps(thumbnailStrip);
            if (files is { Count: > 0 })
            {
                int maxThumbs = Math.Min(files.Count, 5);
                int validBitmaps = 0;
                for (int i = 0; i < maxThumbs; i++)
                {
                    Bitmap? thumbBmp = TryDecodeThumbnail(files[i], 128);
                    if (thumbBmp is null)
                    {
                        continue;
                    }

                    Image thumbImg = new()
                    {
                        Width = 64,
                        Height = 48,
                        Stretch = Stretch.UniformToFill,
                        Source = thumbBmp
                    };
                    Border thumbClip = new()
                    {
                        CornerRadius = new CornerRadius(6),
                        ClipToBounds = true,
                        Child = thumbImg
                    };
                    thumbnailStrip.Children.Add(thumbClip);
                    validBitmaps++;
                }

                if (files.Count > maxThumbs)
                {
                    Border overflowBadge = StatusBadge.Create(
                        theme,
                        i18n.Format("Publisher.PhotosOverflowBadge", files.Count - maxThumbs),
                        AuditSeverity.Info);
                    overflowBadge.VerticalAlignment = VerticalAlignment.Center;
                    thumbnailStrip.Children.Add(overflowBadge);
                }

                if (validBitmaps > 0)
                {
                    if (files.Count == 1)
                    {
                        attachedFileNameTb.Text = Path.GetFileName(files[0]);
                    }
                    else
                    {
                        List<string> fileNames = new(files.Count);
                        for (int i = 0; i < files.Count; i++)
                        {
                            fileNames.Add(Path.GetFileName(files[i]));
                        }

                        attachedFileNameTb.Text = $"{i18n.Format("Publisher.PhotosAttachedCount", files.Count)}: {string.Join(", ", fileNames)}";
                    }

                    container.IsVisible = true;
                    return;
                }
            }

            container.IsVisible = false;
        };

        return container;
    }

    /// <summary>
    /// Decodes a local image file scaled down to <paramref name="decodeWidth"/> pixels.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Unreadable local image files should return null.")]
    public static Bitmap? TryDecodeThumbnail(string filePath, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Bitmap.DecodeToWidth(fs, decodeWidth, BitmapInterpolationMode.MediumQuality);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Disposes all <see cref="Bitmap"/> instances held by child <see cref="Image"/> controls inside <paramref name="strip"/>.
    /// </summary>
    public static void DisposeStripBitmaps(Panel strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        for (int i = 0; i < strip.Children.Count; i++)
        {
            if (strip.Children[i] is Border { Child: Image img } && img.Source is IDisposable disp)
            {
                img.Source = null;
                disp.Dispose();
            }
        }

        strip.Children.Clear();
    }
}
