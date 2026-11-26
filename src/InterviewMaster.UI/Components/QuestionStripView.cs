using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// How a question's phase is coloured.
public static class PhaseStyle
{
    public static Token.Hue Hue(this QuestionBurstPhase phase) => phase switch
    {
        QuestionBurstPhase.Queued => Token.Hue.Neutral,
        QuestionBurstPhase.Generating or QuestionBurstPhase.Answering => Token.Hue.Warning,
        QuestionBurstPhase.Ready => Token.Hue.Success,
        _ => Token.Hue.Danger
    };

    public static Color TextColor(this QuestionBurstPhase phase) =>
        phase == QuestionBurstPhase.Queued ? Token.Colors.TextMuted : phase.Hue().Text;
}

/// The last few questions side by side: number, state, and the question in two lines. The selected
/// one carries a bar down its leading edge; a click selects another.
public sealed class QuestionStripView : FrameView
{
    private const double MinimumNumberWidth = 18;
    private readonly Border surface = new();
    private readonly List<Segment> segments = new();
    private IReadOnlyList<QuestionBurstEntry> entries = Array.Empty<QuestionBurstEntry>();
    private IReadOnlyDictionary<Guid, int> numbers = new Dictionary<Guid, int>();
    private IReadOnlySet<Guid> codeIds = new HashSet<Guid>();
    private Guid? selectedId;

    public event Action<Guid>? Selected;

    public QuestionStripView()
    {
        AutomationProperties.SetName(this, "Questions");
        Children.Add(surface);
        ApplyTransparency();
    }

    public IReadOnlyList<QuestionBurstEntry> Entries => entries;

    /// The strip always names the phase in words, so the answer card need not repeat it.
    public bool ShowsPhaseWords => true;

    public void ApplyTransparency() => Ui.Surface(surface, Token.SurfaceStyle.Strip);

    public static string Relation(bool followsPrevious, bool hasCode) => (followsPrevious, hasCode) switch
    {
        (true, true) => $"Follow-up{Token.Text.Separator}code",
        (true, false) => "Follow-up",
        (false, true) => "Code",
        _ => ""
    };

    public void Update(IReadOnlyList<QuestionBurstEntry> next, Guid? selected, IReadOnlyDictionary<Guid, int>? questionNumbers = null, IReadOnlySet<Guid>? withCode = null)
    {
        questionNumbers ??= new Dictionary<Guid, int>();
        withCode ??= new HashSet<Guid>();
        var sameContent = entries.Count == next.Count && entries.Zip(next).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Sequence == pair.Second.Sequence && pair.First.Question == pair.Second.Question
            && pair.First.Phase == pair.Second.Phase && pair.First.LatencyMs == pair.Second.LatencyMs
            && pair.First.FollowsPrevious == pair.Second.FollowsPrevious);
        var sameNumbers = numbers.Count == questionNumbers.Count && numbers.All(pair => questionNumbers.TryGetValue(pair.Key, out var value) && value == pair.Value);
        if (sameContent && selectedId == selected && sameNumbers && codeIds.SetEquals(withCode)) return;
        entries = next.ToList();
        selectedId = selected;
        numbers = questionNumbers.ToDictionary(pair => pair.Key, pair => pair.Value);
        codeIds = withCode.ToHashSet();
        Rebuild();
    }

    private double NumberWidth
    {
        get
        {
            var longest = (numbers.Count > 0 ? numbers.Values.Max() : entries.Count).ToString();
            return Math.Max(MinimumNumberWidth, Ui.TextWidth(longest, Token.Font.Body, Token.Font.Semibold, digits: true) + 4);
        }
    }

    private void Rebuild()
    {
        foreach (var segment in segments) Children.Remove(segment);
        segments.Clear();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var number = numbers.TryGetValue(entry.Id, out var known) ? known : index + 1;
            var divided = !Token.Transparency.Floating && index > 0 && entry.Id != selectedId && entries[index - 1].Id != selectedId;
            var segment = new Segment(entry, number, entry.Id == selectedId, codeIds.Contains(entry.Id), divided, () => NumberWidth);
            segment.Click += () => Selected?.Invoke(entry.Id);
            segments.Add(segment);
            Children.Add(segment);
        }
        Relayout();
    }

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        if (segments.Count == 0) return;
        var width = size.Width / segments.Count;
        for (var index = 0; index < segments.Count; index++) Place(segments[index], index * width, 0, width, size.Height);
    }

    /// One question of the strip.
    public sealed class Segment : FrameView
    {
        private readonly Border highlight = new() { IsVisible = false };
        private readonly Border bar = new() { IsVisible = false };
        private readonly Border divider = new() { IsVisible = false, Background = Token.Colors.Separator.Brush() };
        private readonly Border pill = new();
        private readonly TextBlock number;
        private readonly TextBlock status;
        private readonly TextBlock relation;
        private readonly TextBlock question;
        private readonly Func<double> numberWidth;

        public event Action? Click;

        public Segment(QuestionBurstEntry entry, int questionNumber, bool selected, bool hasCode, bool divided, Func<double> numberWidth)
        {
            this.numberWidth = numberWidth;
            Entry = entry;
            Number = questionNumber;
            IsSelected = selected;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            Focusable = true;

            highlight.IsVisible = selected;
            bar.IsVisible = selected;
            Ui.Surface(highlight, Token.SurfaceStyle.Highlight);
            Ui.Surface(bar, Token.SurfaceStyle.HighlightBar);
            divider.IsVisible = divided;

            number = Ui.Label(questionNumber.ToString(), Token.Font.Body, Token.Font.Semibold, Token.Colors.TextStrong, digits: true);
            number.VerticalAlignment = VerticalAlignment.Center;

            var latency = entry.LatencyMs is { } ms ? InterviewMessage.FormatLatency(ms) : "";
            status = Ui.Overline(entry.Phase.Title(), entry.Phase.TextColor(), latency);
            status.TextAlignment = TextAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Center;
            status.IsVisible = entry.Phase.ShowsPill();
            pill.IsVisible = status.IsVisible;
            Ui.Surface(pill, Token.SurfaceStyle.Pill(entry.Phase.Hue(), Token.Sizing.Pill));

            RelationText = Relation(entry.FollowsPrevious, hasCode);
            relation = Ui.Label(RelationText, Token.Font.Caption, color: Token.Colors.TextMuted);
            relation.VerticalAlignment = VerticalAlignment.Center;
            relation.IsVisible = RelationText.Length > 0;
            Ui.Tip(relation, RelationText);

            question = Ui.Wrapping(
                entry.Question, Token.Font.Body, selected ? Token.Font.Medium : Token.Font.Regular,
                selected ? Token.Colors.TextStrong : Token.Colors.TextBody, Token.Leading.Compact, maxLines: 2);

            var took = latency.Length == 0 ? "" : $" in {latency}";
            Ui.Tip(this, $"{entry.Phase.Title()}{took}: {entry.Question}");
            var related = RelationText.Length == 0 ? "" : $", {RelationText.ToLowerInvariant()}";
            AutomationProperties.SetName(this, $"Question {questionNumber}, {entry.Phase.Title().ToLowerInvariant()}{related}: {entry.Question}");

            Children.Add(highlight);
            Children.Add(bar);
            Children.Add(divider);
            Children.Add(pill);
            Children.Add(number);
            Children.Add(status);
            Children.Add(relation);
            Children.Add(question);
        }

        public QuestionBurstEntry Entry { get; }

        public int Number { get; }

        public bool IsSelected { get; }

        public string StatusText => status.IsVisible ? status.Text ?? "" : "";

        public string RelationText { get; }

        public TextBlock QuestionLabel => question;

        protected override void LayoutFrames(Size size)
        {
            var pad = Token.Space.Inset;
            var width = size.Width;
            if (highlight.IsVisible)
            {
                var inset = Token.Selection.Inset;
                var washHeight = Math.Max(0, size.Height - inset);
                Place(highlight, inset, inset / 2, Math.Max(0, width - inset * 2), washHeight);
                var barHeight = Math.Max(0, washHeight - Token.Selection.BarVerticalInset * 2);
                Place(bar, inset + Token.Selection.BarInset, inset / 2 + (washHeight - barHeight) / 2, Token.Selection.BarWidth, barHeight);
            }
            if (divider.IsVisible) Place(divider, 0, 10, 1, Math.Max(20, size.Height - 20));

            // The first row sits 27 points down from the top; the question hangs 4 under it.
            var rowBottom = Math.Max(42, size.Height - 27);
            var rowTop = size.Height - rowBottom - 18;
            var numberW = numberWidth();
            var statusX = pad + numberW + Token.Space.Sm;
            var statusWidth = Math.Min(WidthOf(status) + 16, Math.Max(62, width - statusX - 12));
            Place(number, pad, rowTop, numberW, 18);
            if (status.IsVisible)
            {
                Place(pill, statusX, rowTop, statusWidth, Token.Sizing.Pill);
                Place(status, statusX, rowTop, statusWidth, Token.Sizing.Pill);
            }
            var relationX = status.IsVisible ? statusX + statusWidth + Token.Space.Sm : statusX;
            if (relation.IsVisible) Place(relation, relationX, rowTop, Math.Max(0, width - relationX - pad), 18);

            var pitch = Math.Round(Token.Font.Body * Token.Leading.Compact, 1);
            var questionTop = size.Height - rowBottom + 4;
            var fitting = Math.Max(1, (int)Math.Floor((size.Height - questionTop - 4 + 2) / pitch));
            question.MaxLines = Math.Min(2, fitting);
            var labelWidth = Math.Min(Math.Max(50, width - pad * 2), Token.Measure.Caption);
            Place(question, pad, questionTop, labelWidth, HeightOf(question, labelWidth));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Click?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            Click?.Invoke();
        }

        public void PerformClick() => Click?.Invoke();

        protected override AutomationPeer OnCreateAutomationPeer() => new SegmentPeer(this);

        private sealed class SegmentPeer(Segment owner) : ControlAutomationPeer(owner), Avalonia.Automation.Provider.IInvokeProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            protected override bool IsContentElementCore() => true;

            protected override bool IsControlElementCore() => true;

            public void Invoke() => owner.PerformClick();
        }
    }
}
