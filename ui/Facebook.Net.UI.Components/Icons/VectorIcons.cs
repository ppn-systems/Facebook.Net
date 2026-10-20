// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Facebook.Net.UI.Components.Icons;

/// <summary>
/// Loads vector icons directly from `.svg` files stored in <c>Assets/Icons/*.svg</c>
/// (via Avalonia <see cref="AssetLoader"/>, disk assets, or embedded resource streams)
/// and caches parsed <see cref="StreamGeometry"/> instances in memory.
/// </summary>
public static class VectorIcons
{
    private static readonly ConcurrentDictionary<string, StreamGeometry> s_geometryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Home / Overview globe icon asset file name.</summary>
    public const string Home = "home.svg";

    /// <summary>Sidebar collapse / expand panel toggle icon asset file name.</summary>
    public const string SidebarToggle = "sidebar-toggle.svg";

    /// <summary>Info circle (`i`) icon asset file name for click-to-view explanation flyouts.</summary>
    public const string InfoCircle = "info-circle.svg";

    /// <summary>Brand flame / bolt logo icon asset file name.</summary>
    public const string BrandMark = "brand-mark.svg";

    /// <summary>Users / Accounts icon asset file name.</summary>
    public const string Accounts = "accounts.svg";

    /// <summary>Community Groups icon asset file name.</summary>
    public const string Groups = "groups.svg";

    /// <summary>Send / Publish Post paper plane icon asset file name.</summary>
    public const string Publish = "publish.svg";

    /// <summary>Message / Sales Comment icon asset file name.</summary>
    public const string SalesComment = "sales-comment.svg";

    /// <summary>Settings / Gear icon asset file name.</summary>
    public const string Settings = "settings.svg";

    /// <summary>Browser / Window icon asset file name.</summary>
    public const string Browser = "browser.svg";

    /// <summary>Terminal / Audit Log icon asset file name.</summary>
    public const string AuditLog = "audit-log.svg";

    /// <summary>Sun / Light Theme icon asset file name.</summary>
    public const string Sun = "sun.svg";

    /// <summary>Moon / Dark Theme icon asset file name.</summary>
    public const string Moon = "moon.svg";

    /// <summary>Search / Magnifier icon asset file name.</summary>
    public const string Search = "search.svg";

    /// <summary>Sync / Refresh arrows icon asset file name.</summary>
    public const string Sync = "sync.svg";

    /// <summary>CheckCircle / Verified icon asset file name.</summary>
    public const string CheckCircle = "check-circle.svg";

    /// <summary>Folder / File Picker icon asset file name.</summary>
    public const string Folder = "folder.svg";

    /// <summary>StopCircle / Cancel task icon asset file name.</summary>
    public const string StopCircle = "stop-circle.svg";

    /// <summary>
    /// Creates a crisp <see cref="PathIcon"/> loaded from an `.svg` file in <c>Assets/Icons/</c>.
    /// </summary>
    public static PathIcon Create(
        string iconAssetOrPath,
        double size = 16,
        IBrush? foreground = null,
        Thickness? margin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconAssetOrPath);
        StreamGeometry geometry = s_geometryCache.GetOrAdd(iconAssetOrPath, LoadGeometryFromSvgAsset);

        PathIcon icon = new()
        {
            Data = geometry,
            Width = size,
            Height = size,
            Margin = margin ?? new Thickness(0)
        };

        if (foreground is not null)
        {
            icon.Foreground = foreground;
        }

        return icon;
    }

    private static StreamGeometry LoadGeometryFromSvgAsset(string iconAssetOrPath)
    {
        // Support loading from Assets/Icons/<name>.svg
        if (iconAssetOrPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(iconAssetOrPath);
            string? pathData = TryExtractSvgPathData(fileName);
            if (!string.IsNullOrWhiteSpace(pathData))
            {
                return StreamGeometry.Parse(pathData);
            }
        }

        return StreamGeometry.Parse(iconAssetOrPath);
    }

    private static string? TryExtractSvgPathData(string svgFileName)
    {
        // 1. Try disk file in AppContext.BaseDirectory/Assets/Icons/<svgFileName> (allows live editing of .svg files)
        string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", svgFileName);
        if (File.Exists(diskPath))
        {
            using FileStream fs = File.OpenRead(diskPath);
            string? parsed = ExtractPathAttributeFromXmlStream(fs);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        // 2. Try Avalonia AssetLoader (avares://Facebook.Net.UI.Components/Assets/Icons/<svgFileName>)
        try
        {
            Uri assetUri = new($"avares://Facebook.Net.UI.Components/Assets/Icons/{svgFileName}");
            if (AssetLoader.Exists(assetUri))
            {
                using Stream stream = AssetLoader.Open(assetUri);
                string? parsed = ExtractPathAttributeFromXmlStream(stream);
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // AssetLoader may not be initialized in headless unit tests; fall back to EmbeddedResource
        }

        // 3. Try assembly EmbeddedResource stream (Assets.Icons.<svgFileName>)
        Assembly asm = typeof(VectorIcons).Assembly;
        using Stream? resStream = asm.GetManifestResourceStream($"Assets.Icons.{svgFileName}");
        if (resStream is not null)
        {
            return ExtractPathAttributeFromXmlStream(resStream);
        }

        return null;
    }

    private static string? ExtractPathAttributeFromXmlStream(Stream xmlStream)
    {
        XDocument doc = XDocument.Load(xmlStream);
        if (doc.Root is null)
        {
            return null;
        }

        System.Text.StringBuilder combined = new();
        foreach (XElement el in doc.Root.Descendants())
        {
            if (string.Equals(el.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
            {
                string? d = el.Attribute("d")?.Value;
                if (!string.IsNullOrWhiteSpace(d))
                {
                    if (combined.Length > 0)
                    {
                        _ = combined.Append(' ');
                    }

                    _ = combined.Append(d.Trim());
                }
            }
        }

        return combined.Length > 0 ? combined.ToString() : null;
    }
}
