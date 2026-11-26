using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The answer to the selected question: the question as a heading, the answer under it, and what
/// stands in for the answer while it is on its way or after it failed.
public sealed class FocusAnswerView : FrameView
{
    private const double RowHeight = 17;
    private const double MinimumTitleWidth = 150;
    private const string Welcome = "Start the interview and each question the interviewer asks gets a short answer here. Code opens here too.";

    private readonly Border surface = new();
    private readonly TextBlock titleLabel = Ui.Overline(TopicTitle.Unknown, Token.Hue.Accent.Text);
    private readonly TextBlock positionLabel = Ui.Label("", Token.Font.Caption, Token.Font.Medium, Token.Colors.TextMuted, digits: true);
    private readonly TextBlock questionLabel = Ui.Wrapping("", Token.Font.Title, Token.Font.Semibold, Token.Colors.TextPrimary, Token.Leading.Heading, maxLines: 2);
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly AnswerTextView text = new();
    private readonly SkeletonView skeleton = new() { IsVisible = false };
    private readonly TextBlock failureLabel = Ui.Label("Couldn't generate the answer", Token.Font.Body, Token.Font.Medium, Token.Hue.Danger.Text);
    private readonly TextBlock failureDetail = Ui.Wrapping("", Token.Font.Caption, color: Token.Colors.TextMuted, maxLines: 2);
    private readonly HoverButton retryButton = HoverButton.Chip("Try again", Token.ControlStyle.Chip(Token.Hue.Warning), Token.Hue.Warning.Text);
    private readonly DispatcherTimer waitTimer = new() { Interval = TimeSpan.FromSeconds(Token.Motion.ClockTick) };

    private Guid? currentEntryId;
    private string topic = TopicTitle.Unknown;
    private (DateTime Since, bool Queued)? waiting;
    private QuestionBurstPhase shownPhase = QuestionBurstPhase.Ready;
    private string? shownLatency;
    private bool listShowsPhase;
    private string positionFull = "";
    private string positionShort = "";
    private bool attached;

    /// The clock the wait is counted on. The UI driver replaces it.
    public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public event Action<Guid>? Retry;

    public FocusAnswerView()
    {
        positionLabel.TextAlignment = TextAlignment.Right;
        positionLabel.VerticalAlignment = VerticalAlignment.Center;
        titleLabel.VerticalAlignment = VerticalAlignment.Center;
        questionLabel.IsVisible = false;
        failureLabel.IsVisible = false;
        failureLabel.VerticalAlignment = VerticalAlignment.Center;
        failureDetail.IsVisible = false;
        retryButton.IsVisible = false;
        retryButton.Help = "Ask for this answer again";
        retryButton.Click += () =>
        {
            if (currentEntryId is { } id) Retry?.Invoke(id);
        };
        scroll.Content = text;
        scroll.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(Token.Motion.FadeIn) } };
        waitTimer.Tick += (_, _) =>
        {
            if (waiting is not null) RefreshStatus();
        };
        Children.Add(surface);
        Children.Add(titleLabel);
        Children.Add(positionLabel);
        Children.Add(questionLabel);
        Children.Add(scroll);
        Children.Add(skeleton);
        Children.Add(failureLabel);
        Children.Add(failureDetail);
        Children.Add(retryButton);
        ApplyTransparency();
        ShowWelcome();
    }

    public string Topic => topic;

    public string Position => positionLabel.Text ?? "";

    public string Question => questionLabel.IsVisible ? questionLabel.Text ?? "" : "";

    public string AnswerText => scroll.IsVisible ? text.PlainText : "";

    public bool ShowsSkeleton => skeleton.IsVisible;

    public bool ShowsFailure => failureLabel.IsVisible;

    public string FailureDetail => failureDetail.IsVisible ? failureDetail.Text ?? "" : "";

    public HoverButton RetryButton => retryButton;

    public ScrollViewer Scroll => scroll;

    public void ApplyTransparency() => Ui.Surface(surface, Token.SurfaceStyle.Card);

    public void Update(
        QuestionBurstEntry? entry, int position, int total, IReadOnlyList<AnswerParagraph>? answer, bool streaming, bool canRetry = false, bool listShowsPhase = false)
    {
        if (entry is null)
        {
            ShowWelcome();
            return;
        }
        var selectionChanged = currentEntryId != entry.Id;
        currentEntryId = entry.Id;
        SetTopic(TopicTitle.Display(entry.Topic));
        var asked = entry.Question.Trim();
        Ui.SetText(questionLabel, asked);
        Ui.Tip(questionLabel, asked);
        questionLabel.IsVisible = asked.Length > 0;

        var hasText = answer is { Count: > 0 };
        var wasWaiting = waiting is not null;
        var onItsWay = entry.Phase is not (QuestionBurstPhase.Failed or QuestionBurstPhase.Ready);
        waiting = !hasText && onItsWay ? (entry.DetectedAt, entry.Phase == QuestionBurstPhase.Queued) : null;
        shownPhase = entry.Phase;
        this.listShowsPhase = listShowsPhase;
        shownLatency = entry.LatencyMs is { } ms ? InterviewMessage.FormatLatency(ms) : null;
        RefreshStatus();
        UpdateWaitTimer();

        if (hasText)
        {
            text.SetAnswer(answer!, streaming);
            var revealing = !scroll.IsVisible;
            scroll.IsVisible = true;
            failureLabel.IsVisible = false;
            failureDetail.IsVisible = false;
            retryButton.IsVisible = false;
            skeleton.IsVisible = false;
            if (wasWaiting && !selectionChanged && revealing) FadeIn(scroll);
            if (selectionChanged) scroll.Offset = default;
        }
        else
        {
            text.Clear();
            scroll.IsVisible = false;
            var failed = entry.Phase == QuestionBurstPhase.Failed;
            failureLabel.IsVisible = failed;
            Ui.SetText(failureDetail, entry.Failure ?? "");
            failureDetail.IsVisible = failed && !string.IsNullOrEmpty(entry.Failure);
            retryButton.IsVisible = failed && canRetry;
            skeleton.IsVisible = waiting is not null;
        }
        AutomationProperties.SetName(this, $"{entry.Phase.Title()}, question {position} of {Math.Max(total, 1)}, {topic}");
        Relayout();
    }

    public void ShowWelcome()
    {
        currentEntryId = null;
        SetTopic(TopicTitle.Unknown);
        positionFull = "";
        positionShort = "";
        Ui.SetText(positionLabel, "");
        questionLabel.IsVisible = false;
        text.SetPlain(Welcome, Token.Colors.TextBody);
        scroll.IsVisible = true;
        scroll.Offset = default;
        waiting = null;
        UpdateWaitTimer();
        skeleton.IsVisible = false;
        failureLabel.IsVisible = false;
        failureDetail.IsVisible = false;
        retryButton.IsVisible = false;
        Relayout();
    }

    private void SetTopic(string value)
    {
        topic = value;
        Ui.SetText(titleLabel, Ui.OverlineText(value));
        Ui.Tip(titleLabel, value);
    }

    /// The right of the title row: the wait while there is one, else the phase unless the strip
    /// already says it.
    private void RefreshStatus()
    {
        if (waiting is { } wait)
        {
            var elapsed = (Now() - wait.Since).TotalSeconds;
            positionFull = WaitProgress.AnswerStatus(wait.Queued, elapsed, Token.Text.Separator);
            positionShort = WaitProgress.AnswerStatusShort(wait.Queued, elapsed);
            Ui.Tip(positionLabel, positionFull);
            if (listShowsPhase && (wait.Queued || WaitProgress.Stage(elapsed) == WaitStage.Brief))
            {
                positionFull = "";
                positionShort = "";
            }
        }
        else
        {
            var ready = shownPhase == QuestionBurstPhase.Ready;
            var silent = ready || listShowsPhase;
            positionFull = silent ? "" : shownPhase.Title() + (shownLatency is null ? "" : Token.Text.Separator + shownLatency);
            positionShort = silent ? "" : shownPhase.Title();
            Ui.Tip(positionLabel, shownLatency is null ? shownPhase.Title() : $"{shownPhase.Title()} in {shownLatency}");
            if (ready && shownLatency is not null && questionLabel.IsVisible)
            {
                Ui.Tip(questionLabel, $"{questionLabel.Text}\n{shownPhase.Title()} in {shownLatency}");
            }
        }
        Ui.SetText(positionLabel, positionFull);
        Relayout();
    }

    private void UpdateWaitTimer()
    {
        if (waiting is not null && attached) waitTimer.Start();
        else waitTimer.Stop();
    }

    private static void FadeIn(Control content)
    {
        if (Token.Motion.ReducesMotion()) return;
        var transitions = content.Transitions;
        content.Transitions = null;
        content.Opacity = 0;
        content.Transitions = transitions;
        content.Opacity = 1;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        UpdateWaitTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        UpdateWaitTimer();
    }

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        var inset = Token.Space.Inset;
        var rowWidth = size.Width - inset * 2;
        titleLabel.IsVisible = !questionLabel.IsVisible;
        var rowHeight = !titleLabel.IsVisible && positionFull.Length == 0 ? 0 : RowHeight;

        double PositionWidth(string value) => value.Length == 0 ? 0 : Ui.TextWidth(value, Token.Font.Caption, Token.Font.Medium, digits: true) + 6;
        var fitsWhole = rowWidth - PositionWidth(positionFull) - Token.Space.Md >= MinimumTitleWidth;
        var position = fitsWhole || positionShort.Length == 0 ? positionFull : positionShort;
        Ui.SetText(positionLabel, position);
        var positionWidth = position.Length == 0 ? 0 : Math.Min(PositionWidth(position), rowWidth * 0.6);
        var positionGap = position.Length == 0 ? 0 : Token.Space.Md;
        positionLabel.IsVisible = position.Length > 0;
        if (titleLabel.IsVisible) Place(titleLabel, inset, inset, Math.Max(40, rowWidth - positionWidth - positionGap), rowHeight);
        if (positionLabel.IsVisible) Place(positionLabel, size.Width - inset - positionWidth, inset, positionWidth, rowHeight);

        var readingWidth = Math.Min(rowWidth, Token.Measure.Reading);
        var headingTop = inset + rowHeight + (rowHeight > 0 ? Token.Space.Xs : 0);
        var questionHeight = 0.0;
        if (questionLabel.IsVisible)
        {
            // A short card keeps the heading to one line so the answer still shows.
            questionLabel.MaxLines = size.Height >= 220 ? 2 : 1;
            questionHeight = HeightOf(questionLabel, readingWidth);
            Place(questionLabel, inset, headingTop, readingWidth, questionHeight);
        }
        var textTop = headingTop + (questionHeight > 0 ? questionHeight + Token.Space.Md : Token.Space.Sm);
        var textHeight = Math.Max(40, size.Height - inset - textTop);
        if (scroll.IsVisible) Place(scroll, inset, textTop, readingWidth, textHeight);

        if (skeleton.IsVisible)
        {
            var lineHeight = Token.Font.Lead * Token.Leading.Reading;
            var pitch = lineHeight + Token.Space.Xs;
            var rows = Math.Max(1, Math.Min(3, (int)Math.Floor((Math.Max(0, size.Height - inset - textTop) + Token.Space.Xs) / pitch)));
            skeleton.Pieces = SkeletonView.AnswerLines(rows, text.BulletIndent, pitch, lineHeight);
            Place(skeleton, inset, textTop, readingWidth, rows * pitch);
        }

        var failureTop = textTop + 2;
        if (failureLabel.IsVisible) Place(failureLabel, inset, failureTop, Math.Max(60, Math.Min(240, rowWidth)), 18);
        var above = failureTop + 18;
        if (failureDetail.IsVisible)
        {
            var detailWidth = Math.Min(rowWidth, Token.Measure.Note);
            var detailHeight = HeightOf(failureDetail, detailWidth);
            Place(failureDetail, inset, above + Token.Space.Xs, detailWidth, detailHeight);
            above += Token.Space.Xs + detailHeight;
        }
        if (retryButton.IsVisible)
        {
            var buttonWidth = Ui.TextWidth(retryButton.Title, Token.Font.Caption, Token.Font.Semibold) + 24;
            Place(retryButton, inset, above + Token.Space.Sm, buttonWidth, Token.Sizing.ControlSmall);
        }
    }
}
