using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Tests;

internal static class RepoPaths
{
    public static readonly string Root = FindRoot();

    public static string PracticeDirectory => Path.Combine(Root, "Resources", "practice");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InterviewMaster.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("InterviewMaster.sln was not found above the test output.");
    }
}

internal static class PracticeFixtures
{
    public static readonly Lazy<PracticeBankFile> Bank = new(PracticeBankLoader.Load);

    public static readonly DateTime RecallNow = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime;

    public static bool ContainsIgnoringCase(this string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    public static PracticeQuestion Question(
        string id,
        string packId,
        string text,
        string groupId = "",
        string topicId = "",
        string topicTitle = "",
        string[]? hints = null,
        string rubric = "",
        string answer = "") => new()
    {
        Id = id,
        PackId = packId,
        Text = text,
        GroupId = groupId,
        TopicId = topicId,
        TopicTitle = topicTitle,
        Hints = hints ?? Array.Empty<string>(),
        Rubric = rubric,
        Answer = answer
    };

    public static PracticeScoredAnswer RecallAnswerRecord(string id, double score, PracticeRecallRating rating, DateTime due) => new()
    {
        QuestionId = id,
        PackId = "study-book",
        Question = "Q",
        Answer = "A",
        UsedHelp = false,
        Mark = PracticeHelpMark.None,
        RawScore = score,
        FinalScore = score,
        Feedback = rating.Title(),
        Strengths = Array.Empty<string>(),
        Gaps = Array.Empty<string>(),
        RecallRating = rating,
        NextReviewAt = due
    };

    public static readonly PracticeScoredAnswer RepeatedLow = RecallAnswerRecord("repeat", 0.0, PracticeRecallRating.Again, RecallNow);

    public static readonly PracticeScoredAnswer RepeatedHigh = RecallAnswerRecord("repeat", 1.0, PracticeRecallRating.GotIt, RecallNow.AddSeconds(259_200));

    public static readonly PracticeRecallResponse RevisedResponse = new("RAG retrieves context.", "RAG retrieves relevant context before generation.");

    public static readonly PracticeSessionQuestionState FirstState = new(new PracticeQuestionReference("study-book", "due"))
    {
        DraftResponse = RevisedResponse.Current,
        RecallResponse = RevisedResponse,
        Confidence = PracticeRecallConfidence.Mostly,
        HasRevealedKeyIdeas = true,
        CoverageOverrides = new[] { new PracticeCoverageOverride(2, true) },
        UsedHelp = true,
        RepairDraft = "Add metadata filtering.",
        IsRepairingGap = true,
        RepairIdeaIndex = 2
    };

    public static readonly PracticeSessionQuestionState SecondState = new(new PracticeQuestionReference("study-book", "new"))
    {
        DraftResponse = "Draft in progress"
    };

    public static readonly PracticeSessionSnapshot Snapshot = new(
        packId: "study-book",
        packTitle: "Study book",
        mode: PracticeRunMode.Learn,
        questionStates: new[] { FirstState, SecondState },
        startedAt: RecallNow,
        updatedAt: RecallNow.AddSeconds(30),
        id: "resume-me",
        roleId: "ai-engineer",
        groupIds: new[] { "genai-and-llm" },
        currentQuestionIndex: 1,
        answers: new[] { RepeatedLow },
        requeuedQuestionKeys: new[] { Practice.RecallReviewKey("study-book", "due") },
        lastRatingUndo: new PracticeRatingUndoMetadata
        {
            Question = FirstState.Reference,
            QuestionIndex = 0,
            AnswerCountBeforeRating = 0,
            QuestionCountBeforeRating = 2,
            PreviousQuestionState = FirstState,
            RatedAt = RecallNow,
            RequeuedQuestionKeysBeforeRating = Array.Empty<string>()
        },
        targetDate: RecallNow.AddSeconds(14 * 86_400));
}
