using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Where Markdown is shown: the notes (large, fixed-width), the analysis view, or prose in the code panel.
public enum MarkdownLook
{
    Notes,
    Analysis,
    Panel
}

/// One stretch of Markdown source with everything needed to draw it.
public readonly record struct MarkdownSpan(
    int Start, string Text, FontFamily Family, double Size, FontWeight Weight, FontStyle Style,
    Color Foreground, Color? Background, bool Underline, bool Divider)
{
    public int Length => Text.Length;
}

/// Markdown source as coloured text. The source is shown as typed, with its markers set back.
public static class MarkdownText
{
    private static readonly Color Yellow = Color.FromRgb(255, 214, 0);
    private static readonly Color Body = Color.FromRgb(242, 242, 242);
    private static readonly Color CodeBackground = Color.FromArgb(128, 31, 33, 38);
    private static readonly Color InlineCode = Color.FromRgb(250, 184, 153);
    private static readonly Color InlineCodeBackground = Color.FromArgb(204, 38, 38, 51);
    private static readonly Color Link = Color.FromRgb(102, 179, 255);
    private static readonly Color Marker = Color.FromArgb(153, 128, 128, 128);
    private static readonly Color MatchBackground = Token.Palette.Gold.WithAlpha(0.5);

    public static readonly Color DividerColor = Yellow.WithAlpha(0.4);

    public static Color BodyColor => Body;

    public static double BodySize(MarkdownLook look) => look switch
    {
        MarkdownLook.Notes => 16,
        MarkdownLook.Analysis => 15,
        _ => Token.Font.Body,
    };

    private static double HeadingSize(MarkdownLook look) => look switch
    {
        MarkdownLook.Notes => 24,
        MarkdownLook.Analysis => 20,
        _ => Token.Font.Lead,
    };

    private static double SubHeadingSize(MarkdownLook look) => look switch
    {
        MarkdownLook.Notes => 20,
        MarkdownLook.Analysis => 18,
        _ => Token.Font.Body,
    };

    private static double CodeSize(MarkdownLook look) => look switch
    {
        MarkdownLook.Notes => 15,
        MarkdownLook.Analysis => 14,
        _ => Token.Font.Code,
    };

    public static FontFamily BodyFamily(MarkdownLook look) => look == MarkdownLook.Panel ? Token.Font.Family : Token.Font.MonoFamily;

    /// The source cut into stretches that are drawn alike. `matches` are stretches a search found.
    public static List<MarkdownSpan> Spans(string text, MarkdownLook look, IReadOnlyList<(int Start, int Length)>? matches = null)
    {
        var emphasis = look == MarkdownLook.Panel ? Token.Font.Semibold : FontWeight.Bold;
        var body = BodySize(look);
        var spans = new List<MarkdownSpan>();
        var position = 0;
        foreach (var run in MarkdownStyler.Style(text))
        {
            var (family, size, weight) = run.Face switch
            {
                MarkdownFace.MarkerMono => (Token.Font.MonoFamily, body, Token.Font.Regular),
                MarkdownFace.Heading => (Token.Font.Family, HeadingSize(look), emphasis),
                MarkdownFace.SubHeading => (Token.Font.Family, SubHeadingSize(look), Token.Font.Semibold),
                MarkdownFace.Bold => (Token.Font.Family, body, emphasis),
                MarkdownFace.InlineCode => (Token.Font.MonoFamily, CodeSize(look), Token.Font.Medium),
                MarkdownFace.ListMarker => (Token.Font.Family, body, Token.Font.Semibold),
                MarkdownFace.Divider => (Token.Font.Family, body, FontWeight.Light),
                MarkdownFace.Code => (Token.Font.MonoFamily, CodeSize(look), Token.Font.Regular),
                _ => (BodyFamily(look), body, Token.Font.Regular),
            };
            var foreground = run.Ink switch
            {
                MarkdownInk.Marker => Marker,
                MarkdownInk.Heading or MarkdownInk.ListMarker => Yellow,
                MarkdownInk.Bold => look == MarkdownLook.Analysis ? Yellow : Token.Palette.White,
                MarkdownInk.InlineCode => InlineCode,
                MarkdownInk.Divider => DividerColor,
                MarkdownInk.Link => Link,
                MarkdownInk.LinkUrl => Marker.WithAlpha(0.4),
                MarkdownInk.Syntax => Token.Syntax.Of(run.Syntax),
                _ => Body,
            };
            Color? background = run.Fill switch
            {
                MarkdownFill.CodeBlock => CodeBackground,
                MarkdownFill.InlineCode => InlineCodeBackground,
                _ => null,
            };
            foreach (var (piece, offset, matched) in Split(run.Text, position, matches))
            {
                spans.Add(new MarkdownSpan(
                    position + offset, piece, family, size, weight, run.Italic ? FontStyle.Italic : FontStyle.Normal,
                    matched ? Token.Palette.Black : foreground, matched ? MatchBackground : background,
                    run.Underline, run.Face == MarkdownFace.Divider));
            }
            position += run.Text.Length;
        }
        return spans;
    }

    /// Fills a label with the coloured source. A divider is drawn as the line the Mac draws.
    public static void Fill(InlineCollection inlines, string text, MarkdownLook look)
    {
        inlines.Clear();
        foreach (var span in Spans(text, look))
        {
            var inline = new Run(span.Divider ? MarkdownStyler.DividerLine : span.Text)
            {
                FontFamily = span.Family,
                FontSize = span.Size,
                FontWeight = span.Weight,
                FontStyle = span.Style,
                Foreground = span.Foreground.Brush(),
            };
            if (span.Background is { } background) inline.Background = background.Brush();
            if (span.Underline) inline.TextDecorations = TextDecorations.Underline;
            inlines.Add(inline);
        }
    }

    /// The same styling for a text layout an editor draws, where every character keeps its place.
    /// A divider's dashes are left clear: the editor rules a line over them.
    public static List<ValueSpan<TextRunProperties>> Overrides(IEnumerable<MarkdownSpan> spans)
    {
        var overrides = new List<ValueSpan<TextRunProperties>>();
        foreach (var span in spans)
        {
            overrides.Add(new ValueSpan<TextRunProperties>(span.Start, span.Length, new GenericTextRunProperties(
                new Typeface(span.Family, span.Style, span.Weight),
                span.Size,
                span.Underline ? TextDecorations.Underline : null,
                (span.Divider ? Colors.Transparent : span.Foreground).Brush(),
                span.Background?.Brush())));
        }
        return overrides;
    }

    /// Cuts a run where a search match starts or ends inside it.
    private static IEnumerable<(string Text, int Offset, bool Matched)> Split(string text, int position, IReadOnlyList<(int Start, int Length)>? matches)
    {
        if (matches is null || matches.Count == 0)
        {
            yield return (text, 0, false);
            yield break;
        }
        var from = 0;
        foreach (var (start, length) in matches)
        {
            var begin = Math.Max(start - position, from);
            var end = Math.Min(start + length - position, text.Length);
            if (end <= begin) continue;
            if (begin > from) yield return (text[from..begin], from, false);
            yield return (text[begin..end], begin, true);
            from = end;
        }
        if (from < text.Length) yield return (text[from..], from, false);
    }
}
