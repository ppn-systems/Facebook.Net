// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp;

/// <summary>
/// Preset MCP Prompt templates for buyer-intent lead analysis, Spintax campaign drafting, competitor post hijacking,
/// multi-step account warmup, and automated group joining with approval question answering.
/// </summary>
[McpServerPromptType]
public sealed class Prompts
{
    [McpServerPrompt(Name = "analyze_group_leads")]
    [Description("Generates a prompt template to find high-intent buyer posts in scraped Facebook groups and suggest tailored sales comments.")]
    public static GetPromptResult AnalyzeGroupLeads(
        [Description("Comma-separated buyer keywords to search (e.g. 'căn hộ, tìm mua, báo giá')")] string keywords,
        [Description("Language for the report (default 'vi')")] string language = "vi")
    {
        return new GetPromptResult
        {
            Description = $"Analyze Facebook group leads matching '{keywords}'",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            1. Call `fb_search_leads_advanced` (with keywords = "{keywords}" and commentStatus = "pending") and `fb_analyze_workspace_data` (with buyerKeywords = "{keywords}").
                            2. Review the highest-scoring buyer-intent posts (including extracted Vietnamese phone numbers and zero-comment first-mover opportunities) and present a concise summary in {language}:
                               - Author name, Group name, Buyer-Intent Score, Extracted Phones, and key inquiry details.
                               - Offer to auto-tag high-value leads in-app via `fb_auto_tag_leads_by_rules`, save notes via `fb_update_lead_notes`, or send friend requests via `fb_send_friend_requests_batch` (strictly NEVER export leads to external Excel/CSV files).
                               - Draft a natural, helpful sales comment (with Spintax variations) ready to send via `fb_create_comment` or `fb_auto_comment_leads_batch`.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "compose_spintax_campaign")]
    [Description("Generates a prompt template to craft an anti-checkpoint Spintax post and comment template for Facebook group campaigns.")]
    public static GetPromptResult ComposeSpintaxCampaign(
        [Description("Product, service, or topic to promote")] string topic,
        [Description("Tone of voice (e.g. 'Professional', 'Friendly', 'Storytelling')")] string tone = "Friendly")
    {
        return new GetPromptResult
        {
            Description = $"Compose Spintax campaign for {topic}",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $$"""
                            Please create a Facebook community group campaign for the following topic:
                            "{{topic}}"

                            Requirements:
                            - Tone: {{tone}}
                            - Use valid Spintax syntax (`{option1|option2|option3}`) for greetings, call-to-actions, and key phrases so every group post is unique.
                            - Provide 1 main post template and 1 short bump/sales comment template (supporting `{group_name}`, `{date}`, `{time}`, `{random_id}`), and offer to save them via `fb_save_template`, check account safety quotas via `fb_get_account_safety_quota`, or publish with multi-account rotation via `fb_publish_campaign_batch`.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "competitor_post_hijack")]
    [Description("Generates a workflow prompt to inspect a competitor's high-engagement Facebook post, extract commenters and reactors, and convert them into warm leads/friends.")]
    public static GetPromptResult CompetitorPostHijack(
        [Description("Facebook Post ID of the competitor or viral post")] string postId)
    {
        return new GetPromptResult
        {
            Description = $"Extract commenters and reactors from post '{postId}'",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            1. Call `fb_get_single_post` (with postId = "{postId}", includeEngagementUsers = true), `fb_get_post_commenters` (postId = "{postId}"), and `fb_get_post_reactors` (postId = "{postId}").
                            2. Analyze the post content and list the most promising prospects who commented or reacted.
                            3. Offer to send batch friend requests to the commenters/reactors via `fb_send_friend_requests_batch` or post a helpful Spintax comment on the post via `fb_create_comment`.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "account_warmup_routine")]
    [Description("Generates a natural human-like account warmup workflow combining News Feed reactions, Story viewing/replying, friend request confirmation, and notification checks.")]
    public static GetPromptResult AccountWarmupRoutine(
        [Description("Optional target Facebook account UID (leave empty for active account)")] string accountUid = "")
    {
        return new GetPromptResult
        {
            Description = "Execute full human-like Facebook account warmup routine",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $$"""
                            Execute a natural, anti-checkpoint warmup routine for account "{{accountUid}}":
                            1. Call `fb_get_account_safety_quota` to inspect recent activity heat score.
                            2. Call `fb_warmup_account` (maxReactions = 2) to browse the News Feed and react naturally.
                            3. Call `fb_watch_and_reply_stories_batch` (maxStoriesToWatch = 5, replySpintax = "{❤️|🔥|😍}") to watch active Stories.
                            4. Call `fb_handle_friend_requests_batch` (action = "confirm", maxCount = 3) to accept incoming friend requests.
                            5. Call `fb_get_notifications` (onlyUnread = true) to check if any customer replied to our posts or comments.
                            Summarize all warmup actions performed clearly in Vietnamese.
                            """
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "auto_join_groups_with_answers")]
    [Description("Generates a workflow prompt to search and join community groups by keyword and automatically answer pending membership approval questionnaires.")]
    public static GetPromptResult AutoJoinGroupsWithAnswers(
        [Description("Keyword to search community groups (e.g. 'bất động sản hà nội')")] string keyword,
        [Description("Maximum number of groups to join (default 5)")] int maxGroups = 5)
    {
        return new GetPromptResult
        {
            Description = $"Search, join, and auto-answer approval questions for '{keyword}' groups",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"""
                            1. Call `fb_search_groups` with keyword = "{keyword}" to find relevant unjoined groups.
                            2. For up to {maxGroups} unjoined groups, call `fb_join_group` (with fetchQuestionsIfRequested = true).
                            3. Whenever a group returns `pending_questions` (membership_state = "Requested"), craft polite, rule-abiding Vietnamese answers tailored to each question and submit them immediately using `fb_submit_group_answers`.
                            4. Report the final join status and submitted answers for each group.
                            """
                    }
                }
            ]
        };
    }
}
