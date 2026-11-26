using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// The practice tab: what to practise at the top, then the card for the plan, the question or the
/// report. A port of the views `PracticeTabController.swift` builds and lays out.
public sealed class PracticeView : FrameView
{
    private readonly AppIconView appIcon = new() { IsVisible = false };
    private readonly TextBlock appTitle = Ui.Label("Interview Master", 17, Token.Font.Semibold, Token.Palette.White);
    private readonly TextBlock header = Ui.Label("Practice", 20, Token.Font.Semibold, Token.Palette.White);
    private readonly TextBlock subtitle = PracticeUi.Label("", 12, color: Token.Palette.White.WithAlpha(0.86));
    private readonly TextBlock modePillText = Ui.Label("ACTIVE RECALL", 10, FontWeight.Bold, PracticeUi.Blue);
    private readonly Border modePill = new()
    {
        CornerRadius = new CornerRadius(6),
        Background = PracticeUi.Blue.WithAlpha(0.08).Brush(),
        BorderBrush = PracticeUi.Blue.WithAlpha(0.5).Brush(),
        BorderThickness = new Thickness(1),
        IsVisible = false,
    };
    private readonly TextBlock roleCaption = Caption("ROLE");
    private readonly TextBlock modeCaption = Caption("MODE");
    private readonly TextBlock countCaption = Caption("QUESTIONS");
    private readonly TopicRowView topicRow = new();
    private readonly TextBlock status = PracticeUi.Label("Pick topics and a count. Live listen stays on Timeline.", 11, color: Token.Palette.White.WithAlpha(0.78));
    private readonly Border historyCard = PracticeUi.Card(Color.FromRgb(19, 19, 20), Token.Palette.White.WithAlpha(0.12));
    private readonly TextBlock historyLabel = PracticeUi.Label("History", 12, color: Token.Palette.White.WithAlpha(0.85), maxLines: 12);

    public PracticeView()
    {
        appTitle.IsVisible = false;
        modePillText.Padding = default;
        modePillText.HorizontalAlignment = HorizontalAlignment.Center;
        modePillText.VerticalAlignment = VerticalAlignment.Center;
        modePill.Child = modePillText;
        Progress.IsVisible = false;
        EndHeader.IsVisible = false;
        Undo.IsVisible = false;
        RecallCard.IsVisible = false;
        ChoiceCard.IsVisible = false;
        historyCard.IsVisible = false;
        TargetDatePicker.IsEnabled = false;
        ToolTip.SetTip(Role, "Interview role");
        ToolTip.SetTip(Mode, "Practice mode");
        ToolTip.SetTip(Count, "Question count");
        AutomationProperties.SetName(status, "Practice status");
        foreach (var child in new Control[]
                 {
                     appIcon, appTitle, header, History, Leave, EndHeader, subtitle, modePill, Progress, roleCaption, modeCaption, countCaption,
                     Role, Mode, Count, topicRow, TargetDateToggle, TargetDatePicker, Start, status, QuestionCard, RecallCard, ChoiceCard, Undo,
                     historyCard, historyLabel, PracticeWeak,
                 })
        {
            Children.Add(child);
        }
        Refresh();
    }

    public HoverButton History { get; } = PracticeUi.Button("History", Token.Palette.Gold);

    public HoverButton Leave { get; } = PracticeUi.Button("Timeline", Token.Palette.White);

    public HoverButton EndHeader { get; } = PracticeUi.Button("Exit", Token.Palette.Red);

    public HoverButton Start { get; } = PracticeUi.Button("Start practice", Token.Palette.Green);

    public HoverButton Undo { get; } = PracticeUi.Button("Undo last", Token.Palette.Gold, Icons.Undo);

    public HoverButton PracticeWeak { get; } = PracticeUi.Button("Practice 5 misses", PracticeUi.Blue, Icons.Switching);

    public ComboBox Role { get; } = Dropdown("Role");

    public ComboBox Mode { get; } = Dropdown("Mode");

    public ComboBox Count { get; } = Dropdown("Questions");

    public PracticeCheckBox TargetDateToggle { get; } = new("Interview on", 10.5);

    public PracticeDateStepper TargetDatePicker { get; } = new();

    public PracticeProgressSegmentsView Progress { get; } = new();

    public PracticeQuestionCard QuestionCard { get; } = new();

    public PracticeRecallCard RecallCard { get; } = new();

    public PracticeMultipleChoiceCard ChoiceCard { get; } = new();

    public IReadOnlyList<PracticeCheckBox> TopicButtons => topicRow.Buttons;

    public bool InSession { get; set; }

    public bool CardMode { get; set; }

    public bool HistoryVisible { get; set; }

    public bool UndoAvailable { get; set; }

    public string StatusText
    {
        get => status.Text ?? "";
        set => status.Text = value;
    }

    public string? StatusTip
    {
        get => ToolTip.GetTip(status) as string;
        set => ToolTip.SetTip(status, value);
    }

    public string SubtitleText
    {
        get => subtitle.Text ?? "";
        set => subtitle.Text = value;
    }

    public string ModePillText
    {
        get => modePillText.Text ?? "";
        set => modePillText.Text = value;
    }

    public string HistoryText
    {
        get => historyLabel.Text ?? "";
        set => historyLabel.Text = value;
    }

    public void SetTopics(IEnumerable<(string Id, string Title, bool On)> topics, Action<PracticeCheckBox> toggled) => topicRow.Set(topics, toggled);

    /// Shows what belongs to the state the tab is in, then lays it out.
    public void Refresh()
    {
        var idle = !InSession;
        appIcon.IsVisible = InSession;
        appTitle.IsVisible = InSession;
        subtitle.IsVisible = idle;
        foreach (var control in new Control[] { Role, Mode, Count, roleCaption, modeCaption, countCaption, topicRow, Start, History }) control.IsVisible = idle;
        TargetDateToggle.IsVisible = idle && !CardMode;
        TargetDatePicker.IsVisible = idle && !CardMode;
        modePill.IsVisible = InSession;
        EndHeader.IsVisible = InSession;
        Undo.IsVisible = InSession && UndoAvailable;
        Progress.IsVisible = InSession;
        status.Foreground = Token.Palette.White.WithAlpha(InSession ? 0.84 : 0.78).Brush();
        var showsHistory = idle && HistoryVisible;
        historyCard.IsVisible = showsHistory;
        historyLabel.IsVisible = showsHistory;
        PracticeWeak.IsVisible = showsHistory;
        Relayout();
        QuestionCard.Relayout();
        RecallCard.Relayout();
        ChoiceCard.Relayout();
    }

    private static TextBlock Caption(string text)
    {
        var caption = Ui.Label(text, 9.5, FontWeight.Bold, Token.Palette.White.WithAlpha(0.78));
        caption.TextTrimming = TextTrimming.None;
        return caption;
    }

    /// A pop-up as the Mac draws it in a dark window: a quiet fill and no rim.
    private static ComboBox Dropdown(string name)
    {
        var dropdown = Ui.Named(new ComboBox
        {
            FontSize = 13,
            FontWeight = Token.Font.Medium,
            FontFamily = Token.Font.Family,
            MinHeight = 0,
            Padding = new Thickness(10, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
        }, name);
        foreach (var (state, alpha) in new[] { ("", 0.1), ("PointerOver", 0.15), ("Pressed", 0.19), ("Disabled", 0.06), ("Unfocused", 0.1) })
        {
            dropdown.Resources[$"ComboBoxBackground{state}"] = Token.Palette.White.WithAlpha(alpha).Brush();
            if (state != "Unfocused") dropdown.Resources[$"ComboBoxBorderBrush{state}"] = Token.Colors.Clear.Brush();
        }
        dropdown.Resources["ComboBoxBorderThemeThickness"] = new Thickness(0);
        return dropdown;
    }

    protected override void LayoutFrames(Size size)
    {
        var (w, h) = (size.Width, size.Height);
        var leaveX = Math.Max(300, w - 118);
        Rect card;
        if (InSession)
        {
            Place(appIcon, 18, 8, 28, 28);
            Place(appTitle, 56, 12, 190, 22);
            Place(header, 18, 46, 120, 26);
            Place(Leave, leaveX, 8, 100, 28);
            Place(modePill, 140, 48, 112, 22);
            Place(EndHeader, leaveX - 96, 8, 88, 28);
            Place(Undo, leaveX - 192, 8, 88, 28);
            Place(status, 18, 78, Math.Max(170, w - 310), 18);
            Place(Progress, Math.Max(300, w - 248), 83, 230, 8);
            card = new Rect(16, 104, w - 32, Math.Max(280, h - 104 - 16));
        }
        else
        {
            Place(header, 18, 12, 120, 26);
            Place(Leave, leaveX, 12, 100, 28);
            Place(subtitle, 18, 38, w - 36, 18);
            Place(History, leaveX - 108, 12, 100, 28);
            const double controlsY = 72;
            var roleWidth = Math.Min(180, Math.Max(140, w * 0.22));
            Place(Role, 18, controlsY, roleWidth, 26);
            var modeX = 18 + roleWidth + 8;
            Place(Mode, modeX, controlsY, 130, 26);
            var countX = modeX + 130 + 8;
            Place(Count, countX, controlsY, 72, 26);
            Place(Start, countX + 72 + 8, controlsY - 2, 120, 30);
            Place(roleCaption, 18, 58, roleWidth, 12);
            Place(modeCaption, modeX, 58, 130, 12);
            Place(countCaption, countX, 58, 72, 12);
            var chipHeight = topicRow.HeightFor(w - 36);
            Place(topicRow, 18, 106, w - 36, chipHeight);
            var rowBottom = 106 + chipHeight;
            Place(TargetDateToggle, 18, rowBottom + 3, 92, 22);
            const double pickerRight = 18 + 92 + 4 + 118;
            Place(TargetDatePicker, 18 + 92 + 4, rowBottom + 2, 118, 24);
            Place(status, pickerRight + 10, rowBottom + 4, Math.Max(100, w - pickerRight - 28), 20);
            var cardTop = rowBottom + 4 + 20 + 8;
            card = new Rect(16, cardTop, w - 32, Math.Max(140, h - cardTop - 16));
        }
        Place(QuestionCard, card);
        Place(RecallCard, card);
        Place(ChoiceCard, card);

        var historyHeight = Math.Min(250, Math.Max(160, h * 0.42));
        var historyTop = h - historyHeight - 12;
        var historyWidth = w - 32;
        Place(historyCard, 16, historyTop, historyWidth, historyHeight);
        Place(historyLabel, 16 + 14, historyTop + 10, historyWidth - 28, historyHeight - 56);
        Place(PracticeWeak, 16 + Math.Max(14, historyWidth - 164), historyTop + historyHeight - 38, 150, 28);
    }

    /// The topics of the chosen role as tick boxes that wrap onto as many rows as they need.
    private sealed class TopicRowView : FrameView
    {
        private readonly List<PracticeCheckBox> buttons = new();

        public IReadOnlyList<PracticeCheckBox> Buttons => buttons;

        public void Set(IEnumerable<(string Id, string Title, bool On)> topics, Action<PracticeCheckBox> toggled)
        {
            foreach (var button in buttons) Children.Remove(button);
            buttons.Clear();
            foreach (var (id, title, on) in topics)
            {
                var button = new PracticeCheckBox(title, 11.5, id) { IsChecked = on };
                button.Toggled += () => toggled(button);
                buttons.Add(button);
                Children.Add(button);
            }
            Relayout();
        }

        public double HeightFor(double width) => Lay(width, place: false);

        protected override void LayoutFrames(Size size) => Lay(size.Width, place: true);

        private double Lay(double width, bool place)
        {
            double x = 0;
            double y = 0;
            const double height = 28;
            const double gap = 8;
            foreach (var button in buttons)
            {
                var chipWidth = Math.Min(width, Math.Max(70, button.NaturalWidth + 8));
                if (x + chipWidth > width && x > 0)
                {
                    x = 0;
                    y += height + 6;
                }
                if (place) Place(button, x, y, chipWidth, height);
                x += chipWidth + gap;
            }
            return y + height;
        }
    }
}
