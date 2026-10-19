// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Net.Contracts;
using Facebook.Net.Contracts.Exceptions;

namespace Facebook.Net.Browser.MacOS;

/// <summary>
/// macOS-specific implementation of <see cref="IBrowserAdapter"/> handling <c>.app</c> bundles,
/// executable permissions, and isolated Chromium process management on Apple Silicon and Intel Macs.
/// </summary>
public sealed class MacOsBrowserAdapter : IBrowserAdapter
{
    private static readonly List<string> s_candidateAppPaths = new(4)
    {
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Chromium.app/Contents/MacOS/Chromium",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
        "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"
    };

    /// <inheritdoc />
    public string PlatformName => "macos";

    /// <inheritdoc />
    public string? LocateExecutablePath()
    {
        string userApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");

        foreach (string systemPath in s_candidateAppPaths)
        {
            if (File.Exists(systemPath))
            {
                return systemPath;
            }

            if (!string.IsNullOrWhiteSpace(userApps))
            {
                string relativeFromApps = systemPath["/Applications/".Length..];
                string userAppPath = Path.Combine(userApps, relativeFromApps);
                if (File.Exists(userAppPath))
                {
                    return userAppPath;
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
                ?? throw new ProtocolException("Could not locate Chrome, Chromium, or Edge .app bundle on macOS."));

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Specified macOS browser executable was not found.", executable);
        }

        if (OperatingSystem.IsMacOS())
        {
            EnsureExecutablePermission(executable);
        }

        _ = Directory.CreateDirectory(options.UserDataDir);
        int port = BrowserLauncherHelpers.AllocateFreePort();

        BrowserLaunchOptions effectiveOptions = string.IsNullOrWhiteSpace(options.UserAgent)
            ? options with { UserAgent = ProtocolConstants.Protocol.DefaultMacOsUserAgent }
            : options;

        IReadOnlyList<string> args = BrowserLauncherHelpers.BuildStealthArguments(effectiveOptions, port);

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
            ?? throw new ProtocolException("Failed to start macOS Chromium process.");

        try
        {
            Uri wsUri = await BrowserLauncherHelpers.WaitForPageDebuggerUriAsync(port, process, ct).ConfigureAwait(false);
            return new ManagedBrowserProcess(process, wsUri);
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

    [SupportedOSPlatform("macos")]
    private static void EnsureExecutablePermission(string executablePath)
    {
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(executablePath);
            UnixFileMode required = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & required) == 0)
            {
                File.SetUnixFileMode(executablePath, mode | UnixFileMode.UserExecute);
            }
        }
        catch
        {
            // Ignore if running without permission to modify system /Applications bundle
        }
    }
}
