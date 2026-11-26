using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// The chat request shape Groq's OpenAI-compatible endpoint takes.
internal static class GroqChat
{
    public const string Domain = "GroqClient";

    public static JsonObject RequestBody(
        IEnumerable<IReadOnlyDictionary<string, string>> messages, int maxTokens, double temperature, string model)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = ApiHttp.MessageArray(messages),
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature
        };

        foreach (var (key, value) in GroqRequestTuning.ChatReasoningFields(model))
        {
            body[key] = value switch
            {
                bool flag => JsonValue.Create(flag),
                _ => JsonValue.Create(value.ToString())
            };
        }

        return body;
    }

    public static IReadOnlyDictionary<string, string> UserMessage(string prompt) =>
        new Dictionary<string, string> { ["role"] = "user", ["content"] = prompt };
}

/// Groq API client for interview assistance (Whisper STT + LLM)
public class GroqInterviewClient : IDisposable
{
    /// Classification result for an utterance.
    /// <param name="Status">"question", "incomplete", "answer", "filler"</param>
    /// <param name="Topic">topic name or null</param>
    public sealed record UtteranceClassification(string Status, string? Topic);

    private const double RequestTimeoutSeconds = 15;
    private const double ResourceTimeoutSeconds = 20;

    private readonly string apiKey;
    private readonly string whisperUrl = AppConstants.ApiUrls.GroqTranscriptions;
    private readonly string chatUrl = AppConstants.ApiUrls.GroqChat;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly AppSettings? injectedSettings;
    private bool isConnectionWarm;

    private AppSettings Settings => injectedSettings ?? AppSettings.Shared;

    public GroqInterviewClient(string apiKey, HttpClient? client = null, AppSettings? settings = null)
    {
        this.apiKey = apiKey;
        injectedSettings = settings;
        ownsClient = client is null;
        this.client = client ?? ApiHttp.CreateClient(maxConnectionsPerHost: 4);
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
        GC.SuppressFinalize(this);
    }

    public virtual async Task WarmupConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (isConnectionWarm) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            await ApiHttp.SendAsync(client, request, 5, cancellationToken).ConfigureAwait(false);
            isConnectionWarm = true;
            DebugLogger.Shared.Log(LogCategory.Transcription, "Groq connection warmed");
        }
        catch (Exception error)
        {
            DebugLogger.Shared.Log(LogCategory.Error, $"Groq warmup failed (non-critical): {error.Message}");
        }
    }

    /// Requests are cancelled through the token each call takes; kept so callers can signal intent.
    public virtual void CancelCurrentRequest()
    {
    }

    public virtual async Task<(string Text, double LatencyMs)> TranscribeAsync(
        byte[] audioData, string? filename = null, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var upload = GroqRequestTuning.TranscriptionUpload(audioData);
        var name = filename ?? upload.Filename;
        var settings = Settings;

        var boundary = Guid.NewGuid().ToString().ToUpperInvariant();
        using var body = new MemoryStream();
        void Append(string text) => body.Write(Encoding.UTF8.GetBytes(text));

        Append($"--{boundary}\r\n");
        Append("Content-Disposition: form-data; name=\"model\"\r\n\r\n");
        Append($"{AppConstants.Models.GroqWhisper}\r\n");

        var languageCode = settings.LanguageCode;
        DebugLogger.Shared.Log(
            LogCategory.Transcription,
            $"Groq STT language={languageCode}, responseLanguage={settings.ResponseLanguage.DisplayName()}");
        Append($"--{boundary}\r\n");
        Append("Content-Disposition: form-data; name=\"language\"\r\n\r\n");
        Append($"{languageCode}\r\n");

        var vocabulary = CappedWhisperPrompt(settings.WhisperVocabulary);
        DebugLogger.Shared.Log(
            LogCategory.Transcription,
            $"Groq STT prompt length={vocabulary.Length} chars, {Encoding.UTF8.GetByteCount(vocabulary)} bytes");
        Append($"--{boundary}\r\n");
        Append("Content-Disposition: form-data; name=\"prompt\"\r\n\r\n");
        Append($"{vocabulary}\r\n");

        Append($"--{boundary}\r\n");
        Append($"Content-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n");
        Append($"Content-Type: {upload.MimeType}\r\n\r\n");
        body.Write(audioData);
        Append($"\r\n--{boundary}--\r\n");
        var payload = body.ToArray();

        var attemptNumber = 0;
        return await ApiHttp.WithRetriesAsync(
            GroqChat.Domain,
            "Transcription failed after retries",
            async () =>
            {
                attemptNumber++;
                DebugLogger.Shared.Log(
                    LogCategory.Transcription,
                    $"Groq STT attempt {attemptNumber}/{ApiHttp.MaxAttempts}, model={AppConstants.Models.GroqWhisper}, file={name}, mime={upload.MimeType}, audio={audioData.Length} bytes");
                using var request = new HttpRequestMessage(HttpMethod.Post, whisperUrl) { Content = new ByteArrayContent(payload) };
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                request.Content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
                var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);

                DebugLogger.Shared.Log(LogCategory.Transcription, $"Groq STT HTTP {status}, response={data.Length} bytes");
                if (status is < 200 or >= 300)
                {
                    var responseBody = ApiHttp.Utf8(data) ?? "";
                    var message = responseBody.Length == 0 ? $"HTTP {status}" : $"HTTP {status}: {responseBody.Prefix(300)}";
                    throw new ApiClientException(GroqChat.Domain, status, message);
                }

                var latency = started.Elapsed.TotalMilliseconds;
                try
                {
                    using var document = StrictJson.ParseObject(data);
                    var text = StrictJson.RequiredString(document.RootElement, "text");
                    DebugLogger.Shared.Log(LogCategory.Transcription, $"Groq STT decoded {text.Length} chars");
                    return (text, latency);
                }
                catch (JsonException error)
                {
                    var responseBody = ApiHttp.Utf8(data) ?? "<non-utf8 body>";
                    DebugLogger.Shared.Log(LogCategory.Error, $"Groq STT decode failed: {error.Message}. Body prefix: {responseBody.Prefix(300)}");
                    throw;
                }
            },
            cancellationToken,
            onNonRetryable: error => DebugLogger.Shared.Log(LogCategory.Error, $"Groq STT non-retryable failure: {error.Message}"),
            onRetry: (index, error) => DebugLogger.Shared.Log(LogCategory.Error, $"Groq STT attempt {index + 1} failed: {error.Message}. Retrying..."),
            onExhausted: error => DebugLogger.Shared.Log(LogCategory.Error, $"Groq STT failed after {ApiHttp.MaxAttempts} attempts: {error.Message}"))
            .ConfigureAwait(false);
    }

    /// Whisper rejects long prompts, so the vocabulary is cut at the byte limit and then at the last whole term.
    internal static string CappedWhisperPrompt(string prompt, int limit = AppConstants.Limits.GroqWhisperPromptBytes)
    {
        if (Encoding.UTF8.GetByteCount(prompt) <= limit) return prompt;

        var capped = new StringBuilder();
        var byteCount = 0;

        var characters = StringInfo.GetTextElementEnumerator(prompt);
        while (characters.MoveNext())
        {
            var character = characters.GetTextElement();
            var byteLength = Encoding.UTF8.GetByteCount(character);
            if (byteCount + byteLength > limit) break;
            capped.Append(character);
            byteCount += byteLength;
        }

        var text = capped.ToString();
        var lastComma = text.LastIndexOf(',');
        if (lastComma >= 0)
        {
            var trimmed = text.Substring(0, lastComma).Trim();
            if (trimmed.Length > 0) return trimmed;
        }

        return text.Trim();
    }

    /// Generate a concise interview answer for a topic
    public virtual Task<(string Answer, double LatencyMs)> GenerateAnswerAsync(
        string topic, string transcription, string? userBackground = null, CancellationToken cancellationToken = default)
    {
        var prompt = AnswerPrompt(topic, transcription, userBackground);
        var body = GroqChat.RequestBody(
            GroqRequestTuning.ChatMessages(prompt), AppConstants.MaxTokens.GroqAnswer, 0.3, AppConstants.Models.GroqFastAnswer);
        return BufferedAnswerAsync(body, "Answer generation failed after retries", cancellationToken);
    }

    private async Task<(string Answer, double LatencyMs)> BufferedAnswerAsync(
        JsonObject requestBody, string exhaustedDescription, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var body = ApiHttp.Encode(requestBody);

        return await ApiHttp.WithRetriesAsync(GroqChat.Domain, exhaustedDescription, async () =>
        {
            using var request = ApiHttp.JsonPost(chatUrl, body, apiKey);
            var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (status is >= 400 and < 500) throw new ApiClientException(GroqChat.Domain, status, $"HTTP {status}");

            var latency = started.Elapsed.TotalMilliseconds;

            using var document = StrictJson.ParseObject(data);
            var (_, firstContent) = StrictJson.ChatChoices(document.RootElement, choicesRequired: false, contentRequired: true);
            if (StrictJson.ErrorMessage(document.RootElement) is { } apiError)
            {
                Log.Print($"⚠️ Groq API error: {apiError}");
                return ($"API error: {apiError}", latency);
            }

            var rawAnswer = firstContent?.Trim() ?? "No answer generated";
            return (GroqRequestTuning.VisibleCueCardPrefix(rawAnswer) + rawAnswer, latency);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// Shared cue-card answer prompt used by both buffered and streaming answers.
    /// When `context` is provided (vague follow-ups), the recent conversation is added
    /// so the answer extends the topic with new angles instead of repeating it.
    internal string AnswerPrompt(
        string topic,
        string transcription,
        string? userBackground,
        string? context = null,
        bool contextIsFollowUp = true,
        bool topicInherited = false,
        string? pinnedSolution = null)
    {
        var settings = Settings;
        var backgroundContext = Briefing.ResumeGroundingInstruction(userBackground ?? "", transcription, topic, context ?? "");

        var followUpContext = AnswerPromptBlocks.RecentConversation(context, contextIsFollowUp);
        var pinnedContext = AnswerPromptBlocks.PinnedCode(pinnedSolution);
        var topicBlock = AnswerPromptBlocks.TopicBlock(topic, topicInherited);

        var languageInstruction = settings.LlmLanguageInstruction;
        var topicGuidance = topicBlock.AllowsTopicSpecificShape ? TopicSpecificAnswerGuidance(topic) : "";
        var roleHint = "";
        if (settings.IsPlaywrightFocused)
        {
            roleHint = "\nPLAYWRIGHT: prefer `await expect(locator).toBeVisible()`, getByRole/getByLabel, no waitForTimeout.\n";
        }
        else if (settings.IsTestAutomationRole)
        {
            roleHint = $"\n{settings.Role.QaSeniorityInstruction()}\n";
        }
        if (settings.IsAIOrDataRole)
        {
            roleHint += "\nRAG/CAG: source -> chunks/embeddings -> vector DB -> semantic search -> LLM with retrieved context."
                + "\nCORE ML: define the metric/concept plainly, give the formula or intuition, then when to use it and one pitfall.\n";
        }
        var topicLower = topic.Lowercased();
        var ragNote = topicBlock.AllowsTopicSpecificShape && topicLower is "rag" or "cag" or "ragcag"
            ? "If Topic is rag, cag, or ragCag, answer RAG/CAG even if the transcript says \"Rack\" or \"CAC\"."
            : "";
        var topicLine = topicBlock.Line.Length == 0 ? "" : $"\n{topicBlock.Line}";
        return string.Join("\n", new[]
        {
            "Cue-card interview answer the candidate can say out loud.",
            "3-5 bullets only. Every line starts with \"- \". Under 90 characters.",
            "Plain text only. No markdown, no **bold**, no headings, no numbered lists.",
            "For acronyms like SOLID, one bullet per letter as \"S - Single Responsibility: ...\".",
            "No preamble, headings, disclaimers, or hedges.",
            "Point first, then one example or trade-off.",
            settings.CandidateVoiceInstruction,
            "For \"What is X?\", first bullet defines X with the acronym expanded.",
            backgroundContext + followUpContext + pinnedContext,
            settings.InterviewContext,
            roleHint,
            $"Q: \"{transcription}\"{topicLine}",
            topicGuidance,
            topicBlock.GarbleNote,
            ragNote,
            languageInstruction
        });
    }

    internal static string TopicSpecificAnswerGuidance(string topic)
    {
        var topicLower = topic.Lowercased();
        if (topicLower == "solid")
        {
            return string.Join("\n", new[]
            {
                "",
                "SOLID ANSWER SHAPE:",
                "One bullet per letter. Plain text. Never write **S**ingle or S**.",
                "- S - Single Responsibility: one class, one reason to change",
                "- O - Open/Closed: extend without modifying existing code",
                "- L - Liskov Substitution: subtypes must replace their base type",
                "- I - Interface Segregation: many small interfaces, not one fat one",
                "- D - Dependency Inversion: depend on abstractions, not concretions"
            });
        }
        if (topicLower is not ("rag" or "cag" or "ragcag")) return "";

        return string.Join("\n", new[]
        {
            "",
            "RAG/CAG ANSWER SHAPE:",
            "- First bullet: \"RAG (Retrieval-Augmented Generation) means...\"",
            "- Explain that docs, pages, PDFs, images, or video/audio transcripts are chunked and embedded.",
            "- Mention those embeddings are stored in a vector DB and searched semantically.",
            "- End with the LLM using retrieved chunks as context, plus one practical trade-off.",
            "- Avoid abstract-only wording like \"combines a vector store with generation\"."
        });
    }

    /// Stream a concise interview answer for a topic (fast quality path).
    /// Uses Groq's OpenAI-compatible SSE so the first useful text reaches the
    /// card without waiting on the slower authoritative classification.
    /// Returns failure (and may have emitted nothing) so the caller can fall back.
    public virtual async Task<Outcome> StreamAnswerAsync(
        string topic,
        string transcription,
        Action<string> onChunk,
        string? userBackground = null,
        string? context = null,
        bool contextIsFollowUp = true,
        bool topicInherited = false,
        string? pinnedSolution = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = AnswerPrompt(topic, transcription, userBackground, context, contextIsFollowUp, topicInherited, pinnedSolution);
        var requestBody = GroqChat.RequestBody(
            GroqRequestTuning.ChatMessages(prompt), AppConstants.MaxTokens.GroqAnswer, 0.3, AppConstants.Models.GroqFastAnswer);
        requestBody["stream"] = true;
        var body = ApiHttp.Encode(requestBody);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = ApiHttp.JsonPost(chatUrl, body, apiKey);
                using var response = await ApiHttp.OpenStreamAsync(
                    client, request, RequestTimeoutSeconds, ResourceTimeoutSeconds, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is < 200 or > 299)
                {
                    var failure = new ApiClientException(GroqChat.Domain, response.StatusCode, $"HTTP {response.StatusCode}");
                    if (response.StatusCode is >= 400 and < 500) return Outcome.Failure(failure);
                    if (attempt == 0)
                    {
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return Outcome.Failure(failure);
                }

                var emittedFirstVisible = false;
                while (await response.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    var (done, json) = ApiHttp.ParseEventLine(line);
                    if (done) break;
                    if (json is not { } payload || ApiHttp.OpenAIDeltaText(payload) is not { } content) continue;

                    if (!emittedFirstVisible)
                    {
                        emittedFirstVisible = true;
                        onChunk(GroqRequestTuning.VisibleCueCardPrefix(content) + content);
                    }
                    else
                    {
                        onChunk(content);
                    }
                }

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

        return Outcome.Failure(new ApiClientException(GroqChat.Domain, -1, "Answer stream failed after retry"));
    }

    internal static string ClassificationPrompt(string combinedText, string lastTopicNote) => $$"""
        Classify this utterance. Return: STATUS,TOPIC

        Text: "{{combinedText}}"
        {{lastTopicNote}}

        IMPORTANT: This is speech-to-text from a technical interview. Words may be misheard.
        Use context to interpret what makes sense technically (e.g., "Ray vs ArrayList" → Array vs ArrayList).

        STATUS (pick one):
        - question = asking about something OR mentioning a topic (wants info)
        - incomplete = cut off mid-sentence ("What is the", "Can you")
        - answer = user responding/explaining (any length). Includes: "Yes, sure", "Thank you", "Okay, I understand", any explanation
        - filler = ONLY single meaningless sounds: "um", "uh", "hmm", "ah". NOT "okay", "yes", "thank you"

        Short topic mentions like "polymorphism", "singleton pattern" = question

        TOPICS:
        array, arrayList, linkedList, hashMap, hashSet, treeMap, queue, collections
        threads, process, synchronized, volatile, deadlock, locks
        jvm, jdk, jre, garbageCollection, heap, stack
        oop, inheritance, polymorphism, encapsulation, abstraction, abstractClass, interface
        lambda, streamApi, optional, functionalInterface
        exceptions, checkedExceptions, uncheckedExceptions
        closure, hoisting, eventLoop, promises, asyncAwait, this, scope
        reactHooks, useState, useEffect, useContext, virtualDOM, redux
        typescript, generics, interfaces, types
        bigO, sorting, binarySearch, recursion, dynamicProgramming, bfs, dfs
        systemDesign, caching, redis, loadBalancing, database, sql, nosql, microservices, rest
        llm, rag, cag, ragCag, embeddings, vectorDatabase, retrieval, chunking, reranking, promptEngineering, fineTuning, transformers, agents, aiEvaluation, hallucination, guardrails
        machineLearning, supervisedLearning, classification, regression, clustering, metrics, precisionRecall, f1Score, confusionMatrix, rocAuc, prAuc, classImbalance, overfitting, biasVariance, regularization, crossValidation, hyperparameterTuning, featureEngineering, dataPreprocessing, dataLeakage, logisticRegression, decisionTrees, randomForest, gradientBoosting, neuralNetworks, computerVision, forecasting, timeSeries, kMeans, dbscan
        singleton, factory, builder, observer, strategy, dependencyInjection, solid
        testing, unitTest, tdd, mocking, testAutomation, selenium, playwright, playwrightTest, playwrightLocators, webFirstAssertions, autoWaiting, storageState, browserContext, playwrightFixtures, pageRoute, networkMocking, traceViewer, playwrightProjects, workers, sharding, retries, cypress, apiTesting, contractTesting, e2eTesting, fixtures, testData, pageObjects, locators, visualRegression, accessibilityTesting, testStrategy, flakyTests
        docker, kubernetes, ci, cd, git, aws
        linux, bash, ssh, networking
        background, experience, tellMeAboutYourself, projects
        followUp (for "tell me more" with no new topic)
        unknown (if no match)

        EXAMPLES:
        "What is ray list?" → question,arrayList (ray list = ArrayList)
        "What is Rack system, Rack or CAC?" → question,ragCag (AI acronym mishear = RAG/CAG)
        "When would you use F one over accuracy?" → question,f1Score (F one = F1)
        "difference between a ray and ray list" → question,array (comparing Array vs ArrayList)
        "What is key developer?" → question,hashMap (nonsense phrase + last topic = asking about hashMap key)
        "What is the" → incomplete,none
        "Tell me more" → question,followUp

        NOTE: If phrase makes no sense (like "key developer"), use lastTopic - it's likely a misheard word.

        Return ONLY: STATUS,TOPIC (e.g., "question,array")
        """;

    /// What the classifier's "status,topic" reply means for this utterance.
    internal static UtteranceClassification ParseClassification(string? content, string combinedText)
    {
        var raw = content?.Trim().Lowercased() ?? "question,unknown";

        var cleaned = raw.Replace(":", ",").Replace(" ", "");
        var parts = cleaned.SplitOmittingEmpty(',');
        var status = parts.Count > 0 ? parts[0] : "question";
        var topicRaw = parts.Count > 1 ? parts[1] : "unknown";
        string? topic = topicRaw is "none" or "" ? null : topicRaw;
        if (status == "question"
            && (topic is null || topic.Lowercased() == "unknown")
            && FollowUpPrompts.IsVague(combinedText.Lowercased()))
        {
            topic = "followUp";
        }

        return new UtteranceClassification(status, topic);
    }

    /// Combined classification: status + topic in ONE call
    /// Replaces both checkCompleteness() and detectTopic()
    public virtual async Task<(UtteranceClassification Classification, double LatencyMs)> ClassifyUtteranceAsync(
        string text, string buffer, string? lastTopic, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var combinedText = buffer.Length == 0 ? text : $"{buffer} {text}";
        var lastTopicNote = lastTopic is not null ? $"Last topic: {lastTopic}" : "";
        var prompt = ClassificationPrompt(combinedText, lastTopicNote);

        var body = ApiHttp.Encode(GroqChat.RequestBody(
            new[] { GroqChat.UserMessage(prompt) },
            AppConstants.MaxTokens.GroqClassification,
            0,
            AppConstants.Models.GroqFastClassification));

        return await ApiHttp.WithRetriesAsync(GroqChat.Domain, "Classification failed after retries", async () =>
        {
            using var request = ApiHttp.JsonPost(chatUrl, body, apiKey);
            var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (status is >= 400 and < 500) throw new ApiClientException(GroqChat.Domain, status, $"HTTP {status}");

            var latency = started.Elapsed.TotalMilliseconds;

            using var document = StrictJson.ParseObject(data);
            var (_, firstContent) = StrictJson.ChatChoices(document.RootElement, choicesRequired: true, contentRequired: true);
            return (ParseClassification(firstContent, combinedText), latency);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal string FollowUpPrompt(string topic, string transcription, string context, string? userBackground)
    {
        var settings = Settings;
        var backgroundContext = Briefing.ResumeGroundingInstruction(userBackground ?? "", transcription, topic, context);

        var languageInstruction = settings.LlmLanguageInstruction;
        return string.Join("\n", new[]
        {
            $"Follow-up on: {topic}",
            $"Request: \"{transcription}\"",
            backgroundContext,
            settings.InterviewContext,
            "",
            settings.AnswerStyleInstruction,
            "",
            $"Context: {context}",
            "",
            "Add 1-2 short bullets not covered. No intro. Every line starts with \"- \".",
            languageInstruction
        });
    }

    /// Generate follow-up answer for current topic
    public virtual Task<(string Answer, double LatencyMs)> GenerateFollowUpAnswerAsync(
        string topic, string transcription, string context, string? userBackground = null, CancellationToken cancellationToken = default)
    {
        var prompt = FollowUpPrompt(topic, transcription, context, userBackground);
        var body = GroqChat.RequestBody(
            GroqRequestTuning.ChatMessages(prompt), AppConstants.MaxTokens.FollowUp, 0.3, AppConstants.Models.GroqFastAnswer);
        return BufferedAnswerAsync(body, "Follow-up answer failed after retries", cancellationToken);
    }
}
