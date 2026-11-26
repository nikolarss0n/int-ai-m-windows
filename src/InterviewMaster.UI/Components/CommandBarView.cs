using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The bar under the content: where to go, start and end, which audio to listen to, and the two
/// things done after an interview. While an interview runs on the live view, the places to go step
/// out of the way.
public sealed class CommandBarView : FrameView
{
    private readonly Border surface = new();

    public CommandBarView()
    {
        ContextButton = Make(Icons.Document, "Context", "Ctrl+1");
        TimelineButton = Make(Icons.Clock, "Timeline", "Ctrl+2");
        TimelineButton.Apply(Token.ControlStyle.GhostSelected);
        PracticeButton = Make(Icons.GraduationCap, "Practice", "Ctrl+4");
        CodeButton = Make(Icons.Code, "Code");
        InterviewButton = new HoverButton
        {
            Width = Token.CommandBar.PrimaryWidth,
            Height = Token.Sizing.Control,
            CornerRadius = new CornerRadius(Token.Radius.Full(Token.Sizing.Control)),
            Icon = Icons.Play,
            IconSize = 13
        };
        SystemAudioButton = Make(Icons.Speaker, "Use system audio: the interviewer is on a call");
        MicrophoneButton = Make(Icons.Microphone, "Use the microphone: the interviewer is in the room");
        MuteButton = Make(Icons.WaveformSlash, "Audio starts with the interview");
        Capsule = new ActivityCapsuleView(compact: true);
        ExportButton = Make(Icons.Export, "Export interview");
        SettingsButton = Make(Icons.Gear, "Settings");

        Children.Add(surface);
        foreach (var control in Leading) Children.Add(control);
        Children.Add(ExportButton);
        Children.Add(SettingsButton);
        ApplyTransparency();
        SetInterviewActive(false);
        SetCode(hasCode: false, visible: false);
    }

    public HoverButton ContextButton { get; }
    public HoverButton TimelineButton { get; }
    public HoverButton PracticeButton { get; }
    public HoverButton CodeButton { get; }
    public HoverButton InterviewButton { get; }
    public HoverButton SystemAudioButton { get; }
    public HoverButton MicrophoneButton { get; }
    public HoverButton MuteButton { get; }
    public ActivityCapsuleView Capsule { get; }
    public HoverButton ExportButton { get; }
    public HoverButton SettingsButton { get; }

    /// Left to right. A gap of `GroupGap` follows the code button and the start button.
    private IEnumerable<Control> Leading => new Control[]
    {
        ContextButton, TimelineButton, PracticeButton, CodeButton, InterviewButton, SystemAudioButton, MicrophoneButton, MuteButton, Capsule
    };

    public void ApplyTransparency() => Ui.Surface(surface, Token.SurfaceStyle.CommandBar);

    private static HoverButton Make(IconShape icon, string title, string? shortcut = null)
    {
        var button = HoverButton.Glyph(icon, title);
        button.Help = shortcut is null ? title : $"{title} ({shortcut})";
        return button;
    }

    /// Lights the place the window is showing, and hides all three during an interview on the live view.
    public void SetNavigation(Tab current, bool inCall)
    {
        ContextButton.Apply(current == Tab.Notes ? Token.ControlStyle.GhostSelected : Token.ControlStyle.Ghost);
        TimelineButton.Apply(current == Tab.Voice ? Token.ControlStyle.GhostSelected : Token.ControlStyle.Ghost);
        PracticeButton.Apply(Token.ControlStyle.Ghost);
        foreach (var button in new[] { ContextButton, TimelineButton, PracticeButton }) button.IsVisible = !inCall;
        // The header says the status on the live view; the bar says it everywhere else.
        Capsule.IsVisible = current != Tab.Voice;
        Relayout();
    }

    public void SetInterviewActive(bool active)
    {
        var hue = active ? Token.Hue.Danger : Token.Hue.Success;
        var label = active ? "End interview" : "Start interview";
        InterviewButton.Icon = active ? Icons.Stop : Icons.Play;
        InterviewButton.Title = active ? "End" : "Start";
        InterviewButton.TitleColor = hue.Text;
        InterviewButton.Apply(Token.ControlStyle.Primary(hue));
        InterviewButton.Label = label;
        InterviewButton.Help = label;
    }

    /// The source in use is lit in the accent; the other is a muted glyph. Mute appears with the interview.
    public void SetAudio(CaptureDevice selected, bool switching, CapturePresentation state, bool muted, bool interviewActive, bool hasActiveDevice)
    {
        foreach (var (button, device) in new[] { (SystemAudioButton, CaptureDevice.SystemAudio), (MicrophoneButton, CaptureDevice.Microphone) })
        {
            var isSelected = device == selected;
            button.Apply(isSelected ? Token.ControlStyle.Selected(Token.Hue.Accent) : Token.ControlStyle.Ghost);
            button.GlyphColor = isSelected ? null : Token.Colors.TextMuted;
            button.IsEnabled = !switching;
            AutomationProperties.SetItemStatus(button, isSelected ? "Selected" : "Not selected");
        }
        MuteButton.Icon = Icons.ForSymbol(state.Symbol);
        MuteButton.Label = state.Label;
        MuteButton.Help = state.Label;
        AutomationProperties.SetItemStatus(MuteButton, muted ? "Muted" : "Unmuted");
        MuteButton.IsEnabled = interviewActive;
        MuteButton.IsVisible = interviewActive;
        MuteButton.GlyphColor = muted ? Token.Hue.Warning.Text : hasActiveDevice ? Token.Hue.Success.Text : Token.Colors.TextMuted;
        Relayout();
    }

    /// The code button is dim until there is code, and lit while the panel shows.
    public void SetCode(bool hasCode, bool visible)
    {
        CodeButton.Opacity = hasCode || visible ? 1 : Token.State.IdleAlpha;
        CodeButton.Apply(visible ? Token.ControlStyle.Selected(Token.Hue.Accent) : Token.ControlStyle.Ghost);
        var action = visible ? "Hide code panel" : "Show code panel";
        CodeButton.Label = "Code";
        CodeButton.Help = hasCode ? $"{action} (Ctrl+3)" : $"{action} (Ctrl+3). Code appears here when the interviewer asks for it.";
        AutomationProperties.SetItemStatus(CodeButton, visible ? "Shown" : "Hidden");
    }

    protected override Size PreferredSize(Size available) => new(available.Width, Token.CommandBar.Height);

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        var padding = Token.CommandBar.Padding;
        var x = padding.Left;
        var gap = 0.0;
        foreach (var control in Leading)
        {
            if (!control.IsVisible) continue;
            x += gap;
            var width = control == Capsule ? Token.CommandBar.CapsuleWidth : control.Width;
            Place(control, x, (size.Height - Token.Sizing.Control) / 2, width, Token.Sizing.Control);
            x += width;
            gap = control == CodeButton || control == InterviewButton ? Token.CommandBar.GroupGap : Token.CommandBar.ItemGap;
        }
        var side = Token.Sizing.Control;
        var settingsX = size.Width - padding.Right - side;
        Place(SettingsButton, settingsX, (size.Height - side) / 2, side, side);
        Place(ExportButton, settingsX - Token.CommandBar.ItemGap - side, (size.Height - side) / 2, side, side);
    }
}
