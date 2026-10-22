// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Meta.UI.Core.Theming;

namespace Meta.UI.Components.Controls;

/// <summary>
/// Lightweight inline and fenced-code Markdown renderer for <see cref="SelectableTextBlock"/>.
/// Supports fenced code blocks (<c>```</c>), headings (<c>#</c>, <c>##</c>, <c>###</c>),
/// bullet lists (<c>- </c>, <c>* </c>), inline code (<c>`code`</c>), bold (<c>**bold**</c>),
/// and italic (<c>*italic*</c>) spans without external layout passes.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly FontFamily s_monospaceFont = new("Cascadia Code, Consolas, monospace");

    /// <summary>
    /// Renders Markdown text into the <see cref="TextBlock.Inlines"/> collection of <paramref name="targetTb"/>.
    /// </summary>
    /// <param name="targetTb">Target <see cref="SelectableTextBlock"/> control.</param>
    /// <param name="markdown">Raw Markdown text to render.</param>
    /// <param name="p">Active <see cref="ThemePalette"/> used for code span colors.</param>
    public static void Render(SelectableTextBlock targetTb, string? markdown, ThemePalette p)
    {
        ArgumentNullException.ThrowIfNull(targetTb);
        ArgumentNullException.ThrowIfNull(p);

        InlineCollection? inlines = targetTb.Inlines;
        if (inlines is null)
        {
            targetTb.Text = markdown ?? string.Empty;
            return;
        }

        targetTb.Text = null;
        inlines.Clear();

        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        bool inCodeBlock = false;
        bool hasRenderedLine = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string rawLine = lines[i];
            string trimmedStart = rawLine.TrimStart();

            if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (hasRenderedLine)
            {
                inlines.Add(new LineBreak());
            }

            hasRenderedLine = true;

            if (inCodeBlock)
            {
                inlines.Add(new Run(rawLine)
                {
                    FontFamily = s_monospaceFont,
                    Foreground = p.BrandPrimary,
                    FontSize = 11.5
                });
                continue;
            }

            if (trimmedStart.StartsWith("### ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[4..], p, isHeading: true);
                continue;
            }

            if (trimmedStart.StartsWith("## ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[3..], p, isHeading: true);
                continue;
            }

            if (trimmedStart.StartsWith("# ", StringComparison.Ordinal))
            {
                AppendInlineMarkdownRuns(inlines, trimmedStart[2..], p, isHeading: true);
                continue;
            }

            string lineToParse = rawLine;
            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("* ", StringComparison.Ordinal))
            {
                lineToParse = string.Concat("• ", trimmedStart[2..]);
            }

            AppendInlineMarkdownRuns(inlines, lineToParse, p, isHeading: false);
        }

        if (inlines.Count == 0 && !string.IsNullOrEmpty(markdown))
        {
            inlines.Add(new Run(markdown));
        }
    }

    private static void AppendInlineMarkdownRuns(
        InlineCollection inlines,
        string line,
        ThemePalette p,
        bool isHeading)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        int idx = 0;
        while (idx < line.Length)
        {
            int nextCode = FindValidSpanStart(line, idx, "`", "`", out int codeEnd);
            int nextBold = FindValidSpanStart(line, idx, "**", "**", out int boldEnd);
            int nextItalic = FindValidSingleAsteriskStart(line, idx, out int italicEnd);

            int earliestPos = int.MaxValue;
            int kind = 0; // 1 = code, 2 = bold, 3 = italic
            int spanEnd = -1;

            if (nextCode >= 0 && nextCode < earliestPos)
            {
                earliestPos = nextCode;
                kind = 1;
                spanEnd = codeEnd;
            }

            if (nextBold >= 0 && nextBold < earliestPos)
            {
                earliestPos = nextBold;
                kind = 2;
                spanEnd = boldEnd;
            }

            if (nextItalic >= 0 && nextItalic < earliestPos)
            {
                earliestPos = nextItalic;
                kind = 3;
                spanEnd = italicEnd;
            }

            if (kind == 0)
            {
                AddPlainRun(inlines, line[idx..], isHeading);
                break;
            }

            if (earliestPos > idx)
            {
                AddPlainRun(inlines, line[idx..earliestPos], isHeading);
            }

            if (kind == 1)
            {
                string inner = line[(earliestPos + 1)..spanEnd];
                Run codeRun = new(inner)
                {
                    FontFamily = s_monospaceFont,
                    Foreground = p.BrandPrimary,
                    FontWeight = FontWeight.SemiBold
                };
                if (isHeading)
                {
                    codeRun.FontSize = 13.5;
                }

                inlines.Add(codeRun);
                idx = spanEnd + 1;
            }
            else if (kind == 2)
            {
                string inner = line[(earliestPos + 2)..spanEnd];
                Run boldRun = new(inner)
                {
                    FontWeight = FontWeight.Bold
                };
                if (isHeading)
                {
                    boldRun.FontSize = 13.5;
                }

                inlines.Add(boldRun);
                idx = spanEnd + 2;
            }
            else
            {
                string inner = line[(earliestPos + 1)..spanEnd];
                Run italicRun = new(inner)
                {
                    FontStyle = FontStyle.Italic
                };
                if (isHeading)
                {
                    italicRun.FontWeight = FontWeight.Bold;
                    italicRun.FontSize = 13.5;
                }

                inlines.Add(italicRun);
                idx = spanEnd + 1;
            }
        }
    }

    private static void AddPlainRun(InlineCollection inlines, string text, bool isHeading)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Run run = new(text);
        if (isHeading)
        {
            run.FontWeight = FontWeight.Bold;
            run.FontSize = 13.5;
        }

        inlines.Add(run);
    }

    private static int FindValidSpanStart(
        string text,
        int startIndex,
        string openDelimiter,
        string closeDelimiter,
        out int closePos)
    {
        int openPos = text.IndexOf(openDelimiter, startIndex, StringComparison.Ordinal);
        while (openPos >= 0)
        {
            int contentStart = openPos + openDelimiter.Length;
            if (contentStart < text.Length)
            {
                int foundClose = text.IndexOf(closeDelimiter, contentStart, StringComparison.Ordinal);
                if (foundClose > contentStart)
                {
                    closePos = foundClose;
                    return openPos;
                }
            }

            openPos = text.IndexOf(openDelimiter, openPos + 1, StringComparison.Ordinal);
        }

        closePos = -1;
        return -1;
    }

    private static int FindValidSingleAsteriskStart(string text, int startIndex, out int closePos)
    {
        int i = startIndex;
        while (i < text.Length)
        {
            int starPos = text.IndexOf('*', i);
            if (starPos < 0)
            {
                break;
            }

            bool isDouble = (starPos + 1 < text.Length && text[starPos + 1] == '*') ||
                            (starPos > 0 && text[starPos - 1] == '*');
            if (isDouble || starPos + 1 >= text.Length || char.IsWhiteSpace(text[starPos + 1]))
            {
                i = starPos + 1;
                continue;
            }

            int searchClose = starPos + 1;
            while (searchClose < text.Length)
            {
                int endStar = text.IndexOf('*', searchClose);
                if (endStar < 0)
                {
                    break;
                }

                bool endIsDouble = (endStar + 1 < text.Length && text[endStar + 1] == '*') ||
                                   (endStar > 0 && text[endStar - 1] == '*');
                if (!endIsDouble && !char.IsWhiteSpace(text[endStar - 1]))
                {
                    closePos = endStar;
                    return starPos;
                }

                searchClose = endStar + 1;
            }

            i = starPos + 1;
        }

        closePos = -1;
        return -1;
    }
}
