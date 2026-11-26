using InterviewMaster.Domain;

namespace InterviewMaster.Tests.Domain;

/// The text the window colours: Markdown source in the notes and the code panel, answers on the card,
/// code in a fence. These have no counterpart in `Tests/test_processor.swift`; they pin what
/// `MarkdownRenderer.swift`, `MessageViewFactory.swift` and `SyntaxHighlighter.swift` do.
public class MarkdownAndFormattingTests : SwiftStyleTests
{
    private void CheckEqual<T>(T actual, T expected, string message) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{message} (got {actual}, expected {expected})");

    private static string Join(IEnumerable<MarkdownRun> runs) => string.Concat(runs.Select(run => run.Text));

    private static MarkdownRun RunAt(List<MarkdownRun> runs, string text, string piece)
    {
        var at = text.IndexOf(piece, StringComparison.Ordinal);
        var position = 0;
        foreach (var run in runs)
        {
            if (at < position + run.Text.Length) return run;
            position += run.Text.Length;
        }
        throw new InvalidOperationException($"'{piece}' is not in the text");
    }

    [Fact]
    public void MarkdownKeepsTheSourceAndMarksIt()
    {
        const string text = "# Title\n## Sub\nSome **bold** and *slanted* and `code`.\n- item\n---\n[label](https://example.com)\n";
        var runs = MarkdownStyler.Style(text);
        CheckEqual(Join(runs), text, "styling never changes the text");
        CheckEqual(RunAt(runs, text, "Title").Face, MarkdownFace.Heading, "a single hash is a heading");
        CheckEqual(RunAt(runs, text, "# Title").Ink, MarkdownInk.Marker, "the hash itself is set back");
        CheckEqual(RunAt(runs, text, "# Title").Face, MarkdownFace.MarkerMono, "and stays the size of the body");
        CheckEqual(RunAt(runs, text, "Sub").Face, MarkdownFace.SubHeading, "two hashes are a subheading");
        CheckEqual(RunAt(runs, text, "bold").Face, MarkdownFace.Bold, "double stars are bold");
        CheckEqual(RunAt(runs, text, "**bold").Ink, MarkdownInk.Marker, "the stars are set back");
        Check(runs.Any(run => run.Text == "slanted" && run.Italic), "single stars slant the words between them");
        CheckEqual(RunAt(runs, text, "code`").Face, MarkdownFace.InlineCode, "backticks are inline code");
        CheckEqual(RunAt(runs, text, "`code").Ink, MarkdownInk.Marker, "a backtick is set back");
        CheckEqual(RunAt(runs, text, "`code").Fill, MarkdownFill.InlineCode, "but keeps the code's background");
        CheckEqual(RunAt(runs, text, "- item").Ink, MarkdownInk.ListMarker, "the dash of a list is marked");
        CheckEqual(RunAt(runs, text, "---").Face, MarkdownFace.Divider, "three dashes are a divider");
        var label = RunAt(runs, text, "label");
        Check(label.Ink == MarkdownInk.Link && label.Underline, "the words of a link are underlined in the link colour");
        CheckEqual(RunAt(runs, text, "https://").Ink, MarkdownInk.LinkUrl, "its address is set back");
        CheckEqual(RunAt(MarkdownStyler.Style("**[a](b)**"), "**[a](b)**", "a").Face, MarkdownFace.Bold, "a link inside bold stays bold");
    }

    [Fact]
    public void MarkdownLeavesFencedCodeAlone()
    {
        const string text = "Before\n```python\n# not a heading\ndef f():\n    return **x**\n```\nAfter **b**";
        var runs = MarkdownStyler.Style(text);
        CheckEqual(Join(runs), text, "styling never changes the text");
        CheckEqual(RunAt(runs, text, "```python").Ink, MarkdownInk.Marker, "the opening fence is set back");
        CheckEqual(RunAt(runs, text, "not a heading").Face, MarkdownFace.Code, "a hash inside a fence is code");
        CheckEqual(RunAt(runs, text, "x**").Face, MarkdownFace.Code, "stars inside a fence are code");
        CheckEqual(RunAt(runs, text, "x**").Fill, MarkdownFill.CodeBlock, "the fence has its own background");
        Check(runs.Any(run => run.Text == "def" && run.Face == MarkdownFace.Code && run.Syntax == SyntaxKind.Keyword), "code in a fence is highlighted");
        CheckEqual(RunAt(runs, text, "b**").Face, MarkdownFace.Bold, "Markdown carries on after the fence");
    }

    [Fact]
    public void NotesListCarriesOnAndEnds()
    {
        var carried = NotesEditing.ContinueList("- one\n", 6);
        CheckEqual(carried?.Apply("- one\n"), "- one\n- ", "Enter after a bullet starts the next one");
        CheckEqual(carried?.CaretAfter, 8, "the caret sits after the new bullet");
        var ended = NotesEditing.ContinueList("- one\n- \n", 9);
        CheckEqual(ended?.Apply("- one\n- \n"), "- one\n", "Enter on an empty bullet ends the list");
        CheckEqual(ended?.CaretAfter, 6, "the caret goes back to where the bullet was");
        Check(NotesEditing.ContinueList("plain\n", 6) is null, "a plain line is left alone");
        Check(NotesEditing.ContinueList("- one", 5) is null, "nothing happens without a new line");
    }

    [Fact]
    public void NotesFormattingWrapsTheSelection()
    {
        CheckEqual(NotesEditing.Wrap("make this bold", 5, 4, "**", "**")?.Apply("make this bold"), "make **this** bold", "bold wraps the selection in stars");
        Check(NotesEditing.Wrap("nothing selected", 3, 0, "**", "**") is null, "formatting needs a selection");
        CheckEqual(NotesEditing.Bullet("a\n\nb", 0, 4)?.Apply("a\n\nb"), "- a\n\n- b", "each line with words becomes a bullet");
        CheckEqual(NotesEditing.InsertDivider("ab", 1, 0).Apply("ab"), "a\n\n---\n\nb", "a divider goes in at the caret");
        CheckEqual(NotesEditing.NormalizedLineEnds("a\r\nb\rc"), "a\nb\nc", "pasted line ends become plain new lines");
        CheckEqual(NotesEditing.Matches("Hash map and hashmap, HASH.", "hash").Count, 3, "search ignores case");
        CheckEqual(NotesEditing.Matches("aaaa", "aa").Count, 2, "matches do not overlap");
        CheckEqual(NotesEditing.Matches("anything", "").Count, 0, "an empty search finds nothing");
    }

    [Fact]
    public void TemplateBecomesNotes()
    {
        var template = new InterviewTemplate(
            "t", "Warm-up", "Two easy ones", TemplateCategory.Behavioral,
            new[]
            {
                new TemplateQuestion("Tell me about yourself", "intro", TemplateDifficulty.Easy, new[] { "Keep it to two minutes" }),
                new TemplateQuestion("Why this role?", "motivation", TemplateDifficulty.Medium, Array.Empty<string>()),
            });
        var markdown = NotesEditing.TemplateMarkdown(template);
        Check(markdown.StartsWith("# Warm-up\n\n*Two easy ones*\n\n## 1. Tell me about yourself\n\n", StringComparison.Ordinal), "the template's name, description and first question lead");
        Check(markdown.Contains("**Topic:** intro | **Difficulty:** Easy\n\n**Hints:**\n- Keep it to two minutes\n\n**Your notes:**\n\n\n"), "a question carries its topic, difficulty and hints");
        Check(markdown.Contains("## 2. Why this role?\n\n**Topic:** motivation | **Difficulty:** Medium\n\n**Your notes:**"), "a question without hints has no hints heading");
        Check(NotesEditing.DefaultNotes.StartsWith("# Common Interview Questions\n", StringComparison.Ordinal), "the first notes are the sample questions");
        Check(NotesEditing.DefaultNotes.EndsWith("**Add your own notes below...**", StringComparison.Ordinal), "and they end by inviting more");
    }

    [Fact]
    public void AnswerFormatterReadsTheModelsConventions()
    {
        var paragraphs = AnswerFormatter.Format("- First point\n• Second **strong** `x`\nPlain line\n```python\nreturn 1\n```");
        CheckEqual(paragraphs[0].Kind, AnswerParagraphKind.Bullet, "a dash starts a bullet");
        CheckEqual(paragraphs[0].PlainText, "First point", "the bullet's mark is not part of its words");
        CheckEqual(paragraphs[1].Kind, AnswerParagraphKind.Bullet, "a dot starts a bullet");
        Check(paragraphs[1].Runs.Any(run => run.Style == RichStyle.Bold && run.Text == "strong"), "stars make words strong and are dropped");
        Check(paragraphs[1].Runs.Any(run => run.Style == RichStyle.InlineCode && run.Text == "x"), "backticks make code and are dropped");
        CheckEqual(paragraphs[2].Kind, AnswerParagraphKind.Text, "anything else is a paragraph");
        Check(paragraphs.Any(paragraph => paragraph.Kind == AnswerParagraphKind.Code && paragraph.PlainText == "return 1"), "a fence is code, a line at a time");
        Check(paragraphs.All(paragraph => !paragraph.PlainText.Contains("```")), "the fence itself is not shown");
    }

    [Fact]
    public void SyntaxHighlighterKeepsTheCodeAndNamesItsParts()
    {
        const string code = "def reverse(text):\n    # backwards\n    return text[::-1] + \"x\" + 42";
        var runs = SyntaxHighlighter.Highlight(code, "python");
        CheckEqual(string.Concat(runs.Select(run => run.Text)), code, "highlighting never changes the code");
        Check(runs.Any(run => run.Text == "def" && run.Kind == SyntaxKind.Keyword), "def is a keyword");
        Check(runs.Any(run => run.Text.Contains("# backwards") && run.Kind == SyntaxKind.Comment), "a hash starts a comment in Python");
        Check(runs.Any(run => run.Text == "\"x\"" && run.Kind == SyntaxKind.String), "quotes hold a string");
        Check(runs.Any(run => run.Text == "42" && run.Kind == SyntaxKind.Number), "digits are a number");
        CheckEqual(SyntaxHighlighter.DetectLanguage("public class A { public static void main() {} }"), "java", "Java is recognised by its class line");
    }
}
