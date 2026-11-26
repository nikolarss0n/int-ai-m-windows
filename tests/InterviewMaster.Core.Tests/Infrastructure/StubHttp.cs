using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Tests;

internal static class TestEnvironment
{
    /// Clients log through the shared debug logger; keep that file out of the working directory.
    [ModuleInitializer]
    internal static void RedirectDebugLog()
    {
        DebugLogger.Shared = new DebugLogger(Path.Combine(Path.GetTempPath(), $"interview-master-tests-{Guid.NewGuid():N}.log"));
    }
}

/// Replaces only the HTTP transport, as the macOS tests do with a URLProtocol stub.
internal sealed class StubHttp : HttpMessageHandler
{
    public sealed record Call(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body)
    {
        public string Text => Encoding.UTF8.GetString(Body);

        public JsonNode Json => JsonNode.Parse(Body)!;
    }

    private readonly Func<Call, (int Status, string Body)> respond;
    private readonly List<Call> calls = new();

    public StubHttp(Func<Call, (int Status, string Body)> respond)
    {
        this.respond = respond;
    }

    /// Answers each request with the next reply; the last one repeats.
    public StubHttp(params (int Status, string Body)[] replies)
    {
        var index = 0;
        respond = _ => replies[Math.Min(Interlocked.Increment(ref index) - 1, replies.Length - 1)];
    }

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (calls) return calls.ToList();
        }
    }

    public HttpClient Client() => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers) headers[header.Key] = string.Join(", ", header.Value);
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
        }
        var call = new Call(request.Method, request.RequestUri!, headers, body);
        lock (calls) calls.Add(call);
        var (status, text) = respond(call);
        return new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(text)) };
    }

    public static string ChatReply(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    public static string OpenAIStream(params string[] chunks) =>
        string.Concat(chunks.Select(chunk => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = chunk } } } }) + "\n\n"))
        + "data: [DONE]\n\n";

    public static string AnthropicStream(params string[] chunks) =>
        "event: message_start\ndata: {\"type\":\"message_start\"}\n"
        + string.Concat(chunks.Select(chunk =>
            "data: " + JsonSerializer.Serialize(new { type = "content_block_delta", delta = new { type = "text_delta", text = chunk } }) + "\n"))
        + "data: {\"type\":\"message_stop\"}\n";
}
