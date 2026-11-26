using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace InterviewMaster.Presentation;

/// The live interview view, top to bottom: the title row, the line to say over the code, the CV strip
/// and the answer (or the code), and the last few questions. The answer takes whatever the others
/// leave, because the answer is what is read.
public sealed class InterviewFocusView : FrameView
{
    private readonly ScrollViewer bodyScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Body body;
    private bool codeOpen;
    private bool answerOverCode;
    private int displayedQuestions;
    private double barTop;

    public InterviewFocusView()
    {
        body = new Body(this);
        bodyScroll.Content = body;
        TalkStrip.IsVisible = false;
        Strip.IsVisible = false;
        CodePanel.IsVisible = false;
        Children.Add(Header);
        Children.Add(TalkStrip);
        Children.Add(bodyScroll);
        Children.Add(CodePanel);
        Children.Add(Strip);
    }

    public FocusHeaderView Header { get; } = new();
    public CodeTalkStripView TalkStrip { get; } = new();
    public BriefingView Briefing { get; } = new();
    public FocusAnswerView Answer { get; } = new();
    public QuestionStripView Strip { get; } = new();
    public CodePanelView CodePanel { get; } = new();
    public ScrollViewer BodyScroll => bodyScroll;

    /// Whether the code panel has the body's place.
    public bool CodeOpen
    {
        get => codeOpen;
        set => Set(ref codeOpen, value);
    }

    /// Whether the full answer is shown in the code's place for the moment.
    public bool AnswerOverCode
    {
        get => answerOverCode;
        set => Set(ref answerOverCode, value);
    }

    /// How many questions the strip would show. It only appears for two or more.
    public int DisplayedQuestions
    {
        get => displayedQuestions;
        set => Set(ref displayedQuestions, value);
    }

    /// Where the command bar's top edge is, in this view's coordinates.
    public double BarTop
    {
        get => barTop;
        set => Set(ref barTop, value);
    }

    public bool ReadingAnswer => !codeOpen || answerOverCode;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Relayout();
    }

    public void RefreshBody()
    {
        body.Relayout();
        Relayout();
    }

    /// The most the CV strip may take of a body this tall: two fifths, and never the answer's minimum.
    public static double BriefingBudget(double bodyHeight)
    {
        var leavingAnswer = bodyHeight - Token.FocusLayout.BriefingGap - Token.FocusLayout.MinimumAnswerHeight;
        return Math.Max(Token.FocusLayout.BriefingMinimum, Math.Min(Math.Floor(bodyHeight * 0.4), leavingAnswer));
    }

    protected override void LayoutFrames(Size size)
    {
        var x = Token.FocusLayout.WindowInset + Token.Space.Inset;
        var width = size.Width - x * 2;
        var top = Token.FocusLayout.WindowInset + Token.FocusLayout.ContentTop;
        var bottom = (barTop > 0 ? barTop : size.Height) - Token.FocusLayout.StripToBar;

        Place(Header, x, Token.Header.CenterY - Token.Header.Height / 2, width, Token.Header.Height);

        var bodyTop = codeOpen ? top + Token.FocusLayout.TalkStripHeight + Token.FocusLayout.TalkStripGap : top;
        var hasStrip = ReadingAnswer && displayedQuestions > 1;
        Strip.IsVisible = hasStrip;
        bodyScroll.IsVisible = ReadingAnswer;
        TalkStrip.IsVisible = codeOpen;
        CodePanel.IsVisible = codeOpen && !answerOverCode;
        if (codeOpen) Place(TalkStrip, x, top, width, Token.FocusLayout.TalkStripHeight);

        var stripHeight = hasStrip ? Token.FocusLayout.BurstHeight : 0;
        var bodyBottom = bottom - (hasStrip ? stripHeight + Token.FocusLayout.CardToStrip : 0);
        if (hasStrip) Place(Strip, x, bottom - stripHeight, width, stripHeight);
        if (bodyScroll.IsVisible)
        {
            var bodyHeight = Math.Max(Token.FocusLayout.MinimumCardHeight, bodyBottom - bodyTop);
            body.ViewportHeight = bodyHeight;
            Place(bodyScroll, x, bodyTop, width, bodyHeight);
        }
        if (CodePanel.IsVisible) Place(CodePanel, x, bodyTop, width, Math.Max(160, bottom - bodyTop));
    }

    /// The CV strip over the answer. It scrolls only when the window is too short for the answer's minimum.
    private sealed class Body : FrameView
    {
        private readonly InterviewFocusView owner;

        public Body(InterviewFocusView owner)
        {
            this.owner = owner;
            Children.Add(owner.Briefing);
            Children.Add(owner.Answer);
        }

        public double ViewportHeight { get; set; }

        private (double Briefing, double Spacing, double Answer) Heights(double width)
        {
            var briefing = owner.Briefing.IsVisible ? owner.Briefing.PreferredHeight(width, BriefingBudget(ViewportHeight)) : 0;
            var spacing = briefing > 0 ? Token.FocusLayout.BriefingGap : 0;
            return (briefing, spacing, Math.Max(Token.FocusLayout.MinimumAnswerHeight, ViewportHeight - briefing - spacing));
        }

        protected override Size PreferredSize(Size available)
        {
            var (briefing, spacing, answer) = Heights(available.Width);
            return new Size(available.Width, Math.Max(briefing + spacing + answer, ViewportHeight));
        }

        protected override void LayoutFrames(Size size)
        {
            var (briefing, spacing, answer) = Heights(size.Width);
            if (owner.Briefing.IsVisible) Place(owner.Briefing, 0, 0, size.Width, briefing);
            Place(owner.Answer, 0, briefing + spacing, size.Width, answer);
        }
    }
}
