// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp;

/// <summary>
/// Runtime guide resources and operational documentation for AI assistants connecting to <c>Facebook.Net.Mcp</c>.
/// </summary>
[McpServerResourceType]
public sealed class Resources
{
    [McpServerResource(UriTemplate = "guide://overview", Name = "overview", MimeType = "text/markdown")]
    [Description("Overview and operational guidelines for Facebook.Net MCP Server.")]
    public static string Overview() =>
        """
        # Facebook.Net MCP Server — User Guide

        Facebook.Net MCP Server connects AI assistants (Claude, Antigravity, ChatGPT, Cursor) with your Facebook accounts and the **Facebook One Client** SQLite database.

        ## Key Capabilities:
        1. **Account Vault & Proxies (`fb_list_accounts`, `fb_switch_account`, `fb_import_session`, `fb_check_account_health`, `fb_set_account_proxy`)**: Multi-account session management with isolated Chromium profiles and per-account HTTP/SOCKS5 proxies.
        2. **Community Groups & Color Tags (`fb_list_target_groups`, `fb_sync_joined_groups`, `fb_search_groups`, `fb_join_group`, `fb_save_target_group`, `fb_create_group_tag`, `fb_assign_group_tag`)**: Discover, join, and organize target groups using color tags.
        3. **Publishing & Engagement (`fb_publish_post`, `fb_create_comment`, `fb_react_to_post`, `fb_list_published_posts`, `fb_list_templates`, `fb_save_template`)**: Publish posts and comments with Spintax (`{a|b|c}`) support and duplicate-comment protection.
        4. **Sales Leads & Audit Logs (`fb_scrape_group_feed`, `fb_query_scraped_leads`, `fb_get_single_post`, `fb_get_audit_logs`, `fb_get_storage_usage`)**: Scrape group feeds, rank posts by buyer-intent score (0..100), and inspect activity logs.

        ## Recommended Workflow:
        - **Step 1**: Call `fb_list_accounts` to inspect available accounts and verify which account is active.
        - **Step 2**: Call `fb_list_target_groups` or `fb_sync_joined_groups` to see target community groups.
        - **Step 3**: Use `fb_query_scraped_leads` (instant local SQLite query) or `fb_scrape_group_feed` to find high-intent customer posts before replying with `fb_create_comment`.
        """;

    [McpServerResource(UriTemplate = "guide://sales-agent", Name = "sales-agent", MimeType = "text/markdown")]
    [Description("Best practices and safety rules for AI sales agents using Facebook.Net MCP.")]
    public static string SalesAgentGuide() =>
        """
        # Smart AI Sales Agent Guidelines for Facebook.Net MCP

        1. **Zero-Network Local Lead Search First**:
           Always call `fb_query_scraped_leads` first to search cached group posts in SQLite with accent-insensitive Vietnamese keywords (`commentStatus = 'pending'`). Only call `fb_scrape_group_feed` when fresh data from Facebook is needed.

        2. **Anti-Checkpoint & Duplicate Protection**:
           - Keep `skipIfAlreadyCommented = true` when calling `fb_create_comment` so the same account never comments twice on the same post.
           - Use Spintax variations (`{Xin chào|Chào bạn}, ...`) when publishing or commenting across multiple targets.

        3. **Group Safety**:
           - Never attempt to leave joined groups; use `fb_save_target_group` with `isEnabled = false` if a group should be excluded from campaigns.
        """;
}
