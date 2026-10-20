// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Facebook.Net.UI.Components.Icons;

/// <summary>
/// Provides crisp 24x24 Lucide/Tabler SVG path geometries and helper methods to create vector icons
/// for the application UI without requiring external font files.
/// Caches parsed <see cref="StreamGeometry"/> instances in memory to eliminate repeated SVG parsing overhead.
/// </summary>
public static class VectorIcons
{
    private static readonly ConcurrentDictionary<string, StreamGeometry> s_geometryCache = new(StringComparer.Ordinal);

    /// <summary>Home / Overview globe icon path.</summary>
    public const string Home = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z";

    /// <summary>Sidebar collapse / expand panel toggle icon path (`[|]`).</summary>
    public const string SidebarToggle = "M3 5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5z M9 3v18";

    /// <summary>Info circle (`i`) icon path for click-to-view explanation flyouts.</summary>
    public const string InfoCircle = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z";

    /// <summary>Brand flame / shield logo icon path.</summary>
    public const string BrandMark = "M13 2L3 14h9l-1 8 10-12h-9l1-8z";

    /// <summary>Users / Accounts icon path.</summary>
    public const string Accounts = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75";

    /// <summary>Community Groups / Globe grid icon path.</summary>
    public const string Groups = "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 7a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M23 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75";

    /// <summary>Send / Publish Post paper plane icon path.</summary>
    public const string Publish = "M22 2L11 13 M22 2l-7 20-4-9-9-4 20-7z";

    /// <summary>Message / Sales Comment icon path.</summary>
    public const string SalesComment = "M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z";

    /// <summary>Settings / Gear icon path.</summary>
    public const string Settings = "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1Z";

    /// <summary>Browser / Globe window icon path.</summary>
    public const string Browser = "M3 5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5z M3 9h18 M7 6h.01 M10 6h.01";

    /// <summary>Terminal / Audit Log icon path.</summary>
    public const string AuditLog = "M4 17l6-6-6-6 M12 19h8";

    /// <summary>Sun / Light Theme icon path.</summary>
    public const string Sun = "M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z M12 1v2 M12 21v2 M4.22 4.22l1.42 1.42 M18.36 18.36l1.42 1.42 M1 12h2 M21 12h2 M4.22 19.78l1.42-1.42 M18.36 5.64l1.42-1.42";

    /// <summary>Moon / Dark Theme icon path.</summary>
    public const string Moon = "M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z";

    /// <summary>Search / Magnifier icon path.</summary>
    public const string Search = "M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z M21 21l-4.35-4.35";

    /// <summary>Sync / Refresh arrows icon path.</summary>
    public const string Sync = "M23 4v6h-6 M1 20v-6h6 M3.51 9a9 9 0 0 1 14.85-3.36L23 10 M1 14l4.64 4.36A9 9 0 0 0 20.49 15";

    /// <summary>CheckCircle / Verified icon path.</summary>
    public const string CheckCircle = "M22 11.08V12a10 10 0 1 1-5.93-9.14 M22 4L12 14.01l-3-3";

    /// <summary>Folder / File Picker icon path.</summary>
    public const string Folder = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z";

    /// <summary>StopCircle / Cancel task icon path.</summary>
    public const string StopCircle = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M9 9h6v6H9z";

    /// <summary>Creates a crisp <see cref="PathIcon"/> from a cached SVG path geometry.</summary>
    public static PathIcon Create(
        string svgPathData,
        double size = 16,
        IBrush? foreground = null,
        Thickness? margin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgPathData);
        StreamGeometry geometry = s_geometryCache.GetOrAdd(svgPathData, static path => StreamGeometry.Parse(path));

        PathIcon icon = new()
        {
            Data = geometry,
            Width = size,
            Height = size,
            Margin = margin ?? new Thickness(0, 0, 6, 0)
        };

        if (foreground is not null)
        {
            icon.Foreground = foreground;
        }

        return icon;
    }
}
