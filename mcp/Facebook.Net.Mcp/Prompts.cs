// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Facebook.Net.Mcp;

/// <summary>
/// Preset MCP Prompt templates for buyer-intent lead analysis, Spintax campaign drafting, and audit summarization.
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
                            Call the `fb_query_scraped_leads` tool with keywords = "{keywords}" and commentStatus = "pending".
                            Review the highest-scoring buyer-intent posts and present a concise summary in {language}:
                            1. Author name, Group name, Buyer-Intent Score, and key inquiry details.
                            2. A natural, helpful sales comment draft (with Spintax variations) ready to send via `fb_create_comment`.
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
                            - Provide 1 main post template and 1 short bump/sales comment template, and offer to save them via `fb_save_template`.
                            """
                    }
                }
            ]
        };
    }
}
