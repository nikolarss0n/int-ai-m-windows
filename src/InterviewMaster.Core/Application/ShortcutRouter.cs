using InterviewMaster.Domain;

namespace InterviewMaster.AppServices;

/// The keys the app's shortcuts are made of. Anything else is `Other` and always passes through.
public enum ShortcutKey
{
    Other,
    A,
    B,
    C,
    F,
    G,
    I,
    M,
    S,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Enter,
    Left,
    Right,
    Down,
    Up,
    J,
    K,
    Backslash
}

/// One key press or release as the system-wide keyboard hook sees it. `Control` stands where ⌘ does
/// on macOS and `Alt` where ⌥ does. `RightAlt` is AltGr on many keyboards, where it types letters.
public readonly record struct KeyStroke(
    ShortcutKey Key, bool IsDown, bool Control, bool Shift, bool Alt, bool IsRepeat = false, bool RightAlt = false);

/// What the router needs to know about the app. The UI thread publishes a new one whenever any of it
/// changes; the hook thread reads the latest.
public sealed record ShortcutState(
    bool WindowVisible = false,
    bool InterviewActive = false,
    bool ModalOpen = false,
    Tab CurrentTab = Tab.Voice,
    bool PracticeInteractionEnabled = false,
    bool CodeOpen = false,
    bool CanStepCode = false);

public enum ShortcutAction
{
    None,
    PushToTalkDown,
    PushToTalkRepeat,
    PushToTalkUp,
    PracticePrimaryAction,
    NudgeWindowLeft,
    NudgeWindowRight,
    NudgeWindowDown,
    NudgeWindowUp,
    NewerQuestion,
    OlderQuestion,
    CodeStepForward,
    CodeStepBack,
    CopyCode,
    ToggleAnswerOverCode,
    ToggleClickThrough,
    ToggleWindow,
    CaptureScreenshot,
    AnalyzeScreenshots,
    Clear,
    ShowNotes,
    ShowVoice,
    ToggleCodePanel,
    ShowPractice,
    ToggleSearch
}

/// `Consume` keeps the key from the app in front; `Action` is what this app does about it.
public readonly record struct ShortcutDecision(bool Consume, ShortcutAction Action = ShortcutAction.None)
{
    public static readonly ShortcutDecision PassThrough = new(false);
    public static readonly ShortcutDecision Swallow = new(true);

    public static ShortcutDecision Do(ShortcutAction action) => new(true, action);
}

/// Decides, for every key pressed anywhere, whether it is one of the app's shortcuts. This is
/// `handleGlobalKeyEvent` from `HotkeyManager.swift`, in the same order, with ⌘ as Ctrl, ⌥ as Alt and
/// ⌘⌃ as Ctrl+Alt+Shift.
///
/// Two differences from macOS, both because AltGr is Ctrl+Alt on Windows: a stroke made with the
/// right Alt key is never a shortcut, and the plain Ctrl shortcuts do not fire while Alt is held.
public sealed class ShortcutRouter
{
    private bool pushToTalkHeld;

    public ShortcutDecision Handle(KeyStroke stroke, ShortcutState state)
    {
        if (stroke.Key == ShortcutKey.M && PushToTalk(stroke, state) is { } decision) return decision;
        if (!stroke.IsDown || stroke.Key == ShortcutKey.Other) return ShortcutDecision.PassThrough;

        // Only Ctrl combinations are shortcuts.
        if (!stroke.Control || stroke.RightAlt) return ShortcutDecision.PassThrough;

        if (state.ModalOpen && IsApplicationHotkey(stroke.Key)) return ShortcutDecision.Swallow;

        var practiceReserved = state.CurrentTab == Tab.Practice && state.WindowVisible && state.PracticeInteractionEnabled;
        // Practice must retain keyboard ownership. These commands otherwise switch to Timeline or
        // Coding and interrupt the learner's response.
        if (practiceReserved && !stroke.Shift)
        {
            if (stroke.IsRepeat) return ShortcutDecision.Swallow;
            switch (stroke.Key)
            {
                case ShortcutKey.Enter:
                    return state.ModalOpen ? ShortcutDecision.Swallow : ShortcutDecision.Do(ShortcutAction.PracticePrimaryAction);
                case ShortcutKey.S or ShortcutKey.G:
                    return ShortcutDecision.Swallow;
            }
        }

        if (stroke.Alt)
        {
            // Ctrl+Alt+Shift+arrows move the window from any app: the window never takes the keyboard,
            // so a shortcut that needs it focused would never fire. Holding the key keeps it moving.
            if (stroke.Shift)
            {
                if (!state.WindowVisible) return ShortcutDecision.PassThrough;
                return stroke.Key switch
                {
                    ShortcutKey.Left => ShortcutDecision.Do(ShortcutAction.NudgeWindowLeft),
                    ShortcutKey.Right => ShortcutDecision.Do(ShortcutAction.NudgeWindowRight),
                    ShortcutKey.Down => ShortcutDecision.Do(ShortcutAction.NudgeWindowDown),
                    ShortcutKey.Up => ShortcutDecision.Do(ShortcutAction.NudgeWindowUp),
                    _ => ShortcutDecision.PassThrough
                };
            }

            // Only during a visible interview: at any other time these keys belong to the app in front.
            if (!state.InterviewActive || !state.WindowVisible) return ShortcutDecision.PassThrough;
            return stroke.Key switch
            {
                ShortcutKey.Up => ShortcutDecision.Do(ShortcutAction.NewerQuestion),
                ShortcutKey.Down => ShortcutDecision.Do(ShortcutAction.OlderQuestion),
                // Only while code in several steps is up; otherwise the editor keeps its own keys.
                ShortcutKey.Right when state.CanStepCode => ShortcutDecision.Do(ShortcutAction.CodeStepForward),
                ShortcutKey.Left when state.CanStepCode => ShortcutDecision.Do(ShortcutAction.CodeStepBack),
                ShortcutKey.C => ShortcutDecision.Do(ShortcutAction.CopyCode),
                ShortcutKey.A when state.CodeOpen => ShortcutDecision.Do(ShortcutAction.ToggleAnswerOverCode),
                _ => ShortcutDecision.PassThrough
            };
        }

        if (stroke.Shift) return ShortcutDecision.PassThrough;
        return stroke.Key switch
        {
            ShortcutKey.I => ShortcutDecision.Do(ShortcutAction.ToggleClickThrough),
            ShortcutKey.B => ShortcutDecision.Do(ShortcutAction.ToggleWindow),
            ShortcutKey.S => ShortcutDecision.Do(ShortcutAction.CaptureScreenshot),
            ShortcutKey.Enter => ShortcutDecision.Do(ShortcutAction.AnalyzeScreenshots),
            ShortcutKey.G when state.WindowVisible => ShortcutDecision.Do(ShortcutAction.Clear),
            ShortcutKey.Digit1 when state.WindowVisible => ShortcutDecision.Do(ShortcutAction.ShowNotes),
            ShortcutKey.Digit2 when state.WindowVisible => ShortcutDecision.Do(ShortcutAction.ShowVoice),
            ShortcutKey.Digit3 when state.WindowVisible => ShortcutDecision.Do(ShortcutAction.ToggleCodePanel),
            ShortcutKey.Digit4 when state.WindowVisible => ShortcutDecision.Do(ShortcutAction.ShowPractice),
            ShortcutKey.F when state.WindowVisible && state.CurrentTab == Tab.Notes => ShortcutDecision.Do(ShortcutAction.ToggleSearch),
            _ => ShortcutDecision.PassThrough
        };
    }

    /// Ctrl+Shift+M mutes while held. It is looked at before the modifier and modal checks so that
    /// letting go of Ctrl before M cannot leave capture muted.
    private ShortcutDecision? PushToTalk(KeyStroke stroke, ShortcutState state)
    {
        if (!stroke.IsDown)
        {
            if (!pushToTalkHeld) return null;
            pushToTalkHeld = false;
            return ShortcutDecision.Do(ShortcutAction.PushToTalkUp);
        }
        if (!state.InterviewActive || !stroke.Control || !stroke.Shift || stroke.Alt || state.ModalOpen) return null;
        if (stroke.IsRepeat || pushToTalkHeld) return ShortcutDecision.Do(ShortcutAction.PushToTalkRepeat);
        pushToTalkHeld = true;
        return ShortcutDecision.Do(ShortcutAction.PushToTalkDown);
    }

    /// A session that ends while the key is down forgets the press, as a fresh `PushToTalkState` does.
    public void Reset() => pushToTalkHeld = false;

    private static bool IsApplicationHotkey(ShortcutKey key) => key is
        ShortcutKey.S or ShortcutKey.F or ShortcutKey.G or ShortcutKey.B or ShortcutKey.I or ShortcutKey.Enter
        or ShortcutKey.Digit1 or ShortcutKey.Digit2 or ShortcutKey.Digit3 or ShortcutKey.Digit4
        or ShortcutKey.Left or ShortcutKey.Right or ShortcutKey.Down or ShortcutKey.Up;
}
