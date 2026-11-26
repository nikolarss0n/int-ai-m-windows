using InterviewMaster.AppServices;
using InterviewMaster.Domain;

namespace InterviewMaster.Tests;

/// The decisions of `handleGlobalKeyEvent` in HotkeyManager.swift, with ⌘ as Ctrl and ⌥ as Alt.
public sealed class ShortcutRouterTests : SwiftStyleTests
{
    private static readonly ShortcutState Hidden = new();
    private static readonly ShortcutState Visible = new(WindowVisible: true);
    private static readonly ShortcutState Live = new(WindowVisible: true, InterviewActive: true);

    private static KeyStroke Ctrl(ShortcutKey key, bool shift = false, bool alt = false, bool repeat = false, bool rightAlt = false) =>
        new(key, IsDown: true, Control: true, Shift: shift, Alt: alt, IsRepeat: repeat, RightAlt: rightAlt);

    private static ShortcutDecision Route(KeyStroke stroke, ShortcutState state) => new ShortcutRouter().Handle(stroke, state);

    [Fact]
    public void GlobalShortcutsWorkWhileTheWindowIsHidden()
    {
        Check(Route(Ctrl(ShortcutKey.B), Hidden) == ShortcutDecision.Do(ShortcutAction.ToggleWindow), "Ctrl+B shows a hidden window");
        Check(Route(Ctrl(ShortcutKey.I), Hidden) == ShortcutDecision.Do(ShortcutAction.ToggleClickThrough), "Ctrl+I toggles click-through");
        Check(Route(Ctrl(ShortcutKey.S), Hidden) == ShortcutDecision.Do(ShortcutAction.CaptureScreenshot), "Ctrl+S captures from any app");
        Check(Route(Ctrl(ShortcutKey.Enter), Hidden) == ShortcutDecision.Do(ShortcutAction.AnalyzeScreenshots), "Ctrl+Enter analyzes from any app");
    }

    [Fact]
    public void WindowShortcutsNeedAVisibleWindow()
    {
        foreach (var key in new[] { ShortcutKey.G, ShortcutKey.Digit1, ShortcutKey.Digit2, ShortcutKey.Digit3, ShortcutKey.Digit4, ShortcutKey.F })
        {
            Check(Route(Ctrl(key), Hidden) == ShortcutDecision.PassThrough, $"Ctrl+{key} belongs to the app in front while the window is hidden");
        }
        Check(Route(Ctrl(ShortcutKey.G), Visible).Action == ShortcutAction.Clear, "Ctrl+G clears");
        Check(Route(Ctrl(ShortcutKey.Digit1), Visible).Action == ShortcutAction.ShowNotes, "Ctrl+1 opens notes");
        Check(Route(Ctrl(ShortcutKey.Digit2), Visible).Action == ShortcutAction.ShowVoice, "Ctrl+2 opens the timeline");
        Check(Route(Ctrl(ShortcutKey.Digit3), Visible).Action == ShortcutAction.ToggleCodePanel, "Ctrl+3 toggles the code panel");
        Check(Route(Ctrl(ShortcutKey.Digit4), Visible).Action == ShortcutAction.ShowPractice, "Ctrl+4 opens practice");
        Check(Route(Ctrl(ShortcutKey.F), Visible) == ShortcutDecision.PassThrough, "Ctrl+F is find only on the notes tab");
        Check(Route(Ctrl(ShortcutKey.F), Visible with { CurrentTab = Tab.Notes }).Action == ShortcutAction.ToggleSearch, "Ctrl+F finds in notes");
    }

    [Fact]
    public void OtherKeysAndModifiersPassThrough()
    {
        Check(Route(new KeyStroke(ShortcutKey.S, true, Control: false, Shift: false, Alt: false), Visible) == ShortcutDecision.PassThrough, "a plain letter is typing");
        Check(Route(Ctrl(ShortcutKey.Other), Visible) == ShortcutDecision.PassThrough, "Ctrl+C, Ctrl+V and the rest are left alone");
        Check(Route(Ctrl(ShortcutKey.C), Visible) == ShortcutDecision.PassThrough, "Ctrl+C stays copy");
        Check(Route(Ctrl(ShortcutKey.A), Live) == ShortcutDecision.PassThrough, "Ctrl+A stays select all");
        Check(Route(Ctrl(ShortcutKey.S, shift: true), Visible) == ShortcutDecision.PassThrough, "Ctrl+Shift+S is another app's shortcut");
        Check(Route(Ctrl(ShortcutKey.S) with { IsDown = false }, Visible) == ShortcutDecision.PassThrough, "key releases are not shortcuts");
        Check(Route(Ctrl(ShortcutKey.S, alt: true), Visible) == ShortcutDecision.PassThrough, "Ctrl+Alt+S is left to the keyboard layout");
        Check(Route(Ctrl(ShortcutKey.A, alt: true, rightAlt: true), Live with { CodeOpen = true }) == ShortcutDecision.PassThrough, "AltGr letters are typing, never shortcuts");
    }

    [Fact]
    public void InterviewShortcutsOnlyDuringAVisibleInterview()
    {
        var code = Live with { CodeOpen = true, CanStepCode = true };
        Check(Route(Ctrl(ShortcutKey.Up, alt: true), Live).Action == ShortcutAction.NewerQuestion, "Ctrl+Alt+Up selects the newer question");
        Check(Route(Ctrl(ShortcutKey.Down, alt: true), Live).Action == ShortcutAction.OlderQuestion, "Ctrl+Alt+Down selects the older question");
        Check(Route(Ctrl(ShortcutKey.Right, alt: true), code).Action == ShortcutAction.CodeStepForward, "Ctrl+Alt+Right steps the code forward");
        Check(Route(Ctrl(ShortcutKey.Left, alt: true), code).Action == ShortcutAction.CodeStepBack, "Ctrl+Alt+Left steps the code back");
        Check(Route(Ctrl(ShortcutKey.Right, alt: true), Live) == ShortcutDecision.PassThrough, "without steps the arrows stay with the editor");
        Check(Route(Ctrl(ShortcutKey.C, alt: true), Live).Action == ShortcutAction.CopyCode, "Ctrl+Alt+C copies the code");
        Check(Route(Ctrl(ShortcutKey.A, alt: true), code).Action == ShortcutAction.ToggleAnswerOverCode, "Ctrl+Alt+A shows the answer over the code");
        Check(Route(Ctrl(ShortcutKey.A, alt: true), Live) == ShortcutDecision.PassThrough, "Ctrl+Alt+A needs the code panel");
        Check(Route(Ctrl(ShortcutKey.Up, alt: true), Visible) == ShortcutDecision.PassThrough, "outside an interview these keys belong to the app in front");
        Check(Route(Ctrl(ShortcutKey.Up, alt: true), Live with { WindowVisible = false }) == ShortcutDecision.PassThrough, "a hidden window takes no interview keys");
    }

    [Fact]
    public void WindowMovesFromAnyAppWhileVisible()
    {
        Check(Route(Ctrl(ShortcutKey.Left, shift: true, alt: true), Visible).Action == ShortcutAction.NudgeWindowLeft, "left");
        Check(Route(Ctrl(ShortcutKey.Right, shift: true, alt: true), Visible).Action == ShortcutAction.NudgeWindowRight, "right");
        Check(Route(Ctrl(ShortcutKey.Up, shift: true, alt: true), Visible).Action == ShortcutAction.NudgeWindowUp, "up");
        Check(Route(Ctrl(ShortcutKey.Down, shift: true, alt: true, repeat: true), Visible).Action == ShortcutAction.NudgeWindowDown, "holding the key keeps it moving");
        Check(Route(Ctrl(ShortcutKey.Left, shift: true, alt: true), Hidden) == ShortcutDecision.PassThrough, "a hidden window does not move");
    }

    [Fact]
    public void ModalSuspendsTheShortcutsWithoutLeakingThem()
    {
        var modal = Live with { ModalOpen = true };
        foreach (var key in new[] { ShortcutKey.S, ShortcutKey.B, ShortcutKey.I, ShortcutKey.Enter, ShortcutKey.Digit1, ShortcutKey.Left })
        {
            Check(Route(Ctrl(key), modal) == ShortcutDecision.Swallow, $"Ctrl+{key} does nothing while a dialog is open, and does not reach another app");
        }
        Check(Route(Ctrl(ShortcutKey.C), modal) == ShortcutDecision.PassThrough, "copy still works in a dialog");
    }

    [Fact]
    public void PracticeKeepsTheKeyboard()
    {
        var practice = new ShortcutState(WindowVisible: true, CurrentTab: Tab.Practice, PracticeInteractionEnabled: true);
        Check(Route(Ctrl(ShortcutKey.Enter), practice) == ShortcutDecision.Do(ShortcutAction.PracticePrimaryAction), "Ctrl+Enter is the practice primary action");
        Check(Route(Ctrl(ShortcutKey.Enter, repeat: true), practice) == ShortcutDecision.Swallow, "a held key does not answer twice");
        Check(Route(Ctrl(ShortcutKey.S), practice) == ShortcutDecision.Swallow, "screenshots are suspended in practice");
        Check(Route(Ctrl(ShortcutKey.G), practice) == ShortcutDecision.Swallow, "clear is suspended in practice");
        Check(Route(Ctrl(ShortcutKey.Enter), practice with { ModalOpen = true }) == ShortcutDecision.Swallow, "a dialog over practice blocks the primary action");
        Check(Route(Ctrl(ShortcutKey.B), practice).Action == ShortcutAction.ToggleWindow, "the window can still be hidden");
        Check(Route(Ctrl(ShortcutKey.Digit2), practice).Action == ShortcutAction.ShowVoice, "tabs still switch");
        Check(Route(Ctrl(ShortcutKey.S), practice with { PracticeInteractionEnabled = false }).Action == ShortcutAction.CaptureScreenshot, "a practice tab that is not taking input keeps the global keys");
    }

    [Fact]
    public void PushToTalkAlwaysSeesItsRelease()
    {
        var router = new ShortcutRouter();
        var down = new KeyStroke(ShortcutKey.M, IsDown: true, Control: true, Shift: true, Alt: false);
        Check(router.Handle(down, Visible) == ShortcutDecision.PassThrough, "Ctrl+Shift+M is not ours outside an interview");
        Check(router.Handle(down with { IsDown = false }, Visible) == ShortcutDecision.PassThrough, "nor is its release");

        Check(router.Handle(down, Live) == ShortcutDecision.Do(ShortcutAction.PushToTalkDown), "the press mutes");
        Check(router.Handle(down with { IsRepeat = true }, Live) == ShortcutDecision.Do(ShortcutAction.PushToTalkRepeat), "autorepeat is swallowed");
        var bareRelease = new KeyStroke(ShortcutKey.M, IsDown: false, Control: false, Shift: false, Alt: false);
        Check(router.Handle(bareRelease, Live with { ModalOpen = true }) == ShortcutDecision.Do(ShortcutAction.PushToTalkUp), "the release is taken even after the modifiers are let go or a dialog opens");
        Check(router.Handle(bareRelease, Live) == ShortcutDecision.PassThrough, "a second release is someone else's");

        Check(router.Handle(down with { Alt = true }, Live) == ShortcutDecision.PassThrough, "Ctrl+Alt+Shift+M is not the mute key");
        Check(router.Handle(down, Live with { ModalOpen = true }) == ShortcutDecision.PassThrough, "a dialog blocks a new press");
        Check(router.Handle(new KeyStroke(ShortcutKey.M, true, Control: true, Shift: false, Alt: false), Live) == ShortcutDecision.PassThrough, "Ctrl+M is not the mute key");

        router.Handle(down, Live);
        router.Reset();
        Check(router.Handle(bareRelease, Live) == ShortcutDecision.PassThrough, "a new session forgets a press from the old one");
    }
}
