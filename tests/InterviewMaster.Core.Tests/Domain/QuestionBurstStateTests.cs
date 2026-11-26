using InterviewMaster.Domain;

namespace InterviewMaster.Tests;

/// The "QuestionBurstState" section of Tests/test_processor.swift.
public sealed class QuestionBurstStateTests : SwiftStyleTests
{
    private static void Ask(QuestionBurstState burst, Guid id, int sequence, string question)
    {
        burst.BeginAIWork(id);
        burst.ReceiveQuestion(id, sequence, question, "general");
    }

    [Fact]
    public void InterleavedTurnsKeepTheirOwnAnswers()
    {
        Guid q1 = Guid.NewGuid(), q2 = Guid.NewGuid(), q3 = Guid.NewGuid(), q4 = Guid.NewGuid();
        var burst = new QuestionBurstState();
        Ask(burst, q1, 1, "Design a rate limiter");
        Ask(burst, q2, 2, "Which failures matter?");
        Ask(burst, q3, 3, "How would you load test it?");
        Check(burst.VisibleEntries.Select(entry => entry.Id).SequenceEqual(new[] { q3, q2, q1 }), "Burst presents newest question first");
        Check(burst.SelectedId == q3, "Newest question always becomes the selected answer");
        Check(burst.IsAIWorking, "Any active turn keeps the gold AI-working state active");

        burst.UpdateAnswer(q1, "A1 partial");
        burst.UpdateAnswer(q2, "A2 partial");
        burst.FinishAnswer(q1, "A1 final", null);
        Check(burst.IsAIWorking, "Finishing one interleaved turn does not clear another active turn");
        Check(burst.Entries.First(entry => entry.Id == q1).Answer == "A1 final", "Q1 finalization updates only Q1");
        Check(burst.Entries.First(entry => entry.Id == q2).Answer == "A2 partial", "Q1 finalization does not overwrite Q2");

        burst.ReceiveQuestion(q2, 2, "Which failure modes matter most?", "general");
        Check(burst.Entries.Count(entry => entry.Id == q2) == 1, "Duplicate question event is idempotent by turn ID");
        Check(burst.Entries.First(entry => entry.Id == q2).Answer == "A2 partial", "A reworded question keeps what was already answered");

        Ask(burst, q4, 4, "What would you monitor?");
        Check(burst.VisibleEntries.Select(entry => entry.Id).SequenceEqual(new[] { q4, q3, q2 }), "Visible burst is clipped to the newest three turns");
        Check(burst.SelectedId == q4, "Newest question remains selected as prior questions shift to slots 2 and 3");
        Check(burst.QuestionNumber(q1) == 1 && burst.QuestionNumber(q4) == 4, "A question keeps its number as more are asked");

        burst.FinishAnswer(q2, "A2 final", null);
        burst.FinishAnswer(q3, "A3 final", null);
        burst.FinishAnswer(q4, "A4 final", 1500);
        Check(!burst.IsAIWorking, "Gold AI-working state clears only after every active turn finishes");
    }

    [Fact]
    public void SpeechOrderDecidesTheSlots()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var burst = new QuestionBurstState();
        Ask(burst, second, 2, "Second spoken question");
        Ask(burst, first, 1, "First spoken question arrived from the model later");
        Check(burst.VisibleEntries.Select(entry => entry.Id).SequenceEqual(new[] { second, first }), "Speech sequence, not model callback completion, controls slot order");
        Check(burst.SelectedId == second, "A late callback from an older turn cannot steal newest-question focus");
    }

    [Fact]
    public void PhasesFollowTheWords()
    {
        var id = Guid.NewGuid();
        var burst = new QuestionBurstState();
        burst.ReceiveQuestion(id, 1, "What is a mutex?", "general");
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Queued, "A question nobody is working on yet is queued");
        burst.BeginAIWork(id);
        burst.StartStreaming(id);
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Generating, "A card that is open but has no words yet is still generating");
        burst.UpdateAnswer(id, "A lock");
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Answering, "The first words make it answering");
        burst.FinishAnswer(id, "A lock for one thread at a time", 900);
        burst.StartStreaming(id);
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Ready, "A finished answer is not reopened by a late streaming signal");
        Check(!QuestionBurstPhase.Ready.ShowsPill() && QuestionBurstPhase.Failed.ShowsPill(), "Only work and failures are marked");
    }

    [Fact]
    public void AFailedCardCanBeAskedAgain()
    {
        var id = Guid.NewGuid();
        var burst = new QuestionBurstState();
        Ask(burst, id, 1, "What is a mutex?");
        burst.Fail(id, "The model took too long to answer.");
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Failed && burst.Entries[0].Failure == "The model took too long to answer.", "A failed card keeps the reason");
        Check(!burst.IsAIWorking, "A failed turn is no longer the AI working");
        var second = new DateTime(2026, 1, 1, 0, 1, 40, DateTimeKind.Utc);
        burst.Restart(id, second);
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Generating && burst.Entries[0].Failure is null && burst.Entries[0].Answer.Length == 0, "Asking again clears the failure and the words");
        Check(burst.Entries[0].DetectedAt == second, "The wait counts from the second ask");
        burst.Restart(id, second.AddSeconds(100));
        Check(burst.Entries[0].DetectedAt == second, "Only a failed card can be asked again");
        burst.FinishAnswer(id, "A lock", null);
        burst.Fail(id, "late");
        Check(burst.Entries[0].Phase == QuestionBurstPhase.Ready && burst.Entries[0].Failure is null, "A late failure does not undo a finished answer");
    }

    [Fact]
    public void KeyboardWalksTheWholeInterview()
    {
        var ids = Enumerable.Range(1, 6).Select(_ => Guid.NewGuid()).ToList();
        var burst = new QuestionBurstState();
        for (var index = 0; index < ids.Count; index++) burst.ReceiveQuestion(ids[index], index + 1, $"Question {index + 1}?", "general");
        Check(burst.NeighbourOfSelection(newer: true) is null, "Nothing is newer than the newest");
        for (var step = 0; step < 4; step++) burst.Select(burst.NeighbourOfSelection(newer: false)!.Value);
        Check(burst.SelectedId == ids[1], "Four steps back from the sixth is the second");
        Check(burst.DisplayedEntries.Select(entry => entry.Id).SequenceEqual(new[] { ids[2], ids[1], ids[0] }), "The rows follow an older selection, clamped to the first question");
        burst.Select(ids[2]);
        Check(burst.DisplayedEntries.Select(entry => entry.Id).SequenceEqual(new[] { ids[3], ids[2], ids[1] }), "An older selection sits in the middle row");
        burst.Select(Guid.NewGuid());
        Check(burst.SelectedId == ids[2], "An unknown question cannot be selected");
        burst.Clear();
        Check(burst.Entries.Count == 0 && burst.SelectedEntry is null && !burst.IsAIWorking, "Clearing forgets everything");
    }
}
