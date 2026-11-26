using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Presentation;
using InterviewMaster.Support;

namespace InterviewMaster.UiDriver;

/// Replays the practice tab through the states the macOS reference driver renders, on a practice
/// history of its own: no network, no microphone, no keys, and never the person's real history.
public static class PracticeDriver
{
    private const string Answer = "It keeps the system reliable: validate inputs early, cache what is expensive, and measure latency before scaling.";

    public static async Task<int> RunAsync(string output, HashSet<string>? only)
    {
        SettingsStore.Standard = new InMemorySettingsStore();
        ApiKeyManager.Shared = new ApiKeyManager(
            Path.Combine(Path.GetTempPath(), $"interview-master-driver-no-keys-{Guid.NewGuid():N}"), _ => null);
        Token.Font.Family = new FontFamily("fonts:Inter#Inter");
        Token.Motion.ReducesMotion = () => true;

        var data = Directory.CreateTempSubdirectory("interview-master-practice-driver").FullName;
        var bank = PracticeBankLoader.Load();
        var shell = new DriverShell();
        var main = new MainView();
        var window = new Window { Width = 860, Height = 760, Background = new SolidColorBrush(Color.FromRgb(27, 27, 29)), Content = main };
        window.Show();

        var log = new List<string>();
        PracticeView view = null!;
        PracticeController controller = null!;
        var left = false;

        var reservations = new MicrophoneReservations();
        void Launch(string name, IPracticeRecorder? recorder = null)
        {
            view = new PracticeView();
            controller = new PracticeController(view, shell, new PracticeStore(Path.Combine(data, name)), recorder, random: new Random(7), bank: bank)
            {
                AcquireMicrophone = () => reservations.Acquire(MicrophoneOwner.Practice),
                OwnsMicrophone = reservations.Owns,
                ReleaseMicrophone = reservations.Release,
                CanPresentPracticeUI = () => true,
                // No client: answers are scored on this machine and nothing is sent anywhere.
                MakeClient = () => null,
                RequestInputFocus = control => control?.Focus(),
                LeaveToTimeline = () => left = true,
            };
            main.SetTabView(Tab.Practice, view);
            main.ShowTab(Tab.Practice);
            controller.Activate();
        }

        void Click(HoverButton button, string name)
        {
            window.UpdateLayout();
            if (!button.IsVisible || !button.IsEffectivelyEnabled || button.Bounds.Width <= 0)
            {
                log.Add($"no button for {name}");
                return;
            }
            button.PerformClick();
            Dispatcher.UIThread.RunJobs();
        }
        void Select(string item)
        {
            foreach (var popup in new[] { view.Role, view.Mode, view.Count })
            {
                var index = (popup.ItemsSource as IEnumerable<string>)?.ToList().IndexOf(item) ?? -1;
                if (index < 0) continue;
                popup.SelectedIndex = index;
                Dispatcher.UIThread.RunJobs();
                return;
            }
            log.Add($"no popup with {item}");
        }
        void Type(string text, TextBox box)
        {
            box.Text = text;
            Dispatcher.UIThread.RunJobs();
        }
        void Resize(double width, double height)
        {
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        void Option(int index)
        {
            view.ChoiceCard.OptionButtons[index].PerformClick();
            Dispatcher.UIThread.RunJobs();
        }
        void AnswerAndReveal()
        {
            Type(Answer, view.RecallCard.ResponseView);
            Click(view.RecallCard.Reveal, "reveal");
        }

        var steps = new List<(string Name, Action Run)>();
        void Add(string name, Action run) => steps.Add((name, run));

        Launch("recall");
        Add("initial", () => { });
        Add("count-5", () => Select("5"));
        Add("recall-start", () => Click(view.Start, "start"));
        Add("recall-typed", () => Type(Answer, view.RecallCard.ResponseView));
        Add("recall-confidence", () => Click(view.RecallCard.ConfidencePicker.Buttons[1], "Mostly sure"));
        Add("recall-revealed", () => Click(view.RecallCard.Reveal, "reveal"));
        Add("recall-coverage-toggled", () => view.RecallCard.IdeaRows[0].PerformClick());
        Add("recall-repair", () => Click(view.RecallCard.Edit, "repair"));
        Add("recall-repair-empty", () => Click(view.RecallCard.Repair.Save, "Save"));
        Add("recall-repair-saved", () =>
        {
            Type("It also needs a rollback plan.", view.RecallCard.GapRepairResponder);
            Click(view.RecallCard.Repair.Save, "Save");
        });
        Add("recall-rated-again", () => Click(view.RecallCard.Again, "again"));
        Add("recall-dont-know", () => Click(view.RecallCard.DontKnow, "dont know"));
        Add("recall-rated-hard", () => Click(view.RecallCard.Hard, "hard"));
        Add("recall-undo", () => Click(view.Undo, "undo"));
        Add("recall-rated-got-it", () => Click(view.RecallCard.GotIt, "got it"));
        Add("recall-review-narrow", () =>
        {
            Resize(600, 760);
            AnswerAndReveal();
        });
        Add("recall-repair-narrow", () => Click(view.RecallCard.Edit, "repair"));
        Add("recall-edit-response", () =>
        {
            Resize(860, 760);
            Click(view.RecallCard.Edit, "repair");
            Click(view.RecallCard.GotIt, "got it");
            AnswerAndReveal();
            Click(view.RecallCard.GotIt, "got it");
        });
        Add("recall-last-question", AnswerAndReveal);
        Add("recall-retry-queued", () => Click(view.RecallCard.GotIt, "got it"));
        Add("recall-awaiting-finish", () =>
        {
            AnswerAndReveal();
            Click(view.RecallCard.GotIt, "got it");
        });
        Add("recall-report", () => Click(view.EndHeader, "end"));
        Add("history", () => Click(view.History, "history"));
        Add("history-closed", () => Click(view.History, "history"));
        Add("topic-off", () => view.TopicButtons[0].PerformClick());
        Add("target-date-on", () => view.TargetDateToggle.PerformClick());
        Add("mode-rehearse", () => Select("Voice Rehearsal"));
        Add("rehearse-start", () => Click(view.Start, "start"));
        Add("relaunch-with-saved", () => Launch("recall"));
        Add("resumed", () => Click(view.QuestionCard.Resume, "resume"));
        Add("interview-idle", () =>
        {
            Launch("interview");
            Select("Interview");
        });
        Add("interview-start", () => Click(view.Start, "start"));
        Add("interview-typed", () => Type("I would start from the failure modes and work back to the checks.", view.QuestionCard.AnswerView));
        Add("role-frontend", () =>
        {
            Launch("roles");
            Select("Frontend engineer");
        });
        Add("role-qa", () => Select("QA automation"));
        Add("cards-idle", () =>
        {
            Launch("cards");
            Select("Claude Architect");
        });
        Add("cards-count-5", () => Select("5"));
        Add("cards-start", () => Click(view.Start, "start"));
        Add("cards-selected", () => Option(0));
        Add("cards-second", () => Click(view.ChoiceCard.Submit, "next"));
        Add("cards-second-selected", () => Option(2));
        Add("cards-previous", () => Click(view.ChoiceCard.Previous, "previous"));
        Add("cards-last", () =>
        {
            Click(view.ChoiceCard.Submit, "next");
            for (var index = 0; index < 3; index++)
            {
                Click(view.ChoiceCard.Submit, "next");
                Option(1);
            }
        });
        Add("cards-report", () => Click(view.ChoiceCard.Submit, "finish"));
        Add("cards-history", () => Click(view.History, "history"));
        Add("cards-interview-idle", () =>
        {
            Click(view.History, "history");
            Select("Full Interview");
        });
        Add("cards-interview-start", () => Click(view.Start, "start"));
        Add("cards-interview-selected", () => Option(1));

        // Past the macOS reference: the paths it leaves out because they would send an answer to be judged.
        Add("cards-interview-report", () =>
        {
            for (var index = 0; index < 4; index++)
            {
                Click(view.ChoiceCard.Submit, "next");
                Option(index % 4);
            }
            Click(view.ChoiceCard.Submit, "finish");
        });
        Add("interview-scored", () =>
        {
            Launch("interview");
            Click(view.QuestionCard.Resume, "resume");
            Type("I would start from the failure modes and work back to the checks.", view.QuestionCard.AnswerView);
            Click(view.QuestionCard.Submit, "submit");
        });
        Add("interview-record-without-microphone", () => Click(view.QuestionCard.Record, "record"));
        Add("interview-exit-kept", () =>
        {
            shell.NextAnswer = "Continue";
            Click(view.QuestionCard.End, "exit run");
        });
        Add("interview-exit-saved", () =>
        {
            shell.NextAnswer = "Save & Exit";
            Click(view.EndHeader, "exit");
            log.Add($"left for the timeline after Save & Exit: {left}");
        });
        Add("interview-start-over-saved", () =>
        {
            Launch("interview");
            shell.NextAnswer = "Start New";
            Click(view.Start, "start");
        });
        Add("interview-exit-discarded", () => Click(view.EndHeader, "exit"));
        Add("window-close-question", () =>
        {
            Click(view.Start, "start");
            shell.NextAnswer = "Cancel";
            log.Add($"close allowed on Cancel: {controller.ShouldAllowWindowCloseAsync().GetAwaiter().GetResult()}");
            shell.NextAnswer = "Save & Close";
            log.Add($"close allowed on Save & Close: {controller.ShouldAllowWindowCloseAsync().GetAwaiter().GetResult()}");
            log.Add($"primary action while idle starts a run: {controller.HasActiveSession}");
        });

        // A microphone that records a second of silence: the paths around recording, with no device.
        var microphone = new ScriptedRecorder();
        MicrophoneLease? interviewLease = null;
        Add("rehearse-recording", () =>
        {
            Launch("voice", microphone);
            Select("Voice Rehearsal");
            Click(view.Start, "start");
            Click(view.RecallCard.Record, "start speaking");
        });
        Add("rehearse-transcription-unavailable", () =>
        {
            log.Add($"recording: {microphone.IsRecording}, reserved for practice: {reservations.Current?.Owner}");
            Click(view.RecallCard.Record, "stop recording");
        });
        Add("rehearse-microphone-refused", () =>
        {
            log.Add($"recording after stop: {microphone.IsRecording}, reservation released: {reservations.Current is null}");
            microphone.Granted = false;
            Click(view.RecallCard.Record, "start speaking");
        });
        Add("rehearse-microphone-busy", () =>
        {
            log.Add($"reservation released after a refusal: {reservations.Current is null}");
            interviewLease = reservations.Acquire(MicrophoneOwner.Interview);
            Click(view.RecallCard.Record, "start speaking");
        });
        Add("rehearse-recording-cancelled-by-reveal", () =>
        {
            if (interviewLease is not null) reservations.Release(interviewLease);
            microphone.Granted = true;
            Click(view.RecallCard.Record, "start speaking");
            Dispatcher.UIThread.RunJobs();
            Click(view.RecallCard.DontKnow, "dont know");
        });

        var from = int.TryParse(Environment.GetEnvironmentVariable("QA_FROM"), out var first) ? first : 1;
        await Task.Delay(200);
        for (var index = 0; index < steps.Count; index++)
        {
            var (name, run) = steps[index];
            run();
            // Autosave waits a quarter of a second, and a scored answer comes back on a later turn.
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            var file = $"{index + 1:00}-{name}";
            if (index + 1 < from || (only is not null && !only.Contains(name) && !only.Contains($"{index + 1:00}"))) continue;
            Save(window, view, output, file);
            log.Add($"snap {file}: {view.StatusText}");
        }
        log.AddRange(shell.Calls.Select(call => $"shell {call}"));
        File.WriteAllLines(Path.Combine(output, "driver.log"), log);
        controller.ShutdownMicrophone();
        return 0;
    }

    private sealed class ScriptedRecorder : IPracticeRecorder
    {
        public bool Granted { get; set; } = true;

        public bool IsRecording { get; private set; }

        public byte[]? Recording { get; private set; }

        public Task<bool> RequestAccessAsync() => Task.FromResult(Granted);

        public Task<bool> StartAsync()
        {
            IsRecording = true;
            Recording = null;
            return Task.FromResult(true);
        }

        public void Stop()
        {
            if (!IsRecording) return;
            IsRecording = false;
            Recording = PcmPracticeRecorder.Wav(new byte[PcmPracticeRecorder.SampleRate * 2]);
        }

        public void Discard() => Recording = null;
    }

    private static void Save(Window window, PracticeView view, string directory, string name)
    {
        window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Width * 2), (int)(window.Height * 2)), new Vector(192, 192));
        bitmap.Render(window);
        using (var stream = File.Create(Path.Combine(directory, name + ".png"))) bitmap.Save(stream);
        var entries = new List<(string Text, Control Control)>();
        var containers = new List<(string Kind, Control Control)>();
        Walk(view, entries, containers);
        string Frame(Control control)
        {
            var origin = control.TranslatePoint(new Point(0, 0), view) ?? new Point(-1, -1);
            return $"{Math.Round(origin.X)},{Math.Round(origin.Y)} {Math.Round(control.Bounds.Width)}x{Math.Round(control.Bounds.Height)}";
        }
        File.WriteAllText(
            Path.Combine(directory, name + ".txt"),
            $"window {window.Width}x{window.Height}\n---\n" + string.Join("\n", entries.Select(entry => entry.Text)));
        File.WriteAllLines(
            Path.Combine(directory, name + ".frames.txt"),
            entries.Select(entry => $"{Frame(entry.Control)} {(entry.Text.Length > 60 ? entry.Text[..60] : entry.Text)}")
                .Concat(containers.Select(container => $"{Frame(container.Control)} <{container.Kind}>")));
    }

    /// The words the macOS reference dump lists, in its notation: a button also lists its label, tick
    /// boxes say whether they are on, and what cannot be pressed says so.
    private static void Walk(Visual visual, List<(string, Control)> entries, List<(string, Control)> containers)
    {
        if (!visual.IsVisible || visual is not Control control) return;
        if (visual is not PracticeView && (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)) return;
        var disabled = control.IsEffectivelyEnabled ? "" : " (disabled)";
        switch (control)
        {
            case HoverButton button:
                if (string.IsNullOrEmpty(button.Title)) return;
                entries.Add(($"[btn] {button.Title}{disabled}", control));
                entries.Add(($"[label] {button.Title}", control));
                return;
            case ComboBox popup:
                if (popup.SelectedItem is string item) entries.Add(($"[btn] {item}{disabled}", control));
                containers.Add(("NSPopUpButton", control));
                return;
            case PracticeCheckBox check:
                entries.Add(($"[btn] {check.Title}{(check.IsChecked ? " (on)" : " (off)")}{disabled}", control));
                return;
            case PracticeChoiceOption option:
                entries.Add(($"[btn] {option.Title}", control));
                return;
            case PracticeDateStepper:
                containers.Add(("NSDatePicker", control));
                return;
            case PracticeProgressSegmentsView:
                containers.Add(("PracticeProgressSegmentsView", control));
                return;
            case TextBox box:
                if (!string.IsNullOrEmpty(box.Text)) entries.Add(($"[text] {box.Text.Replace("\n", " ⏎ ")}", control));
                containers.Add(("NSScrollView", control));
                return;
            case SelectableTextBlock reading:
                if (!string.IsNullOrEmpty(reading.Text)) entries.Add(($"[text] {reading.Text.Replace("\n", " ⏎ ")}", control));
                return;
            case TextBlock label:
                if (!string.IsNullOrEmpty(label.Text)) entries.Add(($"[label] {label.Text.Replace("\n", " ⏎ ")}", control));
                return;
            case ScrollViewer:
                containers.Add(("NSScrollView", control));
                break;
            case PracticeCardView and not PracticeGapRepairView:
                containers.Add((control.GetType().Name, control));
                break;
        }
        foreach (var child in visual.GetVisualChildren()) Walk(child, entries, containers);
    }
}
