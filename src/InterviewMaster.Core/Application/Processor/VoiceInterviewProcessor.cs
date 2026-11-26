using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

/// The colour of the loading line; the window maps each to its own tint.
public enum LoadingColor
{
    SystemBlue,
    ApplePurple,
    AppleGreen
}

/// Protocol for voice interview processor callbacks
public interface IVoiceInterviewProcessorDelegate
{
    // UI Callbacks
    void ProcessorShowLoading(string message, LoadingColor color, Guid turnId);
    void ProcessorHideLoading(Guid turnId);
    void ProcessorDidReceiveQuestion(string text, string topic, MessageType messageType, CaptureDevice source, Guid turnId, int sequence);
    void ProcessorDidStartStreaming(MessageType messageType, string topic, int? latencyMs, Guid turnId, int sequence);
    void ProcessorDidReceiveAnswerChunk(string fullContent, Guid turnId);
    void ProcessorDidFinishAnswer(string fullAnswer, int? totalLatencyMs, Guid turnId);
    void ProcessorDidUpdateStatus(string message);
    void ProcessorDidUpdateBriefing(InterviewBriefing briefing);
    void ProcessorDidUpdateCodingTask(string content);

    /// Code for a spoken task is about to stream in.
    void ProcessorDidStartCodingTask(CodingTaskInfo info)
    {
    }

    void ProcessorDidFinishCodingTask(CodingTaskOutcome outcome)
    {
    }

    // Data Access
    string UserBackground { get; }
    string? PinnedSolution { get; }
    ConversationContext ConversationContext { get; }
}

/// Handles voice interview audio processing: transcription, classification, and answer generation
public partial class VoiceInterviewProcessor
{
    public IVoiceInterviewProcessorDelegate? Delegate { get; set; }

    private readonly IMainThread mainThread;

    // Clients (injected)
    private GroqInterviewClient? groqClient;
    private AnthropicClient? anthropicClient;

    // Deduplication state
    private readonly List<(string Text, DateTime Timestamp, CaptureDevice Source)> recentTranscriptions = new();
    private const double DedupeWindow = AppConstants.Thresholds.DedupeWindow;
    private const double SimilarityThreshold = AppConstants.Thresholds.SimilarityThreshold;

    // Utterance buffering state
    private int bufferGeneration;
    private readonly Func<DateTime> bufferNow;
    private string utteranceBuffer = "";
    private DateTime? bufferTimestamp;
    private int? utteranceBufferSequence;
    private const double BufferTimeout = AppConstants.Thresholds.BufferTimeout;

    // Answer cooldown
    private DateTime? lastAnswerTime;
    private const double AnswerCooldown = AppConstants.Thresholds.AnswerCooldown;

    // Memory retrieval
    private readonly MemoryRetrievalUseCase memoryRetrieval;
    private InterviewBriefing? currentBriefing;
    private const double TranscriptionTimeout = 12.0;
    private readonly object turnLock = new();
    private readonly object processingStateLock = new();
    private int turnGeneration;
    private int previewGeneration;
    private int nextTurnSequence = 1;
    private int summaryRevision;
    private bool summaryInFlight;
    private TurnTask? previewTask;
    private readonly Dictionary<Guid, TurnTask> processTasks = new();
    private (string Text, int ByteCount, int Generation)? previewTranscript;

    // Live-coding exercise the interviewer is dictating; guarded by processingStateLock.
    private readonly CodingSessionState codingSession = new();
    private int codingGenerationToken;

    /// What the code panel's Retry button re-runs, on the same card.
    private sealed record CodingRequest(string Task, string Text, string? Change, Guid CardTurnId, int TurnSequence);

    private CodingRequest? lastCodingRequest;

    private sealed class TranscriptionTimeoutException : Exception
    {
        public TranscriptionTimeoutException(double seconds) : base($"Groq transcription timed out after {(int)seconds}s")
        {
        }
    }

    /// A unit of background work that can be cancelled, as a Swift `Task` handle can.
    private sealed class TurnTask
    {
        private readonly CancellationTokenSource source = new();

        public CancellationToken Token => source.Token;

        public Task Task { get; set; } = System.Threading.Tasks.Task.CompletedTask;

        public void Cancel()
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    internal readonly record struct QuestionSignal(int Score, List<string> Reasons, string? TopicHint, bool IsFollowUp, bool IsBareIncomplete)
    {
        public bool ProtectsFromSkip => Score >= 3 && !IsBareIncomplete;
    }

    /// When the first talking point and the first line of code reached the screen, measured from the end
    /// of the question. Touched only on the main thread.
    private sealed class CodingStreamTiming
    {
        private int? talkingPointMs;
        private int? codeMs;

        public void Note(CodingResponse response, DateTime start)
        {
            var elapsed = (int)((DateTime.UtcNow - start).TotalSeconds * 1000);
            if (talkingPointMs is null && response.SayBullets.Count > 0) talkingPointMs = elapsed;
            if (codeMs is null && response.StepsMarkdown.Contains("```", StringComparison.Ordinal)) codeMs = elapsed;
        }

        public string Summary =>
            $"talkingPoint={(talkingPointMs is { } talking ? $"{talking}ms" : "none")} code={(codeMs is { } code ? $"{code}ms" : "none")}";
    }

    private sealed class AnswerStreamState
    {
        private readonly object gate = new();
        private string accumulatedText = "";
        private bool started;
        private bool cancelled;

        public (string Snapshot, bool IsFirst)? AppendVisibleChunk(string chunk)
        {
            lock (gate)
            {
                if (cancelled) return null;
                if (!started && chunk.Trim().Length == 0) return null;

                accumulatedText += chunk;
                var first = !started;
                started = true;
                return (accumulatedText, first);
            }
        }

        public void Cancel()
        {
            lock (gate) cancelled = true;
        }

        public bool HasStarted
        {
            get
            {
                lock (gate) return started;
            }
        }

        public string Text
        {
            get
            {
                lock (gate) return accumulatedText;
            }
        }
    }

    public VoiceInterviewProcessor(IMainThread mainThread, Func<DateTime>? bufferNow = null, MemoryRetrievalUseCase? memoryRetrieval = null)
    {
        this.mainThread = mainThread;
        this.bufferNow = bufferNow ?? (() => DateTime.UtcNow);
        this.memoryRetrieval = memoryRetrieval ?? new MemoryRetrievalUseCase();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static void DebugLog(string message, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        DebugLogger.Shared.Log(message, file, line);

    private static void DebugLog(LogCategory category, string message, [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        DebugLogger.Shared.Log(category, message, file, line);

    /// Configure the processor with API clients
    public void Configure(GroqInterviewClient? groqClient, AnthropicClient? anthropicClient)
    {
        this.groqClient = groqClient;
        this.anthropicClient = anthropicClient;
        DebugLog($"VoiceInterviewProcessor configured - groq: {Flag(groqClient is not null)}, anthropic: {Flag(anthropicClient is not null)}");
        _ = Task.Run(async () =>
        {
            if (groqClient is not null) await groqClient.WarmupConnectionAsync().ConfigureAwait(false);
            if (anthropicClient is not null) await anthropicClient.WarmupConnectionAsync().ConfigureAwait(false);
        });
    }

    /// Clear all state (call when stopping interview)
    public void Reset()
    {
        CancelInFlightTurn();
        lock (processingStateLock)
        {
            unchecked { bufferGeneration++; }
            recentTranscriptions.Clear();
            utteranceBuffer = "";
            bufferTimestamp = null;
            utteranceBufferSequence = null;
            lastAnswerTime = null;
            currentBriefing = null;
            summaryRevision++;
            summaryInFlight = false;
            codingSession.Clear();
            codingGenerationToken++;
            lastCodingRequest = null;
        }
        lock (turnLock) nextTurnSequence = 1;
    }

    /// Isolate continuation text after switching capture devices without cancelling
    /// accepted turns or their answer streams. Generation checks and buffer mutations
    /// share processingStateLock so a late old-source classifier cannot refill it.
    public void ResetCaptureBuffers()
    {
        lock (processingStateLock)
        {
            unchecked { bufferGeneration++; }
            utteranceBuffer = "";
            bufferTimestamp = null;
            utteranceBufferSequence = null;
        }
        CancelPrefetchTranscription();
    }

    public void CancelInFlightTurn()
    {
        TurnTask? currentPreview;
        List<TurnTask> currentTasks;
        lock (turnLock)
        {
            turnGeneration++;
            previewGeneration++;
            previewTranscript = null;
            currentPreview = previewTask;
            previewTask = null;
            currentTasks = processTasks.Values.ToList();
            processTasks.Clear();
        }
        currentPreview?.Cancel();
        foreach (var task in currentTasks) task.Cancel();
        groqClient?.CancelCurrentRequest();
        anthropicClient?.CancelCurrentRequest();
        DebugLog(LogCategory.Audio, "Cancelled in-flight turn");
    }

    /// Cancel speculative STT only. A resumed utterance must not cancel an already
    /// committed answer for the previous question in a rapid question burst.
    public void CancelPrefetchTranscription()
    {
        TurnTask? task;
        lock (turnLock)
        {
            previewGeneration++;
            previewTranscript = null;
            task = previewTask;
            previewTask = null;
        }
        task?.Cancel();
        DebugLog(LogCategory.Audio, "Cancelled speculative transcription preview");
    }

    /// Overlap Whisper with the remaining end-of-speech wait. Must not start an answer card.
    public void PrefetchTranscription(byte[] audioData, CaptureDevice source)
    {
        var action = SpeechTurnPolicy.Action(SpeechTurnEmit.SpeculativePreview);
        if (action != SpeechTurnAction.PrefetchTranscriptionOnly || SpeechTurnPolicy.StartsAnswerCard(action)) return;
        if (groqClient is not { } client) return;

        int generation;
        TurnTask? previous;
        lock (turnLock)
        {
            previewGeneration++;
            previewTranscript = null;
            previous = previewTask;
            previewTask = null;
            generation = previewGeneration;
        }
        previous?.Cancel();

        var task = new TurnTask();
        var token = task.Token;
        task.Task = Task.Run(async () =>
        {
            try
            {
                var (text, _) = await TranscribeWithTimeoutAsync(client, audioData, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                bool stillCurrent;
                lock (turnLock)
                {
                    stillCurrent = generation == previewGeneration;
                    if (stillCurrent) previewTranscript = (text, audioData.Length, generation);
                }
                if (stillCurrent)
                {
                    DebugLog(LogCategory.Transcription, $"Preview STT cached {text.Length} chars from {(source == CaptureDevice.Microphone ? "mic" : "system")}");
                }
            }
            catch (Exception error)
            {
                DebugLog(LogCategory.Transcription, $"Preview STT skipped: {error.Message}");
            }
        });
        bool accepted;
        lock (turnLock)
        {
            accepted = generation == previewGeneration;
            if (accepted) previewTask = task;
        }
        if (!accepted) task.Cancel();
    }

    private bool IsCurrentTurn(int generation)
    {
        lock (turnLock) return generation == turnGeneration;
    }

    private int ReserveTurnSequence()
    {
        lock (turnLock)
        {
            var sequence = nextTurnSequence;
            nextTurnSequence++;
            return sequence;
        }
    }

    private void ScheduleSummarizationIfNeeded(ConversationContext context, int generation)
    {
        if (anthropicClient is not { } client) return;

        int revision;
        string text;
        lock (processingStateLock)
        {
            if (!context.NeedsSummarization || summaryInFlight) return;
            summaryRevision++;
            summaryInFlight = true;
            revision = summaryRevision;
            text = context.GetTextForSummarization();
        }
        if (text.Length == 0)
        {
            FinishSummarization(revision);
            return;
        }

        _ = Task.Run(async () =>
        {
            if (!IsCurrentTurn(generation))
            {
                FinishSummarization(revision);
                return;
            }

            try
            {
                var summary = await client.SummarizeConversationAsync(text).ConfigureAwait(false);
                if (!IsCurrentTurn(generation))
                {
                    FinishSummarization(revision);
                    return;
                }
                lock (processingStateLock)
                {
                    if (revision != summaryRevision) return;
                    context.SetSummary(summary);
                    summaryInFlight = false;
                }
            }
            catch (Exception error)
            {
                FinishSummarization(revision);
                Log.Print($"⚠️ Summarization failed: {error}");
            }
        });
    }

    private void FinishSummarization(int revision)
    {
        lock (processingStateLock)
        {
            if (revision == summaryRevision) summaryInFlight = false;
        }
    }

    private string? CachedPreviewTranscript(byte[] audioData, int generation)
    {
        lock (turnLock)
        {
            if (previewTranscript is not { } preview || preview.Generation != generation || preview.Text.Length == 0) return null;
            var larger = Math.Max(preview.ByteCount, audioData.Length);
            if (larger <= 0) return null;
            var delta = Math.Abs(preview.ByteCount - audioData.Length);
            if (delta * 100 / larger >= 25) return null;
            return preview.Text;
        }
    }
}
