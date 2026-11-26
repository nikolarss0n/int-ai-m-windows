using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// An answer set as paragraphs: bullets hang under their text, code keeps its spacing. Each paragraph
/// can be selected and copied.
public sealed class AnswerTextView : StackPanel
{
    private readonly double size;
    private readonly double leading;

    public AnswerTextView(double size = Token.Font.Lead, double leading = Token.Leading.Reading)
    {
        this.size = size;
        this.leading = leading;
        Spacing = Token.Space.Xs;
        Orientation = Orientation.Vertical;
    }

    /// What is shown, as plain text: one line per paragraph, bullets marked.
    public string PlainText { get; private set; } = "";

    /// How far a bullet's text is set in from its mark.
    public double BulletIndent => Ui.TextWidth("• ", size);

    public void SetPlain(string text, Color color)
    {
        Children.Clear();
        PlainText = text;
        var block = Paragraph();
        block.Inlines!.Add(Ui.Run(text, color));
        Children.Add(block);
    }

    public void Clear()
    {
        Children.Clear();
        PlainText = "";
    }

    /// `streaming` leaves a caret after the last word, because more is on its way.
    public void SetAnswer(IReadOnlyList<AnswerParagraph> paragraphs, bool streaming)
    {
        Children.Clear();
        var lines = new List<string>();
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var block = Paragraph();
            foreach (var run in paragraph.Runs) block.Inlines!.Add(Ui.Run(run, size));
            if (paragraph.Runs.Count == 0) block.Inlines!.Add(new Run(" "));
            if (streaming && index == paragraphs.Count - 1)
            {
                block.Inlines!.Add(Ui.Run("  ▌", Token.Colors.TextStrong, Token.Font.Medium));
            }
            if (paragraph.Kind == AnswerParagraphKind.Code) block.TextWrapping = TextWrapping.NoWrap;

            if (paragraph.Kind == AnswerParagraphKind.Bullet)
            {
                var mark = Ui.Label("•", size, color: Token.Colors.TextStrong);
                mark.LineHeight = size * leading;
                mark.Padding = default;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions { new(BulletIndent, GridUnitType.Pixel), new(1, GridUnitType.Star) } };
                Grid.SetColumn(block, 1);
                row.Children.Add(mark);
                row.Children.Add(block);
                Children.Add(row);
                lines.Add("• " + paragraph.PlainText);
            }
            else
            {
                Children.Add(block);
                lines.Add(paragraph.PlainText);
            }
        }
        PlainText = string.Join("\n", lines);
    }

    private SelectableTextBlock Paragraph() => new()
    {
        FontSize = size,
        FontFamily = Token.Font.Family,
        FontWeight = Token.Font.Regular,
        Foreground = Token.Colors.TextBody.Brush(),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size * leading,
        SelectionBrush = Token.Hue.Accent.Tint(Token.Tint.Strong).Brush(),
        Inlines = new InlineCollection()
    };
}
