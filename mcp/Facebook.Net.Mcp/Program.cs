// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Facebook.Net.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Facebook.Net.Mcp;

internal static class Program
{
    private static async Task Main(string[] args)
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
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                {
                    Name = "Facebook.Net.Mcp",
                    Version = "1.0.0"
                };

                options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
                {
                    ILogger logger = context.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("Facebook.Net.Mcp.Tool");
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("MCP Tool Invoked: {ToolName} (Args: {ArgCount})", context.Params?.Name, context.Params?.Arguments?.Count ?? 0);
                    }

                    return await next(context, ct).ConfigureAwait(false);
                });
            })
            .WithResources<Resources>()
            .WithPrompts<Prompts>()
            .WithTools<AccountTools>()
            .WithTools<GroupTools>()
            .WithTools<PublisherTools>()
            .WithTools<SalesLeadsTools>()
            .WithTools<SystemSettingsTools>()
            .WithTools<AiMemoryTools>()
            .WithStdioServerTransport();

        using IHost app = builder.Build();

        FacebookMcpSessionManager sessionManager = app.Services.GetRequiredService<FacebookMcpSessionManager>();
        sessionManager.StartBootstrapInBackground();

        await app.RunAsync().ConfigureAwait(false);
    }
}
