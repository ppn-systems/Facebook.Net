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
    [Description("Overview and operational guidelines for Facebook.Net MCP Server (72 Tools across 8 Bounded Contexts).")]
    public static string Overview() =>
        """
        # Facebook.Net MCP Server — User Guide (72 Tools across 8 Bounded Contexts)

        Facebook.Net MCP Server connects AI assistants (Claude, Antigravity, ChatGPT, Cursor) with your Facebook accounts and the **Facebook One Client** SQLite database.

        ## Key Capabilities (72 Tools):
        1. **Account Vault, Safety Quotas & Browser Profiles (`fb_list_accounts`, `fb_switch_account`, `fb_import_session`, `fb_check_account_health`, `fb_get_account_safety_quota`, `fb_set_account_proxy`, `fb_delete_account`, `fb_warmup_account`, `fb_open_browser_login`, `fb_list_browser_profiles`, `fb_create_or_assign_browser_profile`, `fb_delete_browser_profile`)**: Multi-account session management, live anti-checkpoint rate-limit & heat-score evaluation (`fb_get_account_safety_quota`), News Feed warmup, interactive browser login, isolated Chromium profile creation/sync, and per-account HTTP/SOCKS5 proxies.
        2. **Community Groups, Questionnaire Auto-Answer, Member Discovery & Smart Color Tagging (`fb_list_target_groups`, `fb_sync_joined_groups`, `fb_search_groups`, `fb_join_group`, `fb_batch_join_groups`, `fb_get_group_questions`, `fb_submit_group_answers`, `fb_get_group_new_members`, `fb_invite_friends_to_group`, `fb_save_target_group`, `fb_remove_target_group`, `fb_configure_target_groups_selection`, `fb_list_group_tags`, `fb_create_group_tag`, `fb_create_group_tags_batch`, `fb_assign_group_tag`, `fb_auto_tag_groups_by_keywords`, `fb_clean_and_classify_all_groups`, `fb_delete_group_tag`)**: Discover, join, auto-answer private group membership approval questionnaires (`fb_get_group_questions`, `fb_submit_group_answers`), scrape new group members (`fb_get_group_new_members`), invite friends, rotate/enable random subsets (`fb_configure_target_groups_selection`), and auto-classify target groups by Vietnamese keywords.
        3. **Publishing (Photos & .mp4 Videos), Multi-Account Batch Campaigns, Timeline Sync, Repost & Up-Top Bump (`fb_publish_post`, `fb_publish_campaign_batch`, `fb_sync_personal_timeline_posts`, `fb_repost_to_groups`, `fb_create_comment`, `fb_bump_recent_posts`, `fb_react_to_post`, `fb_list_published_posts`, `fb_delete_published_post`, `fb_list_templates`, `fb_save_template`, `fb_delete_template`)**: Publish single/multi-photo or `.mp4` video posts, run multi-account round-robin group campaigns (`fb_publish_campaign_batch` with `accountUidsCsv`), sync personal timeline posts, repost across groups, up-top bump recent group posts (`fb_bump_recent_posts`), and comment with Spintax (`{a|b|c}`) and dynamic variables (`{author_name}`, `{group_name}`, `{date}`, `{time}`, `{random_id}`).
        4. **Sales Leads, Competitor Post Mining, Multi-Account Auto-Commenting & Deep Analytics (`fb_scrape_group_feed`, `fb_scrape_target_groups_batch`, `fb_auto_comment_leads_batch`, `fb_delete_scraped_leads`, `fb_query_scraped_leads`, `fb_search_leads_advanced`, `fb_list_lead_tags`, `fb_create_lead_tags_batch`, `fb_assign_lead_tag`, `fb_auto_tag_leads_by_rules`, `fb_delete_lead_tag`, `fb_update_lead_notes`, `fb_analyze_workspace_data`, `fb_get_single_post`, `fb_get_post_commenters`, `fb_get_post_reactors`)**: Multi-dimensional lead search, batch group scraping, competitor post commenter/reactor scraping (`fb_get_post_commenters`, `fb_get_post_reactors`), multi-account sales commenting, obfuscated Vietnamese phone extraction, in-app customer CRM color tagging & notes, and workspace ROI analytics (`fb_analyze_workspace_data`).
        5. **Friendship & Social Graph Automation (`fb_get_incoming_friend_requests`, `fb_get_outgoing_friend_requests`, `fb_get_friend_suggestions`, `fb_get_profile_followers`, `fb_handle_friend_requests_batch`, `fb_send_friend_requests_batch`, `fb_unfriend_or_unfollow_batch`, `fb_poke_users_batch`)**: Manage incoming/outgoing friend requests, PYMK suggestions, followers, batch friend outreach to high-intent leads, social graph cleanup, and poking.
        6. **Stories, Notifications & Managed Fanpages (`fb_get_notifications`, `fb_mark_notifications_read`, `fb_get_stories_tray`, `fb_watch_and_reply_stories_batch`, `fb_create_story`, `fb_get_managed_pages`)**: Monitor customer replies via notifications, automate Story viewing/replying warmup, publish photo/video/text Stories, and inspect managed Fanpages.
        7. **System Settings, Anti-Checkpoint Delays, Storage Cache & Audit Logs (`fb_get_app_settings`, `fb_update_app_settings`, `fb_get_storage_usage`, `fb_clean_browser_cache`, `fb_get_audit_logs`, `fb_clear_audit_logs`, `fb_check_app_updates`)**: Inspect or adjust anti-checkpoint publishing/scraping delays and auto-scrape intervals in `appsettings.json`, clean Chromium disk caches, query/clear audit logs, and check GitHub Releases updates.
        8. **AI Context Memory (`fb_memory_save`, `fb_memory_search`, `fb_memory_delete`, `fb_memory_get_context`)**: Persistent SQLite second-brain memory across conversations.
        """;

    [McpServerResource(UriTemplate = "guide://sales-agent", Name = "sales-agent", MimeType = "text/markdown")]
    [Description("Best practices and safety rules for AI sales agents using Facebook.Net MCP.")]
    public static string SalesAgentGuide() =>
        """
        # Smart AI Sales Agent Guidelines for Facebook.Net MCP

        1. **Zero-Network Local Lead Search & Analytics First**:
           Always call `fb_search_leads_advanced`, `fb_query_scraped_leads`, or `fb_analyze_workspace_data` first to search cached group posts in SQLite with accent-insensitive Vietnamese keywords (`commentStatus = 'pending'`). Call `fb_scrape_group_feed` or `fb_scrape_target_groups_batch` when fresh data from Facebook is needed.

        2. **Competitor Post Hijacking & Social Outreach**:
           - Use `fb_get_post_commenters` and `fb_get_post_reactors` on viral/competitor posts to extract engaged prospects.
           - Use `fb_send_friend_requests_batch` (`autoFromHighIntentLeads = true` or `targetUidsCsv`) to connect with high-intent buyers before or after commenting.
           - Strictly NEVER export customer leads, phone numbers, or audit logs to external Excel/CSV files; manage all leads inside the application via CRM color tags (`fb_assign_lead_tag`, `fb_auto_tag_leads_by_rules`) and notes (`fb_update_lead_notes`).

        3. **Multi-Account Rotation & Anti-Checkpoint Safety**:
           - Call `fb_get_account_safety_quota` before large campaigns to check 1h/24h action counts and heat scores across accounts.
           - Pass `accountUidsCsv = "all"` (or comma-separated UIDs) to `fb_publish_campaign_batch` and `fb_auto_comment_leads_batch` to distribute posts/comments across multiple accounts in round-robin fashion.
           - When joining private groups via `fb_join_group`, inspect `pending_questions` and submit tailored answers via `fb_submit_group_answers`.

        4. **Group Safety**:
           - Never attempt to leave joined groups; use `fb_configure_target_groups_selection`, `fb_save_target_group` (`isEnabled = false`), or `fb_remove_target_group` if a group should be excluded from campaigns.
        """;

    [McpServerResource(UriTemplate = "guide://anti-checkpoint-playbook", Name = "anti-checkpoint-playbook", MimeType = "text/markdown")]
    [Description("Anti-checkpoint rate limits, multi-account rotation, and human-like warmup playbook.")]
    public static string AntiCheckpointPlaybook() =>
        """
        # Anti-Checkpoint & Multi-Account Rotation Playbook

        1. **Check Heat Score First (`fb_get_account_safety_quota`)**:
           - `SAFE` (Heat < 35): Account is cool and ready for group publishing, commenting, or joining.
           - `MODERATE` (Heat 35..79): Prefer rotating with other accounts via `accountUidsCsv = "all"`.
           - `COOLDOWN_RECOMMENDED` (Heat >= 80): Switch to `recommended_safest_account_uid` via `fb_switch_account` or run passive warmup (`fb_watch_and_reply_stories_batch`, `fb_get_notifications`).

        2. **Human-Like Warmup Before Heavy Campaigns**:
           - Run `fb_warmup_account` (1..3 feed reactions) + `fb_watch_and_reply_stories_batch` (3..5 stories) + `fb_handle_friend_requests_batch` (`confirm`) to establish natural session telemetry before batch group publishing.

        3. **Content Diversity**:
           - Always use Spintax (`{a|b|c}`) and dynamic variables (`{group_name}`, `{author_name}`, `{date}`, `{time}`, `{random_id}`) so no two GraphQL mutations carry identical text hashes.
        """;
}
