// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Facebook.Net.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Facebook.Net.Mcp;

/// <summary>
/// Entry point and host runner for the companion MCP stdio server.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the MCP stdio server asynchronously.
    /// </summary>
    public static async Task Main(string[] args) =>
        await RunAsync(args).ConfigureAwait(false);

    /// <summary>
    /// Configures and runs the MCP stdio host with all Facebook One Client tools, prompts, and resources.
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Standard stdout is reserved for MCP JSON-RPC framing — all logs must strictly go to stderr.
        LogLevel logLevel = Enum.TryParse(Environment.GetEnvironmentVariable("FACEBOOK_MCP_LOG_LEVEL"), ignoreCase: true, out LogLevel parsed)
            ? parsed
            : LogLevel.Warning;

        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.SetMinimumLevel(logLevel);
        _ = builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        _ = builder.Services.AddSingleton<FacebookMcpSessionManager>();

        _ = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "Facebook.Net.Mcp",
                    Version = "1.0.0"
                };

                options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
                {
                    ILogger logger = context.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("Facebook.Net.Mcp.Tool");
                    string toolName = context.Params?.Name ?? "unknown";
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("MCP Tool Invoked: {ToolName} (Args: {ArgCount})", toolName, context.Params?.Arguments?.Count ?? 0);
                    }

                    Stopwatch sw = Stopwatch.StartNew();
                    try
                    {
                        return await next(context, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        sw.Stop();
                        logger.LogWarning(ex, "MCP Tool '{ToolName}' failed after {ElapsedMs} ms.", toolName, sw.ElapsedMilliseconds);

                        string recoveryHint = ResolveRecoveryHint(ex, toolName);
                        string errorJson = McpJson.Serialize(new
                        {
                            status = "error",
                            tool = toolName,
                            error_type = ex.GetType().Name,
                            message = ex.Message,
                            recovery_hint = recoveryHint,
                            elapsed_ms = sw.ElapsedMilliseconds
                        });

                        return new CallToolResult
                        {
                            IsError = true,
                            Content =
                            [
                                new TextContentBlock
                                {
                                    Text = errorJson
                                }
                            ]
                        };
                    }
                });
            })
            .WithResources<Resources>()
            .WithPrompts<Prompts>()
            .WithTools<AccountTools>()
            .WithTools<GroupTools>()
            .WithTools<PublisherTools>()
            .WithTools<SalesLeadsTools>()
            .WithTools<FriendshipTools>()
            .WithTools<EngagementTools>()
            .WithTools<SystemSettingsTools>()
            .WithTools<AiMemoryTools>()
            .WithStdioServerTransport();

        using IHost app = builder.Build();

        FacebookMcpSessionManager sessionManager = app.Services.GetRequiredService<FacebookMcpSessionManager>();
        sessionManager.StartBootstrapInBackground();

        await app.RunAsync().ConfigureAwait(false);
    }

    private static string ResolveRecoveryHint(Exception ex, string toolName)
    {
        string msg = ex.Message;
        if (msg.Contains("No Facebook account found", StringComparison.OrdinalIgnoreCase))
        {
            return "Call 'fb_list_accounts' or 'fb_import_session' / 'fb_open_browser_login' to register a Facebook account in the vault first.";
        }

        if (msg.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("dtsg", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("session", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return "Call 'fb_check_account_health' to refresh session material, or call 'fb_get_account_safety_quota' and 'fb_switch_account' to switch to another healthy account.";
        }

        if (ex is ArgumentException)
        {
            return $"Verify the required parameters for '{toolName}' (non-empty IDs/text) and retry.";
        }

        return "Check 'fb_get_account_safety_quota' or 'fb_get_audit_logs' if the issue persists, or retry with another account via 'fb_switch_account'.";
    }
}
