// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;

namespace Meta.UI.Core.Configuration;

/// <summary>
/// Application service contract for safely opening external URLs in the system default browser
/// and opening local directories in the OS file manager (Windows Explorer / macOS Finder).
/// </summary>
public interface ISystemLauncherService
{
    /// <summary>Opens an <c>http://</c> or <c>https://</c> URL in the operating system's default browser.</summary>
    bool OpenUrl(string? url);

    /// <summary>Opens an existing local directory path in the operating system's file manager.</summary>
    bool OpenDirectory(string? directoryPath);
}

/// <summary>
/// Default stateless implementation of <see cref="ISystemLauncherService"/>.
/// </summary>
public sealed class SystemLauncherService : ISystemLauncherService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="SystemLauncherService"/>.</summary>
    public static SystemLauncherService Instance { get; } = new();

    /// <inheritdoc />
    public bool OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string trimmed = url.Trim();
        if (!trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = trimmed,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool OpenDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(directoryPath)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
