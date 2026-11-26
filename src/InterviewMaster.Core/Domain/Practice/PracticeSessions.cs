using System.Text.Json;
using System.Text.Json.Serialization;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

// The second half of `Domain/Practice/PracticeLogic.swift`: scored answers, runs, resumable sessions,
// mastery, today's plan and contrast pairs.

public sealed record PracticeScoredAnswer
{
    public required string QuestionId { get; init; }
    public required string PackId { get; init; }
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public required bool UsedHelp { get; init; }
    public required PracticeHelpMark Mark { get; init; }
    public required double RawScore { get; init; }
    public required double FinalScore { get; init; }
    public required string Feedback { get; init; }
    public required ValueList<string> Strengths { get; init; }
    public required ValueList<string> Gaps { get; init; }
    public PracticeRecallRating? RecallRating { get; init; }
    public DateTime? NextReviewAt { get; init; }

    /// Rich recall fields are optional so all pre-Active-Recall runs still decode.
    public PracticeRecallResponse? RecallResponse { get; init; }
    public PracticeRecallConfidence? RecallConfidence { get; init; }
    public ValueList<PracticeCoverageOverride>? CoverageOverrides { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public PracticeCardResult? CardResult { get; init; }

    [JsonIgnore]
    public string InitialRecallAnswer => RecallResponse?.Initial ?? Answer;

    [JsonIgnore]
    public string? RevisedRecallAnswer => RecallResponse?.Revised;

    [JsonIgnore]
    public string EffectiveRecallAnswer => RecallResponse?.Current ?? Answer;
}

public sealed record PracticeRunRecord
{
    public required string Id { get; init; }
    public required string PackId { get; init; }
    public required string PackTitle { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime FinishedAt { get; init; }
    public required ValueList<PracticeScoredAnswer> Answers { get; init; }
    public required double OverallScore { get; init; }
    public required int HelpedCount { get; init; }
    public PracticeRunMode? Mode { get; init; }
    public DateTime? TargetDate { get; init; }
}

public sealed record PracticeQuestionReference
{
    public required string PackId { get; init; }
    public required string QuestionId { get; init; }

    public PracticeQuestionReference() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PracticeQuestionReference(string packId, string questionId)
    {
        PackId = packId;
        QuestionId = questionId;
    }
}

/// Everything needed to resume a particular prompt without losing learner work.
public sealed record PracticeSessionQuestionState
{
    public required PracticeQuestionReference Reference { get; init; }
    public string DraftResponse { get; init; } = "";
    public PracticeRecallResponse? RecallResponse { get; init; }
    public PracticeRecallConfidence? Confidence { get; init; }
    public bool HasRevealedKeyIdeas { get; init; }
    public ValueList<PracticeCoverageOverride> CoverageOverrides { get; init; } = ValueList<PracticeCoverageOverride>.Empty;
    public bool UsedHelp { get; init; }
    public string RepairDraft { get; init; } = "";
    public bool IsRepairingGap { get; init; }
    public int? RepairIdeaIndex { get; init; }

    /// Freezes wording, answer key and option order for an in-progress card run.
    public PracticeQuestion? CardQuestion { get; init; }

    public PracticeSessionQuestionState() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PracticeSessionQuestionState(PracticeQuestionReference reference)
    {
        Reference = reference;
    }
}

/// Enough pre-rating state to reverse the most recent rating and restore the review card.
public sealed record PracticeRatingUndoMetadata
{
    public required PracticeQuestionReference Question { get; init; }
    public required int QuestionIndex { get; init; }
    public required int AnswerCountBeforeRating { get; init; }
    public required int QuestionCountBeforeRating { get; init; }
    public required PracticeSessionQuestionState PreviousQuestionState { get; init; }
    public required DateTime RatedAt { get; init; }
    public ValueList<string>? RequeuedQuestionKeysBeforeRating { get; init; }
}

/// Autosaved, in-progress work. Versioned and default-decoded so early snapshots can
/// remain resumable as fields are added.
[JsonConverter(typeof(PracticeSessionSnapshotJsonConverter))]
public sealed record PracticeSessionSnapshot
{
    public const int CurrentVersion = 2;

    public int Version { get; init; }
    public string Id { get; init; }
    public string PackId { get; init; }
    public string PackTitle { get; init; }
    public string? RoleId { get; init; }
    public ValueList<string> GroupIds { get; init; }
    public PracticeRunMode Mode { get; init; }
    public ValueList<PracticeSessionQuestionState> QuestionStates { get; init; }
    public int BaseQuestionCount { get; init; }
    public int CurrentQuestionIndex { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public ValueList<PracticeScoredAnswer> Answers { get; init; }
    public ValueList<string> RequeuedQuestionKeys { get; init; }
    public PracticeRatingUndoMetadata? LastRatingUndo { get; init; }
    public DateTime? TargetDate { get; init; }

    public PracticeSessionSnapshot(
        string packId,
        string packTitle,
        PracticeRunMode mode,
        ValueList<PracticeSessionQuestionState> questionStates,
        DateTime startedAt,
        DateTime updatedAt,
        int version = CurrentVersion,
        string? id = null,
        string? roleId = null,
        ValueList<string>? groupIds = null,
        int? baseQuestionCount = null,
        int currentQuestionIndex = 0,
        ValueList<PracticeScoredAnswer>? answers = null,
        ValueList<string>? requeuedQuestionKeys = null,
        PracticeRatingUndoMetadata? lastRatingUndo = null,
        DateTime? targetDate = null)
    {
        Version = version;
        Id = id ?? Guid.NewGuid().ToString().ToUpperInvariant();
        PackId = packId;
        PackTitle = packTitle;
        RoleId = roleId;
        GroupIds = groupIds ?? ValueList<string>.Empty;
        Mode = mode;
        QuestionStates = questionStates;
        BaseQuestionCount = baseQuestionCount ?? questionStates.Count;
        CurrentQuestionIndex = currentQuestionIndex;
        StartedAt = startedAt;
        UpdatedAt = updatedAt;
        Answers = answers ?? ValueList<PracticeScoredAnswer>.Empty;
        RequeuedQuestionKeys = requeuedQuestionKeys ?? ValueList<string>.Empty;
        LastRatingUndo = lastRatingUndo;
        TargetDate = targetDate;
    }

    public bool CanResume => CurrentQuestionIndex >= 0 && CurrentQuestionIndex < QuestionStates.Count;

    public List<PracticeQuestionReference> QuestionReferences => QuestionStates.Select(state => state.Reference).ToList();
}

/// Reads snapshots from every app version: absent fields take their defaults, and a version-1 list of
/// `questionIds` becomes question states. Writes the current shape only.
public sealed class PracticeSessionSnapshotJsonConverter : JsonConverter<PracticeSessionSnapshot>
{
    private sealed class Stored
    {
        public int? Version { get; set; }
        public string? Id { get; set; }
        public string? PackId { get; set; }
        public string? PackTitle { get; set; }
        public string? RoleId { get; set; }
        public ValueList<string>? GroupIds { get; set; }
        public PracticeRunMode? Mode { get; set; }
        public ValueList<PracticeSessionQuestionState>? QuestionStates { get; set; }
        public ValueList<string>? QuestionIds { get; set; }
        public int? BaseQuestionCount { get; set; }
        public int? CurrentQuestionIndex { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public ValueList<PracticeScoredAnswer>? Answers { get; set; }
        public ValueList<string>? RequeuedQuestionKeys { get; set; }
        public PracticeRatingUndoMetadata? LastRatingUndo { get; set; }
        public DateTime? TargetDate { get; set; }
    }

    public override PracticeSessionSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stored = JsonSerializer.Deserialize<Stored>(ref reader, options) ?? throw new JsonException("Expected a practice session.");
        var startedAt = stored.StartedAt ?? throw new JsonException("A practice session needs startedAt.");
        var packId = stored.PackId ?? "";
        var questionStates = stored.QuestionStates
            ?? new ValueList<PracticeSessionQuestionState>((stored.QuestionIds ?? ValueList<string>.Empty)
                .Select(questionId => new PracticeSessionQuestionState(new PracticeQuestionReference(packId, questionId))));
        return new PracticeSessionSnapshot(
            packId: packId,
            packTitle: stored.PackTitle ?? "Practice",
            mode: stored.Mode ?? PracticeRunMode.Learn,
            questionStates: questionStates,
            startedAt: startedAt,
            updatedAt: stored.UpdatedAt ?? startedAt,
            version: stored.Version ?? 1,
            id: stored.Id ?? "legacy-session",
            roleId: stored.RoleId,
            groupIds: stored.GroupIds,
            baseQuestionCount: stored.BaseQuestionCount,
            currentQuestionIndex: stored.CurrentQuestionIndex ?? 0,
            answers: stored.Answers,
            requeuedQuestionKeys: stored.RequeuedQuestionKeys,
            lastRatingUndo: stored.LastRatingUndo,
            targetDate: stored.TargetDate);
    }

    public override void Write(Utf8JsonWriter writer, PracticeSessionSnapshot value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new Stored
        {
            Version = value.Version,
            Id = value.Id,
            PackId = value.PackId,
            PackTitle = value.PackTitle,
            RoleId = value.RoleId,
            GroupIds = value.GroupIds,
            Mode = value.Mode,
            QuestionStates = value.QuestionStates,
            BaseQuestionCount = value.BaseQuestionCount,
            CurrentQuestionIndex = value.CurrentQuestionIndex,
            StartedAt = value.StartedAt,
            UpdatedAt = value.UpdatedAt,
            Answers = value.Answers,
            RequeuedQuestionKeys = value.RequeuedQuestionKeys,
            LastRatingUndo = value.LastRatingUndo,
            TargetDate = value.TargetDate
        }, options);
    }
}

public enum PracticeMasteryState
{
    New,
    Learning,
    Solid,
    Due
}

public static class PracticeMasteryStates
{
    public static readonly IReadOnlyList<PracticeMasteryState> AllCases = new[]
    {
        PracticeMasteryState.New, PracticeMasteryState.Learning, PracticeMasteryState.Solid, PracticeMasteryState.Due
    };

    public static string RawValue(this PracticeMasteryState state) => state switch
    {
        PracticeMasteryState.New => "new",
        PracticeMasteryState.Learning => "learning",
        PracticeMasteryState.Solid => "solid",
        _ => "due"
    };

    public static string Title(this PracticeMasteryState state) => state switch
    {
        PracticeMasteryState.New => "New",
        PracticeMasteryState.Learning => "Learning",
        PracticeMasteryState.Solid => "Solid",
        _ => "Due"
    };
}

public sealed record PracticeTopicIdentity
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    public PracticeTopicIdentity() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PracticeTopicIdentity(string id, string title)
    {
        Id = id;
        Title = title;
    }
}

public sealed record PracticeTopicMasterySummary
{
    public required PracticeTopicIdentity Topic { get; init; }
    public required int NewCount { get; init; }
    public required int LearningCount { get; init; }
    public required int SolidCount { get; init; }
    public required int DueCount { get; init; }

    [JsonIgnore]
    public int TotalCount => NewCount + LearningCount + SolidCount + DueCount;

    [JsonIgnore]
    public double MasteredFraction => TotalCount > 0 ? (double)SolidCount / TotalCount : 0;
}

public sealed record PracticeTodayPlan
{
    public required int DueCount { get; init; }
    public required int LearningCount { get; init; }
    public required int NewCount { get; init; }
    public required int SolidCount { get; init; }
    public required int RecommendedCount { get; init; }
    public required int EstimatedMinutes { get; init; }

    [JsonIgnore]
    public int WeakCount => LearningCount;

    [JsonIgnore]
    public int TotalQuestionCount => DueCount + LearningCount + NewCount + SolidCount;
}

public sealed record PracticeContrastPair
{
    public required PracticeTopicIdentity Topic { get; init; }
    public required PracticeQuestion First { get; init; }
    public required PracticeQuestion Second { get; init; }
}

public static partial class Practice
{
    private static readonly IReadOnlyDictionary<string, List<PracticeScoredAnswer>> NoAttempts = new Dictionary<string, List<PracticeScoredAnswer>>();
    private static readonly IReadOnlyDictionary<string, PracticeScoredAnswer> NoAnswers = new Dictionary<string, PracticeScoredAnswer>();

    public static List<PracticeScoredAnswer> LatestScoredAnswers(IEnumerable<PracticeScoredAnswer> answers)
    {
        var order = new List<string>();
        var latest = new Dictionary<string, PracticeScoredAnswer>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            var key = RecallReviewKey(answer.PackId, answer.QuestionId);
            if (!latest.ContainsKey(key)) order.Add(key);
            latest[key] = answer;
        }
        return order.Select(key => latest[key]).ToList();
    }

    public static double RunOverallScore(IEnumerable<PracticeScoredAnswer> answers)
    {
        var latest = LatestScoredAnswers(answers);
        if (latest.Count == 0) return 0;
        double total = 0;
        foreach (var answer in latest) total += answer.FinalScore;
        return total / latest.Count;
    }

    public static PracticeScoredAnswer? ScoreCard(PracticeQuestion question, string selectedOptionID)
    {
        if (question.Card is not { IsValid: true } card) return null;
        var selected = card.Options.FirstOrDefault(option => option.Id == selectedOptionID);
        var correct = card.Options.FirstOrDefault(option => option.Id == card.CorrectOptionID);
        if (selected is null || correct is null) return null;
        var score = selected.Id == correct.Id ? 1.0 : 0.0;
        var explanation = card.Explanation + (score == 0 ? $"\nYour choice: {selected.Rationale}" : "");
        return new PracticeScoredAnswer
        {
            QuestionId = question.Id,
            PackId = question.PackId,
            Question = question.Text,
            Answer = selected.Text,
            UsedHelp = false,
            Mark = PracticeHelpMark.None,
            RawScore = score,
            FinalScore = score,
            Feedback = explanation,
            Strengths = ValueList<string>.Empty,
            Gaps = ValueList<string>.Empty,
            CardResult = new PracticeCardResult
            {
                SelectedOptionID = selected.Id,
                CorrectOptionID = correct.Id,
                CorrectAnswer = correct.Text,
                Explanation = explanation,
                Topic = question.TopicTitle,
                ContentVersion = card.ContentVersion
            }
        };
    }

    /// Interview rendering receives no correctness or explanation before submission.
    public static PracticeCardResult? CardFeedback(PracticeQuestion question, string? selectedOptionID, PracticeRunMode mode) =>
        mode == PracticeRunMode.Learn && selectedOptionID is not null ? ScoreCard(question, selectedOptionID)?.CardResult : null;

    public static PracticeQuestion? RestoreCardQuestion(PracticeSessionQuestionState state)
    {
        if (state.CardQuestion is not { Card: { IsValid: true } card } question) return null;
        if (question.Id != state.Reference.QuestionId || question.PackId != state.Reference.PackId) return null;
        if (state.DraftResponse.Length > 0 && !card.Options.Any(option => option.Id == state.DraftResponse)) return null;
        return question;
    }

    /// Returns a restored value instead of mutating persistence, keeping Undo easy to test.
    public static PracticeSessionSnapshot? SnapshotAfterUndoingLastRating(PracticeSessionSnapshot snapshot, DateTime? updatedAt = null)
    {
        if (snapshot.LastRatingUndo is not { } undo
            || undo.AnswerCountBeforeRating < 0
            || undo.AnswerCountBeforeRating >= snapshot.Answers.Count
            || undo.QuestionCountBeforeRating <= 0
            || undo.QuestionCountBeforeRating > snapshot.QuestionStates.Count
            || undo.QuestionIndex < 0
            || undo.QuestionIndex >= undo.QuestionCountBeforeRating)
        {
            return null;
        }

        var questionStates = snapshot.QuestionStates.Take(undo.QuestionCountBeforeRating).ToArray();
        questionStates[undo.QuestionIndex] = undo.PreviousQuestionState;
        return snapshot with
        {
            Answers = snapshot.Answers.Take(undo.AnswerCountBeforeRating).ToArray(),
            QuestionStates = questionStates,
            CurrentQuestionIndex = undo.QuestionIndex,
            RequeuedQuestionKeys = undo.RequeuedQuestionKeysBeforeRating ?? snapshot.RequeuedQuestionKeys,
            LastRatingUndo = null,
            UpdatedAt = updatedAt ?? snapshot.UpdatedAt
        };
    }

    public static string RecallReviewKey(string packId, string questionId) => $"{packId}\u001f{questionId}";

    public static Dictionary<string, PracticeScoredAnswer> LatestRecallAnswers(IEnumerable<PracticeRunRecord> runs)
    {
        var latest = new Dictionary<string, PracticeScoredAnswer>(StringComparer.Ordinal);
        foreach (var run in runs.OrderBy(run => run.FinishedAt))
        {
            foreach (var answer in run.Answers)
            {
                if (answer.RecallRating is null) continue;
                latest[RecallReviewKey(answer.PackId, answer.QuestionId)] = answer;
            }
        }
        return latest;
    }

    public static Dictionary<string, List<PracticeScoredAnswer>> RecallAttempts(IEnumerable<PracticeRunRecord> runs)
    {
        var attempts = new Dictionary<string, List<PracticeScoredAnswer>>(StringComparer.Ordinal);
        foreach (var run in runs.OrderBy(run => run.FinishedAt))
        {
            foreach (var answer in run.Answers)
            {
                if (answer.RecallRating is null) continue;
                var key = RecallReviewKey(answer.PackId, answer.QuestionId);
                if (!attempts.TryGetValue(key, out var list))
                {
                    list = new List<PracticeScoredAnswer>();
                    attempts[key] = list;
                }
                list.Add(answer);
            }
        }
        return attempts;
    }

    public static PracticeMasteryState MasteryState(
        PracticeScoredAnswer? latestAnswer,
        IReadOnlyList<PracticeScoredAnswer>? attempts = null,
        DateTime? now = null)
    {
        if (latestAnswer is null) return PracticeMasteryState.New;
        if (latestAnswer.NextReviewAt is { } due && due <= (now ?? DateTime.UtcNow)) return PracticeMasteryState.Due;
        if (latestAnswer.RecallRating != PracticeRecallRating.GotIt || latestAnswer.FinalScore < 0.8 || latestAnswer.UsedHelp)
        {
            return PracticeMasteryState.Learning;
        }
        var evidence = attempts is null || attempts.Count == 0 ? new[] { latestAnswer } : attempts;
        var successfulUnassisted = evidence.Count(attempt =>
            attempt.RecallRating == PracticeRecallRating.GotIt && attempt.FinalScore >= 0.8 && !attempt.UsedHelp);
        return successfulUnassisted >= 2 ? PracticeMasteryState.Solid : PracticeMasteryState.Learning;
    }

    private static PracticeTopicIdentity DefaultTopicIdentity(PracticeQuestion question)
    {
        var id = question.GroupId.Length == 0 ? question.PackId : question.GroupId;
        var fallback = question.TopicTitle.Length == 0 ? id : question.TopicTitle;
        return new PracticeTopicIdentity(id, GroupDisplayTitle(id, fallback));
    }

    private static PracticeMasteryState MasteryState(
        PracticeQuestion question,
        IReadOnlyDictionary<string, PracticeScoredAnswer> latestAnswers,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>> attemptsByQuestion,
        DateTime now)
    {
        var key = RecallReviewKey(question.PackId, question.Id);
        return MasteryState(
            latestAnswers.TryGetValue(key, out var latest) ? latest : null,
            attemptsByQuestion.TryGetValue(key, out var attempts) ? attempts : null,
            now);
    }

    public static List<PracticeTopicMasterySummary> TopicMasterySummaries(
        IEnumerable<PracticeQuestion> questions,
        IReadOnlyDictionary<string, PracticeScoredAnswer> latestAnswers,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>>? attemptsByQuestion = null,
        DateTime? now = null,
        Func<PracticeQuestion, PracticeTopicIdentity>? topicOf = null)
    {
        var moment = now ?? DateTime.UtcNow;
        var order = new List<PracticeTopicIdentity>();
        var counts = new Dictionary<PracticeTopicIdentity, Dictionary<PracticeMasteryState, int>>();
        foreach (var question in questions)
        {
            var topic = topicOf?.Invoke(question) ?? DefaultTopicIdentity(question);
            if (!counts.TryGetValue(topic, out var topicCounts))
            {
                order.Add(topic);
                topicCounts = new Dictionary<PracticeMasteryState, int>();
                counts[topic] = topicCounts;
            }
            var state = MasteryState(question, latestAnswers, attemptsByQuestion ?? NoAttempts, moment);
            topicCounts[state] = topicCounts.GetValueOrDefault(state) + 1;
        }
        return order.Select(topic =>
        {
            var topicCounts = counts[topic];
            return new PracticeTopicMasterySummary
            {
                Topic = topic,
                NewCount = topicCounts.GetValueOrDefault(PracticeMasteryState.New),
                LearningCount = topicCounts.GetValueOrDefault(PracticeMasteryState.Learning),
                SolidCount = topicCounts.GetValueOrDefault(PracticeMasteryState.Solid),
                DueCount = topicCounts.GetValueOrDefault(PracticeMasteryState.Due)
            };
        }).ToList();
    }

    public static PracticeTodayPlan TodaysPlan(
        IEnumerable<PracticeQuestion> questions,
        IReadOnlyDictionary<string, PracticeScoredAnswer> latestAnswers,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>>? attemptsByQuestion = null,
        DateTime? now = null,
        int maximumQuestions = 10,
        int estimatedSecondsPerQuestion = 48)
    {
        var moment = now ?? DateTime.UtcNow;
        var counts = new Dictionary<PracticeMasteryState, int>();
        foreach (var question in questions)
        {
            var state = MasteryState(question, latestAnswers, attemptsByQuestion ?? NoAttempts, moment);
            counts[state] = counts.GetValueOrDefault(state) + 1;
        }
        var actionable = counts.GetValueOrDefault(PracticeMasteryState.Due)
            + counts.GetValueOrDefault(PracticeMasteryState.Learning)
            + counts.GetValueOrDefault(PracticeMasteryState.New);
        var recommended = Math.Min(Math.Max(0, maximumQuestions), actionable);
        var seconds = recommended * Math.Max(0, estimatedSecondsPerQuestion);
        var minutes = seconds == 0 ? 0 : (int)Math.Ceiling(seconds / 60.0);
        return new PracticeTodayPlan
        {
            DueCount = counts.GetValueOrDefault(PracticeMasteryState.Due),
            LearningCount = counts.GetValueOrDefault(PracticeMasteryState.Learning),
            NewCount = counts.GetValueOrDefault(PracticeMasteryState.New),
            SolidCount = counts.GetValueOrDefault(PracticeMasteryState.Solid),
            RecommendedCount = recommended,
            EstimatedMinutes = minutes
        };
    }

    public static List<PracticeQuestion> PrioritizedRecallQuestions(
        IEnumerable<PracticeQuestion> questions,
        IReadOnlyDictionary<string, PracticeScoredAnswer> latestAnswers,
        DateTime? now = null)
    {
        var moment = now ?? DateTime.UtcNow;

        (int Rank, DateTime Due, int Index) Priority(PracticeQuestion question, int index)
        {
            var key = RecallReviewKey(question.PackId, question.Id);
            if (!latestAnswers.TryGetValue(key, out var answer) || answer.NextReviewAt is not { } due)
            {
                return (1, DateTime.MaxValue, index);
            }
            return due <= moment ? (0, due, index) : (2, due, index);
        }

        return questions
            .Select((question, index) => (Question: question, Priority: Priority(question, index)))
            .OrderBy(item => item.Priority.Rank)
            .ThenBy(item => item.Priority.Due)
            .ThenBy(item => item.Priority.Index)
            .Select(item => item.Question)
            .ToList();
    }

    public static HashSet<string> Tokenize(string text) =>
        text.Lowercased()
            .SplitWhere(character => !char.IsLetter(character))
            .Where(word => word.Length > 2)
            .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> GenericContrastWords = new(StringComparer.Ordinal)
    {
        "compare", "describe", "difference", "does", "explain", "how", "versus",
        "what", "when", "which", "why", "would"
    };

    private static HashSet<string> ContrastTerms(string text) =>
        Tokenize(text).Where(word => !RecallStopWords.Contains(word) && !GenericContrastWords.Contains(word)).ToHashSet(StringComparer.Ordinal);

    private static int ContrastWeakness(
        PracticeQuestion question,
        IReadOnlyDictionary<string, PracticeScoredAnswer> latestAnswers,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>> attemptsByQuestion,
        DateTime now) => MasteryState(question, latestAnswers, attemptsByQuestion, now) switch
    {
        PracticeMasteryState.Due => 3,
        PracticeMasteryState.Learning => 2,
        PracticeMasteryState.New => 1,
        _ => 0
    };

    /// Selects deterministic, non-overlapping pairs of related prompts. Pairs must share
    /// a broad topic and either a more specific topic id or meaningful vocabulary; this
    /// avoids turning ordinary random interleaving into a purported contrast exercise.
    public static List<PracticeContrastPair> ContrastPairs(
        IReadOnlyList<PracticeQuestion> questions,
        IReadOnlyDictionary<string, PracticeScoredAnswer>? latestAnswers = null,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>>? attemptsByQuestion = null,
        DateTime? now = null,
        int limit = 3,
        Func<PracticeQuestion, PracticeTopicIdentity>? topicOf = null)
    {
        var result = new List<PracticeContrastPair>();
        if (limit <= 0 || questions.Count < 2) return result;
        var moment = now ?? DateTime.UtcNow;
        var answers = latestAnswers ?? NoAnswers;
        var attempts = attemptsByQuestion ?? NoAttempts;

        var candidates = new List<(int FirstIndex, int SecondIndex, PracticeTopicIdentity Topic, int Score)>();
        for (var firstIndex = 0; firstIndex < questions.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < questions.Count; secondIndex++)
            {
                var first = questions[firstIndex];
                var second = questions[secondIndex];
                var firstTopic = topicOf?.Invoke(first) ?? DefaultTopicIdentity(first);
                var secondTopic = topicOf?.Invoke(second) ?? DefaultTopicIdentity(second);
                if (firstTopic.Id != secondTopic.Id) continue;

                var secondTerms = ContrastTerms(second.Text);
                var overlap = ContrastTerms(first.Text).Count(secondTerms.Contains);
                var sharesSpecificTopic = first.TopicId.Length > 0 && first.TopicId == second.TopicId;
                var contrastLanguage = new[] { first.Text, second.Text }.Any(text =>
                    text.Lowercased().ContainsAny(new[] { "difference", "compare", " versus ", " vs ", "trade-off" }));
                // A broad topic id is not evidence that two prompts are confusable.
                // Require either meaningful shared vocabulary or explicit comparison
                // language backed by at least one shared concept term.
                if (!(overlap >= 2 || (contrastLanguage && overlap >= 1))) continue;
                var score = overlap * 3
                    + (sharesSpecificTopic ? 4 : 0)
                    + (contrastLanguage ? 2 : 0)
                    + ContrastWeakness(first, answers, attempts, moment)
                    + ContrastWeakness(second, answers, attempts, moment);
                candidates.Add((firstIndex, secondIndex, firstTopic, score));
            }
        }

        candidates.Sort((lhs, rhs) =>
        {
            if (lhs.Score != rhs.Score) return rhs.Score.CompareTo(lhs.Score);
            if (lhs.FirstIndex != rhs.FirstIndex) return lhs.FirstIndex.CompareTo(rhs.FirstIndex);
            return lhs.SecondIndex.CompareTo(rhs.SecondIndex);
        });
        var used = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            if (used.Contains(candidate.FirstIndex) || used.Contains(candidate.SecondIndex)) continue;
            used.Add(candidate.FirstIndex);
            used.Add(candidate.SecondIndex);
            result.Add(new PracticeContrastPair
            {
                Topic = candidate.Topic,
                First = questions[candidate.FirstIndex],
                Second = questions[candidate.SecondIndex]
            });
            if (result.Count == limit) break;
        }
        return result;
    }

    public static List<PracticeQuestion> ContrastQuestionSelection(
        IReadOnlyList<PracticeQuestion> questions,
        IReadOnlyDictionary<string, PracticeScoredAnswer>? latestAnswers = null,
        IReadOnlyDictionary<string, List<PracticeScoredAnswer>>? attemptsByQuestion = null,
        DateTime? now = null,
        int pairLimit = 3,
        Func<PracticeQuestion, PracticeTopicIdentity>? topicOf = null) =>
        ContrastPairs(questions, latestAnswers, attemptsByQuestion, now, pairLimit, topicOf)
            .SelectMany(pair => new[] { pair.First, pair.Second })
            .ToList();

    public static double HeuristicRawScore(string question, string answer)
    {
        var answerWords = Tokenize(answer);
        var questionWords = Tokenize(question);
        if (answerWords.Count == 0) return 0;
        if (answerWords.Count < 8) return 0.25;
        double overlap = answerWords.Count(questionWords.Contains);
        var topical = questionWords.Count == 0 ? 0.5 : Math.Min(1.0, overlap / Math.Max(2, questionWords.Count / 3));
        var lengthScore = Math.Min(1.0, answerWords.Count / 50.0);
        return Math.Min(1.0, 0.3 + 0.45 * lengthScore + 0.25 * topical);
    }

    public static PracticeRunRecord MakeRunRecord(
        PracticeTopicPack pack,
        DateTime startedAt,
        DateTime finishedAt,
        IReadOnlyList<PracticeScoredAnswer> answers,
        PracticeRunMode? mode = null,
        DateTime? targetDate = null,
        string? id = null)
    {
        var latest = LatestScoredAnswers(answers);
        return new PracticeRunRecord
        {
            Id = id ?? Guid.NewGuid().ToString().ToUpperInvariant(),
            PackId = pack.Id,
            PackTitle = pack.Title,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Answers = new ValueList<PracticeScoredAnswer>(answers),
            OverallScore = RunOverallScore(answers),
            HelpedCount = latest.Count(answer => answer.UsedHelp),
            Mode = mode,
            TargetDate = targetDate
        };
    }
}
