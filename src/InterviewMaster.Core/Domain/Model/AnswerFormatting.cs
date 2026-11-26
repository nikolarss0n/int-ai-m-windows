using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

public enum RichStyle
{
    Body,
    /// The first label of an "X: value | Y: value" comparison.
    LabelFirst,
    /// Every later label of a comparison.
    LabelSecond,
    /// The bar between the sides of a comparison.
    Separator,
    /// A "STEP n" line.
    Header,
    /// "Why:", "Tip:", "Note:" and the like.
    Callout,
    Bold,
    InlineCode,
    /// A run of a fenced code block; `RichRun.Syntax` says which kind.
    Code
}

public readonly record struct RichRun(string Text, RichStyle Style, SyntaxKind Syntax = SyntaxKind.Plain);

public enum AnswerParagraphKind
{
    Text,
    Bullet,
    Code
}

/// One line of an answer. A bullet's marker is not part of its runs.
public sealed record AnswerParagraph(AnswerParagraphKind Kind, ValueList<RichRun> Runs)
{
    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

/// Reads an answer as the Mac app's `MessageViewFactory.formatMessageContent` does: bullets,
/// comparisons, callouts, **bold**, `code` and fenced code blocks.
public static partial class AnswerFormatter
{
    private static readonly string[] BulletPrefixes = { "• ", "- ", "* ", "· " };
    private static readonly string[] CalloutPrefixes = { "When to use:", "Use case:", "Tip:", "Note:", "Gotcha:" };

    [GeneratedRegex(@"\*\*([^*]+)\*\*|`([^`]+)`")]
    private static partial Regex InlineMarkup();

    public static List<AnswerParagraph> Format(string text)
    {
        var paragraphs = new List<AnswerParagraph>();
        var inCodeBlock = false;
        var code = new List<string>();
        var language = "";

        foreach (var line in text.Split('\n'))
        {
            if (line.HasPrefix("```"))
            {
                if (inCodeBlock)
                {
                    if (code.Count > 0) AddCode(paragraphs, string.Join("\n", code), language);
                    code.Clear();
                    language = "";
                    inCodeBlock = false;
                }
                else
                {
                    language = line.DropFirst(3).Trim(' ', '\t');
                    inCodeBlock = true;
                }
            }
            else if (inCodeBlock)
            {
                code.Add(line);
            }
            else
            {
                paragraphs.Add(FormatLine(line));
            }
        }
        return paragraphs;
    }

    private static void AddCode(List<AnswerParagraph> paragraphs, string code, string language)
    {
        var line = new List<RichRun>();
        foreach (var run in SyntaxHighlighter.Highlight(code, language.Length == 0 ? null : language))
        {
            var parts = run.Text.Split('\n');
            for (var index = 0; index < parts.Length; index++)
            {
                if (index > 0)
                {
                    paragraphs.Add(new AnswerParagraph(AnswerParagraphKind.Code, line));
                    line = new List<RichRun>();
                }
                if (parts[index].Length > 0) line.Add(new RichRun(parts[index], RichStyle.Code, run.Kind));
            }
        }
        paragraphs.Add(new AnswerParagraph(AnswerParagraphKind.Code, line));
    }

    private static AnswerParagraph FormatLine(string line)
    {
        var trimmed = line.Trim(' ', '\t');
        foreach (var prefix in BulletPrefixes)
        {
            if (!trimmed.HasPrefix(prefix)) continue;
            var runs = new List<RichRun>();
            AppendLine(runs, trimmed.DropFirst(prefix.Length), trimmed.DropFirst(prefix.Length).Trim(' ', '\t'), nested: true);
            return new AnswerParagraph(AnswerParagraphKind.Bullet, runs);
        }
        var result = new List<RichRun>();
        AppendLine(result, line, trimmed, nested: false);
        return new AnswerParagraph(AnswerParagraphKind.Text, result);
    }

    private static void AppendLine(List<RichRun> runs, string line, string trimmed, bool nested)
    {
        if (IsComparison(trimmed))
        {
            AppendComparison(runs, trimmed);
            return;
        }
        // Inside a bullet only a comparison is special.
        if (nested)
        {
            AppendInline(runs, line);
            return;
        }
        if (trimmed.Uppercased().HasPrefix("STEP "))
        {
            runs.Add(new RichRun(trimmed, RichStyle.Header));
            return;
        }
        if (trimmed.Lowercased().HasPrefix("why:"))
        {
            runs.Add(new RichRun("Why:", RichStyle.Callout));
            AppendInline(runs, trimmed.DropFirst(4));
            return;
        }
        foreach (var prefix in CalloutPrefixes)
        {
            if (!trimmed.Lowercased().HasPrefix(prefix.Lowercased())) continue;
            runs.Add(new RichRun(prefix, RichStyle.Callout));
            AppendInline(runs, trimmed.DropFirst(prefix.Length));
            return;
        }
        AppendInline(runs, line);
    }

    private static bool IsComparison(string trimmed) => trimmed.Contains(" | ") && trimmed.Contains(':');

    private static void AppendComparison(List<RichRun> runs, string trimmed)
    {
        var parts = trimmed.Split(" | ");
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var colon = part.IndexOf(':');
            if (colon >= 0)
            {
                runs.Add(new RichRun(part[..colon], index == 0 ? RichStyle.LabelFirst : RichStyle.LabelSecond));
                runs.Add(new RichRun(": ", RichStyle.Body));
                AppendInline(runs, part[(colon + 1)..].Trim(' ', '\t'));
            }
            else
            {
                AppendInline(runs, part);
            }
            if (index < parts.Length - 1) runs.Add(new RichRun("  │  ", RichStyle.Separator));
        }
    }

    private static void AppendInline(List<RichRun> runs, string text)
    {
        var last = 0;
        foreach (Match match in InlineMarkup().Matches(text))
        {
            if (match.Index > last) runs.Add(new RichRun(text[last..match.Index], RichStyle.Body));
            if (match.Groups[1].Success) runs.Add(new RichRun(match.Groups[1].Value, RichStyle.Bold));
            else if (match.Groups[2].Success) runs.Add(new RichRun(match.Groups[2].Value, RichStyle.InlineCode));
            last = match.Index + match.Length;
        }
        if (last < text.Length) runs.Add(new RichRun(text[last..], RichStyle.Body));
    }
}
