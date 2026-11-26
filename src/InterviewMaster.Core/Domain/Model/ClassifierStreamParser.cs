using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Incremental parser for the combined classify-and-answer reply:
///
///     STATUS:question|TOPIC:oop
///     ---
///     - answer bullet
///
/// The model sometimes drops the `---` separator or the whole STATUS header. This parser always
/// recovers the answer text, so a format slip costs nothing.
public static class ClassifierStreamParser
{
    /// <param name="StatusLine">First line of the reply when it is a STATUS header, e.g. "STATUS:question|TOPIC:oop".</param>
    /// <param name="FormatDrift">The reply started answering without a STATUS header.</param>
    /// <param name="Answer">Answer text visible so far. Header and separator removed; only ever grows by appending.</param>
    public readonly record struct Parse(string? StatusLine, bool FormatDrift, string Answer);

    /// A header line is short. A first line this long with no "STATUS" in it is already answer text.
    private const int DriftProbeLength = 12;

    /// `headerless` is for the answer-only prompt, where the reply is the answer and nothing else.
    public static Parse ParseReply(string fullText, bool headerless = false, bool streamEnded = false)
    {
        var text = fullText.TrimStart();
        if (headerless)
        {
            return new Parse(null, false, VisibleAnswer(text, streamEnded));
        }
        if (text.Length == 0) return new Parse(null, false, "");

        var newline = text.IndexOf('\n');
        if (newline < 0)
        {
            if (IsStatusLine(text))
            {
                // A header with no answer behind it yet (or ever, when the stream ended).
                return new Parse(streamEnded ? text : null, false, "");
            }
            var couldStillBeHeader = !streamEnded && text.Length < DriftProbeLength
                && "status:".HasPrefix(text.Lowercased().Prefix(7));
            if (couldStillBeHeader) return new Parse(null, false, "");
            return Drift(text, streamEnded);
        }

        var firstLine = text.Substring(0, newline);
        if (IsStatusLine(firstLine))
        {
            var rest = text.Substring(newline + 1);
            return new Parse(firstLine, false, VisibleAnswer(rest, streamEnded));
        }
        return Drift(text, streamEnded);
    }

    private static bool IsStatusLine(string line) => line.Lowercased().Contains("status:", StringComparison.Ordinal);

    private static Parse Drift(string text, bool streamEnded)
    {
        // Drop any header or separator lines the model scattered into an otherwise plain answer.
        var kept = text.SplitKeepingEmpty('\n').Where(line =>
        {
            var trimmed = line.TrimInline();
            return !IsStatusLine(trimmed) && !(trimmed.Length >= 3 && trimmed.All(c => c == '-'));
        });
        var answer = VisibleAnswer(string.Join("\n", kept), streamEnded);
        return new Parse(null, true, answer);
    }

    /// Strips a leading `---` separator. A lone "-" or "--" is held back because it may still turn
    /// into "---"; "- text" is a bullet and passes straight through.
    private static string VisibleAnswer(string text, bool streamEnded)
    {
        var body = text.TrimStart();
        if (body.HasPrefix("---"))
        {
            return body.TrimStart('-').TrimStart();
        }
        if (!streamEnded && (body == "-" || body == "--")) return "";
        return body;
    }
}
