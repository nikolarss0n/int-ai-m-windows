using Avalonia.Threading;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// The interview window's behaviour: what the processor reports goes into the question list, the
/// answer, the code panel and the notices, and what the person presses goes back to the processor.
public sealed partial class InterviewController : IVoiceInterviewProcessorDelegate
{
    public const string AppName = "Interview Master";
    private const double StreamingUpdateInterval = 0.06;
    private const string DataConsentKey = "InterviewMaster.DataConsentGiven";
    private const string MicrophoneDisclosureKey = "InterviewMaster.MicrophoneDisclosureShown";

    private readonly MainView view;
    private readonly IAppShell shell;
    private readonly IMainThread mainThread;
    private readonly IScreenCapture? screenCapture;
    private readonly List<Screenshot> screenshots = new();
    private readonly List<InterviewMessage> voiceMessages = new();
    private readonly NoticeBoard notices = new();
    private readonly Dictionary<string, Action?> noticeActions = new();
    private readonly DelayedActivity transcription = new(Token.Motion.LoadingDelay, 20);
    private readonly Dictionary<Guid, string> pendingStreamingContent = new();
    private readonly Dictionary<Guid, Delayed> streamingFlushes = new();
    private readonly Dictionary<Guid, DateTime> lastStreamingUpdates = new();
    private Delayed? transcriptionRefresh;
    private Delayed? readyReset;
    private Delayed? noticeExpiry;
    private Delayed? pushToTalkWatchdog;
    private DispatcherTimer? recordingTimer;
    private PushToTalkState pushToTalk = new();
    private bool answerReadyVisible;
    private bool codePanelDismissed;
    private bool codingTaskRecorded;
    private bool isSpeaking;
    private GroqInterviewClient? groqClient;
    private AnthropicClient? anthropicClient;
    private DateTime? interviewStartedAt;

    public InterviewController(
        MainView view, IAppShell shell, IMainThread mainThread, AudioCaptureCoordinator audio,
        VoiceInterviewProcessor processor, IScreenCapture? screenCapture = null)
    {
        this.view = view;
        this.shell = shell;
        this.mainThread = mainThread;
        this.screenCapture = screenCapture;
        Audio = audio;
        Processor = processor;
        processor.Delegate = this;
        WireViews();
        WireAudio();
        view.Bar.SetNavigation(view.CurrentTab, false);
        ApplyTransparency();
        Refresh();
    }

    /// The clock everything here reads, so a scenario can be replayed at a fixed time.
    public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public VoiceInterviewProcessor Processor { get; }

    public AudioCaptureCoordinator Audio { get; }

    public QuestionBurstState Questions { get; } = new();

    public ConversationContext ConversationContext { get; } = new();

    public IReadOnlyList<InterviewMessage> Messages => voiceMessages;

    public IReadOnlyList<Screenshot> Screenshots => screenshots;

    public bool IsInterviewActive { get; internal set; }

    /// When the timer in the header started.
    public DateTime? RecordingStartTime { get; set; }

    public string? PinnedSolution { get; internal set; }

    public bool IsClickThrough { get; private set; }

    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Smart;

    /// Replaces what the audio controls show, for a state that needs a live device to reach.
    public CapturePresentation? AudioPresentationOverride { get; set; }

    public FeedbackNotice? CurrentNotice => notices.Current;

    public string UserBackground => ResumeStore.Shared.ExtractedText;

    public Tab CurrentTab => view.CurrentTab;

    private InterviewFocusView Focus => view.Interview;

    private bool CodeOpen => Focus.CodeOpen;

    private bool AnswerOverCode => Focus.AnswerOverCode;

    public bool IsSelfSpeechMuted => pushToTalk.IsMuted;

    public bool CanStepCode => CodeOpen && !AnswerOverCode && Focus.CodePanel.CanStep;

    public static InterviewLLMProvider LlmProvider
    {
        get
        {
            var keys = ApiKeyManager.Shared;
            if (AppSettings.Shared.AnswerModel == AnswerModel.Gpt && !string.IsNullOrEmpty(keys.GetKey(ApiKeyType.OpenAI))) return InterviewLLMProvider.OpenAI;
            return string.IsNullOrEmpty(keys.GetKey(ApiKeyType.Xai)) ? InterviewLLMProvider.Anthropic : InterviewLLMProvider.Xai;
        }
    }

    /// The key that writes answers and reads screenshots: Grok first, Anthropic when there is no Grok key.
    public static string? ModelApiKey
    {
        get
        {
            var keys = ApiKeyManager.Shared;
            if (LlmProvider == InterviewLLMProvider.OpenAI) return keys.GetKey(ApiKeyType.OpenAI);
            var xai = keys.GetKey(ApiKeyType.Xai);
            return string.IsNullOrEmpty(xai) ? keys.GetKey(ApiKeyType.Anthropic) : xai;
        }
    }

    private void WireViews()
    {
        var bar = view.Bar;
        bar.ContextButton.Click += () => SwitchTab(Tab.Notes);
        bar.TimelineButton.Click += () => SwitchTab(Tab.Voice);
        bar.PracticeButton.Click += () => SwitchTab(Tab.Practice);
        bar.CodeButton.Click += ToggleCodePanel;
        bar.InterviewButton.Click += ToggleInterview;
        bar.SystemAudioButton.Click += () => _ = SelectAudioSourceAsync(CaptureDevice.SystemAudio);
        bar.MicrophoneButton.Click += () => _ = SelectAudioSourceAsync(CaptureDevice.Microphone);
        bar.MuteButton.Click += ToggleAudioMute;
        bar.ExportButton.Click += () => _ = ExportInterviewAsync();
        bar.SettingsButton.Click += shell.ShowSettings;

        Focus.Header.Notice.Action += RunNoticeAction;
        Focus.Header.Notice.Dismiss += DismissNotice;
        Focus.Header.CloseRequested += RequestClose;
        Focus.Strip.Selected += SelectQuestion;
        Focus.Answer.Retry += RetryAnswer;
        Focus.TalkStrip.Toggle += ToggleAnswerOverCode;

        var panel = Focus.CodePanel;
        panel.Close += () => HideCodePanel(byUser: true);
        panel.QuickAction += instruction => Processor.RequestCodeChange(instruction);
        panel.RetryRequested += () => Processor.RetryLastCodingRequest();
        panel.OriginClicked += () =>
        {
            if (panel.Displayed is not { } version) return;
            SelectQuestion(version.Id);
            ShowAnswerOverCode(true);
        };
        panel.Rendered += RefreshCodeOrigin;
        ResumeStore.Shared.Changed += () => mainThread.Post(Refresh);
    }

    // MARK: - Tabs

    /// Called when the window shows another place, so the app can start and stop what lives there.
    public event Action<Tab, Tab>? TabChanged;

    public void SwitchTab(Tab tab)
    {
        if (view.CurrentTab == tab) return;
        var previous = view.CurrentTab;
        view.ShowTab(tab);
        view.Bar.SetNavigation(tab, IsInterviewActive && tab == Tab.Voice);
        TabChanged?.Invoke(previous, tab);
        Refresh();
    }

    public void ApplyTransparency()
    {
        view.ApplyTransparency(AppSettings.Shared.BackgroundTransparency);
        Refresh();
    }

    // MARK: - Refresh

    /// Puts the whole live view in step with the state: the question list, the answer, the code's
    /// origin, the title row, the status and the audio controls.
    public void Refresh()
    {
        var visible = Questions.DisplayedEntries;
        var selected = Questions.SelectedEntry;
        var asked = Questions.Entries.Count;
        var numbers = new Dictionary<Guid, int>();
        foreach (var entry in visible)
        {
            if (Questions.QuestionNumber(entry.Id) is { } number) numbers[entry.Id] = number;
        }
        var codeIds = Focus.CodePanel.Versions.Select(version => version.Id).ToHashSet();
        Focus.Strip.Update(visible, selected?.Id, numbers, codeIds);
        Ui.Tip(Focus.Strip, asked > visible.Count ? $"{asked} questions. Ctrl+Alt+↑ and Ctrl+Alt+↓ move between them." : null);
        Focus.DisplayedQuestions = visible.Count;

        if (selected is not null && Questions.QuestionNumber(selected.Id) is { } position)
        {
            Focus.Answer.Update(
                selected, position, asked,
                selected.Answer.Length == 0 ? null : FormatAnswer(selected.Answer),
                streaming: selected.Phase == QuestionBurstPhase.Answering && Questions.ActiveAIWork.Contains(selected.Id),
                canRetry: IsInterviewActive,
                listShowsPhase: visible.Count > 1 && selected.Phase.ShowsPill() && Focus.Strip.ShowsPhaseWords);
        }
        else
        {
            Focus.Answer.ShowWelcome();
        }

        RefreshCodeOrigin();
        RefreshTalkStrip();
        Focus.CodePanel.IsLive = IsInterviewActive;

        var settings = AppSettings.Shared;
        var parts = new List<string> { settings.Role.DisplayName(), settings.ProgrammingLanguage.DisplayName(), settings.ResponseLanguage.DisplayName() };
        if (ResumeStore.Shared.HasResume) parts.Add("CV");
        var shots = ScreenshotPresentation.LiveScreenshotStatusText(screenshots.Count);
        if (shots.Length > 0) parts.Add(shots);
        var profile = string.Join(Token.Text.Separator, parts);
        Focus.Header.SetTitle(profile, AppName + Token.Text.Separator + profile);
        Focus.Header.ReservedTrailingWidth = IsInterviewActive ? Token.Header.CapsuleWidth : 0;

        RefreshActivity();
        view.Bar.SetInterviewActive(IsInterviewActive);
        SyncAudioControls();
        UpdateCodeButton();
        Focus.RefreshBody();
        Refreshed?.Invoke();
    }

    /// The answer as paragraphs, with the model's arrow bullets shown as dots.
    private static IReadOnlyList<AnswerParagraph> FormatAnswer(string answer) =>
        AnswerFormatter.Format(answer.Replace("▸", "•"));

    private string ElapsedText()
    {
        if (!IsInterviewActive || RecordingStartTime is not { } start) return "";
        var elapsed = Math.Max(0, (int)(Now() - start).TotalSeconds);
        return $"{elapsed / 60:00}:{elapsed % 60:00}";
    }

    public CapturePresentation AudioPresentation =>
        AudioPresentationOverride ?? AudioSourceSelection.CapturePresentation(
            Audio.ActiveDevice, IsInterviewActive, Audio.IsSwitching, IsSelfSpeechMuted, isSpeaking);

    public void RefreshActivity()
    {
        var mode = CurrentActivityMode();
        Focus.Header.Capsule.Update(
            mode.Kind == ActivityKind.Ready ? AudioActivityMode() : mode,
            isRecording: Audio.ActiveDevice is not null && !IsSelfSpeechMuted,
            elapsed: ElapsedText());
        view.Bar.Capsule.Update(Questions.IsAIWorking ? ActivityMode.Working(null) : mode, isRecording: false, elapsed: "");
        Focus.Header.Relayout();
    }

    private ActivityMode CurrentActivityMode()
    {
        if (Questions.IsAIWorking) return ActivityMode.Working(Questions.QuestionNumber(Questions.LatestActiveWorkId));
        if (transcription.IsShowing(Now())) return ActivityMode.Transcribing;
        if (AudioPresentation.Kind == CapturePresentationKind.Switching) return ActivityMode.Connecting;
        return answerReadyVisible ? ActivityMode.Ready : AudioActivityMode();
    }

    private ActivityMode AudioActivityMode() => AudioPresentation.Kind switch
    {
        CapturePresentationKind.Listening => ActivityMode.Listening(false),
        CapturePresentationKind.Speaking => ActivityMode.Listening(true),
        CapturePresentationKind.Muted => ActivityMode.Muted,
        CapturePresentationKind.Unavailable => ActivityMode.Unavailable,
        _ => ActivityMode.Idle,
    };

    private void SyncAudioControls()
    {
        view.Bar.SetAudio(
            Audio.SelectedDevice, Audio.IsSwitching, AudioPresentation, IsSelfSpeechMuted,
            IsInterviewActive, Audio.ActiveDevice is not null);
        view.Bar.SetNavigation(view.CurrentTab, IsInterviewActive && view.CurrentTab == Tab.Voice);
        AudioControlsChanged?.Invoke();
    }

    /// The tray menu mirrors the audio source, so it is told when the controls change.
    public event Action? AudioControlsChanged;

    /// Raised after every refresh, so the keyboard hook can be told what the app is doing now.
    public event Action? Refreshed;

    /// Closing the window hides it. The app asks Practice first, as `windowShouldClose` does.
    public Func<Task>? CloseWindow { get; set; }

    private void RequestClose()
    {
        if (CloseWindow is { } close) _ = close();
        else HideWindow(animated: false);
    }

    private void UpdateCodeButton() =>
        view.Bar.SetCode(Focus.CodePanel.Versions.Count > 0, CodeOpen && !AnswerOverCode);

    /// Puts screenshots in place without capturing them, for a state the driver wants to show.
    internal void ReplaceScreenshots(IEnumerable<Screenshot> next)
    {
        screenshots.Clear();
        screenshots.AddRange(next);
    }

    private static void DebugLog(string message) => DebugLogger.Shared.Log(LogCategory.Delegate, message);
}
