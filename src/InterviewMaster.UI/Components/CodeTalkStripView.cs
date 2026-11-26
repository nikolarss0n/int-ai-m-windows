using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The line above the code: what to say while typing, and the way back to the full answer.
public sealed class CodeTalkStripView : FrameView
{
    private readonly TextBlock label = Ui.Label("", Token.Font.Body, Token.Font.Medium, Token.Colors.TextPrimary);
    private readonly HoverButton toggleButton = HoverButton.Glyph(Icons.ChevronDown, "Show the full answer", Token.Sizing.ControlSmall, 12);

    public event Action? Toggle;

    public CodeTalkStripView()
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        toggleButton.CornerRadius = new CornerRadius(Token.Radius.Sm);
        toggleButton.Click += () => Toggle?.Invoke();
        Children.Add(label);
        Children.Add(toggleButton);
    }

    public string Line => label.Text ?? "";

    public HoverButton ToggleButton => toggleButton;

    public void Update(string line, bool answerOpen)
    {
        var text = answerOpen ? "Show the code" : line;
        Ui.SetText(label, text);
        Ui.Tip(label, text);
        var action = answerOpen ? "Show the code" : "Show the full answer";
        toggleButton.Icon = answerOpen ? Icons.ChevronUp : Icons.ChevronDown;
        toggleButton.Label = action;
        toggleButton.Help = $"{action} ({PlatformCopy.AnswerOverCodeShortcut})";
    }

    protected override void LayoutFrames(Size size)
    {
        var control = Token.Sizing.ControlSmall;
        var buttonX = size.Width - control;
        Place(toggleButton, buttonX, (size.Height - control) / 2, control, control);
        var x = Token.FocusLayout.LabelIndent;
        Place(label, x, (size.Height - 18) / 2, Math.Max(0, buttonX - Token.Space.Sm - x), 18);
    }
}
