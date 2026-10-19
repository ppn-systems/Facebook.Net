// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Facebook.Net.App;

/// <summary>
/// Root Avalonia application class.
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
            desktop.MainWindow = new Window
            {
                Title = "Facebook.Net Desktop",
                Width = 1200,
                Height = 800
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
