using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

var raw = File.ReadAllBytes(args[0]);
using var document = JsonDocument.Parse(raw);
var json = document.RootElement;
var lines = new List<string>();

string Esc(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
void Emit(string key, string value)
{
    lock (lines) lines.Add(key + "\t" + Esc(value));
}
Stub.Emit = Emit;

List<string> Strings(JsonElement element) => element.EnumerateArray().Select(item => item.GetString()!).ToList();
string Bool(bool value) => value ? "true" : "false";
byte[] Data(string text) => Encoding.UTF8.GetBytes(text);
string? NullableString(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

var texts = Strings(json.GetProperty("texts"));
var cv = json.GetProperty("cv").GetString()!;
var configs = json.GetProperty("configs").EnumerateArray().ToList();
var sse = json.GetProperty("sse").EnumerateArray().ToList();
var defaultSse = json.GetProperty("defaultSse");
var answerPrompts = json.GetProperty("answerPrompts").EnumerateArray().ToList();
var classifyVariants = json.GetProperty("classifyVariants").EnumerateArray().ToList();
var sequences = json.GetProperty("sequences");
var providers = new[] { ("xai", InterviewLLMProvider.Xai), ("openai", InterviewLLMProvider.OpenAI), ("anthropic", InterviewLLMProvider.Anthropic) };

void Apply(JsonElement c)
{
    var s = AppSettings.Shared;
    s.Role = InterviewRoles.FromRawValue(c[0].GetString())!.Value;
    s.ProgrammingLanguage = ProgrammingLanguages.FromRawValue(c[1].GetString())!.Value;
    s.Frameworks = c[2].GetString()!;
    s.ListeningLanguage = SpeakingLanguages.FromRawValue(c[3].GetString())!.Value;
    s.ResponseLanguage = SpeakingLanguages.FromRawValue(c[4].GetString())!.Value;
    s.OpenAIReasoningEffort = c[5].GetString()!;
    s.OpenAIPriority = c[6].GetBoolean();
}

HttpClient StubClient() => new(new Stub());
AnthropicClient Anthropic(InterviewLLMProvider provider) => new("test-only", provider, StubClient());
GroqInterviewClient Groq() => new("test-only", StubClient());
PracticeGroqClient PracticeClient() => new("test-only", StubClient());
string Wire(InterviewLLMProvider provider) => provider == InterviewLLMProvider.Anthropic ? "anthropic" : "openai";

string Describe(Exception error) => error is ApiClientException api ? $"{api.Domain}|{api.Code}|{api.Message}" : "other";
string Result(Outcome outcome) => outcome.IsSuccess ? "success" : "failure " + Describe(outcome.Error!);
string Cls(string status, string? topic) => $"{status}|{topic ?? "nil"}";
string Analysis(AnthropicClient.SessionAnalysisResult r) =>
    string.Join("§", new[] { r.Topics, r.Strengths, r.Weaknesses, r.Insights }.Select(list => string.Join("¦", list)));
string Judge(PracticeGroqClient.Judgement? p) => p is null
    ? "nil"
    : p.Score.ToString("F6", CultureInfo.InvariantCulture) + "|" + p.Feedback + "|" + string.Join("¦", p.Strengths) + "|" + string.Join("¦", p.Gaps);

List<(int, byte[])> Responses(JsonElement sequence, string fallback, string nullContent = "") =>
    sequence.GetProperty("responses").EnumerateArray().Select(item =>
    {
        var body = item[1].ValueKind == JsonValueKind.String ? item[1].GetString() : null;
        return (item[0].GetInt32(), Data(body is null ? fallback : body == "NULLCONTENT" ? nullContent : body));
    }).ToList();

async Task Classify(AnthropicClient client, string t, string buffer, string? lastTopic, bool withCv, List<Dictionary<string, string>> history)
{
    var result = await client.ClassifyAndStreamAnswerAsync(
        t, buffer, lastTopic, withCv ? cv : null, history,
        c => Emit("CLS", Cls(c.Status, c.Topic)), chunk => Emit("CHUNK", chunk));
    Emit("RESULT", Result(result));
}
Task ClassifyVariant(AnthropicClient client, JsonElement v) => Classify(
    client, v.GetProperty("t").GetString()!, v.GetProperty("buffer").GetString()!, NullableString(v, "lastTopic"), v.GetProperty("cv").GetBoolean(),
    v.GetProperty("history").EnumerateArray()
        .Select(item => new Dictionary<string, string> { ["role"] = item[0].GetString()!, ["content"] = item[1].GetString()! }).ToList());
string AnswerPrompt(GroqInterviewClient groq, JsonElement p) => groq.AnswerPrompt(
    p.GetProperty("topic").GetString()!, p.GetProperty("q").GetString()!, p.GetProperty("cv").GetBoolean() ? cv : null,
    NullableString(p, "context"), p.GetProperty("followUp").GetBoolean(), p.GetProperty("inherited").GetBoolean(), NullableString(p, "pinned"));
async Task Text(Func<Task<string>> call)
{
    try
    {
        Emit("TEXT", await call());
    }
    catch (Exception error)
    {
        Emit("ERROR", Describe(error));
    }
}

// PURE
Apply(configs[0]);
Emit("SYSTEM_PROMPT", AnthropicClient.ClassificationSystemPrompt);
foreach (var text in texts)
{
    Emit("TEXT", text);
    Emit("MARKER", Bool(AnthropicClient.HasTechnicalQuestionMarker(text)));
    Emit("VAGUE_A", Bool(FollowUpPrompts.IsVague(text.Lowercased())));
    Emit("VAGUE_G", Bool(FollowUpPrompts.IsVague(text.Lowercased())));
    foreach (var (status, topic) in new (string, string?)[] { ("question", "unknown"), ("statement", null), ("filler", "unknown"), ("incomplete", "java"), ("question", "Unknown") })
    {
        var c = AnthropicClient.AdjustedClassification(new AnthropicClient.UtteranceClassification(status, topic), text);
        Emit("ADJ", Cls(c.Status, c.Topic));
    }
}
foreach (var text in Strings(json.GetProperty("classifications")))
{
    var c = AnthropicClient.ParseClassification(text);
    Emit("PARSE", Cls(c.Status, c.Topic));
}
foreach (var item in json.GetProperty("errorBodies").EnumerateArray())
{
    Emit("ERRMSG", AnthropicClient.ExtractErrorMessage(item[0].GetString()!, item[1].GetInt32()));
}
foreach (var text in Strings(json.GetProperty("analysis"))) Emit("ANALYSIS", Analysis(AnthropicClient.ParseAnalysisResponse(text)));
foreach (var text in Strings(json.GetProperty("whisper"))) Emit("WHISPER", GroqInterviewClient.CappedWhisperPrompt(text));
foreach (var topic in new[] { "solid", "rag", "CAG", "ragCag", "oop", "" }) Emit("GUIDANCE", GroqInterviewClient.TopicSpecificAnswerGuidance(topic));
foreach (var reply in Strings(json.GetProperty("judgeReplies"))) Emit("JUDGE", Judge(PracticeGroqClient.ParseJudgeJson(reply)));
foreach (var text in new[] { new string('a', 250), "  short  ", new string('б', 200), new string('б', 201) }) Emit("CLIP", PracticeGroqClient.ClipForOrpheus(text));

// CONFIGS
for (var index = 0; index < configs.Count; index++)
{
    Apply(configs[index]);
    Emit("CONFIG", index.ToString(CultureInfo.InvariantCulture));
    Emit("TOPICS", AnthropicClient.TopicsForSettings(AppSettings.Shared));
    var groq = Groq();
    foreach (var p in answerPrompts) Emit("ANSWER_PROMPT", AnswerPrompt(groq, p));
    foreach (var (name, provider) in providers)
    {
        var sseBody = Data(defaultSse.GetProperty(Wire(provider)).GetString()!);
        foreach (var v in classifyVariants)
        {
            Stub.Set((200, sseBody));
            Emit("CALL", $"classify {name} {v.GetProperty("name").GetString()}");
            await ClassifyVariant(Anthropic(provider), v);
        }
        var client = Anthropic(provider);
        Stub.Set((200, sseBody));
        Emit("CALL", $"streamText {name}");
        Emit("RESULT", Result(await client.StreamTextMessageAsync("Explain closures briefly.", chunk => Emit("CHUNK", chunk), 120)));
        Emit("CALL", $"images {name}");
        Emit("RESULT", Result(await client.SendMessageStreamAsync(new[] { "QUJD", "REVG" }, "Solve this.", chunk => Emit("CHUNK", chunk), "## Solution\n")));
        Emit("RESULT", Result(await client.SendMessageStreamAsync(Array.Empty<string>(), "No images.", chunk => Emit("CHUNK", chunk))));
        Stub.Set((200, Data(json.GetProperty(provider == InterviewLLMProvider.Anthropic ? "anthropicMessage" : "openaiMessage").GetString()!)));
        Emit("CALL", $"sendMessage {name}");
        await Text(async () => (await client.SendMessageAsync("Say hi", 50)).Text);
        await Text(() => client.SummarizeConversationAsync("Q: What is OOP?\nA: - Objects"));
        await Text(() => client.SummarizeConversationAsync(""));
        await Text(async () => Analysis(await client.AnalyzeSessionAsync("Interviewer [oop]: What is OOP?\nAnswer [oop]: - Objects")));
    }
    Emit("CALL", "groq");
    Stub.Set((200, Data(json.GetProperty("openaiMessage").GetString()!)));
    await Text(async () => (await groq.GenerateAnswerAsync("oop", "What is polymorphism?", cv)).Answer);
    await Text(async () => (await groq.GenerateFollowUpAnswerAsync("rag", "Tell me more", "Q: What is RAG?\nA: - RAG retrieves.", cv)).Answer);
    await Text(async () => (await groq.GenerateFollowUpAnswerAsync("oop", "Go deeper", "")).Answer);
    try
    {
        var c = (await groq.ClassifyUtteranceAsync("ray list?", "What is", "arrayList")).Classification;
        Emit("CLS", Cls(c.Status, c.Topic));
        var d = (await groq.ClassifyUtteranceAsync("Tell me more", "", null)).Classification;
        Emit("CLS", Cls(d.Status, d.Topic));
    }
    catch (Exception error)
    {
        Emit("ERROR", Describe(error));
    }
    Stub.Set((200, Data(defaultSse.GetProperty("openai").GetString()!)));
    var prompt = answerPrompts[3];
    Emit("RESULT", Result(await groq.StreamAnswerAsync(
        prompt.GetProperty("topic").GetString()!, prompt.GetProperty("q").GetString()!, chunk => Emit("CHUNK", chunk), cv,
        NullableString(prompt, "context"), contextIsFollowUp: false, topicInherited: true, pinnedSolution: NullableString(prompt, "pinned"))));
    Stub.Set((200, Data("{\"text\":\" spoken words \"}")));
    await Text(async () => (await groq.TranscribeAsync(Data("RIFF0000WAVEfmt audio-bytes"))).Text);
    await Text(async () => (await groq.TranscribeAsync(Data("0000ftypM4A audio"), "clip.m4a")).Text);
}

// PRACTICE
Apply(configs[0]);
var practice = PracticeClient();
Emit("CALL", "practice");
Stub.Set((200, Data(json.GetProperty("openaiMessage").GetString()!)));
await Text(() => practice.HelpCueCardAsync("What is a closure?"));
foreach (var (body, rubric) in Strings(json.GetProperty("judgeBodies")).Zip(new[] { "Mentions scope.\nGives an example.", "", "  ", "r", "r", "r" }))
{
    Stub.Set((200, Data(body)));
    try
    {
        Emit("JUDGE", Judge(await practice.JudgeAnswerAsync("What is a closure?", "A function that keeps its scope, for example a counter.", rubric)));
    }
    catch (Exception error)
    {
        Emit("ERROR", Describe(error));
    }
}
Stub.Set((200, Data("WAVDATA")));
await Text(async () => (await practice.SynthesizeSpeechAsync(string.Concat(Enumerable.Repeat("word ", 80)))).Length.ToString(CultureInfo.InvariantCulture));
Stub.Set((200, Data("{\"text\":\" hi there \"}")));
await Text(() => practice.TranscribeAsync(Data("RIFF0000WAVEfmt practice")));

// STREAM PARSING
Stub.Record = false;
foreach (var (name, provider) in new[] { providers[0], providers[2] })
{
    foreach (var item in sse)
    {
        foreach (var transcription in new[] { "What is polymorphism?", "Great answer", "Compare RAG and CAG", "Tell me more" })
        {
            Stub.Set((200, Data(item.GetProperty(Wire(provider)).GetString()!)));
            Emit("CALL", $"parse {name} {item.GetProperty("name").GetString()} {transcription}");
            await Classify(Anthropic(provider), transcription, "", null, false, new List<Dictionary<string, string>>());
        }
    }
}

// HTTP SEQUENCES
foreach (var (name, provider) in new[] { providers[0], providers[2] })
{
    var fallback = defaultSse.GetProperty(Wire(provider)).GetString()!;
    foreach (var sequence in sequences.GetProperty("stream").EnumerateArray())
    {
        Stub.Set(Responses(sequence, fallback));
        Emit("CALL", $"sequence stream {name} {sequence.GetProperty("name").GetString()}");
        await ClassifyVariant(Anthropic(provider), classifyVariants[0]);
        Stub.Set(Responses(sequence, fallback));
        Emit("RESULT", Result(await Anthropic(provider).StreamTextMessageAsync("p", chunk => Emit("CHUNK", chunk))));
        Stub.Set(Responses(sequence, fallback));
        Emit("RESULT", Result(await Anthropic(provider).SendMessageStreamAsync(new[] { "QUJD" }, "p", chunk => Emit("CHUNK", chunk))));
    }
    var isAnthropic = provider == InterviewLLMProvider.Anthropic;
    foreach (var sequence in sequences.GetProperty("message").EnumerateArray())
    {
        Stub.Set(Responses(
            sequence,
            json.GetProperty(isAnthropic ? "anthropicMessage" : "openaiMessage").GetString()!,
            json.GetProperty(isAnthropic ? "nullContentAnthropic" : "nullContentOpenAI").GetString()!));
        Emit("CALL", $"sequence message {name} {sequence.GetProperty("name").GetString()}");
        await Text(async () => (await Anthropic(provider).SendMessageAsync("p")).Text);
    }
}
foreach (var sequence in sequences.GetProperty("groqAnswer").EnumerateArray())
{
    Emit("CALL", $"sequence groqAnswer {sequence.GetProperty("name").GetString()}");
    Stub.Set(Responses(sequence, ""));
    await Text(async () => (await Groq().GenerateAnswerAsync("oop", "What is OOP?")).Answer);
    Stub.Set(Responses(sequence, ""));
    await Text(async () => (await Groq().GenerateFollowUpAnswerAsync("oop", "More", "c")).Answer);
}
foreach (var sequence in sequences.GetProperty("groqStream").EnumerateArray())
{
    Emit("CALL", $"sequence groqStream {sequence.GetProperty("name").GetString()}");
    Stub.Set(Responses(sequence, ""));
    Emit("RESULT", Result(await Groq().StreamAnswerAsync("oop", "What is OOP?", chunk => Emit("CHUNK", chunk))));
}
foreach (var sequence in sequences.GetProperty("groqTranscribe").EnumerateArray())
{
    Emit("CALL", $"sequence groqTranscribe {sequence.GetProperty("name").GetString()}");
    Stub.Set(Responses(sequence, ""));
    await Text(async () => (await Groq().TranscribeAsync(Data("RIFF0000WAVEfmt x"))).Text);
}
foreach (var body in Strings(json.GetProperty("groqClassifyBodies")))
{
    foreach (var text in new[] { "Tell me more", "What is ray list?" })
    {
        Stub.Set((200, Data(body)));
        try
        {
            var c = (await Groq().ClassifyUtteranceAsync(text, "", null)).Classification;
            Emit("CLS", Cls(c.Status, c.Topic));
        }
        catch (Exception error)
        {
            Emit("ERROR", Describe(error));
        }
    }
}
foreach (var sequence in sequences.GetProperty("practiceChat").EnumerateArray())
{
    Emit("CALL", $"sequence practice {sequence.GetProperty("name").GetString()}");
    Stub.Set(Responses(sequence, ""));
    await Text(() => PracticeClient().HelpCueCardAsync("q"));
    Stub.Set(Responses(sequence, ""));
    await Text(async () => (await PracticeClient().SynthesizeSpeechAsync("hello")).Length.ToString(CultureInfo.InvariantCulture));
    Stub.Set(Responses(sequence, ""));
    await Text(() => PracticeClient().TranscribeAsync(Data("RIFF0000WAVEfmt x")));
}

File.WriteAllText(args[1], string.Join("\n", lines) + "\n", new UTF8Encoding(false));

sealed class Stub : HttpMessageHandler
{
    private static readonly List<(int Status, byte[] Body)> Queue = new();
    public static bool Record = true;
    public static Action<string, string> Emit = (_, _) => { };

    public static void Set(params (int, byte[])[] responses) => Set(responses.ToList());

    public static void Set(List<(int, byte[])> responses)
    {
        lock (Queue)
        {
            Queue.Clear();
            Queue.AddRange(responses);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        (int Status, byte[] Body) response;
        lock (Queue)
        {
            response = Queue[0];
            if (Queue.Count > 1) Queue.RemoveAt(0);
        }
        if (Record)
        {
            var headers = new Dictionary<string, string>();
            foreach (var header in request.Headers) headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers) headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
            }
            string? boundary = null;
            if (headers.TryGetValue("content-type", out var type) && type.IndexOf("boundary=", StringComparison.Ordinal) is var at and >= 0)
            {
                boundary = type.Substring(at + "boundary=".Length);
            }
            var headerText = string.Join("; ", new[] { "authorization", "x-api-key", "anthropic-version", "anthropic-beta", "content-type" }
                .Where(headers.ContainsKey).Select(key => $"{key}={headers[key]}"));
            var text = Encoding.UTF8.GetString(body);
            if (boundary is not null)
            {
                headerText = headerText.Replace(boundary, "BOUNDARY");
                text = text.Replace(boundary, "BOUNDARY");
            }
            Emit("REQ", $"{request.Method.Method} {request.RequestUri!.AbsoluteUri} [{headerText}]");
            Emit(boundary is not null ? "BODYTEXT" : body.Length == 0 ? "BODYNONE" : "BODYJSON", text);
        }
        return new HttpResponseMessage((HttpStatusCode)response.Status) { Content = new ByteArrayContent(response.Body) };
    }
}
