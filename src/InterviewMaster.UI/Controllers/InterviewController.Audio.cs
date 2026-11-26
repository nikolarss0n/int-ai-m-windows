using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

public sealed partial class InterviewController
{
    /// Whether Windows lets the app use a source. The app supplies the real check.
    public Func<CaptureDevice, bool> HasAudioPermission { get; set; } = _ => true;

    /// The source to start on when `IM_CAPTURE_DEVICE` names one, as the test scripts do.
    public Func<string, string?> Environment { get; set; } = System.Environment.GetEnvironmentVariable;

    private void WireAudio()
    {
        Audio.OnResetBuffers = () => Processor.ResetCaptureBuffers();
        Audio.OnStateChange = () =>
        {
            if (Audio.ActiveDevice is null || IsSelfSpeechMuted) isSpeaking = false;
            Refresh();
        };
        Audio.OnLevel = (db, speaking) =>
        {
            UpdateSpeaking(speaking && !IsSelfSpeechMuted && Audio.ActiveDevice is not null);
            Focus.Header.Capsule.SetLevel(db);
            view.Bar.Capsule.SetLevel(db);
        };
        Audio.OnPreview = (data, device) => Processor.PrefetchTranscription(data, device);
        Audio.OnCancelled = () => Processor.CancelPrefetchTranscription();
        Audio.OnSegment = (data, device) => Processor.ProcessAudioSegment(data, device);
        Audio.OnStatus = status => DebugLogger.Shared.Log(LogCategory.Audio, status);
        Audio.OnCommitted = (device, explicitChoice) =>
        {
            if (explicitChoice) AppSettings.Shared.CaptureDevice = device;
            if (!IsInterviewActive) return;
            DebugLogger.Shared.Log(LogCategory.Audio, $"AUDIO_SOURCE: {device.RawValue()}");
            AddMessage(MessageType.Status, $"Audio source: {device.DisplayName()}", null);
        };
        Audio.OnProblem = (device, error) => _ = ReportAudioProblemAsync(device, error);
    }

    private async Task ReportAudioProblemAsync(CaptureDevice device, Exception error)
    {
        var microphoneDenied = device == CaptureDevice.Microphone && !HasAudioPermission(device);
        var buttons = microphoneDenied ? new[] { "Open Settings", "Cancel" } : new[] { "OK" };
        var choice = await shell.AskAsync(new AppQuestion($"{device.DisplayName()} unavailable", error.Message, buttons));
        if (microphoneDenied && choice == 0) shell.OpenLink(PlatformCopy.MicrophoneSettingsLink);
    }

    private void UpdateSpeaking(bool speaking)
    {
        if (isSpeaking == speaking) return;
        isSpeaking = speaking;
        SyncAudioControls();
        if (!Questions.IsAIWorking) RefreshActivity();
    }

    private CaptureDevice StartupDevice() => AudioSourceSelection.ResolveStartupCaptureDevice(
        AppSettings.Shared.CaptureDevice, HasAudioPermission(CaptureDevice.Microphone));

    public void BeginAudioSession() =>
        Audio.StartSession(CaptureDevices.FromRawValue(Environment("IM_CAPTURE_DEVICE")) ?? StartupDevice());

    public void EndAudioSession()
    {
        Audio.StopSession();
        pushToTalk = new PushToTalkState();
        pushToTalkWatchdog?.Dispose();
        pushToTalkWatchdog = null;
        isSpeaking = false;
        SyncAudioControls();
    }

    public void ToggleMicrophoneSource() => _ = SelectAudioSourceAsync(
        Audio.SelectedDevice == CaptureDevice.Microphone ? CaptureDevice.SystemAudio : CaptureDevice.Microphone);

    /// The microphone hears the person too, so the first switch to it says what that means.
    public async Task SelectAudioSourceAsync(CaptureDevice device)
    {
        if (Audio.IsSwitching)
        {
            SyncAudioControls();
            return;
        }
        if (device == CaptureDevice.Microphone && !SettingsStore.Standard.GetBool(MicrophoneDisclosureKey))
        {
            var question = new AppQuestion(
                "Use the microphone for the interviewer", PlatformCopy.MicrophoneDisclosure, new[] { "Use Microphone", "Cancel" });
            if (await shell.AskAsync(question) != 0)
            {
                SyncAudioControls();
                return;
            }
            SettingsStore.Standard.Set(MicrophoneDisclosureKey, true);
        }
        Audio.Select(device);
        SyncAudioControls();
    }

    public void ToggleAudioMute()
    {
        if (!IsInterviewActive) return;
        pushToTalk.ToggleLatch();
        pushToTalkWatchdog?.Dispose();
        pushToTalkWatchdog = null;
        Audio.SetMuted(pushToTalk.IsMuted);
    }

    private static double Uptime => System.Environment.TickCount64 / 1000.0;

    /// The mute key went down: muted for as long as it is held. A release that never arrives would
    /// leave the app deaf, so the hold gives up after two minutes.
    public void PushToTalkDown(bool isRepeat)
    {
        if (!IsInterviewActive) return;
        if (pushToTalk.KeyDown(Uptime, isRepeat) is not { } id) return;
        pushToTalkWatchdog?.Dispose();
        pushToTalkWatchdog = Delayed.After(120, () =>
        {
            if (!pushToTalk.Expire(id)) return;
            Audio.SetMuted(pushToTalk.IsMuted);
            AddMessage(MessageType.Status, "Mute released after a missed key release.", null);
        });
        Audio.SetMuted(pushToTalk.IsMuted);
    }

    public void PushToTalkUp()
    {
        if (!pushToTalk.KeyUp(Uptime)) return;
        pushToTalkWatchdog?.Dispose();
        pushToTalkWatchdog = null;
        Audio.SetMuted(pushToTalk.IsMuted);
    }

    // MARK: - Shortcuts

    /// What the keyboard hook needs to decide whether a key is one of the app's.
    public ShortcutState ShortcutState(bool modalOpen = false, bool practiceInteractionEnabled = false) => new(
        WindowVisible: shell.IsWindowVisible,
        InterviewActive: IsInterviewActive,
        ModalOpen: modalOpen,
        CurrentTab: view.CurrentTab,
        PracticeInteractionEnabled: practiceInteractionEnabled,
        CodeOpen: CodeOpen,
        CanStepCode: CanStepCode);

    /// Does what a shortcut asks for. Returns false for the ones another part of the app owns.
    public bool Perform(ShortcutAction action)
    {
        const double step = 40;
        switch (action)
        {
            case ShortcutAction.PushToTalkDown: PushToTalkDown(isRepeat: false); break;
            case ShortcutAction.PushToTalkRepeat: PushToTalkDown(isRepeat: true); break;
            case ShortcutAction.PushToTalkUp: PushToTalkUp(); break;
            case ShortcutAction.NudgeWindowLeft: shell.NudgeWindow(-step, 0); break;
            case ShortcutAction.NudgeWindowRight: shell.NudgeWindow(step, 0); break;
            case ShortcutAction.NudgeWindowDown: shell.NudgeWindow(0, step); break;
            case ShortcutAction.NudgeWindowUp: shell.NudgeWindow(0, -step); break;
            case ShortcutAction.NewerQuestion: MoveQuestionSelection(newer: true); break;
            case ShortcutAction.OlderQuestion: MoveQuestionSelection(newer: false); break;
            case ShortcutAction.CodeStepForward: Focus.CodePanel.MoveStep(forward: true); break;
            case ShortcutAction.CodeStepBack: Focus.CodePanel.MoveStep(forward: false); break;
            case ShortcutAction.CopyCode: CopyCode(); break;
            case ShortcutAction.ToggleAnswerOverCode: ToggleAnswerOverCode(); break;
            case ShortcutAction.ToggleClickThrough: ToggleClickThrough(); break;
            case ShortcutAction.ToggleWindow: ToggleWindowVisibility(); break;
            case ShortcutAction.CaptureScreenshot:
                if (view.CurrentTab != Tab.Voice) SwitchTab(Tab.Voice);
                _ = CaptureScreenshotAsync();
                break;
            case ShortcutAction.AnalyzeScreenshots:
                if (shell.IsWindowVisible && view.CurrentTab != Tab.Voice) SwitchTab(Tab.Voice);
                _ = AnalyzeScreenshotsAsync();
                break;
            case ShortcutAction.Clear:
                if (view.CurrentTab != Tab.Voice) return false;
                ClearScreenshots();
                break;
            case ShortcutAction.ShowNotes: SwitchTab(Tab.Notes); break;
            case ShortcutAction.ShowVoice: SwitchTab(Tab.Voice); break;
            case ShortcutAction.ToggleCodePanel:
                if (view.CurrentTab != Tab.Voice) SwitchTab(Tab.Voice);
                ToggleCodePanel();
                break;
            case ShortcutAction.ShowPractice: SwitchTab(Tab.Practice); break;
            default: return false;
        }
        return true;
    }

    // MARK: - The window

    /// Clicks go to the app behind while this is on.
    public void ToggleClickThrough()
    {
        IsClickThrough = !IsClickThrough;
        shell.SetClickThrough(IsClickThrough);
        if (IsClickThrough) PostNotice(FeedbackCopy.ClickThroughOn);
        else ResolveNotice(FeedbackCopy.ClickThroughOn.Key);
    }

    public void ToggleWindowVisibility()
    {
        if (shell.IsWindowVisible) HideWindow(animated: true);
        else ShowWindow(animated: true);
    }

    public void ShowWindow(bool animated)
    {
        if (IsClickThrough)
        {
            IsClickThrough = false;
            shell.SetClickThrough(false);
        }
        shell.HideScreenshotAlert();
        shell.ShowWindow(animated);
        SyncCodePanelWithPinnedSolution();
        WindowShown?.Invoke();
    }

    public void HideWindow(bool animated)
    {
        WindowHiding?.Invoke();
        shell.HideWindow(animated);
        if (!IsClickThrough) return;
        IsClickThrough = false;
        shell.SetClickThrough(false);
    }

    /// Practice starts and stops with the window, so it is told when the window comes and goes.
    public event Action? WindowShown;

    public event Action? WindowHiding;
}
