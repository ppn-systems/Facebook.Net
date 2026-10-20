// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone asynchronous remote image loader and memory cache for group avatars, author avatars, and post media thumbnails.
/// </summary>
public static class RemoteImageLoader
{
    private static readonly HttpClient s_sharedHttpClient = CreateClient();
    private static readonly ConcurrentDictionary<string, Bitmap> s_bitmapCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> s_inFlightLoads = new(StringComparer.Ordinal);

    /// <summary>
    /// Asynchronously loads an image from <paramref name="imageUrl"/> into <paramref name="targetImage"/>.
    /// Optionally toggles visibility of <paramref name="containerToShow"/> and hides <paramref name="fallbackToHide"/> upon success.
    /// </summary>
    public static void Attach(
        Image targetImage,
        string? imageUrl,
        Control? containerToShow = null,
        Control? fallbackToHide = null)
    {
        ArgumentNullException.ThrowIfNull(targetImage);

        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            containerToShow?.IsVisible = false;
            fallbackToHide?.IsVisible = true;
            return;
        }

        if (s_bitmapCache.TryGetValue(imageUrl, out Bitmap? cachedBitmap))
        {
            targetImage.Source = cachedBitmap;
            targetImage.IsVisible = true;
            containerToShow?.IsVisible = true;
            fallbackToHide?.IsVisible = false;
            return;
        }

        _ = LoadAndApplyAsync(targetImage, imageUrl, containerToShow, fallbackToHide);
    }

    private static async Task LoadAndApplyAsync(
        Image targetImage,
        string imageUrl,
        Control? containerToShow,
        Control? fallbackToHide)
    {
        try
        {
            Task<Bitmap?> loadTask = s_inFlightLoads.GetOrAdd(imageUrl, DownloadBitmapAsync);
            Bitmap? bitmap = await loadTask.ConfigureAwait(false);
            _ = s_inFlightLoads.TryRemove(imageUrl, out _);

            if (bitmap is null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                targetImage.Source = bitmap;
                targetImage.IsVisible = true;
                containerToShow?.IsVisible = true;
                fallbackToHide?.IsVisible = false;
            });
        }
        catch
        {
            _ = s_inFlightLoads.TryRemove(imageUrl, out _);
        }
    }

    private static async Task<Bitmap?> DownloadBitmapAsync(string url)
    {
        try
        {
            if (s_bitmapCache.TryGetValue(url, out Bitmap? existing))
            {
                return existing;
            }

            byte[] bytes = await s_sharedHttpClient.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
            {
                return null;
            }

            using MemoryStream ms = new(bytes);
            Bitmap bitmap = new(ms);

            // Keep memory cache bounded
            if (s_bitmapCache.Count > 350)
            {
                s_bitmapCache.Clear();
            }

            s_bitmapCache[url] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _ = client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
        return client;
    }
}
