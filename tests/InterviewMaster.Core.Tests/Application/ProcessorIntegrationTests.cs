using System.Text;
using System.Text.Json;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

/// Port of Tests/test_capture_processor.swift: audio in, cards out, with only the transports faked.
[Collection("SharedSettings")]
public sealed class ProcessorIntegrationTests : SwiftStyleTests
{
    private readonly AppSettings previousSettings;
    private readonly QueueMainThread main = new();

    public ProcessorIntegrationTests()
    {
        previousSettings = AppSettings.Shared;
        AppSettings.Shared = new AppSettings(new InMemorySettingsStore(new Dictionary<string, string>
        {
            ["InterviewMaster.Role"] = "software_engineer",
            ["InterviewMaster.ProgrammingLanguage"] = "swift",
            ["InterviewMaster.ListeningLanguage"] = "en",
            ["InterviewMaster.ResponseLanguage"] = "en"
        }));
    }

    public override void Dispose()
    {
        AppSettings.Shared = previousSettings;
        main.Dispose();
        base.Dispose();
    }

    private (VoiceInterviewProcessor Processor, RecordingDelegate Delegate, FakeAnthropicClient Anthropic) Setup(
        FakeGroqClient? groq = null, BufferClock? clock = null)
    {
        var bufferClock = clock ?? new BufferClock();
        var processor = new VoiceInterviewProcessor(main, bufferClock.Now);
        var recorder = new RecordingDelegate();
        var anthropic = new FakeAnthropicClient();
        processor.Delegate = recorder;
        processor.Configure(groq ?? new FakeGroqClient(), anthropic);
        return (processor, recorder, anthropic);
    }

    private static async Task<Guid> Begin(string text, CaptureDevice device, VoiceInterviewProcessor processor, RecordingDelegate recorder)
    {
        processor.ProcessAudioSegment(Encoding.UTF8.GetBytes(text), device);
        return await recorder.Started.Next();
    }

    /// Waits for the turn's loading line to go, then for every callback queued before it.
    private async Task Hidden(RecordingDelegate recorder, Guid turn)
    {
        await recorder.Hidden.Wait(turn);
        await main.RunAsync(() => { });
    }

    private async Task Settle() => await main.RunAsync(() => { });

    private void CheckExport(RecordingDelegate recorder, CaptureDevice device)
    {
        var json = new ExportInterviewUseCase().Execute(recorder.Messages, ExportFormat.Json);
        using var decoded = JsonDocument.Parse(json);
        var messages = decoded.RootElement.GetProperty("messages");
        Check(messages.GetArrayLength() == 1, "Exactly one exported question");
        Check(messages.GetArrayLength() > 0 && messages[0].GetProperty("audioSource").GetString() == device.RawValue(), "Export retains immutable capture provenance");
        Check(json.Contains("\"audioSource\""), "Export keeps the existing field name");
        Check(!json.Contains("captureDevice"), "Export does not introduce a renamed JSON field");
    }

    [Theory]
    [InlineData(CaptureDevice.Microphone, false)]
    [InlineData(CaptureDevice.Microphone, true)]
    [InlineData(CaptureDevice.SystemAudio, false)]
    [InlineData(CaptureDevice.SystemAudio, true)]
    public async Task AnswerKeepsTheDeviceThatCapturedIt(CaptureDevice device, bool directFallback)
    {
        Check(CaptureDevices.FromRawValue(device.RawValue()) == device, "Existing source raw value round-trips");
        var groq = new FakeGroqClient(directFallback ? AnswerMode.Empty : AnswerMode.Success);
        var (processor, recorder, anthropic) = Setup(groq);
        var turn = await Begin("What is a hash map?", device, processor, recorder);
        await Hidden(recorder, turn);
        var messages = recorder.Messages;
        Check(messages.Count == 1, $"Audio reaches an answer path for {device}");
        Check(messages.FirstOrDefault()?.AudioSource == device, "Direct answer publishes the captured device");
        Check(messages.FirstOrDefault()?.TurnSequence == 1, "First accepted turn has sequence one");
        Check(!string.IsNullOrEmpty(recorder.Answer(turn)), "Answer body is populated");
        CheckExport(recorder, device);
        Check(groq.CancellationCount == 0 && anthropic.CancellationCount == 0, "No shared API cancellation");
    }

    [Theory]
    [InlineData(CaptureDevice.Microphone)]
    [InlineData(CaptureDevice.SystemAudio)]
    public async Task DelayedClassifierKeepsItsDevice(CaptureDevice device)
    {
        var (processor, recorder, anthropic) = Setup();
        var turn = await Begin("A distributed service", device, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        processor.ResetCaptureBuffers();
        call.Finish("question", answer: "- Keep independent workers behind a queue.");
        await Hidden(recorder, turn);
        Check(recorder.Messages.FirstOrDefault()?.AudioSource == device, "An accepted old classifier keeps its device after switching");
        Check(!string.IsNullOrEmpty(recorder.Answer(turn)), "Accepted old classifier still finishes");
        CheckExport(recorder, device);
    }

    [Theory]
    [InlineData(CaptureDevice.Microphone, "incomplete", false)]
    [InlineData(CaptureDevice.Microphone, "question", false)]
    [InlineData(CaptureDevice.Microphone, "question", true)]
    [InlineData(CaptureDevice.SystemAudio, "incomplete", false)]
    [InlineData(CaptureDevice.SystemAudio, "question", false)]
    [InlineData(CaptureDevice.SystemAudio, "question", true)]
    public async Task StaleIncompleteCannotCrossTheCaptureBoundary(CaptureDevice device, string status, bool comma)
    {
        var (processor, recorder, anthropic) = Setup();
        var oldText = comma ? "Consider a design," : "Consider a design with";
        var oldTurn = await Begin(oldText, device, processor, recorder);
        var oldCall = await anthropic.ClassificationCalls.Next();
        processor.ResetCaptureBuffers();
        oldCall.Finish(status);
        await Hidden(recorder, oldTurn);

        var nextDevice = device == CaptureDevice.Microphone ? CaptureDevice.SystemAudio : CaptureDevice.Microphone;
        var newTurn = await Begin("A separate server", nextDevice, processor, recorder);
        var newCall = await anthropic.ClassificationCalls.Next();
        Check(newCall.Buffer.Length == 0, $"Late old incomplete result cannot cross the capture boundary ({status}, comma={comma})");
        newCall.Finish("statement");
        await Hidden(recorder, newTurn);
        Check(recorder.Messages.Count == 0, "Old incomplete fragments must return without generating answers");
    }

    [Fact]
    public async Task StaleTranscriptionCannotReadOrExpireNewBuffer()
    {
        const string oldText = "Consider the earlier plan with";
        var groq = new FakeGroqClient(heldTranscription: oldText);
        var clock = new BufferClock();
        var (processor, recorder, anthropic) = Setup(groq, clock);
        var oldTurn = await Begin(oldText, CaptureDevice.SystemAudio, processor, recorder);
        var oldTranscription = await groq.TranscriptionCalls.Next();
        processor.ResetCaptureBuffers();

        const string newText = "Assume a service with";
        var newTurn = await Begin(newText, CaptureDevice.Microphone, processor, recorder);
        var newCall = await anthropic.ClassificationCalls.Next();
        newCall.Finish("incomplete");
        await Hidden(recorder, newTurn);
        var readsBeforeOldWork = clock.ReadCount;

        oldTranscription.Response.Send(oldText);
        var oldCall = await anthropic.ClassificationCalls.Next();
        Check(oldCall.Buffer.Length == 0, "Old STT cannot read new-source continuation");
        oldCall.Finish("incomplete");
        await Hidden(recorder, oldTurn);
        Check(clock.ReadCount == readsBeforeOldWork, "Stale work cannot expire or refresh the new buffer's timestamp");

        var continuation = await Begin("separate retry queues", CaptureDevice.Microphone, processor, recorder);
        var continuationCall = await anthropic.ClassificationCalls.Next();
        Check(continuationCall.Buffer == newText, "New continuation survives late old-source work");
        continuationCall.Finish("statement");
        await Hidden(recorder, continuation);
    }

    [Fact]
    public async Task OldSnapshotCannotConsumeNewBuffer()
    {
        var (processor, recorder, anthropic) = Setup();
        const string prefix = "Consider a design with";
        var prefixTurn = await Begin(prefix, CaptureDevice.SystemAudio, processor, recorder);
        var prefixCall = await anthropic.ClassificationCalls.Next();
        prefixCall.Finish("incomplete");
        await Hidden(recorder, prefixTurn);
        var oldTurn = await Begin("persistent work queues", CaptureDevice.SystemAudio, processor, recorder);
        var oldCall = await anthropic.ClassificationCalls.Next();
        Check(oldCall.Buffer == prefix, "Old turn captures its local continuation before switching");
        processor.ResetCaptureBuffers();

        const string newPrefix = "Assume a service with";
        var newTurn = await Begin(newPrefix, CaptureDevice.Microphone, processor, recorder);
        var newCall = await anthropic.ClassificationCalls.Next();
        newCall.Finish("incomplete");
        await Hidden(recorder, newTurn);
        oldCall.Finish("question", answer: "- Persist jobs before acknowledging them.");
        await Hidden(recorder, oldTurn);
        var first = recorder.Messages.FirstOrDefault();
        Check(first?.Content == $"{prefix} persistent work queues", "Accepted old local snapshot may finish");
        Check(first?.AudioSource == CaptureDevice.SystemAudio, "Old snapshot retains provenance");
        Check(first?.TurnSequence == 2, "Source switching does not restart sequence allocation");

        var nextTurn = await Begin("separate retry queues", CaptureDevice.Microphone, processor, recorder);
        var nextCall = await anthropic.ClassificationCalls.Next();
        Check(nextCall.Buffer == newPrefix, "Old local snapshot cannot consume the new source's buffer");
        nextCall.Finish("statement");
        await Hidden(recorder, nextTurn);
    }

    [Fact]
    public async Task StreamingAndDedupeSurviveSwitch()
    {
        var groq = new FakeGroqClient(AnswerMode.Held);
        var (processor, recorder, anthropic) = Setup(groq);
        var turn = await Begin("What is a hash map?", CaptureDevice.Microphone, processor, recorder);
        var call = await groq.AnswerCalls.Next();
        call.OnChunk("- Store keys in buckets.");
        await recorder.Chunks.Next();
        processor.ResetCaptureBuffers();
        call.OnChunk("\n- Resolve collisions explicitly.");
        call.Completion.Send(Outcome.Success);
        await Hidden(recorder, turn);
        Check(recorder.Answer(turn)?.Contains("Resolve collisions") == true, "Existing answer finishes across switch");
        Check(recorder.Messages.FirstOrDefault()?.AudioSource == CaptureDevice.Microphone, "Streaming turn keeps its original device");
        Check(groq.CancellationCount == 0 && anthropic.CancellationCount == 0, "Switch never calls shared request cancellation");

        var duplicate = await Begin("What is a hash map?", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, duplicate);
        Check(recorder.Messages.Count == 1, "Dedupe history survives source switch");
        var nextTurn = await Begin("What is inheritance?", CaptureDevice.SystemAudio, processor, recorder);
        var nextCall = await groq.AnswerCalls.Next();
        nextCall.OnChunk("- Derive behavior from a parent type.");
        nextCall.Completion.Send(Outcome.Success);
        await Hidden(recorder, nextTurn);
        Check(recorder.Messages.LastOrDefault()?.TurnSequence == 3, "Turn numbering stays monotonic after a deduped turn");
    }

    [Fact]
    public async Task PreviewCancellationPreservesAcceptedTurn()
    {
        const string audioText = "What is a hash map?";
        var groq = new FakeGroqClient(heldTranscription: audioText);
        var (processor, recorder, anthropic) = Setup(groq);
        processor.PrefetchTranscription(Encoding.UTF8.GetBytes(audioText), CaptureDevice.SystemAudio);
        var preview = await groq.TranscriptionCalls.Next();
        var turn = await Begin(audioText, CaptureDevice.SystemAudio, processor, recorder);
        processor.ResetCaptureBuffers();
        preview.Response.Send("What is inheritance?");
        var committed = await groq.TranscriptionCalls.Next();
        committed.Response.Send(audioText);
        await Hidden(recorder, turn);
        Check(recorder.Messages.FirstOrDefault()?.Content == audioText, "Invalidated preview is not reused by an accepted turn");
        Check(!string.IsNullOrEmpty(recorder.Answer(turn)), "Accepted turn retranscribes after preview cancellation");
        Check(groq.CancellationCount == 0 && anthropic.CancellationCount == 0, "Preview cancellation is task scoped");
    }

    [Fact]
    public async Task CurrentBufferStillExpires()
    {
        var clock = new BufferClock();
        var (processor, recorder, anthropic) = Setup(clock: clock);
        var turn = await Begin("Consider a design with", CaptureDevice.SystemAudio, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("incomplete");
        await Hidden(recorder, turn);
        clock.Advance(AppConstants.Thresholds.BufferTimeout + 1);
        var nextTurn = await Begin("A separate server", CaptureDevice.SystemAudio, processor, recorder);
        var nextCall = await anthropic.ClassificationCalls.Next();
        Check(nextCall.Buffer.Length == 0, "Current-generation buffer timeout behavior is preserved");
        nextCall.Finish("statement");
        await Hidden(recorder, nextTurn);
    }

    // Speech in pieces

    private async Task WaitForFirstMessage(RecordingDelegate recorder)
    {
        for (var attempt = 0; attempt < 40 && recorder.Messages.Count == 0; attempt++)
        {
            await Task.Delay(200);
        }
        await Settle();
    }

    /// The sequence from a real run: a question cut at a pause, a filler tail, then garbled speech.
    [Fact]
    public async Task MumbledQuestionIsAnsweredOnce()
    {
        var (processor, recorder, _) = Setup();
        foreach (var piece in new[] { "Okay, how to read a huge Python file with 10,000 rows and what...", "kind of, let's say,", "Aki, di, sari, u, gontu, yus?" })
        {
            var turn = await Begin(piece, CaptureDevice.Microphone, processor, recorder);
            await Hidden(recorder, turn);
            Check(recorder.Messages.Count == 0, $"Nothing is answered while the question is still going: {piece}");
        }
        await WaitForFirstMessage(recorder);
        Check(recorder.Messages.Count == 1, "The pieces become one question");
        var question = recorder.Messages.FirstOrDefault()?.Content ?? "";
        Check(question == "Okay, how to read a huge Python file with 10,000 rows", $"It is answered without the tail or the garbled words: {question}");
        Check(recorder.Statuses.Count == 0, "A held question needs no 'didn't catch that'");
    }

    /// The same speech as Whisper wrote it from clean system audio: other punctuation, other scraps.
    [Fact]
    public async Task MumbledQuestionSurvivesTranscriptionPunctuation()
    {
        var (processor, recorder, _) = Setup();
        foreach (var piece in new[] { "Okay, how to read a huge python file with 10,000 rows and what?", "Kind of, let's say.", "Aki, D, sorry, U, gone to, yas?" })
        {
            var turn = await Begin(piece, CaptureDevice.SystemAudio, processor, recorder);
            await Hidden(recorder, turn);
            Check(recorder.Messages.Count == 0, $"Nothing is answered while the question is still going: {piece}");
        }
        await WaitForFirstMessage(recorder);
        Check(recorder.Messages.Select(message => message.Content).SequenceEqual(new[] { "Okay, how to read a huge python file with 10,000 rows" }), "One question, without the tail");
    }

    /// From a real run: the missing space hides the question from the local signal, so only the model hears it.
    [Fact]
    public async Task CommaEndedQuestionTheModelHeardIsAnsweredAfterSilence()
    {
        var (processor, recorder, anthropic) = Setup();
        var turn = await Begin("whatisObject.freeze,", CaptureDevice.Microphone, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("question", "typescript", "- It makes an object read-only.");
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 0, "It waits in case the question goes on");
        await WaitForFirstMessage(recorder);
        Check(recorder.Messages.FirstOrDefault()?.Content == "whatisObject.freeze", "Silence answers it without the comma");
        Check(recorder.Messages.Count == 1, "It is answered once");
    }

    [Fact]
    public async Task CommaEndedQuestionStillJoinsTheNextPiece()
    {
        var (processor, recorder, anthropic) = Setup();
        var turn = await Begin("whatisObject.freeze,", CaptureDevice.Microphone, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("question", "typescript");
        await Hidden(recorder, turn);
        var next = await Begin("and when would you use it", CaptureDevice.Microphone, processor, recorder);
        var nextCall = await anthropic.ClassificationCalls.Next();
        Check(nextCall.Buffer == "whatisObject.freeze,", "The next piece takes the held question over");
        nextCall.Finish("question", "typescript", "- Use it for shared config.");
        await Hidden(recorder, next);
        await Task.Delay(TimeSpan.FromSeconds(AppConstants.Thresholds.BufferedQuestionSilence + 0.5));
        await Settle();
        Check(recorder.Messages.Count == 1, "The timer does not answer it a second time");
        Check(recorder.Messages.FirstOrDefault()?.Content == "whatisObject.freeze, and when would you use it", "It is one question");
    }

    [Fact]
    public async Task GarbledSpeechIsNeverGuessedAt()
    {
        var (processor, recorder, _) = Setup();
        var turn = await Begin("Aki, di, sari, u, gontu, yus?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 0, "Garbled speech opens no card");
        Check(recorder.Statuses.SequenceEqual(new[] { FeedbackCopy.DidntCatchThat }), "It says so instead");
    }

    [Fact]
    public async Task FragmentJoinsTheQuestionBeforeIt()
    {
        var (processor, recorder, anthropic) = Setup();
        var question = await Begin("What is a hash map?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, question);
        var fragment = await Begin("kind of, let's say,", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, fragment);
        var next = await Begin("with a lot of collisions", CaptureDevice.Microphone, processor, recorder);
        var nextCall = await anthropic.ClassificationCalls.Next();
        Check(nextCall.Buffer == "What is a hash map? kind of, let's say,", $"The next piece is heard with the question: {nextCall.Buffer}");
        nextCall.Finish("statement");
        await Hidden(recorder, next);
    }

    // Conversation understanding

    [Fact]
    public async Task CandidateSpeechDoesNotOpenACard()
    {
        var (processor, recorder, anthropic) = Setup();
        const string text = "Yeah, of course, for me, like I have 10 plus years experience in software engineering, now focusing more on AI engineering, but in the past I started working with neural networks";
        var turn = await Begin(text, CaptureDevice.Microphone, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("question", answer: "- I have ten years of experience.");
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 0, "The candidate's own answer never becomes an interviewer question card");
        Check(recorder.Answer(turn) is null, "No answer is generated for the candidate's own speech");
    }

    [Fact]
    public async Task StallingIsSkippedBeforeAnyModelCall()
    {
        var (processor, recorder, anthropic) = Setup();
        foreach (var phrase in new[] { "Okay, just give me a second.", "Sorry, can you repeat?" })
        {
            var turn = await Begin(phrase, CaptureDevice.Microphone, processor, recorder);
            await Hidden(recorder, turn);
        }
        Check(recorder.Messages.Count == 0, "Stalling and repeat requests open no card");
        Check(anthropic.ClassificationCount == 0, "Stalling costs no classification call");
    }

    private static readonly string[] TopicSequence =
    {
        "What is RAG and CAG?", "So how do you evaluate a classifier?", "Have you used any of these?"
    };

    [Fact]
    public async Task StaleTopicNeverSteersAFreshQuestion()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, _) = Setup(groq);
        foreach (var question in TopicSequence)
        {
            var turn = await Begin(question, CaptureDevice.Microphone, processor, recorder);
            await Hidden(recorder, turn);
        }
        var requests = groq.Requests;
        Check(requests.Count == 3, "All three clear questions take the fast path");
        if (requests.Count != 3) return;
        Check(requests[0].Topic == "ragCag" && !requests[0].TopicInherited, "A named topic is used as named");
        Check(requests[1].Topic == "unknown" && !requests[1].TopicInherited, "A new concrete question is not forced onto the RAG topic");
        Check(requests[0].Context is null, "The first question of a session has no conversation to borrow");
        Check(requests[1].Context?.Contains("RAG and CAG") == true, "Later questions see the conversation so references resolve");
        Check(requests[2].Topic == "ragCag" && requests[2].TopicInherited, "A back-reference inherits the topic and says so");
        Check(requests[2].Context?.Contains("classifier") == true, "A back-reference receives the recent conversation");
    }

    [Fact]
    public async Task DirectPromptKeepsStaleTopicOut()
    {
        var groq = new FakeGroqClient(AnswerMode.Empty);
        var (processor, recorder, anthropic) = Setup(groq);
        foreach (var question in TopicSequence)
        {
            var turn = await Begin(question, CaptureDevice.Microphone, processor, recorder);
            await Hidden(recorder, turn);
        }
        var prompts = anthropic.Prompts;
        Check(prompts.Count == 3, "Each question reaches the direct answer prompt");
        if (prompts.Count != 3) return;
        Check(prompts.All(prompt => prompt.Split(AnswerPromptBlocks.CandidateVoice).Length == 2), "Direct and contextual answers receive the shared candidate voice exactly once");
        Check(prompts[0].Contains("RAG/CAG ANSWER SHAPE") && prompts[0].Contains("TOPIC: ragCag"), "A RAG question keeps the RAG answer shape");
        Check(!prompts[1].Contains("RAG/CAG ANSWER SHAPE"), "A classifier question does not get the RAG answer shape");
        Check(!prompts[1].Contains("\nTOPIC:") && !prompts[1].Contains("PREVIOUS TOPIC"), "A classifier question carries no topic line");
        Check(prompts[1].Contains("Answer the question as asked."), "A classifier question is answered as asked");
        Check(prompts[1].Contains("=== CONVERSATION SO FAR ===") && prompts[1].Contains("CURRENT question"), "The conversation is context for the current question only");
        Check(prompts[2].Contains("PREVIOUS TOPIC (only if the question refers back to it): ragCag"), "A back-reference names the previous topic as a hint only");
        Check(prompts[2].Contains("=== CONVERSATION SO FAR ==="), "A back-reference carries the recent conversation");
        Check(!prompts[2].Contains("RAG/CAG ANSWER SHAPE"), "An inherited topic never imposes an answer shape");
    }

    [Fact]
    public async Task EmptyCombinedAnswerIsRecoveredOnTheOpenCard()
    {
        var (processor, recorder, anthropic) = Setup();
        var turn = await Begin("A distributed service", CaptureDevice.SystemAudio, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("question", answer: "");
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 1, "Recovery reuses the open card instead of opening another");
        Check(recorder.Answer(turn)?.Contains("deterministic direct transport") == true, "The direct stream fills the card");
        Check(recorder.Answer(turn)?.ToLowerInvariant().Contains("error") == false, "No 'No answer text received' error is left behind");
    }

    [Fact]
    public async Task RetryFillsTheCardThatFailed()
    {
        var (processor, recorder, _) = Setup();
        var turn = Guid.NewGuid();
        processor.RetryAnswer(turn, "What is a hash map?", "hashMap", 3);
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 0, "A retry fills the card that failed; it opens no second question");
        Check(recorder.Answer(turn)?.Contains("deterministic direct transport") == true, "The answer streams onto the card that failed");
        Check(recorder.Answer(turn)?.ToLowerInvariant().Contains("error") == false, "A retry that worked leaves no error behind");
    }

    // Live coding

    private const string ReverseReply = """
        SAY:
        - Restating: take a string, return it reversed.
        - Assumption: an empty string returns an empty string.
        - O(n) time, O(n) space.

        STEP 1 — file: main.py
        Why: Slice the string backwards.
        ```python
        def reverse(text):
            return text[::-1]
        ```
        """;

    private const string TypeScriptReply = """
        SAY:
        - Same logic in TypeScript: split, reverse, join.
        - Strings are immutable there too, so it is O(n) space.

        STEP 1 — file: reverse.ts
        Why: Split into characters, reverse, join.
        ```typescript
        function reverse(text: string): string {
          return text.split("").reverse().join("");
        }
        ```
        """;

    private const string CutOffReply = """
        SAY:
        - Swap characters from both ends.

        STEP 1 — file: main.py
        Why: Walk the string from both ends.
        ```python
        def reverse(text):
            chars = list(text)
            left, right = 0, len(chars) - 1
            while left < right:
        """;

    [Fact]
    public async Task CodingRequestFillsBothSides()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, anthropic) = Setup(groq);
        anthropic.CodingReply = ReverseReply;

        var turn = await Begin("Write a function that reverses a string", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 1 && recorder.Messages.FirstOrDefault()?.Topic == "coding", "The coding request appears as a question on the left");
        var card = recorder.Answer(turn) ?? "";
        Check(card.Contains("Restating: take a string") && card.Contains("O(n) time"), "Talking points go to the answer card");
        Check(!card.Contains("Code is in the panel"), "Talking points need no pointer to the open panel");
        Check(!card.Contains("def reverse"), "Code stays out of the answer card");
        Check(recorder.CodingUpdates.LastOrDefault()?.StartsWith("STEP 1", StringComparison.Ordinal) == true, "Steps go to the code panel");
        Check(recorder.CodingUpdates.LastOrDefault()?.Contains("def reverse(text):") == true, "The panel receives the code");
        Check(recorder.CodingUpdates.All(update => !update.Contains("SAY:")), "Talking points never leak into the panel");
        Check(anthropic.ClassificationCount == 0 && groq.Requests.Count == 0, "A coding request skips the cue-card paths");

        // The interviewer keeps dictating: the same card and panel update, no second card opens.
        var detail = await Begin("like text, return string", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, detail);
        Check(recorder.Messages.Count == 1, "A refinement does not open another card");
        Check(anthropic.Prompts.LastOrDefault()?.Contains("Write a function that reverses a string like text, return string") == true, "The refined task carries everything said so far");
        Check(recorder.CodingUpdates.Count >= 2, "The panel is updated again");

        // A normal question in the middle of the exercise is answered on the left with the code as context.
        var question = await Begin("What is polymorphism?", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, question);
        Check(recorder.Messages.Count == 2 && recorder.Messages.LastOrDefault()?.Topic == "polymorphism", "Questions keep getting answered on the left");
        Check(groq.Requests.LastOrDefault()?.Topic == "polymorphism", "The question takes the normal fast path");
    }

    /// The exact sequence that failed live: the panel showed Python while the interviewer asked for
    /// TypeScript, another way, and technique examples.
    [Fact]
    public async Task FollowUpsChangeTheCodeOnScreen()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, anthropic) = Setup(groq);
        anthropic.CodingReplies = new List<string> { ReverseReply, TypeScriptReply, ReverseReply, ReverseReply };

        var first = await Begin("Write me a reverse function in Python.", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, first);
        Check(recorder.CodingUpdates.LastOrDefault()?.Contains("def reverse(text):") == true, "The first request puts Python on the panel");

        var language = await Begin("Okay, now can you write it in TypeScript?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, language);
        Check(recorder.Messages.Count == 2 && recorder.Messages.LastOrDefault()?.Content == "Okay, now can you write it in TypeScript?", "A language change opens its own card");
        Check(recorder.CodingUpdates.LastOrDefault()?.Contains("function reverse(text: string)") == true, "The panel now holds the TypeScript version");
        var languagePrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(languagePrompt.Contains(AnswerPromptBlocks.CandidateVoice), "A spoken code change keeps the candidate's prose voice");
        Check(languagePrompt.Contains("CURRENT CODE (already on the candidate's screen)") && languagePrompt.Contains("def reverse(text):"), "The model is shown the Python it has to convert");
        Check(languagePrompt.Contains("CHANGE REQUEST") && languagePrompt.Contains("write it in TypeScript"), "The model is told what to change");
        Check(recorder.Answer(language)?.Contains("Same logic in TypeScript") == true, "The card explains the change");
        Check(groq.Requests.Count == 0 && anthropic.ClassificationCount == 0, "A code change never falls through to a cue card");

        var alternative = await Begin("Is there another way in Python to reverse it?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, alternative);
        Check(recorder.Messages.Count == 3, "Asking for another way opens a card");
        Check((anthropic.Prompts.LastOrDefault() ?? "").Contains("function reverse(text: string)"), "Another way builds on the code that is on the panel now");

        var technique = await Begin("Can you show me sliding window technique?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, technique);
        var techniquePrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(recorder.Messages.Count == 4, "A technique example opens a card");
        Check(techniquePrompt.Contains("sliding window") && !techniquePrompt.Contains("CURRENT CODE"), "A technique example starts clean instead of editing the last code");

        var concept = await Begin("What is polymorphism?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, concept);
        Check(groq.Requests.Count == 1 && groq.Requests.LastOrDefault()?.Topic == "polymorphism", "A concept question in the middle is still a cue card");
    }

    /// Panel actions: a one-click change, code that stops early, and Retry on the same card.
    [Fact]
    public async Task PanelActionsAndOutcomes()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, anthropic) = Setup(groq);
        anthropic.CodingReplies = new List<string> { ReverseReply, CutOffReply, CutOffReply, ReverseReply };

        // Nothing on screen yet: a panel action has nothing to change.
        processor.RequestCodeChange("Optimize it");

        var first = await Begin("Write a function that reverses a string", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, first);
        await recorder.CodingFinished.Next();
        Check(anthropic.Prompts.Count == 1, "A panel action without code sends nothing");
        var firstStart = recorder.CodingStarts.FirstOrDefault();
        Check(firstStart?.Request == "Write a function that reverses a string" && firstStart?.AllowsChanges == true, "The panel is told what was asked and that it can be changed");
        Check(firstStart?.TurnId == first && firstStart?.IsRefinement == false, "The code belongs to the card that asked for it");
        Check(recorder.CodingOutcomes.SequenceEqual(new[] { CodingTaskOutcome.Complete }), "Finished code reports complete");

        processor.RequestCodeChange("Optimize it");
        var optimizeOutcome = await recorder.CodingFinished.Next();
        await Settle();
        Check(recorder.Messages.Count == 2 && recorder.Messages.LastOrDefault()?.Content == "Optimize it", "A panel action opens its own card, worded like a spoken request");
        var optimizePrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(optimizePrompt.Contains("CURRENT CODE") && optimizePrompt.Contains("text[::-1]") && optimizePrompt.Contains("Optimize it"), "The action is applied to the code on screen");
        Check(!optimizeOutcome.IsComplete, "Code that ends inside a fence is reported as incomplete");
        if (optimizeOutcome.IsComplete) return;
        Check(!string.IsNullOrEmpty(optimizeOutcome.Reason), "An incomplete outcome says why");
        var optimizeCard = recorder.Answer(recorder.CodingStarts[1].TurnId) ?? "";
        Check(optimizeCard.Contains("Use Try again in the panel"), "The card points at the way out, by the name on the button");

        // Cut-off code is not remembered as the code on screen.
        processor.RequestCodeChange("Add tests for it");
        await recorder.CodingFinished.Next();
        var testsPrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(testsPrompt.Contains("text[::-1]") && !testsPrompt.Contains("chars = list(text)"), "The next change builds on the last complete code, not the fragment");

        // Retry runs the same request again on the same card.
        await Settle();
        var cardsBeforeRetry = recorder.Messages.Count;
        processor.RetryLastCodingRequest();
        var retryOutcome = await recorder.CodingFinished.Next();
        await Settle();
        Check(retryOutcome == CodingTaskOutcome.Complete, "The retry finishes the code");
        Check(recorder.Messages.Count == cardsBeforeRetry, "A retry does not open another card");
        var starts = recorder.CodingStarts;
        Check(starts.Count >= 2 && starts[^1].TurnId == starts[^2].TurnId && !starts[^1].IsRefinement, "A retry restarts the version it belongs to");
        Check(recorder.CodingUpdates.LastOrDefault()?.Contains("return text[::-1]") == true, "The retry puts full code on the panel");
    }

    [Fact]
    public async Task ScreenOnlyRequestPointsAtTheShortcut()
    {
        var (processor, recorder, anthropic) = Setup();
        var turn = await Begin("Write this code", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, turn);
        Check(recorder.Messages.Count == 1, "The request still appears on the left");
        Check(recorder.Answer(turn)?.Contains(PlatformCopy.CaptureShortcut) == true, "The card says how to get the task in");
        Check(anthropic.Prompts.Count == 0 && anthropic.ClassificationCount == 0, "No model invents a task that is only on the screen");
        Check(recorder.CodingUpdates.Count == 0, "Nothing is sent to the code panel");
    }

    [Fact]
    public async Task SessionStartOpensNoCardButCollectsTheTask()
    {
        var (processor, recorder, anthropic) = Setup();
        anthropic.CodingReply = ReverseReply;
        var arm = await Begin("Okay, let's start a new Python file.", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, arm);
        Check(recorder.Messages.Count == 0 && anthropic.ClassificationCount == 0, "Starting a file is logistics, not a question");

        var task = await Begin("Given a string, return its reverse", CaptureDevice.SystemAudio, processor, recorder);
        await Hidden(recorder, task);
        Check(recorder.Messages.Count == 1 && recorder.CodingUpdates.LastOrDefault()?.StartsWith("STEP 1", StringComparison.Ordinal) == true, "The first task statement in an armed session produces code");
    }

    [Fact]
    public async Task ConversationalMlDiscussionIsNotCoding()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, anthropic) = Setup(groq);
        anthropic.CodingReply = ReverseReply;
        var turn = await Begin("Let's say we want to create a spam classifier. How would you evaluate it?", CaptureDevice.SystemAudio, processor, recorder);
        var call = await anthropic.ClassificationCalls.Next();
        call.Finish("question", "classification", "- Precision, recall, and F1 on a held-out set.");
        await Hidden(recorder, turn);
        Check(recorder.Answer(turn)?.Contains("Precision") == true, "The scenario question is answered as a normal cue card");
        Check(recorder.CodingUpdates.Count == 0, "An ML scenario question does not open the code panel");
        Check(anthropic.Prompts.All(prompt => !prompt.Contains("live coding interview copilot")), "No coding prompt is sent for a conceptual question");
    }

    /// From a live run: "show me that in code" and "is there an easier way?" got text cards.
    [Fact]
    public async Task CodeExampleAndEasierWay()
    {
        var groq = new FakeGroqClient();
        var (processor, recorder, anthropic) = Setup(groq);
        anthropic.CodingReplies = new List<string> { ReverseReply, TypeScriptReply };

        var concept = await Begin("What is polymorphism?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, concept);
        Check(recorder.CodingUpdates.Count == 0, "A concept question is a cue card");

        var example = await Begin("Can you show me that in code?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, example);
        var examplePrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(recorder.CodingUpdates.Count > 0, "Asking for it in code opens the code panel");
        Check(examplePrompt.Contains("polymorphism"), "The code is about the question it points back at");
        Check(groq.Requests.Count == 1, "It does not become a second cue card");

        var easier = await Begin("Is there some easier way?", CaptureDevice.Microphone, processor, recorder);
        await Hidden(recorder, easier);
        var easierPrompt = anthropic.Prompts.LastOrDefault() ?? "";
        Check(easierPrompt.Contains("CURRENT CODE (already on the candidate's screen)") && easierPrompt.Contains("CHANGE REQUEST"), "An easier way changes the code on screen");
        Check(groq.Requests.Count == 1, "An easier way does not become a cue card");
    }

    [Fact]
    public void LastInterviewerQuestionIgnoresTheCandidateReply()
    {
        var context = new ConversationContext();
        Check(context.LastInterviewerQuestion is null, "No question yet");
        context.AddUtterance("What is polymorphism?", "polymorphism", isQuestion: true, sequence: 1);
        context.AddUtterance("I used it in a payments service.", null, isQuestion: false, sequence: 2);
        Check(context.LastInterviewerQuestion == "What is polymorphism?", "The last question is the interviewer's, not the candidate's reply");
    }
}
