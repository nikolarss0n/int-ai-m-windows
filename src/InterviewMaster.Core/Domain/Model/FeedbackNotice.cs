using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// How much a message matters, which decides what it may push aside and how long it stays.
public enum FeedbackKind
{
    Info,
    Success,
    Warning,
    Error
}

/// One line of feedback about something that just happened. A confirmation says so and goes; an error
/// stays until it is dismissed or the thing it is about has gone right again.
/// <param name="Key">What it is about. A second notice with the same key replaces the first, and resolving the key removes it.</param>
/// <param name="Detail">The longer explanation, for hover and for the screen reader.</param>
/// <param name="ActionTitle">A way out or a way back: "Undo", "Show", "Try again". The controller knows what it does.</param>
/// <param name="Lasts">Seconds it stays; null means until it is dismissed or resolved.</param>
public sealed record FeedbackNotice(
    string Key,
    FeedbackKind Kind,
    string Message,
    string? Detail = null,
    string? ActionTitle = null,
    double? Lasts = null)
{
    public const double ConfirmationHold = 3.5;

    /// A notice with an action needs longer: the person has to read it and reach for the button.
    public const double UndoHold = 8;

    /// Something worked. It says so and goes; with an action ("Show", "Undo") it stays a little longer.
    public static FeedbackNotice Confirmation(string key, string message, string? detail = null, string? actionTitle = null) =>
        new(key, FeedbackKind.Success, message, detail, actionTitle, actionTitle is null ? ConfirmationHold : UndoHold);

    /// Something to know that needs nothing done, or something done that can be taken back.
    public static FeedbackNotice Info(string key, string message, string? detail = null, string? actionTitle = null) =>
        new(key, FeedbackKind.Info, message, detail, actionTitle, actionTitle is null ? ConfirmationHold : UndoHold);

    /// Something failed. It stays until it is dismissed or the thing it is about goes right.
    public static FeedbackNotice Error(string key, string message, string? detail = null, string? actionTitle = null) =>
        new(key, FeedbackKind.Error, message, detail, actionTitle, null);

    internal int Rank => Kind switch
    {
        FeedbackKind.Info or FeedbackKind.Success => 0,
        FeedbackKind.Warning => 1,
        _ => 2
    };
}

/// The one notice on screen. Time is passed in, so the rules can be tested without waiting.
public sealed class NoticeBoard
{
    private DateTime? shownAt;

    public FeedbackNotice? Current { get; private set; }

    /// Shows `notice` unless something more important is already showing (an error is not covered by a
    /// confirmation). Returns whether it was shown.
    public bool Show(FeedbackNotice notice, DateTime now)
    {
        Expire(now);
        if (Current is not null && Current.Key != notice.Key && Current.Rank > notice.Rank) return false;
        Current = notice;
        shownAt = now;
        return true;
    }

    public void Dismiss()
    {
        Current = null;
        shownAt = null;
    }

    /// The thing the notice was about has gone right again.
    public void Resolve(string key)
    {
        if (Current?.Key == key) Dismiss();
    }

    /// Removes a notice that has lasted as long as it was meant to.
    public void Expire(DateTime now)
    {
        if (Current?.Lasts is not double lasts || shownAt is not DateTime shown) return;
        if ((now - shown).TotalSeconds >= lasts) Dismiss();
    }

    /// When the notice on screen goes by itself, for scheduling a refresh.
    public DateTime? Expiry =>
        Current?.Lasts is double lasts && shownAt is DateTime shown ? shown.AddSeconds(lasts) : null;
}

/// The words for what went wrong, in the terms of what the person can do about it: what failed, then
/// the fix. A line the person cannot act on says what may have been lost instead.
public static class FeedbackCopy
{
    /// What a failure was, read from the words of its error. The header notice and the failed card both
    /// take their wording from this, so they never name two causes for one failure.
    public enum Cause
    {
        Network,
        Timeout,
        KeyRejected,
        Busy,
        NoText,
        Other
    }

    /// The labels of a notice's button when the fix is somewhere else, or is to do it again.
    public const string OpenSettings = "Open Settings";
    public const string TryAgain = "Try again";
    public const string ShowInFinder = PlatformCopy.ShowInFileManager;
    public const string Export = "Export";
    public const string LastQuestionMissed = "The last question may not have been captured.";
    public const string DidntCatchThat = "Didn't catch that. Ask again.";

    public static Cause CauseOf(string message)
    {
        var lowered = Reason(message).Lowercased();
        if (lowered.Contains("no answer text")) return Cause.NoText;
        if (lowered.Contains("timed out") || lowered.Contains("timeout")) return Cause.Timeout;
        if (lowered.Contains("network") || lowered.Contains("offline") || lowered.Contains("connection") || lowered.Contains("internet")) return Cause.Network;
        if (lowered.Contains("401") || lowered.Contains("unauthorized") || lowered.Contains("api key") || lowered.Contains("x-api-key")) return Cause.KeyRejected;
        if (lowered.Contains("429") || lowered.Contains("rate limit") || lowered.Contains("overloaded")) return Cause.Busy;
        return Cause.Other;
    }

    /// The notice for a status line from the pipeline. Only errors get one; progress is not news. Every
    /// status error comes from the audio turn, so each says what happened to the question.
    public static FeedbackNotice? NoticeForStatus(string message)
    {
        var text = message.Trim();
        if (text == DidntCatchThat) return FeedbackNotice.Confirmation("heard", text);
        if (!text.Lowercased().HasPrefix("error:")) return null;
        var detail = Reason(text);

        static FeedbackNotice Error(string line, string? hint, string? action = null) =>
            new("pipeline", FeedbackKind.Error, line, hint, action);

        switch (CauseOf(detail))
        {
            case Cause.Network:
                return Error("Couldn't transcribe. Check your connection.", $"Transcription needs a connection to Groq. {LastQuestionMissed}");
            case Cause.Timeout:
                return Error("Transcription timed out. Ask again.", $"{LastQuestionMissed} If it keeps happening, check your connection.");
            case Cause.KeyRejected:
                return Error("Groq API key rejected", $"{LastQuestionMissed} Check the key in Settings.", OpenSettings);
            case Cause.Busy:
                return Error("Transcription is busy. Ask again in a moment.", $"{LastQuestionMissed} The service is handling too many requests.");
            default:
                var parts = new List<string>();
                if (detail.Length > 0) parts.Add(Sentence(detail));
                parts.Add(LastQuestionMissed);
                return Error("Couldn't transcribe. Ask again.", string.Join(" ", parts));
        }
    }

    /// Why an answer failed, in a line a person can act on. `message` is the pipeline's "Error: ..." text.
    public static string FailureReason(string message)
    {
        var text = Reason(message);
        switch (CauseOf(text))
        {
            case Cause.NoText: return "The model sent back no text.";
            case Cause.Timeout: return "The model took too long to answer.";
            case Cause.Network: return "The network connection failed. Check it, then try again.";
            case Cause.KeyRejected: return $"The API key was rejected. Check it in Settings or {PlatformCopy.KeysFileDisplayPath}.";
            case Cause.Busy: return "The service is busy. Wait a moment and try again.";
        }
        if (text.Length == 0) return "The model did not answer.";
        var firstSentence = FirstSentence(text);
        var clipped = firstSentence.Length > 90 ? firstSentence.Prefix(89) + "…" : firstSentence;
        return Sentence(clipped);
    }

    /// Swift `split(separator: ".", maxSplits: 1, omittingEmptySubsequences: true).first ?? text`.
    private static string FirstSentence(string text)
    {
        var body = text.TrimStart('.');
        if (body.Length == 0) return text;
        var stop = body.IndexOf('.');
        return stop < 0 ? body : body.Substring(0, stop);
    }

    /// Analyze with nothing captured: what to do first is on the line, what comes after is on hover.
    public static readonly FeedbackNotice NothingToAnalyze = FeedbackNotice.Info(
        "analyze",
        $"No screenshots yet. Press {PlatformCopy.CaptureShortcut} to capture one.",
        detail: $"Then press {PlatformCopy.AnalyzeShortcut} to analyze.");

    /// Clicks go to the app behind, so the way back has to be on screen.
    public static readonly FeedbackNotice ClickThroughOn = FeedbackNotice.Info(
        "clickThrough", $"Click-through is on. Press {PlatformCopy.ClickThroughShortcut} to use this window.");

    public static readonly FeedbackNotice NothingToExport = FeedbackNotice.Info(
        "export", "Nothing to export yet", detail: "Export saves the questions and answers from an interview. Start one first.");

    public static FeedbackNotice Exported(string fileName, string path) =>
        FeedbackNotice.Confirmation("export", $"Exported {fileName}", detail: path);

    public static FeedbackNotice SessionSaved(string path) =>
        FeedbackNotice.Confirmation("session", "Session saved", detail: path, actionTitle: ShowInFinder);

    /// The automatic save failed. Export writes the same interview somewhere else, so the button is there.
    public static FeedbackNotice SessionNotSaved(string reason) =>
        FeedbackNotice.Error("session", "Couldn't save the session", detail: Sentence(reason) + " Export keeps a copy somewhere else.", actionTitle: Export);

    /// The screenshot could not be taken. Doing it again is the fix, so the button is next to the error.
    public static FeedbackNotice ScreenshotFailed(string detail) =>
        new("screenshot", FeedbackKind.Error, "Couldn't capture the screenshot", detail, TryAgain);

    /// What a clear took away, or null when there was nothing to take.
    public static string? Cleared(int screenshots, bool pinnedCode) => Summary("Cleared", screenshots, pinnedCode);

    /// What Undo put back, in the same words the clear used.
    public static string? Restored(int screenshots, bool pinnedCode) => Summary("Restored", screenshots, pinnedCode);

    /// A reason as one sentence: it ends in a full stop unless it already ends in punctuation.
    public static string Sentence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return "";
        return ".!?…".Contains(trimmed[^1]) ? trimmed : trimmed + ".";
    }

    /// The message without its "Error:" prefix.
    private static string Reason(string message)
    {
        var text = message.Trim();
        if (!text.Lowercased().HasPrefix("error:")) return text;
        return text.DropFirst("error:".Length).Trim();
    }

    private static string? Summary(string verb, int screenshots, bool pinnedCode)
    {
        var parts = new List<string>();
        if (screenshots > 0) parts.Add(screenshots == 1 ? "1 screenshot" : $"{screenshots} screenshots");
        if (pinnedCode) parts.Add("the pinned code");
        if (parts.Count == 0) return null;
        return verb + " " + string.Join(" and ", parts);
    }
}
