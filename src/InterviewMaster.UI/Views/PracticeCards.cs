using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// A practice card. The Mac lays these out from the bottom-left corner; `Put` keeps its numbers.
public abstract class PracticeCardView : FrameView
{
    private double cardHeight;

    protected void Begin(Size size) => cardHeight = size.Height;

    protected void Put(Control child, double x, double y, double width, double height) =>
        Place(child, x, cardHeight - y - Math.Max(0, height), width, height);

    protected static double TextHeight(Control label, double width, double minimum = 18) =>
        width <= 1 ? minimum : Math.Max(minimum, HeightOf(label, width));
}

/// One of the four answers on a study card.
public sealed class PracticeChoiceOption : Border
{
    private readonly TextBlock label = Ui.Wrapping("", 14, color: Token.Colors.TextBody, leading: 1.25);

    public event Action? Click;

    public PracticeChoiceOption()
    {
        label.Padding = default;
        label.Margin = new Thickness(14, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        Child = label;
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1);
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public string Title => label.Text ?? "";

    public string Status { get; private set; } = "";

    public void Show(string title, Color tint, bool selected, string status)
    {
        label.Text = title;
        Background = tint.WithAlpha(selected ? 0.19 : 0.08).Brush();
        BorderBrush = tint.Brush();
        Status = status;
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetHelpText(this, status);
    }

    public double HeightFor(double width)
    {
        label.Measure(new Size(Math.Max(1, width - 28), double.PositiveInfinity));
        return Math.Max(22, Math.Ceiling(label.DesiredSize.Height)) + 28;
    }

    public void PerformClick() => Click?.Invoke();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        BorderThickness = new Thickness(IsKeyboardFocusWithin ? 2 : 1);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        BorderThickness = new Thickness(1);
    }
}

/// A card with four answers to choose from, and what was right once one is chosen.
public sealed class PracticeMultipleChoiceCard : PracticeCardView
{
    private static readonly Color Separator = Token.Palette.White;

    private readonly Border surface = PracticeUi.Card(Color.FromRgb(28, 28, 28));
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Document document;
    private readonly TextBlock heading = PracticeUi.Label("", 12, Token.Font.Medium, Token.Palette.White.WithAlpha(0.55), maxLines: 0);
    private readonly TextBlock prompt = PracticeUi.Label("", 17, Token.Font.Medium, Token.Colors.TextBody, maxLines: 0);
    private readonly SelectableTextBlock feedbackLabel = new()
    {
        FontSize = 14,
        FontFamily = Token.Font.Family,
        Foreground = Token.Colors.TextBody.Brush(),
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(Token.Text.LabelInset, 0),
        IsVisible = false,
    };
    private readonly List<PracticeChoiceOption> options = new();
    private List<string> optionIDs = new();
    private string? questionID;
    private bool selectionLocked;

    public event Action<string>? Selected;

    public PracticeMultipleChoiceCard()
    {
        document = new Document(this);
        for (var index = 0; index < 4; index++)
        {
            var position = index;
            var option = new PracticeChoiceOption();
            option.Click += () =>
            {
                if (position < optionIDs.Count && !selectionLocked) Selected?.Invoke(optionIDs[position]);
            };
            options.Add(option);
        }
        document.Children.Add(heading);
        document.Children.Add(prompt);
        foreach (var option in options) document.Children.Add(option);
        document.Children.Add(feedbackLabel);
        scroll.Content = document;
        Previous.IsVisible = false;
        foreach (var child in new Control[] { surface, scroll, Previous, Submit }) Children.Add(child);
    }

    public string DeckTitle { get; set; } = "Claude Architect";

    public IReadOnlyList<PracticeChoiceOption> OptionButtons => options;

    public HoverButton Previous { get; } = PracticeUi.Button("Previous", Token.Palette.White);

    public HoverButton Submit { get; } = PracticeUi.Button("Next", Token.Palette.Green);

    public string FeedbackText => feedbackLabel.IsVisible ? feedbackLabel.Text ?? "" : "";

    public void Display(PracticeQuestion question, int index, int total, string? selectedOptionID, PracticeCardResult? feedback, bool allowsSelection)
    {
        if (question.Card is not { } card) return;
        var changed = questionID != question.Id;
        questionID = question.Id;
        heading.Text = $"{DeckTitle} · {index + 1} of {total} · {question.TopicTitle}";
        prompt.Text = question.Text;
        optionIDs = card.Options.Select(option => option.Id).ToList();
        selectionLocked = !allowsSelection;
        for (var i = 0; i < options.Count && i < card.Options.Count; i++)
        {
            var option = card.Options[i];
            var selected = option.Id == selectedOptionID;
            var tint = selected ? PracticeUi.Blue : Separator;
            var status = selected ? "Selected" : "Choose this answer";
            if (feedback is not null)
            {
                if (option.Id == feedback.CorrectOptionID)
                {
                    tint = Token.Palette.SystemGreen;
                    status = "Correct answer";
                }
                else if (selected)
                {
                    tint = Token.Palette.SystemRed;
                    status = "Your answer, incorrect";
                }
            }
            options[i].Show($"{(char)('A' + i)}.  {option.Text}", tint, selected, status);
            // The neutral rim is the system's hairline, not a solid white line.
            if (tint == Separator) options[i].BorderBrush = Token.Palette.White.WithAlpha(0.14).Brush();
        }
        if (feedback is not null)
        {
            var correct = feedback.SelectedOptionID == feedback.CorrectOptionID;
            feedbackLabel.Text = $"{(correct ? "Correct" : "Not quite")}\n\nAnswer: {feedback.CorrectAnswer}\n\n{feedback.Explanation}";
        }
        else
        {
            feedbackLabel.Text = "";
        }
        feedbackLabel.IsVisible = feedback is not null;
        document.Relayout();
        Relayout();
        if (changed) scroll.Offset = default;
    }

    protected override void LayoutFrames(Size size)
    {
        Begin(size);
        Place(surface, 0, 0, size.Width, size.Height);
        Put(scroll, 16, 48, Math.Max(1, size.Width - 32), Math.Max(1, size.Height - 62));
        if (Previous.IsVisible) Put(Previous, 18, 10, 86, 28);
        if (Submit.IsVisible) Put(Submit, Previous.IsVisible ? 112 : 18, 10, 86, 28);
    }

    private sealed class Document(PracticeMultipleChoiceCard card) : FrameView
    {
        protected override Size PreferredSize(Size available) => new(available.Width, Lay(available.Width, place: false));

        protected override void LayoutFrames(Size size) => Lay(size.Width, place: true);

        private double Lay(double fullWidth, bool place)
        {
            var width = Math.Max(1, fullWidth - 8);
            double y = 8;
            foreach (var label in new[] { card.heading, card.prompt })
            {
                var height = TextHeight(label, width) + 8;
                if (place) Place(label, 0, y, width, height);
                y += height + 10;
            }
            foreach (var option in card.options)
            {
                var height = option.HeightFor(width);
                if (place) Place(option, 0, y, width, height);
                y += height + 10;
            }
            if (card.feedbackLabel.IsVisible)
            {
                var height = TextHeight(card.feedbackLabel, width) + 16;
                if (place) Place(card.feedbackLabel, 0, y + 8, width, height);
                y += height + 24;
            }
            return y;
        }

        private static double TextHeight(Control label, double width) => Math.Max(18, HeightOf(label, width));
    }
}

/// The card that holds today's plan, an interview question with the answer under it, or a report.
public sealed class PracticeQuestionCard : PracticeCardView
{
    public const string IdleText = "Choose a role, topics, and session length. Active Recall lets you answer in your own words, compare key ideas, and control when each concept returns.";

    private readonly Border surface = PracticeUi.Card(Color.FromRgb(24, 24, 24), Token.Palette.White.WithAlpha(0.18));
    private readonly Border badge;
    private readonly TextBlock badgeLetter;
    private readonly TextBlock positionLabel = Ui.Label("Ready", 11, Token.Font.Medium, Token.Palette.White.WithAlpha(0.68));
    private readonly ScrollViewer questionScroll;
    private readonly SelectableTextBlock questionView;
    private readonly TextBlock answerCaption = Ui.Label("YOUR ANSWER", 9.5, FontWeight.Bold, Token.Palette.White.WithAlpha(0.72));
    private readonly ScrollViewer reportScroll;
    private readonly SelectableTextBlock reportView;
    private bool showingReport;
    private bool showingIdle = true;

    public PracticeQuestionCard()
    {
        (badge, badgeLetter) = PracticeUi.Badge(0.16);
        (questionScroll, questionView) = PracticeUi.Reading(15, Token.Palette.White.WithAlpha(0.92), inset: new Thickness(0, 2));
        (reportScroll, reportView) = PracticeUi.Reading(13, Token.Palette.White.WithAlpha(0.9), inset: new Thickness(0, 2));
        AutomationProperties.SetName(this, "Practice question card");
        AutomationProperties.SetName(questionView, "Question");
        AutomationProperties.SetName(reportView, "Practice report");
        positionLabel.TextAlignment = TextAlignment.Right;
        reportScroll.IsVisible = false;
        Resume.IsVisible = false;
        foreach (var child in new Control[] { surface, badge, TitleLabel, positionLabel, SourceLabel, questionScroll, answerCaption, AnswerView, reportScroll, Record, Submit, End, Resume, Contrast })
        {
            Children.Add(child);
        }
        ShowIdle();
    }

    public TextBlock TitleLabel { get; } = Ui.Label("Practice", 18, Token.Font.Semibold, Token.Palette.White);

    public TextBlock SourceLabel { get; } = Ui.Label("Questions come from the study book.", 11, Token.Font.Medium, Token.Palette.White.WithAlpha(0.55));

    public TextBox AnswerView { get; } = PracticeUi.Editor(15, "Your answer", new Thickness(0, 2));

    public HoverButton Record { get; } = PracticeUi.Button("Record", Token.Palette.Gold);

    public HoverButton Submit { get; } = PracticeUi.Button("Submit", Token.Palette.Green);

    public HoverButton End { get; } = PracticeUi.Button("Exit run", Token.Palette.Red);

    public HoverButton Resume { get; } = PracticeUi.Button("Resume session", PracticeUi.Blue, Icons.PlayCircle);

    public HoverButton Contrast { get; } = PracticeUi.Button("Compare concepts", Token.Palette.SystemPurple, Icons.Swap);

    public Control ReportView => reportView;

    public string BadgeText => badgeLetter.Text ?? "";

    public string PositionText => positionLabel.Text ?? "";

    public string QuestionText => questionView.Text ?? "";

    public string ReportText => reportView.Text ?? "";

    public void SetQuestionText(string text) => questionView.Text = text;

    public void ShowIdle()
    {
        showingReport = false;
        showingIdle = true;
        badgeLetter.Text = "Q";
        TitleLabel.Text = "Practice";
        positionLabel.Text = "Ready";
        SourceLabel.Text = "Questions come from the study book.";
        SetQuestionText(IdleText);
        AnswerView.Text = "";
        AnswerView.IsReadOnly = false;
        reportScroll.IsVisible = false;
        questionScroll.IsVisible = true;
        AnswerView.IsVisible = false;
        answerCaption.IsVisible = false;
        AutomationProperties.SetName(questionView, "Question");
        Relayout();
    }

    public void ShowTodayPlan(PracticeTodayPlan plan, string? resumeSummary = null)
    {
        ShowIdle();
        badgeLetter.Text = "T";
        TitleLabel.Text = "Today’s plan";
        positionLabel.Text = $"~{Math.Max(1, plan.EstimatedMinutes)} min";
        SourceLabel.Text = "Due work first, then new material";
        var lines = new List<string>
        {
            $"{Math.Max(0, plan.DueCount)} due for review",
            $"{Math.Max(0, plan.NewCount)} new questions",
            $"{Math.Max(0, plan.WeakCount)} weak concepts to repair",
        };
        if (resumeSummary?.Trim() is { Length: > 0 } resume)
        {
            lines.Add("");
            lines.Add($"Continue: {resume}");
        }
        SetQuestionText(string.Join("\n", lines));
        AutomationProperties.SetName(questionView, "Today’s practice plan");
        Relayout();
    }

    public void ShowQuestion(string topic, int index, int total, string question, string source, string answer, bool editable)
    {
        showingReport = false;
        showingIdle = false;
        badgeLetter.Text = "Q";
        TitleLabel.Text = topic;
        positionLabel.Text = $"Question {index} of {total}";
        SourceLabel.Text = source;
        AutomationProperties.SetName(this, $"Practice question {index} of {total}");
        SetQuestionText(question);
        AnswerView.Text = answer;
        AnswerView.IsReadOnly = !editable;
        AutomationProperties.SetHelpText(AnswerView, editable ? "Type or dictate your answer before submitting." : "Previously submitted answer.");
        reportScroll.IsVisible = false;
        questionScroll.IsVisible = true;
        AnswerView.IsVisible = true;
        answerCaption.IsVisible = true;
        Relayout();
    }

    public void ShowReport(string text)
    {
        showingReport = true;
        showingIdle = false;
        badgeLetter.Text = "R";
        TitleLabel.Text = "Run report";
        positionLabel.Text = "Done";
        SourceLabel.Text = "Saved to practice history";
        reportView.Text = text;
        questionScroll.IsVisible = false;
        AnswerView.IsVisible = false;
        answerCaption.IsVisible = false;
        reportScroll.IsVisible = true;
        reportScroll.Offset = default;
        Relayout();
    }

    protected override void LayoutFrames(Size size)
    {
        Begin(size);
        var (w, h) = (size.Width, size.Height);
        Place(surface, 0, 0, w, h);
        Put(badge, 18, h - 42, 24, 24);
        Put(TitleLabel, 54, h - 42, Math.Max(120, w - 230), 24);
        Put(positionLabel, w - 164, h - 39, 146, 18);
        Put(SourceLabel, 54, h - 58, Math.Max(80, w - 74), 16);

        const double buttonWidth = 86;
        const double gap = 8;
        double buttonX = 18;
        if (Record.IsVisible)
        {
            Put(Record, buttonX, 10, buttonWidth, 28);
            buttonX += buttonWidth + gap;
        }
        if (Submit.IsVisible)
        {
            Put(Submit, buttonX, 10, buttonWidth, 28);
            buttonX += buttonWidth + gap;
        }
        if (End.IsVisible) Put(End, buttonX, 10, buttonWidth, 28);
        if (Resume.IsVisible) Put(Resume, Math.Max(18, w - 168), 10, 150, 30);
        if (Contrast.IsVisible) Put(Contrast, 18, 10, 150, 30);

        if (showingReport)
        {
            Put(reportScroll, 20, 48, w - 40, Math.Max(40, h - 110));
            return;
        }
        if (showingIdle)
        {
            Put(questionScroll, 20, 18, w - 40, Math.Max(48, h - 88));
            return;
        }
        var answerHeight = Math.Min(110, Math.Max(72, h * 0.22));
        const double answerY = 52;
        Put(AnswerView, 20, answerY, w - 40, answerHeight);
        var captionY = answerY + answerHeight + 4;
        Put(answerCaption, 20, captionY, 160, 14);
        var questionBottom = captionY + 14 + 8 + 8;
        Put(questionScroll, 20, questionBottom, w - 40, Math.Max(48, h - 64 - questionBottom));
    }
}

/// One prepared idea in the review, with a mark for whether the response covered it.
public sealed class PracticeKeyIdeaRow : FrameView
{
    private readonly Border surface = new() { CornerRadius = new CornerRadius(7) };
    private readonly IconView icon = new() { Size = 14 };
    private readonly TextBlock label = PracticeUi.Label("", 13, color: Token.Palette.White.WithAlpha(0.88), maxLines: 0);
    private bool allowsToggle;

    public event Action<bool>? Toggled;

    public PracticeKeyIdeaRow()
    {
        label.Padding = default;
        label.LineHeight = 16;
        Children.Add(surface);
        Children.Add(icon);
        Children.Add(label);
    }

    public bool IsCovered { get; private set; }

    public string Text => label.Text ?? "";

    public void Configure(string text, bool covered, bool allowsToggle)
    {
        IsCovered = covered;
        this.allowsToggle = allowsToggle;
        Focusable = allowsToggle;
        Cursor = allowsToggle ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        icon.Shape = covered ? Icons.CheckCircle : Icons.Circle;
        icon.Foreground = (covered ? Token.Palette.Green : Token.Palette.Gold.WithAlpha(0.78)).Brush();
        label.Text = text;
        label.Foreground = Token.Palette.White.WithAlpha(covered ? 0.92 : 0.72).Brush();
        surface.Background = (allowsToggle ? Token.Palette.White.WithAlpha(0.025) : Token.Colors.Clear).Brush();
        AutomationProperties.SetName(this, $"{(covered ? "Covered" : "Review")}: {text}");
        AutomationProperties.SetHelpText(this, allowsToggle
            ? "Toggle whether your response covered this idea."
            : covered ? "Covered by your response." : "Review this missed idea.");
        ToolTip.SetTip(this, allowsToggle ? "Click to correct estimated coverage" : null);
    }

    public double PreferredHeight(double width)
    {
        label.Measure(new Size(Math.Max(40, width - 34), double.PositiveInfinity));
        return Math.Max(38, Math.Min(132, Math.Max(18, Math.Ceiling(label.DesiredSize.Height)) + 12));
    }

    public void PerformClick()
    {
        if (!allowsToggle) return;
        Configure(Text, !IsCovered, true);
        Toggled?.Invoke(IsCovered);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        Place(icon, 6, Math.Max(2, size.Height - 24) + 2, 14, 14);
        Place(label, 32, 4, Math.Max(40, size.Width - 36), Math.Max(18, size.Height - 8));
    }
}

/// How sure the person was before seeing the key ideas.
public sealed class PracticeConfidencePickerView : FrameView
{
    private readonly List<HoverButton> buttons = new();
    private PracticeRecallConfidence? selection;

    public event Action<PracticeRecallConfidence>? Selected;

    public PracticeConfidencePickerView()
    {
        foreach (var confidence in PracticeRecallConfidences.AllCases)
        {
            var button = new HoverButton { CornerRadius = new CornerRadius(7) };
            button.TitleSize = 11.5;
            button.TitleWeight = Token.Font.Semibold;
            PracticeUi.SetTitle(button, confidence.Title());
            button.Click += () =>
            {
                Selection = confidence;
                Selected?.Invoke(confidence);
            };
            buttons.Add(button);
            Children.Add(button);
        }
        Refresh();
    }

    public IReadOnlyList<HoverButton> Buttons => buttons;

    public PracticeRecallConfidence? Selection
    {
        get => selection;
        set
        {
            selection = value;
            Refresh();
        }
    }

    private void Refresh()
    {
        for (var index = 0; index < buttons.Count; index++)
        {
            var selected = selection is { } chosen && PracticeRecallConfidences.AllCases[index] == chosen;
            var accent = selected ? PracticeUi.Blue : Token.Palette.White;
            buttons[index].TitleColor = selected ? PracticeUi.Blue : Token.Palette.White.WithAlpha(0.72);
            buttons[index].Apply(new Token.ControlStyle(
                null, accent.WithAlpha(selected ? 0.18 : 0.06), accent.WithAlpha(selected ? 0.26 : 0.12), accent.WithAlpha(0.3),
                accent.WithAlpha(selected ? 0.7 : 0.22), accent.WithAlpha(0.7), 1));
        }
    }

    protected override void LayoutFrames(Size size)
    {
        if (buttons.Count == 0) return;
        const double gap = 6;
        var width = Math.Max(40, (size.Width - gap * (buttons.Count - 1)) / buttons.Count);
        for (var index = 0; index < buttons.Count; index++) Place(buttons[index], index * (width + gap), 0, width, size.Height);
    }
}

/// Where one missed idea is repaired in the person's own words.
public sealed class PracticeGapRepairView : PracticeCardView
{
    private readonly Border surface = new()
    {
        CornerRadius = new CornerRadius(9),
        Background = PracticeUi.Blue.WithAlpha(0.055).Brush(),
        BorderBrush = PracticeUi.Blue.WithAlpha(0.38).Brush(),
        BorderThickness = new Thickness(1),
    };
    private readonly TextBlock promptLabel = PracticeUi.Label("", 10.5, Token.Font.Medium, Token.Palette.White.WithAlpha(0.78));
    private readonly TextBlock validationLabel = Ui.Label("", 9.5, Token.Font.Medium, Token.Palette.SystemRed);

    public event Action<string>? Submitted;
    public event Action<string>? DraftChanged;

    public PracticeGapRepairView()
    {
        Save.CornerRadius = new CornerRadius(7);
        Save.TitleSize = 11;
        Save.Apply(PracticeUi.Accent(PracticeUi.Blue, border: 1));
        Save.Click += Submit;
        validationLabel.TextAlignment = TextAlignment.Right;
        validationLabel.IsVisible = false;
        TextView.TextChanged += (_, _) => DraftChanged?.Invoke(TextView.Text ?? "");
        foreach (var child in new Control[] { surface, promptLabel, TextView, validationLabel, Save }) Children.Add(child);
    }

    public TextBox TextView { get; } = PracticeUi.Editor(12.5, "Repair sentence", new Thickness(5, 4));

    public HoverButton Save { get; } = PracticeUi.Button("Save", PracticeUi.Blue);

    public string Text => (TextView.Text ?? "").Trim();

    public string ValidationMessage => validationLabel.IsVisible ? validationLabel.Text ?? "" : "";

    public void Configure(string missedIdea, string draft)
    {
        promptLabel.Text = $"Add one sentence for: {missedIdea}";
        TextView.Text = draft;
        SetValidationMessage(null);
        Relayout();
    }

    public void SetValidationMessage(string? message)
    {
        var value = message?.Trim() ?? "";
        validationLabel.Text = value;
        validationLabel.IsVisible = value.Length > 0;
    }

    private void Submit()
    {
        var value = Text;
        if (value.Length == 0)
        {
            SetValidationMessage("Write one repair sentence first.");
            return;
        }
        SetValidationMessage(null);
        Submitted?.Invoke(value);
    }

    protected override void LayoutFrames(Size size)
    {
        Begin(size);
        var (w, h) = (size.Width, size.Height);
        Place(surface, 0, 0, w, h);
        const double pad = 8;
        var buttonWidth = Math.Min(64, Math.Max(50, w * 0.2));
        var promptWidth = Math.Max(60, w - pad * 2);
        var promptHeight = Math.Min(34, PracticeUi.TextHeight(promptLabel.Text ?? "", 10.5, Token.Font.Medium, promptWidth));
        var promptY = h - pad - promptHeight;
        Put(promptLabel, pad, promptY, promptWidth, promptHeight);
        var saveX = w - pad - buttonWidth;
        var saveHeight = Math.Min(26, Math.Max(20, h - promptHeight - pad * 3));
        Put(Save, saveX, pad, buttonWidth, saveHeight);
        if (validationLabel.IsVisible) Put(validationLabel, pad, pad + saveHeight + 1, w - pad * 2, 12);
        Put(TextView, pad, pad, Math.Max(30, saveX - pad * 2), Math.Max(20, promptY - pad * 2));
    }
}

/// Active Recall: the question, the person's response, and after the reveal the key ideas beside it.
public sealed class PracticeRecallCard : PracticeCardView
{
    private const string PromptHint = "Write what you remember, then reveal the key ideas.";
    private const string ReviewHint = "When should we ask you this again?";

    private readonly Border surface = PracticeUi.Card(Color.FromRgb(24, 24, 24), Token.Palette.White.WithAlpha(0.18));
    private readonly Border badge;
    private readonly ScrollViewer questionScroll;
    private readonly SelectableTextBlock questionView;
    private readonly Border topDivider = PracticeUi.Rule();
    private readonly Border columnDivider = PracticeUi.Rule();
    private readonly Border actionDivider = PracticeUi.Rule();
    private readonly IconView responseIcon = PracticeUi.Symbol(Icons.PersonFilled, PracticeUi.Blue);
    private readonly TextBlock responseCaption = Ui.Label("YOUR RESPONSE", 10.5, FontWeight.Bold, PracticeUi.Blue);
    private readonly Border responseSurface = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
    private readonly PracticeGapRepairView repairView = new() { IsVisible = false };
    private readonly IconView keyIdeasIcon = PracticeUi.Symbol(Icons.Lightbulb, Token.Palette.Gold);
    private readonly TextBlock keyIdeasCaption = Ui.Label("KEY IDEAS", 10.5, FontWeight.Bold, Token.Palette.Gold);
    private readonly ScrollViewer keyIdeasScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly IdeaList ideaList;
    private readonly List<PracticeKeyIdeaRow> ideaRows = new();
    private readonly IconView coverageIcon = PracticeUi.Symbol(Icons.CheckCircle, Token.Palette.Green);
    private readonly TextBlock coverageLabel = PracticeUi.Label("", 11.5, Token.Font.Medium, Token.Palette.Green);
    private readonly TextBlock actionPromptLabel = PracticeUi.Label("", 11.5, Token.Font.Medium, Token.Palette.White.WithAlpha(0.72));
    private readonly PracticeConfidencePickerView confidencePicker = new() { IsVisible = false };
    private bool showingReview;
    private bool showingRepair;
    private List<string> currentIdeas = new();
    private List<bool> currentCoverage = new();
    private bool coverageIsEstimated = true;
    private bool allowsManualCoverage;

    public event Action<PracticeRecallConfidence>? ConfidenceSelected;
    public event Action<int, bool>? CoverageChanged;
    public event Action<string>? GapRepairSubmitted;
    public event Action<string>? GapRepairDraftChanged;

    public PracticeRecallCard()
    {
        (badge, _) = PracticeUi.Badge(0.1);
        (questionScroll, questionView) = PracticeUi.Reading(17, Token.Palette.White.WithAlpha(0.96), Token.Font.Medium);
        AutomationProperties.SetName(questionView, "Practice question");
        AutomationProperties.SetName(keyIdeasScroll, "Key ideas");
        AutomationProperties.SetName(coverageLabel, "Coverage summary");
        ideaList = new IdeaList(this);
        keyIdeasScroll.Content = ideaList;
        repairView.Submitted += text => GapRepairSubmitted?.Invoke(text);
        repairView.DraftChanged += text => GapRepairDraftChanged?.Invoke(text);
        confidencePicker.Selected += confidence => ConfidenceSelected?.Invoke(confidence);
        foreach (var child in new Control[]
                 {
                     surface, badge, questionScroll, topDivider, columnDivider, actionDivider, responseIcon, responseCaption, responseSurface, ResponseView,
                     repairView, keyIdeasIcon, keyIdeasCaption, keyIdeasScroll, coverageIcon, coverageLabel, actionPromptLabel, confidencePicker,
                     Reveal, Again, Hard, GotIt, Edit, DontKnow, Record,
                 })
        {
            Children.Add(child);
        }
        ShowPrompt("", "");
    }

    public TextBox ResponseView { get; } = PracticeUi.Editor(15, "Your response", new Thickness(10, 8));

    public HoverButton Reveal { get; } = PracticeUi.Button("Reveal key ideas", Token.Palette.Green, Icons.Lightbulb);

    public HoverButton Again { get; } = PracticeUi.Button("Again · now", Token.Palette.Red, Icons.Retry);

    public HoverButton Hard { get; } = PracticeUi.Button("Hard · tomorrow", Token.Palette.Gold, Icons.Trend);

    public HoverButton GotIt { get; } = PracticeUi.Button("Got it · 3 days", Token.Palette.Green, Icons.Calendar);

    public HoverButton Edit { get; } = PracticeUi.Button("Repair one gap", Token.Palette.White, Icons.Pencil);

    public HoverButton DontKnow { get; } = PracticeUi.Button("I don’t know yet", Token.Palette.Gold, Icons.QuestionCircle);

    public HoverButton Record { get; } = PracticeUi.Button("Answer aloud", Token.Palette.Gold, Icons.Microphone);

    public PracticeRecallConfidence? SelectedConfidence => confidencePicker.Selection;

    public PracticeConfidencePickerView ConfidencePicker => confidencePicker;

    public PracticeGapRepairView Repair => repairView;

    public IReadOnlyList<PracticeKeyIdeaRow> IdeaRows => ideaRows.Where(row => row.IsVisible).ToList();

    public string RepairDraft => repairView.Text;

    public TextBox GapRepairResponder => repairView.TextView;

    public string QuestionText => questionView.Text ?? "";

    public string CoverageText => coverageLabel.IsVisible ? coverageLabel.Text ?? "" : "";

    public string ActionPrompt => actionPromptLabel.Text ?? "";

    public bool IsStacked { get; private set; }

    public void ShowPrompt(string question, string response, PracticeRecallConfidence? confidence = null, bool showsConfidencePicker = false)
    {
        showingReview = false;
        showingRepair = false;
        questionView.Text = question;
        ResponseView.Text = response;
        ResponseView.IsReadOnly = false;
        AutomationProperties.SetHelpText(ResponseView, "Write what you remember before revealing the key ideas.");
        responseSurface.Background = Token.Palette.White.WithAlpha(0.035).Brush();
        responseSurface.BorderBrush = PracticeUi.Blue.WithAlpha(0.4).Brush();
        actionPromptLabel.Text = PromptHint;
        confidencePicker.Selection = confidence;
        confidencePicker.IsVisible = showsConfidencePicker;
        repairView.IsVisible = false;
        SetReviewElementsHidden(true);
        AutomationProperties.SetName(this, "Active Recall question");
        Relayout();
    }

    public void ShowReview(string question, string response, PracticeRecallReview review, bool coverageIsEstimated = true, bool allowsManualCoverage = false)
    {
        showingReview = true;
        showingRepair = false;
        this.coverageIsEstimated = coverageIsEstimated;
        this.allowsManualCoverage = allowsManualCoverage;
        questionView.Text = question;
        ResponseView.Text = response;
        ResponseView.IsReadOnly = true;
        AutomationProperties.SetHelpText(ResponseView, "Your original response. Compare it with the key ideas.");
        responseSurface.Background = Token.Colors.Clear.Brush();
        responseSurface.BorderBrush = Token.Colors.Clear.Brush();
        repairView.IsVisible = false;
        confidencePicker.IsVisible = false;
        ConfigureKeyIdeas(review.KeyIdeas, review.Covered);
        actionPromptLabel.Text = ReviewHint;
        SetReviewElementsHidden(false);
        AutomationProperties.SetName(this, "Active Recall review");
        Relayout();
    }

    public void SetCoverage(bool covered, int index, bool notify = false)
    {
        if (index < 0 || index >= currentCoverage.Count || index >= ideaRows.Count) return;
        currentCoverage[index] = covered;
        ideaRows[index].Configure(currentIdeas[index], covered, allowsManualCoverage);
        UpdateCoverageSummary();
        if (notify) CoverageChanged?.Invoke(index, covered);
    }

    public void BeginGapRepair(string missedIdea, string draft = "")
    {
        if (!showingReview) return;
        showingRepair = true;
        repairView.Configure(missedIdea, draft);
        repairView.IsVisible = true;
        actionPromptLabel.Text = "Repair one missed idea, then choose when to review again.";
        Relayout();
    }

    public void EndGapRepair()
    {
        showingRepair = false;
        repairView.IsVisible = false;
        actionPromptLabel.Text = showingReview ? ReviewHint : PromptHint;
        Relayout();
    }

    public void SetGapRepairValidationMessage(string? message)
    {
        repairView.SetValidationMessage(message);
        repairView.Relayout();
    }

    private void ConfigureKeyIdeas(IReadOnlyList<string> ideas, IReadOnlyList<bool> covered)
    {
        currentIdeas = ideas.ToList();
        currentCoverage = Enumerable.Range(0, ideas.Count).Select(index => index < covered.Count && covered[index]).ToList();
        while (ideaRows.Count < ideas.Count)
        {
            var index = ideaRows.Count;
            var row = new PracticeKeyIdeaRow();
            row.Toggled += value => SetCoverage(value, index, notify: true);
            ideaRows.Add(row);
            ideaList.Children.Add(row);
        }
        for (var index = 0; index < ideaRows.Count; index++)
        {
            ideaRows[index].IsVisible = index < ideas.Count;
            if (index < ideas.Count) ideaRows[index].Configure(ideas[index], currentCoverage[index], allowsManualCoverage);
        }
        UpdateCoverageSummary();
        ideaList.Relayout();
    }

    private void UpdateCoverageSummary()
    {
        if (currentIdeas.Count == 0)
        {
            coverageLabel.Text = "No prepared key ideas are available for this question.";
            coverageIcon.Shape = Icons.Info;
            coverageIcon.Foreground = Token.Palette.Gold.Brush();
            coverageLabel.Foreground = Token.Palette.Gold.Brush();
            return;
        }
        var prefix = coverageIsEstimated ? "Estimated: " : "";
        var correction = allowsManualCoverage ? " Select an idea to correct it." : "";
        coverageLabel.Text = $"{prefix}{currentCoverage.Count(covered => covered)} of {currentIdeas.Count} key ideas covered.{correction}";
        coverageIcon.Shape = Icons.CheckCircle;
        coverageIcon.Foreground = Token.Palette.Green.Brush();
        coverageLabel.Foreground = Token.Palette.Green.Brush();
    }

    private void SetReviewElementsHidden(bool hidden)
    {
        foreach (var part in new Control[] { columnDivider, keyIdeasIcon, keyIdeasCaption, keyIdeasScroll, coverageIcon, coverageLabel }) part.IsVisible = !hidden;
        for (var index = 0; index < ideaRows.Count; index++) ideaRows[index].IsVisible = !hidden && index < currentIdeas.Count;
    }

    protected override void LayoutFrames(Size size)
    {
        Begin(size);
        var (w, h) = (size.Width, size.Height);
        Place(surface, 0, 0, w, h);
        const double pad = 20;
        if (w <= pad * 2 || h <= 160) return;

        var questionWidth = Math.Max(120, w - pad * 2 - 40);
        var naturalQuestionHeight = TextHeight(questionView, questionWidth);
        var maximumQuestionHeight = Math.Min(120, Math.Max(54, h * 0.3));
        var questionHeight = Math.Min(maximumQuestionHeight, Math.Max(42, naturalQuestionHeight + 4));
        var questionY = h - pad - questionHeight;
        Put(badge, pad, h - pad - 24, 24, 24);
        Put(questionScroll, pad + 40, questionY, questionWidth, questionHeight);
        var topDividerY = questionY - 10;
        Put(topDivider, pad, topDividerY, Math.Max(0, w - pad * 2), 1);

        const double reserve = 46;
        var across = Math.Max(80, w - pad * 2);
        double contentBottom;
        if (showingReview)
        {
            Put(actionPromptLabel, pad, reserve + 5, across, 28);
            Put(coverageIcon, pad, reserve + 39, 16, 16);
            Put(coverageLabel, pad + 24, reserve + 35, Math.Max(80, w - pad * 2 - 24), 30);
            Put(actionDivider, pad, reserve + 70, Math.Max(0, w - pad * 2), 1);
            contentBottom = reserve + 80;
        }
        else if (confidencePicker.IsVisible)
        {
            Put(confidencePicker, pad, reserve + 4, Math.Min(300, Math.Max(180, w - pad * 2)), 30);
            Put(actionPromptLabel, pad, reserve + 39, across, 28);
            Put(actionDivider, pad, reserve + 72, Math.Max(0, w - pad * 2), 1);
            contentBottom = reserve + 81;
        }
        else
        {
            Put(actionPromptLabel, pad, reserve + 8, across, 28);
            Put(actionDivider, pad, reserve + 40, Math.Max(0, w - pad * 2), 1);
            contentBottom = reserve + 50;
        }

        var contentTop = topDividerY - 12;
        var contentHeight = Math.Max(54, contentTop - contentBottom);
        IsStacked = showingReview && w < 620;
        if (showingReview)
        {
            if (IsStacked) LayoutStackedReview(pad, contentBottom, w - pad * 2, contentHeight);
            else LayoutColumnReview(pad, contentBottom, w - pad * 2, contentHeight);
        }
        else
        {
            Put(responseIcon, pad, contentTop - 20, 16, 16);
            Put(responseCaption, pad + 24, contentTop - 20, Math.Max(80, w - pad * 2 - 24), 16);
            PutResponse(pad, contentBottom, Math.Max(120, w - pad * 2), Math.Max(42, contentHeight - 30));
        }

        const double side = 18;
        const double gap = 7;
        if (Reveal.IsVisible)
        {
            Put(Reveal, Math.Max(side, w - side - 148), 10, 148, 30);
            Put(DontKnow, side, 10, 142, 30);
            Put(Record, side + 142 + gap, 10, 136, 30);
        }
        if (Again.IsVisible)
        {
            var available = Math.Max(360, w - side * 2 - gap * 3);
            var againWidth = Math.Floor(available * 0.21);
            var hardWidth = Math.Floor(available * 0.25);
            var gotWidth = Math.Floor(available * 0.28);
            var editWidth = available - againWidth - hardWidth - gotWidth;
            var x = side;
            Put(Again, x, 10, againWidth, 30);
            x += againWidth + gap;
            Put(Hard, x, 10, hardWidth, 30);
            x += hardWidth + gap;
            Put(GotIt, x, 10, gotWidth, 30);
            x += gotWidth + gap;
            Put(Edit, x, 10, editWidth, 30);
        }
    }

    private void LayoutColumnReview(double x, double y, double width, double height)
    {
        const double columnGap = 25;
        var columnWidth = Math.Max(120, Math.Floor((width - columnGap) / 2));
        var rightX = x + columnWidth + columnGap;
        var headerY = y + height - 20;
        var bodyHeight = Math.Max(32, height - 30);
        Put(responseIcon, x, headerY, 16, 16);
        Put(responseCaption, x + 24, headerY, Math.Max(60, columnWidth - 24), 16);
        LayoutResponseArea(x, y, columnWidth, bodyHeight);
        Put(columnDivider, x + columnWidth + Math.Floor(columnGap / 2), y, 1, height);
        Put(keyIdeasIcon, rightX, headerY, 16, 16);
        Put(keyIdeasCaption, rightX + 24, headerY, Math.Max(60, columnWidth - 24), 16);
        Put(keyIdeasScroll, rightX, y, columnWidth, bodyHeight);
    }

    private void LayoutStackedReview(double x, double y, double width, double height)
    {
        const double fixedHeight = 65;
        var bodySpace = Math.Max(48, height - fixedHeight);
        var responseBodyHeight = Math.Max(24, Math.Floor(bodySpace * (showingRepair ? 0.58 : 0.48)));
        var keyBodyHeight = Math.Max(24, bodySpace - responseBodyHeight);
        Put(keyIdeasScroll, x, y, width, keyBodyHeight);
        var keyTop = y + keyBodyHeight;
        Put(keyIdeasIcon, x, keyTop + 5, 16, 16);
        Put(keyIdeasCaption, x + 24, keyTop + 5, Math.Max(60, width - 24), 16);
        var dividerY = keyTop + 5 + 16 + 7;
        Put(columnDivider, x, dividerY, width, 1);
        var responseY = dividerY + 8;
        LayoutResponseArea(x, responseY, width, responseBodyHeight);
        Put(responseIcon, x, responseY + responseBodyHeight + 5, 16, 16);
        Put(responseCaption, x + 24, responseY + responseBodyHeight + 5, Math.Max(60, width - 24), 16);
    }

    private void LayoutResponseArea(double x, double y, double width, double height)
    {
        if (!showingRepair)
        {
            PutResponse(x, y, width, height);
            return;
        }
        if (height < 92)
        {
            responseSurface.IsVisible = false;
            ResponseView.IsVisible = false;
            Put(repairView, x, y, width, height);
            return;
        }
        var repairHeight = Math.Max(50, Math.Min(108, Math.Floor(height * 0.56)));
        Put(repairView, x, y, width, repairHeight);
        PutResponse(x, y + repairHeight + 7, width, Math.Max(28, height - repairHeight - 7));
    }

    private void PutResponse(double x, double y, double width, double height)
    {
        responseSurface.IsVisible = true;
        ResponseView.IsVisible = true;
        Put(responseSurface, x, y, width, height);
        Put(ResponseView, x, y, width, height);
    }

    private sealed class IdeaList(PracticeRecallCard card) : FrameView
    {
        protected override Size PreferredSize(Size available) => new(available.Width, Lay(available.Width, place: false));

        protected override void LayoutFrames(Size size) => Lay(size.Width, place: true);

        private double Lay(double fullWidth, bool place)
        {
            var width = Math.Max(1, fullWidth);
            double y = 0;
            for (var index = 0; index < card.ideaRows.Count && index < card.currentIdeas.Count; index++)
            {
                var row = card.ideaRows[index];
                var height = row.PreferredHeight(width);
                if (place) Place(row, 0, y, width, height);
                y += height + 6;
            }
            return Math.Max(0, y - 6);
        }
    }
}
