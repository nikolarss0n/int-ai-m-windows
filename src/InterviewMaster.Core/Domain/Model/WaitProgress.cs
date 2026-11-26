namespace InterviewMaster.Domain;

/// How long a wait has lasted decides what the interface says about it: at first only what is
/// happening, then the seconds, then that it is taking longer than usual.
public enum WaitStage
{
    Brief,
    Counting,
    Stalled
}

public static class WaitProgress
{
    /// After this many seconds a wait shows how long it has been.
    public const double ShowSecondsAfter = 4;

    /// After this many it says it is taking longer than usual.
    public const double StalledAfter = 10;

    public static WaitStage Stage(double elapsed)
    {
        if (elapsed >= StalledAfter) return WaitStage.Stalled;
        if (elapsed >= ShowSecondsAfter) return WaitStage.Counting;
        return WaitStage.Brief;
    }

    /// "7s", and "1m 05s" once a minute has passed.
    public static string Format(double elapsed)
    {
        var whole = Math.Max(0, (int)elapsed);
        return whole < 60 ? $"{whole}s" : $"{whole / 60}m {whole % 60:00}s";
    }

    /// What an answer card says about an answer that has not started to appear.
    public static string AnswerStatus(bool queued, double elapsed, string separator)
    {
        if (queued) return "Queued";
        return Status("Generating", "Still generating", elapsed, separator);
    }

    /// The same for a card too narrow for the whole line: once the wait has a length, the length is what
    /// is left, because the placeholder and the strip already say what is happening.
    public static string AnswerStatusShort(bool queued, double elapsed)
    {
        if (queued) return "Queued";
        return Stage(elapsed) == WaitStage.Brief ? "Generating" : Format(elapsed);
    }

    /// What the code panel says about code that has not started to appear.
    public static string CodeStatus(double elapsed, string separator) =>
        Status("Writing", "Still writing", elapsed, separator);

    private static string Status(string brief, string stalled, double elapsed, string separator) => Stage(elapsed) switch
    {
        WaitStage.Brief => brief,
        WaitStage.Counting => brief + separator + Format(elapsed),
        _ => stalled + separator + Format(elapsed)
    };
}

/// Work that only earns an indicator once it has lasted a moment (a wait of a tenth of a second
/// flashes an indicator and is gone), and that stops earning one when nothing ever reports its end.
/// Time is passed in, so the rule can be tested without waiting.
public sealed class DelayedActivity
{
    private readonly Dictionary<Guid, DateTime> starts = new();

    public DelayedActivity(double showAfter, double expireAfter)
    {
        ShowAfter = showAfter;
        ExpireAfter = expireAfter;
    }

    public double ShowAfter { get; }

    public double ExpireAfter { get; }

    public bool IsEmpty => starts.Count == 0;

    /// A second begin for the same work keeps the first start.
    public void Begin(Guid id, DateTime now) => starts.TryAdd(id, now);

    public void End(Guid id) => starts.Remove(id);

    public void RemoveAll() => starts.Clear();

    public bool IsShowing(DateTime now) =>
        starts.Values.Any(start => Age(start, now) >= ShowAfter && Age(start, now) < ExpireAfter);

    /// The next moment `IsShowing` can change on its own, for scheduling a refresh.
    public DateTime? NextChange(DateTime now)
    {
        var moments = starts.Values
            .SelectMany(start => new[] { start.AddSeconds(ShowAfter), start.AddSeconds(ExpireAfter) })
            .Where(moment => moment > now)
            .ToList();
        return moments.Count == 0 ? null : moments.Min();
    }

    private static double Age(DateTime start, DateTime now) => (now - start).TotalSeconds;
}
