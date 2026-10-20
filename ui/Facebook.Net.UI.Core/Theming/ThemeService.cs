// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Facebook.Net.UI.Core.Threading;

namespace Facebook.Net.UI.Core.Theming;

/// <summary>
/// Supported application color themes (Dark, Light, and System Default mode).
/// </summary>
public enum AppThemeMode
{
    /// <summary>Dark Mode (Charcoal/Zinc canvas with warm orange accents).</summary>
    Dark = 0,

    /// <summary>Light Mode (Crisp Slate/White canvas with warm orange accents).</summary>
    Light = 1,

    /// <summary>Automatically follow the operating system's default Dark/Light theme.</summary>
    System = 2
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
/// Reactive cross-platform theme service providing Dark, Light, and OS System Default palettes
/// and synchronizing Avalonia's <see cref="ThemeVariant"/> and <see cref="IPlatformSettings"/>.
/// </summary>
public sealed partial class ThemeService : INotifyPropertyChanged
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
        BrandPrimary: new ImmutableSolidColorBrush(Color.Parse("#EA580C")),
        BrandHover: new ImmutableSolidColorBrush(Color.Parse("#C2410C")),
        BrandSubtleFill: new ImmutableSolidColorBrush(Color.Parse("#FFEDD5")),
        BrandSubtleBorder: new ImmutableSolidColorBrush(Color.Parse("#FB923C")),
        AppCanvasBackground: new ImmutableSolidColorBrush(Color.Parse("#EAEEF4")),
        ElevatedSurface: new ImmutableSolidColorBrush(Color.Parse("#FFFFFF")),
        SecondarySurface: new ImmutableSolidColorBrush(Color.Parse("#E2E8F0")),
        SubtleBorder: new ImmutableSolidColorBrush(Color.Parse("#CBD5E1")),
        InteractiveBorder: new ImmutableSolidColorBrush(Color.Parse("#94A3B8")),
        TextPrimary: new ImmutableSolidColorBrush(Color.Parse("#0F172A")),
        TextSecondary: new ImmutableSolidColorBrush(Color.Parse("#334155")),
        TextMuted: new ImmutableSolidColorBrush(Color.Parse("#475569")),
        SuccessText: new ImmutableSolidColorBrush(Color.Parse("#166534")),
        SuccessFill: new ImmutableSolidColorBrush(Color.Parse("#DCFCE7")),
        SuccessBorder: new ImmutableSolidColorBrush(Color.Parse("#4ADE80")),
        WarningText: new ImmutableSolidColorBrush(Color.Parse("#92400E")),
        WarningFill: new ImmutableSolidColorBrush(Color.Parse("#FEF3C7")),
        WarningBorder: new ImmutableSolidColorBrush(Color.Parse("#FBBF24")),
        DangerText: new ImmutableSolidColorBrush(Color.Parse("#991B1B")),
        DangerFill: new ImmutableSolidColorBrush(Color.Parse("#FEE2E2")),
        DangerBorder: new ImmutableSolidColorBrush(Color.Parse("#F87171")),
        InfoText: new ImmutableSolidColorBrush(Color.Parse("#075985")),
        InfoFill: new ImmutableSolidColorBrush(Color.Parse("#E0F2FE")),
        InfoBorder: new ImmutableSolidColorBrush(Color.Parse("#38BDF8")));

    private readonly IUiDispatcher _dispatcher;
    private IPlatformSettings? _platformSettings;
    private AppThemeMode _currentMode = AppThemeMode.Dark;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    public ThemeService(IUiDispatcher? dispatcher = null) =>
        _dispatcher = dispatcher ?? AvaloniaUiDispatcher.Instance;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs whenever the active <see cref="AppThemeMode"/> or OS system theme changes.
    /// </summary>
    public event EventHandler<AppThemeMode>? ThemeChanged;

    /// <summary>
    /// Gets the user-configured theme mode (<see cref="AppThemeMode.Dark"/>, <see cref="AppThemeMode.Light"/>, or <see cref="AppThemeMode.System"/>).
    /// </summary>
    public AppThemeMode CurrentMode => _currentMode;

    /// <summary>
    /// Gets the effective resolved theme (<see cref="AppThemeMode.Dark"/> or <see cref="AppThemeMode.Light"/>).
    /// When <see cref="CurrentMode"/> is <see cref="AppThemeMode.System"/>, resolves the OS theme across Windows, macOS, and Linux.
    /// </summary>
    public AppThemeMode EffectiveMode =>
        _currentMode == AppThemeMode.System
            ? this.DetectSystemThemeMode()
            : _currentMode;

    /// <summary>
    /// Gets the active <see cref="ThemePalette"/> for the resolved <see cref="EffectiveMode"/>.
    /// </summary>
    public ThemePalette Palette =>
        this.EffectiveMode == AppThemeMode.Light ? s_lightPalette : s_darkPalette;

    /// <summary>
    /// Attaches Avalonia's cross-platform <see cref="IPlatformSettings"/> so live OS theme changes are tracked automatically.
    /// </summary>
    public void AttachPlatformSettings(IPlatformSettings? platformSettings)
    {
        if (ReferenceEquals(_platformSettings, platformSettings))
        {
            return;
        }

        _platformSettings?.ColorValuesChanged -= this.OnPlatformColorValuesChanged;
        _platformSettings = platformSettings;
        _platformSettings?.ColorValuesChanged += this.OnPlatformColorValuesChanged;

        if (_currentMode == AppThemeMode.System)
        {
            this.BroadcastThemeUpdate();
        }
    }

    /// <summary>
    /// Detects the operating system's current Dark/Light theme across Windows, macOS, and Linux.
    /// </summary>
    public AppThemeMode DetectSystemThemeMode()
    {
        try
        {
            IPlatformSettings? ps = _platformSettings ?? Application.Current?.PlatformSettings;
            if (ps is not null)
            {
                PlatformColorValues values = ps.GetColorValues();
                return values.ThemeVariant == PlatformThemeVariant.Light
                    ? AppThemeMode.Light
                    : AppThemeMode.Dark;
            }
        }
        catch
        {
            // Fallback to OS-level inspection below if Avalonia platform settings are not yet ready
        }

        return DetectOsThemeFallback();
    }

    /// <summary>
    /// Switches the application theme and notifies all bound UI controls on the UI thread.
    /// </summary>
    /// <param name="mode">The target theme mode (<see cref="AppThemeMode.System"/>, <see cref="AppThemeMode.Dark"/>, or <see cref="AppThemeMode.Light"/>).</param>
    public void SetTheme(AppThemeMode mode)
    {
        if (_platformSettings is null && Application.Current?.PlatformSettings is { } appPlatformSettings)
        {
            this.AttachPlatformSettings(appPlatformSettings);
        }

        if (_currentMode == mode)
        {
            return;
        }

        _currentMode = mode;
        this.BroadcastThemeUpdate();
    }

    /// <summary>
    /// Toggles between Dark and Light mode.
    /// </summary>
    public void ToggleTheme() =>
        this.SetTheme(this.EffectiveMode == AppThemeMode.Dark ? AppThemeMode.Light : AppThemeMode.Dark);

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (_currentMode == AppThemeMode.System)
        {
            this.BroadcastThemeUpdate();
        }
    }

    private void BroadcastThemeUpdate()
    {
        void ApplyOnUiThread()
        {
            AppThemeMode effective = this.EffectiveMode;
            Application.Current?.RequestedThemeVariant = effective == AppThemeMode.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CurrentMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.EffectiveMode)));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Palette)));
            this.ThemeChanged?.Invoke(this, effective);
        }

        _dispatcher.Post(ApplyOnUiThread);
    }

    private static AppThemeMode DetectOsThemeFallback()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                const int hkeyCurrentUser = unchecked((int)0x80000001);
                const uint rrfRtRegDword = 0x00000010;
                uint data = 1;
                uint dataSize = sizeof(uint);
                int status = RegGetValueW(
                    hkeyCurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    rrfRtRegDword,
                    out _,
                    ref data,
                    ref dataSize);
                if (status == 0)
                {
                    return data != 0 ? AppThemeMode.Light : AppThemeMode.Dark;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string globalPrefs = Path.Combine(home, "Library", "Preferences", ".GlobalPreferences.plist");
                if (File.Exists(globalPrefs))
                {
                    byte[] raw = File.ReadAllBytes(globalPrefs);
                    string ascii = Encoding.ASCII.GetString(raw);
                    if (ascii.Contains("AppleInterfaceStyle", StringComparison.Ordinal) &&
                        ascii.Contains("Dark", StringComparison.OrdinalIgnoreCase))
                    {
                        return AppThemeMode.Dark;
                    }

                    return AppThemeMode.Light;
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string? gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
                if (!string.IsNullOrWhiteSpace(gtkTheme))
                {
                    return gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase)
                        ? AppThemeMode.Dark
                        : AppThemeMode.Light;
                }
            }
        }
        catch
        {
            // Ignore OS probe errors
        }

        return AppThemeMode.Light;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
        Justification = "Avoids requiring AllowUnsafeBlocks in UI.Core.")]
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegGetValueW(
        nint hkey,
        string lpSubKey,
        string lpValue,
        uint dwFlags,
        out uint pdwType,
        ref uint pvData,
        ref uint pcbData);
}
