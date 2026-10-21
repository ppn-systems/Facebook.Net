// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Standalone asynchronous remote image loader with persistent disk cache and bounded memory cache
/// for group avatars, author avatars, and post media thumbnails.
/// </summary>
public static class RemoteImageLoader
{
    private static readonly HttpClient s_sharedHttpClient = CreateClient();
    private static readonly ConcurrentDictionary<string, Bitmap> s_bitmapCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> s_inFlightLoads = new(StringComparer.Ordinal);
    private static readonly string s_diskCacheDir = InitializeDiskCacheDir();
    private static readonly SemaphoreSlim s_downloadGate = new(8, 8);

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

            string diskPath = GetDiskCachePath(url);
            if (File.Exists(diskPath))
            {
                try
                {
                    byte[] cachedBytes = await File.ReadAllBytesAsync(diskPath).ConfigureAwait(false);
                    if (cachedBytes.Length > 0 && cachedBytes.Length <= 8 * 1024 * 1024)
                    {
                        using MemoryStream cachedStream = new(cachedBytes);
                        Bitmap cachedBitmap = new(cachedStream);
                        CacheBitmapInMemory(url, cachedBitmap);
                        return cachedBitmap;
                    }
                }
                catch
                {
                    // Fall back to network download if disk cache read or decode fails.
                }
            }

            await s_downloadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (s_bitmapCache.TryGetValue(url, out existing))
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

                try
                {
                    await File.WriteAllBytesAsync(diskPath, bytes).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore best-effort disk write failures.
                }

                CacheBitmapInMemory(url, bitmap);
                return bitmap;
            }
            finally
            {
                _ = s_downloadGate.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    private static void CacheBitmapInMemory(string url, Bitmap bitmap)
    {
        if (s_bitmapCache.Count > 500)
        {
            int removed = 0;
            foreach (string key in s_bitmapCache.Keys)
            {
                if (s_bitmapCache.TryRemove(key, out _))
                {
                    removed++;
                    if (removed >= 80)
                    {
                        break;
                    }
                }
            }
        }

        s_bitmapCache[url] = bitmap;
    }

    private static string GetDiskCachePath(string url)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Path.Combine(s_diskCacheDir, $"{Convert.ToHexStringLower(hash)}.cache");
    }

    private static string InitializeDiskCacheDir()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FacebookOneClient",
            "Data",
            "ImageCache");

        try
        {
            _ = Directory.CreateDirectory(path);
        }
        catch
        {
            // Ignore directory creation failures.
        }

        return path;
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
