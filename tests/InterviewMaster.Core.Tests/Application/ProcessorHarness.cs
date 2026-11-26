using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

// This harness links the real processor, clients, domain models, and exporter.
// Only transport responses and clocks are faked; no production logic is copied.
internal static class Harness
{
    /// Fails a test boundedly if a route stops reaching a controlled callback.
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
}

internal sealed class Pipe<T>
{
    private readonly Channel<T> channel = Channel.CreateUnbounded<T>();

    public void Send(T value) => channel.Writer.TryWrite(value);

    public Task<T> Next() => channel.Reader.ReadAsync().AsTask().WaitAsync(Harness.Limit);
}

internal sealed class CompletionEvents
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> sources = new();

    private TaskCompletionSource Source(Guid id) =>
        sources.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public void Complete(Guid id) => Source(id).TrySetResult();

    public Task Wait(Guid id) => Source(id).Task.WaitAsync(Harness.Limit);
}

internal sealed class BufferClock
{
    private readonly object gate = new();
    private DateTime value = DateTime.UnixEpoch.AddSeconds(100);
    private int reads;

    public DateTime Now()
    {
        lock (gate)
        {
            reads++;
            return value;
        }
    }

    public void Advance(double seconds)
    {
        lock (gate) value = value.AddSeconds(seconds);
    }

    public int ReadCount
    {
        get
        {
            lock (gate) return reads;
        }
    }
}

internal sealed class TranscriptionCall(string input)
{
    public string Input { get; } = input;
    public Pipe<string> Response { get; } = new();
}

internal sealed class AnswerCall(Action<string> onChunk)
{
    public Action<string> OnChunk { get; } = onChunk;
    public Pipe<Outcome> Completion { get; } = new();
}

internal enum AnswerMode
{
    Success,
    Empty,
    Held
}

internal sealed record AnswerRequest(string Topic, bool TopicInherited, string? Context, string? PinnedSolution);

internal sealed class FakeGroqClient : GroqInterviewClient
{
    private readonly AnswerMode answerMode;
    private readonly string? heldTranscription;
    private readonly object requestLock = new();
    private readonly List<AnswerRequest> recorded = new();
    private int cancellationCount;

    public Pipe<TranscriptionCall> TranscriptionCalls { get; } = new();
    public Pipe<AnswerCall> AnswerCalls { get; } = new();
    public int CancellationCount => Volatile.Read(ref cancellationCount);

    public List<AnswerRequest> Requests
    {
        get
        {
            lock (requestLock) return recorded.ToList();
        }
    }

    public FakeGroqClient(AnswerMode answerMode = AnswerMode.Success, string? heldTranscription = null)
        : base("test-no-network")
    {
        this.answerMode = answerMode;
        this.heldTranscription = heldTranscription;
    }

    public override Task WarmupConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override void CancelCurrentRequest() => Interlocked.Increment(ref cancellationCount);

    public override async Task<(string Text, double LatencyMs)> TranscribeAsync(
        byte[] audioData, string? filename = null, CancellationToken cancellationToken = default)
    {
        var input = Encoding.UTF8.GetString(audioData);
        if (input != heldTranscription) return (input, 0);
        var call = new TranscriptionCall(input);
        TranscriptionCalls.Send(call);
        return (await call.Response.Next(), 0);
    }

    public override async Task<Outcome> StreamAnswerAsync(
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
        lock (requestLock) recorded.Add(new AnswerRequest(topic, topicInherited, context, pinnedSolution));
        switch (answerMode)
        {
            case AnswerMode.Success:
                onChunk("- The answer comes from the deterministic Groq transport.");
                return Outcome.Success;
            case AnswerMode.Empty:
                return Outcome.Success;
            default:
                var call = new AnswerCall(onChunk);
                AnswerCalls.Send(call);
                return await call.Completion.Next();
        }
    }
}

internal sealed record ClassificationResponse(string Status, string Topic, string Answer);

internal sealed class ClassificationCall(string transcription, string buffer)
{
    public string Transcription { get; } = transcription;
    public string Buffer { get; } = buffer;
    public Pipe<ClassificationResponse> Response { get; } = new();

    public void Finish(string status, string topic = "architecture", string answer = "") =>
        Response.Send(new ClassificationResponse(status, topic, answer));
}

internal sealed class FakeAnthropicClient : AnthropicClient
{
    private readonly object logLock = new();
    private readonly List<string> loggedPrompts = new();
    private int classifications;
    private int cancellationCount;

    public Pipe<ClassificationCall> ClassificationCalls { get; } = new();
    public int CancellationCount => Volatile.Read(ref cancellationCount);
    public string DirectReply { get; set; } = "- The answer comes from the deterministic direct transport.";
    public string CodingReply { get; set; } = "";

    /// Replies for consecutive coding prompts, oldest first; `CodingReply` answers once they run out.
    public List<string> CodingReplies { get; set; } = new();

    public FakeAnthropicClient() : base("test-no-network")
    {
    }

    private string NextCodingReply()
    {
        lock (logLock)
        {
            if (CodingReplies.Count == 0) return CodingReply;
            var reply = CodingReplies[0];
            CodingReplies.RemoveAt(0);
            return reply;
        }
    }

    public List<string> Prompts
    {
        get
        {
            lock (logLock) return loggedPrompts.ToList();
        }
    }

    public int ClassificationCount
    {
        get
        {
            lock (logLock) return classifications;
        }
    }

    public override Task WarmupConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override void CancelCurrentRequest() => Interlocked.Increment(ref cancellationCount);

    public override Task<string> SummarizeConversationAsync(string conversationText, CancellationToken cancellationToken = default) =>
        Task.FromResult("Test conversation summary");

    public override Task<Outcome> StreamTextMessageAsync(
        string prompt, Action<string> onChunk, int maxTokens = 300, CancellationToken cancellationToken = default)
    {
        lock (logLock) loggedPrompts.Add(prompt);
        var isCodingPrompt = prompt.Contains("live coding interview copilot", StringComparison.Ordinal);
        // Deliver the reply in small pieces, the way a real stream arrives.
        var codingAnswer = isCodingPrompt ? NextCodingReply() : "";
        var reply = isCodingPrompt && codingAnswer.Length > 0 ? codingAnswer : DirectReply;
        for (var index = 0; index < reply.Length; index += 24)
        {
            onChunk(reply.Substring(index, Math.Min(24, reply.Length - index)));
        }
        return Task.FromResult(Outcome.Success);
    }

    public override async Task<Outcome> ClassifyAndStreamAnswerAsync(
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
        lock (logLock) classifications++;
        var call = new ClassificationCall(transcription, buffer);
        ClassificationCalls.Send(call);
        var response = await call.Response.Next();
        onClassification(new UtteranceClassification(response.Status, response.Topic));
        if (response.Answer.Length > 0) onAnswerChunk(response.Answer);
        return Outcome.Success;
    }
}

/// Callbacks arrive on the stand-in UI thread while the test reads from its own, so state is guarded.
internal sealed class RecordingDelegate : IVoiceInterviewProcessorDelegate
{
    private readonly object gate = new();
    private readonly HashSet<Guid> observedTurns = new();
    private readonly List<InterviewMessage> messages = new();
    private readonly Dictionary<Guid, string> answers = new();
    private readonly List<string> codingUpdates = new();
    private readonly List<CodingTaskInfo> codingStarts = new();
    private readonly List<CodingTaskOutcome> codingOutcomes = new();
    private readonly List<string> statuses = new();

    public string UserBackground => "";
    public string? PinnedSolution => null;
    public ConversationContext ConversationContext { get; } = new();
    public Pipe<Guid> Started { get; } = new();
    public Pipe<Guid> Chunks { get; } = new();
    public CompletionEvents Hidden { get; } = new();
    public CompletionEvents Finished { get; } = new();
    public Pipe<CodingTaskOutcome> CodingFinished { get; } = new();

    public List<InterviewMessage> Messages
    {
        get
        {
            lock (gate) return messages.ToList();
        }
    }

    public string? Answer(Guid turnId)
    {
        lock (gate) return answers.GetValueOrDefault(turnId);
    }

    public List<string> CodingUpdates
    {
        get
        {
            lock (gate) return codingUpdates.ToList();
        }
    }

    public List<CodingTaskInfo> CodingStarts
    {
        get
        {
            lock (gate) return codingStarts.ToList();
        }
    }

    public List<CodingTaskOutcome> CodingOutcomes
    {
        get
        {
            lock (gate) return codingOutcomes.ToList();
        }
    }

    public List<string> Statuses
    {
        get
        {
            lock (gate) return statuses.ToList();
        }
    }

    public void ProcessorShowLoading(string message, LoadingColor color, Guid turnId)
    {
        bool first;
        lock (gate) first = observedTurns.Add(turnId);
        if (first) Started.Send(turnId);
    }

    public void ProcessorHideLoading(Guid turnId) => Hidden.Complete(turnId);

    public void ProcessorDidReceiveQuestion(string text, string topic, MessageType messageType, CaptureDevice source, Guid turnId, int sequence)
    {
        lock (gate)
        {
            messages.Add(new InterviewMessage(MessageType.Question, text, topic, audioSource: source, turnId: turnId, turnSequence: sequence));
        }
    }

    public void ProcessorDidStartStreaming(MessageType messageType, string topic, int? latencyMs, Guid turnId, int sequence)
    {
    }

    public void ProcessorDidReceiveAnswerChunk(string fullContent, Guid turnId)
    {
        lock (gate) answers[turnId] = fullContent;
        Chunks.Send(turnId);
    }

    public void ProcessorDidFinishAnswer(string fullAnswer, int? totalLatencyMs, Guid turnId)
    {
        lock (gate) answers[turnId] = fullAnswer;
        Finished.Complete(turnId);
    }

    public void ProcessorDidUpdateStatus(string message)
    {
        lock (gate) statuses.Add(message);
    }

    public void ProcessorDidUpdateBriefing(InterviewBriefing briefing)
    {
    }

    public void ProcessorDidUpdateCodingTask(string content)
    {
        lock (gate) codingUpdates.Add(content);
    }

    public void ProcessorDidStartCodingTask(CodingTaskInfo info)
    {
        lock (gate) codingStarts.Add(info);
    }

    public void ProcessorDidFinishCodingTask(CodingTaskOutcome outcome)
    {
        lock (gate) codingOutcomes.Add(outcome);
        CodingFinished.Send(outcome);
    }
}
