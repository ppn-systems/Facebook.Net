// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using static System.FormattableString;

namespace Facebook.Net.UI.Components.Icons;

/// <summary>
/// Loads vector icons from <c>.svg</c> files in <c>Assets/Icons/*.svg</c>
/// (via Avalonia <see cref="AssetLoader"/>, disk assets, or embedded resource streams)
/// and caches parsed <see cref="Geometry"/> instances in memory.
/// Supports both fill-based (solid) and stroke-based (outline) SVG icons, with
/// full support for <c>&lt;path&gt;</c>, <c>&lt;line&gt;</c>, <c>&lt;circle&gt;</c>,
/// <c>&lt;ellipse&gt;</c>, <c>&lt;rect&gt;</c>, <c>&lt;polyline&gt;</c>,
/// and <c>&lt;polygon&gt;</c> elements.
/// </summary>
public static class VectorIcons
{
    private static readonly ConcurrentDictionary<string, SvgIconData> s_iconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Home / Overview house icon asset file name.</summary>
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

    /// <summary>Trash / Delete / Clear icon asset file name.</summary>
    public const string Trash = "trash.svg";

    /// <summary>Plus / Add item icon asset file name.</summary>
    public const string Plus = "plus.svg";

    /// <summary>Clipboard / Paste icon asset file name.</summary>
    public const string Clipboard = "clipboard.svg";

    /// <summary>Shield / Proxy / Security icon asset file name.</summary>
    public const string Shield = "shield.svg";

    /// <summary>Bookmark / Save Template icon asset file name.</summary>
    public const string Bookmark = "bookmark.svg";

    /// <summary>Download / Export icon asset file name.</summary>
    public const string Download = "download.svg";

    /// <summary>Warning triangle icon asset file name.</summary>
    public const string Warning = "warning.svg";

    /// <summary>Error circle icon asset file name.</summary>
    public const string ErrorCircle = "error-circle.svg";

    /// <summary>Thumb-up / Like reaction icon asset file name.</summary>
    public const string ThumbUp = "thumb-up.svg";

    /// <summary>Filter funnel icon asset file name for the Search &amp; Filter bar.</summary>
    public const string Filter = "filter.svg";

    /// <summary>Pencil / Edit item icon asset file name.</summary>
    public const string Edit = "edit.svg";

    /// <summary>External link / Open in browser icon asset file name.</summary>
    public const string ExternalLink = "external-link.svg";

    // ───────────────────────────── Create ─────────────────────────────

    /// <summary>
    /// Creates a vector icon control loaded from an <c>.svg</c> file in <c>Assets/Icons/</c>.
    /// Returns a <see cref="PathIcon"/> for fill-based SVGs or a <see cref="SvgStrokeIcon"/>
    /// for stroke/outline-based SVGs.
    /// </summary>
    public static Control Create(
        string svgAssetFileName,
        double size = 16,
        IBrush? foreground = null,
        Thickness? margin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgAssetFileName);
        SvgIconData data = s_iconCache.GetOrAdd(svgAssetFileName, LoadSvgIconData);

        if (data.RenderMode == SvgRenderMode.Stroke)
        {
            SvgStrokeIcon strokeIcon = new(data.Geometry, data.ViewBox, data.StrokeWidth, data.LineCap, data.LineJoin)
            {
                Width = size,
                Height = size,
                Margin = margin ?? new Thickness(0),
                ClipToBounds = true,
            };

            if (foreground is not null)
            {
                strokeIcon.Foreground = foreground;
            }

            return strokeIcon;
        }

        PathIcon icon = new()
        {
            Data = data.Geometry,
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

    /// <summary>
    /// Sets the foreground brush on an icon control created by <see cref="Create"/>.
    /// Works for both <see cref="PathIcon"/> (fill-based) and <see cref="SvgStrokeIcon"/> (stroke-based) icons.
    /// </summary>
    public static void SetForeground(Control iconControl, IBrush foreground)
    {
        ArgumentNullException.ThrowIfNull(iconControl);
        ArgumentNullException.ThrowIfNull(foreground);

        if (iconControl is PathIcon pathIcon)
        {
            pathIcon.Foreground = foreground;
        }
        else if (iconControl is SvgStrokeIcon strokeIcon)
        {
            strokeIcon.Foreground = foreground;
        }
    }

    // ───────────────────────── SvgStrokeIcon ──────────────────────────

    /// <summary>
    /// A lightweight control that renders SVG geometry using stroke (outline) instead of fill.
    /// Inherits foreground color from the parent visual tree for automatic theme adaptation.
    /// </summary>
    public sealed class SvgStrokeIcon : Control
    {
        /// <summary>
        /// Defines the <see cref="Foreground"/> property.
        /// Inherits from parent <see cref="TemplatedControl.ForegroundProperty"/>.
        /// </summary>
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TemplatedControl.ForegroundProperty.AddOwner<SvgStrokeIcon>();

        private readonly Geometry _geometry;
        private readonly Rect _viewBox;
        private readonly double _strokeWidth;
        private readonly PenLineCap _lineCap;
        private readonly PenLineJoin _lineJoin;

        static SvgStrokeIcon() => AffectsRender<SvgStrokeIcon>(ForegroundProperty);

        internal SvgStrokeIcon(Geometry geometry, Rect viewBox, double strokeWidth, PenLineCap lineCap, PenLineJoin lineJoin)
        {
            this._geometry = geometry;
            this._viewBox = viewBox;
            this._strokeWidth = strokeWidth;
            this._lineCap = lineCap;
            this._lineJoin = lineJoin;
        }

        /// <summary>Gets or sets the brush used to stroke the icon outline.</summary>
        public IBrush? Foreground
        {
            get => this.GetValue(ForegroundProperty);
            set => this.SetValue(ForegroundProperty, value);
        }

        /// <inheritdoc/>
        public override void Render(DrawingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (this._viewBox.Width <= 0 || this._viewBox.Height <= 0 || this.Bounds.Width <= 0 || this.Bounds.Height <= 0)
            {
                return;
            }

            double scaleX = this.Bounds.Width / this._viewBox.Width;
            double scaleY = this.Bounds.Height / this._viewBox.Height;
            double scale = Math.Min(scaleX, scaleY);

            double offsetX = (this.Bounds.Width - (this._viewBox.Width * scale)) / 2;
            double offsetY = (this.Bounds.Height - (this._viewBox.Height * scale)) / 2;

            IBrush brush = this.Foreground ?? Brushes.White;
            Pen pen = new(brush, _strokeWidth, null, _lineCap, _lineJoin);

            Matrix transform =
                Matrix.CreateTranslation(-_viewBox.X, -_viewBox.Y) *
                Matrix.CreateScale(scale, scale) *
                Matrix.CreateTranslation(offsetX, offsetY);

            using (context.PushTransform(transform))
            {
                context.DrawGeometry(null, pen, _geometry);
            }
        }
    }

    // ────────────────────── Internal Data Types ───────────────────────

    private enum SvgRenderMode { Fill, Stroke }

    private sealed record SvgIconData(
        Geometry Geometry,
        SvgRenderMode RenderMode,
        Rect ViewBox,
        double StrokeWidth = 0,
        PenLineCap LineCap = PenLineCap.Round,
        PenLineJoin LineJoin = PenLineJoin.Round);

    // ──────────────────────── Asset Loading ───────────────────────────

    private static SvgIconData LoadSvgIconData(string svgAssetFileName)
    {
        string fileName = svgAssetFileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(svgAssetFileName)
            : $"{svgAssetFileName}.svg";

        SvgIconData data = TryLoadFromDisk(fileName)
                         ?? TryLoadFromAssetLoader(fileName)
                         ?? TryLoadFromEmbeddedResource(fileName)
                         ?? throw new FileNotFoundException($"SVG icon asset '{fileName}' was not found in Assets/Icons/.", fileName);

        return data;
    }

    private static SvgIconData? TryLoadFromDisk(string svgFileName)
    {
        // 1. Try disk file in AppContext.BaseDirectory/Assets/Icons/<svgFileName>
        string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", svgFileName);
        if (!File.Exists(diskPath))
        {
            return null;
        }

        using FileStream fs = File.OpenRead(diskPath);
        return ParseSvgStream(fs);
    }

    private static SvgIconData? TryLoadFromAssetLoader(string svgFileName)
    {
        // 2. Try Avalonia AssetLoader (avares://Facebook.Net.UI.Components/Assets/Icons/<svgFileName>)
        try
        {
            Uri assetUri = new($"avares://Facebook.Net.UI.Components/Assets/Icons/{svgFileName}");
            if (!AssetLoader.Exists(assetUri))
            {
                return null;
            }

            using Stream stream = AssetLoader.Open(assetUri);
            return ParseSvgStream(stream);
        }
        catch
        {
            // AssetLoader may not be initialized in headless unit tests; fall back to EmbeddedResource
            return null;
        }
    }

    private static SvgIconData? TryLoadFromEmbeddedResource(string svgFileName)
    {
        // 3. Try assembly EmbeddedResource stream (Assets.Icons.<svgFileName>)
        Assembly asm = typeof(VectorIcons).Assembly;
        using Stream? resStream = asm.GetManifestResourceStream($"Assets.Icons.{svgFileName}");
        return resStream is not null ? ParseSvgStream(resStream) : null;
    }

    // ─────────────────────── SVG Parsing ─────────────────────────────

    private static SvgIconData? ParseSvgStream(Stream xmlStream)
    {
        XDocument doc = XDocument.Load(xmlStream);
        if (doc.Root is null)
        {
            return null;
        }

        // ── ViewBox ──
        Rect viewBox = new(0, 0, 24, 24);
        string? viewBoxStr = doc.Root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(viewBoxStr))
        {
            string[] parts = viewBoxStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double vx)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double vy)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double vw)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double vh))
            {
                viewBox = new Rect(vx, vy, vw, vh);
            }
        }

        // ── Render mode detection ──
        string? rootFill = doc.Root.Attribute("fill")?.Value;
        string? rootStroke = doc.Root.Attribute("stroke")?.Value;
        bool isStroke = string.Equals(rootFill, "none", StringComparison.OrdinalIgnoreCase)
                        && rootStroke is not null;

        double strokeWidth = 1.5;
        PenLineCap lineCap = PenLineCap.Round;
        PenLineJoin lineJoin = PenLineJoin.Round;

        if (isStroke)
        {
            if (double.TryParse(doc.Root.Attribute("stroke-width")?.Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double sw))
            {
                strokeWidth = sw;
            }

            lineCap = doc.Root.Attribute("stroke-linecap")?.Value switch
            {
                "square" => PenLineCap.Square,
                "butt" => PenLineCap.Flat,
                _ => PenLineCap.Round,
            };
            lineJoin = doc.Root.Attribute("stroke-linejoin")?.Value switch
            {
                "miter" => PenLineJoin.Miter,
                "bevel" => PenLineJoin.Bevel,
                _ => PenLineJoin.Round,
            };
        }

        // ── Extract geometry from all supported SVG elements ──
        StringBuilder combined = new();
        foreach (XElement el in doc.Root.Descendants())
        {
            string? pathData = el.Name.LocalName.ToLowerInvariant() switch
            {
                "path" => el.Attribute("d")?.Value?.Trim(),
                "line" => PathDataFromLine(el),
                "circle" => PathDataFromCircle(el),
                "ellipse" => PathDataFromEllipse(el),
                "rect" => PathDataFromRect(el),
                "polyline" => PathDataFromPolyline(el, close: false),
                "polygon" => PathDataFromPolyline(el, close: true),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(pathData))
            {
                if (combined.Length > 0)
                {
                    _ = combined.Append(' ');
                }

                _ = combined.Append(pathData);
            }
        }

        if (combined.Length == 0)
        {
            return null;
        }

        Geometry geometry = StreamGeometry.Parse(combined.ToString());

        return new SvgIconData(
            geometry,
            isStroke ? SvgRenderMode.Stroke : SvgRenderMode.Fill,
            viewBox,
            strokeWidth,
            lineCap,
            lineJoin);
    }

    // ──────────── SVG Element → Path Data Converters ─────────────────

    private static string? PathDataFromLine(XElement el)
    {
        string? x1 = el.Attribute("x1")?.Value;
        string? y1 = el.Attribute("y1")?.Value;
        string? x2 = el.Attribute("x2")?.Value;
        string? y2 = el.Attribute("y2")?.Value;
        if (x1 is null || y1 is null || x2 is null || y2 is null)
        {
            return null;
        }

        return $"M{x1} {y1}L{x2} {y2}";
    }

    private static string? PathDataFromCircle(XElement el)
    {
        if (!TryParseAttr(el, "cx", out double cx))
        {
            return null;
        }

        if (!TryParseAttr(el, "cy", out double cy))
        {
            return null;
        }

        if (!TryParseAttr(el, "r", out double r) || r <= 0)
        {
            return null;
        }

        return Invariant($"M{cx - r} {cy}A{r} {r} 0 1 0 {cx + r} {cy}A{r} {r} 0 1 0 {cx - r} {cy}Z");
    }

    private static string? PathDataFromEllipse(XElement el)
    {
        if (!TryParseAttr(el, "cx", out double cx))
        {
            return null;
        }

        if (!TryParseAttr(el, "cy", out double cy))
        {
            return null;
        }

        if (!TryParseAttr(el, "rx", out double rx) || rx <= 0)
        {
            return null;
        }

        if (!TryParseAttr(el, "ry", out double ry) || ry <= 0)
        {
            return null;
        }

        return Invariant($"M{cx - rx} {cy}A{rx} {ry} 0 1 0 {cx + rx} {cy}A{rx} {ry} 0 1 0 {cx - rx} {cy}Z");
    }

    private static string? PathDataFromRect(XElement el)
    {
        if (!TryParseAttr(el, "width", out double w) || w <= 0)
        {
            return null;
        }

        if (!TryParseAttr(el, "height", out double h) || h <= 0)
        {
            return null;
        }

        _ = TryParseAttr(el, "x", out double x);
        _ = TryParseAttr(el, "y", out double y);
        _ = TryParseAttr(el, "rx", out double rx);
        _ = TryParseAttr(el, "ry", out double ry);

        if (rx <= 0 && ry <= 0)
        {
            return Invariant($"M{x} {y}L{x + w} {y}L{x + w} {y + h}L{x} {y + h}Z");
        }

        if (rx <= 0)
        {
            rx = ry;
        }

        if (ry <= 0)
        {
            ry = rx;
        }

        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);

        return Invariant($"M{x + rx} {y}L{x + w - rx} {y}A{rx} {ry} 0 0 1 {x + w} {y + ry}L{x + w} {y + h - ry}A{rx} {ry} 0 0 1 {x + w - rx} {y + h}L{x + rx} {y + h}A{rx} {ry} 0 0 1 {x} {y + h - ry}L{x} {y + ry}A{rx} {ry} 0 0 1 {x + rx} {y}Z");
    }

    private static string? PathDataFromPolyline(XElement el, bool close)
    {
        string? points = el.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(points))
        {
            return null;
        }

        string[] coords = points.Trim().Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (coords.Length < 4 || coords.Length % 2 != 0)
        {
            return null;
        }

        StringBuilder sb = new();
        _ = sb.Append(CultureInfo.InvariantCulture, $"M{coords[0]} {coords[1]}");
        for (int i = 2; i < coords.Length; i += 2)
        {
            _ = sb.Append(CultureInfo.InvariantCulture, $"L{coords[i]} {coords[i + 1]}");
        }

        if (close)
        {
            _ = sb.Append('Z');
        }

        return sb.ToString();
    }

    private static bool TryParseAttr(XElement el, string attrName, out double value)
    {
        string? raw = el.Attribute(attrName)?.Value;
        if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
