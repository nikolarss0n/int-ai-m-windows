using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Draws a text box's text as coloured Markdown. Caret, selection and clicks come from the same
/// layout, so they stay true to the larger headings and the code's own face.
public sealed class MarkdownPresenter : TextPresenter
{
    private Size constraint;
    private IReadOnlyList<(int Start, int Length)> highlights = Array.Empty<(int, int)>();
    private List<MarkdownSpan> spans = new();

    public MarkdownLook Look { get; init; } = MarkdownLook.Notes;

    /// Stretches a search found, drawn on gold.
    public IReadOnlyList<(int Start, int Length)> Highlights
    {
        get => highlights;
        set
        {
            highlights = value;
            InvalidateTextLayout();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<MarkdownSpan> Spans => spans;

    protected override TextLayout CreateTextLayout()
    {
        // While a composition is under way the base class owns the text: it shows what is being typed.
        if (!string.IsNullOrEmpty(PreeditText)) return base.CreateTextLayout();
        var text = Text ?? "";
        spans = MarkdownText.Spans(text, Look, highlights);
        var maxWidth = constraint.Width <= 0 || double.IsNaN(constraint.Width) ? double.PositiveInfinity : constraint.Width;
        var maxHeight = constraint.Height <= 0 || double.IsNaN(constraint.Height) ? double.PositiveInfinity : constraint.Height;
        return new TextLayout(
            text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground,
            TextAlignment, TextWrapping, flowDirection: FlowDirection, maxWidth: maxWidth, maxHeight: maxHeight,
            lineHeight: LineHeight, letterSpacing: LetterSpacing, fontFeatures: FontFeatures,
            textStyleOverrides: MarkdownText.Overrides(spans));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        constraint = availableSize;
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (Math.Abs(constraint.Width - arranged.Width) > 0.01) constraint = new Size(Math.Ceiling(arranged.Width), double.PositiveInfinity);
        return arranged;
    }

    /// Where each divider's line goes, in this presenter's coordinates.
    public IEnumerable<(Point From, Point To)> DividerRules()
    {
        var layout = TextLayout;
        var width = Math.Min(Bounds.Width, MarkdownStyler.DividerLine.Length * FontSize * 0.62);
        foreach (var span in spans.Where(span => span.Divider))
        {
            var line = layout.HitTestTextRange(span.Start, span.Length).FirstOrDefault();
            if (line.Height <= 0) continue;
            var y = Math.Round(line.Y + line.Height / 2) + 0.5;
            yield return (new Point(0, y), new Point(width, y));
        }
    }
}

/// Rules the line a divider stands for over its dashes, which the presenter leaves clear.
internal sealed class DividerRulesView : Control
{
    private readonly MarkdownPresenter presenter;

    public DividerRulesView(MarkdownPresenter presenter)
    {
        this.presenter = presenter;
        IsHitTestVisible = false;
        presenter.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextPresenter.TextProperty || change.Property == BoundsProperty) InvalidateVisual();
        };
    }

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(MarkdownText.DividerColor.Brush(), 1);
        foreach (var (from, to) in presenter.DividerRules()) context.DrawLine(pen, from, to);
    }
}

/// A text box that shows its text as Markdown while it is being written.
public sealed class NotesEditor : TextBox
{
    private readonly MarkdownLook look;
    private MarkdownPresenter? presenter;
    private IReadOnlyList<(int Start, int Length)> highlights = Array.Empty<(int, int)>();

    public NotesEditor(MarkdownLook look = MarkdownLook.Notes)
    {
        this.look = look;
        AcceptsReturn = true;
        AcceptsTab = true;
        NewLine = "\n";
        TextWrapping = TextWrapping.Wrap;
        FontFamily = MarkdownText.BodyFamily(look);
        FontSize = MarkdownText.BodySize(look);
        Foreground = MarkdownText.BodyColor.Brush();
        CaretBrush = Token.Palette.White.Brush();
        SelectionBrush = Token.Colors.FocusRing.WithAlpha(0.45).Brush();
        Background = Brushes.Transparent;
        BorderThickness = default;
        Padding = default;
        MinHeight = 0;
        MinWidth = 0;
        FocusAdorner = null;
        Template = new FuncControlTemplate<TextBox>(BuildTemplate);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    /// Room between the scrolling edge and the words: the Mac's text inset plus its line padding.
    public Thickness TextInset { get; init; } = new(25, 20);

    public IReadOnlyList<(int Start, int Length)> Highlights
    {
        get => highlights;
        set
        {
            highlights = value;
            if (presenter is not null) presenter.Highlights = value;
        }
    }

    public IReadOnlyList<MarkdownSpan> Spans => presenter?.Spans ?? MarkdownText.Spans(Text ?? "", look, highlights);

    /// Replaces a stretch of the text as one step that Undo takes back, and leaves the caret after it.
    public void Replace(NotesEditing.Edit edit)
    {
        SelectionStart = edit.Start;
        SelectionEnd = edit.Start + edit.Length;
        SelectedText = edit.Replacement;
        SelectionStart = SelectionEnd = CaretIndex = edit.CaretAfter;
    }

    public void Select(int start, int length)
    {
        // The caret goes first: placing it afterwards would collapse the selection onto it.
        CaretIndex = start;
        SelectionStart = start;
        SelectionEnd = start + length;
    }

    private Control BuildTemplate(TextBox box, INameScope scope)
    {
        presenter = new MarkdownPresenter
        {
            Name = "PART_TextPresenter",
            Look = look,
            Margin = TextInset,
            Highlights = highlights,
        };
        presenter.Bind(TextPresenter.TextProperty, box.GetObservable(TextProperty));
        presenter.Bind(TextPresenter.CaretIndexProperty, box.GetObservable(CaretIndexProperty));
        presenter.Bind(TextPresenter.SelectionStartProperty, box.GetObservable(SelectionStartProperty));
        presenter.Bind(TextPresenter.SelectionEndProperty, box.GetObservable(SelectionEndProperty));
        presenter.Bind(TextPresenter.TextWrappingProperty, box.GetObservable(TextWrappingProperty));
        presenter.Bind(TextPresenter.TextAlignmentProperty, box.GetObservable(TextAlignmentProperty));
        presenter.Bind(TextPresenter.SelectionBrushProperty, box.GetObservable(SelectionBrushProperty));
        presenter.Bind(TextPresenter.CaretBrushProperty, box.GetObservable(CaretBrushProperty));
        presenter.Bind(TextPresenter.CaretBlinkIntervalProperty, box.GetObservable(CaretBlinkIntervalProperty));
        presenter.RegisterInNameScope(scope);

        var scroll = new ScrollViewer
        {
            Name = "PART_ScrollViewer",
            Content = new Panel { Children = { presenter, new DividerRulesView(presenter) { Margin = TextInset } } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
        };
        scroll.RegisterInNameScope(scope);
        return scroll;
    }
}
