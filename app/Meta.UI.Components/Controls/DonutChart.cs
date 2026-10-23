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
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Represents a single colored slice in an <see cref="DonutChart"/>.
/// </summary>
/// <param name="Label">Human-friendly slice label.</param>
/// <param name="Value">Numeric count for this slice.</param>
/// <param name="ColorHex">Hex color string (e.g. <c>#F6821F</c>, <c>#10B981</c>, <c>#3B82F6</c>).</param>
public sealed record DonutChartSlice(string Label, int Value, string ColorHex);

/// <summary>
/// Multi-colored vector SVG donut chart component (<c>DonutChart.cs</c>) rendered using
/// <see cref="StreamGeometry"/> arc paths, a center total counter + icon badge, and a colorful percentage legend.
/// </summary>
public sealed class DonutChart : Border
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
    /// Initializes a new instance of the <see cref="DonutChart"/> class.
    /// </summary>
    public DonutChart(
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

        // Body Stack: Top = Centered Vector SVG Donut Ring, Bottom = Full-width Multi-color Legend with progress bars
        StackPanel bodyStack = new()
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Grid ringContainer = new()
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 4, 0, 2),
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
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        this._totalCaptionTb = new TextBlock
        {
            Text = totalCaption,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        centerStack.Children.Add(this._totalValueTb);
        centerStack.Children.Add(this._totalCaptionTb);

        ringContainer.Children.Add(this._arcCanvas);
        ringContainer.Children.Add(centerStack);

        this._legendStack = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        bodyStack.Children.Add(ringContainer);
        bodyStack.Children.Add(this._legendStack);

        cardStack.Children.Add(bodyStack);
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
    public void SetSlices(
        IReadOnlyList<DonutChartSlice> slices,
        string? customTotalText = null,
        Func<int, string>? valueFormatter = null)
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

        string centerText = !string.IsNullOrWhiteSpace(customTotalText)
            ? customTotalText
            : total.ToString("N0", CultureInfo.InvariantCulture);
        this._totalValueTb.Text = centerText;
        this._totalValueTb.FontSize = centerText.Length > 5 ? 16 : 22;

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
            List<int> activeIndices = new(slices.Count);
            for (int i = 0; i < slices.Count; i++)
            {
                if (slices[i].Value > 0)
                {
                    activeIndices.Add(i);
                }
            }

            int nonZeroCount = activeIndices.Count;
            if (nonZeroCount == 1)
            {
                DonutChartSlice onlySlice = slices[activeIndices[0]];
                Ellipse fullRing = new()
                {
                    Width = radius * 2.0,
                    Height = radius * 2.0,
                    Stroke = new SolidColorBrush(Color.Parse(onlySlice.ColorHex)),
                    StrokeThickness = this._strokeThickness,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                this._arcCanvas.Children.Add(fullRing);
            }
            else if (nonZeroCount > 1)
            {
                double gapDegrees = 1.6;
                double totalGapDegrees = nonZeroCount * gapDegrees;
                double availableDegrees = Math.Max(36.0, 360.0 - totalGapDegrees);
                double minSliceDegrees = Math.Min(4.0, availableDegrees / nonZeroCount);

                double[] sweeps = new double[nonZeroCount];
                double deficit = 0.0;
                double surplusPool = 0.0;

                for (int k = 0; k < nonZeroCount; k++)
                {
                    DonutChartSlice slice = slices[activeIndices[k]];
                    double rawSweep = (double)slice.Value / total * availableDegrees;
                    if (rawSweep < minSliceDegrees)
                    {
                        sweeps[k] = minSliceDegrees;
                        deficit += minSliceDegrees - rawSweep;
                    }
                    else
                    {
                        sweeps[k] = rawSweep;
                        surplusPool += rawSweep - minSliceDegrees;
                    }
                }

                if (deficit > 0.0 && surplusPool > 0.0)
                {
                    for (int k = 0; k < nonZeroCount; k++)
                    {
                        if (sweeps[k] > minSliceDegrees)
                        {
                            double share = (sweeps[k] - minSliceDegrees) / surplusPool;
                            sweeps[k] -= deficit * share;
                        }
                    }
                }

                double startAngle = -90.0 + (gapDegrees / 2.0);
                for (int k = 0; k < nonZeroCount; k++)
                {
                    DonutChartSlice slice = slices[activeIndices[k]];
                    double effectiveSweep = Math.Max(1.0, sweeps[k]);
                    IBrush brush = new SolidColorBrush(Color.Parse(slice.ColorHex));

                    Path arcPath = new()
                    {
                        Data = CreateArcGeometry(cx, cy, radius, startAngle, effectiveSweep),
                        Stroke = brush,
                        StrokeThickness = this._strokeThickness,
                        StrokeLineCap = PenLineCap.Flat
                    };
                    this._arcCanvas.Children.Add(arcPath);

                    startAngle += effectiveSweep + gapDegrees;
                }
            }
        }

        // Build colorful full-width legend rows with proportional progress bars
        for (int i = 0; i < slices.Count; i++)
        {
            DonutChartSlice slice = slices[i];
            Color parsedColor = Color.Parse(slice.ColorHex);
            IBrush sliceBrush = new SolidColorBrush(parsedColor);
            IBrush subtleTintBrush = new SolidColorBrush(Color.FromArgb(36, parsedColor.R, parsedColor.G, parsedColor.B));
            int pct = total > 0 ? (int)Math.Round(slice.Value * 100.0 / total) : 0;

            StackPanel rowStack = new() { Spacing = 5 };

            Grid topRow = new()
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
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = p.TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center
            };

            string formattedVal = valueFormatter is not null
                ? valueFormatter(slice.Value)
                : slice.Value.ToString("N0", CultureInfo.InvariantCulture);

            Border countBadge = new()
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2),
                Background = subtleTintBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{formattedVal} · {pct}%",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = sliceBrush
                }
            };

            Grid.SetColumn(colorDot, 0);
            Grid.SetColumn(labelTb, 1);
            Grid.SetColumn(countBadge, 2);
            topRow.Children.Add(colorDot);
            topRow.Children.Add(labelTb);
            topRow.Children.Add(countBadge);

            int fillWeight = Math.Clamp(pct, 0, 100);
            int emptyWeight = Math.Max(1, 100 - fillWeight);
            Grid barGrid = new()
            {
                Height = 5,
                ColumnDefinitions = new ColumnDefinitions($"{fillWeight}*,{emptyWeight}*")
            };

            Border trackBg = new()
            {
                CornerRadius = new CornerRadius(2.5),
                Background = p.SubtleBorder
            };
            Grid.SetColumnSpan(trackBg, 2);
            barGrid.Children.Add(trackBg);

            if (fillWeight > 0)
            {
                Border fillBar = new()
                {
                    CornerRadius = new CornerRadius(2.5),
                    Background = sliceBrush
                };
                Grid.SetColumn(fillBar, 0);
                barGrid.Children.Add(fillBar);
            }

            rowStack.Children.Add(topRow);
            rowStack.Children.Add(barGrid);

            Border legendCard = new()
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7),
                Background = p.SecondarySurface,
                BorderBrush = p.SubtleBorder,
                BorderThickness = new Thickness(1),
                Child = rowStack
            };

            this._legendStack.Children.Add(legendCard);
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
