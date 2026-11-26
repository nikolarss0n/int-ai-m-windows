using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// A question the app stops to ask: the app's mark, a bold title, the message, and a row of buttons.
/// It stands where an `NSAlert` does on macOS. Enter presses the first button, Escape the last.
public sealed class AlertView : FrameView
{
    public const double PanelWidth = 440;

    private const double Margin = 22;
    private const double IconSide = 44;
    private const double TextX = Margin + IconSide + 16;
    private const double TextWidth = PanelWidth - TextX - Margin;
    private const double ButtonHeight = 28;

    private readonly Border backdrop = new() { Background = Token.Colors.BgSolidWindow.Brush() };
    private readonly AppIconView icon = new();
    private readonly TextBlock title;
    private readonly TextBlock message;

    public AlertView(AppQuestion question)
    {
        Focusable = true;
        title = Ui.Wrapping(question.Title, 14, FontWeight.Bold, Token.Colors.TextPrimary);
        message = Ui.Wrapping(question.Message, 12, color: Token.Colors.TextBody, leading: Token.Leading.Reading);
        Children.Add(backdrop);
        Children.Add(icon);
        Children.Add(title);
        Children.Add(message);
        for (var index = 0; index < question.Buttons.Count; index++)
        {
            var chosen = index;
            var isDefault = index == 0;
            var danger = isDefault && question.Critical;
            var button = SettingsView.Action(
                question.Buttons[index],
                isDefault ? danger ? Token.ControlStyle.Primary(Token.Hue.Danger) : SettingsView.DefaultAction : null,
                isDefault && !danger ? Token.Palette.White : null);
            button.Click += () => Chosen?.Invoke(chosen);
            Buttons.Add(button);
            Children.Add(button);
        }
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            e.Handled = true;
            Chosen?.Invoke(e.Key == Key.Enter ? 0 : question.DismissIndex);
        };
    }

    public List<HoverButton> Buttons { get; } = new();

    public string TitleText => title.Text ?? "";

    public string MessageText => message.Text ?? "";

    public event Action<int>? Chosen;

    public double PanelHeight
    {
        get
        {
            var text = HeightOf(title, TextWidth) + (MessageText.Length > 0 ? 8 + HeightOf(message, TextWidth) : 0);
            return Margin + Math.Max(IconSide, text) + 20 + ButtonHeight + 18;
        }
    }

    protected override Size PreferredSize(Size available) => new(PanelWidth, PanelHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(backdrop, 0, 0, size.Width, size.Height);
        Place(icon, Margin, Margin, IconSide, IconSide);
        var titleHeight = HeightOf(title, TextWidth);
        Place(title, TextX, Margin, TextWidth, titleHeight);
        if (MessageText.Length > 0) Place(message, TextX, Margin + titleHeight + 8, TextWidth, HeightOf(message, TextWidth));

        // The default button leads the group, which sits at the right edge as Windows dialogs have it.
        var widths = Buttons.Select(button => Math.Max(84, Math.Ceiling(Ui.TextWidth(button.Title, 13)) + 28)).ToList();
        var x = size.Width - Margin - widths.Sum() - 8 * (widths.Count - 1);
        for (var index = 0; index < Buttons.Count; index++)
        {
            Place(Buttons[index], x, size.Height - 18 - ButtonHeight, widths[index], ButtonHeight);
            x += widths[index] + 8;
        }
    }
}

/// The slider that sets how see-through the window's background is. On macOS it is a row inside the
/// status menu; a Windows tray menu cannot hold a slider, so the menu opens this small panel instead.
public sealed class TransparencyPanelView : FrameView
{
    public const double PanelWidth = 260;
    public const double PanelHeight = 54;

    private readonly Border backdrop = new() { Background = Token.Colors.BgSolidWindow.Brush() };
    private readonly TextBlock title = Ui.Label("Background Transparency", Token.Font.Caption, FontWeight.Medium, Token.Colors.TextPrimary);
    private readonly TextBlock value = Ui.Label("", Token.Font.Caption, FontWeight.Medium, Token.Colors.TextPrimary, digits: true);

    public TransparencyPanelView(double transparency)
    {
        value.TextAlignment = TextAlignment.Right;
        Slider = Ui.Tip(Ui.Named(new Slider { Minimum = 0, Maximum = 1, Value = transparency, MinHeight = 0 }, "Background transparency"),
            "Changes only the background. Text and code stay fully visible.");
        Children.Add(backdrop);
        Children.Add(title);
        Children.Add(value);
        Children.Add(Slider);
        Show(transparency);
        Slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            Show(Slider.Value);
            Changed?.Invoke(Slider.Value);
        };
    }

    public Slider Slider { get; }

    public string ValueText => value.Text ?? "";

    public event Action<double>? Changed;

    private void Show(double transparency) => value.Text = $"{(int)Math.Round(transparency * 100, MidpointRounding.AwayFromZero)}%";

    protected override Size PreferredSize(Size available) => new(PanelWidth, PanelHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(backdrop, 0, 0, size.Width, size.Height);
        Place(title, 14, 6, 176, 16);
        Place(value, 196, 6, 50, 16);
        Place(Slider, 14, 22, 232, 28);
    }
}
