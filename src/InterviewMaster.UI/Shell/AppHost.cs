using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// One row of the tray menu. `Checked` is null for a row that is not a tick.
public sealed class MenuEntry
{
    public MenuEntry(string title, Action? run = null)
    {
        Title = title;
        Run = run;
    }

    public static MenuEntry Separator() => new("") { IsSeparator = true };

    public string Title { get; }
    public Action? Run { get; }
    public bool IsSeparator { get; private init; }
    public bool? Checked { get; set; }
    public bool Enabled { get; set; } = true;
    public List<MenuEntry> Children { get; } = new();
}

/// Puts the app together: the window, the three tabs, the small windows, the tray menu's commands and
/// the keyboard. It stands where `InterviewMasterDelegate` does on macOS. Everything that needs the
/// operating system comes in through `IPlatformServices`, so the UI driver runs this same wiring.
public sealed class AppHost
{
    private const string HelpLink = "https://github.com/nikolayprosenikov/interview-master";
    private const string PrivacyLink = HelpLink + "/blob/main/PRIVACY.md";
    private const string AboutText =
        "Version 1.0.0\n\nAI-powered interview assistant for software engineers.\n\nCapture coding problems and get instant analysis from an AI model.\n\n© 2024 Nikolay Prosenikov";

    private readonly MainWindow window;
    private readonly Action exit;
    private readonly AudioCaptureCoordinator audio;
    private readonly MicrophoneReservations reservations = new();
    private readonly FloatingSolutionPresenter floating;
    private readonly ShortcutRouter localRouter = new();
    private readonly IGlobalKeys? keys;
    private readonly MenuEntry microphoneEntry;
    private readonly List<(AnswerModelOption Option, MenuEntry Entry)> modelEntries = new();
    private IPanel? transparencyPanel;
    private bool wasInterviewActive;
    private bool quitting;

    /// `exit` ends the process once the app has let go of its devices.
    public AppHost(
        MainWindow window, IPlatformServices platform, Action exit,
        PracticeStore? practiceStore = null, PracticeBankFile? practiceBank = null, Random? random = null,
        VoiceInterviewProcessor? processor = null)
    {
        this.window = window;
        this.exit = exit;
        var mainThread = new DispatcherMainThread();
        audio = new AudioCaptureCoordinator(
            mainThread, reservations, device => platform.MakeAudioCapture(device, mainThread), platform.PreflightAudioAsync,
            AudioSourceSelection.ResolveStartupCaptureDevice(
                AppSettings.Shared.CaptureDevice, platform.HasAudioPermission(CaptureDevice.Microphone)));

        Shell = new DesktopShell(window, platform.Windows);
        Interview = new InterviewController(
            window.View, Shell, mainThread, audio, processor ?? new VoiceInterviewProcessor(mainThread), platform.ScreenCapture)
        {
            HasAudioPermission = platform.HasAudioPermission,
            CloseWindow = RequestCloseAsync,
        };
        floating = new FloatingSolutionPresenter(Shell, () => Interview.PinnedSolution, () => Interview.Messages);
        Shell.FloatingSolution = floating;

        var notesView = new NotesView();
        window.View.SetTabView(Tab.Notes, notesView);
        Notes = new NotesController(notesView, Shell, Interview);

        var practiceView = new PracticeView();
        window.View.SetTabView(Tab.Practice, practiceView);
        Practice = new PracticeController(practiceView, Shell, practiceStore, platform.MakePracticeRecorder(), random, practiceBank)
        {
            LeaveToTimeline = () => Interview.SwitchTab(Tab.Voice),
            RequestInputFocus = control =>
            {
                if (IsPracticeAvailable) Shell.FocusInput(control);
            },
            CanPresentPracticeUI = () => IsPracticeAvailable,
            AcquireMicrophone = () => reservations.Acquire(MicrophoneOwner.Practice),
            OwnsMicrophone = reservations.Owns,
            ReleaseMicrophone = reservations.Release,
        };

        Settings = new SettingsController(Shell, Interview.Refresh);
        Shell.OpenSettings = Settings.Show;

        microphoneEntry = new MenuEntry("Use Microphone", Interview.ToggleMicrophoneSource) { Checked = false };
        Menu = BuildMenu();
        SyncMenu();

        Interview.TabChanged += OnTabChanged;
        Interview.WindowShown += () =>
        {
            if (window.View.CurrentTab == Tab.Practice) BeginPractice();
        };
        Interview.WindowHiding += () =>
        {
            if (window.View.CurrentTab == Tab.Practice) EndPractice();
        };
        Interview.AudioControlsChanged += SyncMenu;
        Interview.Refreshed += PublishState;
        Shell.Changed += PublishState;

        window.Closing += (_, e) =>
        {
            if (quitting) return;
            // Close means hide: Show Window must be able to bring the same window back.
            e.Cancel = true;
            _ = RequestCloseAsync();
        };
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);

        keys = InstallGlobalKeys(platform, mainThread);
        Interview.ApplyTransparency();
        PublishState();
    }

    public DesktopShell Shell { get; }
    public InterviewController Interview { get; }
    public NotesController Notes { get; }
    public PracticeController Practice { get; }
    public SettingsController Settings { get; }

    /// The app is on its way out: its devices are released and windows may close for good.
    public bool IsQuitting => quitting;

    /// The tray menu, top to bottom.
    public IReadOnlyList<MenuEntry> Menu { get; }

    /// A tick or a row's availability changed.
    public event Action? MenuChanged;

    /// Whether shortcuts reach the app from other apps. Without that, only the window's own keys work.
    public bool HasGlobalKeys => keys is not null;

    /// What the keyboard hook is told about the app right now.
    public ShortcutState ShortcutState => Interview.ShortcutState(Shell.IsModalOpen, Shell.AcceptsKeyboard);

    /// Shows the window as the app opens, and starts listening when the environment asks for it.
    public void Start()
    {
        Interview.ShowWindow(animated: false);
        if (Interview.Environment("IM_AUTO_START_INTERVIEW") != "1") return;
        Delayed.After(0.8, () =>
        {
            if (!Interview.IsInterviewActive) _ = Interview.StartInterviewAsync();
        });
    }

    // MARK: - Practice and the keyboard

    private bool IsPracticeAvailable =>
        window.View.CurrentTab == Tab.Practice && Shell.IsWindowVisible && !Shell.IsModalOpen && Shell.AcceptsKeyboard;

    private void OnTabChanged(Tab previous, Tab tab)
    {
        if (previous == Tab.Practice) EndPractice();
        if (tab == Tab.Practice) BeginPractice();
        PublishState();
    }

    /// Practice is the one place the person types, so the window takes the keyboard there.
    private void BeginPractice()
    {
        Shell.SetAcceptsKeyboard(true);
        Practice.Activate();
    }

    private void EndPractice()
    {
        Practice.Deactivate();
        Shell.SetAcceptsKeyboard(false);
    }

    // MARK: - Shortcuts

    private IGlobalKeys? InstallGlobalKeys(IPlatformServices platform, IMainThread mainThread)
    {
        var global = platform.MakeGlobalKeys(mainThread, Perform);
        if (global is null) return null;
        try
        {
            global.Install();
        }
        catch (Exception error)
        {
            Log.Print($"⚠️ Shortcuts work only while the window has the keyboard: {error.Message}");
            global.Dispose();
            return null;
        }
        // The floating code has no keyboard of its own: its keys are heard from whatever app is in front.
        global.KeyObserved = stroke =>
        {
            if (stroke.Control) floating.HandleKey(stroke.Key);
        };
        global.ObservesKeys = true;
        Log.Print("✅ Shortcuts are heard from every app");
        return global;
    }

    /// Does what a shortcut asks for, whichever part of the app owns it.
    public void Perform(ShortcutAction action)
    {
        if (!Interview.Perform(action))
        {
            switch (action)
            {
                case ShortcutAction.ToggleSearch:
                    Notes.ToggleSearch();
                    break;
                case ShortcutAction.PracticePrimaryAction:
                    if (IsPracticeAvailable) Practice.PerformPrimaryAction();
                    break;
            }
        }
        PublishState();
    }

    private void PublishState()
    {
        var state = ShortcutState;
        if (wasInterviewActive && !state.InterviewActive)
        {
            keys?.ResetPushToTalk();
            localRouter.Reset();
        }
        wasInterviewActive = state.InterviewActive;
        if (keys is not null) keys.State = state;
    }

    /// The keys the window hears while it has the keyboard: the menu shortcuts of the macOS app, and
    /// every shortcut when there is no system-wide hook.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Notes.IsSearchVisible)
        {
            Notes.ToggleSearch();
            e.Handled = true;
            return;
        }
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (keys is null && Route(e.Key, isDown: true, control, shift, alt))
        {
            e.Handled = true;
            return;
        }
        if (!control || shift || alt) return;

        const double step = 20;
        var typing = window.FocusManager?.GetFocusedElement() is TextBox;
        switch (e.Key)
        {
            case Key.OemComma: Settings.Show(); break;
            case Key.N: Notes.NewNote(); break;
            case Key.E: _ = Notes.ExportNotesAsync(); break;
            case Key.T: Notes.ShowTemplates(); break;
            case Key.OemBackslash or Key.OemPipe: floating.Dismiss(); break;
            case Key.M: Interview.HideWindow(animated: false); break;
            case Key.Q: _ = QuitAsync(); break;
            // In a text field these keys move the caret by a word, so the window stays put.
            case Key.Left when !typing: Shell.NudgeWindow(-step, 0); break;
            case Key.Right when !typing: Shell.NudgeWindow(step, 0); break;
            case Key.Up when !typing: Shell.NudgeWindow(0, -step); break;
            case Key.Down when !typing: Shell.NudgeWindow(0, step); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (keys is not null) return;
        var modifiers = e.KeyModifiers;
        Route(e.Key, isDown: false, modifiers.HasFlag(KeyModifiers.Control), modifiers.HasFlag(KeyModifiers.Shift), modifiers.HasFlag(KeyModifiers.Alt));
    }

    private bool Route(Key key, bool isDown, bool control, bool shift, bool alt)
    {
        var mapped = Map(key);
        if (mapped == ShortcutKey.Other) return false;
        var decision = localRouter.Handle(new KeyStroke(mapped, isDown, control, shift, alt), ShortcutState);
        if (decision.Action != ShortcutAction.None) Perform(decision.Action);
        return decision.Consume;
    }

    private static ShortcutKey Map(Key key) => key switch
    {
        Key.A => ShortcutKey.A,
        Key.B => ShortcutKey.B,
        Key.C => ShortcutKey.C,
        Key.F => ShortcutKey.F,
        Key.G => ShortcutKey.G,
        Key.I => ShortcutKey.I,
        Key.M => ShortcutKey.M,
        Key.S => ShortcutKey.S,
        Key.D1 => ShortcutKey.Digit1,
        Key.D2 => ShortcutKey.Digit2,
        Key.D3 => ShortcutKey.Digit3,
        Key.D4 => ShortcutKey.Digit4,
        Key.Enter => ShortcutKey.Enter,
        Key.Left => ShortcutKey.Left,
        Key.Right => ShortcutKey.Right,
        Key.Down => ShortcutKey.Down,
        Key.Up => ShortcutKey.Up,
        _ => ShortcutKey.Other
    };

    // MARK: - Closing and quitting

    /// Closing the window hides it, once Practice has had its say about an unfinished session.
    public async Task RequestCloseAsync()
    {
        if (!await Practice.ShouldAllowWindowCloseAsync()) return;
        Interview.HideWindow(animated: false);
    }

    /// True when the app may end now. An unfinished Practice session is asked about first.
    public async Task<bool> MayQuitAsync() =>
        !Practice.HasActiveSession || await Practice.ShouldAllowWindowCloseAsync();

    public async Task QuitAsync()
    {
        if (quitting || !await MayQuitAsync()) return;
        Shutdown();
        exit();
    }

    /// Lets go of the devices and the keyboard. The process is about to end.
    public void Shutdown()
    {
        if (quitting) return;
        quitting = true;
        audio.Terminate();
        Practice.ShutdownMicrophone();
        Interview.Shutdown();
        keys?.Dispose();
        floating.Dismiss();
        Shell.HideScreenshotAlert();
        foreach (var panel in Shell.Panels.ToList()) panel.Close();
    }

    // MARK: - The tray menu

    private List<MenuEntry> BuildMenu()
    {
        var models = new MenuEntry("Answer Model");
        foreach (var option in AnswerModelOption.All)
        {
            var entry = new MenuEntry(option.Title, () => _ = SelectAnswerModelAsync(option)) { Checked = false };
            modelEntries.Add((option, entry));
            models.Children.Add(entry);
        }
        return new List<MenuEntry>
        {
            new("Show Window", () => Interview.ShowWindow(animated: false)),
            new("Hide Window", () => Interview.HideWindow(animated: false)),
            MenuEntry.Separator(),
            new("Capture Screenshot", () => Perform(ShortcutAction.CaptureScreenshot)),
            new("Analyze Screenshots", () => Perform(ShortcutAction.AnalyzeScreenshots)),
            new("Start / End Interview", Interview.ToggleInterview),
            microphoneEntry,
            models,
            new("Background Transparency…", ShowTransparency),
            new("Settings...", Settings.Show),
            new("Keyboard Shortcuts", () => _ = ShowKeyboardShortcutsAsync()),
            MenuEntry.Separator(),
            // The macOS app has these in its menu bar; a window without one keeps them here.
            new("New Note", Notes.NewNote),
            new("Export Notes...", () => _ = Notes.ExportNotesAsync()),
            new("Interview Templates...", Notes.ShowTemplates),
            new("Hide Floating Solution", floating.Dismiss),
            MenuEntry.Separator(),
            new("About Interview Master", () => _ = ShowAboutAsync()),
            new("Interview Master Help", () => Shell.OpenLink(HelpLink)),
            new("Privacy Policy", () => Shell.OpenLink(PrivacyLink)),
            MenuEntry.Separator(),
            new("Quit Interview Master", () => _ = QuitAsync()),
        };
    }

    private void SyncMenu()
    {
        microphoneEntry.Checked = audio.SelectedDevice == CaptureDevice.Microphone;
        microphoneEntry.Enabled = !audio.IsSwitching;
        var current = AnswerModelOption.Current;
        foreach (var (option, entry) in modelEntries) entry.Checked = option == current;
        MenuChanged?.Invoke();
    }

    public async Task SelectAnswerModelAsync(AnswerModelOption option)
    {
        if (option.Model == AnswerModel.Gpt && string.IsNullOrEmpty(ApiKeyManager.Shared.GetKey(ApiKeyType.OpenAI)))
        {
            await Shell.AskAsync(AppQuestion.Notice("OpenAI key needed", PlatformCopy.OpenAIKeyNeeded));
            return;
        }
        var settings = AppSettings.Shared;
        settings.AnswerModel = option.Model;
        if (option.ReasoningEffort is { } effort) settings.OpenAIReasoningEffort = effort;
        settings.OpenAIPriority = option.Priority;
        Interview.AnswerModelChanged();
        SyncMenu();
    }

    /// The slider that the macOS status menu holds in a row of its own.
    public void ShowTransparency()
    {
        transparencyPanel?.Close();
        var view = new TransparencyPanelView(AppSettings.Shared.BackgroundTransparency);
        view.Changed += value =>
        {
            AppSettings.Shared.BackgroundTransparency = value;
            Interview.ApplyTransparency();
        };
        transparencyPanel = Shell.ShowPanel("Background Transparency", view, TransparencyPanelView.PanelWidth, TransparencyPanelView.PanelHeight);
    }

    public async Task ShowAboutAsync()
    {
        var choice = await Shell.AskAsync(new AppQuestion("Interview Master", AboutText, new[] { "OK", "Privacy Policy" }, Dismiss: 0));
        if (choice == 1) Shell.OpenLink(PrivacyLink);
    }

    public Task ShowKeyboardShortcutsAsync() =>
        Shell.AskAsync(AppQuestion.Notice("Keyboard Shortcuts", PlatformCopy.KeyboardShortcutsHelp));

    /// A click on the tray icon itself brings the window back, or puts it away.
    public void ToggleFromTray()
    {
        if (Shell.IsWindowVisible) Interview.HideWindow(animated: false);
        else Interview.ShowWindow(animated: false);
    }
}
