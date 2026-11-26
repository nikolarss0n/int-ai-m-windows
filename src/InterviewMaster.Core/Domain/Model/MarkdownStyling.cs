using System.Text.RegularExpressions;

namespace InterviewMaster.Domain;

/// Which face a stretch of Markdown source is set in.
public enum MarkdownFace
{
    Body,
    MarkerMono,
    Heading,
    SubHeading,
    Bold,
    InlineCode,
    ListMarker,
    Divider,
    Code
}

/// Which colour it takes.
public enum MarkdownInk
{
    Body,
    Marker,
    Heading,
    Bold,
    InlineCode,
    ListMarker,
    Divider,
    Link,
    LinkUrl,
    Syntax
}

/// What lies behind it.
public enum MarkdownFill
{
    None,
    CodeBlock,
    InlineCode
}

public readonly record struct MarkdownRun(
    string Text, MarkdownFace Face, MarkdownInk Ink, MarkdownFill Fill = MarkdownFill.None,
    SyntaxKind Syntax = SyntaxKind.Plain, bool Italic = false, bool Underline = false);

/// Marks up Markdown source the way `MarkdownRenderer.swift` attributes it: fenced code first, which
/// nothing else may touch, then headings, bold, italic, inline code, list markers, dividers and links,
/// each laid over the one before. The source stays as typed: markers are set back, not removed.
public static partial class MarkdownStyler
{
    /// What the Mac draws in place of a divider line.
    public const string DividerLine = "────────────────────────────────────────────────";

    [GeneratedRegex(@"```(\w*)\n([\s\S]*?)```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(#{2,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex SubHeading();

    [GeneratedRegex(@"(?<!\*)\*\*(?!\*)(.+?)(?<!\*)\*\*(?!\*)")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex Italic();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^([ \t]*)([-*+]|\d+\.)\s", RegexOptions.Multiline)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^[ \t]*(?:[-*_]{3,}|─{3,})[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Divider();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkPattern();

    public static List<MarkdownRun> Style(string text)
    {
        var face = new MarkdownFace[text.Length];
        var ink = new MarkdownInk[text.Length];
        var fill = new MarkdownFill[text.Length];
        var syntax = new SyntaxKind[text.Length];
        var italic = new bool[text.Length];
        var underline = new bool[text.Length];
        var excluded = new List<(int Start, int End)>();

        void Set<T>(T[] layer, int start, int length, T value)
        {
            for (var index = Math.Max(0, start); index < Math.Min(text.Length, start + length); index++) layer[index] = value;
        }
        bool Excluded(Match match) => excluded.Any(range => match.Index < range.End && match.Index + match.Length > range.Start);

        foreach (Match match in CodeBlock().Matches(text))
        {
            excluded.Add((match.Index, match.Index + match.Length));
            Set(fill, match.Index, match.Length, MarkdownFill.CodeBlock);
            Set(face, match.Index, match.Length, MarkdownFace.Code);
            var language = match.Groups[1];
            var code = match.Groups[2];
            Set(ink, match.Index, language.Index + language.Length + 1 - match.Index, MarkdownInk.Marker);
            var runs = SyntaxHighlighter.Highlight(code.Value, language.Length == 0 ? null : language.Value);
            if (runs.Sum(run => run.Text.Length) == code.Length)
            {
                var position = code.Index;
                foreach (var run in runs)
                {
                    Set(ink, position, run.Text.Length, MarkdownInk.Syntax);
                    Set(syntax, position, run.Text.Length, run.Kind);
                    position += run.Text.Length;
                }
            }
            Set(ink, match.Index + match.Length - 3, 3, MarkdownInk.Marker);
            Set(syntax, match.Index + match.Length - 3, 3, SyntaxKind.Plain);
        }

        foreach (Match match in Heading().Matches(text))
        {
            if (Excluded(match)) continue;
            Set(ink, match.Index, 1, MarkdownInk.Marker);
            Set(face, match.Index, 1, MarkdownFace.MarkerMono);
            Set(face, match.Groups[1].Index, match.Groups[1].Length, MarkdownFace.Heading);
            Set(ink, match.Groups[1].Index, match.Groups[1].Length, MarkdownInk.Heading);
        }
        foreach (Match match in SubHeading().Matches(text))
        {
            if (Excluded(match)) continue;
            Set(ink, match.Groups[1].Index, match.Groups[1].Length, MarkdownInk.Marker);
            Set(face, match.Groups[1].Index, match.Groups[1].Length, MarkdownFace.MarkerMono);
            Set(face, match.Groups[2].Index, match.Groups[2].Length, MarkdownFace.SubHeading);
            Set(ink, match.Groups[2].Index, match.Groups[2].Length, MarkdownInk.Heading);
        }
        foreach (Match match in Bold().Matches(text))
        {
            if (Excluded(match)) continue;
            var content = match.Groups[1];
            Set(face, content.Index, content.Length, MarkdownFace.Bold);
            Set(ink, content.Index, content.Length, MarkdownInk.Bold);
            Set(ink, match.Index, 2, MarkdownInk.Marker);
            Set(ink, content.Index + content.Length, 2, MarkdownInk.Marker);
        }
        foreach (Match match in Italic().Matches(text))
        {
            if (Excluded(match)) continue;
            var content = match.Groups[1];
            Set(italic, content.Index, content.Length, true);
            Set(ink, match.Index, 1, MarkdownInk.Marker);
            Set(ink, content.Index + content.Length, 1, MarkdownInk.Marker);
        }
        foreach (Match match in InlineCode().Matches(text))
        {
            if (Excluded(match)) continue;
            Set(face, match.Index, match.Length, MarkdownFace.InlineCode);
            Set(ink, match.Index, match.Length, MarkdownInk.InlineCode);
            Set(fill, match.Index, match.Length, MarkdownFill.InlineCode);
            Set(ink, match.Index, 1, MarkdownInk.Marker);
            Set(ink, match.Index + match.Length - 1, 1, MarkdownInk.Marker);
        }
        foreach (Match match in ListItem().Matches(text))
        {
            if (Excluded(match)) continue;
            Set(face, match.Groups[2].Index, match.Groups[2].Length, MarkdownFace.ListMarker);
            Set(ink, match.Groups[2].Index, match.Groups[2].Length, MarkdownInk.ListMarker);
        }
        foreach (Match match in Divider().Matches(text))
        {
            if (Excluded(match) || match.Length == 0) continue;
            Set(face, match.Index, match.Length, MarkdownFace.Divider);
            Set(ink, match.Index, match.Length, MarkdownInk.Divider);
            Set(fill, match.Index, match.Length, MarkdownFill.None);
            Set(italic, match.Index, match.Length, false);
        }
        foreach (Match match in LinkPattern().Matches(text))
        {
            if (Excluded(match)) continue;
            var label = match.Groups[1];
            var url = match.Groups[2];
            Set(ink, label.Index, label.Length, MarkdownInk.Link);
            Set(underline, label.Index, label.Length, true);
            Set(ink, match.Index, 1, MarkdownInk.Marker);
            Set(ink, label.Index + label.Length, 1, MarkdownInk.Marker);
            Set(ink, url.Index - 1, 1, MarkdownInk.Marker);
            Set(ink, url.Index + url.Length, 1, MarkdownInk.Marker);
            Set(ink, url.Index, url.Length, MarkdownInk.LinkUrl);
        }

        var result = new List<MarkdownRun>();
        var from = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index < text.Length
                && face[index] == face[from] && ink[index] == ink[from] && fill[index] == fill[from]
                && syntax[index] == syntax[from] && italic[index] == italic[from] && underline[index] == underline[from]) continue;
            result.Add(new MarkdownRun(text[from..index], face[from], ink[from], fill[from], syntax[from], italic[from], underline[from]));
            from = index;
        }
        return result;
    }
}

/// What the notes editor does to the text besides typing: carrying a list on, wrapping the selection
/// in Markdown, searching, and laying a template out. Each takes the text and returns the new text
/// and selection, so the editor itself stays a text box.
public static class NotesEditing
{
    public const string StorageKey = "InterviewMaster.SavedNotes";

    /// One replacement in the text: what the editor does as a single undoable step.
    public readonly record struct Edit(int Start, int Length, string Replacement)
    {
        public int CaretAfter => Start + Replacement.Length;

        public string Apply(string text) => text.Remove(Start, Length).Insert(Start, Replacement);
    }

    /// After Enter on a bullet line: the next bullet, or the list ends when the bullet was empty.
    /// `caret` is where the caret sits after the new line. Null when there is nothing to do.
    public static Edit? ContinueList(string text, int caret)
    {
        if (caret <= 0 || caret > text.Length || text[caret - 1] != '\n') return null;
        var lineStart = caret - 2;
        while (lineStart > 0 && text[lineStart] != '\n') lineStart--;
        if (lineStart > 0) lineStart++;
        var lineLength = caret - lineStart - 1;
        if (lineLength <= 0 || lineStart < 0) return null;
        var previous = text.Substring(lineStart, lineLength);
        if (!previous.StartsWith("- ", StringComparison.Ordinal)) return null;
        return previous[2..].Trim(' ', '\t').Length == 0
            ? new Edit(lineStart, lineLength + 1, "")
            : new Edit(caret, 0, "- ");
    }

    /// Puts `prefix` and `suffix` around the selection. Null when nothing is selected.
    public static Edit? Wrap(string text, int start, int length, string prefix, string suffix)
    {
        if (length <= 0 || start < 0 || start + length > text.Length) return null;
        return new Edit(start, length, prefix + text.Substring(start, length) + suffix);
    }

    /// Makes each selected line a bullet. Null when nothing is selected.
    public static Edit? Bullet(string text, int start, int length)
    {
        if (length <= 0 || start < 0 || start + length > text.Length) return null;
        var lines = text.Substring(start, length).Split('\n');
        return new Edit(start, length, string.Join("\n", lines.Select(line => line.Length == 0 ? "" : "- " + line)));
    }

    public static Edit InsertDivider(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        return new Edit(start, Math.Clamp(length, 0, text.Length - start), "\n\n---\n\n");
    }

    public const string NewNote = "# New Note\n\n";

    /// Line ends as Markdown expects them, whatever was pasted.
    public static string NormalizedLineEnds(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// Where `term` occurs, ignoring case, without overlaps.
    public static List<(int Start, int Length)> Matches(string text, string term)
    {
        var found = new List<(int, int)>();
        if (term.Length == 0) return found;
        var from = 0;
        while (from <= text.Length - term.Length)
        {
            var at = text.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            found.Add((at, term.Length));
            from = at + term.Length;
        }
        return found;
    }

    /// A template as notes to fill in: each question with its topic, hints and room to write.
    public static string TemplateMarkdown(InterviewTemplate template)
    {
        var markdown = new System.Text.StringBuilder();
        markdown.Append($"# {template.Name}\n\n*{template.Description}*\n\n");
        for (var index = 0; index < template.Questions.Count; index++)
        {
            var question = template.Questions[index];
            var difficulty = question.Difficulty.RawValue();
            markdown.Append($"## {index + 1}. {question.Text}\n\n");
            markdown.Append($"**Topic:** {question.Topic} | **Difficulty:** {char.ToUpperInvariant(difficulty[0])}{difficulty[1..]}\n\n");
            if (question.Hints.Count > 0)
            {
                markdown.Append("**Hints:**\n");
                foreach (var hint in question.Hints) markdown.Append($"- {hint}\n");
                markdown.Append('\n');
            }
            markdown.Append("**Your notes:**\n\n\n");
        }
        return markdown.ToString();
    }

    public const string DefaultNotes = """
        # Common Interview Questions

        ## JavaScript/TypeScript

        **Q: What is the difference between `let`, `const`, and `var`?**
        A: `var` is function-scoped, `let` and `const` are block-scoped. `const` cannot be reassigned.

        **Q: Explain closures**
        A: Functions that have access to variables from outer scope even after outer function has returned.

        **Q: What is event delegation?**
        A: Technique of handling events at parent level using event bubbling instead of adding listeners to each child.

        ## System Design

        **Q: Design a URL shortener**
        - Hash function (MD5/Base62)
        - Database: key-value store (Redis)
        - Cache layer
        - Load balancer
        - Analytics tracking

        **Q: Design Twitter feed**
        - Fan-out on write vs read
        - Timeline service
        - Caching strategy
        - Pagination

        ## React

        **Q: useEffect vs useLayoutEffect?**
        A: useEffect runs after paint, useLayoutEffect runs synchronously before paint.

        **Q: What are React keys?**
        A: Unique identifiers to help React identify which items changed/added/removed in lists.

        ---

        ## Code Example

        Here's a sample Python function:

        ```python
        def two_sum(nums, target):
            # Hash map to store value -> index
            seen = {}
            for i, num in enumerate(nums):
                complement = target - num
                if complement in seen:
                    return [seen[complement], i]
                seen[num] = i
            return []
        ```

        The function uses a **hash map** for `O(n)` time complexity.

        ---

        **Add your own notes below...**
        """;
}
