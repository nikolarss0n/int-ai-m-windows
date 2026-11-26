using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Practice-only Groq client. Talks to the same Groq HTTP APIs as live assist
/// without adding Help, TTS, or scoring onto GroqInterviewClient.
public sealed class PracticeGroqClient : IDisposable
{
    public sealed record Judgement(double Score, string Feedback, IReadOnlyList<string> Strengths, IReadOnlyList<string> Gaps);

    private const string ChatUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string WhisperUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string SpeechUrl = "https://api.groq.com/openai/v1/audio/speech";
    private const string TtsModel = "canopylabs/orpheus-v1-english";
    private const string TtsVoice = "austin";
    private const double RequestTimeoutSeconds = 20;

    private readonly string apiKey;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    public PracticeGroqClient(string apiKey, HttpClient? client = null)
    {
        this.apiKey = apiKey;
        ownsClient = client is null;
        this.client = client ?? ApiHttp.CreateClient();
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }

    public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        var upload = GroqRequestTuning.TranscriptionUpload(audioData);
        var boundary = Guid.NewGuid().ToString().ToUpperInvariant();

        using var body = new MemoryStream();
        void Append(string text) => body.Write(Encoding.UTF8.GetBytes(text));
        void Field(string name, string value)
        {
            Append($"--{boundary}\r\n");
            Append($"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            Append($"{value}\r\n");
        }
        Field("model", AppConstants.Models.GroqWhisper);
        Field("language", "en");
        Append($"--{boundary}\r\n");
        Append($"Content-Disposition: form-data; name=\"file\"; filename=\"{upload.Filename}\"\r\n");
        Append($"Content-Type: {upload.MimeType}\r\n\r\n");
        body.Write(audioData);
        Append($"\r\n--{boundary}--\r\n");

        using var request = new HttpRequestMessage(HttpMethod.Post, WhisperUrl) { Content = new ByteArrayContent(body.ToArray()) };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
        var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        ThrowIfHttpError(status, data, "PracticeGroqSTT");
        using var document = StrictJson.ParseObject(data);
        return StrictJson.RequiredString(document.RootElement, "text").Trim();
    }

    internal static string HelpCueCardPrompt(string question) => string.Join("\n", new[]
    {
        "Cue-card hints for a candidate who asked for help. 3-5 short bullets.",
        "Every line starts with \"- \". Under 90 characters. Plain text, no markdown.",
        "Cover definition, one example, one trade-off. Do not write a full speech.",
        "",
        $"Question: {question}"
    });

    public Task<string> HelpCueCardAsync(string question, CancellationToken cancellationToken = default) =>
        ChatAsync(HelpCueCardPrompt(question), 280, 0.3, cancellationToken);

    internal static string JudgePrompt(string question, string answer, string rubric)
    {
        var rubricBlock = rubric.Trim().Length == 0 ? "" : $"\nRubric (from the question bank):\n{rubric}\n";
        return string.Join("\n", new[]
        {
            "Score this interview answer from 0 to 1. JSON only, no markdown:",
            "{\"score\":0.0,\"feedback\":\"one short paragraph\",\"strengths\":[\"...\"],\"gaps\":[\"...\"]}",
            "Score raw quality only. Ignore whether the candidate used notes.",
            rubricBlock,
            $"Question: {question}",
            $"Answer: {answer}"
        });
    }

    public async Task<Judgement> JudgeAnswerAsync(
        string question, string answer, string rubric = "", CancellationToken cancellationToken = default)
    {
        var raw = await ChatAsync(JudgePrompt(question, answer, rubric), 320, 0.2, cancellationToken).ConfigureAwait(false);
        if (ParseJudgeJson(raw) is { } parsed) return parsed;
        var fallback = Practice.HeuristicRawScore(question, answer);
        return new Judgement(fallback, raw.Trim(), Array.Empty<string>(), Array.Empty<string>());
    }

    public async Task<byte[]> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["model"] = TtsModel,
            ["input"] = ClipForOrpheus(text),
            ["voice"] = TtsVoice,
            ["response_format"] = "wav"
        };
        using var request = ApiHttp.JsonPost(SpeechUrl, ApiHttp.Encode(payload), apiKey);
        var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        ThrowIfHttpError(status, data, "PracticeGroqTTS");
        return data;
    }

    private async Task<string> ChatAsync(string prompt, int maxTokens, double temperature, CancellationToken cancellationToken)
    {
        var body = GroqChat.RequestBody(new[] { GroqChat.UserMessage(prompt) }, maxTokens, temperature, AppConstants.Models.GroqFastAnswer);
        using var request = ApiHttp.JsonPost(ChatUrl, ApiHttp.Encode(body), apiKey);
        var (status, data) = await ApiHttp.SendAsync(client, request, RequestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        ThrowIfHttpError(status, data, "PracticeGroqChat");

        using var document = StrictJson.ParseObject(data);
        return StrictJson.ChatChoices(document.RootElement, choicesRequired: false, contentRequired: false).FirstContent?.Trim() ?? "";
    }

    private static void ThrowIfHttpError(int status, byte[] data, string domain)
    {
        if (status is >= 200 and < 300) return;
        var body = ApiHttp.Utf8(data) ?? "";
        throw new ApiClientException(domain, status, $"HTTP {status}: {body.Prefix(240)}");
    }

    internal static string ClipForOrpheus(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed.Substring(0, 197) + "...";
    }

    internal static Judgement? ParseJudgeJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start) return null;
        try
        {
            using var document = JsonDocument.Parse(text.Substring(start, end - start + 1));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var score = StrictJson.Required(root, "score", JsonValueKind.Number).GetDouble();
            var feedback = StrictJson.RequiredString(root, "feedback");
            return new Judgement(Math.Min(1, Math.Max(0, score)), feedback, Strings(root, "strengths"), Strings(root, "gaps"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> Strings(JsonElement root, string name)
    {
        var result = new List<string>();
        if (StrictJson.Optional(root, name, JsonValueKind.Array) is not { } items) return result;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new JsonException($"Expected strings in \"{name}\".");
            result.Add(item.GetString() ?? "");
        }
        return result;
    }
}
