// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Facebook.Net.App.Views;
using Facebook.Net.Browser.MacOS;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.UI.ViewModels.Services;
using Facebook.Net.UI.ViewModels.Shell;

namespace Facebook.Net.App;

/// <summary>
/// Root Avalonia application class wiring OS-specific services, Cloudflare theme, i18n, and the multi-screen Shell window.
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
            IBrowserAdapter platformAdapter = OperatingSystem.IsMacOS()
                ? new MacOsBrowserAdapter()
                : new WindowsBrowserAdapter();

            UiServiceContext context = new(platformAdapter);
            ShellViewModel shellViewModel = new(context);
            desktop.MainWindow = new MainWindow(shellViewModel);
            desktop.Exit += (_, _) =>
            {
                shellViewModel.Dispose();
                context.Dispose();
            };

            _ = shellViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
