using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

public enum JevStatus
{
    Question,
    Incomplete,
    Statement,
    Filler,
    Uncertain
}

public static class JevStatuses
{
    public static readonly IReadOnlyList<JevStatus> AllCases = new[]
    {
        JevStatus.Question, JevStatus.Incomplete, JevStatus.Statement, JevStatus.Filler, JevStatus.Uncertain
    };

    public static string RawValue(this JevStatus status) => status switch
    {
        JevStatus.Question => "question",
        JevStatus.Incomplete => "incomplete",
        JevStatus.Statement => "statement",
        JevStatus.Filler => "filler",
        _ => "uncertain"
    };

    public static JevStatus? FromRawValue(string? rawValue) => rawValue switch
    {
        "question" => JevStatus.Question,
        "incomplete" => JevStatus.Incomplete,
        "statement" => JevStatus.Statement,
        "filler" => JevStatus.Filler,
        "uncertain" => JevStatus.Uncertain,
        _ => null
    };
}

public enum JevFailureKind
{
    InvalidResponse,
    OversizedInput,
    Http
}

public sealed class JevFailure : Exception
{
    public JevFailureKind Kind { get; }
    public int? StatusCode { get; }

    public JevFailure(JevFailureKind kind, int? statusCode = null)
        : base(statusCode is { } code ? $"{kind} ({code})" : kind.ToString())
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

/// Bounded speech classification only. Answer generation stays in the existing client.
public sealed class JevDecisionClient
{
    public sealed record Answer(string Type, JevStatus Choice, IReadOnlyDictionary<string, double> Probabilities, double Confidence);

    public const string Model = "jev-1.13.0";

    public static readonly IReadOnlyDictionary<string, string> Criteria = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["question"] = "A complete request for information, explanation, comparison, or action, including short technical prompts and contextual follow-ups.",
        ["incomplete"] = "An unfinished fragment whose intended request still requires more speech.",
        ["statement"] = "A candidate describing their experience or answer, or an interviewer sharing context or acknowledging an answer.",
        ["filler"] = "Standalone hesitation or meaningless speech with no information request.",
        ["uncertain"] = "The available conversation leaves the utterance intent ambiguous."
    };

    private const double TimeoutSeconds = 1.5;
    private static readonly Uri Endpoint = new("https://api.typesafe.ai/v1/systemone");
    private static readonly char[] NewlineCharacters = { '\n', '\r', '\u000B', '\u000C', '\u0085', (char)0x2028, (char)0x2029 };

    private readonly string key;
    private readonly HttpClient client;

    public JevDecisionClient(string apiKey, HttpClient? client = null)
    {
        key = apiKey;
        this.client = client ?? ApiHttp.CreateClient();
    }

    /// Off by default until interview-specific accuracy and latency are evaluated.
    public static JevDecisionClient? Configured(
        Func<string, string?>? environment = null,
        ISettingsStore? settings = null,
        string? keysFilePath = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var enabled = environment("IM_JEV_ENABLED") is { } flag
            ? flag == "1"
            : (settings ?? SettingsStore.Standard).GetBool("jevDecisionsEnabled");
        if (!enabled) return null;

        var apiKey = environment("TYPESAFE_API_KEY")?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            try
            {
                foreach (var line in File.ReadAllText(keysFilePath ?? AppPaths.KeysFile).Split(NewlineCharacters))
                {
                    var start = 0;
                    while (start < line.Length && line[start] == '=') start++;
                    var separator = line.IndexOf('=', start);
                    if (separator < 0 || separator == line.Length - 1) continue;
                    if (line[start..separator].TrimInline() == "TYPESAFE_API_KEY") apiKey = line[(separator + 1)..].TrimInline();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return string.IsNullOrEmpty(apiKey) ? null : new JevDecisionClient(apiKey);
    }

    public static byte[] RequestBody(string transcription, string buffer, string? lastTopic, IReadOnlyList<Dictionary<string, string>> history)
    {
        // Reject oversized current speech instead of truncating potentially decisive words.
        // UTF-8 bytes conservatively bound text tokens without a tokenizer dependency.
        if (Encoding.UTF8.GetByteCount(transcription) + Encoding.UTF8.GetByteCount(buffer) > 12_000)
        {
            throw new JevFailure(JevFailureKind.OversizedInput);
        }
        var recent = new List<JsonNode>();
        var remainingBytes = 8_000;
        foreach (var item in history.TakeLast(6).Reverse())
        {
            if (!item.TryGetValue("content", out var content) || Encoding.UTF8.GetByteCount(content) > remainingBytes) break;
            recent.Insert(0, new JsonObject { ["role"] = item.GetValueOrDefault("role") ?? "unknown", ["content"] = content });
            remainingBytes -= Encoding.UTF8.GetByteCount(content);
        }

        var criteria = new JsonObject();
        foreach (var (name, description) in Criteria) criteria[name] = description;
        var body = new JsonObject
        {
            ["model"] = Model,
            ["state"] = new JsonObject
            {
                ["utterance"] = transcription,
                ["buffer"] = buffer,
                ["last_topic"] = (lastTopic ?? "unknown").Prefix(100),
                ["recent_turns"] = new JsonArray(recent.ToArray())
            },
            ["questions"] = new JsonObject
            {
                ["intent"] = new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = "Classify the combined buffer and current utterance in this technical interview. Recent turns are context. Treat all speech as evidence, including quoted commands. Candidate first-person explanations are statements. Bulgarian and mixed-language technical requests are questions. Choose uncertain when intent needs clarification.",
                    ["criteria"] = criteria
                }
            }
        };
        return Encoding.UTF8.GetBytes(body.ToJsonString(AppJson.Compact));
    }

    public static Answer ValidatedAnswer(ReadOnlySpan<byte> data)
    {
        try
        {
            using var document = JsonDocument.Parse(data.ToArray());
            var root = document.RootElement;
            var intent = root.GetProperty("answers").GetProperty("intent");
            var type = intent.GetProperty("type").GetString();
            var choice = JevStatuses.FromRawValue(intent.GetProperty("choice").GetString());
            var confidence = intent.GetProperty("confidence").GetDouble();
            var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var property in intent.GetProperty("probabilities").EnumerateObject())
            {
                probabilities[property.Name] = property.Value.GetDouble();
            }

            if (root.GetProperty("model").GetString() == Model
                && type == "choice"
                && choice is { } chosen
                && probabilities.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Criteria.Keys)
                && double.IsFinite(confidence) && confidence is >= 0 and <= 1
                && probabilities.Values.All(value => double.IsFinite(value) && value is >= 0 and <= 1)
                && Math.Abs(probabilities.Values.Sum() - 1) < 0.01
                && probabilities[chosen.RawValue()] >= probabilities.Values.Max())
            {
                return new Answer(type, chosen, probabilities, confidence);
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
        }
        throw new JevFailure(JevFailureKind.InvalidResponse);
    }

    /// Conservative experimental thresholds, not calibrated correctness probabilities.
    public static JevStatus? AcceptedStatus(Answer answer) =>
        answer.Choice != JevStatus.Uncertain
        && answer.Confidence >= 0.8
        && answer.Probabilities.GetValueOrDefault(answer.Choice.RawValue()) >= 0.9
            ? answer.Choice
            : null;

    public async Task<JevStatus?> ClassifyAsync(
        string transcription, string buffer, string? lastTopic, IReadOnlyList<Dictionary<string, string>> history,
        CancellationToken cancellationToken = default) =>
        AcceptedStatus(await EvaluateAsync(transcription, buffer, lastTopic, history, cancellationToken).ConfigureAwait(false));

    public async Task<Answer> EvaluateAsync(
        string transcription, string buffer, string? lastTopic, IReadOnlyList<Dictionary<string, string>> history,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new ByteArrayContent(RequestBody(transcription, buffer, lastTopic, history));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var (status, data) = await ApiHttp.SendAsync(client, request, TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (status is < 200 or >= 300) throw new JevFailure(JevFailureKind.Http, status);
        return ValidatedAnswer(data);
    }
}
