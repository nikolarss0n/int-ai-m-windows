using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Carries what the macOS clients put in an NSError: a domain, a code and the text shown to the user.
public sealed class ApiClientException : Exception
{
    public string Domain { get; }
    public int Code { get; }

    public ApiClientException(string domain, int code, string? description = null)
        : base(description ?? $"The operation couldn’t be completed. ({domain} error {code}.)")
    {
        Domain = domain;
        Code = code;
    }
}

/// A finished streaming call: either it completed or it carries the error that stopped it.
public readonly record struct Outcome(Exception? Error)
{
    public static readonly Outcome Success = new((Exception?)null);

    public static Outcome Failure(Exception error) => new(error);

    public bool IsSuccess => Error is null;
}

/// A response whose body is read line by line, with the same idle timeout URLSession applies.
internal sealed class StreamedResponse : IDisposable
{
    private readonly HttpResponseMessage response;
    private readonly StreamReader reader;
    private readonly CancellationTokenSource idle;
    private readonly CancellationTokenSource total;
    private readonly TimeSpan idleTimeout;
    private readonly CancellationToken caller;

    public StreamedResponse(
        HttpResponseMessage response, StreamReader reader, CancellationTokenSource idle, CancellationTokenSource total,
        TimeSpan idleTimeout, CancellationToken caller)
    {
        this.response = response;
        this.reader = reader;
        this.idle = idle;
        this.total = total;
        this.idleTimeout = idleTimeout;
        this.caller = caller;
    }

    public int StatusCode => (int)response.StatusCode;

    /// The next line, or null at the end of the body.
    public async Task<string?> ReadLineAsync()
    {
        idle.CancelAfter(idleTimeout);
        try
        {
            return await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!caller.IsCancellationRequested)
        {
            throw new TimeoutException(ApiHttp.TimedOutDescription, error);
        }
    }

    public void Dispose()
    {
        reader.Dispose();
        response.Dispose();
        idle.Dispose();
        total.Dispose();
    }
}

internal static class ApiHttp
{
    public const string TimedOutDescription = "The request timed out.";
    public const int MaxAttempts = 3;

    private static readonly double[] BackoffSeconds = { 0.5, 1.0, 1.5 };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// Timeouts are enforced per request, so the client itself never times out.
    public static HttpClient CreateClient(int? maxConnectionsPerHost = null)
    {
        var handler = new SocketsHttpHandler();
        if (maxConnectionsPerHost is { } limit) handler.MaxConnectionsPerServer = limit;
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// What the macOS clients call a network error: the request never produced a usable response.
    public static bool IsNetworkError(Exception error) =>
        error is HttpRequestException or IOException or TimeoutException or SocketException;

    public static HttpRequestMessage JsonPost(string url, byte[] body, string bearerKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerKey}");
        return request;
    }

    public static byte[] Encode(JsonNode body) => Encoding.UTF8.GetBytes(body.ToJsonString(AppJson.Compact));

    public static JsonArray MessageArray(IEnumerable<IReadOnlyDictionary<string, string>> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var item = new JsonObject();
            foreach (var (key, value) in message) item[key] = value;
            array.Add(item);
        }
        return array;
    }

    /// `String(data:encoding:)`: null when the bytes are not UTF-8.
    public static string? Utf8(byte[] data)
    {
        try
        {
            return StrictUtf8.GetString(data);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static async Task<(int Status, byte[] Data)> SendAsync(
        HttpClient client, HttpRequestMessage request, double timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            var data = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            return ((int)response.StatusCode, data);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(TimedOutDescription, error);
        }
    }

    public static async Task<StreamedResponse> OpenStreamAsync(
        HttpClient client, HttpRequestMessage request, double idleSeconds, double? totalSeconds, CancellationToken cancellationToken)
    {
        var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (totalSeconds is { } limit) total.CancelAfter(TimeSpan.FromSeconds(limit));
        var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        var idleTimeout = TimeSpan.FromSeconds(idleSeconds);
        idle.CancelAfter(idleTimeout);
        HttpResponseMessage? response = null;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            return new StreamedResponse(response, new StreamReader(stream, Encoding.UTF8), idle, total, idleTimeout, cancellationToken);
        }
        catch (Exception error)
        {
            response?.Dispose();
            idle.Dispose();
            total.Dispose();
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(TimedOutDescription, error);
            }
            throw;
        }
    }

    /// Three attempts with 0.5 s, 1 s backoff. A 4xx from `domain` is the caller's fault and is not retried.
    public static async Task<T> WithRetriesAsync<T>(
        string domain, string exhaustedDescription, Func<Task<T>> attempt, CancellationToken cancellationToken,
        Action<Exception>? onNonRetryable = null, Action<int, Exception>? onRetry = null, Action<Exception>? onExhausted = null)
    {
        Exception? lastError = null;
        for (var index = 0; index < MaxAttempts; index++)
        {
            try
            {
                return await attempt().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
                if (error is ApiClientException { Code: >= 400 and < 500 } clientError && clientError.Domain == domain)
                {
                    onNonRetryable?.Invoke(error);
                    throw;
                }
                lastError = error;
                if (index < MaxAttempts - 1)
                {
                    onRetry?.Invoke(index, error);
                    await Task.Delay(TimeSpan.FromSeconds(BackoffSeconds[index]), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        if (lastError is not null)
        {
            onExhausted?.Invoke(lastError);
            throw lastError;
        }
        throw new ApiClientException(domain, -1, exhaustedDescription);
    }

    /// One server-sent-events line. `Done` for the terminator, a JSON object for a data line, neither otherwise.
    public static (bool Done, JsonElement? Json) ParseEventLine(string line)
    {
        if (!line.HasPrefix("data: ")) return (false, null);
        var payload = line.Substring(6);
        if (payload == "[DONE]") return (true, null);
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object ? (false, document.RootElement.Clone()) : (false, null);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    /// Text of an OpenAI-compatible stream delta; null when the event carries no visible text.
    public static string? OpenAIDeltaText(JsonElement json)
    {
        if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return null;
        JsonElement? first = null;
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object) return null;
            first ??= choice;
        }
        if (first is not { } head
            || !head.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object
            || !delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var text = content.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// Text of an Anthropic `content_block_delta` event.
    public static string? AnthropicDeltaText(JsonElement json) =>
        json.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "content_block_delta"
        && json.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
        && delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
}

/// Reads JSON the way the macOS `Codable` structs do: a missing or mistyped required member is an error.
internal static class StrictJson
{
    public static JsonDocument ParseObject(byte[] data)
    {
        var document = JsonDocument.Parse(data);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new JsonException("Expected a JSON object.");
        }
        return document;
    }

    /// The member, or null when it is absent or JSON null.
    public static JsonElement? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    public static JsonElement Required(JsonElement element, string name, JsonValueKind kind)
    {
        if (Member(element, name) is { } value && value.ValueKind == kind) return value;
        throw new JsonException($"Expected {kind} \"{name}\".");
    }

    public static JsonElement? Optional(JsonElement element, string name, JsonValueKind kind)
    {
        if (Member(element, name) is not { } value) return null;
        if (value.ValueKind != kind) throw new JsonException($"Expected {kind} \"{name}\".");
        return value;
    }

    public static string RequiredString(JsonElement element, string name) =>
        Required(element, name, JsonValueKind.String).GetString() ?? "";

    public static string? OptionalString(JsonElement element, string name) =>
        Optional(element, name, JsonValueKind.String)?.GetString();

    /// The `choices[].message.content` of a chat completion; the first choice's content is returned.
    public static (bool HasChoices, string? FirstContent) ChatChoices(JsonElement root, bool choicesRequired, bool contentRequired)
    {
        var choices = choicesRequired ? Required(root, "choices", JsonValueKind.Array) : Optional(root, "choices", JsonValueKind.Array);
        if (choices is not { } list) return (false, null);
        string? first = null;
        var index = 0;
        foreach (var choice in list.EnumerateArray())
        {
            var message = Required(choice, "message", JsonValueKind.Object);
            var content = contentRequired ? RequiredString(message, "content") : OptionalString(message, "content");
            if (index++ == 0) first = content;
        }
        return (true, first);
    }

    /// The optional `error.message` some chat completions return with a 5xx.
    public static string? ErrorMessage(JsonElement root) =>
        Optional(root, "error", JsonValueKind.Object) is { } error ? RequiredString(error, "message") : null;
}

/// "Tell me more" style prompts that name no subject of their own.
internal static class FollowUpPrompts
{
    private static readonly string[] TopicMarkers =
    {
        "hash", "array", "list", "oop", "solid", "playwright", "selenium",
        "cypress", "test", "api", "contract", "fixture", "locator", "mock",
        "flaky", "trace", "worker", "sharding", "retry", "retries",
        "thread", "deadlock", "jvm", "jdk", "garbage", "closure",
        "event loop", "docker", "kubernetes", "linux", "sql", "database",
        "llm", "rag", "cag", "embedding", "vector", "retrieval",
        "хеш", "ооп", "полиморф", "наследяване", "капсулация"
    };

    private static readonly HashSet<string> ExactFollowUps = new(StringComparer.Ordinal)
    {
        "tell me more", "can you elaborate", "could you elaborate", "elaborate",
        "what else", "anything else", "go deeper", "more details",
        "can you expand", "expand on that", "continue"
    };

    /// `lowercased` is the utterance already in lower case.
    public static bool IsVague(string lowercased)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
                lowercased.Replace("’", "'").Replace("-", " ").Replace("_", " "), @"\s+", " ")
            .Trim(' ', '.', ',', '!', '?', ':', ';');

        if (TopicMarkers.Any(marker => cleaned.Contains(marker, StringComparison.Ordinal))) return false;
        if (ExactFollowUps.Contains(cleaned)) return true;
        return cleaned.Contains("give me an example", StringComparison.Ordinal)
            || cleaned.Contains("more about that", StringComparison.Ordinal)
            || cleaned.Contains("expand on this", StringComparison.Ordinal);
    }
}
