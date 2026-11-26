using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

public enum ActivityKind { Idle, Listening, Transcribing, Connecting, Muted, Unavailable, Working, Ready }

/// What the status capsule says. `Speaking` belongs to listening, `QuestionNumber` to working.
public readonly record struct ActivityMode(ActivityKind Kind, bool Speaking = false, int? QuestionNumber = null)
{
    public static readonly ActivityMode Idle = new(ActivityKind.Idle);
    public static readonly ActivityMode Transcribing = new(ActivityKind.Transcribing);
    public static readonly ActivityMode Connecting = new(ActivityKind.Connecting);
    public static readonly ActivityMode Muted = new(ActivityKind.Muted);
    public static readonly ActivityMode Unavailable = new(ActivityKind.Unavailable);
    public static readonly ActivityMode Ready = new(ActivityKind.Ready);

    public static ActivityMode Listening(bool speaking) => new(ActivityKind.Listening, Speaking: speaking);

    public static ActivityMode Working(int? questionNumber) => new(ActivityKind.Working, QuestionNumber: questionNumber);

    public bool NeedsAttention => Kind is ActivityKind.Muted or ActivityKind.Unavailable;

    /// What to do about a state that stops the app from hearing.
    public string? Explanation => Kind switch
    {
        ActivityKind.Muted => $"Muted. Click the mute button or press {PlatformCopy.MuteShortcut} to unmute.",
        ActivityKind.Unavailable => "No audio source is working. Select one in the bar to try again.",
        ActivityKind.Connecting => "Starting the audio source.",
        _ => null
    };
}

/// A dot that breathes while work is in progress.
public sealed class PulseDot : AnimatedControl
{
    private bool pulses;

    public PulseDot()
    {
        IsHitTestVisible = false;
    }

    public Color Color { get; set; } = Token.Hue.Warning.Solid;

    public bool Pulses
    {
        get => pulses;
        set
        {
            pulses = value;
            RefreshClock();
            InvalidateVisual();
        }
    }

    protected override bool WantsFrames => pulses;

    public override void Render(DrawingContext context)
    {
        var opacity = 1.0;
        if (pulses && !Token.Motion.ReducesMotion())
        {
            var phase = FrameClock.Now() % (Token.Waveform.PulsePeriod * 2) / Token.Waveform.PulsePeriod;
            var along = phase <= 1 ? phase : 2 - phase;
            opacity = 1 - (1 - Token.Waveform.PulseLowOpacity) * (along * along * (3 - 2 * along));
        }
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2;
        context.DrawEllipse(Color.Faded(opacity).Brush(), null, Bounds.WithX(0).WithY(0).Center, radius, radius);
    }
}

/// The status of the interview in a few words: listening, transcribing, working, muted. The header
/// shows it at the size of its content; the command bar shows a compact one on the other tabs.
public sealed class ActivityCapsuleView : FrameView
{
    private const double MaximumLabelWidth = 128;
    private readonly bool compact;
    private readonly Border background = new();
    private readonly PulseDot recordingDot = new() { Color = Token.Hue.Danger.Solid };
    private readonly PulseDot workingDot = new() { Color = Token.Hue.Warning.Solid, IsVisible = false };
    private readonly TextBlock stateLabel = Ui.Label("Ready", Token.Font.Caption, Token.Font.Semibold, digits: true);
    private readonly TextBlock elapsedLabel = Ui.Label("", Token.Font.Caption, Token.Font.Medium, Token.Colors.TextBody, digits: true);
    private readonly WaveformView waveform = new();

    public ActivityCapsuleView(bool compact = false)
    {
        this.compact = compact;
        Height = compact ? Token.Sizing.Control : Token.Sizing.Notice;
        elapsedLabel.TextAlignment = TextAlignment.Right;
        Children.Add(background);
        Children.Add(recordingDot);
        Children.Add(workingDot);
        Children.Add(stateLabel);
        Children.Add(waveform);
        Children.Add(elapsedLabel);
        Update(ActivityMode.Idle, false, "");
    }

    public ActivityMode Mode { get; private set; } = ActivityMode.Idle;

    public bool IsRecording { get; private set; }

    public string StateText => stateLabel.Text ?? "";

    public string ElapsedText => elapsedLabel.Text ?? "";

    public WaveformView Waveform => waveform;

    public void Update(ActivityMode mode, bool isRecording, string elapsed)
    {
        Mode = mode;
        IsRecording = isRecording;
        Ui.SetText(elapsedLabel, elapsed);
        elapsedLabel.IsVisible = elapsed.Length > 0 && !compact;
        recordingDot.IsVisible = !compact && isRecording;

        var (hue, labelColor, label, animate, meter) = mode.Kind switch
        {
            ActivityKind.Listening => (Token.Hue.Success, Token.Hue.Success.Text, "Listening", false, true),
            ActivityKind.Transcribing => (Token.Hue.Warning, Token.Hue.Warning.Text, "Transcribing…", true, false),
            ActivityKind.Connecting => (Token.Hue.Neutral, Token.Colors.TextBody, "Connecting audio…", true, false),
            ActivityKind.Muted => (Token.Hue.Warning, Token.Hue.Warning.Text, "Muted", false, false),
            ActivityKind.Unavailable => (Token.Hue.Danger, Token.Hue.Danger.Text, "No audio", false, false),
            ActivityKind.Working => (
                Token.Hue.Warning, Token.Hue.Warning.Text,
                mode.QuestionNumber is { } number ? $"AI working{Token.Text.Separator}Q{number}" : "AI working", false, false),
            ActivityKind.Ready => (Token.Hue.Success, Token.Hue.Success.Text, "Answer ready", false, false),
            _ => (Token.Hue.Neutral, Token.Colors.TextMuted, "Ready", false, false)
        };
        var spoken = mode is { Kind: ActivityKind.Listening, Speaking: true } ? "Hearing speech" : null;

        // The header has nothing to say while idle; the bar's capsule always holds its place.
        if (!compact) IsVisible = mode.Kind is not (ActivityKind.Idle or ActivityKind.Ready);
        Ui.SetText(stateLabel, label);
        stateLabel.Foreground = labelColor.Brush();
        Ui.Tip(stateLabel, mode.Explanation ?? spoken ?? label);
        Ui.Tip(this, mode.Explanation);

        var working = mode.Kind == ActivityKind.Working;
        workingDot.IsVisible = working;
        workingDot.Pulses = working;
        background.Background = Token.Transparency.Floating ? null : hue.Tint(mode.NeedsAttention ? Token.Tint.Medium : Token.Tint.Soft).Brush();
        waveform.IsVisible = animate || meter;
        waveform.Speaking = mode.Speaking;
        waveform.Set(hue, animate, meter);

        var recording = isRecording ? ", recording" : "";
        var time = elapsed.Length == 0 ? "" : $", {elapsed}";
        AutomationProperties.SetName(this, $"{spoken ?? label}{recording}{time}");
        Relayout();
        (Parent as FrameView)?.Relayout();
    }

    public void SetLevel(double db) => waveform.SetLevel(db);

    private readonly record struct Pieces(Rect Dot, Rect Working, Rect Label, Rect Waveform, Rect Elapsed, double Width);

    private Pieces HeaderPieces(double height)
    {
        var edge = Token.Space.Inset - 2;
        var x = edge;
        Rect dot = default, working = default, wave = default, elapsed = default;
        if (recordingDot.IsVisible)
        {
            dot = new Rect(x, (height - Token.Sizing.Dot) / 2, Token.Sizing.Dot, Token.Sizing.Dot);
            x += Token.Sizing.Dot + Token.Space.Sm;
        }
        if (workingDot.IsVisible)
        {
            working = new Rect(x, (height - Token.Sizing.Dot) / 2, Token.Sizing.Dot, Token.Sizing.Dot);
            x += Token.Sizing.Dot + Token.Space.Sm;
        }
        var labelWidth = Math.Min(WidthOf(stateLabel) + 4, MaximumLabelWidth);
        var label = new Rect(x, (height - 18) / 2, labelWidth, 18);
        x += labelWidth;
        if (waveform.IsVisible)
        {
            x += Token.Space.Md;
            wave = new Rect(x, (height - 18) / 2, 62, 18);
            x += 62;
        }
        if (elapsedLabel.IsVisible)
        {
            x += Token.Space.Md;
            var elapsedWidth = WidthOf(elapsedLabel) + 4;
            elapsed = new Rect(x, (height - 16) / 2, elapsedWidth, 16);
            x += elapsedWidth;
        }
        return new Pieces(dot, working, label, wave, elapsed, x + edge);
    }

    /// The width the header capsule takes for what it shows now.
    public double FittingWidth => compact ? Token.CommandBar.CapsuleWidth : HeaderPieces(Height).Width;

    protected override Size PreferredSize(Size available) => new(compact ? Token.CommandBar.CapsuleWidth : available.Width, Height);

    protected override void LayoutFrames(Size size)
    {
        background.CornerRadius = new CornerRadius(Token.Radius.Full(size.Height));
        Place(background, 0, 0, size.Width, size.Height);
        stateLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        elapsedLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        if (compact)
        {
            if (waveform.IsVisible) Place(waveform, 10, (size.Height - 16) / 2, 38, 16);
            var labelX = waveform.IsVisible ? 53.0 : 14.0;
            if (workingDot.IsVisible)
            {
                Place(workingDot, labelX, (size.Height - Token.Sizing.Dot) / 2, Token.Sizing.Dot, Token.Sizing.Dot);
                labelX += Token.Sizing.Dot + Token.Space.Sm;
            }
            Place(stateLabel, labelX, (size.Height - 16) / 2, Math.Max(30, size.Width - labelX - 8), 16);
            return;
        }
        var pieces = HeaderPieces(size.Height);
        if (recordingDot.IsVisible) Place(recordingDot, pieces.Dot);
        if (workingDot.IsVisible) Place(workingDot, pieces.Working);
        Place(stateLabel, pieces.Label);
        if (waveform.IsVisible) Place(waveform, pieces.Waveform);
        if (elapsedLabel.IsVisible) Place(elapsedLabel, pieces.Elapsed);
    }
}
