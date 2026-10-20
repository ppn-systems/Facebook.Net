// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Facebook.Net.App.Views;
using Facebook.Net.Browser.MacOS;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.UI.Core.Theming;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App;

/// <summary>
/// Root Avalonia application class wiring OS-specific services, theme, i18n, and the multi-screen Shell window.
/// </summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        this.Styles.Add(new FluentTheme());

        // Override FluentTheme dynamic resources so FlyoutPresenter has zero outer box and ListBox/ComboBox selection uses warm brand tint
        this.Resources["FlyoutContentThemePadding"] = new Thickness(0);
        this.Resources["FlyoutBorderThemeThickness"] = new Thickness(0);
        this.Resources["FlyoutPresenterBackground"] = Brushes.Transparent;
        this.Resources["FlyoutBorderThemeBrush"] = Brushes.Transparent;

        ImmutableSolidColorBrush brandSelectLow = new(Color.FromArgb(34, 246, 130, 31));
        ImmutableSolidColorBrush brandSelectMed = new(Color.FromArgb(52, 246, 130, 31));
        ImmutableSolidColorBrush brandHoverLow = new(Color.FromArgb(18, 246, 130, 31));
        this.Resources["SystemControlHighlightListAccentLowBrush"] = brandSelectLow;
        this.Resources["SystemControlHighlightListAccentMediumBrush"] = brandSelectMed;
        this.Resources["SystemControlHighlightListAccentHighBrush"] = brandSelectMed;
        this.Resources["SystemControlHighlightListLowBrush"] = brandHoverLow;

        // Strip Fluent's default outer gray box/border/padding on FlyoutPresenter & its inner LayoutRoot
        Style flyoutPresenterStyle = new(x => x.OfType<FlyoutPresenter>());
        flyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, Brushes.Transparent));
        flyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty, Brushes.Transparent));
        flyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));
        flyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        flyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(10)));
        this.Styles.Add(flyoutPresenterStyle);

        Style flyoutRootStyle = new(x => x.OfType<FlyoutPresenter>().Template().OfType<Border>());
        flyoutRootStyle.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        flyoutRootStyle.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        flyoutRootStyle.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
        flyoutRootStyle.Setters.Add(new Setter(Border.PaddingProperty, new Thickness(0)));
        this.Styles.Add(flyoutRootStyle);

        Style comboBoxItemStyle = new(x => x.OfType<ComboBoxItem>());
        comboBoxItemStyle.Setters.Add(new Setter(ComboBoxItem.CornerRadiusProperty, new CornerRadius(6)));
        comboBoxItemStyle.Setters.Add(new Setter(ComboBoxItem.MarginProperty, new Thickness(4, 1)));
        comboBoxItemStyle.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(10, 6)));
        this.Styles.Add(comboBoxItemStyle);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IBrowserAdapter platformAdapter = OperatingSystem.IsMacOS()
                ? new MacOsBrowserAdapter()
                : new WindowsBrowserAdapter();

            UiServiceContext context = new(platformAdapter);
            this.RequestedThemeVariant = context.Theme.CurrentMode == AppThemeMode.Light
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            ShellViewModel shellViewModel = new(context);
            desktop.MainWindow = new MainWindow(shellViewModel);
            desktop.Exit += (_, _) =>
            {
                shellViewModel.Dispose();
                context.Dispose();
            };

            InitializeShellSafeAsync(shellViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void InitializeShellSafeAsync(ShellViewModel shellViewModel)
    {
        try
        {
            await shellViewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            shellViewModel.AuditDrawer.LatestTickerText = $"Startup error: {ex.Message}";
        }
    }
}
