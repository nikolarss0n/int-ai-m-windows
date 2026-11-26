using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Presentation;
using InterviewMaster.Support;

namespace InterviewMaster.UiDriver;

/// Runs the app as the Windows entry point puts it together (`AppHost`, the real shell and windows)
/// on stand-ins for the operating system, and checks what the wiring does: tabs, the keyboard, the
/// tray menu's commands, closing and quitting. No network, no devices, no keys, no saved data.
public static class AppDriver
{
    private sealed class Platform(List<string> log) : IPlatformServices, INativeWindows
    {
        public Keys? Keys { get; private set; }
        public bool OffersGlobalKeys { get; set; } = true;

        public INativeWindows Windows => this;
        public IScreenCapture? ScreenCapture => null;
        public bool HasAudioPermission(CaptureDevice device) => true;
        public Task PreflightAudioAsync(CaptureDevice device, bool allowPrompt) => Task.CompletedTask;
        public IAudioSourceCapture MakeAudioCapture(CaptureDevice device, IMainThread mainThread) =>
            throw new InvalidOperationException("The driver has no audio device.");
        public IPracticeRecorder? MakePracticeRecorder() => null;
        public IGlobalKeys? MakeGlobalKeys(IMainThread mainThread, Action<ShortcutAction> perform) =>
            OffersGlobalKeys ? Keys = new Keys(perform) : null;

        public void ExcludeFromCapture(IntPtr window) => log.Add($"native: exclude {window}");
        public void SetClickThrough(IntPtr window, bool on) => log.Add($"native: clickThrough {window} {on}");
        public void SetNeverActivates(IntPtr window, bool on) => log.Add($"native: neverActivates {window} {on}");
        public void HideFromTaskSwitcher(IntPtr window) => log.Add($"native: hideFromSwitcher {window}");
    }

    /// The keyboard hook without Windows: the same router, fed by the script.
    private sealed class Keys(Action<ShortcutAction> perform) : IGlobalKeys
    {
        private readonly ShortcutRouter router = new();

        public ShortcutState State { get; set; } = new();
        public Action<KeyStroke>? KeyObserved { get; set; }
        public bool ObservesKeys { get; set; }
        public bool Installed { get; private set; }
        public bool Disposed { get; private set; }
        public int Resets { get; private set; }

        public void Install() => Installed = true;
        public void ResetPushToTalk() => Resets++;
        public void Dispose() => Disposed = true;

        /// Returns whether the key was kept from the app in front.
        public bool Press(ShortcutKey key, bool control = true, bool shift = false, bool alt = false, bool isDown = true)
        {
            var stroke = new KeyStroke(key, isDown, control, shift, alt);
            if (isDown && ObservesKeys) KeyObserved?.Invoke(stroke);
            var decision = router.Handle(stroke, State);
            if (decision.Action != ShortcutAction.None) perform(decision.Action);
            Dispatcher.UIThread.RunJobs();
            return decision.Consume;
        }
    }

    public static async Task<int> RunAsync(string output)
    {
        SettingsStore.Standard = new InMemorySettingsStore();
        ApiKeyManager.Shared = new ApiKeyManager(
            Path.Combine(Path.GetTempPath(), $"interview-master-driver-no-keys-{Guid.NewGuid():N}"), _ => null);
        Token.Font.Family = new FontFamily("fonts:Inter#Inter");
        Token.Motion.ReducesMotion = () => true;

        var log = new List<string>();
        var failures = 0;
        void Check(bool ok, string what)
        {
            log.Add($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }

        var handles = new Dictionary<Window, IntPtr>();
        DesktopShell.HandleOf = target => handles.TryGetValue(target, out var handle) ? handle : handles[target] = new IntPtr(handles.Count + 1);

        var scratch = Directory.CreateTempSubdirectory("interview-master-app-driver").FullName;
        var platform = new Platform(log);
        var exits = 0;
        AppHost Launch(string name)
        {
            var mainThread = new DispatcherMainThread();
            var host = new AppHost(
                new MainWindow(), platform, () => exits++,
                new PracticeStore(Path.Combine(scratch, name)), PracticeBankLoader.Load(), new Random(7),
                new VoiceInterviewProcessor(mainThread, memoryRetrieval: new MemoryRetrievalUseCase(new MemoryStore(Path.Combine(scratch, name + "-memory")))));
            host.Interview.SessionsDirectory = Path.Combine(scratch, name + "-sessions");
            host.Interview.AnalyzesSavedSessions = false;
            host.Interview.Environment = _ => null;
            host.Practice.MakeClient = () => null;
            return host;
        }

        var app = Launch("first");
        var shell = app.Shell;
        var keys = platform.Keys!;
        MainWindow Main() => (MainWindow)handles.Keys.First(window => window is MainWindow);
        void Pump() => Dispatcher.UIThread.RunJobs();
        void Save(Window target, string name)
        {
            target.UpdateLayout();
            var pixels = new PixelSize((int)(target.Width * 2), (int)(target.Height * 2));
            using var shot = new RenderTargetBitmap(pixels, new Vector(192, 192));
            shot.Render(target);
            using var stream = File.Create(Path.Combine(output, name + ".png"));
            if (target.Background is not ISolidColorBrush { Color.A: 0 })
            {
                shot.Save(stream);
                return;
            }
            // A see-through window is shown over a bright striped desktop, the hardest thing to read over.
            using var composed = new RenderTargetBitmap(pixels, new Vector(192, 192));
            using (var context = composed.CreateDrawingContext())
            {
                for (var stripe = 0; stripe * 24 < target.Width; stripe++)
                {
                    var brush = new SolidColorBrush(stripe % 2 == 0 ? Color.FromRgb(236, 238, 242) : Color.FromRgb(120, 160, 220));
                    context.FillRectangle(brush, new Rect(stripe * 24, 0, 24, target.Height));
                }
                context.DrawImage(shot, new Rect(0, 0, pixels.Width, pixels.Height), new Rect(0, 0, target.Width, target.Height));
            }
            composed.Save(stream);
        }
        /// Answers the dialog on screen with the button called `title`, after `look` has seen it.
        async Task<bool> Answer(Task pending, string title, Action<AlertView>? look = null)
        {
            Pump();
            if (shell.PresentedAlert is not { } alert)
            {
                Check(false, $"a dialog offering \"{title}\" is on screen");
                return false;
            }
            look?.Invoke(alert);
            var button = alert.Buttons.FirstOrDefault(candidate => candidate.Title == title);
            Check(button is not null, $"dialog \"{alert.TitleText}\" offers \"{title}\" [{string.Join(" | ", alert.Buttons.Select(candidate => candidate.Title))}]");
            if (button is null) return false;
            button.PerformClick();
            Pump();
            await pending;
            Pump();
            return true;
        }
        MenuEntry Entry(params string[] path)
        {
            IReadOnlyList<MenuEntry> level = app.Menu;
            MenuEntry? found = null;
            foreach (var title in path)
            {
                found = level.First(entry => entry.Title == title);
                level = found.Children;
            }
            return found!;
        }

        // Launch
        Check(!shell.IsWindowVisible, "nothing is on screen before the app starts");
        app.Start();
        Pump();
        var main = Main();
        Check(shell.IsWindowVisible, "the window is up after launch");
        Check(main is { Topmost: true, ShowInTaskbar: false, ShowActivated: false, WindowDecorations: WindowDecorations.None }, "the window floats, has no title bar and no taskbar button");
        Check(main is { Width: 760, Height: 500, MinWidth: 760, MinHeight: 500 }, "the window is 760 by 500 and no smaller");
        Check(log.IndexOf("native: exclude 1") is var excluded and >= 0 && log.Contains("native: hideFromSwitcher 1"), "the window is kept out of capture and the task switcher");
        Check(log.Contains("native: neverActivates 1 True") && !shell.AcceptsKeyboard, "the window never takes the keyboard on the timeline");
        Check(keys is { Installed: true, ObservesKeys: true } && app.HasGlobalKeys, "the keyboard hook is installed");
        Check(keys.State is { WindowVisible: true, CurrentTab: Tab.Voice, InterviewActive: false, ModalOpen: false }, "the hook knows the window is up on the timeline");
        Save(main, "01-launch");
        File.WriteAllLines(Path.Combine(output, "tray-menu.txt"), Describe(app.Menu, ""));

        // Tabs and search
        Check(keys.Press(ShortcutKey.Digit1) && main.View.CurrentTab == Tab.Notes, "Ctrl+1 shows the notes");
        Check(keys.State.CurrentTab == Tab.Notes, "the hook knows the tab changed");
        Check(keys.Press(ShortcutKey.F) && app.Notes.IsSearchVisible, "Ctrl+F opens the search in the notes");
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump();
        Check(!app.Notes.IsSearchVisible, "Escape closes the search");
        Check(keys.Press(ShortcutKey.Digit2) && main.View.CurrentTab == Tab.Voice, "Ctrl+2 shows the timeline");
        Check(!keys.Press(ShortcutKey.F), "Ctrl+F is left to the app in front outside the notes");

        // Practice takes the keyboard
        log.Add("-- practice");
        Check(keys.Press(ShortcutKey.Digit4) && main.View.CurrentTab == Tab.Practice, "Ctrl+4 shows practice");
        Check(shell.AcceptsKeyboard && log.Contains("native: neverActivates 1 False"), "the window may take the keyboard on practice");
        Check(keys.State is { CurrentTab: Tab.Practice, PracticeInteractionEnabled: true }, "the hook knows practice has the keyboard");
        Check(keys.Press(ShortcutKey.S) && main.View.CurrentTab == Tab.Practice, "Ctrl+S is swallowed on practice and does not leave it");
        Check(!app.Practice.HasActiveSession, "no practice session before one is started");
        Check(keys.Press(ShortcutKey.Enter) && app.Practice.HasActiveSession, "Ctrl+Enter starts the practice run");
        Save(main, "02-practice-session");

        // Closing asks first
        var closing = app.RequestCloseAsync();
        await Answer(closing, "Cancel", alert =>
        {
            Check(keys.State.ModalOpen, "the hook knows a dialog is open");
            Check(keys.Press(ShortcutKey.Digit2) && main.View.CurrentTab == Tab.Practice, "shortcuts are swallowed while a dialog is open");
            if (TopLevel.GetTopLevel(alert) is Window dialog) Save(dialog, "03-close-question");
        });
        Check(shell.IsWindowVisible && !keys.State.ModalOpen, "Cancel keeps the window up");
        await Answer(app.RequestCloseAsync(), "Save & Close");
        Check(!shell.IsWindowVisible && !shell.AcceptsKeyboard, "Save & Close hides the window and gives the keyboard back");
        Check(!keys.State.WindowVisible, "the hook knows the window is hidden");
        Check(!keys.Press(ShortcutKey.Digit2), "tab shortcuts are left alone while the window is hidden");

        Check(keys.Press(ShortcutKey.B) && shell.IsWindowVisible, "Ctrl+B brings the window back");
        Check(shell.AcceptsKeyboard && main.View.CurrentTab == Tab.Practice, "it comes back on practice with the keyboard");
        Check(app.Practice.HasActiveSession, "with the session it was hidden with");

        // Click-through and moving
        log.Add("-- window");
        keys.Press(ShortcutKey.Digit2);
        Check(!shell.AcceptsKeyboard && log.Last(line => line.StartsWith("native: neverActivates 1")) == "native: neverActivates 1 True", "leaving practice gives the keyboard up");
        Check(keys.Press(ShortcutKey.I) && log.Contains("native: clickThrough 1 True") && app.Interview.IsClickThrough, "Ctrl+I lets clicks through");
        Check(keys.Press(ShortcutKey.I) && log.Last(line => line.StartsWith("native: clickThrough")) == "native: clickThrough 1 False", "Ctrl+I again takes clicks back");
        main.Position = new PixelPoint(400, 300);
        var before = main.Position;
        keys.Press(ShortcutKey.Left, shift: true, alt: true);
        keys.Press(ShortcutKey.Down, shift: true, alt: true);
        log.Add($"screens: {string.Join("; ", main.Screens.All.Select(screen => screen.WorkingArea))}, moved {before} -> {main.Position}");
        Check(main.Position == new PixelPoint(before.X - (int)(40 * main.DesktopScaling), before.Y + (int)(40 * main.DesktopScaling)), "Ctrl+Alt+Shift+arrows move the window a step");
        for (var step = 0; step < 80; step++) keys.Press(ShortcutKey.Left, shift: true, alt: true);
        Check(main.Position.X == main.Screens.All.Min(screen => screen.WorkingArea.X), "the window stops at the edge of the screens");
        main.KeyPress(Key.Right, RawInputModifiers.Control, PhysicalKey.ArrowRight, null);
        Pump();
        Check(main.Position.X == main.Screens.All.Min(screen => screen.WorkingArea.X) + (int)(20 * main.DesktopScaling), "Ctrl+arrow in the window moves it a small step");

        // The tray menu
        log.Add("-- tray menu");
        Entry("Hide Window").Run!();
        Check(!shell.IsWindowVisible, "Hide Window hides it");
        Entry("Show Window").Run!();
        Check(shell.IsWindowVisible, "Show Window shows it");
        app.ToggleFromTray();
        app.ToggleFromTray();
        Check(shell.IsWindowVisible, "two clicks on the tray icon hide and show it");

        Check(Entry("Answer Model", "Grok 4.20 (fastest)").Checked == true, "the menu ticks the current answer model");
        await Answer(app.SelectAnswerModelAsync(AnswerModelOption.All[1]), "OK", alert => Check(alert.TitleText == "OpenAI key needed", "a GPT model without an OpenAI key says so"));
        Check(AppSettings.Shared.AnswerModel == AnswerModel.Grok, "and the model is not changed");

        var interview = app.Interview.StartInterviewAsync();
        await Answer(interview, "Cancel", alert => Check(alert.TitleText == "Groq API key needed", "starting without a Groq key asks for one"));
        Check(!app.Interview.IsInterviewActive, "and no interview starts");

        Entry("Use Microphone").Run!();
        Pump();
        if (shell.PresentedAlert is { } disclosure)
        {
            Check(disclosure.TitleText == "Use the microphone for the interviewer", "the first switch to the microphone explains it");
            disclosure.Buttons[0].PerformClick();
            Pump();
            await Task.Yield();
            Pump();
        }
        Check(Entry("Use Microphone").Checked == true, "the menu ticks the microphone once it is the source");

        Entry("Background Transparency…").Run!();
        Pump();
        var slider = shell.Panels.Select(panel => panel.Content).OfType<TransparencyPanelView>().FirstOrDefault();
        Check(slider is not null, "the menu opens the transparency slider");
        if (slider is not null)
        {
            slider.Slider.Value = 0.5;
            Pump();
            Check(Math.Abs(AppSettings.Shared.BackgroundTransparency - 0.5) < 0.001 && slider.ValueText == "50%", "dragging it changes the setting at once");
            Save((Window)TopLevel.GetTopLevel(slider)!, "04-transparency");
            Save(main, "05-half-transparent");
        }

        Entry("Settings...").Run!();
        Pump();
        Check(shell.Panels.Any(panel => panel.Content is SettingsView) && log.Count(line => line.StartsWith("native: exclude")) >= 3, "Settings opens in a window of its own, kept out of capture");
        app.Settings.Close();
        Pump();
        Check(!shell.Panels.Any(panel => panel.Content is SettingsView), "and closes");

        Entry("Interview Templates...").Run!();
        Pump();
        Check(shell.Panels.Any(panel => panel.Content is TemplatePickerView), "the menu opens the templates");
        Entry("New Note").Run!();
        Pump();
        Check(main.View.CurrentTab == Tab.Notes && app.Notes.Text == NotesEditing.NewNote, "New Note starts one on the notes tab");

        var shortcuts = app.ShowKeyboardShortcutsAsync();
        await Answer(shortcuts, "OK", alert =>
        {
            Check(alert.MessageText.Contains("Ctrl+Alt+Shift+Arrows"), "Keyboard Shortcuts lists the Windows keys");
            if (TopLevel.GetTopLevel(alert) is Window dialog) Save(dialog, "06-shortcuts");
        });
        var about = app.ShowAboutAsync();
        Pump();
        if (TopLevel.GetTopLevel(shell.PresentedAlert) is Window aboutDialog)
        {
            Save(aboutDialog, "07-about");
            aboutDialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump();
            await about;
        }
        Check(about.IsCompleted && shell.PresentedAlert is null, "Escape closes About without opening the privacy page");

        // The floating code hears its keys from any app
        log.Add("-- floating code");
        Entry("Hide Window").Run!();
        app.Interview.PinnedSolution = "```python\nprint('hello')\n```";
        shell.FloatingSolution!.Show();
        Pump();
        Check(shell.Overlays.Count == 1 && shell.Overlays[0] is { Topmost: true, ShowActivated: false, ShowInTaskbar: false }, "the code floats in a window of its own");
        if (shell.Overlays.Count == 1)
        {
            var overlay = shell.Overlays[0];
            Check(log.Contains($"native: neverActivates {handles[overlay]} True") && log.Contains($"native: exclude {handles[overlay]}"), "which never takes the keyboard and is kept out of capture");
            var at = overlay.Position;
            Check(!keys.Press(ShortcutKey.Right) && overlay.Position.X == at.X + (int)(FloatingSolutionView.MoveStep * overlay.DesktopScaling), "Ctrl+Right moves it and still reaches the app in front");
            Save(overlay, "08-floating-code");
            keys.Press(ShortcutKey.Backslash);
            Check(shell.Overlays.Count == 0 && !shell.FloatingSolution.IsOpen, "Ctrl+\\ puts it away");
        }

        // Quitting asks about an unfinished practice session
        log.Add("-- quit");
        keys.Press(ShortcutKey.B);
        keys.Press(ShortcutKey.Digit4);
        if (!app.Practice.HasActiveSession) app.Practice.ResumeSavedSession();
        Pump();
        Check(app.Practice.HasActiveSession, "the saved practice session is resumed");
        await Answer(app.QuitAsync(), "Cancel");
        Check(exits == 0 && !app.IsQuitting, "Cancel keeps the app running");
        await Answer(app.QuitAsync(), "Save & Close");
        Check(exits == 1 && app.IsQuitting && keys.Disposed, "Save & Close quits and lets go of the keyboard");

        // Without the hook the window still answers its own keys
        log.Add("-- no keyboard hook");
        platform.OffersGlobalKeys = false;
        handles.Clear();
        app = Launch("second");
        shell = app.Shell;
        app.Start();
        Pump();
        main = Main();
        Check(!app.HasGlobalKeys, "the app runs without a keyboard hook");
        main.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, null);
        Pump();
        Check(main.View.CurrentTab == Tab.Notes, "Ctrl+1 in the window shows the notes");
        main.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        Pump();
        Check(app.Notes.IsSearchVisible, "Ctrl+F in the window opens the search");
        main.KeyPress(Key.D4, RawInputModifiers.Control, PhysicalKey.Digit4, null);
        Pump();
        Check(main.View.CurrentTab == Tab.Practice && shell.AcceptsKeyboard, "Ctrl+4 in the window shows practice");
        main.KeyPress(Key.OemComma, RawInputModifiers.Control, PhysicalKey.Comma, null);
        Pump();
        Check(shell.Panels.Any(panel => panel.Content is SettingsView), "Ctrl+, in the window opens Settings");
        await app.QuitAsync();
        Check(exits == 2, "an app with no practice session quits without asking");

        File.WriteAllLines(Path.Combine(output, "driver.log"), log);
        foreach (var line in log.Where(line => line.StartsWith("FAIL"))) Console.WriteLine(line);
        Console.WriteLine($"{log.Count(line => line.StartsWith("ok  "))} checks passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<string> Describe(IReadOnlyList<MenuEntry> entries, string indent)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                yield return indent + "---";
                continue;
            }
            var mark = entry.Checked switch { true => "[x] ", false => "[ ] ", _ => "" };
            yield return indent + mark + entry.Title + (entry.Enabled ? "" : " (disabled)");
            foreach (var line in Describe(entry.Children, indent + "    ")) yield return line;
        }
    }
}
