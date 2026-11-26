using System.Text;
using System.Text.Json;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

public sealed class ClientTests : SwiftStyleTests
{
    private static readonly IReadOnlyList<Dictionary<string, string>> NoHistory = Array.Empty<Dictionary<string, string>>();

    private static AppSettings Settings() => new(new InMemorySettingsStore());

    private static string JevReply(string status, double confidence = 0.95, double probability = 0.96)
    {
        var probabilities = JevDecisionClient.Criteria.Keys.ToDictionary(key => key, key => key == status ? probability : (1 - probability) / 4);
        return JsonSerializer.Serialize(new
        {
            model = JevDecisionClient.Model,
            answers = new { intent = new { type = "choice", choice = status, confidence, probabilities } }
        });
    }

    private static JevDecisionClient.Answer Validated(string reply) => JevDecisionClient.ValidatedAnswer(Encoding.UTF8.GetBytes(reply));

    [Fact]
    public void JevDecisionsNeedConfidence()
    {
        foreach (var status in JevStatuses.AllCases)
        {
            var accepted = JevDecisionClient.AcceptedStatus(Validated(JevReply(status.RawValue())));
            Check(accepted == (status == JevStatus.Uncertain ? null : status), $"decision {status}");
        }
        Check(JevDecisionClient.AcceptedStatus(Validated(JevReply("question", confidence: 0.2))) is null, "uncertainty falls back");
        Check(JevDecisionClient.AcceptedStatus(Validated(JevReply("question", probability: 0.6))) is null, "diffuse distribution falls back");
        foreach (var reply in new[] { "{}", JevReply("invented"), JevReply("question", confidence: 2), "not json", "[]" })
        {
            Check(Assert.Throws<JevFailure>(() => Validated(reply)).Kind == JevFailureKind.InvalidResponse, "invalid rejected");
        }
        var oversized = Assert.Throws<JevFailure>(() => JevDecisionClient.RequestBody(new string('я', 7000), "", null, NoHistory));
        Check(oversized.Kind == JevFailureKind.OversizedInput, "oversize falls back without clipping speech");
        var history = Enumerable.Repeat(new Dictionary<string, string> { ["role"] = "user", ["content"] = new string('x', 2000) }, 20).ToList();
        var bounded = JevDecisionClient.RequestBody("А как работи?", "", "rag", history);
        Check(bounded.Length < 12000, "history bounded");
        var state = JsonDocument.Parse(bounded).RootElement.GetProperty("state");
        Check(state.GetProperty("utterance").GetString() == "А как работи?" && state.GetProperty("last_topic").GetString() == "rag"
            && state.GetProperty("recent_turns").GetArrayLength() == 4, "request carries the utterance, topic and the latest turns that fit");
    }

    [Fact]
    public async Task JevOnlyReplacesTheClassifierWhenItIsSure()
    {
        // Link and run the real combined client: only HTTP transport is replaced.
        foreach (var scenario in new[] { "question", "statement", "protected", "uncertain", "http", "malformed", "timeout" })
        {
            var modelCalls = 0;
            var stub = new StubHttp(call =>
            {
                if (call.Uri.Host == "api.typesafe.ai")
                {
                    return scenario switch
                    {
                        "http" => (429, ""),
                        "malformed" => (200, "{}"),
                        "timeout" => throw new HttpRequestException("timed out"),
                        _ => (200, JevReply(scenario == "protected" ? "statement" : scenario))
                    };
                }
                Interlocked.Increment(ref modelCalls);
                return (200, StubHttp.OpenAIStream(scenario == "question" ? "- A grounded answer." : "STATUS:question|TOPIC:unknown\n---\n- Fallback answer."));
            });
            using var client = new AnthropicClient("test-only", client: stub.Client(), settings: Settings());
            client.JevDecisionClient = new JevDecisionClient("test-only", stub.Client());
            var statuses = new List<string>();
            var text = new StringBuilder();
            var result = await client.ClassifyAndStreamAnswerAsync(
                "And that approach", "", null, null, NoHistory,
                classification => statuses.Add(classification.Status), chunk => text.Append(chunk),
                jevMaySuppressAnswer: scenario != "protected");
            Check(result.IsSuccess, $"scenario completed {scenario}");
            Check(statuses.SequenceEqual(new[] { scenario == "statement" ? "statement" : "question" }), $"one classification {scenario}");
            Check(modelCalls == (scenario == "statement" ? 0 : 1), $"generation call count {scenario}");
            Check(scenario == "statement" ? text.Length == 0 : text.ToString().HasPrefix("- "), $"visible answer {scenario}");
            if (scenario == "question")
            {
                var system = stub.Calls[^1].Json["messages"]![0]!["content"]!.GetValue<string>();
                Check(system.HasPrefix("Answer the interview question directly.") && !system.Contains("STATUS options", StringComparison.Ordinal),
                    "an accepted question gets the answer-only prompt");
            }
        }

        // Cancellation must not turn into an expensive fallback request.
        var silent = new StubHttp((200, JevReply("question")));
        var jev = new JevDecisionClient("test-only", silent.Client());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => jev.ClassifyAsync("test", "", null, NoHistory, cancelled.Token));
        Check(silent.Calls.Count == 0, "cancelled task has no HTTP request");
    }

    [Fact]
    public async Task ClassifyRequestFollowsEachProvidersWireFormat()
    {
        var history = new List<Dictionary<string, string>>
        {
            new() { ["role"] = "user", ["content"] = "What is RAG?" },
            new() { ["role"] = "assistant", ["content"] = "- RAG retrieves context." },
            new() { ["role"] = "user", ["content"] = "and how does it compare" }
        };

        async Task<StubHttp.Call> Send(InterviewLLMProvider provider, AppSettings settings)
        {
            var stub = new StubHttp((200, provider == InterviewLLMProvider.Anthropic
                ? StubHttp.AnthropicStream("STATUS:question|TOPIC:rag\n---\n- Answer.")
                : StubHttp.OpenAIStream("STATUS:question|TOPIC:rag\n---\n- Answer.")));
            using var client = new AnthropicClient("secret-key", provider, stub.Client(), settings);
            var result = await client.ClassifyAndStreamAnswerAsync("and how does it compare to CAG", "So tell me about RAG", "rag", null, history, _ => { }, _ => { });
            Check(result.IsSuccess && stub.Calls.Count == 1, $"{provider} classify completes in one request");
            return stub.Calls[0];
        }

        var xai = await Send(InterviewLLMProvider.Xai, Settings());
        Check(xai.Uri.AbsoluteUri == AppConstants.ApiUrls.XaiChat && xai.Headers["Authorization"] == "Bearer secret-key", "xAI uses bearer auth on the chat endpoint");
        Check(xai.Json["model"]!.GetValue<string>() == AppConstants.Models.XaiGrok && xai.Json["max_tokens"]!.GetValue<int>() == 320
            && xai.Json["stream"]!.GetValue<bool>() && xai.Json["temperature"]!.GetValue<double>() == 0.2, "xAI request carries the model, token cap, stream and temperature");
        var messages = xai.Json["messages"]!.AsArray();
        Check(messages.Count == 4 && messages[0]!["role"]!.GetValue<string>() == "system"
            && messages[0]!["content"]!.GetValue<string>() == AnthropicClient.ClassificationSystemPrompt, "The system prompt leads the messages");
        var last = messages[3]!["content"]!.GetValue<string>();
        Check(messages[3]!["role"]!.GetValue<string>() == "user" && last.HasPrefix("Q: \"So tell me about RAG and how does it compare to CAG\"\nTOPICS: "),
            "The trailing user turn is replaced by the question with its context");
        Check(last.Contains("\nPrevious topic (context only, answer the question actually asked): rag", StringComparison.Ordinal), "The previous topic is context only");

        var tuned = Settings();
        tuned.OpenAIReasoningEffort = "medium";
        tuned.OpenAIPriority = true;
        var openAI = await Send(InterviewLLMProvider.OpenAI, tuned);
        Check(openAI.Uri.AbsoluteUri == AppConstants.ApiUrls.OpenAIChat && openAI.Json["model"]!.GetValue<string>() == "gpt-5.5", "OpenAI uses the chosen GPT model");
        Check(openAI.Json["max_tokens"] is null && openAI.Json["temperature"] is null
            && openAI.Json["max_completion_tokens"]!.GetValue<int>() == 320 + AppConstants.MaxTokens.OpenAIReasoningAllowance,
            "GPT-5 gets max_completion_tokens with a reasoning allowance and no temperature");
        Check(openAI.Json["reasoning_effort"]!.GetValue<string>() == "medium" && openAI.Json["service_tier"]!.GetValue<string>() == "priority", "GPT-5 gets the reasoning effort and priority tier");

        var anthropic = await Send(InterviewLLMProvider.Anthropic, Settings());
        Check(anthropic.Uri.AbsoluteUri == AppConstants.ApiUrls.AnthropicMessages && anthropic.Headers["x-api-key"] == "secret-key"
            && anthropic.Headers["anthropic-version"] == "2023-06-01" && anthropic.Headers["anthropic-beta"] == "prompt-caching-2024-07-31"
            && !anthropic.Headers.ContainsKey("Authorization"), "Claude uses its key header and prompt caching");
        var system = anthropic.Json["system"]![0]!;
        Check(system["cache_control"]!["type"]!.GetValue<string>() == "ephemeral" && system["text"]!.GetValue<string>() == AnthropicClient.ClassificationSystemPrompt
            && anthropic.Json["messages"]!.AsArray().Count == 3 && anthropic.Json["temperature"] is null, "Claude gets a cached system block and plain messages");
        Check(AnthropicClient.ClassificationSystemPrompt.Contains(AnswerPromptBlocks.CandidateVoice, StringComparison.Ordinal), "The classifier answers in the shared candidate voice");
    }

    [Fact]
    public async Task ClassifierStreamGivesTheClassificationThenTheAnswer()
    {
        async Task<(List<string> Classifications, List<string> Chunks, Outcome Result)> Run(string transcription, InterviewLLMProvider provider, params string[] chunks)
        {
            var stub = new StubHttp((200, provider == InterviewLLMProvider.Anthropic ? StubHttp.AnthropicStream(chunks) : StubHttp.OpenAIStream(chunks)));
            using var client = new AnthropicClient("test-only", provider, stub.Client(), Settings());
            var classifications = new List<string>();
            var answer = new List<string>();
            var result = await client.ClassifyAndStreamAnswerAsync(
                transcription, "", null, null, NoHistory, c => classifications.Add($"{c.Status}|{c.Topic ?? "nil"}"), answer.Add);
            return (classifications, answer, result);
        }

        foreach (var provider in new[] { InterviewLLMProvider.Xai, InterviewLLMProvider.Anthropic })
        {
            var split = await Run("How do you design it?", provider, "STAT", "US:question|TOPIC:systemDesign\n", "---\n", "- First ", "bullet.\n- Second.");
            Check(split.Classifications.SequenceEqual(new[] { "question|systemdesign" }) && string.Concat(split.Chunks) == "- First bullet.\n- Second.",
                $"{provider}: a header split across chunks is parsed once and the answer streams after it");
            Check(split.Chunks.Count >= 2, $"{provider}: the answer arrives as it is generated");

            var drift = await Run("How do you design it?", provider, "- Closures keep ", "their scope.");
            Check(drift.Classifications.SequenceEqual(new[] { "question|unknown" }) && string.Concat(drift.Chunks) == "- Closures keep their scope.",
                $"{provider}: a reply without a header is used as the answer");

            var statement = await Run("Great answer", provider, "STATUS:statement|TOPIC:none\n---\n- Should not show.");
            Check(statement.Classifications.SequenceEqual(new[] { "statement|nil" }) && statement.Chunks.Count == 0 && statement.Result.IsSuccess,
                $"{provider}: a statement ends the turn without an answer");

            var protectedQuestion = await Run("Compare RAG and CAG", provider, "STATUS:filler|TOPIC:none\n---\n- Both ground the model.");
            Check(protectedQuestion.Classifications.SequenceEqual(new[] { "question|ragCag" }) && string.Concat(protectedQuestion.Chunks) == "- Both ground the model.",
                $"{provider}: a clear technical question is answered even when the model calls it filler");

            var empty = await Run("Tell me more", provider);
            Check(empty.Classifications.SequenceEqual(new[] { "question|followUp" }) && empty.Chunks.Count == 0, $"{provider}: an empty reply still classifies as a question");
        }
    }

    [Fact]
    public async Task StreamFailuresCarryTheProvidersMessage()
    {
        async Task<(Outcome Result, int Calls)> Run(params (int, string)[] replies)
        {
            var stub = new StubHttp(replies);
            using var client = new AnthropicClient("test-only", client: stub.Client(), settings: Settings());
            var result = await client.StreamTextMessageAsync("prompt", _ => { });
            return (result, stub.Calls.Count);
        }

        var unauthorized = await Run((401, "{\"error\":{\"message\":\"Invalid key\"}}"));
        Check(unauthorized.Result.Error is ApiClientException { Domain: "Invalid key", Code: 401 } && unauthorized.Calls == 1, "A 4xx is reported once with the provider's message");
        Check(unauthorized.Result.Error!.Message == "The operation couldn’t be completed. (Invalid key error 401.)", "The message reads as it does on macOS");
        var plain = await Run((400, "bad request"));
        Check(plain.Result.Error is ApiClientException { Domain: "HTTP 400: bad request\n" }, "A plain error body is quoted");
        var down = await Run((500, "one"), (502, "{\"error\":\"two\"}"));
        Check(down.Result.Error is ApiClientException { Domain: "two", Code: 502 } && down.Calls == 2, "A 5xx is retried once");
        var recovered = await Run((503, ""), (200, StubHttp.OpenAIStream("- ok")));
        Check(recovered.Result.IsSuccess && recovered.Calls == 2, "A retry that succeeds is a success");

        var network = new StubHttp(_ => throw new HttpRequestException("connection reset"));
        using var offline = new AnthropicClient("test-only", client: network.Client(), settings: Settings());
        var failed = await offline.StreamTextMessageAsync("prompt", _ => { });
        Check(failed.Error is HttpRequestException && network.Calls.Count == 2 && ApiHttp.IsNetworkError(failed.Error!), "A network failure is retried once and reported");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = await offline.StreamTextMessageAsync("prompt", _ => { }, cancellationToken: cancelled.Token);
        Check(stopped.Error is OperationCanceledException && network.Calls.Count == 2, "A cancelled turn makes no request");
    }

    [Fact]
    public async Task MessagesRetryServerErrorsButNotClientErrors()
    {
        var rejected = new StubHttp((400, "{\"error\":{\"message\":\"Bad prompt\"}}"));
        using var client = new AnthropicClient("test-only", client: rejected.Client(), settings: Settings());
        var error = await Assert.ThrowsAsync<ApiClientException>(() => client.SendMessageAsync("p"));
        Check(error.Domain == "InterviewLLMClient" && error.Code == 400 && error.Message == "Bad prompt" && rejected.Calls.Count == 1, "A 4xx is not retried");

        var flaky = new StubHttp((500, "{}"), (200, StubHttp.ChatReply("  Hello.\n")));
        using var retried = new AnthropicClient("test-only", client: flaky.Client(), settings: Settings());
        var (text, latency) = await retried.SendMessageAsync("p", 50);
        Check(text == "Hello." && flaky.Calls.Count == 2 && latency >= 400, "A 5xx is retried after a pause and the reply is trimmed");
        Check(flaky.Calls[0].Json["max_tokens"]!.GetValue<int>() == 50, "The token cap is sent");

        var claude = new StubHttp((200, "{\"content\":[{\"type\":\"text\",\"text\":\" Hi \"}]}"));
        using var anthropic = new AnthropicClient("test-only", InterviewLLMProvider.Anthropic, claude.Client(), Settings());
        Check((await anthropic.SendMessageAsync("p")).Text == "Hi", "Claude's reply format is read");

        var analysis = new StubHttp((200, StubHttp.ChatReply("TOPICS:\noop\n- systemDesign\nSTRENGTHS:\n- clear\nWEAKNESSES:\nscaling\nINSIGHTS:\nGood depth.")));
        using var analyst = new AnthropicClient("test-only", client: analysis.Client(), settings: Settings());
        var result = await analyst.AnalyzeSessionAsync("Interviewer: What is OOP?");
        Check(result.Topics.SequenceEqual(new[] { "oop", "systemDesign" }) && result.Strengths.SequenceEqual(new[] { "clear" })
            && result.Weaknesses.SequenceEqual(new[] { "scaling" }) && result.Insights.SequenceEqual(new[] { "Good depth." }), "Session analysis is read by section");
        Check(await analyst.SummarizeConversationAsync("") == "" && analysis.Calls.Count == 1, "Nothing to summarise means no request");
    }

    [Fact]
    public async Task ScreenshotsAreSentInEachProvidersImageFormat()
    {
        var stub = new StubHttp((200, StubHttp.OpenAIStream("code")));
        using var grok = new AnthropicClient("test-only", client: stub.Client(), settings: Settings());
        var chunks = new List<string>();
        Check((await grok.SendMessageStreamAsync(new[] { "QUJD" }, "Solve this.", chunks.Add, "## Solution\n")).IsSuccess, "The image request completes");
        Check(chunks.SequenceEqual(new[] { "## Solution\n", "code" }), "The prefill is shown before the streamed reply");
        var body = stub.Calls[0].Json;
        var content = body["messages"]![0]!["content"]!.AsArray();
        Check(content[0]!["image_url"]!["url"]!.GetValue<string>() == "data:image/png;base64,QUJD" && content[1]!["text"]!.GetValue<string>() == "Solve this."
            && body["messages"]![1]!["content"]!.GetValue<string>() == "## Solution\n" && body["max_tokens"]!.GetValue<int>() == AppConstants.MaxTokens.ImageAnalysis,
            "Grok gets image_url parts, the prompt and the prefill");

        var claudeStub = new StubHttp((200, StubHttp.AnthropicStream("code")));
        using var claude = new AnthropicClient("test-only", InterviewLLMProvider.Anthropic, claudeStub.Client(), Settings());
        await claude.SendMessageStreamAsync(new[] { "QUJD" }, "Solve this.", _ => { });
        var source = claudeStub.Calls[0].Json["messages"]![0]!["content"]![0]!["source"]!;
        Check(source["type"]!.GetValue<string>() == "base64" && source["media_type"]!.GetValue<string>() == "image/png" && source["data"]!.GetValue<string>() == "QUJD",
            "Claude gets base64 image blocks");
    }

    [Fact]
    public async Task GroqTranscriptionUploadsTheAudioAndRetries()
    {
        var settings = Settings();
        var audio = Encoding.UTF8.GetBytes("RIFF0000WAVEfmt audio");
        var stub = new StubHttp((200, "{\"text\":\" What is polymorphism? \"}"));
        using var groq = new GroqInterviewClient("secret-key", stub.Client(), settings);
        var (text, _) = await groq.TranscribeAsync(audio);
        Check(text == " What is polymorphism? ", "The transcript comes back untouched");
        var call = stub.Calls[0];
        var boundary = call.Headers["Content-Type"].Split("boundary=")[1];
        Check(call.Uri.AbsoluteUri == AppConstants.ApiUrls.GroqTranscriptions && call.Headers["Authorization"] == "Bearer secret-key", "Speech goes to Groq with the key");
        Check(call.Text == $"--{boundary}\r\nContent-Disposition: form-data; name=\"model\"\r\n\r\n{AppConstants.Models.GroqWhisper}\r\n"
            + $"--{boundary}\r\nContent-Disposition: form-data; name=\"language\"\r\n\r\n{settings.LanguageCode}\r\n"
            + $"--{boundary}\r\nContent-Disposition: form-data; name=\"prompt\"\r\n\r\n{GroqInterviewClient.CappedWhisperPrompt(settings.WhisperVocabulary)}\r\n"
            + $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\nRIFF0000WAVEfmt audio\r\n--{boundary}--\r\n",
            "The upload is the same multipart form the macOS app sends");

        var denied = new StubHttp((401, "{\"error\":{\"message\":\"Invalid API Key\"}}"));
        using var locked = new GroqInterviewClient("k", denied.Client(), settings);
        var error = await Assert.ThrowsAsync<ApiClientException>(() => locked.TranscribeAsync(audio));
        Check(error.Domain == "GroqClient" && error.Code == 401 && error.Message.HasPrefix("HTTP 401: {\"error\"") && denied.Calls.Count == 1, "A rejected key is reported once");

        var flaky = new StubHttp((200, "{\"nope\":1}"), (200, "{\"text\":\"second\"}"));
        using var retried = new GroqInterviewClient("k", flaky.Client(), settings);
        Check((await retried.TranscribeAsync(audio)).Text == "second" && flaky.Calls.Count == 2, "An unreadable reply is retried");
    }

    [Fact]
    public void WhisperPromptIsCutAtAWholeTerm()
    {
        var terms = string.Join(", ", Enumerable.Range(0, 300).Select(index => $"term{index}"));
        var capped = GroqInterviewClient.CappedWhisperPrompt(terms);
        Check(Encoding.UTF8.GetByteCount(capped) <= AppConstants.Limits.GroqWhisperPromptBytes && terms.HasPrefix(capped + ",") && !capped.HasSuffix(","), "A long vocabulary ends at a whole term");
        var cyrillic = string.Join(", ", Enumerable.Range(0, 120).Select(index => $"термин{index}"));
        Check(Encoding.UTF8.GetByteCount(GroqInterviewClient.CappedWhisperPrompt(cyrillic)) <= AppConstants.Limits.GroqWhisperPromptBytes, "The cap counts bytes, not letters");
        Check(GroqInterviewClient.CappedWhisperPrompt("short, list") == "short, list", "A short vocabulary is sent whole");
        Check(GroqInterviewClient.CappedWhisperPrompt(new string('x', 1000)).Length == AppConstants.Limits.GroqWhisperPromptBytes, "Without commas the cut is at the byte limit");
        Check(GroqInterviewClient.CappedWhisperPrompt("👩‍👩‍👧‍👦, abc, def", 30) == "👩‍👩‍👧‍👦", "A family emoji is never split");
    }

    [Fact]
    public async Task GroqAnswersAreCueCards()
    {
        var settings = Settings();
        var plain = new StubHttp((200, StubHttp.ChatReply("Polymorphism lets objects differ.")));
        using var groq = new GroqInterviewClient("k", plain.Client(), settings);
        Check((await groq.GenerateAnswerAsync("oop", "What is polymorphism?")).Answer == "- Polymorphism lets objects differ.", "A reply without a bullet gets one");
        var body = plain.Calls[0].Json;
        var messages = body["messages"]!.AsArray();
        Check(body["model"]!.GetValue<string>() == AppConstants.Models.GroqFastAnswer && body["max_tokens"]!.GetValue<int>() == AppConstants.MaxTokens.GroqAnswer
            && body["temperature"]!.GetValue<double>() == 0.3 && body["reasoning_effort"]!.GetValue<string>() == "low" && !body["include_reasoning"]!.GetValue<bool>(),
            "The answer request is tuned for speed");
        Check(messages.Count == 2 && messages[1]!["role"]!.GetValue<string>() == "assistant" && messages[1]!["content"]!.GetValue<string>() == "- ", "The reply is prefilled as a bullet");
        var prompt = messages[0]!["content"]!.GetValue<string>();
        Check(prompt.HasPrefix("Cue-card interview answer the candidate can say out loud.\n3-5 bullets only.") && prompt.Contains("\nQ: \"What is polymorphism?\"\nTOPIC: oop\n", StringComparison.Ordinal)
            && prompt.Contains(AnswerPromptBlocks.CandidateVoice, StringComparison.Ordinal), $"The prompt asks for a cue card in the candidate's voice: {prompt}");

        var failing = new StubHttp((500, "{\"error\":{\"message\":\"boom\"}}"));
        using var broken = new GroqInterviewClient("k", failing.Client(), settings);
        Check((await broken.GenerateFollowUpAnswerAsync("oop", "More", "context")).Answer == "API error: boom", "An API error is shown as the answer");

        var streaming = new StubHttp((200, StubHttp.OpenAIStream("Poly", "morphism.")));
        using var live = new GroqInterviewClient("k", streaming.Client(), settings);
        var chunks = new List<string>();
        Check((await live.StreamAnswerAsync("oop", "What is polymorphism?", chunks.Add)).IsSuccess && chunks.SequenceEqual(new[] { "- Poly", "morphism." }), "Only the first streamed chunk gets the bullet");
        Check(streaming.Calls[0].Json["stream"]!.GetValue<bool>(), "The streaming request asks for a stream");

        var forbidden = new StubHttp((403, "no"));
        using var blocked = new GroqInterviewClient("k", forbidden.Client(), settings);
        var failure = await blocked.StreamAnswerAsync("oop", "q", _ => { });
        Check(failure.Error is ApiClientException { Domain: "GroqClient", Code: 403, Message: "HTTP 403" } && forbidden.Calls.Count == 1, "A refused stream fails once so the caller can fall back");
    }

    [Fact]
    public async Task GroqClassifiesStatusAndTopic()
    {
        Check(GroqInterviewClient.ParseClassification("question,arrayList", "x") == new GroqInterviewClient.UtteranceClassification("question", "arraylist"), "status,topic is read in lower case");
        Check(GroqInterviewClient.ParseClassification("STATUS: question, TOPIC: none", "x").Status == "status", "a labelled reply keeps the macOS reading");
        Check(GroqInterviewClient.ParseClassification("incomplete,none", "What is the").Topic is null, "none is no topic");
        Check(GroqInterviewClient.ParseClassification("question", "Tell me more").Topic == "followUp", "a vague prompt with no topic is a follow-up");
        Check(GroqInterviewClient.ParseClassification("question", "Tell me more about hash maps").Topic == "unknown", "a prompt that names a subject is not a vague follow-up");
        Check(GroqInterviewClient.ParseClassification(null, "x") == new GroqInterviewClient.UtteranceClassification("question", "unknown"), "no reply defaults to a question");

        var stub = new StubHttp((200, StubHttp.ChatReply("question,arrayList")));
        using var groq = new GroqInterviewClient("k", stub.Client(), Settings());
        var (classification, _) = await groq.ClassifyUtteranceAsync("ray list?", "What is", "arrayList");
        Check(classification.Topic == "arraylist", "The reply is parsed");
        var body = stub.Calls[0].Json;
        var prompt = body["messages"]![0]!["content"]!.GetValue<string>();
        Check(body["temperature"]!.GetValue<double>() == 0 && body["max_tokens"]!.GetValue<int>() == AppConstants.MaxTokens.GroqClassification
            && prompt.HasPrefix("Classify this utterance. Return: STATUS,TOPIC\n\nText: \"What is ray list?\"\nLast topic: arrayList\n\nIMPORTANT:")
            && prompt.HasSuffix("Return ONLY: STATUS,TOPIC (e.g., \"question,array\")"), "The classify prompt carries the buffered text and last topic");
    }

    [Fact]
    public async Task PracticeClientScoresHelpsAndSpeaks()
    {
        var judge = new StubHttp((200, StubHttp.ChatReply("```json\n{\"score\": 1.7, \"feedback\": \"Over.\", \"strengths\": [\"clear\"]}\n```")));
        using var client = new PracticeGroqClient("secret-key", judge.Client());
        var scored = await client.JudgeAnswerAsync("What is a closure?", "A function that keeps its scope.", "Mentions scope.");
        Check(scored.Score == 1 && scored.Feedback == "Over." && scored.Strengths.SequenceEqual(new[] { "clear" }) && scored.Gaps.Count == 0, "A fenced JSON verdict is read and the score clamped");
        var prompt = judge.Calls[0].Json["messages"]![0]!["content"]!.GetValue<string>();
        Check(prompt.Contains("\n\nRubric (from the question bank):\nMentions scope.\n\nQuestion: What is a closure?\nAnswer: A function that keeps its scope.", StringComparison.Ordinal), "The rubric is sent with the answer");

        var vague = new StubHttp((200, StubHttp.ChatReply(" Looks fine to me. ")));
        using var fallback = new PracticeGroqClient("k", vague.Client());
        var heuristic = await fallback.JudgeAnswerAsync("What is a closure?", "A function that keeps its scope.");
        Check(heuristic.Score == Practice.HeuristicRawScore("What is a closure?", "A function that keeps its scope.") && heuristic.Feedback == "Looks fine to me.",
            "Without JSON the reply becomes the feedback and the score is estimated");
        Check(PracticeGroqClient.ParseJudgeJson("} {") is null && PracticeGroqClient.ParseJudgeJson("{\"score\":\"high\",\"feedback\":\"x\"}") is null, "A malformed verdict is not trusted");

        var speech = new StubHttp((200, "WAVDATA"));
        using var speaker = new PracticeGroqClient("k", speech.Client());
        Check((await speaker.SynthesizeSpeechAsync(new string('a', 250))).Length == 7, "Speech comes back as audio bytes");
        var payload = speech.Calls[0].Json;
        Check(payload["input"]!.GetValue<string>() == new string('a', 197) + "..." && payload["voice"]!.GetValue<string>() == "austin"
            && payload["response_format"]!.GetValue<string>() == "wav", "Long text is clipped for the voice model");

        var down = new StubHttp((500, "overloaded"));
        using var failing = new PracticeGroqClient("k", down.Client());
        var error = await Assert.ThrowsAsync<ApiClientException>(() => failing.HelpCueCardAsync("q"));
        Check(error.Domain == "PracticeGroqChat" && error.Code == 500 && error.Message == "HTTP 500: overloaded" && down.Calls.Count == 1, "Practice calls report the failure without retrying");
    }

    private sealed class FakeAnalyst : AnthropicClient
    {
        public string? Transcript { get; private set; }
        public bool Fail { get; init; }

        public FakeAnalyst() : base("test-no-network")
        {
        }

        public override Task<SessionAnalysisResult> AnalyzeSessionAsync(string transcript, CancellationToken cancellationToken = default)
        {
            Transcript = transcript;
            if (Fail) throw new InvalidOperationException("offline");
            return Task.FromResult(new SessionAnalysisResult(new[] { "oop" }, new[] { "clear" }, new[] { "scaling" }, new[] { "Good depth." }));
        }
    }

    [Fact]
    public async Task SessionAnalysisRemembersTheInterview()
    {
        var directory = Path.Combine(Path.GetTempPath(), "interview-master-analysis-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MemoryStore(directory);
            var date = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
            var useCase = new SessionAnalysisUseCase(store, () => date);
            var messages = new[]
            {
                new InterviewMessage(MessageType.Status, "Listening"),
                new InterviewMessage(MessageType.Question, "What is OOP?", topic: "oop"),
                new InterviewMessage(MessageType.Answer, "- Objects."),
                new InterviewMessage(MessageType.CodingTask, "```js\n1\n```", topic: "code"),
                new InterviewMessage(MessageType.Question, "And SOLID?")
            };
            using var analyst = new FakeAnalyst();
            await useCase.Analyze(messages, "session.md", analyst);
            Check(analyst.Transcript == "Interviewer [oop]: What is OOP?\nAnswer: - Objects.\nCode Solution [code]: ```js\n1\n```\nInterviewer: And SOLID?", $"The transcript names each speaker: {analyst.Transcript}");
            var summary = store.GetSessionSummaries().Single();
            Check(summary.SourceFile == "session.md" && summary.QuestionsAsked == 2 && summary.Topics.SequenceEqual(new[] { "oop" })
                && summary.KeyInsights.SequenceEqual(new[] { "Good depth." }) && summary.Date == date, "The session is remembered with its topics and insights");
            Check(store.GetUserProfile().Strengths.SequenceEqual(new[] { "clear" }) && store.GetUserProfile().Weaknesses.SequenceEqual(new[] { "scaling" }), "Strengths and gaps update the profile");

            using var idle = new FakeAnalyst();
            await useCase.Analyze(messages.Take(2).ToArray(), "short.md", idle);
            Check(idle.Transcript is null, "A session with one exchange is not analysed");
            using var offline = new FakeAnalyst { Fail = true };
            await useCase.Analyze(messages, "failed.md", offline);
            Check(store.GetSessionSummaries().Count == 1, "A failed analysis leaves memory alone");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
