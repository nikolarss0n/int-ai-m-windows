using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The Settings window: who the interview is for, the CV, and the two keys that can be typed in.
/// Frames are the Mac window's, counted from the top.
public sealed class SettingsView : FrameView
{
    public const double PanelWidth = 500;
    public const double PanelHeight = 530;

    private const double Margin = 20;
    private const double FieldRight = 475;
    private static readonly string[] Captions =
    {
        "Position/Role:", "Programming Language:", "Listening Language:", "Response Language:", "Tech Stack:", "CV:",
        "Anthropic API Key:", "Groq API Key:",
    };

    /// The Mac starts its fields at 165. A font that sets the longest caption wider moves them right,
    /// so the caption is never cut by the field beside it.
    private static double FieldX => Math.Max(165, Margin + Math.Ceiling(Captions.Max(caption => Ui.TextWidth(caption, 13))) + 8);
    private static double FieldWidth => FieldRight - FieldX;
    private static double LabelWidth => FieldX - Margin;

    /// The button Enter presses: filled in the accent, whatever the main window's transparency is.
    internal static readonly Token.ControlStyle DefaultAction = new(
        null, Token.Hue.Accent.Tint(Token.Tint.Strong), Token.Palette.Magenta.WithAlpha(0.6), Token.Palette.Magenta.WithAlpha(0.75),
        Token.Colors.Clear, Token.Colors.Clear, 0);

    private readonly Border backdrop = new() { Background = Token.Colors.BgSolidWindow.Brush() };
    private readonly Border divider = new() { Background = Token.Colors.Separator.Brush() };
    private readonly List<(Control Control, Rect Frame)> placed = new();
    private readonly TextBlock resumeStatus = Ui.Label("", 13, color: Token.Colors.TextPrimary);

    public SettingsView()
    {
        Children.Add(backdrop);
        Children.Add(divider);

        Add(Header("Interview Settings"), Margin, 30, 200, 20);
        Add(Caption("Position/Role:"), Margin, 65, LabelWidth, 20);
        Add(Role, FieldX, 61, FieldWidth, 26);
        Add(Caption("Programming Language:"), Margin, 100, LabelWidth, 20);
        Add(Language, FieldX, 96, FieldWidth, 26);
        Add(Caption("Listening Language:"), Margin, 135, LabelWidth, 20);
        Add(Listening, FieldX, 131, FieldWidth, 26);
        Add(Caption("Response Language:"), Margin, 170, LabelWidth, 20);
        Add(Response, FieldX, 166, FieldWidth, 26);
        Add(Caption("Tech Stack:"), Margin, 205, LabelWidth, 20);
        Add(Frameworks, FieldX, 203, FieldWidth, 24);
        Add(Help("Comma-separated list of frameworks and tools you use", 10), FieldX, 229, FieldWidth, 16);

        Add(Caption("CV:"), Margin, 260, LabelWidth, 20);
        resumeStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        Add(resumeStatus, FieldX, 260, FieldRight - 140 - FieldX, 20);
        Add(Attach, FieldRight - 135, 258, 72, 26);
        Add(Clear, FieldRight - 62, 258, 62, 26);

        Add(Header("API Keys"), Margin, 320, 200, 20);
        Add(Caption("Anthropic API Key:"), Margin, 355, LabelWidth, 20);
        Add(AnthropicKey, FieldX, 353, FieldWidth, 24);
        Add(Caption("Groq API Key:"), Margin, 390, LabelWidth, 20);
        Add(GroqKey, FieldX, 388, FieldWidth, 24);
        var keysHelp = Help(PlatformCopy.KeysSettingsHelp, 11);
        keysHelp.TextWrapping = TextWrapping.Wrap;
        Add(keysHelp, Margin, 420, 460, 34);

        Add(Cancel, 305, 485, 80, 30);
        Add(Save, 395, 485, 80, 30);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Cancel.PerformClick();
            }
            else if (e.Key == Key.Enter && !Role.IsDropDownOpen && !Language.IsDropDownOpen && !Listening.IsDropDownOpen && !Response.IsDropDownOpen)
            {
                e.Handled = true;
                Save.PerformClick();
            }
        };
    }

    public ComboBox Role { get; } = Dropdown("Position or role");
    public ComboBox Language { get; } = Dropdown("Programming language");
    public ComboBox Listening { get; } = Dropdown("Listening language");
    public ComboBox Response { get; } = Dropdown("Response language");
    public TextBox Frameworks { get; } = Field("Playwright, Pytest, AWS, React...", "Tech stack");
    public TextBox AnthropicKey { get; } = Field("sk-ant-...", "Anthropic API key", secret: true);
    public TextBox GroqKey { get; } = Field("gsk_...", "Groq API key", secret: true);
    public HoverButton Attach { get; } = Action("Attach…");
    public HoverButton Clear { get; } = Action("Clear");
    public HoverButton Cancel { get; } = Action("Cancel");
    public HoverButton Save { get; } = Action("Save", DefaultAction, Token.Palette.White);

    public string ResumeStatus
    {
        get => resumeStatus.Text ?? "";
        set => resumeStatus.Text = value;
    }

    public static void Fill(ComboBox dropdown, IEnumerable<string> titles, string selected)
    {
        var items = titles.ToList();
        dropdown.ItemsSource = items;
        dropdown.SelectedIndex = Math.Max(0, items.IndexOf(selected));
    }

    private void Add(Control control, double x, double y, double width, double height)
    {
        placed.Add((control, new Rect(x, y, width, height)));
        Children.Add(control);
    }

    private static TextBlock Header(string text) => Ui.Label(text, 13, FontWeight.Bold, Token.Colors.TextPrimary);

    private static TextBlock Caption(string text)
    {
        var caption = Ui.Label(text, 13, color: Token.Colors.TextPrimary);
        caption.TextTrimming = TextTrimming.None;
        caption.Padding = default;
        return caption;
    }

    private static TextBlock Help(string text, double size) => Ui.Label(text, size, color: Token.Colors.TextMuted);

    private static ComboBox Dropdown(string name) => Ui.Named(new ComboBox
    {
        FontSize = 13,
        FontFamily = Token.Font.Family,
        MinHeight = 0,
        Padding = new Thickness(8, 2, 0, 2),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
        CornerRadius = new CornerRadius(6),
    }, name);

    private static TextBox Field(string placeholder, string name, bool secret = false)
    {
        var field = Ui.Named(new TextBox
        {
            PlaceholderText = placeholder,
            FontSize = 13,
            FontFamily = Token.Font.Family,
            MinHeight = 0,
            Padding = new Thickness(7, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        }, name);
        if (secret) field.PasswordChar = '•';
        return field;
    }

    internal static HoverButton Action(string title, Token.ControlStyle? style = null, Color? color = null)
    {
        var button = HoverButton.Chip(title, style ?? Token.ControlStyle.FollowUp, color ?? Token.Colors.TextPrimary, height: 26);
        button.TitleSize = 13;
        button.Padding = default;
        button.Focusable = true;
        return button;
    }

    protected override Size PreferredSize(Size available) => new(PanelWidth, PanelHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(backdrop, 0, 0, size.Width, size.Height);
        Place(divider, Margin, 314, 460, 1);
        foreach (var (control, frame) in placed) Place(control, frame);
    }
}
