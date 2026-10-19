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
        Assert.Contains("--user-agent=CustomAgent/1.0", args);
        Assert.Contains("--proxy-server=socks5://10.0.0.1:1080", args);
        Assert.Contains("--window-size=1366,768", args);
        Assert.Contains("--window-position=100,200", args);
        Assert.Contains("--headless=new", args);
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
