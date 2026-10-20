// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

using Facebook.Net.UI.Core.Threading;

namespace Facebook.Net.UI.Core.Theming;

/// <summary>
/// Supported application color themes (Dark and Light mode).
/// </summary>
public enum AppThemeMode
{
    /// <summary>Dark Mode (Charcoal/Zinc canvas with warm orange accents).</summary>
    Dark,

    /// <summary>Light Mode (Crisp Slate/White canvas with warm orange accents).</summary>
    Light
}

/// <summary>
/// Immutable palette snapshot of UI color tokens for either Dark or Light mode.
/// </summary>
public sealed record ThemePalette(
    IBrush BrandPrimary,
    IBrush BrandHover,
    IBrush BrandSubtleFill,
    IBrush BrandSubtleBorder,
    IBrush AppCanvasBackground,
    IBrush ElevatedSurface,
    IBrush SecondarySurface,
    IBrush SubtleBorder,
    IBrush InteractiveBorder,
    IBrush TextPrimary,
    IBrush TextSecondary,
    IBrush TextMuted,
    IBrush SuccessText,
    IBrush SuccessFill,
    IBrush SuccessBorder,
    IBrush WarningText,
    IBrush WarningFill,
    IBrush WarningBorder,
    IBrush DangerText,
    IBrush DangerFill,
    IBrush DangerBorder,
    IBrush InfoText,
    IBrush InfoFill,
    IBrush InfoBorder);

/// <summary>
/// Reactive theme service providing Dark and Light palettes and synchronizing Avalonia's <see cref="ThemeVariant"/>.
/// </summary>
public sealed class ThemeService : INotifyPropertyChanged
{
    private static readonly ThemePalette s_darkPalette = new(
        BrandPrimary: new ImmutableSolidColorBrush(Color.Parse("#F6821F")),
        BrandHover: new ImmutableSolidColorBrush(Color.Parse("#FF7A4D")),
        BrandSubtleFill: new ImmutableSolidColorBrush(Color.FromArgb(36, 246, 130, 31)),
        BrandSubtleBorder: new ImmutableSolidColorBrush(Color.FromArgb(90, 246, 130, 31)),
        AppCanvasBackground: new ImmutableSolidColorBrush(Color.Parse("#0F1115")),
        ElevatedSurface: new ImmutableSolidColorBrush(Color.Parse("#18181B")),
        SecondarySurface: new ImmutableSolidColorBrush(Color.Parse("#202024")),
        SubtleBorder: new ImmutableSolidColorBrush(Color.Parse("#27272A")),
        InteractiveBorder: new ImmutableSolidColorBrush(Color.Parse("#3F3F46")),
        TextPrimary: new ImmutableSolidColorBrush(Color.Parse("#F4F4F5")),
        TextSecondary: new ImmutableSolidColorBrush(Color.Parse("#A1A1AA")),
        TextMuted: new ImmutableSolidColorBrush(Color.Parse("#71717A")),
        SuccessText: new ImmutableSolidColorBrush(Color.Parse("#4ADE80")),
        SuccessFill: new ImmutableSolidColorBrush(Color.FromArgb(32, 34, 197, 94)),
        SuccessBorder: new ImmutableSolidColorBrush(Color.FromArgb(64, 74, 222, 128)),
        WarningText: new ImmutableSolidColorBrush(Color.Parse("#FBBF24")),
        WarningFill: new ImmutableSolidColorBrush(Color.FromArgb(32, 245, 158, 11)),
        WarningBorder: new ImmutableSolidColorBrush(Color.FromArgb(64, 251, 191, 36)),
        DangerText: new ImmutableSolidColorBrush(Color.Parse("#F87171")),
        DangerFill: new ImmutableSolidColorBrush(Color.FromArgb(32, 239, 68, 68)),
        DangerBorder: new ImmutableSolidColorBrush(Color.FromArgb(64, 248, 113, 113)),
        InfoText: new ImmutableSolidColorBrush(Color.Parse("#38BDF8")),
        InfoFill: new ImmutableSolidColorBrush(Color.FromArgb(32, 14, 165, 233)),
        InfoBorder: new ImmutableSolidColorBrush(Color.FromArgb(64, 56, 189, 248)));

    private static readonly ThemePalette s_lightPalette = new(
        BrandPrimary: new ImmutableSolidColorBrush(Color.Parse("#F6821F")),
        BrandHover: new ImmutableSolidColorBrush(Color.Parse("#E06D10")),
        BrandSubtleFill: new ImmutableSolidColorBrush(Color.Parse("#FFF3E8")),
        BrandSubtleBorder: new ImmutableSolidColorBrush(Color.Parse("#FDBA74")),
        AppCanvasBackground: new ImmutableSolidColorBrush(Color.Parse("#F8FAFC")),
        ElevatedSurface: new ImmutableSolidColorBrush(Color.Parse("#FFFFFF")),
        SecondarySurface: new ImmutableSolidColorBrush(Color.Parse("#F1F5F9")),
        SubtleBorder: new ImmutableSolidColorBrush(Color.Parse("#E2E8F0")),
        InteractiveBorder: new ImmutableSolidColorBrush(Color.Parse("#CBD5E1")),
        TextPrimary: new ImmutableSolidColorBrush(Color.Parse("#09090B")),
        TextSecondary: new ImmutableSolidColorBrush(Color.Parse("#475569")),
        TextMuted: new ImmutableSolidColorBrush(Color.Parse("#94A3B8")),
        SuccessText: new ImmutableSolidColorBrush(Color.Parse("#15803D")),
        SuccessFill: new ImmutableSolidColorBrush(Color.Parse("#DCFCE7")),
        SuccessBorder: new ImmutableSolidColorBrush(Color.Parse("#86EFAC")),
        WarningText: new ImmutableSolidColorBrush(Color.Parse("#B45309")),
        WarningFill: new ImmutableSolidColorBrush(Color.Parse("#FEF3C7")),
        WarningBorder: new ImmutableSolidColorBrush(Color.Parse("#FCD34D")),
        DangerText: new ImmutableSolidColorBrush(Color.Parse("#B91C1C")),
        DangerFill: new ImmutableSolidColorBrush(Color.Parse("#FEE2E2")),
        DangerBorder: new ImmutableSolidColorBrush(Color.Parse("#FCA5A5")),
        InfoText: new ImmutableSolidColorBrush(Color.Parse("#0369A1")),
        InfoFill: new ImmutableSolidColorBrush(Color.Parse("#E0F2FE")),
        InfoBorder: new ImmutableSolidColorBrush(Color.Parse("#7DD3FC")));

    private readonly IUiDispatcher _dispatcher;
    private AppThemeMode _currentMode = AppThemeMode.Dark;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    public ThemeService(IUiDispatcher? dispatcher = null) =>
        this._dispatcher = dispatcher ?? AvaloniaUiDispatcher.Instance;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs whenever the active <see cref="AppThemeMode"/> changes.
    /// </summary>
    public event EventHandler<AppThemeMode>? ThemeChanged;

    /// <summary>
    /// Gets the current theme mode (<see cref="AppThemeMode.Dark"/> or <see cref="AppThemeMode.Light"/>).
    /// </summary>
    public AppThemeMode CurrentMode => this._currentMode;

    /// <summary>
    /// Gets the active <see cref="ThemePalette"/> for the current mode.
    /// </summary>
    public ThemePalette Palette =>
        this._currentMode == AppThemeMode.Light ? s_lightPalette : s_darkPalette;

    /// <summary>
    /// Switches the application theme and notifies all bound UI controls on the UI thread.
    /// </summary>
    /// <param name="mode">The target theme mode.</param>
    public void SetTheme(AppThemeMode mode)
    {
        if (this._currentMode == mode)
        {
            return;
        }

        this._currentMode = mode;

        void ApplyOnUiThread()
        {
            Application.Current?.RequestedThemeVariant = mode == AppThemeMode.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CurrentMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Palette)));
            this.ThemeChanged?.Invoke(this, mode);
        }

        this._dispatcher.Post(ApplyOnUiThread);
    }

    /// <summary>
    /// Toggles between Dark and Light mode.
    /// </summary>
    public void ToggleTheme() =>
        this.SetTheme(this._currentMode == AppThemeMode.Dark ? AppThemeMode.Light : AppThemeMode.Dark);
}
