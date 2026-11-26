using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// One file of the answer: its name, then each step's note and code in order, with line numbers,
/// step numbers and marks for what changed since the version before.
public sealed class CodeFileView : FrameView
{
    public const double HeaderHeight = 26;
    public const double CodePadding = 10;
    private const double MarkX = 2;
    private const double MarkWidth = 2;
    public static readonly double CodeLine = Math.Round(Token.Font.Code * Token.Leading.Compact, 1);
    public static readonly double NoteLine = Math.Round(Token.Font.Caption * Token.Leading.Compact, 1);

    public readonly record struct Piece(string Text, Color Color, bool Mono);

    /// One line of the well. `Line` is the zero-based line of the file for code; `StepNumber` is drawn
    /// in the gutter beside a step's first note line.
    public sealed record Row(bool IsCode, int Line, int Step, int? StepNumber, bool Separated, bool Dimmed, IReadOnlyList<Piece> Pieces, double Top, double Height)
    {
        public string Text => string.Concat(Pieces.Select(piece => piece.Text));
    }

    private readonly TextBlock fileLabel = Ui.Label("", Token.Font.Caption, Token.Font.Semibold, Token.Colors.TextStrong, mono: true);
    private readonly Well well = new();
    private readonly Lines lines = new();
    private readonly ScrollViewer lineScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly HoverButton copyButton = HoverButton.Glyph(Icons.Copy, "Copy file", Token.Sizing.ControlSmall, 13);
    private DispatcherTimer? copyReset;
    private bool numbersSteps;
    private bool showsCopy;
    private CodeLineChanges? changes;
    private int? currentStep;
    private Size textSize;

    public CodeFileView(CodeFileSection section, bool numbersSteps, bool showsCopy, CodeLineChanges? changes, int? currentStep = null)
    {
        Section = section;
        this.numbersSteps = numbersSteps;
        this.showsCopy = showsCopy;
        this.changes = changes;
        this.currentStep = currentStep;
        fileLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        fileLabel.VerticalAlignment = VerticalAlignment.Center;
        copyButton.CornerRadius = new CornerRadius(Token.Radius.Sm);
        copyButton.Click += CopyFile;
        lineScroll.Content = lines;
        Children.Add(fileLabel);
        Children.Add(well);
        Children.Add(lineScroll);
        Children.Add(copyButton);
        Apply();
    }

    public CodeFileSection Section { get; private set; }

    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    public double PreferredHeight => HeaderHeight + textSize.Height + CodePadding * 2;

    public HoverButton CopyButton => copyButton;

    public void Update(CodeFileSection section, bool numbers, bool copy, CodeLineChanges? lineChanges, int? step = null)
    {
        if (section == Section && numbers == numbersSteps && copy == showsCopy && Equals(lineChanges, changes) && step == currentStep) return;
        Section = section;
        numbersSteps = numbers;
        showsCopy = copy;
        changes = lineChanges;
        currentStep = step;
        Apply();
    }

    public void ApplyTransparency() => well.InvalidateVisual();

    /// Where a step's rows are, in this view's coordinates.
    public Rect? RectForStep(int step)
    {
        var rows = Rows.Where(row => row.Step == step).ToList();
        if (rows.Count == 0) return null;
        var top = rows.Min(row => row.Top);
        var bottom = rows.Max(row => row.Top + row.Height);
        return new Rect(0, HeaderHeight + CodePadding + top, Bounds.Width, bottom - top);
    }

    public static string? HighlighterLanguage(string fence) => fence.ToLowerInvariant() switch
    {
        "python" or "py" => "python",
        "javascript" or "js" or "typescript" or "ts" or "tsx" or "jsx" => "javascript",
        "java" => "java",
        "sql" => "sql",
        "swift" => "swift",
        "go" or "golang" => "go",
        _ => null
    };

    private void Apply()
    {
        Ui.SetText(fileLabel, Section.FileName);
        Ui.Tip(fileLabel, Section.FileName);
        copyButton.IsVisible = showsCopy;
        copyButton.Label = $"Copy {Section.FileName}";
        copyButton.Help = $"Copy {Section.FileName}";

        var rows = new List<Row>();
        var y = 0.0;
        void Add(bool isCode, int line, int step, int? number, bool separated, IReadOnlyList<Piece> pieces, double before, double after)
        {
            y += before;
            var height = isCode ? CodeLine : NoteLine;
            var dimmed = currentStep is { } current && step != current;
            rows.Add(new Row(isCode, line, step, number, separated, dimmed, pieces, y, height));
            y += height + after;
        }

        for (var index = 0; index < Section.Chunks.Count; index++)
        {
            var chunk = Section.Chunks[index];
            var notes = NoteLines(chunk);
            for (var lineIndex = 0; lineIndex < notes.Count; lineIndex++)
            {
                var first = lineIndex == 0;
                Add(
                    isCode: false, line: -1, step: chunk.Number, number: first && numbersSteps ? chunk.Number : null,
                    separated: first && index > 0, pieces: notes[lineIndex],
                    before: first && index > 0 ? Token.Space.Md : 0, after: lineIndex == notes.Count - 1 ? Token.Space.Xs : 0);
            }
            if (chunk.Code.Length == 0) continue;
            var line = chunk.FirstLine;
            foreach (var pieces in CodeLines(chunk.Code, HighlighterLanguage(Section.Language)))
            {
                Add(isCode: true, line: line, step: chunk.Number, number: null, separated: false, pieces: pieces, before: 0, after: 0);
                line++;
            }
        }
        Rows = rows;
        var width = rows.Count == 0 ? 0 : rows.Max(RowWidth);
        textSize = new Size(Math.Ceiling(width), rows.Count == 0 ? 16 : Math.Ceiling(y));
        lines.Set(rows, new Size(textSize.Width + Token.Space.Sm, textSize.Height));
        well.Set(rows, changes ?? new CodeLineChanges(), GutterWidth, currentStep is not null, CodePadding + textSize.Height + 2);

        var parts = new List<string> { Section.FileName, $"{Section.Chunks.Count} step{(Section.Chunks.Count == 1 ? "" : "s")}" };
        if (changes is { Changed.Count: > 0 }) parts.Add($"{changes.Changed.Count} changed lines");
        if (changes is { RemovedBefore.Count: > 0 }) parts.Add("lines removed");
        AutomationProperties.SetName(this, string.Join(", ", parts));
        Relayout();
    }

    private static double RowWidth(Row row) =>
        row.Pieces.Sum(piece => Ui.Formatted(piece.Text, row.IsCode ? Token.Font.Code : Token.Font.Caption, piece.Color, mono: piece.Mono).WidthIncludingTrailingWhitespace);

    /// A step's note, wrapped to an even measure. A step without a note still gets a row when steps
    /// are numbered, so its number has somewhere to sit.
    private List<IReadOnlyList<Piece>> NoteLines(CodeChunk chunk)
    {
        var result = new List<IReadOnlyList<Piece>>();
        if (chunk.Why.Length == 0 && !chunk.RepeatsEarlierCode)
        {
            if (numbersSteps) result.Add(Array.Empty<Piece>());
            return result;
        }
        var wrapped = chunk.Why.Length == 0 ? new List<string> { "" } : Wrapped(chunk.Why, Token.Measure.Note);
        for (var index = 0; index < wrapped.Count; index++)
        {
            var pieces = new List<Piece> { new(wrapped[index], Token.Colors.TextBody, false) };
            if (index == wrapped.Count - 1 && chunk.RepeatsEarlierCode)
            {
                var separator = chunk.Why.Length == 0 ? "" : Token.Text.Separator;
                pieces.Add(new Piece(separator + "Repeats earlier code", Token.Hue.Warning.Text, false));
            }
            result.Add(pieces);
        }
        return result;
    }

    /// Breaks `text` into the fewest lines that fit `width`, then narrows them until they are even.
    public static List<string> Wrapped(string text, double width)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new List<string> { "" };
        double Measure(string value) => Ui.Formatted(value, Token.Font.Caption, Token.Colors.TextBody).WidthIncludingTrailingWhitespace;
        var widths = words.Select(Measure).ToArray();
        var space = Measure(" ");

        List<int> Starts(double limit)
        {
            var starts = new List<int> { 0 };
            var used = widths[0];
            for (var index = 1; index < words.Length; index++)
            {
                if (used + space + widths[index] > limit)
                {
                    starts.Add(index);
                    used = widths[index];
                }
                else
                {
                    used += space + widths[index];
                }
            }
            return starts;
        }

        var best = Starts(width);
        if (best.Count > 1)
        {
            double low = width / best.Count, high = width;
            for (var pass = 0; pass < 8; pass++)
            {
                var middle = (low + high) / 2;
                var candidate = Starts(middle);
                if (candidate.Count == best.Count)
                {
                    best = candidate;
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }
        }
        var result = new List<string>();
        for (var line = 0; line < best.Count; line++)
        {
            var end = line + 1 < best.Count ? best[line + 1] : words.Length;
            result.Add(string.Join(" ", words[best[line]..end]));
        }
        return result;
    }

    private static List<IReadOnlyList<Piece>> CodeLines(string code, string? language)
    {
        var result = new List<IReadOnlyList<Piece>>();
        var line = new List<Piece>();
        foreach (var run in SyntaxHighlighter.Highlight(code.Replace("\t", "    "), language))
        {
            var parts = run.Text.Split('\n');
            for (var index = 0; index < parts.Length; index++)
            {
                if (index > 0)
                {
                    result.Add(line);
                    line = new List<Piece>();
                }
                if (parts[index].Length > 0) line.Add(new Piece(parts[index], Token.Syntax.Of(run.Kind), true));
            }
        }
        result.Add(line);
        return result;
    }

    /// Wide enough for the last line's number, with room on both sides.
    public double GutterWidth
    {
        get
        {
            var last = Section.Chunks.Count == 0 ? 1 : Section.Chunks[^1].FirstLine + Math.Max(1, Section.Chunks[^1].Code.Split('\n').Length);
            var digits = Math.Max(2, last.ToString().Length);
            return Math.Ceiling(Ui.Formatted(new string('0', digits), Token.Font.Caption, Token.Colors.TextMuted, digits: true).Width) + Token.Space.Md * 2;
        }
    }

    private void CopyFile()
    {
        Ui.CopyText(this, Section.Code);
        copyButton.Icon = Icons.Checkmark;
        copyButton.GlyphColor = Token.Hue.Success.Text;
        copyReset?.Stop();
        copyReset = new DispatcherTimer(TimeSpan.FromSeconds(Token.Motion.FeedbackHold), DispatcherPriority.Normal, (_, _) =>
        {
            copyReset?.Stop();
            copyButton.Icon = Icons.Copy;
            copyButton.GlyphColor = null;
        });
        copyReset.Start();
    }

    protected override Size PreferredSize(Size available) => new(available.Width, PreferredHeight);

    protected override void LayoutFrames(Size size)
    {
        var control = Token.Sizing.ControlSmall;
        if (copyButton.IsVisible) Place(copyButton, size.Width - control, (HeaderHeight - control) / 2 - 2, control, control);
        Place(fileLabel, 0, 2, Math.Max(40, size.Width - (showsCopy ? control + Token.Space.Xs : 0)), 18);
        Place(well, 0, HeaderHeight, size.Width, textSize.Height + CodePadding * 2);
        var gutter = GutterWidth;
        Place(lineScroll, gutter, HeaderHeight + CodePadding, Math.Max(40, size.Width - gutter - CodePadding), textSize.Height);
    }

    /// The text of the rows. It scrolls sideways when a line is longer than the well.
    private sealed class Lines : Control
    {
        private IReadOnlyList<Row> rows = Array.Empty<Row>();
        private Size size;

        public Lines()
        {
            IsHitTestVisible = false;
        }

        public void Set(IReadOnlyList<Row> next, Size content)
        {
            rows = next;
            size = content;
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => size;

        public override void Render(DrawingContext context)
        {
            foreach (var row in rows)
            {
                var x = 0.0;
                foreach (var piece in row.Pieces)
                {
                    var text = Ui.Formatted(piece.Text, row.IsCode ? Token.Font.Code : Token.Font.Caption, row.Dimmed ? piece.Color.Faded(Token.State.SetBackAlpha) : piece.Color, mono: piece.Mono);
                    context.DrawText(text, new Point(x, row.Top + (row.Height - text.Height) / 2));
                    x += text.WidthIncludingTrailingWhitespace;
                }
            }
        }
    }

    /// The well behind the rows: its fill, the numbers in the gutter, the line between steps and the
    /// change marks.
    private sealed class Well : Control
    {
        private IReadOnlyList<Row> rows = Array.Empty<Row>();
        private CodeLineChanges changes = new();
        private double gutterWidth;
        private bool focusesStep;
        private double endY;

        public Well()
        {
            IsHitTestVisible = false;
        }

        public void Set(IReadOnlyList<Row> next, CodeLineChanges lineChanges, double gutter, bool focuses, double end)
        {
            rows = next;
            changes = lineChanges;
            gutterWidth = gutter;
            focusesStep = focuses;
            endY = end;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var style = Token.SurfaceStyle.CodeWell;
            if (style.Background is { } fill) context.DrawRectangle(fill.Brush(), null, new Rect(Bounds.Size), style.Radius, style.Radius);
            double Fade(Row row) => row.Dimmed ? Token.State.SetBackAlpha : 1;
            foreach (var row in rows)
            {
                var top = row.Top + CodePadding;
                if (!row.IsCode)
                {
                    if (row.StepNumber is { } step)
                    {
                        var color = row.Dimmed || !focusesStep ? Token.Colors.TextBody : Token.Hue.Accent.Text;
                        var number = Ui.Formatted(step.ToString(), Token.Font.Caption, color.Faded(Fade(row)), Token.Font.Semibold, digits: true);
                        context.DrawText(number, new Point(gutterWidth - Token.Space.Md - number.Width, top + (row.Height - number.Height) / 2));
                    }
                    if (row.Separated) context.FillRectangle(Token.Colors.Separator.Brush(), new Rect(0, top - Token.Space.Xs, Bounds.Width, 1));
                    continue;
                }
                var label = Ui.Formatted((row.Line + 1).ToString(), Token.Font.Caption, Token.Colors.TextMuted.Faded(Fade(row)), digits: true);
                context.DrawText(label, new Point(gutterWidth - Token.Space.Md - label.Width, top + (row.Height - label.Height) / 2 + 0.5));
                if (changes.Changed.Contains(row.Line))
                {
                    context.FillRectangle(Token.Hue.Success.Solid.Brush(), new Rect(MarkX, top, MarkWidth, row.Height));
                }
                if (changes.RemovedBefore.Contains(row.Line)) DrawRemoved(context, top);
            }
            if (rows.Count > 0 && rows[^1] is { IsCode: true } last && changes.RemovedBefore.Contains(last.Line + 1)) DrawRemoved(context, endY);
        }

        private static void DrawRemoved(DrawingContext context, double y) =>
            context.FillRectangle(Token.Hue.Danger.Solid.Brush(), new Rect(MarkX, y - 1, Token.Space.Sm, MarkWidth));
    }

    /// The shape of one step of one file, for the placeholder shown while the first step is written.
    public sealed record Placeholder(double NameX, double NameMidY, double NameWidth, double WellTop, double WellHeight, double NumberMaxX, double TextX, double[] RowMidYs, double Indent, double Height);

    public static Placeholder PlaceholderShape()
    {
        var gutter = Math.Ceiling(Ui.Formatted("00", Token.Font.Caption, Token.Colors.TextMuted, digits: true).Width) + Token.Space.Md * 2;
        var noteHeight = NoteLine + Token.Space.Xs;
        var textHeight = noteHeight + CodeLine * 3;
        var top = HeaderHeight + CodePadding;
        return new Placeholder(
            NameX: 2,
            NameMidY: 2 + 9,
            NameWidth: Math.Ceiling(Ui.Formatted("main.py", Token.Font.Caption, Token.Colors.TextStrong, Token.Font.Semibold, mono: true).Width),
            WellTop: HeaderHeight,
            WellHeight: textHeight + CodePadding * 2,
            NumberMaxX: gutter - Token.Space.Md,
            TextX: gutter,
            RowMidYs: new[] { top + NoteLine / 2, top + noteHeight + CodeLine * 0.5, top + noteHeight + CodeLine * 1.5, top + noteHeight + CodeLine * 2.5 },
            Indent: Math.Ceiling(Ui.Formatted("    ", Token.Font.Code, Token.Colors.TextBody, mono: true).WidthIncludingTrailingWhitespace),
            Height: HeaderHeight + textHeight + CodePadding * 2);
    }
}

/// Stands where the first step of the code will be while it is written.
public sealed class CodeStepSkeletonView : FrameView
{
    private readonly Border well = new();
    private readonly SkeletonView bars = new();

    public CodeStepSkeletonView()
    {
        IsHitTestVisible = false;
        Children.Add(well);
        Children.Add(bars);
        ApplyTransparency();
        var shape = CodeFileView.PlaceholderShape();
        var bar = Token.Skeleton.SmallLineHeight;
        var pieces = new List<SkeletonView.Piece> { new(shape.NameX, shape.NameMidY - bar / 2, shape.NameWidth, bar) };
        double[] shares = { 0.3, 0.2, 0.42, 0.26 };
        for (var row = 0; row < shape.RowMidYs.Length; row++)
        {
            var midY = shape.RowMidYs[row];
            if (row > 0) pieces.Add(new SkeletonView.Piece(shape.NumberMaxX - bar, midY - bar / 2, bar, bar));
            pieces.Add(new SkeletonView.Piece(shape.TextX + (row > 1 ? shape.Indent : 0), midY - bar / 2, shares[row % shares.Length], bar, Share: true));
        }
        bars.Pieces = pieces;
    }

    public static double PreferredHeight => CodeFileView.PlaceholderShape().Height;

    public void ApplyTransparency() => Ui.Surface(well, Token.SurfaceStyle.CodeWell);

    protected override void LayoutFrames(Size size)
    {
        var shape = CodeFileView.PlaceholderShape();
        Place(well, 0, shape.WellTop, size.Width, shape.WellHeight);
        Place(bars, 0, 0, size.Width, size.Height);
    }
}
