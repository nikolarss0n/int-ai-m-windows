using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// LLM backend for interview classify/answer/analysis.
/// Historically named AnthropicClient; now defaults to xAI Grok (OpenAI-compatible chat API).
public enum InterviewLLMProvider
{
    Xai,
    Anthropic,

    /// OpenAI chat completions (GPT); same wire format as xAI.
    OpenAI
}

public class AnthropicClient : IDisposable
{
    /// Classification result for an utterance.
    /// <param name="Status">"question", "incomplete", "statement" (or "answer" for backwards compat), "filler"</param>
    /// <param name="Topic">topic name or null</param>
    public sealed record UtteranceClassification(string Status, string? Topic);

    public sealed record SessionAnalysisResult(
        IReadOnlyList<string> Topics, IReadOnlyList<string> Strengths, IReadOnlyList<string> Weaknesses, IReadOnlyList<string> Insights);

    public JevDecisionClient? JevDecisionClient { get; set; }

    private const string ClientDomain = "InterviewLLMClient";
    private const double RequestTimeoutSeconds = 30;

    private readonly string apiKey;
    private readonly InterviewLLMProvider provider;
    private readonly string baseUrl;
    private readonly string model;
    private readonly int imageMaxTokens = AppConstants.MaxTokens.ImageAnalysis;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly AppSettings? injectedSettings;
    private bool isConnectionWarm;

    private AppSettings Settings => injectedSettings ?? AppSettings.Shared;

    /// <param name="provider">`Xai` (default) uses Grok via `api.x.ai`; `Anthropic` keeps Claude.</param>
    public AnthropicClient(
        string apiKey, InterviewLLMProvider provider = InterviewLLMProvider.Xai, HttpClient? client = null, AppSettings? settings = null)
    {
        this.apiKey = apiKey;
        this.provider = provider;
        injectedSettings = settings;
        ownsClient = client is null;
        this.client = client ?? ApiHttp.CreateClient(maxConnectionsPerHost: 2);
        switch (provider)
        {
            case InterviewLLMProvider.Xai:
                baseUrl = AppConstants.ApiUrls.XaiChat;
                model = AppConstants.Models.XaiGrok;
                break;
            case InterviewLLMProvider.OpenAI:
                baseUrl = AppConstants.ApiUrls.OpenAIChat;
                model = Settings.OpenAIAnswerModel;
                break;
            default:
                baseUrl = AppConstants.ApiUrls.AnthropicMessages;
                model = AppConstants.Models.AnthropicHaiku;
                break;
        }
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
        GC.SuppressFinalize(this);
    }

    /// Requests are cancelled through the token each call takes; kept so callers can signal intent.
    public virtual void CancelCurrentRequest()
    {
    }

    private bool UsesOpenAIWireFormat => provider != InterviewLLMProvider.Anthropic;

    /// GPT-5 models reject `max_tokens` and custom temperature, and take a reasoning effort.
    private JsonObject ProviderAdjustedBody(JsonObject body)
    {
        if (provider != InterviewLLMProvider.OpenAI) return body;
        var maxTokens = body["max_tokens"];
        body.Remove("max_tokens");
        if (maxTokens is JsonValue value && value.TryGetValue<int>(out var tokens))
        {
            body["max_completion_tokens"] = tokens + AppConstants.MaxTokens.OpenAIReasoningAllowance;
        }
        body.Remove("temperature");
        body["reasoning_effort"] = Settings.OpenAIReasoningEffort;
        if (Settings.OpenAIPriority)
        {
            // Priority processing: same model, ~40% faster first text, higher per-token price.
            body["service_tier"] = "priority";
        }
        return body;
    }

    private HttpRequestMessage CreateRequest(byte[] body, bool promptCaching = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (UsesOpenAIWireFormat)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            if (promptCaching) request.Headers.TryAddWithoutValidation("anthropic-beta", "prompt-caching-2024-07-31");
        }
        return request;
    }

    internal static string ExtractErrorMessage(string body, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? "";
                }
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }
        return body.Length == 0 ? $"HTTP {statusCode}" : $"HTTP {statusCode}: {body.Prefix(300)}";
    }

    private static string OpenAIMessageText(byte[] data)
    {
        using var document = StrictJson.ParseObject(data);
        return StrictJson.ChatChoices(document.RootElement, choicesRequired: true, contentRequired: false).FirstContent?.Trim() ?? "";
    }

    private static string AnthropicMessageText(byte[] data)
    {
        using var document = StrictJson.ParseObject(data);
        string? first = null;
        var index = 0;
        foreach (var block in StrictJson.Required(document.RootElement, "content", JsonValueKind.Array).EnumerateArray())
        {
            var text = StrictJson.RequiredString(block, "text");
            if (index++ == 0) first = text;
        }
        return first?.Trim() ?? "";
    }

    /// Pre-warm the connection (DNS + TCP + TLS handshake)
    public virtual async Task WarmupConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (isConnectionWarm) return;

        var started = Stopwatch.StartNew();
        var host = provider switch
        {
            InterviewLLMProvider.Xai => "https://api.x.ai",
            InterviewLLMProvider.OpenAI => "https://api.openai.com",
            _ => "https://api.anthropic.com"
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, host);
            await ApiHttp.SendAsync(client, request, 5, cancellationToken).ConfigureAwait(false);
            isConnectionWarm = true;
            Log.Print($"🔥 {model} connection warmed up in {started.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}ms");
        }
        catch (Exception error)
        {
            Log.Print($"⚠️ Connection warmup failed (non-critical): {error.Message}");
        }
    }

    public virtual async Task<(string Text, double LatencyMs)> SendMessageAsync(
        string prompt, int maxTokens = 300, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var body = ApiHttp.Encode(ProviderAdjustedBody(new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt })
        }));

        return await ApiHttp.WithRetriesAsync(ClientDomain, "Request failed after retries", async () =>
        {
            using var request = CreateRequest(body);
            var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (status is >= 400 and < 500)
            {
                throw new ApiClientException(ClientDomain, status, ExtractErrorMessage(ApiHttp.Utf8(data) ?? "", status));
            }

            var latency = started.Elapsed.TotalMilliseconds;
            var text = UsesOpenAIWireFormat ? OpenAIMessageText(data) : AnthropicMessageText(data);
            return (text, latency);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// One streaming request with a single retry for network failures and 5xx replies.
    /// `beginAttempt` resets per-attempt state, `onText` returns false to stop reading,
    /// `endAttempt` runs when the stream finishes on its own.
    private async Task<Outcome> StreamAsync(
        JsonObject requestBody, bool promptCaching, string exhaustedDescription,
        Action? beginAttempt, Func<string, bool> onText, Action? endAttempt, CancellationToken cancellationToken)
    {
        var body = ApiHttp.Encode(ProviderAdjustedBody(requestBody));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = CreateRequest(body, promptCaching);
                using var response = await ApiHttp.OpenStreamAsync(client, request, RequestTimeoutSeconds, null, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is < 200 or > 299)
                {
                    var errorBody = new StringBuilder();
                    while (await response.ReadLineAsync().ConfigureAwait(false) is { } errorLine)
                    {
                        errorBody.Append(errorLine).Append('\n');
                        if (errorBody.Length > 500) break;
                    }
                    var errorMessage = ExtractErrorMessage(errorBody.ToString(), response.StatusCode);

                    if (response.StatusCode is >= 400 and < 500)
                    {
                        return Outcome.Failure(new ApiClientException(errorMessage, response.StatusCode));
                    }

                    if (attempt == 0)
                    {
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return Outcome.Failure(new ApiClientException(errorMessage, response.StatusCode));
                }

                beginAttempt?.Invoke();
                while (await response.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    var (done, json) = ApiHttp.ParseEventLine(line);
                    if (done) break;
                    if (json is not { } payload) continue;

                    var text = UsesOpenAIWireFormat ? ApiHttp.OpenAIDeltaText(payload) : ApiHttp.AnthropicDeltaText(payload);
                    if (text is null) continue;
                    if (!onText(text)) return Outcome.Success;
                }

                endAttempt?.Invoke();
                return Outcome.Success;
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException || !ApiHttp.IsNetworkError(error) || attempt == 1)
                {
                    return Outcome.Failure(error);
                }
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        return Outcome.Failure(new ApiClientException(ClientDomain, -1, exhaustedDescription));
    }

    public virtual Task<Outcome> StreamTextMessageAsync(
        string prompt, Action<string> onChunk, int maxTokens = 300, CancellationToken cancellationToken = default)
    {
        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt })
        };
        return StreamAsync(requestBody, false, "Stream request failed after retry", null, text =>
        {
            onChunk(text);
            return true;
        }, null, cancellationToken);
    }

    /// Static system prompt for classification (cached) - OPTIMIZED: topics come from settings
    internal static readonly string ClassificationSystemPrompt = $$"""
@@PROMPT@@
        """;

    /// Get topics based on current role and programming language settings
    internal static string TopicsForSettings(AppSettings settings)
    {
        const string common = "@@COMMON@@";

        // QA-specific topics
        const string qaTopics = "@@QA@@";
        const string aiTopics = "@@AI@@";

        // Language-specific topics
        var langTopics = settings.ProgrammingLanguage switch
        {
@@LANGS@@
        };

        // Add QA topics if role is QA-related
        if (settings.IsTestAutomationRole) return $"{qaTopics}, {langTopics}, {common}";
        if (settings.IsAIOrDataRole) return $"{aiTopics}, {langTopics}, {common}";
        return $"{langTopics}, {common}";
    }

    /// Combined classify + answer in ONE streaming call with PROMPT CACHING
    /// Uses multi-turn message format for better context handling
    /// Returns classification immediately, then streams answer if status is "question"
    public virtual async Task<Outcome> ClassifyAndStreamAnswerAsync(
        string transcription,
        string buffer,
        string? lastTopic,
        string? userBackground,
        IReadOnlyList<Dictionary<string, string>> multiTurnMessages,
        Action<UtteranceClassification> onClassification,
        Action<string> onAnswerChunk,
        bool jevMaySuppressAnswer = false,
        CancellationToken cancellationToken = default)
    {
        var combinedText = buffer.Length == 0 ? transcription : $"{buffer} {transcription}";
        UtteranceClassification? jevClassification = null;
        if (JevDecisionClient is { } jev)
        {
            var started = Stopwatch.StartNew();
            try
            {
                var status = await jev.ClassifyAsync(transcription, buffer, lastTopic, multiTurnMessages, cancellationToken).ConfigureAwait(false);
                if (status is { } accepted)
                {
                    jevClassification = AdjustedClassification(new UtteranceClassification(accepted.RawValue(), null), combinedText);
                }
                DebugLogger.Shared.Log(
                    LogCategory.Classification,
                    $"Jev decision_ms={started.ElapsedMilliseconds} accepted={(jevClassification is not null ? "true" : "false")}");
            }
            catch (Exception)
            {
                // Never log response bodies, transcripts or credentials from this provider.
                DebugLogger.Shared.Log(LogCategory.Classification, "Jev unavailable; using combined classifier");
            }
            if (cancellationToken.IsCancellationRequested) return Outcome.Failure(new OperationCanceledException(cancellationToken));
        }
        if (!jevMaySuppressAnswer && jevClassification?.Status != "question") jevClassification = null;
        if (jevClassification is { } decision && decision.Status != "question")
        {
            onClassification(decision);
            return Outcome.Success;
        }
        var systemPrompt = jevClassification is null
            ? ClassificationSystemPrompt
            : "Answer the interview question directly. Return only answer bullets, without status/topic headers or separators.\n"
              + ClassificationSystemPrompt.Split("=== STYLE ===")[1].Split("=== EXAMPLE ===")[0];

        // Build simple user message - just the utterance and topic hints
        var settings = Settings;
        var userParts = new List<string>
        {
            $"Q: \"{combinedText}\"",
            $"TOPICS: {TopicsForSettings(settings)}",
            settings.InterviewContext,
            settings.AnswerStyleInstruction
        };
        // Context only: a stale topic must not pull an unrelated new question toward the last subject.
        if (lastTopic is not null) userParts.Add($"Previous topic (context only, answer the question actually asked): {lastTopic}");
        var grounding = Briefing.ResumeGroundingInstruction(
            userBackground ?? "",
            combinedText,
            lastTopic ?? "",
            string.Join("\n", multiTurnMessages.Where(message => message.ContainsKey("content")).Select(message => message["content"])));
        if (grounding.Length > 0) userParts.Add(grounding);

        var languageInstruction = settings.LlmLanguageInstruction;
        if (languageInstruction.Length > 0) userParts.Add(languageInstruction);

        var currentUserMessage = string.Join("\n", userParts);

        // Build messages array: multi-turn history + current utterance
        var history = multiTurnMessages.Cast<IReadOnlyDictionary<string, string>>().ToList();
        var current = new Dictionary<string, string> { ["role"] = "user", ["content"] = currentUserMessage };
        if (history.Count == 0 || history[^1].GetValueOrDefault("role") != "user")
        {
            history.Add(current);
        }
        else
        {
            // Merge with existing last user message if it's contextual
            history[^1] = current;
        }
        var messages = ApiHttp.MessageArray(history);

        JsonObject requestBody;
        if (UsesOpenAIWireFormat)
        {
            // OpenAI-compatible: system as a message role
            messages.Insert(0, new JsonObject { ["role"] = "system", ["content"] = systemPrompt });
            requestBody = new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = AppConstants.MaxTokens.Classification,
                ["stream"] = true,
                ["temperature"] = 0.2,
                ["messages"] = messages
            };
        }
        else
        {
            requestBody = new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = AppConstants.MaxTokens.Classification,
                ["stream"] = true,
                ["system"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = systemPrompt,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }),
                ["messages"] = messages
            };
        }

        if (jevClassification is { } announced) onClassification(announced);

        var fullText = new StringBuilder();
        var classificationSent = jevClassification is not null;
        var delivered = 0;
        var headerless = jevClassification is not null;

        // Sends whatever the parser can now release: the classification once, then answer text as it grows.
        // Returns false when the reply is not an answer (statement, filler, incomplete).
        bool Release(bool streamEnded)
        {
            var parsed = ClassifierStreamParser.ParseReply(fullText.ToString(), headerless, streamEnded);
            if (!classificationSent)
            {
                if (parsed.StatusLine is { } statusLine)
                {
                    var classification = AdjustedClassification(ParseClassification(statusLine), combinedText);
                    classificationSent = true;
                    onClassification(classification);
                    if (classification.Status != "question") return false;
                }
                else if (parsed.FormatDrift)
                {
                    // The model skipped the STATUS header. Treat it as a question and keep its answer.
                    DebugLogger.Shared.Log(LogCategory.Classification, "Classifier reply had no STATUS header; using it as the answer");
                    classificationSent = true;
                    onClassification(AdjustedClassification(new UtteranceClassification("question", "unknown"), combinedText));
                }
            }
            if (!classificationSent) return true;
            var available = parsed.Answer.Length;
            if (!streamEnded && available > 0 && char.IsHighSurrogate(parsed.Answer[available - 1])) available--;
            if (available > delivered)
            {
                var newText = parsed.Answer.Substring(delivered, available - delivered);
                delivered = available;
                onAnswerChunk(newText);
            }
            return true;
        }

        return await StreamAsync(
            requestBody,
            promptCaching: true,
            "Classify stream failed after retry",
            beginAttempt: () =>
            {
                fullText.Clear();
                classificationSent = jevClassification is not null;
                delivered = 0;
            },
            onText: text =>
            {
                fullText.Append(text);
                return Release(streamEnded: false);
            },
            endAttempt: () =>
            {
                if (!Release(streamEnded: true)) return;
                if (!classificationSent)
                {
                    // Empty or unparseable reply: keep the old default of "question" so the caller can fall back.
                    onClassification(AdjustedClassification(ParseClassification(fullText.ToString()), combinedText));
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// Parse STATUS:xxx|TOPIC:yyy format
    internal static UtteranceClassification ParseClassification(string text)
    {
        var cleaned = text.Lowercased().Replace(" ", "").Replace("\n", "");

        var status = "question";
        string? topic = "unknown";

        // Parse STATUS:xxx
        var statusIndex = cleaned.IndexOf("status:", StringComparison.Ordinal);
        if (statusIndex >= 0)
        {
            var afterStatus = cleaned.Substring(statusIndex + "status:".Length);
            var statusEnd = afterStatus.IndexOf('|');
            status = statusEnd < 0 ? afterStatus : afterStatus.Substring(0, statusEnd);
        }

        // Parse TOPIC:yyy
        var topicIndex = cleaned.IndexOf("topic:", StringComparison.Ordinal);
        if (topicIndex >= 0)
        {
            var afterTopic = cleaned.Substring(topicIndex + "topic:".Length);
            var topicEnd = afterTopic.IndexOf('|');
            if (topicEnd < 0) topicEnd = afterTopic.IndexOf('-');
            var topicValue = topicEnd < 0 ? afterTopic : afterTopic.Substring(0, topicEnd);
            topic = topicValue is "none" or "" ? null : topicValue;
        }

        return new UtteranceClassification(status, topic);
    }

    internal static UtteranceClassification AdjustedClassification(UtteranceClassification classification, string text)
    {
        var topic = CorrectedTopic(text, classification.Topic);
        if (classification.Status != "question" && HasTechnicalQuestionMarker(text))
        {
            return new UtteranceClassification("question", topic);
        }

        return new UtteranceClassification(classification.Status, topic);
    }

    internal static string? CorrectedTopic(string text, string? classifiedTopic)
    {
        var normalized = text.Lowercased();
        bool HasWord(string word) =>
            Regex.IsMatch(normalized, $"(^|[^a-z0-9]){word}($|[^a-z0-9])", RegexOptions.CultureInvariant);
        bool Has(string value) => normalized.Contains(value, StringComparison.Ordinal);

        var hasRag = HasWord("rag") || Has("retrieval augmented generation");
        var hasCag = HasWord("cag") || Has("cache augmented generation") || Has("context augmented generation");
        if (hasRag && hasCag) return "ragCag";
        if (hasRag) return "rag";
        if (hasCag) return "cag";

        if (Has("хешмап") || Has("хеш мап") || Has("хеш таблиц") || Has("hash map") || Has("hashmap") || Has("hash table"))
        {
            return "hashMap";
        }

        if (Has("хеш код") || Has("hash code") || Has("hashcode"))
        {
            return "hashCode";
        }

        if (Has("ооп") || Has("обектно") || Has("object oriented") || Has("object-oriented")
            || Has(" полиморф") || Has(" наследяване") || Has(" капсулация")
            || normalized.HasPrefix("оп,") || normalized.HasPrefix("оп ") || Has(" оп,") || Has(" оп "))
        {
            return "oop";
        }

        if (FollowUpPrompts.IsVague(normalized) && (classifiedTopic is null || classifiedTopic.Lowercased() == "unknown"))
        {
            return "followUp";
        }

        return classifiedTopic;
    }

    private static readonly string[] TechnicalQuestionMarkers =
    {
@@MARKERS@@
    };

    internal static bool HasTechnicalQuestionMarker(string text)
    {
        var normalized = text.Lowercased();
        return TechnicalQuestionMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    /// Send a message with images and stream the response
    /// <param name="images">Base64-encoded images</param>
    /// <param name="prompt">User prompt</param>
    /// <param name="onChunk">Callback for each chunk</param>
    /// <param name="prefill">Optional assistant prefill to start the response (forces format)</param>
    public virtual Task<Outcome> SendMessageStreamAsync(
        IReadOnlyList<string> images, string prompt, Action<string> onChunk, string? prefill = null,
        CancellationToken cancellationToken = default)
    {
        // Build multimodal user content for the active provider
        var userContent = new JsonArray();
        foreach (var imageBase64 in images)
        {
            if (UsesOpenAIWireFormat)
            {
                userContent.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:image/png;base64,{imageBase64}" }
                });
            }
            else
            {
                userContent.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = imageBase64 }
                });
            }
        }
        userContent.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });

        var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent });

        if (!string.IsNullOrEmpty(prefill))
        {
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = prefill });
            onChunk(prefill);
        }

        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = imageMaxTokens,
            ["stream"] = true,
            ["messages"] = messages
        };
        return StreamAsync(requestBody, false, "Image stream failed after retry", null, text =>
        {
            onChunk(text);
            return true;
        }, null, cancellationToken);
    }

    // Session Analysis

    public virtual async Task<SessionAnalysisResult> AnalyzeSessionAsync(string transcript, CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", new[]
        {
            "Analyze this interview session transcript. Return EXACTLY this format (one item per line within each section):",
            "",
            "TOPICS:",
            "<topic1>",
            "<topic2>",
            "",
            "STRENGTHS:",
            "<strength1>",
            "",
            "WEAKNESSES:",
            "<weakness1>",
            "",
            "INSIGHTS:",
            "<insight1>",
            "<insight2>",
            "",
            "Use short topic slugs (e.g., oop, systemDesign, algorithms). Strengths/weaknesses should be brief phrases. Insights should be 1 sentence each (max 3).",
            "",
            "Transcript:",
            transcript
        });

        var (text, _) = await SendMessageAsync(prompt, AppConstants.MaxTokens.SessionAnalysis, cancellationToken).ConfigureAwait(false);
        return ParseAnalysisResponse(text);
    }

    internal static SessionAnalysisResult ParseAnalysisResponse(string text)
    {
        var topics = new List<string>();
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var insights = new List<string>();

        List<string>? currentSection = null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimInline();
            if (trimmed.Length == 0) continue;

            var upper = trimmed.Uppercased();
            if (upper.HasPrefix("TOPICS")) { currentSection = topics; continue; }
            if (upper.HasPrefix("STRENGTHS")) { currentSection = strengths; continue; }
            if (upper.HasPrefix("WEAKNESSES")) { currentSection = weaknesses; continue; }
            if (upper.HasPrefix("INSIGHTS")) { currentSection = insights; continue; }

            currentSection?.Add(trimmed.HasPrefix("- ") ? trimmed.Substring(2) : trimmed);
        }

        return new SessionAnalysisResult(topics, strengths, weaknesses, insights);
    }

    // Conversation Summarization

    /// Summarize older conversation history to compress context
    /// <param name="conversationText">Pre-formatted conversation (e.g., "Q: ...\nA: ...")</param>
    public virtual async Task<string> SummarizeConversationAsync(string conversationText, CancellationToken cancellationToken = default)
    {
        if (conversationText.Length == 0) return "";

        var prompt = string.Join("\n", new[]
        {
            "Summarize this interview conversation in 2-3 concise sentences.",
            "Focus on: main topics discussed, key technical concepts, and any important context for follow-up questions.",
            "",
            "Conversation:",
            conversationText,
            "",
            "Summary:"
        });

        var (summary, _) = await SendMessageAsync(prompt, AppConstants.MaxTokens.Summarization, cancellationToken).ConfigureAwait(false);
        Log.Print($"📋 Generated summary: {summary.Prefix(100)}...");
        return summary;
    }
}
