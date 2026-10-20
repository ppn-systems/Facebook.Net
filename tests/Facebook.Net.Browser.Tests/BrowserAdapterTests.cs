// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Facebook.Net.Browser.MacOS;
using Facebook.Net.Browser.Session;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Contracts.Primitives;
using Xunit;

namespace Facebook.Net.Browser.Tests;

/// <summary>
/// Unit tests for <see cref="BrowserLauncherHelpers"/>, <see cref="WindowsBrowserAdapter"/>, and <see cref="MacOsBrowserAdapter"/>.
/// </summary>
public sealed class BrowserAdapterTests
{
    [Fact]
    public void BuildStealthArguments_IncludesAntiAutomationAndProxyFlags()
    {
        BrowserLaunchOptions options = new(
            AccountUid: "10001",
            UserDataDir: @"C:\Temp\Profile1",
            UserAgent: "CustomAgent/1.0",
            Proxy: new ProxyConfig("p1", "10.0.0.1", 1080, ProxyProtocol.Socks5),
            Headless: true,
            WindowWidth: 1366,
            WindowHeight: 768,
            WindowX: 100,
            WindowY: 200);

        IReadOnlyList<string> args = BrowserLauncherHelpers.BuildStealthArguments(options, 9333);

        Assert.Contains("--remote-debugging-port=9333", args);
        Assert.Contains("--disable-blink-features=AutomationControlled", args);
        Assert.Contains("--disable-background-networking", args);
        Assert.Contains("--disable-component-update", args);
        Assert.Contains("--disable-domain-reliability", args);
        Assert.Contains("--disable-breakpad", args);
        Assert.Contains("--disk-cache-size=104857600", args);
        Assert.Contains("--js-flags=--max-old-space-size=256", args);
        Assert.Contains("--enable-low-end-device-mode", args);
        Assert.Contains("--user-agent=CustomAgent/1.0", args);
        Assert.Contains("--proxy-server=socks5://10.0.0.1:1080", args);
        Assert.Contains("--window-size=1366,768", args);
        Assert.Contains("--window-position=100,200", args);
        Assert.Contains("--headless=new", args);
        Assert.Contains("--disable-gpu", args);
        Assert.Contains("--disable-software-rasterizer", args);
        Assert.Contains("--mute-audio", args);
        Assert.True(options.EffectiveBlockStaticMedia);
        Assert.True(options.EffectiveLowResourceMode);
    }

    [Fact]
    public void BuildStealthArguments_VisibleInteractiveMode_PreservesFullRenderingWithoutHeadlessOrMediaBlock()
    {
        BrowserLaunchOptions visibleOptions = new(
            AccountUid: "10002",
            UserDataDir: @"C:\Temp\ProfileVisible",
            Headless: false);

        IReadOnlyList<string> args = BrowserLauncherHelpers.BuildStealthArguments(visibleOptions, 9444);

        Assert.Contains("--disable-background-networking", args);
        Assert.Contains("--disable-component-update", args);
        Assert.Contains("--disable-domain-reliability", args);
        Assert.Contains("--disable-breakpad", args);
        Assert.Contains("--disk-cache-size=104857600", args);

        Assert.DoesNotContain("--headless=new", args);
        Assert.DoesNotContain("--disable-gpu", args);
        Assert.DoesNotContain("--disable-software-rasterizer", args);
        Assert.DoesNotContain("--mute-audio", args);
        Assert.DoesNotContain("--js-flags=--max-old-space-size=256", args);
        Assert.DoesNotContain("--enable-low-end-device-mode", args);
        Assert.False(visibleOptions.EffectiveBlockStaticMedia);
        Assert.False(visibleOptions.EffectiveLowResourceMode);
    }

    [Fact]
    public void CleanProfileDiskCache_RemovesEphemeralCacheDirs_WhilePreservingCookieStore()
    {
        string tempProfile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fbnet_cache_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            string cacheDir = System.IO.Path.Combine(tempProfile, "Default", "Cache");
            string codeCacheDir = System.IO.Path.Combine(tempProfile, "Default", "Code Cache");
            string shaderDir = System.IO.Path.Combine(tempProfile, "GrShaderCache");
            string crashpadDir = System.IO.Path.Combine(tempProfile, "Crashpad");
            string cookieDir = System.IO.Path.Combine(tempProfile, "Default", "Network");
            string cookieFile = System.IO.Path.Combine(cookieDir, "Cookies");

            _ = System.IO.Directory.CreateDirectory(cacheDir);
            _ = System.IO.Directory.CreateDirectory(codeCacheDir);
            _ = System.IO.Directory.CreateDirectory(shaderDir);
            _ = System.IO.Directory.CreateDirectory(crashpadDir);
            _ = System.IO.Directory.CreateDirectory(cookieDir);
            System.IO.File.WriteAllText(cookieFile, "c_user=10001;xs=secret;");

            int cleaned = BrowserLauncherHelpers.CleanProfileDiskCache(tempProfile);

            Assert.Equal(4, cleaned);
            Assert.False(System.IO.Directory.Exists(cacheDir));
            Assert.False(System.IO.Directory.Exists(codeCacheDir));
            Assert.False(System.IO.Directory.Exists(shaderDir));
            Assert.False(System.IO.Directory.Exists(crashpadDir));
            Assert.True(System.IO.File.Exists(cookieFile));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempProfile))
            {
                System.IO.Directory.Delete(tempProfile, recursive: true);
            }
        }
    }

    [Fact]
    public void AllocateFreePort_ReturnsValidEphemeralPort()
    {
        int port = BrowserLauncherHelpers.AllocateFreePort();
        Assert.InRange(port, 1024, 65535);
    }

    [Fact]
    public void Adapters_ReportExpectedPlatformNames()
    {
        WindowsBrowserAdapter win = new();
        MacOsBrowserAdapter mac = new();

        Assert.Equal("windows", win.PlatformName);
        Assert.Equal("macos", mac.PlatformName);
    }

    [Fact]
    public void ParseCookieHeader_ParsesSemicolonDelimitedCookiesAndMarksHttpOnly()
    {
        string raw = "c_user=100012345; xs=44%3Asecret; datr=abcd; wd=1920x1080";
        List<Facebook.Net.Browser.Protocol.CdpCookie> cookies = ChromiumSession.ParseCookieHeader(raw.AsSpan());

        Assert.Equal(4, cookies.Count);
        Assert.Equal("c_user", cookies[0].Name);
        Assert.False(cookies[0].HttpOnly);
        Assert.Equal("xs", cookies[1].Name);
        Assert.True(cookies[1].HttpOnly);
        Assert.Equal(".facebook.com", cookies[1].Domain);
    }

    [Fact]
    public void LocalSessionFile_WhenPresentLocally_ValidatesExtractedTokensAndChecksum()
    {
        string? localSessionPath = TryFindLocalSessionFile();
        if (localSessionPath is null)
        {
            // Running on CI/CD or clean machine without .local/test-session.json -> skip gracefully
            return;
        }

        string json = System.IO.File.ReadAllText(localSessionPath);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = doc.RootElement;

        string? uid = root.GetProperty("uid").GetString();
        string? fbDtsg = root.GetProperty("fbDtsg").GetString();
        string? jazoest = root.GetProperty("jazoest").GetString();
        string? lsd = root.GetProperty("lsd").GetString();

        Assert.False(string.IsNullOrWhiteSpace(uid));
        Assert.False(string.IsNullOrWhiteSpace(fbDtsg));
        Assert.False(string.IsNullOrWhiteSpace(lsd));
        Assert.Equal(jazoest, ProtocolCodec.ComputeJazoest(fbDtsg.AsSpan()));
    }

    private static string? TryFindLocalSessionFile()
    {
        System.IO.DirectoryInfo? dir = new(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, ".local", "test-session.json");
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
