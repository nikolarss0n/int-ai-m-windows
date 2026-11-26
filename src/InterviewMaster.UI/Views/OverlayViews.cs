using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The note at the corner of the screen after a screenshot was taken while another app had the
/// keyboard: what was captured and the keys that act on it.
public sealed class ScreenshotAlertView : FrameView
{
    public const double AlertWidth = 400;
    public const double AlertHeight = 120;

    private readonly Border surface = new()
    {
        Background = Token.Palette.Graphite.WithAlpha(0.95).Brush(),
        BorderBrush = Token.Palette.SystemPurple.Brush(),
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(16),
    };
    private readonly TextBlock title = Ui.Label("📸 Screenshot Captured!", 16, FontWeight.Bold, Token.Palette.White);
    private readonly TextBlock shortcuts = Ui.Label(PlatformCopy.ScreenshotAlertShortcuts, 11, Token.Font.Medium, Token.Palette.White.WithAlpha(0.9));
    private readonly StackPanel thumbnails = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    public ScreenshotAlertView()
    {
        title.TextAlignment = TextAlignment.Center;
        shortcuts.TextAlignment = TextAlignment.Center;
        scroll.Content = thumbnails;
        Children.Add(surface);
        Children.Add(title);
        Children.Add(shortcuts);
        Children.Add(scroll);
    }

    public int ThumbnailCount => thumbnails.Children.Count;

    public void Show(IReadOnlyList<Screenshot> screenshots)
    {
        thumbnails.Children.Clear();
        foreach (var screenshot in screenshots)
        {
            var frame = new Border
            {
                Width = 70,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                BorderBrush = Token.Palette.White.WithAlpha(0.3).Brush(),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };
            if (Decode(screenshot) is { } bitmap) frame.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            thumbnails.Children.Add(frame);
        }
        Relayout();
    }

    /// The screenshot scaled for a thumbnail, or null when its bytes are not a picture.
    private static Bitmap? Decode(Screenshot screenshot)
    {
        if (screenshot.PngData.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(screenshot.PngData);
            return Bitmap.DecodeToWidth(stream, 140);
        }
        catch (Exception)
        {
            return null;
        }
    }

    protected override Size PreferredSize(Size available) => new(AlertWidth, AlertHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        Place(title, 20, 11, size.Width - 40, 24);
        Place(shortcuts, 20, 40, size.Width - 40, 18);
        Place(scroll, 15, 65, size.Width - 30, 40);
    }
}

/// The code that stays on screen while the main window is hidden, with the last question and its
/// answer under it.
public sealed class FloatingSolutionView : FrameView
{
    public const double PanelWidth = 500;
    public const double MaxHeight = 750;
    public const double ScrollStep = 50;
    public const double MoveStep = 30;

    private const double QAHeight = 270;
    private const double LoadingHeight = 40;

    private readonly Border surface = new()
    {
        Background = Token.Palette.Graphite.WithAlpha(0.96).Brush(),
        BorderBrush = Token.Palette.Purple.WithAlpha(0.5).Brush(),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
    };
    private readonly TextBlock header = Ui.Label(PlatformCopy.FloatingSolutionShortcuts, 10, color: Token.Palette.White.WithAlpha(0.35), mono: true);
    private readonly AnswerTextView solution = new(15, 1.25) { Margin = new Thickness(4) };
    private readonly ScrollViewer solutionScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly GlowDot loading = new() { IsVisible = false };
    private readonly Border divider = new() { Background = Token.Colors.Separator.Brush(), IsVisible = false };
    private readonly Border questionBubble = new() { CornerRadius = new CornerRadius(8), Background = Token.Palette.White.WithAlpha(0.08).Brush(), IsVisible = false };
    private readonly TextBlock question = Ui.Wrapping("", 11, color: Token.Colors.TextMuted, leading: 1.25, maxLines: 2);
    private readonly Border answerBubble = new() { CornerRadius = new CornerRadius(8), Background = Token.Palette.Purple.WithAlpha(0.15).Brush(), IsVisible = false };
    private readonly AnswerTextView answer = new(15, 1.25) { Margin = new Thickness(2) };
    private readonly ScrollViewer answerScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
    private bool hasQA;

    public FloatingSolutionView()
    {
        header.TextAlignment = TextAlignment.Center;
        question.TextTrimming = TextTrimming.CharacterEllipsis;
        question.IsVisible = false;
        solutionScroll.Content = solution;
        answerScroll.Content = answer;
        foreach (var child in new Control[] { surface, header, solutionScroll, loading, divider, questionBubble, question, answerBubble, answerScroll }) Children.Add(child);
    }

    public string SolutionText => solution.PlainText;

    public string QuestionText => hasQA ? question.Text ?? "" : "";

    public string AnswerText => hasQA ? answer.PlainText : "";

    public bool IsLoading => loading.IsVisible;

    public double ScrollOffset => solutionScroll.Offset.Y;

    public void SetSolution(string content) => solution.SetAnswer(AnswerFormatter.Format(content), streaming: false);

    /// The last question and its answer, or null when nothing has been answered yet.
    public void SetQA((string Question, string Answer)? pair)
    {
        hasQA = pair is not null;
        foreach (var part in new Control[] { divider, questionBubble, question, answerBubble, answerScroll }) part.IsVisible = hasQA;
        if (pair is { } qa)
        {
            question.Text = qa.Question;
            answer.SetAnswer(AnswerFormatter.Format(qa.Answer), streaming: false);
        }
        Relayout();
    }

    public void SetLoading(bool on) => loading.IsVisible = on;

    /// Scrolls the code by `delta`; a positive step goes up, as the Mac's keys do.
    public void Scroll(double delta)
    {
        var limit = Math.Max(0, solutionScroll.Extent.Height - solutionScroll.Viewport.Height);
        solutionScroll.Offset = new Vector(0, Math.Clamp(solutionScroll.Offset.Y - delta, 0, limit));
    }

    /// The newest turn's question with its answer; failing that, the last answer and the question before it.
    public static (string Question, string Answer)? LastQuestionAnswer(IReadOnlyList<InterviewMessage> messages)
    {
        var sequences = messages.Where(message => message.TurnSequence is not null).Select(message => message.TurnSequence!.Value).ToList();
        if (sequences.Count > 0)
        {
            var newest = sequences.Max();
            var asked = messages.LastOrDefault(message => message.TurnSequence == newest && message.Type == MessageType.Question);
            var answered = messages.LastOrDefault(message => message.TurnSequence == newest && message.IsAnswer);
            if (asked is not null && answered is not null) return (asked.Content, answered.Content);
        }
        InterviewMessage? lastAnswer = null;
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (lastAnswer is null && message.IsAnswer) lastAnswer = message;
            if (lastAnswer is not null && message.Type == MessageType.Question) return (message.Content, lastAnswer.Content);
        }
        return null;
    }

    protected override void LayoutFrames(Size size)
    {
        var qa = hasQA ? QAHeight : 0;
        Place(surface, 0, 0, size.Width, size.Height);
        Place(header, 12, 8, size.Width - 24, 16);
        Place(solutionScroll, 8, 28, size.Width - 16, size.Height - 36 - qa - LoadingHeight);
        if (loading.IsVisible) Place(loading, (size.Width - GlowDot.Side) / 2, size.Height - qa - LoadingHeight, GlowDot.Side, GlowDot.Side);
        if (!hasQA) return;
        Place(divider, 12, size.Height - qa - 5, size.Width - 24, 1);
        var top = size.Height - qa + 4;
        var width = size.Width - 16;
        Place(questionBubble, 8, top, width, 38);
        Place(question, 16, top + 4, width - 16, 30);
        var answerTop = top + 38 + 8;
        var answerHeight = Math.Max(36, qa - 12 - 38 - 8);
        Place(answerBubble, 8, answerTop, width, answerHeight);
        Place(answerScroll, 12, answerTop + 4, width - 8, answerHeight - 8);
    }

    /// A green dot whose glow breathes while an answer is on its way.
    private sealed class GlowDot : AnimatedControl
    {
        public const double Side = 40;

        protected override bool WantsFrames => IsVisible;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty) RefreshClock();
        }

        public override void Render(DrawingContext context)
        {
            // 1.2 s out and 1.2 s back, eased at both ends.
            var phase = Token.Motion.ReducesMotion() ? 0.5 : (1 - Math.Cos(FrameClock.Now() / 1.2 * Math.PI)) / 2;
            var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var reach = 6 + 9 * phase;
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Token.Palette.Green.WithAlpha(0.3 + 0.4 * phase), 0),
                    new GradientStop(Token.Palette.Green.WithAlpha(0), 1),
                },
            };
            context.DrawEllipse(glow, null, centre, Side / 2 + reach, Side / 2 + reach);
            context.DrawEllipse(Token.Palette.Green.WithAlpha(0.2).Brush(), null, centre, Side / 2, Side / 2);
        }
    }
}
