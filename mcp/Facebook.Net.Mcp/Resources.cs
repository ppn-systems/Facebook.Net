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
        2. **Community Groups & Smart Color Tagging (`fb_list_target_groups`, `fb_sync_joined_groups`, `fb_search_groups`, `fb_join_group`, `fb_save_target_group`, `fb_create_group_tag`, `fb_create_group_tags_batch`, `fb_assign_group_tag`, `fb_auto_tag_groups_by_keywords`, `fb_clean_and_classify_all_groups`, `fb_delete_group_tag`)**: Discover, join, clean polluted tags, and auto-classify target groups by Vietnamese keywords using color tags.
        3. **Publishing, Up-Top Bump & Engagement (`fb_publish_post`, `fb_create_comment`, `fb_bump_recent_posts`, `fb_react_to_post`, `fb_list_published_posts`, `fb_list_templates`, `fb_save_template`)**: Publish single/multi-photo album posts, up-top bump recent group posts (`fb_bump_recent_posts`), and comment with Spintax (`{a|b|c}`) and dynamic variable (`{author_name}`, `{group_name}`, `{date}`, `{time}`, `{random_id}`) support.
        4. **Sales Leads, Customer CRM Tagging & Deep Analytics (`fb_scrape_group_feed`, `fb_query_scraped_leads`, `fb_search_leads_advanced`, `fb_list_lead_tags`, `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, `fb_auto_tag_leads_by_rules`, `fb_delete_lead_tag`, `fb_update_lead_notes`, `fb_analyze_workspace_data`, `fb_get_single_post`, `fb_get_audit_logs`, `fb_get_storage_usage`)**: Multi-dimensional lead search, obfuscated Vietnamese phone extraction, customer CRM color tagging, and workspace ROI analytics (`fb_analyze_workspace_data`).
        5. **AI Context Memory (`fb_memory_save`, `fb_memory_search`, `fb_memory_delete`, `fb_memory_get_context`)**: Persistent SQLite second-brain memory across conversations.

        ## Recommended Workflow:
        - **Step 1**: Call `fb_list_accounts` or `fb_memory_get_context` to inspect available accounts, group tags, and customer lead tags.
        - **Step 2**: Organize groups via `fb_create_group_tags_batch` and `fb_auto_tag_groups_by_keywords`.
        - **Step 3**: Use `fb_search_leads_advanced` or `fb_analyze_workspace_data` (instant local SQLite queries) to find zero-comment / phone-verified leads and auto-tag them with `fb_auto_tag_leads_by_rules` before replying with `fb_create_comment`.
        """;

    [McpServerResource(UriTemplate = "guide://sales-agent", Name = "sales-agent", MimeType = "text/markdown")]
    [Description("Best practices and safety rules for AI sales agents using Facebook.Net MCP.")]
    public static string SalesAgentGuide() =>
        """
        # Smart AI Sales Agent Guidelines for Facebook.Net MCP

        1. **Zero-Network Local Lead Search & Analytics First**:
           Always call `fb_search_leads_advanced`, `fb_query_scraped_leads`, or `fb_analyze_workspace_data` first to search cached group posts in SQLite with accent-insensitive Vietnamese keywords (`commentStatus = 'pending'`). Only call `fb_scrape_group_feed` when fresh data from Facebook is needed.

        2. **Customer CRM Tagging & Group Auto-Classification**:
           - Use `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, and `fb_auto_tag_leads_by_rules` to classify leads (e.g. 'Khách có SĐT', 'Khách VIP', 'Cơ hội 0 bình luận', 'Môi giới/Seeding') and record CRM notes via `fb_update_lead_notes`.
           - Use `fb_auto_tag_groups_by_keywords` to automatically organize target community groups by topic.

        3. **Anti-Checkpoint & Duplicate Protection**:
           - Keep `skipIfAlreadyCommented = true` when calling `fb_create_comment` so the same account never comments twice on the same post.
           - Use Spintax variations (`{Xin chào|Chào bạn}, ...`) when publishing, commenting, or bumping recent group posts (`fb_bump_recent_posts`).

        4. **Group Safety**:
           - Never attempt to leave joined groups; use `fb_save_target_group` with `isEnabled = false` if a group should be excluded from campaigns.
        """;
}
