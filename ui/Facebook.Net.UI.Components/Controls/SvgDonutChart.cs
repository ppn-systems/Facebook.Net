// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.UI.Core.Theming;

namespace Facebook.Net.UI.Components.Controls;

/// <summary>
/// Represents a single colored slice in an <see cref="SvgDonutChart"/>.
/// </summary>
/// <param name="Label">Human-friendly slice label.</param>
/// <param name="Value">Numeric count for this slice.</param>
/// <param name="ColorHex">Hex color string (e.g. <c>#F6821F</c>, <c>#10B981</c>, <c>#3B82F6</c>).</param>
public sealed record SvgDonutSlice(string Label, int Value, string ColorHex);

/// <summary>
/// Multi-colored vector SVG donut chart component (<c>SvgDonutChart.cs</c>) rendered using
/// <see cref="StreamGeometry"/> arc paths, a center total counter + icon badge, and a colorful percentage legend.
/// </summary>
public sealed class SvgDonutChart : Border
{
    private readonly ThemeService _theme;
    private readonly TextBlock _titleTb;
    private readonly TextBlock _totalValueTb;
    private readonly TextBlock _totalCaptionTb;
    private readonly Panel _arcCanvas;
    private readonly StackPanel _legendStack;
    private readonly double _size;
    private readonly double _strokeThickness;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDonutChart"/> class.
    /// </summary>
    public SvgDonutChart(
        ThemeService theme,
        string title,
        string centerIconPath,
        string totalCaption,
        double size = 152,
        double strokeThickness = 22)
    {
        ArgumentNullException.ThrowIfNull(theme);
        this._theme = theme;
        this._size = size;
        this._strokeThickness = strokeThickness;

        this.CornerRadius = new CornerRadius(10);
        this.BorderThickness = new Thickness(1);
        this.Padding = new Thickness(16, 14);

        StackPanel cardStack = new() { Spacing = 12 };

        // Header Row with SVG Icon + Title
        this._titleTb = SectionHeader.CreateLabel(theme, title, 13.5, FontWeight.Bold);
        StackPanel headerRow = SectionHeader.CreateSubHeader(theme, title, centerIconPath, out this._titleTb);
        cardStack.Children.Add(headerRow);

        // Body Grid: Left = Vector SVG Donut Ring, Right = Multi-color Legend with counts & percentages
        Grid bodyGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 16,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid ringContainer = new()
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        this._arcCanvas = new Panel
        {
            Width = size,
            Height = size
        };

        StackPanel centerStack = new()
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        this._totalValueTb = new TextBlock
        {
            Text = "0",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        this._totalCaptionTb = new TextBlock
        {
            Text = totalCaption,
            FontSize = 10.5,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        centerStack.Children.Add(this._totalValueTb);
        centerStack.Children.Add(this._totalCaptionTb);

        ringContainer.Children.Add(this._arcCanvas);
        ringContainer.Children.Add(centerStack);

        this._legendStack = new StackPanel
        {
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(ringContainer, 0);
        Grid.SetColumn(this._legendStack, 1);
        bodyGrid.Children.Add(ringContainer);
        bodyGrid.Children.Add(this._legendStack);

        cardStack.Children.Add(bodyGrid);
        this.Child = cardStack;

        void ApplyColors()
        {
            ThemePalette p = this._theme.Palette;
            this.Background = p.ElevatedSurface;
            this.BorderBrush = p.SubtleBorder;
            this._totalValueTb.Foreground = p.TextPrimary;
            this._totalCaptionTb.Foreground = p.TextMuted;
        }

        ApplyColors();
        ThemeLifecycle.Bind(this, theme, ApplyColors);
    }

    /// <summary>
    /// Updates localized header and total caption labels.
    /// </summary>
    public void UpdateLabels(string title, string totalCaption)
    {
        this._titleTb.Text = title;
        this._totalCaptionTb.Text = totalCaption;
    }

    /// <summary>
    /// Renders the vector donut slices and colorful legend items.
    /// </summary>
    public void SetSlices(IReadOnlyList<SvgDonutSlice> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);

        ThemePalette p = this._theme.Palette;
        this._arcCanvas.Children.Clear();
        this._legendStack.Children.Clear();

        int total = 0;
        for (int i = 0; i < slices.Count; i++)
        {
            if (slices[i].Value > 0)
            {
                total += slices[i].Value;
            }
        }

        this._totalValueTb.Text = total.ToString("N0", CultureInfo.InvariantCulture);

        double cx = this._size / 2.0;
        double cy = this._size / 2.0;
        double radius = ((this._size - this._strokeThickness) / 2.0) - 2.0;

        // Background track ring
        Ellipse trackRing = new()
        {
            Width = radius * 2.0,
            Height = radius * 2.0,
            Stroke = p.SecondarySurface,
            StrokeThickness = this._strokeThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        this._arcCanvas.Children.Add(trackRing);

        if (total > 0)
        {
            int nonZeroCount = 0;
            for (int i = 0; i < slices.Count; i++)
            {
                if (slices[i].Value > 0)
                {
                    nonZeroCount++;
                }
            }

            double startAngle = -90.0;
            double gapDegrees = nonZeroCount > 1 ? 2.4 : 0.0;

            for (int i = 0; i < slices.Count; i++)
            {
                SvgDonutSlice slice = slices[i];
                if (slice.Value <= 0)
                {
                    continue;
                }

                IBrush brush = new SolidColorBrush(Color.Parse(slice.ColorHex));
                double sweep = (double)slice.Value / total * 360.0;

                if (nonZeroCount == 1 || sweep >= 359.5)
                {
                    Ellipse fullRing = new()
                    {
                        Width = radius * 2.0,
                        Height = radius * 2.0,
                        Stroke = brush,
                        StrokeThickness = this._strokeThickness,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    this._arcCanvas.Children.Add(fullRing);
                }
                else
                {
                    double effectiveSweep = Math.Max(3.0, sweep - gapDegrees);
                    Path arcPath = new()
                    {
                        Data = CreateArcGeometry(cx, cy, radius, startAngle, effectiveSweep),
                        Stroke = brush,
                        StrokeThickness = this._strokeThickness,
                        StrokeLineCap = PenLineCap.Round
                    };
                    this._arcCanvas.Children.Add(arcPath);
                }

                startAngle += sweep;
            }
        }

        // Build colorful legend rows
        for (int i = 0; i < slices.Count; i++)
        {
            SvgDonutSlice slice = slices[i];
            IBrush sliceBrush = new SolidColorBrush(Color.Parse(slice.ColorHex));
            int pct = total > 0 ? (int)Math.Round(slice.Value * 100.0 / total) : 0;

            Grid legendRow = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border colorDot = new()
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = sliceBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock labelTb = new()
            {
                Text = slice.Label,
                FontSize = 11.5,
                FontWeight = FontWeight.Medium,
                Foreground = p.TextSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border countBadge = new()
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Background = p.SecondarySurface,
                BorderBrush = p.SubtleBorder,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{slice.Value} · {pct}%",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = p.TextPrimary
                }
            };

            Grid.SetColumn(colorDot, 0);
            Grid.SetColumn(labelTb, 1);
            Grid.SetColumn(countBadge, 2);
            legendRow.Children.Add(colorDot);
            legendRow.Children.Add(labelTb);
            legendRow.Children.Add(countBadge);
            this._legendStack.Children.Add(legendRow);
        }
    }

    private static StreamGeometry CreateArcGeometry(
        double cx,
        double cy,
        double radius,
        double startAngleDegrees,
        double sweepAngleDegrees)
    {
        double startRad = startAngleDegrees * Math.PI / 180.0;
        double endRad = (startAngleDegrees + sweepAngleDegrees) * Math.PI / 180.0;

        Point startPoint = new(
            cx + (radius * Math.Cos(startRad)),
            cy + (radius * Math.Sin(startRad)));
        Point endPoint = new(
            cx + (radius * Math.Cos(endRad)),
            cy + (radius * Math.Sin(endRad)));

        bool isLargeArc = sweepAngleDegrees > 180.0;

        StreamGeometry geom = new();
        using (StreamGeometryContext ctx = geom.Open())
        {
            ctx.BeginFigure(startPoint, isFilled: false);
            ctx.ArcTo(
                endPoint,
                new Size(radius, radius),
                rotationAngle: 0.0,
                isLargeArc: isLargeArc,
                sweepDirection: SweepDirection.Clockwise);
        }

        return geom;
    }
}
