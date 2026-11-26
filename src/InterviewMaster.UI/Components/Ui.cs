using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Small builders the views share, so each view states what it shows and not how a label is made.
public static class Ui
{
    public static TextBlock Label(string text, double size, FontWeight? weight = null, Color? color = null, bool digits = false, bool mono = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? Token.Font.Regular,
            FontFamily = mono ? Token.Font.MonoFamily : Token.Font.Family,
            Foreground = (color ?? Token.Colors.TextBody).Brush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(Token.Text.LabelInset, 0)
        };
        if (digits) label.FontFeatures = Token.Font.TabularDigits;
        return label;
    }

    /// A label that wraps, with the line height the text is read at.
    public static TextBlock Wrapping(string text, double size, FontWeight? weight = null, Color? color = null, double leading = Token.Leading.Compact, int maxLines = 0)
    {
        var label = Label(text, size, weight, color);
        label.TextWrapping = TextWrapping.Wrap;
        label.LineHeight = Math.Round(size * leading);
        label.MaxLines = maxLines;
        return label;
    }

    /// Small capitals are stored in natural case and set in capitals here. `detail` is left as it is
    /// (a latency such as "1.5s" keeps its lowercase unit) and its digits stay tabular.
    public static TextBlock Overline(string caps, Color color, string detail = "", string joiner = " ")
    {
        var label = Label(OverlineText(caps, detail, joiner), Token.Font.Micro, Token.Font.Semibold, color, digits: true);
        label.LetterSpacing = Token.Font.Micro * Token.Text.CapsTracking;
        return label;
    }

    public static string OverlineText(string caps, string detail = "", string joiner = " ") =>
        caps.ToUpperInvariant() + (detail.Length == 0 ? "" : joiner + detail);

    public static void SetText(TextBlock label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    public static Border Surface(Border border, Token.SurfaceStyle style)
    {
        border.CornerRadius = new CornerRadius(style.Radius);
        border.Background = style.Background?.Brush();
        border.BorderBrush = style.Border?.Brush();
        border.BorderThickness = new Thickness(style.Border is null ? 0 : style.BorderWidth);
        return border;
    }

    /// Shows or hides a control with a short fade, or at once when motion is reduced. `done` runs
    /// when the control has arrived or gone.
    public static void Fade(Control control, bool show, double opacity = 1, double seconds = 0.2, Action? done = null)
    {
        if (Token.Motion.ReducesMotion())
        {
            control.Transitions = null;
            control.Opacity = show ? opacity : 0;
            control.IsVisible = show;
            done?.Invoke();
            return;
        }
        control.Transitions ??= new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(seconds) } };
        if (show) control.IsVisible = true;
        control.Opacity = show ? opacity : 0;
        Delayed.After(seconds, () =>
        {
            // A later call may have turned it round while this one was under way.
            if (!show && control.Opacity == 0) control.IsVisible = false;
            done?.Invoke();
        });
    }

    /// Hover help, and the same words for a screen reader when the control has no name of its own.
    public static T Tip<T>(T control, string? text) where T : Control
    {
        ToolTip.SetTip(control, string.IsNullOrEmpty(text) ? null : text);
        return control;
    }

    public static T Named<T>(T control, string name) where T : Control
    {
        AutomationProperties.SetName(control, name);
        return control;
    }

    /// The width of one line of text as the views set it.
    public static double TextWidth(string text, double size, FontWeight? weight = null, bool mono = false, bool digits = false, double letterSpacing = 0)
    {
        if (text.Length == 0) return 0;
        var typeface = new Typeface(mono ? Token.Font.MonoFamily : Token.Font.Family, FontStyle.Normal, weight ?? Token.Font.Regular);
        var layout = new TextLayout(
            text, typeface, size, Brushes.White,
            letterSpacing: letterSpacing, fontFeatures: digits ? Token.Font.TabularDigits : null);
        return Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
    }

    /// Puts text on the clipboard. The UI driver replaces it to see what was copied.
    public static Action<Control, string> CopyText { get; set; } = (control, text) =>
        _ = TopLevel.GetTopLevel(control)?.Clipboard?.SetTextAsync(text);

    public static FormattedText Formatted(string text, double size, Color color, FontWeight? weight = null, bool mono = false, bool digits = false)
    {
        var formatted = new FormattedText(
            text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(mono ? Token.Font.MonoFamily : Token.Font.Family, FontStyle.Normal, weight ?? Token.Font.Regular), size, color.Brush());
        if (digits) formatted.SetFontFeatures(Token.Font.TabularDigits);
        return formatted;
    }

    public static Run Run(string text, Color color, FontWeight? weight = null, bool mono = false, double? size = null)
    {
        var run = new Run(text) { Foreground = color.Brush(), FontWeight = weight ?? Token.Font.Regular };
        if (mono) run.FontFamily = Token.Font.MonoFamily;
        if (size is { } points) run.FontSize = points;
        return run;
    }

    /// The colour and weight of one run of an answer.
    public static Run Run(RichRun run, double size)
    {
        return run.Style switch
        {
            RichStyle.LabelFirst => Run(run.Text, Token.Palette.SystemCyan, Token.Font.Semibold),
            RichStyle.LabelSecond => Run(run.Text, Token.Palette.Pink, Token.Font.Semibold),
            RichStyle.Separator => Run(run.Text, Token.Palette.Gray50),
            RichStyle.Header => Run(run.Text, Token.Palette.Gold, Token.Font.Semibold),
            RichStyle.Callout => Run(run.Text, Token.Palette.Orange, Token.Font.Semibold),
            RichStyle.Bold => Run(run.Text, Token.Palette.Gold, Token.Font.Semibold),
            RichStyle.InlineCode => WithBackground(Run(run.Text, Token.Palette.Orange, mono: true, size: size - 1), Token.Palette.Black.WithAlpha(0.2)),
            RichStyle.Code => Run(run.Text, Token.Syntax.Of(run.Syntax), mono: true, size: size - 1),
            _ => Run(run.Text, Token.Palette.White)
        };
    }

    private static Run WithBackground(Run run, Color color)
    {
        run.Background = color.Brush();
        return run;
    }
}
