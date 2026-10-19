// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Facebook.Net.App.Services;
using Facebook.Net.App.ViewModels;
using Facebook.Net.App.Views;

namespace Facebook.Net.App;

/// <summary>
/// Root Avalonia application class wiring OS-specific services and the main desktop window.
/// </summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() =>
        this.Styles.Add(new FluentTheme());

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppServiceBootstrapper services = new();
            MainWindowViewModel mainViewModel = new(services);
            desktop.MainWindow = new MainWindow(mainViewModel);
            desktop.Exit += (_, _) => services.Dispose();

            _ = mainViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
