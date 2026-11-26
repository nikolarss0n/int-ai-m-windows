namespace InterviewMaster.Domain;

public enum QuestionBurstPhase
{
    Queued,
    Generating,
    Answering,
    Ready,
    Failed
}

public static class QuestionBurstPhases
{
    public static string Title(this QuestionBurstPhase phase) => phase switch
    {
        QuestionBurstPhase.Queued => "Queued",
        QuestionBurstPhase.Generating => "Generating",
        QuestionBurstPhase.Answering => "Answering",
        QuestionBurstPhase.Ready => "Ready",
        _ => "Failed"
    };

    /// Ready is the resting state, so a ready question carries no pill: only work and failures are marked.
    public static bool ShowsPill(this QuestionBurstPhase phase) => phase != QuestionBurstPhase.Ready;
}

public sealed record QuestionBurstEntry(
    Guid Id,
    int Sequence,
    string Question,
    string Topic,
    DateTime DetectedAt,
    QuestionBurstPhase Phase,
    string Answer,
    int? LatencyMs,
    /// Why the answer failed, in a line a person can act on; null unless `Phase` is failed.
    string? Failure = null,
    /// The question carries on from the one asked before it.
    bool FollowsPrevious = false);

/// The questions of an interview and the one whose answer is on screen.
public sealed class QuestionBurstState
{
    private readonly List<QuestionBurstEntry> entries = new();
    private readonly HashSet<Guid> activeAIWork = new();

    public IReadOnlyList<QuestionBurstEntry> Entries => entries;
    public Guid? SelectedId { get; private set; }
    public IReadOnlySet<Guid> ActiveAIWork => activeAIWork;

    private List<QuestionBurstEntry> NewestFirst => entries.OrderByDescending(entry => entry.Sequence).ToList();

    public List<QuestionBurstEntry> VisibleEntries => NewestFirst.Take(3).ToList();

    /// The three rows on screen: the newest, or the three around an older question reached by keyboard.
    public List<QuestionBurstEntry> DisplayedEntries
    {
        get
        {
            var newestFirst = NewestFirst;
            var index = SelectedId is { } id ? newestFirst.FindIndex(entry => entry.Id == id) : -1;
            if (index < 3) return newestFirst.Take(3).ToList();
            var start = Math.Min(index - 1, newestFirst.Count - 3);
            return newestFirst.GetRange(start, 3);
        }
    }

    /// The question's place in the interview, counted from the first. It never changes as more are asked.
    public int? QuestionNumber(Guid? id)
    {
        if (id is not { } wanted) return null;
        var index = entries.OrderBy(entry => entry.Sequence).ToList().FindIndex(entry => entry.Id == wanted);
        return index < 0 ? null : index + 1;
    }

    public QuestionBurstEntry? SelectedEntry =>
        SelectedId is { } id ? entries.FirstOrDefault(entry => entry.Id == id) : VisibleEntries.FirstOrDefault();

    public bool IsAIWorking => activeAIWork.Count > 0;

    public Guid? LatestActiveWorkId =>
        entries.Where(entry => activeAIWork.Contains(entry.Id)).OrderByDescending(entry => entry.Sequence).FirstOrDefault()?.Id;

    public void BeginAIWork(Guid turnId)
    {
        activeAIWork.Add(turnId);
        UpdatePhase(QuestionBurstPhase.Generating, turnId, unlessReady: true);
    }

    public void EndAIWork(Guid turnId) => activeAIWork.Remove(turnId);

    public void EndAllAIWork() => activeAIWork.Clear();

    public void ReceiveQuestion(Guid turnId, int sequence, string text, string topic, DateTime? now = null)
    {
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index >= 0)
        {
            var existing = entries[index];
            entries[index] = existing with
            {
                Question = text,
                Topic = topic,
                Failure = null,
                FollowsPrevious = Follows(existing.Sequence, text, topic)
            };
        }
        else
        {
            entries.Add(new QuestionBurstEntry(
                turnId, sequence, text, topic, now ?? DateTime.UtcNow,
                activeAIWork.Contains(turnId) ? QuestionBurstPhase.Generating : QuestionBurstPhase.Queued,
                Answer: "", LatencyMs: null,
                FollowsPrevious: Follows(sequence, text, topic)));
        }

        // Slot 1 is always the newest detected question. A slower callback from
        // an older turn must never steal focus from a later spoken question.
        SelectedId = VisibleEntries.FirstOrDefault()?.Id;
    }

    private bool Follows(int sequence, string text, string topic)
    {
        var previous = entries.Where(entry => entry.Sequence < sequence).OrderByDescending(entry => entry.Sequence).FirstOrDefault();
        return ConversationUnderstanding.FollowsPrevious(text, topic, previous?.Topic);
    }

    /// The card is open but nothing is written on it yet, so it is still generating; the first words make it answering.
    public void StartStreaming(Guid turnId)
    {
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index < 0) return;
        UpdatePhase(entries[index].Answer.Length == 0 ? QuestionBurstPhase.Generating : QuestionBurstPhase.Answering, turnId, unlessReady: true);
    }

    public void UpdateAnswer(Guid turnId, string content)
    {
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index < 0) return;
        var entry = entries[index];
        entries[index] = entry with
        {
            Answer = content,
            Phase = entry.Phase == QuestionBurstPhase.Ready ? QuestionBurstPhase.Ready : QuestionBurstPhase.Answering
        };
    }

    public void FinishAnswer(Guid turnId, string content, int? latencyMs)
    {
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index >= 0)
        {
            entries[index] = entries[index] with { Answer = content, LatencyMs = latencyMs, Phase = QuestionBurstPhase.Ready, Failure = null };
        }
        activeAIWork.Remove(turnId);
    }

    public void Fail(Guid turnId, string? reason = null)
    {
        UpdatePhase(QuestionBurstPhase.Failed, turnId, unlessReady: true);
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index >= 0 && entries[index].Phase == QuestionBurstPhase.Failed)
        {
            entries[index] = entries[index] with { Failure = reason };
        }
        activeAIWork.Remove(turnId);
    }

    /// A failed answer is asked for again on its own card: it waits from `now` with nothing written on it.
    public void Restart(Guid turnId, DateTime? now = null)
    {
        var index = entries.FindIndex(entry => entry.Id == turnId);
        if (index < 0 || entries[index].Phase != QuestionBurstPhase.Failed) return;
        entries[index] = entries[index] with
        {
            DetectedAt = now ?? DateTime.UtcNow, Phase = QuestionBurstPhase.Generating, Answer = "", LatencyMs = null, Failure = null
        };
    }

    public void Select(Guid id)
    {
        if (entries.Any(entry => entry.Id == id)) SelectedId = id;
    }

    /// The question next to the selected one, for the keyboard. Null at either end of the interview.
    public Guid? NeighbourOfSelection(bool newer)
    {
        var newestFirst = NewestFirst;
        if (SelectedEntry?.Id is not { } current) return null;
        var index = newestFirst.FindIndex(entry => entry.Id == current);
        if (index < 0) return null;
        var target = newer ? index - 1 : index + 1;
        return target >= 0 && target < newestFirst.Count ? newestFirst[target].Id : null;
    }

    public void Clear()
    {
        entries.Clear();
        SelectedId = null;
        activeAIWork.Clear();
    }

    private void UpdatePhase(QuestionBurstPhase phase, Guid id, bool unlessReady)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        if (unlessReady && entries[index].Phase == QuestionBurstPhase.Ready) return;
        entries[index] = entries[index] with { Phase = phase };
    }
}
