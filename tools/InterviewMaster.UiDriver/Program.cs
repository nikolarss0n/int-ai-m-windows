using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Presentation;
using InterviewMaster.Support;

namespace InterviewMaster.UiDriver;

/// Drives the real window with canned data: no network, no audio, no API keys, no saved settings.
/// The states and their names are the ones the macOS driver (`Scripts/focus-ui-qa`) replays, so the
/// two apps can be compared state by state.
public static class Program
{
    public static int Main(string[] args)
    {
        var mode = args.Length > 0 && args[0] is "practice" or "app" or "icon" ? args[0] : "";
        var practice = mode == "practice";
        if (mode.Length > 0) args = args[1..];
        var output = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "shots");
        var only = args.Length > 1 ? args[1].Split(',').ToHashSet() : null;
        Directory.CreateDirectory(output);
        var session = HeadlessUnitTestSession.StartNew(typeof(DriverApp));
        if (mode == "icon") return session.Dispatch(() => WriteIcon(output), CancellationToken.None).GetAwaiter().GetResult();
        return session.Dispatch(() => mode == "app" ? AppDriver.RunAsync(output) : practice ? PracticeDriver.RunAsync(output, only) : RunAsync(output, only), CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// Draws the app's mark at the sizes Windows asks for and packs them into one `.ico`.
    private static int WriteIcon(string output)
    {
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = sizes.Select(side =>
        {
            using var bitmap = AppIcon.Bitmap(side);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }).ToList();
        using var file = new BinaryWriter(File.Create(Path.Combine(output, "InterviewMaster.ico")));
        file.Write((ushort)0);
        file.Write((ushort)1);
        file.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var index = 0; index < sizes.Length; index++)
        {
            file.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
            file.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
            file.Write((byte)0);
            file.Write((byte)0);
            file.Write((ushort)1);
            file.Write((ushort)32);
            file.Write((uint)images[index].Length);
            file.Write((uint)offset);
            offset += images[index].Length;
        }
        foreach (var image in images) file.Write(image);
        File.WriteAllBytes(Path.Combine(output, "icon-256.png"), images[^1]);
        return 0;
    }

    private static async Task<int> RunAsync(string output, HashSet<string>? only)
    {
        SettingsStore.Standard = new InMemorySettingsStore();
        // The driver never reads the person's keys: an empty key file and no environment.
        ApiKeyManager.Shared = new ApiKeyManager(
            Path.Combine(Path.GetTempPath(), $"interview-master-driver-no-keys-{Guid.NewGuid():N}"), _ => null);
        AppSettings.Shared.Role = InterviewRole.SeniorQAEngineer;
        AppSettings.Shared.ProgrammingLanguage = ProgrammingLanguage.Python;
        AppSettings.Shared.ResponseLanguage = SpeakingLanguage.English;
        Token.Font.Family = new FontFamily("fonts:Inter#Inter");
        Token.Motion.ReducesMotion = () => true;

        var main = new MainView();
        main.Interview.Header.ShowsWindowButtons = false;
        var size = (Environment.GetEnvironmentVariable("QA_WINDOW") ?? "760x500").Split('x').Select(double.Parse).ToArray();
        var window = new Window { Width = size[0], Height = size[1], Background = new SolidColorBrush(Color.FromRgb(92, 92, 92)), Content = main };
        window.Show();

        var scratch = Directory.CreateTempSubdirectory("interview-master-driver").FullName;
        var mainThread = new DispatcherMainThread();
        var audio = new AudioCaptureCoordinator(
            mainThread, new MicrophoneReservations(),
            _ => throw new InvalidOperationException("The driver has no audio device."),
            (_, _) => Task.CompletedTask);
        var processor = new VoiceInterviewProcessor(mainThread, memoryRetrieval: new MemoryRetrievalUseCase(new MemoryStore(scratch)));
        var shell = new DriverShell();
        var d = new InterviewController(main, shell, mainThread, audio, processor)
        {
            SessionsDirectory = Path.Combine(scratch, "sessions"),
            AnalyzesSavedSessions = false,
            Environment = _ => null,
        };
        var focus = main.Interview;
        var notesView = new NotesView();
        main.SetTabView(Tab.Notes, notesView);
        var notes = new NotesController(notesView, shell, d);
        var log = new List<string>();
        var steps = new List<(string Name, Action Run)>();
        void Add(string name, Action run) => steps.Add((name, run));

        Guid q1 = Guid.NewGuid(), q2 = Guid.NewGuid(), q3 = Guid.NewGuid(), q4 = Guid.NewGuid(), q5 = Guid.NewGuid(), q6 = Guid.NewGuid();

        void Ask(Guid id, int seq, string text, string topic)
        {
            d.ProcessorDidReceiveQuestion(text, topic, MessageType.Answer, CaptureDevice.Microphone, id, seq);
            d.ProcessorDidStartStreaming(MessageType.Answer, topic, 640, id, seq);
        }
        void Answer(Guid id, string text)
        {
            d.ProcessorDidReceiveAnswerChunk(text, id);
            d.ProcessorDidFinishAnswer(text, 1480, id);
        }
        CodingTaskInfo CodeInfo(Guid id, string request, bool refinement = false) => new(id, request, refinement, true);

        const string partialSteps = """
            STEP 1 — file: main.py
            Why: Slice the string backwards.
            ```python
            def reverse(text):
                return te
            """;
        const string codeSteps = """
            STEP 1 — file: main.py
            Why: Slice the string backwards.
            ```python
            def reverse(text):
                return text[::-1]
            ```

            STEP 2 — file: main.py
            Why: Call it with a sample and print the result so the interviewer sees it work.
            ```python
            if __name__ == "__main__":
                print(reverse("interview"))  # weweaverni
                print(reverse(""))
            ```
            """;
        const string refined = codeSteps + """


            STEP 3 — file: test_main.py
            Why: One test for the empty string edge case the interviewer mentioned earlier in the call.
            ```python
            from main import reverse

            def test_empty_string_returns_empty_string_without_raising_any_error_at_all():
                assert reverse("") == ""
            ```
            """;
        const string typeScriptSteps = """
            STEP 1 — file: reverse.ts
            Why: Split into characters, reverse, join.
            ```typescript
            export function reverse(text: string): string {
              return text.split("").reverse().join("");
            }
            ```
            """;
        const string cutOffSteps = """
            STEP 1 — file: reverse.ts
            Why: Walk the string from the end with two indexes.
            ```typescript
            export function reverse(text: string): string {
              const chars = text.split("");
              let left = 0;
              let right = chars.length - 1;
              while (left < right) {
                [chars[left], chars[right]] = [chars[right], chars[left]];
            """;
        const string analysis = "## Analysis\nThe screenshot shows a two-sum problem.\n- Use a dict of value -> index\n- One pass, O(n)";

        Add("welcome", () => { });
        Add("question-answered", () =>
        {
            Ask(q1, 1, "What is polymorphism?", "polymorphism");
            Answer(q1, "- Polymorphism means one interface with many implementations.\n- I use it to swap strategies without touching callers.\n- Compile-time overloads and run-time overrides are the two forms.");
        });
        Add("coding-question-writing", () =>
        {
            d.ProcessorShowLoading("💻 Writing code...", LoadingColor.ApplePurple, q2);
            Ask(q2, 2, "Write a function that reverses a string", "coding");
            d.ProcessorDidStartCodingTask(CodeInfo(q2, "Write a function that reverses a string"));
        });
        Add("coding-partial-step", () =>
        {
            d.ProcessorDidUpdateCodingTask(partialSteps);
            d.ProcessorDidReceiveAnswerChunk("- Restating: take a string, return it reversed.", q2);
        });
        Add("coding-finished", () =>
        {
            d.ProcessorDidUpdateCodingTask(codeSteps);
            Answer(q2, "- Restating: take a string, return it reversed.\n- Assumption: an empty string returns an empty string.\n- O(n) time, O(n) space.\n- Code is in the panel on the right (2 steps).");
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            d.ProcessorHideLoading(q2);
        });
        Add("question-while-code-up", () =>
        {
            Ask(q3, 3, "Why did you use slicing instead of a loop?", "unknown");
            Answer(q3, "- Slicing is the idiomatic Python way and runs in C, so it is faster.\n- A loop is clearer for languages without slicing.\n- Both are O(n); I would pick slicing here.");
        });
        Add("code-refined", () =>
        {
            d.ProcessorDidStartCodingTask(CodeInfo(q2, "Write a function that reverses a string", refinement: true));
            d.ProcessorDidUpdateCodingTask(refined);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
        });
        Add("language-switched", () =>
        {
            d.ProcessorShowLoading("💻 Writing code...", LoadingColor.ApplePurple, q4);
            Ask(q4, 4, "Okay, now can you write it in TypeScript?", "coding");
            d.ProcessorDidStartCodingTask(CodeInfo(q4, "Okay, now can you write it in TypeScript?"));
            d.ProcessorDidUpdateCodingTask(typeScriptSteps);
            Answer(q4, "- Same logic in TypeScript: split, reverse, join.\n- Strings are immutable there too, so it is O(n) space.\n- Code is in the panel on the right (1 step).");
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            d.ProcessorHideLoading(q4);
        });
        Add("older-version-via-burst", () => d.SelectQuestion(q2));
        Add("back-to-latest", () => focus.CodePanel.SelectVersion(q4));
        Add("incomplete-with-retry", () =>
        {
            Ask(q5, 5, "Is there another way to reverse it?", "coding");
            d.ProcessorDidStartCodingTask(CodeInfo(q5, "Is there another way to reverse it?"));
            d.ProcessorDidUpdateCodingTask(cutOffSteps);
            Answer(q5, "- Swap characters from both ends.\n- The code stopped early. Use Try again in the panel.");
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Incomplete("The reply was cut off before the last step ended."));
        });
        Add("many-questions-vertical-burst", () =>
        {
            Ask(q6, 6, "What is the time complexity of the two pointer version, and does it change with unicode input?", "unknown");
            Answer(q6, "- Two pointers is O(n) time and O(n) space because the string is copied into a list first.\n- Unicode combining marks need grapheme-aware handling; plain code points break them.");
        });
        Add("interview-stopped-locks-changes", () =>
        {
            focus.CodePanel.SelectVersion(q4);
            focus.CodePanel.LockChanges();
        });
        Add("panel-closed", () => d.HideCodePanel(byUser: true));
        Add("panel-reopened-by-button", d.ToggleCodePanel);
        Add("analysis-fallback", () =>
        {
            d.ProcessorDidStartCodingTask(new CodingTaskInfo(Guid.NewGuid(), "Screenshot analysis", false, false));
            d.ProcessorDidUpdateCodingTask(analysis);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
        });
        Add("reset", d.ResetFocus);
        Add("empty-panel-via-button", d.ToggleCodePanel);

        // Every phase pill and the red states, which the coding flow above never reaches.
        Guid q7 = Guid.NewGuid(), q8 = Guid.NewGuid(), q9 = Guid.NewGuid(), q10 = Guid.NewGuid();
        Add("phases-queued-generating-answering", () =>
        {
            d.ReceiveQuestion(q7, 7, "Can you walk me through your last project?", "unknown");
            d.ReceiveQuestion(q8, 8, "How would you test a rate limiter?", "testing");
            d.BeginAIWork(q8);
            d.ReceiveQuestion(q9, 9, "What is polymorphism?", "polymorphism");
            d.BeginAIWork(q9);
            d.StartAnswer(q9);
            d.UpdateAnswer(q9, "- Polymorphism means one interface with many implementations.");
        });
        Add("failed-turn", () =>
        {
            d.ReceiveQuestion(q10, 10, "Explain how the event loop schedules callbacks.", "javascript");
            d.BeginAIWork(q10);
            // The pipeline reports a failure as text; the window turns it into a reason.
            d.ProcessorDidReceiveAnswerChunk("Error: The request timed out.", q10);
            d.SelectQuestion(q10);
        });
        Add("cv-briefing", () => d.ProcessorDidUpdateBriefing(new InterviewBriefing(
            "qa",
            "Senior QA engineer moving toward AI evaluation, with a decade of test automation behind it",
            new[]
            {
                "Built the regression suite for a payments platform that ran on every merge",
                "Led the move from Selenium to Playwright and cut flaky runs by two thirds",
                "Now evaluating LLM answers with rubric checks and a small labelled set",
            })));
        Add("interview-running", () =>
        {
            // The driver has no audio device; a running interview would be listening to something.
            d.AudioPresentationOverride = CapturePresentation.Listening(CaptureDevice.SystemAudio);
            d.IsInterviewActive = true;
            d.RecordingStartTime = InterviewController.Now().AddSeconds(-73);
            d.Refresh();
        });

        // Content that grows: a question of two sentences, a topic name as long as they come, a
        // two-hour timer, eleven versions of the code and a file path that will not fit.
        var q11 = Guid.NewGuid();
        Add("stress-long-answer", () =>
        {
            d.HideCodePanel(byUser: false);
            d.ReceiveQuestion(q11, 11, "Could you walk me through how you would design the evaluation harness for a retrieval augmented system, including how you would build the labelled set and keep it from drifting?", "machineLearningFundamentalsAndOperations");
            d.BeginAIWork(q11);
            d.StartAnswer(q11);
            var longAnswer = string.Join("\n", new[]
            {
                "- Start from real questions: sample them from production logs, strip anything personal and label a few hundred by hand.",
                "- Split the harness in two: retrieval metrics (recall at k, mean reciprocal rank) and answer metrics (faithfulness, relevance, completeness).",
                "- Keep the labelled set honest: freeze a version, add new cases in a separate file and review disagreements between labelers before merging.",
                "- Run it on every change to the prompt, the chunker or the embedding model, and fail the build when a metric drops by more than its noise floor.",
                "- Track drift: compare the live question mix against the labelled mix each week and add cases where they diverge.",
                "- Report a table per slice, not one number, so a regression in a rare but important slice is not averaged away.",
                "- Close the loop: every production complaint becomes a test case with the expected answer attached.",
            });
            d.UpdateAnswer(q11, longAnswer);
            d.FinishAnswer(q11, longAnswer, 123456);
        });
        Add("stress-many-versions", () =>
        {
            for (var index = 1; index <= 11; index++)
            {
                d.ProcessorDidStartCodingTask(new CodingTaskInfo(Guid.NewGuid(), $"Version {index} of a request that is written the way people actually dictate one", false, true));
                d.ProcessorDidUpdateCodingTask(typeScriptSteps);
                d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            }
        });
        Add("stress-long-file-and-timer", () =>
        {
            d.ProcessorDidStartCodingTask(new CodingTaskInfo(Guid.NewGuid(), "Write the service", false, true));
            d.ProcessorDidUpdateCodingTask("""
                STEP 1 — file: src/services/interview/evaluation/very_long_module_name_for_layout_testing.py
                Why: This step wires the retrieval metrics into the harness and keeps the labelled set frozen so a change in the numbers can only come from the change under test.
                ```python
                def evaluate(cases, retriever, judge, k=5):
                    return [judge(case, retriever(case.question, k)) for case in cases]
                ```
                """);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            d.IsInterviewActive = true;
            d.RecordingStartTime = InterviewController.Now().AddSeconds(-7300);
            d.Refresh();
        });

        Add("hearing-speech", () =>
        {
            focus.Header.Capsule.Update(ActivityMode.Listening(true), true, "12:07");
            main.Bar.Capsule.Update(ActivityMode.Listening(true), false, "");
            focus.Header.Relayout();
        });

        // Loading states, one wait at a time, through the same calls the processor makes.
        var transcribingId = Guid.NewGuid();
        Add("loading-transcribing", () =>
        {
            d.EndAllAIWork();
            d.ShowLoading("🎙️ Transcribing...", transcribingId);
        });
        Add("loading-connecting", () =>
        {
            d.HideLoading(transcribingId);
            // HideLoading refreshes on the UI queue; the capsule is set after it, as audio that is still starting would be.
            Dispatcher.UIThread.Post(() =>
            {
                focus.Header.Capsule.Update(ActivityMode.Connecting, false, "01:12");
                main.Bar.Capsule.Update(ActivityMode.Connecting, false, "");
                focus.Header.Relayout();
            });
        });
        var waitingId = Guid.NewGuid();
        var waitingSince = DateTime.UtcNow;
        Add("loading-answer-generating", () =>
        {
            d.ReceiveQuestion(waitingId, 20, "How would you design a rate limiter for a public API?", "systemDesign");
            d.BeginAIWork(waitingId);
            d.StartAnswer(waitingId);
            waitingSince = d.Questions.Entries.First(entry => entry.Id == waitingId).DetectedAt;
        });
        Add("loading-answer-slow", () =>
        {
            FocusAnswerView.Now = () => waitingSince.AddSeconds(6.2);
            d.Refresh();
        });
        Add("loading-answer-stalled", () =>
        {
            FocusAnswerView.Now = () => waitingSince.AddSeconds(12.2);
            d.Refresh();
        });
        const string limiterAnswer = "- Start with a token bucket per API key.\n- Keep the counters in Redis and update them with one atomic script.\n- Return 429 with a Retry-After header when the bucket is empty.";
        Add("loading-answer-arrived", () =>
        {
            FocusAnswerView.Now = () => DateTime.UtcNow;
            d.UpdateAnswer(waitingId, limiterAnswer);
        });
        Add("loading-answer-queued", () =>
        {
            d.FinishAnswer(waitingId, limiterAnswer, 1620);
            d.ReceiveQuestion(Guid.NewGuid(), 21, "And how would you keep the limiter correct across regions?", "systemDesign");
        });
        var codingId = Guid.NewGuid();
        var codingSince = DateTime.UtcNow;
        Add("loading-code-writing", () =>
        {
            d.ReceiveQuestion(codingId, 22, "Write a function that merges two sorted lists.", "coding");
            d.BeginAIWork(codingId);
            d.StartAnswer(codingId);
            d.ProcessorDidStartCodingTask(CodeInfo(codingId, "Write a function that merges two sorted lists."));
            codingSince = focus.CodePanel.Versions[^1].StartedAt;
            waitingSince = d.Questions.Entries.First(entry => entry.Id == codingId).DetectedAt;
        });
        Add("loading-code-slow", () =>
        {
            FocusAnswerView.Now = () => waitingSince.AddSeconds(6.2);
            CodePanelView.Now = () => codingSince.AddSeconds(6.2);
            d.Refresh();
            focus.CodePanel.UpdateLatest("");
        });
        Add("loading-code-arrived", () =>
        {
            FocusAnswerView.Now = () => DateTime.UtcNow;
            CodePanelView.Now = () => DateTime.UtcNow;
            d.UpdateAnswer(codingId, "- Walk both lists with two pointers.\n- Code is in the panel on the right (2 steps).");
            d.ProcessorDidUpdateCodingTask(codeSteps);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
        });

        // Feedback: what the window says about audio, errors, saves and undo.
        Add("feedback-audio-muted", () =>
        {
            d.EndAllAIWork();
            d.ResetTranscription();
            d.AudioPresentationOverride = CapturePresentation.Muted;
            d.Refresh();
        });
        Add("feedback-audio-unavailable", () =>
        {
            d.AudioPresentationOverride = CapturePresentation.Unavailable;
            d.Refresh();
        });
        Add("feedback-error", () =>
        {
            d.AudioPresentationOverride = CapturePresentation.Listening(CaptureDevice.SystemAudio);
            d.Refresh();
            d.ProcessorDidUpdateStatus("Error: Groq transcription network issue. Check connection and try again.");
        });
        // A confirmation does not cover an error.
        Add("feedback-error-kept", () => d.PostNotice(FeedbackNotice.Confirmation("screenshot", "Screenshot captured")));
        // Hearing a question again is the thing an error about hearing was waiting for.
        Add("feedback-error-resolved", () =>
            d.ProcessorDidReceiveQuestion("Why did the build fail on main?", "testing", MessageType.Answer, CaptureDevice.SystemAudio, Guid.NewGuid(), 30));
        Screenshot Shot() => new(new byte[] { 0 }, 160, 90);
        Add("feedback-confirmation", () =>
        {
            d.ReplaceScreenshots(new[] { Shot() });
            d.PostNotice(FeedbackNotice.Confirmation("screenshot", "Screenshot captured", detail: ScreenshotPresentation.LiveScreenshotStatusText(1)));
        });
        Add("feedback-undo", () =>
        {
            d.ReplaceScreenshots(new[] { Shot(), Shot() });
            d.PinnedSolution = "def reverse(text):\n    return text[::-1]";
            d.ClearScreenshots();
        });
        // A rejected key is fixed in Settings, so the button is on the error.
        Add("feedback-error-key", () => d.ProcessorDidUpdateStatus("Error: Groq transcription failed: Groq API error (401): Invalid API Key"));
        // A failed screenshot is tried again from where it failed.
        Add("feedback-error-screenshot", () =>
            d.PostNotice(FeedbackCopy.ScreenshotFailed("No display found. Connect or wake a display, then try again.")));
        var failedId = Guid.NewGuid();
        Add("feedback-answer-failed", () =>
        {
            d.DismissNotice();
            d.ReceiveQuestion(failedId, 31, "How do you keep a flaky test suite stable?", "testing");
            d.BeginAIWork(failedId);
            d.StartAnswer(failedId);
            d.ProcessorDidReceiveAnswerChunk("Error: The request timed out.", failedId);
        });
        Add("feedback-answer-retried", () => d.RetryAnswer(failedId));
        Add("feedback-audio-restored", () =>
        {
            d.EndAllAIWork();
            d.AudioPresentationOverride = CapturePresentation.Listening(CaptureDevice.SystemAudio);
            d.Refresh();
        });

        // The longest things a notice says, each beside the busiest capsule the header can hold.
        var busyId = Guid.NewGuid();
        Add("notice-widest-analyze", () =>
        {
            d.DismissNotice();
            d.ReceiveQuestion(busyId, 41, "What would you change first?", "testing");
            d.BeginAIWork(busyId);
            d.PostNotice(FeedbackCopy.NothingToAnalyze);
        });
        Add("notice-widest-busy", () =>
        {
            d.DismissNotice();
            d.ProcessorDidUpdateStatus("Error: Groq API error (429): rate limit reached");
        });
        Add("notice-widest-timeout", () =>
        {
            d.DismissNotice();
            d.ProcessorDidUpdateStatus("Error: Groq transcription timed out after 30s");
        });
        Add("notice-widest-session", () =>
        {
            d.DismissNotice();
            d.PostNotice(FeedbackCopy.SessionNotSaved("The file couldn’t be saved because the disk is full"));
        });
        Add("notice-widest-saved", () =>
        {
            d.DismissNotice();
            d.PostNotice(FeedbackCopy.SessionSaved(@"C:\Users\me\Documents\InterviewMaster\sessions\session.json"));
        });
        Add("notice-widest-exported", () =>
        {
            d.DismissNotice();
            d.PostNotice(FeedbackCopy.Exported("interview_2026-09-29_08-21.md", @"C:\Users\me\Documents\interview_2026-09-29_08-21.md"));
        });
        Add("notice-widest-nothing", () =>
        {
            d.DismissNotice();
            d.PostNotice(FeedbackCopy.NothingToExport);
            d.FinishAnswer(busyId, "- Start with the flakiest tests and fix their shared cause.", 1200);
            d.EndAllAIWork();
        });

        // A topic the classifier names in camelCase reads as the words people say.
        var acronymId = Guid.NewGuid();
        Add("topic-acronym", () =>
        {
            Ask(acronymId, 42, "How does RAG differ from CAG?", "ragCag");
            Answer(acronymId, "- RAG retrieves at question time; CAG preloads the context into the cache.\n- Use RAG for large or changing corpora and CAG for small stable ones.");
        });

        // The header holds the status on the live tab; on the Context tab the bar has to carry it.
        Add("notes-tab-status", () => d.SwitchTab(Tab.Notes));
        // Typing along: the second step is the one being typed, the others are set back.
        Add("code-step-two", () =>
        {
            d.SwitchTab(Tab.Voice);
            var stepping = Guid.NewGuid();
            Ask(stepping, 60, "Write a function that reverses a string and test it", "coding");
            d.ProcessorDidStartCodingTask(CodeInfo(stepping, "Write a function that reverses a string and test it"));
            d.ProcessorDidUpdateCodingTask(refined);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            focus.CodePanel.MoveStep(forward: true);
        });
        // The last step is below the fold at this size: stepping to it scrolls it to the top.
        Add("code-step-three-scrolled", () => focus.CodePanel.MoveStep(forward: true));

        // From here on the states have no Mac reference: they are the parts the Mac driver does not reach.
        var macStates = steps.Count;
        var editor = notesView.Editor;
        void Type(string text) => editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = editor });
        void Press(Key key) => editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = editor });
        void Note(string what) => log.Add($"notes {what}");

        Add("notes-toolbar-on-selection", () =>
        {
            d.ResetFocus();
            d.SwitchTab(Tab.Notes);
            editor.Focus();
            const string words = "Explain closures";
            editor.Select(notes.Text.IndexOf(words, StringComparison.Ordinal), words.Length);
        });
        Add("notes-formatted-and-list-carried-on", () =>
        {
            Note($"selection before formatting: {editor.SelectionStart}..{editor.SelectionEnd} caret {editor.CaretIndex} focused {editor.IsFocused}");
            notes.Format(FormattingCommand.Italic);
            const string bullet = "- Hash function (MD5/Base62)";
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = notes.Text.IndexOf(bullet, StringComparison.Ordinal) + bullet.Length;
            Press(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Type("Rate limiting");
            Dispatcher.UIThread.RunJobs();
            Note($"italic: {notes.Text.Contains("*Explain closures*")}");
            Note($"list carried on: {notes.Text.Contains(bullet + "\n- Rate limiting\n")}");
            Press(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Press(Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Note($"empty bullet ends the list: {notes.Text.Contains("- Rate limiting\n\n- Database")}");
            Note($"saved: {SettingsStore.Standard.GetString(NotesEditing.StorageKey) == notes.Text}");
        });
        Add("notes-search-found", () =>
        {
            notes.ToggleSearch();
            notesView.Search.Field.Text = "Cache";
            notes.PerformSearch("Cache");
        });
        Add("notes-search-nothing", () => notes.PerformSearch("kubernetes"));
        Add("notes-search-closed", () =>
        {
            Note($"search result: {notesView.Search.ResultText}");
            notes.ToggleSearch();
        });
        Add("notes-templates", () =>
        {
            notes.ShowTemplates();
            ((TemplatePickerView)shell.PanelContent!).Show(TemplateCategory.TestAutomation);
        });
        Add("notes-template-loaded", () => notes.LoadTemplate(BuiltInTemplates.TestAutomation[0]));
        Add("notes-new-note", () =>
        {
            Note($"template is not saved until edited: {SettingsStore.Standard.GetString(NotesEditing.StorageKey) != notes.Text}");
            notes.NewNote();
            Note($"new note saved: {SettingsStore.Standard.GetString(NotesEditing.StorageKey) == NotesEditing.NewNote}");
        });

        var settingsWindow = new SettingsController(shell, d.Refresh);
        Add("settings", () =>
        {
            d.SwitchTab(Tab.Voice);
            AppSettings.Shared.Frameworks = "Playwright, Pytest, AWS";
            ApiKeyManager.Shared.SetKey("driver-sample-key", ApiKeyType.Groq);
            settingsWindow.Show();
        });
        Add("settings-saved", () =>
        {
            var view = settingsWindow.View!;
            view.Role.SelectedIndex = 0;
            view.Response.SelectedIndex = 1;
            view.Frameworks.Text = "  Playwright, k6  ";
            settingsWindow.Save(view);
            log.Add($"settings role: {AppSettings.Shared.Role.DisplayName()}, response: {AppSettings.Shared.ResponseLanguage.DisplayName()}, stack: '{AppSettings.Shared.Frameworks}'");
        });

        // A real picture, so the thumbnails have something to scale.
        Screenshot Picture()
        {
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(320, 180), new Vector(96, 96));
            bitmap.Render(window);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return new Screenshot(stream.ToArray(), 320, 180);
        }
        var alert = new ScreenshotAlertPresenter(shell);
        Add("screenshot-alert", () => alert.Show(new[] { Picture(), Picture(), Picture() }));
        Add("screenshot-alert-hidden", alert.Hide);

        var floating = new FloatingSolutionPresenter(shell, () => d.PinnedSolution, () => d.Messages);
        Add("floating-solution", () =>
        {
            AppSettings.Shared.Role = InterviewRole.SeniorQAEngineer;
            AppSettings.Shared.ResponseLanguage = SpeakingLanguage.English;
            d.ResetFocus();
            var asked = Guid.NewGuid();
            Ask(asked, 70, "Write a function that reverses a string and test it", "coding");
            d.ProcessorDidStartCodingTask(CodeInfo(asked, "Write a function that reverses a string and test it"));
            d.ProcessorDidUpdateCodingTask(refined);
            d.ProcessorDidFinishCodingTask(CodingTaskOutcome.Complete);
            Answer(asked, "- Slice with a step of minus one: **O(n)** time\n- Test the empty string and a palindrome");
            floating.Show();
            floating.ShowLoading();
            log.Add($"floating keys: scroll {floating.HandleKey(ShortcutKey.J)}, move {floating.HandleKey(ShortcutKey.Right)}, other {floating.HandleKey(ShortcutKey.B)}");
        });
        Add("floating-solution-dismissed", floating.Dismiss);

        var from = int.TryParse(Environment.GetEnvironmentVariable("QA_FROM"), out var first) ? first : 1;
        var pause = int.TryParse(Environment.GetEnvironmentVariable("QA_STEP_PAUSE_MS"), out var given) ? given : 700;
        await Task.Delay(200);
        for (var index = 0; index < steps.Count; index++)
        {
            var (name, run) = steps[index];
            if (index + 1 < from) continue;
            run();
            await Task.Delay(550);
            var file = $"{index + 1:00}-{name}";
            if (only is not null && !only.Contains(name) && !only.Contains($"{index + 1:00}"))
            {
                await Task.Delay(pause);
                continue;
            }
            // The follow-up actions show under the pointer; reveal them so a run does not depend on where it is.
            focus.CodePanel.SetQuickActionsRevealed(true);
            Dispatcher.UIThread.RunJobs();
            Snapshot.Save(window, main, output, file);
            if (shell.PanelContent is { } panel) Snapshot.SavePanel(panel, shell.PanelSize, output, file + ".panel");
            log.Add($"snap {file}");
            // The macOS driver takes this long over its audits, and waits that are counted in seconds show it.
            await Task.Delay(pause);
        }
        log.AddRange(shell.Calls.Select(call => $"shell {call}"));
        File.WriteAllLines(Path.Combine(output, "driver.log"), log);
        d.Shutdown();
        return 0;
    }
}

public sealed class DriverApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DriverApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
