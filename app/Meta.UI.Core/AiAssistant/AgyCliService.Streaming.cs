// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Meta.UI.Core.Configuration;

namespace Meta.UI.Core.AiAssistant;

public sealed partial class AgyCliService
{
    /// <inheritdoc />
    public async Task WarmUpSessionAsync(
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        string? agyExe = ResolveAgyExecutablePath();
        if (string.IsNullOrWhiteSpace(agyExe) || !this.CheckIsAuthenticated())
        {
            return;
        }

        string effectiveModel = string.IsNullOrWhiteSpace(model) ? AppConfig.DefaultAiModel : model.Trim();
        string effectiveEffort = string.IsNullOrWhiteSpace(effort) ? AppConfig.DefaultAiReasoningEffort : effort.Trim().ToLowerInvariant();
        string? cleanConvId = string.IsNullOrWhiteSpace(agyConversationId) ? null : agyConversationId.Trim();

        await _warmSessionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (this.IsWarmProcessReusable(effectiveModel, effectiveEffort, cleanConvId))
            {
                return;
            }

            this.DisposeWarmProcessCore();
            _ = this.StartWarmProcessCore(
                agyExe,
                effectiveModel,
                effectiveEffort,
                cleanConvId,
                continueConversation: cleanConvId is not null);
        }
        finally
        {
            _ = _warmSessionGate.Release();
        }
    }

    /// <inheritdoc />
    public void ResetWarmSession()
    {
        this.DisposeWarmProcessCore();
        _hasCompletedAtLeastOneTurn = false;
    }

    /// <inheritdoc />
    public async Task<AgyPromptResult> SendPromptAsync(
        string userPrompt,
        string? systemContextPrompt = null,
        bool continueConversation = true,
        IProgress<string>? streamingProgress = null,
        string? model = null,
        string? effort = null,
        string? agyConversationId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Nội dung tin nhắn không được để trống.");
        }

        if (LooksLikeGoogleOAuthCodeOrCallbackUrl(userPrompt))
        {
            bool manualAuthOk = await this.SubmitManualOAuthCodeAsync(userPrompt, ct).ConfigureAwait(false);
            if (manualAuthOk)
            {
                return new AgyPromptResult(
                    IsSuccess: true,
                    ResponseText: "Đã xác thực Google Antigravity CLI thành công bằng mã OAuth vừa nhập! Bạn có thể tiếp tục trò chuyện ngay bây giờ.",
                    ProposedAction: null,
                    ErrorMessage: null);
            }
        }

        string? agyExe = ResolveAgyExecutablePath();
        if (string.IsNullOrWhiteSpace(agyExe))
        {
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Chưa tìm thấy Google Antigravity CLI (agy). Vui lòng cài đặt trước.");
        }

        if (!this.CheckIsAuthenticated())
        {
            this.DisposeWarmProcessCore();
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Phiên đăng nhập Google Antigravity CLI chưa được xác thực hoặc đã đăng xuất. Vui lòng đăng nhập Google OAuth để tiếp tục.");
        }

        string effectiveModel = string.IsNullOrWhiteSpace(model) ? AppConfig.DefaultAiModel : model.Trim();
        string effectiveEffort = string.IsNullOrWhiteSpace(effort) ? AppConfig.DefaultAiReasoningEffort : effort.Trim().ToLowerInvariant();
        string? requestedConvId = string.IsNullOrWhiteSpace(agyConversationId) ? null : agyConversationId.Trim();

        await _warmSessionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool reusedWarm = continueConversation &&
                                  attempt == 0 &&
                                  this.IsWarmProcessReusable(effectiveModel, effectiveEffort, requestedConvId);

                if (!reusedWarm)
                {
                    this.DisposeWarmProcessCore();
                    if (!continueConversation)
                    {
                        _hasCompletedAtLeastOneTurn = false;
                    }

                    if (!this.StartWarmProcessCore(
                            agyExe,
                            effectiveModel,
                            effectiveEffort,
                            requestedConvId,
                            continueConversation))
                    {
                        return new AgyPromptResult(
                            IsSuccess: false,
                            ResponseText: string.Empty,
                            ProposedAction: null,
                            ErrorMessage: "Không thể khởi chạy tiến trình agy.");
                    }
                }

                Process activeProcess = _warmProcess!;
                string normalizedSysPrompt = systemContextPrompt?.Trim() ?? string.Empty;
                bool systemPromptChanged = !string.Equals(_warmLastSystemPrompt, normalizedSysPrompt, StringComparison.Ordinal);

                string turnPrompt;
                if (!_warmSessionHasSystemPrompt)
                {
                    turnPrompt = BuildCompositePrompt(userPrompt.Trim(), systemContextPrompt);
                }
                else if (systemPromptChanged && !string.IsNullOrWhiteSpace(normalizedSysPrompt))
                {
                    turnPrompt = BuildUpdatedContextFollowUpPrompt(userPrompt.Trim(), normalizedSysPrompt);
                }
                else
                {
                    turnPrompt = BuildFollowUpTurnPrompt(userPrompt.Trim());
                }

                string jsonLine = BuildUserEventJsonLine(turnPrompt);

                try
                {
                    await activeProcess.StandardInput.WriteAsync((jsonLine + "\n").AsMemory(), ct).ConfigureAwait(false);
                    await activeProcess.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    this.DisposeWarmProcessCore();
                    if (reusedWarm && attempt == 0)
                    {
                        continue;
                    }

                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: $"Lỗi kết nối tiến trình agy: {ex.Message}");
                }

                StringBuilder responseBuilder = new();
                StringBuilder plainTextFallback = new();
                string? detectedConvId = _warmConversationId ?? requestedConvId;
                string? streamError = null;
                bool receivedAnyLine = false;

                while (!ct.IsCancellationRequested)
                {
                    string? line = await activeProcess.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                    {
                        continue;
                    }

                    receivedAnyLine = true;
                    if (!trimmedLine.StartsWith('{'))
                    {
                        if (plainTextFallback.Length > 0)
                        {
                            _ = plainTextFallback.AppendLine();
                        }

                        _ = plainTextFallback.Append(trimmedLine);
                        streamingProgress?.Report(StripActionTagPreview(plainTextFallback.ToString()));
                        continue;
                    }

                    (bool isTurnComplete, string? convId, string? textDelta, string? finalResponse, string? errorMsg) =
                        TryParseStreamJsonLine(trimmedLine);

                    if (!string.IsNullOrWhiteSpace(convId))
                    {
                        detectedConvId = convId;
                        _warmConversationId = convId;
                    }

                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        _ = responseBuilder.Append(textDelta);
                        streamingProgress?.Report(StripActionTagPreview(responseBuilder.ToString()));
                    }

                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        streamError = errorMsg;
                    }

                    if (isTurnComplete)
                    {
                        if (responseBuilder.Length == 0 && !string.IsNullOrWhiteSpace(finalResponse))
                        {
                            _ = responseBuilder.Append(finalResponse);
                            streamingProgress?.Report(StripActionTagPreview(responseBuilder.ToString()));
                        }

                        break;
                    }
                }

                if (!receivedAnyLine && reusedWarm && attempt == 0 && activeProcess.HasExited)
                {
                    this.DisposeWarmProcessCore();
                    continue;
                }

                string rawOutput = responseBuilder.Length > 0
                    ? responseBuilder.ToString().Trim()
                    : plainTextFallback.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(streamError) && string.IsNullOrWhiteSpace(rawOutput))
                {
                    this.DisposeWarmProcessCore();
                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: streamError,
                        ConversationId: detectedConvId);
                }

                if (string.IsNullOrWhiteSpace(rawOutput) && activeProcess.HasExited && activeProcess.ExitCode != 0)
                {
                    string rawError = string.Empty;
                    StringBuilder? errBuf = _warmStderrBuffer;
                    if (errBuf is not null)
                    {
                        lock (errBuf)
                        {
                            rawError = errBuf.ToString().Trim();
                        }
                    }

                    string errMsg = !string.IsNullOrWhiteSpace(rawError)
                        ? rawError
                        : $"Tiến trình agy kết thúc với mã lỗi {activeProcess.ExitCode}.";
                    this.DisposeWarmProcessCore();
                    return new AgyPromptResult(
                        IsSuccess: false,
                        ResponseText: string.Empty,
                        ProposedAction: null,
                        ErrorMessage: errMsg,
                        ConversationId: detectedConvId);
                }

                _warmSessionHasSystemPrompt = true;
                _warmLastSystemPrompt = normalizedSysPrompt;
                _hasCompletedAtLeastOneTurn = true;

                (string cleanText, AgyProposedAction? proposedAction, IReadOnlyList<AgyLearnedMemoryItem> learnedMemories) =
                    ParseResponseActionAndMemories(rawOutput);

                return new AgyPromptResult(
                    IsSuccess: true,
                    ResponseText: cleanText,
                    ProposedAction: proposedAction,
                    ErrorMessage: null,
                    ConversationId: detectedConvId,
                    LearnedMemories: learnedMemories);
            }

            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: "Không thể gửi yêu cầu tới tiến trình agy.");
        }
        catch (OperationCanceledException)
        {
            this.DisposeWarmProcessCore();
            throw;
        }
        catch (Exception ex)
        {
            this.DisposeWarmProcessCore();
            return new AgyPromptResult(
                IsSuccess: false,
                ResponseText: string.Empty,
                ProposedAction: null,
                ErrorMessage: $"Lỗi khi gọi Google Antigravity CLI: {ex.Message}");
        }
        finally
        {
            _ = _warmSessionGate.Release();
        }
    }

    /// <summary>
    /// Parses a single NDJSON line emitted by <c>agy --output-format stream-json</c>.
    /// </summary>
    public static (bool IsTurnComplete, string? ConversationId, string? TextDelta, string? FinalResponse, string? ErrorMessage) TryParseStreamJsonLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return (false, null, null, null, null);
        }

        string trimmed = line.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return (false, null, null, null, null);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(trimmed);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, null, null, null, null);
            }

            string? ev = TryGetStringProperty(root, "event");
            string? topConvId = TryGetStringProperty(root, "conversation_id");

            if (string.Equals(ev, "init", StringComparison.OrdinalIgnoreCase))
            {
                return (false, topConvId, null, null, null);
            }

            if (string.Equals(ev, "step_update", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("step_update", out JsonElement stepEl) &&
                    stepEl.ValueKind == JsonValueKind.Object)
                {
                    string? convId = TryGetStringProperty(stepEl, "conversation_id") ?? topConvId;
                    string? stepType = TryGetStringProperty(stepEl, "step_type");
                    string? textDelta = TryGetStringProperty(stepEl, "text_delta");

                    if (string.IsNullOrEmpty(stepType) ||
                        string.Equals(stepType, "agent_response", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, convId, textDelta, null, null);
                    }

                    return (false, convId, null, null, null);
                }

                return (false, topConvId, null, null, null);
            }

            if (string.Equals(ev, "result", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("result", out JsonElement resEl) &&
                    resEl.ValueKind == JsonValueKind.Object)
                {
                    string? convId = TryGetStringProperty(resEl, "conversation_id") ?? topConvId;
                    string? status = TryGetStringProperty(resEl, "status");
                    string? response = TryGetStringProperty(resEl, "response");
                    string? err = TryGetStringProperty(resEl, "error") ?? TryGetStringProperty(resEl, "error_message");

                    if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, convId, null, response, !string.IsNullOrWhiteSpace(err) ? err : "Yêu cầu AI thất bại.");
                    }

                    return (true, convId, null, response, null);
                }

                return (true, topConvId, null, null, null);
            }

            if (string.Equals(ev, "error", StringComparison.OrdinalIgnoreCase))
            {
                string? errMsg = TryGetStringProperty(root, "message") ??
                                 TryGetStringProperty(root, "error") ??
                                 "Lỗi từ tiến trình agy.";
                return (true, topConvId, null, null, errMsg);
            }

            return (false, topConvId, null, null, null);
        }
        catch (JsonException)
        {
            return (false, null, null, null, null);
        }
    }

    /// <summary>
    /// Parses an AI response string, extracting and stripping any silent <c>[LEARN:Category|Key|Content]</c> tags
    /// and trailing <c>[ACTION:...]</c> tag into structured objects so neither tag is ever visible to the user.
    /// </summary>
    public static (string CleanText, AgyProposedAction? ProposedAction, IReadOnlyList<AgyLearnedMemoryItem> LearnedMemories) ParseResponseActionAndMemories(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (string.Empty, null, Array.Empty<AgyLearnedMemoryItem>());
        }

        List<AgyLearnedMemoryItem> learned = new();
        MatchCollection learnMatches = LearnTagRegex().Matches(rawResponse);
        for (int i = 0; i < learnMatches.Count; i++)
        {
            Match m = learnMatches[i];
            if (!m.Success)
            {
                continue;
            }

            string catRaw = m.Groups["category"].Value.Trim();
            string key = m.Groups["key"].Value.Trim();
            string content = m.Groups["content"].Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (!Enum.TryParse<AiMemoryCategory>(catRaw, ignoreCase: true, out AiMemoryCategory category))
            {
                category = AiMemoryCategory.General;
            }

            learned.Add(new AgyLearnedMemoryItem(category, key, content, Importance: 4));
        }

        string withoutLearnTags = learnMatches.Count > 0
            ? LearnTagRegex().Replace(rawResponse, string.Empty)
            : rawResponse;

        (string cleanText, AgyProposedAction? proposedAction) = ParseResponseAndProposedAction(withoutLearnTags);
        return (cleanText, proposedAction, learned);
    }

    /// <summary>
    /// Parses an AI response string, extracting and stripping any trailing <c>[ACTION:...]</c> tag into an <see cref="AgyProposedAction"/>.
    /// </summary>
    public static (string CleanText, AgyProposedAction? ProposedAction) ParseResponseAndProposedAction(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (string.Empty, null);
        }

        string strippedLearn = LearnTagRegex().Replace(rawResponse, string.Empty);
        Match match = ActionTagRegex().Match(strippedLearn);
        if (!match.Success)
        {
            Match naturalGroupMatch = NaturalPublishGroupsRegex().Match(strippedLearn);
            if (naturalGroupMatch.Success &&
                int.TryParse(naturalGroupMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int naturalCount) &&
                naturalCount > 0)
            {
                int clampedNaturalCount = Math.Clamp(naturalCount, 1, 500);
                AgyProposedAction fallbackAction = new(
                    ActionKind: "PUBLISH_GROUPS",
                    SummaryText: FirstLine(strippedLearn.Trim()),
                    ContentText: strippedLearn.Trim(),
                    TargetGroupCount: clampedNaturalCount,
                    MinDelaySeconds: 2,
                    MaxDelaySeconds: 5,
                    GroupKeyword: null,
                    TargetId: null);
                return (strippedLearn.Trim(), fallbackAction);
            }

            return (strippedLearn.Trim(), null);
        }

        string actionKind = match.Groups["kind"].Value.Trim().ToUpperInvariant();
        string paramsRaw = match.Groups["params"].Value;

        int groupCount = 0;
        int minDelay = 2;
        int maxDelay = 5;
        string? content = null;
        string? keyword = null;
        string? targetId = null;

        List<string> pairs = SplitActionTagParameters(paramsRaw);
        for (int i = 0; i < pairs.Count; i++)
        {
            string pair = pairs[i];
            int eqIndex = pair.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex <= 0)
            {
                continue;
            }

            string key = pair[..eqIndex].Trim();
            string val = pair[(eqIndex + 1)..].Trim();

            if (key.Equals("count", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount))
            {
                groupCount = Math.Clamp(parsedCount, 1, 500);
            }
            else if (key.Equals("minDelay", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMin))
            {
                minDelay = Math.Clamp(parsedMin, 0, 3600);
            }
            else if (key.Equals("maxDelay", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
            {
                maxDelay = Math.Clamp(parsedMax, 0, 3600);
            }
            else if (key.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                content = val;
            }
            else if (key.Equals("keyword", StringComparison.OrdinalIgnoreCase))
            {
                keyword = val;
            }
            else if (key.Equals("targetId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("postId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("tagId", StringComparison.OrdinalIgnoreCase))
            {
                targetId = val;
            }
        }

        if (maxDelay < minDelay)
        {
            maxDelay = minDelay;
        }

        string cleanText = strippedLearn.Remove(match.Index, match.Length).Trim();
        string summaryText = !string.IsNullOrWhiteSpace(cleanText)
            ? FirstLine(cleanText)
            : actionKind switch
            {
                "PUBLISH_GROUPS" => $"Đăng bài lên {groupCount} nhóm (delay {minDelay}s - {maxDelay}s)",
                "SEARCH_GROUPS" => $"Tìm kiếm nhóm theo từ khóa '{keyword}'",
                "SCAN_LEADS" => $"Quét khách hàng tiềm năng theo từ khóa '{keyword}'",
                "FILTER_LEADS" => $"Lọc danh sách khách hàng trên bảng ({keyword ?? targetId})",
                "COMMENT_LEAD" => $"Gửi bình luận tư vấn cho bài {targetId}",
                "OPEN_GROUP_TAGS" => "Mở Quản lý Nhãn màu Hội nhóm",
                "BUMP_POSTS" => "Đẩy Top các bài viết vừa đăng",
                _ => $"Thực thi tác vụ {actionKind}"
            };

        AgyProposedAction proposed = new(
            ActionKind: actionKind,
            SummaryText: summaryText,
            ContentText: content,
            TargetGroupCount: groupCount,
            MinDelaySeconds: minDelay,
            MaxDelaySeconds: maxDelay,
            GroupKeyword: keyword,
            TargetId: targetId);

        return (cleanText, proposed);
    }

    private static List<string> SplitActionTagParameters(string paramsRaw)
    {
        List<string> results = new();
        if (string.IsNullOrWhiteSpace(paramsRaw))
        {
            return results;
        }

        int braceDepth = 0;
        int segmentStart = 0;
        for (int i = 0; i < paramsRaw.Length; i++)
        {
            char c = paramsRaw[i];
            if (c == '{')
            {
                braceDepth++;
            }
            else if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (c == '|' && braceDepth == 0)
            {
                string segment = paramsRaw[segmentStart..i].Trim();
                if (!string.IsNullOrEmpty(segment))
                {
                    results.Add(segment);
                }

                segmentStart = i + 1;
            }
        }

        if (segmentStart < paramsRaw.Length)
        {
            string tail = paramsRaw[segmentStart..].Trim();
            if (!string.IsNullOrEmpty(tail))
            {
                results.Add(tail);
            }
        }

        return results;
    }

    private static string StripActionTagPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = ActionTagRegex().Replace(text, string.Empty);
        cleaned = LearnTagRegex().Replace(cleaned, string.Empty);

        int partialActionIdx = cleaned.LastIndexOf("[ACTION:", StringComparison.OrdinalIgnoreCase);
        if (partialActionIdx >= 0)
        {
            cleaned = cleaned[..partialActionIdx];
        }

        int partialLearnIdx = cleaned.LastIndexOf("[LEARN:", StringComparison.OrdinalIgnoreCase);
        if (partialLearnIdx >= 0)
        {
            cleaned = cleaned[..partialLearnIdx];
        }

        return cleaned.TrimEnd();
    }

    private static string BuildCompositePrompt(string userPrompt, string? systemContextPrompt)
    {
        StringBuilder sb = new();
        if (!string.IsNullOrWhiteSpace(systemContextPrompt))
        {
            string trimmedContext = systemContextPrompt.Trim();
            if (!trimmedContext.Contains("[SYSTEM PROMPT]", StringComparison.Ordinal))
            {
                _ = sb.AppendLine("[SYSTEM PROMPT]");
                _ = sb.AppendLine(AppConfig.DefaultAiSystemPrompt);
                _ = sb.AppendLine();
            }

            _ = sb.AppendLine(trimmedContext);
            _ = sb.AppendLine();
        }
        else
        {
            _ = sb.AppendLine("[SYSTEM PROMPT]");
            _ = sb.AppendLine(AppConfig.DefaultAiSystemPrompt);
            _ = sb.AppendLine();
        }

        _ = sb.AppendLine("[USER]");
        _ = sb.Append(userPrompt);
        return sb.ToString();
    }

    private static string BuildFollowUpTurnPrompt(string userPrompt) => userPrompt.Trim();

    private static string BuildUpdatedContextFollowUpPrompt(string userPrompt, string updatedSystemContextPrompt)
    {
        StringBuilder sb = new();
        int workspaceMarkerIdx = updatedSystemContextPrompt.IndexOf("[CURRENT WORKSPACE STATE]", StringComparison.Ordinal);
        if (workspaceMarkerIdx >= 0)
        {
            _ = sb.AppendLine(updatedSystemContextPrompt[workspaceMarkerIdx..].Trim());
        }
        else
        {
            _ = sb.AppendLine(updatedSystemContextPrompt.Trim());
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine("[USER]");
        _ = sb.Append(userPrompt.Trim());
        return sb.ToString();
    }

    private static string BuildUserEventJsonLine(string content)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("event", "user");
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("content", content);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return s_utf8NoBom.GetString(ms.ToArray());
    }

    private static string? TryGetStringProperty(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out JsonElement val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }

    private bool IsWarmProcessReusable(string effectiveModel, string effectiveEffort, string? requestedConvId)
    {
        if (_warmProcess is null)
        {
            return false;
        }

        try
        {
            if (_warmProcess.HasExited)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (!string.Equals(_warmModel, effectiveModel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_warmEffort, effectiveEffort, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedConvId) &&
            !string.IsNullOrWhiteSpace(_warmConversationId) &&
            !string.Equals(_warmConversationId, requestedConvId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool StartWarmProcessCore(
        string agyExe,
        string effectiveModel,
        string effectiveEffort,
        string? agyConversationId,
        bool continueConversation)
    {
        List<string> args =
        [
            "--input-format",
            "stream-json",
            "--output-format",
            "stream-json",
            "--dangerously-skip-permissions",
            "--disable-slash-commands",
            "--model",
            effectiveModel,
            "--effort",
            effectiveEffort,
            "-p="
        ];

        if (!string.IsNullOrWhiteSpace(agyConversationId))
        {
            args.Add("--conversation");
            args.Add(agyConversationId.Trim());
        }
        else if (continueConversation && _hasCompletedAtLeastOneTurn)
        {
            args.Add("-c");
        }

        ProcessStartInfo psi = CreateAgyProcessStartInfo(agyExe, args, redirectStandardInput: true);
        Process process = new() { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            process.Dispose();
            return false;
        }

        StringBuilder stderrBuffer = new();
        _warmProcess = process;
        _warmStderrBuffer = stderrBuffer;
        _warmModel = effectiveModel;
        _warmEffort = effectiveEffort;
        _warmConversationId = agyConversationId;
        _warmLastSystemPrompt = null;
        _warmSessionHasSystemPrompt = false;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    string? errLine = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (errLine is null)
                    {
                        break;
                    }

                    lock (stderrBuffer)
                    {
                        if (stderrBuffer.Length < 8192)
                        {
                            if (stderrBuffer.Length > 0)
                            {
                                _ = stderrBuffer.AppendLine();
                            }

                            _ = stderrBuffer.Append(errLine);
                        }
                    }

                    if (errLine.Contains("Authentication required", StringComparison.OrdinalIgnoreCase) ||
                        errLine.Contains("paste the authorization code", StringComparison.OrdinalIgnoreCase) ||
                        errLine.Contains("You are not logged into Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        _hasAuthenticatedInSession = false;
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or ObjectDisposedException)
                        {
                            // Ignore termination error
                        }

                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Ignore background stderr close
            }
        });

        return true;
    }

    private void DisposeWarmProcessCore()
    {
        Process? old;
        lock (_oauthProcessLock)
        {
            old = _warmProcess;
            _warmProcess = null;
        }

        _warmStderrBuffer = null;
        _warmModel = null;
        _warmEffort = null;
        _warmConversationId = null;
        _warmLastSystemPrompt = null;
        _warmSessionHasSystemPrompt = false;

        if (old is not null)
        {
            try
            {
                if (!old.HasExited)
                {
                    old.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or ObjectDisposedException)
            {
                // Ignore termination error
            }
            finally
            {
                old.Dispose();
            }
        }
    }

    [GeneratedRegex(@"\[ACTION:(?<kind>[A-Za-z0-9_]+)(?:\|(?<params>[^\]]*))?\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActionTagRegex();

    [GeneratedRegex(@"\[LEARN:(?<category>[A-Za-z0-9_]+)\|(?<key>[^\|\]\r\n]+)\|(?<content>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LearnTagRegex();

    [GeneratedRegex(@"(?:đăng(?:\s+bài)?\s+(?:lên|vào)|publish\s+to)\s+(?<count>\d+)\s+(?:nhóm|groups?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NaturalPublishGroupsRegex();
}
