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
        # Facebook.Net MCP Server — User Guide (63 Tools across 6 Bounded Contexts)

        Facebook.Net MCP Server connects AI assistants (Claude, Antigravity, ChatGPT, Cursor) with your Facebook accounts and the **Facebook One Client** SQLite database.

        ## Key Capabilities (63 Tools):
        1. **Account Vault & Browser Profiles (`fb_list_accounts`, `fb_switch_account`, `fb_import_session`, `fb_check_account_health`, `fb_set_account_proxy`, `fb_delete_account`, `fb_warmup_account`, `fb_open_browser_login`, `fb_list_browser_profiles`, `fb_create_or_assign_browser_profile`, `fb_delete_browser_profile`)**: Multi-account session management, News Feed warmup, interactive browser login, isolated Chromium profile creation/sync, and per-account HTTP/SOCKS5 proxies.
        2. **Community Groups, Rotation Selection & Smart Color Tagging (`fb_list_target_groups`, `fb_sync_joined_groups`, `fb_search_groups`, `fb_join_group`, `fb_batch_join_groups`, `fb_save_target_group`, `fb_remove_target_group`, `fb_configure_target_groups_selection`, `fb_list_group_tags`, `fb_create_group_tag`, `fb_create_group_tags_batch`, `fb_assign_group_tag`, `fb_auto_tag_groups_by_keywords`, `fb_clean_and_classify_all_groups`, `fb_delete_group_tag`)**: Discover, batch-join, rotate/enable random subsets (`fb_configure_target_groups_selection`), clean polluted tags, and auto-classify target groups by Vietnamese keywords using color tags.
        3. **Publishing, Batch Campaigns, Timeline Sync, Repost & Up-Top Bump (`fb_publish_post`, `fb_publish_campaign_batch`, `fb_sync_personal_timeline_posts`, `fb_repost_to_groups`, `fb_create_comment`, `fb_bump_recent_posts`, `fb_react_to_post`, `fb_list_published_posts`, `fb_delete_published_post`, `fb_list_templates`, `fb_save_template`, `fb_delete_template`)**: Publish single/multi-photo album posts or multi-group campaigns (`fb_publish_campaign_batch`), sync personal timeline posts, repost across groups, up-top bump recent group posts (`fb_bump_recent_posts`), and comment with Spintax (`{a|b|c}`) and dynamic variable (`{author_name}`, `{group_name}`, `{date}`, `{time}`, `{random_id}`) support.
        4. **Sales Leads, Batch Scraping, Auto-Commenting, Customer CRM Tagging & Deep Analytics (`fb_scrape_group_feed`, `fb_scrape_target_groups_batch`, `fb_auto_comment_leads_batch`, `fb_delete_scraped_leads`, `fb_query_scraped_leads`, `fb_search_leads_advanced`, `fb_list_lead_tags`, `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, `fb_auto_tag_leads_by_rules`, `fb_delete_lead_tag`, `fb_update_lead_notes`, `fb_analyze_workspace_data`, `fb_get_single_post`)**: Multi-dimensional lead search, batch group scraping, batch personalized sales commenting, obfuscated Vietnamese phone extraction, customer CRM color tagging, and workspace ROI analytics (`fb_analyze_workspace_data`).
        5. **System Settings, Anti-Checkpoint Delays, Storage Cache & Audit Logs (`fb_get_app_settings`, `fb_update_app_settings`, `fb_get_storage_usage`, `fb_clean_browser_cache`, `fb_get_audit_logs`, `fb_clear_audit_logs`, `fb_check_app_updates`)**: Inspect or adjust anti-checkpoint publishing/scraping delays and auto-scrape intervals in `appsettings.json`, clean Chromium disk caches, query/clear audit logs, and check GitHub Releases updates.
        6. **AI Context Memory (`fb_memory_save`, `fb_memory_search`, `fb_memory_delete`, `fb_memory_get_context`)**: Persistent SQLite second-brain memory across conversations.

        ## Recommended Workflow:
        - **Step 1**: Call `fb_list_accounts` or `fb_memory_get_context` to inspect available accounts, group tags, and customer lead tags.
        - **Step 2**: Organize groups via `fb_clean_and_classify_all_groups`, `fb_create_group_tags_batch`, `fb_auto_tag_groups_by_keywords`, and `fb_configure_target_groups_selection`.
        - **Step 3**: Use `fb_search_leads_advanced` or `fb_analyze_workspace_data` (instant local SQLite queries) to find zero-comment / phone-verified leads and auto-tag them with `fb_auto_tag_leads_by_rules` before replying with `fb_create_comment` or `fb_auto_comment_leads_batch`.
        """;

    [McpServerResource(UriTemplate = "guide://sales-agent", Name = "sales-agent", MimeType = "text/markdown")]
    [Description("Best practices and safety rules for AI sales agents using Facebook.Net MCP.")]
    public static string SalesAgentGuide() =>
        """
        # Smart AI Sales Agent Guidelines for Facebook.Net MCP

        1. **Zero-Network Local Lead Search & Analytics First**:
           Always call `fb_search_leads_advanced`, `fb_query_scraped_leads`, or `fb_analyze_workspace_data` first to search cached group posts in SQLite with accent-insensitive Vietnamese keywords (`commentStatus = 'pending'`). Call `fb_scrape_group_feed` or `fb_scrape_target_groups_batch` when fresh data from Facebook is needed.

        2. **Customer CRM Tagging & Group Auto-Classification**:
           - Use `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, and `fb_auto_tag_leads_by_rules` to classify leads (e.g. 'Khách có SĐT', 'Khách VIP', 'Cơ hội 0 bình luận', 'Môi giới/Seeding') and record CRM notes via `fb_update_lead_notes`.
           - Use `fb_clean_and_classify_all_groups`, `fb_auto_tag_groups_by_keywords`, and `fb_configure_target_groups_selection` (`mode = 'only_tag'` or `mode = 'random'`) to rotate and organize target community groups.

        3. **Anti-Checkpoint & Duplicate Protection**:
           - Use `fb_get_app_settings` and `fb_update_app_settings` to tune anti-checkpoint delays (`minDelaySeconds`, `maxDelaySeconds`, `scrapeDelayMinSeconds`, `scrapeDelayMaxSeconds`).
           - Keep `skipIfAlreadyCommented = true` when calling `fb_create_comment` (or use `fb_auto_comment_leads_batch`) so the same account never comments twice on the same post.
           - Use Spintax variations (`{Xin chào|Chào bạn}, ...`) when publishing (`fb_publish_post`, `fb_publish_campaign_batch`), reposting (`fb_repost_to_groups`), commenting, or bumping recent group posts (`fb_bump_recent_posts`).

        4. **Group Safety**:
           - Never attempt to leave joined groups; use `fb_configure_target_groups_selection`, `fb_save_target_group` (`isEnabled = false`), or `fb_remove_target_group` if a group should be excluded from campaigns.
        """;
}
