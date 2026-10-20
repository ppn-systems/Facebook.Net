// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Browser.Session;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Exceptions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;

namespace Facebook.Net.Browser.Windows;

/// <summary>
/// Windows-specific implementation of <see cref="IBrowserAdapter"/> for locating and launching
/// Chromium, Google Chrome, or Microsoft Edge on Windows.
/// </summary>
public sealed class WindowsBrowserAdapter : IBrowserAdapter
{
    private static readonly List<string> s_candidateRelativePaths = new(4)
    {
        @"Google\Chrome\Application\chrome.exe",
        @"Chromium\Application\chrome.exe",
        @"Microsoft\Edge\Application\msedge.exe",
        @"BraveSoftware\Brave-Browser\Application\brave.exe"
    };

    /// <inheritdoc />
    public string PlatformName => "windows";

    /// <inheritdoc />
    public string? LocateExecutablePath()
    {
        List<string> roots = new(3)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (string relative in s_candidateRelativePaths)
            {
                string fullPath = Path.Combine(root, relative);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IBrowserProcess> LaunchProcessAsync(BrowserLaunchOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string executable = !string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? options.ExecutablePath
            : (this.LocateExecutablePath()
                ?? throw new ProtocolException("Could not locate Chrome, Chromium, or Edge executable on Windows."));

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Specified Windows browser executable was not found.", executable);
        }

        _ = Directory.CreateDirectory(options.UserDataDir);

        Uri? existingWsUri = await BrowserLauncherHelpers
            .TryResolveExistingDebuggerUriAsync(options.UserDataDir, ct)
            .ConfigureAwait(false);
        if (existingWsUri is not null)
        {
            return new ManagedBrowserProcess(null, existingWsUri);
        }

        if (options.CleanDiskCache)
        {
            _ = BrowserLauncherHelpers.CleanProfileDiskCache(options.UserDataDir);
        }

        int port = BrowserLauncherHelpers.AllocateFreePort();

        BrowserLaunchOptions effectiveOptions = string.IsNullOrWhiteSpace(options.UserAgent)
            ? options with { UserAgent = ProtocolConstants.Protocol.DefaultWindowsUserAgent }
            : options;

        List<string> args = BrowserLauncherHelpers.BuildStealthArguments(effectiveOptions, port);

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = effectiveOptions.Headless
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process = Process.Start(startInfo)
            ?? throw new ProtocolException("Failed to start Windows Chromium process.");

        try
        {
            Uri wsUri = await BrowserLauncherHelpers
                .WaitForPageDebuggerUriAsync(port, process, options.UserDataDir, ct)
                .ConfigureAwait(false);
            return new ManagedBrowserProcess(process, wsUri, killOnDispose: effectiveOptions.Headless);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup failure
            }

            process.Dispose();
            throw;
        }
    }
}
