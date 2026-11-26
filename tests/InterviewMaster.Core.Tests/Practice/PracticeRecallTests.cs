using System.Text;
using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using static InterviewMaster.Tests.PracticeFixtures;

namespace InterviewMaster.Tests;

// Ports the logic half of `Tests/test_practice_bank.swift` (recall review, sessions, mastery, contrast,
// the store) and the practice sections of `Tests/test_processor.swift`.
public sealed class PracticeRecallTests : SwiftStyleTests
{
    private static readonly PracticeQuestion RecallQuestion = Question(
        "rag-recall", "study-book", "What problem does a vector database solve in RAG?", groupId: "genai-and-llm",
        answer: "Key ideas:\n- Semantic similarity search\n- Fast retrieval over large collections\n- Metadata filtering for relevant context");

    private const string RecallAnswer = "It retrieves semantically similar documents fast at scale.";

    private static string Key(string questionId) => Practice.RecallReviewKey("study-book", questionId);

    private static bool Same<T>(IEnumerable<T> actual, params T[] expected) => actual.SequenceEqual(expected);

    [Fact]
    public void TopicPacksAndHelpScoring()
    {
        Check(Practice.PackIDs().Contains("aws"), "AWS pack is available");
        Check(Practice.PackIDs().Contains("models"), "Models pack is available");
        Check(Practice.PackIDs().Contains("angular"), "Angular pack is available");
        Check(Practice.QuestionsForPack("aws").Count > 0, "AWS pack has questions");
        Check(Practice.QuestionsForPack("models").Count > 0, "Models pack has questions");
        Check(Practice.QuestionsForPack("angular").Count > 0, "Angular pack has questions");
        Check(Practice.QuestionsForPack("aws").All(question => question.PackId == "aws"), "AWS questions belong to the AWS pack");
        Check(Practice.QuestionsForPack("unknown").Count == 0, "Unknown pack has no questions");

        Check(PracticeScoring.HelpMultiplier <= 0.5, "Help multiplier is at most half");
        var helpedPerfect = Practice.ApplyHelpPenalty(1.0, true);
        Check(helpedPerfect.FinalScore == 0.4, "Help applies a 0.4× multiplier");
        Check(helpedPerfect.Mark == PracticeHelpMark.Yellow, "Helped answers are marked yellow");
        Check(helpedPerfect.UsedHelp, "Help flag is preserved");
        var unaided = Practice.ApplyHelpPenalty(0.9, false);
        Check(unaided.FinalScore == 0.9, "Unaided answers keep full points");
        Check(unaided.Mark == PracticeHelpMark.None, "Unaided answers are not marked yellow");
        Check(Practice.ApplyHelpPenalty(0.5, true).FinalScore == 0.2, "Help penalty scales the raw score, not a flat 0.4");
        Check(Practice.ApplyHelpPenalty(2.0, true).FinalScore == 0.4, "Raw scores clamp to 1 before the help penalty");

        var day1 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime;
        var day2 = DateTimeOffset.FromUnixTimeSeconds(1_700_086_400).UtcDateTime;
        var day3 = DateTimeOffset.FromUnixTimeSeconds(1_699_913_600).UtcDateTime;
        var storedRuns = new[]
        {
            new PracticeProgressInput(day2, 0.7, "aws"),
            new PracticeProgressInput(day1, 0.4, "angular"),
            new PracticeProgressInput(day3, 0.55, "models")
        };
        var overallSeries = Practice.ProgressSeries(storedRuns);
        Check(Same(overallSeries.Select(point => point.Score), 0.55, 0.4, 0.7), "Progress points are ordered by run date");
        Check(Same(overallSeries.Select(point => point.PackId), "models", "angular", "aws"), "Progress keeps pack identity");
        Check(Same(Practice.ProgressSeries(storedRuns, "aws").Select(point => point.Score), 0.7), "Pack filter keeps only that pack's runs");
    }

    [Fact]
    public void BalancedSelectionRolesAndInterviewOrder()
    {
        var bank = Enumerable.Range(0, 3)
            .SelectMany(topic => Enumerable.Range(0, 4).Select(index => Question($"t{topic}-{index}", "study-book", $"Q{topic}.{index}", groupId: $"t{topic}")))
            .ToList();
        var six = Practice.BalancedSelection(bank, 6, question => question.GroupId, list => list);
        Check(six.Count == 6, "Requested count is honored when the bank is large enough");
        Check(six.GroupBy(question => question.GroupId).All(group => group.Count() == 2), "Six questions across three topics are 2 each");
        Check(Same(six.Select(question => question.Id), "t0-0", "t1-0", "t2-0", "t0-1", "t1-1", "t2-1"), "Round-robin walks topics in lockstep");

        var hundredPool = Enumerable.Range(0, 11)
            .SelectMany(topic => Enumerable.Range(0, 20).Select(index => Question($"g{topic}-{index}", "study-book", "Q", groupId: $"g{topic}")))
            .ToList();
        var hundred = Practice.BalancedSelection(hundredPool, 100, question => question.GroupId, list => list);
        Check(hundred.Count == 100, "A 100-question run is filled when the bank allows it");
        var hundredCounts = hundred.GroupBy(question => question.GroupId).Select(group => group.Count()).ToList();
        Check(hundredCounts.All(count => count is 9 or 10), "100 across 11 topics is 9 or 10 each");
        Check(hundredCounts.Max() - hundredCounts.Min() <= 1, "Topic counts differ by at most one");
        var shuffledHundred = Practice.BalancedSelection(hundredPool, 100, question => question.GroupId);
        Check(shuffledHundred.Count == 100 && shuffledHundred.Select(question => question.Id).Distinct().Count() == 100, "The default shuffle still fills the run without repeats");

        var shortTopic = new[]
        {
            Question("short-0", "p", "S", groupId: "short"),
            Question("long-0", "p", "L0", groupId: "long"),
            Question("long-1", "p", "L1", groupId: "long"),
            Question("long-2", "p", "L2", groupId: "long")
        };
        var redistributed = Practice.BalancedSelection(shortTopic, 4, question => question.GroupId, list => list);
        Check(redistributed.Count(question => question.GroupId == "short") == 1, "A thin topic is exhausted then remainder goes to others");
        Check(redistributed.Count(question => question.GroupId == "long") == 3, "Larger topics absorb leftover slots");
        Check(Practice.BalancedSelection(shortTopic, 0, question => question.GroupId).Count == 0, "A run of zero questions is empty");

        var pythonPack = new PracticeTopicPack
        {
            Id = "study-book",
            Title = "ML",
            Blurb = "",
            Groups = Array.Empty<PracticeGroup>(),
            Questions = new[]
            {
                Question("a", "study-book", "A", groupId: "python-engineering"),
                Question("b", "study-book", "B", groupId: "java-and-jvm")
            }
        };
        Check(Same(Practice.QuestionsMatching(pythonPack, new[] { "python-engineering" }).Select(question => question.Id), "a"), "Position group filter keeps only selected topics");
        Check(Practice.QuestionsMatching(pythonPack, Array.Empty<string>()).Count == 2, "An empty group filter means every group in the pack");

        var availableTopics = new[]
        {
            "python-engineering", "foundations", "angular", "engineering-practice", "java-and-jvm", "aws",
            "jr-javascript", "qa", "oop", "devops"
        };
        Check(Same(PracticeRole.All.Where(role => role.CardPackID is null).Select(role => role.Id), "ai-engineer", "frontend", "full-stack", "qa-automation"), "Four free-response interview roles are selectable");
        Check(Same(PracticeRole.All.Select(role => role.Id), "ai-engineer", "claude-architect", "ai-engineer-cards", "ml-core-cards", "typescript-cards", "frontend", "full-stack", "qa-automation"), "Roles keep the macOS menu order");
        Check(Practice.Role("frontend") == PracticeRole.Frontend && Practice.Role("nobody") is null, "Roles are found by id");
        var aiTopics = Practice.GroupIDs(PracticeRole.AIEngineer, availableTopics);
        Check(aiTopics.Contains("python-engineering"), "AI Engineer includes Python");
        Check(aiTopics.Contains("foundations"), "AI Engineer includes models/foundations");
        Check(!aiTopics.Contains("oop"), "AI Engineer does not use the JS/Java OOP pack");
        Check(aiTopics.Contains("aws"), "AI Engineer includes AWS");
        Check(aiTopics.Contains("devops"), "AI Engineer includes DevOps");
        Check(!aiTopics.Contains("angular"), "AI Engineer does not include Angular");
        Check(Same(Practice.GroupIDs(PracticeRole.Frontend, availableTopics), "jr-javascript", "angular", "oop", "engineering-practice"), "Frontend is filtered to UI topics that exist");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableTopics).Contains("angular"), "Full stack includes frontend");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableTopics).Contains("jr-javascript"), "Full stack includes the interview-guide JS topics");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableTopics).Contains("python-engineering"), "Full stack includes backend Python");
        Check(Practice.GroupIDs(PracticeRole.QAAutomation, availableTopics).Contains("qa"), "QA automation includes the interview-guide QA topic");
        Check(Practice.GroupIDs(PracticeRole.QAAutomation, availableTopics).Contains("engineering-practice"), "QA automation includes testing craft");
        Check(!Practice.GroupIDs(PracticeRole.QAAutomation, availableTopics).Contains("angular"), "QA automation does not include Angular");
        Check(
            Practice.HelpText(Question("h", "study-book", "Q", answer: "One token at a time.\n\nGeneration is autoregressive.")) == "One token at a time.\n\nGeneration is autoregressive.",
            "Help shows the prepared correct answer");
        Check(
            Practice.HelpText(Question("r", "p", "Q", rubric: "Fit the scaler on train only")) == "Fit the scaler on train only",
            "Help falls back to the bank rubric when answer is empty");
        Check(Practice.HelpText(Question("n", "p", "Q")) is null, "A question without answer or rubric has no Help");

        var pythonThenModels = new[]
        {
            Question("m0", "study-book", "emb", groupId: "foundations", topicId: "llm-internals"),
            Question("m1", "study-book", "train", groupId: "foundations", topicId: "llm-internals"),
            Question("p0", "study-book", "gil", groupId: "python-engineering", topicId: "python-concurrency"),
            Question("p1", "study-book", "async", groupId: "python-engineering", topicId: "python-concurrency")
        };
        var ordered = Practice.InterviewOrderedSelection(pythonThenModels, 4, question => question.GroupId, shuffle: list => list);
        Check(Same(ordered.Select(question => question.GroupId), "python-engineering", "python-engineering", "foundations", "foundations"), "A real loop finishes Python before models");
        Check(ordered.Count(question => question.GroupId == "python-engineering") == 2, "Equal split still holds");
        Check(
            Practice.QuestionSourceLine(
                Question("x", "study-book", "Q", groupId: "python-engineering", topicTitle: "Python concurrency & the GIL"),
                "Python engineering") == "Study book · Python engineering · Python concurrency & the GIL",
            "Question source names the study book chapter");
        Check(Practice.TopicKey(pythonThenModels[0], new[] { "foundations" }) == "llm-internals", "One selected group splits a run by topic");
        Check(Practice.TopicKey(pythonThenModels[0], new[] { "foundations", "python-engineering" }) == "foundations", "Several selected groups split a run by group");
    }

    [Fact]
    public void ActiveRecallReview()
    {
        var recallIdeas = Practice.KeyIdeas(RecallQuestion);
        Check(Same(recallIdeas, "Semantic similarity search", "Fast retrieval over large collections", "Metadata filtering for relevant context"), "Active Recall extracts three stable, marker-free key ideas");
        var recallCoverage = Practice.CoveredKeyIdeas(RecallAnswer, recallIdeas);
        Check(Same(recallCoverage, true, true, false), "Recall coverage matches morphology and preserves idea order");
        var negatedIdeas = new[] { "RAG retrieves external context", "Embeddings capture semantic similarity" };
        Check(Same(Practice.CoveredKeyIdeas("RAG does not retrieve external context.", negatedIdeas), false, false), "A negated answer does not cover the positive idea it contradicts");
        Check(Same(Practice.CoveredKeyIdeas("RAG retrieves external context, not just parametric memory.", negatedIdeas), true, false), "A negation in another clause does not hide a positive match");
        Check(Same(Practice.CoveredKeyIdeas("RAG retrieves external context and embeddings capture semantic similarity.", negatedIdeas), true, true), "Answers without negation keep the plain overlap rule");
        Check(Same(Practice.CoveredKeyIdeas("The GIL does not block I/O-bound threads.", new[] { "The GIL does not block I/O waits" }), true), "A negated idea is covered by a matching negated answer");
        Check(Same(Practice.CoveredKeyIdeas("The GIL blocks I/O-bound threads.", new[] { "The GIL does not block I/O waits" }), false), "A positive answer does not cover a negated idea");
        var recallReview = Practice.RecallReview(RecallQuestion, RecallAnswer);
        Check(recallReview.CoveredCount == 2, "Recall review reports two covered ideas");
        Check(Math.Abs(recallReview.Score - 2.0 / 3.0) < 0.0001, "Recall score is the covered-idea ratio");
        Check(Practice.KeyIdeas(RecallQuestion).SequenceEqual(recallIdeas), "Key-idea extraction is deterministic");

        var sparseQuestion = Question(
            "sparse-recall", "study-book", "Why use idempotency keys?",
            hints: new[] { "A retry will not act twice", "What happens on retries?" },
            rubric: "Retries are inevitable. Idempotency prevents duplicate side effects.");
        var sparseIdeas = Practice.KeyIdeas(sparseQuestion);
        Check(sparseIdeas.Any(idea => idea.ContainsIgnoringCase("retries")), "Recall falls back to rubric sentences");
        Check(!sparseIdeas.Any(idea => idea.EndsWith('?')), "Question-shaped hints are not presented as key ideas");

        Check((Practice.NextReviewDate(PracticeRecallRating.Again, RecallNow) - RecallNow).TotalSeconds == 300, "Again is due in five minutes");
        Check((Practice.NextReviewDate(PracticeRecallRating.Hard, RecallNow) - RecallNow).TotalSeconds == 86_400, "Hard is due tomorrow");
        Check((Practice.NextReviewDate(PracticeRecallRating.GotIt, RecallNow) - RecallNow).TotalSeconds == 259_200, "Got it is due in three days");

        var otherAnswer = RecallAnswerRecord("other", 0.5, PracticeRecallRating.Hard, RecallNow.AddSeconds(86_400));
        Check(Math.Abs(Practice.RunOverallScore(new[] { RepeatedLow, RepeatedHigh, otherAnswer }) - 0.75) < 0.0001, "Run score uses the latest same-run retry");

        var dueQuestion = Question("due", "study-book", "Due");
        var newQuestion = Question("new", "study-book", "New");
        var futureQuestion = Question("future", "study-book", "Future");
        var priority = Practice.PrioritizedRecallQuestions(
            new[] { futureQuestion, newQuestion, dueQuestion },
            new Dictionary<string, PracticeScoredAnswer>
            {
                [Key("due")] = RecallAnswerRecord("due", 0.0, PracticeRecallRating.Again, RecallNow.AddSeconds(-1)),
                [Key("future")] = RecallAnswerRecord("future", 1.0, PracticeRecallRating.GotIt, RecallNow.AddSeconds(100))
            },
            RecallNow);
        Check(Same(priority.Select(question => question.Id), "due", "new", "future"), "Due recall questions come before unseen and future questions");

        const string legacyJson = """
        {
          "id": "legacy",
          "packId": "study-book",
          "packTitle": "Study book",
          "startedAt": "2026-08-28T10:00:00Z",
          "finishedAt": "2026-08-28T10:05:00Z",
          "answers": [{
            "questionId": "legacy-q",
            "packId": "study-book",
            "question": "Q",
            "answer": "A",
            "usedHelp": false,
            "mark": "none",
            "rawScore": 1,
            "finalScore": 1,
            "feedback": "Correct option",
            "strengths": [],
            "gaps": []
          }],
          "overallScore": 1,
          "helpedCount": 0
        }
        """;
        var legacyRun = JsonSerializer.Deserialize<PracticeRunRecord>(legacyJson, PracticeJson.StoreOptions);
        Assert.NotNull(legacyRun);
        Check(legacyRun.Mode is null, "Legacy run JSON decodes without a mode");
        Check(legacyRun.Answers[0].RecallRating is null, "Legacy answer JSON decodes without recall metadata");
        Check(legacyRun.Answers[0].RecallResponse is null, "Legacy answer JSON decodes without response revisions");
        Check(legacyRun.Answers[0].RecallConfidence is null, "Legacy answer JSON decodes without confidence");
        Check(legacyRun.Answers[0].CoverageOverrides is null, "Legacy answer JSON decodes without coverage overrides");
        Check(legacyRun.TargetDate is null, "Legacy run JSON decodes without a target date");
        Check(legacyRun.StartedAt == new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc) && legacyRun.StartedAt.Kind == DateTimeKind.Utc, "Stored dates are read as UTC");
        Check(legacyRun.Answers[0].InitialRecallAnswer == "A" && legacyRun.Answers[0].EffectiveRecallAnswer == "A" && legacyRun.Answers[0].RevisedRecallAnswer is null, "Legacy answers stand in for the recall response");
    }

    [Fact]
    public void RecallLearningStateAndResumableSessions()
    {
        Check(Same(PracticeRecallConfidences.AllCases.Select(confidence => confidence.RawValue()), "unsure", "mostly", "very"), "Confidence has stable Codable values");
        Check(PracticeRecallConfidence.Mostly.Title() == "Mostly sure", "Confidence has learner-facing copy");
        Check(Same(PracticeRecallRatings.AllCases.Select(rating => rating.RawValue()), "again", "hard", "gotIt"), "Ratings have stable Codable values");
        Check(RevisedResponse.WasRevised, "Recall responses retain a repair attempt");
        Check(RevisedResponse.Current.Contains("relevant context", StringComparison.Ordinal), "The revised response is the current response");
        var blankRevision = new PracticeRecallResponse("Initial", "  ");
        Check(!blankRevision.WasRevised && blankRevision.Current == "Initial", "Blank revisions do not erase the initial recall");

        var recallCoverage = Practice.CoveredKeyIdeas(RecallAnswer, Practice.KeyIdeas(RecallQuestion));
        var correctedCoverage = Practice.ApplyCoverageOverrides(recallCoverage, new[]
        {
            new PracticeCoverageOverride(0, false),
            new PracticeCoverageOverride(2, true),
            new PracticeCoverageOverride(20, true),
            new PracticeCoverageOverride(0, true)
        });
        Check(Same(correctedCoverage, true, true, true), "Manual coverage uses the final valid correction and ignores invalid indices");
        var correctedReview = Practice.RecallReview(RecallQuestion, RecallAnswer, new[] { new PracticeCoverageOverride(2, true) });
        Check(Same(correctedReview.AutomaticCovered, true, true, false), "Recall review retains estimated coverage");
        Check(Same(correctedReview.Covered, true, true, true) && correctedReview.Score == 1, "Manual coverage changes the displayed score");

        var none = Array.Empty<PracticeScoredAnswer>();
        var firstAdaptive = Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, none, PracticeRecallConfidence.Mostly, RecallNow);
        Check(firstAdaptive == 259_200, "First mostly-sure Got it keeps the three-day baseline");
        var growingAdaptive = Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, new[] { RepeatedHigh }, PracticeRecallConfidence.Very, RecallNow);
        var cautiousAdaptive = Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, new[] { RepeatedHigh }, PracticeRecallConfidence.Unsure, RecallNow);
        Check(growingAdaptive > firstAdaptive, "Successful prior attempts expand the review interval");
        Check(cautiousAdaptive < growingAdaptive, "Low confidence shortens the same review interval");
        var assistedHigh = RepeatedHigh with
        {
            Answer = "Revised after key ideas",
            UsedHelp = true,
            Mark = PracticeHelpMark.Yellow,
            Feedback = "Assisted",
            NextReviewAt = RecallNow
        };
        Check(
            Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, new[] { assistedHigh }, PracticeRecallConfidence.Mostly, RecallNow) == firstAdaptive,
            "Assisted recall does not expand the independent-recall interval");
        var twoHigh = new[] { RepeatedHigh, RepeatedHigh };
        var targetCapped = Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, twoHigh, PracticeRecallConfidence.Very, RecallNow, RecallNow.AddSeconds(2 * 86_400));
        Check(targetCapped == 86_400, "A target date keeps another review opportunity before the interview");
        var expiredTargetInterval = Practice.AdaptiveReviewInterval(PracticeRecallRating.GotIt, twoHigh, PracticeRecallConfidence.Very, RecallNow, RecallNow.AddSeconds(-86_400));
        Check(expiredTargetInterval > 0, "An expired target date no longer makes every review immediately due");
        Check(
            (Practice.AdaptiveNextReviewDate(PracticeRecallRating.Again, none, PracticeRecallConfidence.Very, RecallNow) - RecallNow).TotalSeconds == 300,
            "Adaptive Again remains an immediate five-minute retry");

        Check(Snapshot.CanResume && Same(Snapshot.QuestionReferences.Select(reference => reference.QuestionId), "due", "new"), "Snapshot contains enough ordered state to resume");
        Check(Snapshot.QuestionStates[0].IsRepairingGap && Snapshot.QuestionStates[0].RepairIdeaIndex == 2 && Snapshot.QuestionStates[0].RepairDraft.Length > 0, "Snapshot preserves an unfinished one-gap repair and its target idea");
        Check(Snapshot.Version == PracticeSessionSnapshot.CurrentVersion && Snapshot.BaseQuestionCount == 2, "A new snapshot takes the current version and counts its questions");
        var snapshotJson = JsonSerializer.Serialize(Snapshot, PracticeJson.StoreOptions);
        Check(JsonSerializer.Deserialize<PracticeSessionSnapshot>(snapshotJson, PracticeJson.StoreOptions) == Snapshot, "Full in-progress state survives Codable persistence");
        Check(snapshotJson.Contains("\"startedAt\": \"2023-11-14T22:13:20Z\"", StringComparison.Ordinal), "Dates are written as whole-second UTC ISO 8601");
        Check(snapshotJson.Contains("\"mode\": \"learn\"", StringComparison.Ordinal) && snapshotJson.Contains("\"confidence\": \"mostly\"", StringComparison.Ordinal), "Enums are written with their macOS raw values");
        Check(!snapshotJson.Contains("null", StringComparison.Ordinal), "Absent optionals are left out, as the macOS encoder does");

        var restored = Practice.SnapshotAfterUndoingLastRating(Snapshot);
        Assert.NotNull(restored);
        Check(restored.Answers.IsEmpty, "Rating Undo removes only the newly rated answer");
        Check(restored.CurrentQuestionIndex == 0, "Rating Undo returns to the rated question");
        Check(restored.QuestionStates[0] == FirstState, "Rating Undo restores the exact pre-rating learner state");
        Check(restored.RequeuedQuestionKeys.IsEmpty, "Rating Undo restores the pre-Again retry-key set");
        Check(restored.LastRatingUndo is null, "Rating Undo is single-level");
        Check(Practice.SnapshotAfterUndoingLastRating(restored) is null, "There is nothing to undo twice");

        var retryKeyBeforeRating = Key("due");
        var retrySnapshot = new PracticeSessionSnapshot(
            packId: "study-book",
            packTitle: "Study book",
            mode: PracticeRunMode.Learn,
            questionStates: new[] { FirstState, SecondState },
            startedAt: RecallNow,
            updatedAt: RecallNow,
            id: "retry-undo",
            currentQuestionIndex: 1,
            answers: new[] { RepeatedLow },
            requeuedQuestionKeys: new[] { retryKeyBeforeRating },
            lastRatingUndo: Snapshot.LastRatingUndo! with { RequeuedQuestionKeysBeforeRating = new[] { retryKeyBeforeRating } });
        Check(
            Practice.SnapshotAfterUndoingLastRating(retrySnapshot)?.RequeuedQuestionKeys.SequenceEqual(new[] { retryKeyBeforeRating }) == true,
            "Undoing Again on an already-requeued retry preserves the one-retry bound");

        const string legacySnapshotJson = """
        {
          "packId": "study-book",
          "packTitle": "Study book",
          "questionIds": ["legacy-q", "legacy-q-2"],
          "currentQuestionIndex": 1,
          "startedAt": "2026-08-28T10:00:00Z"
        }
        """;
        var legacySnapshot = JsonSerializer.Deserialize<PracticeSessionSnapshot>(legacySnapshotJson, PracticeJson.StoreOptions);
        Assert.NotNull(legacySnapshot);
        Check(legacySnapshot.Mode == PracticeRunMode.Learn, "Early snapshots default to Active Recall");
        Check(Same(legacySnapshot.QuestionReferences.Select(reference => reference.QuestionId), "legacy-q", "legacy-q-2"), "Early questionIds migrate into question states");
        Check(legacySnapshot.QuestionReferences.All(reference => reference.PackId == "study-book"), "Migrated question states keep the session's pack");
        Check(legacySnapshot.UpdatedAt == legacySnapshot.StartedAt, "Early snapshots default updatedAt to startedAt");
        Check(legacySnapshot.Version == 1 && legacySnapshot.Id == "legacy-session" && legacySnapshot.BaseQuestionCount == 2, "Early snapshots take version 1, the legacy id and their own question count");
        var rejected = false;
        try
        {
            JsonSerializer.Deserialize<PracticeSessionSnapshot>("""{ "packId": "study-book" }""", PracticeJson.StoreOptions);
        }
        catch (JsonException)
        {
            rejected = true;
        }
        Check(rejected, "A snapshot without startedAt is rejected");
    }

    [Fact]
    public void MasteryAndTodaysPlan()
    {
        var learningQuestion = Question("learning", "study-book", "How does metadata filtering constrain retrieval?", groupId: "genai-and-llm", topicId: "retrieval");
        var groupedDue = Question("due", "study-book", "When should RAG be used?", groupId: "genai-and-llm", topicId: "retrieval");
        var groupedNew = Question("new", "study-book", "What is fine-tuning?", groupId: "genai-and-llm", topicId: "adaptation");
        var groupedSolid = Question("future", "study-book", "How does semantic retrieval work?", groupId: "genai-and-llm", topicId: "retrieval");
        var learningAnswer = RecallAnswerRecord("learning", 0.5, PracticeRecallRating.Hard, RecallNow.AddSeconds(86_400));
        var masteryAnswers = new Dictionary<string, PracticeScoredAnswer>
        {
            [Key("due")] = RepeatedLow,
            [Key("future")] = RepeatedHigh,
            [Key("learning")] = learningAnswer
        };
        var masteryQuestions = new[] { groupedDue, groupedNew, groupedSolid, learningQuestion };
        Check(Practice.MasteryState(null, null, RecallNow) == PracticeMasteryState.New, "An unseen question is New");
        Check(Practice.MasteryState(RepeatedHigh, new[] { RepeatedHigh }, RecallNow) == PracticeMasteryState.Learning, "One strong recall remains Learning until it is repeated");
        Check(Practice.MasteryState(RepeatedHigh, new[] { RepeatedHigh, RepeatedHigh }, RecallNow) == PracticeMasteryState.Solid, "Two strong unassisted recalls establish Solid mastery");
        var latestFailure = RecallAnswerRecord("repeat", 0.3, PracticeRecallRating.Hard, RecallNow.AddSeconds(86_400));
        Check(
            Practice.MasteryState(latestFailure, new[] { RepeatedHigh, RepeatedHigh, latestFailure }, RecallNow) == PracticeMasteryState.Learning,
            "A latest failed recall returns a previously solid question to Learning");
        Check(Same(PracticeMasteryStates.AllCases.Select(state => state.Title()), "New", "Learning", "Solid", "Due"), "Mastery states have learner-facing titles");
        var masteryAttempts = new Dictionary<string, List<PracticeScoredAnswer>>
        {
            [Key("due")] = new() { RepeatedLow },
            [Key("future")] = new() { RepeatedHigh, RepeatedHigh },
            [Key("learning")] = new() { learningAnswer }
        };
        var mastery = Practice.TopicMasterySummaries(masteryQuestions, masteryAnswers, masteryAttempts, RecallNow);
        Check(mastery.Count == 1 && mastery[0].Topic.Title == "RAG & agents", "Mastery groups questions into learner-facing topics");
        Check(mastery[0].NewCount == 1, "Mastery counts unseen questions as New");
        Check(mastery[0].LearningCount == 1, "Mastery counts difficult future reviews as Learning");
        Check(mastery[0].SolidCount == 1, "Mastery counts strong future reviews as Solid");
        Check(mastery[0].DueCount == 1 && mastery[0].TotalCount == 4, "Mastery counts elapsed reviews as Due");
        Check(mastery[0].MasteredFraction == 0.25, "A quarter of the topic is mastered");

        var today = Practice.TodaysPlan(masteryQuestions, masteryAnswers, masteryAttempts, RecallNow, maximumQuestions: 3, estimatedSecondsPerQuestion: 60);
        Check(today.DueCount == 1 && today.WeakCount == 1 && today.NewCount == 1, "Today's Plan exposes due, weak, and new counts");
        Check(today.SolidCount == 1, "Today's Plan reports already-solid questions separately");
        Check(today.RecommendedCount == 3 && today.EstimatedMinutes == 3, "Today's Plan bounds the session and estimates its duration");
        var defaultPlan = Practice.TodaysPlan(masteryQuestions, masteryAnswers, masteryAttempts, RecallNow);
        Check(defaultPlan.RecommendedCount == 3 && defaultPlan.EstimatedMinutes == 3 && defaultPlan.TotalQuestionCount == 4, "At 48 seconds a question, three questions round up to three minutes");

        var runs = new[]
        {
            Practice.MakeRunRecord(PracticeTopicPack.Aws, RecallNow, RecallNow.AddSeconds(120), new[] { RepeatedHigh }, PracticeRunMode.Learn, id: "later"),
            Practice.MakeRunRecord(PracticeTopicPack.Aws, RecallNow, RecallNow.AddSeconds(60), new[] { RepeatedLow, RepeatedHigh }, PracticeRunMode.Learn, id: "earlier")
        };
        Check(runs[1].OverallScore == 1 && runs[1].HelpedCount == 0 && runs[1].Answers.Count == 2, "A run keeps every attempt and scores the latest one");
        Check(Practice.LatestRecallAnswers(runs)[Key("repeat")] == RepeatedHigh, "The latest recall answer wins across runs");
        Check(Practice.RecallAttempts(runs)[Key("repeat")].Count == 3, "Recall attempts accumulate across runs in date order");
        Check(Practice.MakeRunRecord(PracticeTopicPack.Aws, RecallNow, RecallNow, Array.Empty<PracticeScoredAnswer>()).Id.Length == 36, "A run gets a UUID when none is given");
        Check(Practice.HeuristicRawScore("What is RAG?", "") == 0 && Practice.HeuristicRawScore("What is RAG?", "It retrieves context.") == 0.25, "The offline score is zero for silence and low for a few words");
    }

    [Fact]
    public void ContrastPairs()
    {
        var ragContrast = Question("rag-contrast", "study-book", "When would you use RAG instead of fine-tuning?", groupId: "genai-and-llm", topicId: "rag-vs-fine-tuning");
        var tuningContrast = Question("tuning-contrast", "study-book", "How does fine-tuning change model behavior compared with retrieval?", groupId: "genai-and-llm", topicId: "rag-vs-fine-tuning");
        var unrelatedContrast = Question("cache", "study-book", "Explain prompt caching costs.", groupId: "genai-and-llm", topicId: "caching");
        var contrastQuestions = new[] { unrelatedContrast, ragContrast, tuningContrast };
        var contrastPairs = Practice.ContrastPairs(contrastQuestions, now: RecallNow, limit: 2);
        Check(contrastPairs.Count == 1, "Contrast selection does not invent unrelated pairs");
        Check(
            contrastPairs.Count == 1 && new[] { contrastPairs[0].First.Id, contrastPairs[0].Second.Id }.ToHashSet().SetEquals(new[] { "rag-contrast", "tuning-contrast" }),
            "Contrast selection pairs confusable concepts");
        Check(Practice.ContrastQuestionSelection(contrastQuestions).Count == 2, "Contrast selection can flatten pairs into a practice queue");
        var broadTopicOnly = new[]
        {
            Question("ts-overview", "study-book", "What is TypeScript?", groupId: "python-engineering", topicId: "typescript"),
            Question("ts-types", "study-book", "Name the basic primitive types.", groupId: "python-engineering", topicId: "typescript")
        };
        Check(Practice.ContrastPairs(broadTopicOnly).Count == 0, "A shared broad topic id alone does not invent a contrast exercise");

        var rankedQuestions = new[]
        {
            Question("solid-a", "study-book", "Describe alpha quorum behavior.", groupId: "genai-and-llm", topicId: "alpha"),
            Question("solid-b", "study-book", "When choose alpha quorum consensus?", groupId: "genai-and-llm", topicId: "alpha"),
            Question("learning-a", "study-book", "Describe beta index behavior.", groupId: "genai-and-llm", topicId: "beta"),
            Question("learning-b", "study-book", "When use beta index retrieval?", groupId: "genai-and-llm", topicId: "beta")
        };
        var rankedLatest = new Dictionary<string, PracticeScoredAnswer>();
        var rankedAttempts = new Dictionary<string, List<PracticeScoredAnswer>>();
        foreach (var question in rankedQuestions)
        {
            var answer = RecallAnswerRecord(question.Id, 1, PracticeRecallRating.GotIt, RecallNow.AddSeconds(259_200));
            var key = Practice.RecallReviewKey(question.PackId, question.Id);
            rankedLatest[key] = answer;
            rankedAttempts[key] = question.TopicId == "alpha" ? new() { answer, answer } : new() { answer };
        }
        var weakestContrast = Practice.ContrastPairs(rankedQuestions, rankedLatest, rankedAttempts, RecallNow, limit: 1);
        Check(
            weakestContrast.Count == 1 && new[] { weakestContrast[0].First.Id, weakestContrast[0].Second.Id }.ToHashSet().SetEquals(new[] { "learning-a", "learning-b" }),
            "Contrast ranking uses full mastery evidence and prioritizes the learning pair over a solid pair");

        Check(Practice.LearnOptionMark(0, null, 1) == PracticeLearnOptionMark.Unmarked, "Options stay unmarked until a choice");
        Check(Practice.LearnOptionMark(1, 1, 1) == PracticeLearnOptionMark.SelectedCorrect, "A correct click is marked green");
        Check(Practice.LearnOptionMark(2, 2, 0) == PracticeLearnOptionMark.SelectedWrong, "A wrong click is marked red");
        Check(Practice.LearnOptionMark(0, 2, 0) == PracticeLearnOptionMark.RevealedCorrect, "The right option still turns green after a miss");
        Check(Practice.LearnOptionMark(3, 2, 0) == PracticeLearnOptionMark.Unmarked, "Untouched options stay unmarked");
    }

    [Fact]
    public void StoreResumePersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "interview-master-practice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storeDirectory = Path.Combine(root, "store");
            var store = new PracticeStore(storeDirectory);
            Check(store.SaveInProgressSession(Snapshot), "PracticeStore reports successful snapshot persistence");
            var completedRun = new PracticeRunRecord
            {
                Id = "store-run",
                PackId = "study-book",
                PackTitle = "Study book",
                StartedAt = RecallNow,
                FinishedAt = RecallNow.AddSeconds(60),
                Answers = new[] { RepeatedHigh },
                OverallScore = 1,
                HelpedCount = 0,
                Mode = PracticeRunMode.Learn
            };
            Check(store.Append(completedRun), "PracticeStore reports successful run persistence");
            var runsPath = Path.Combine(storeDirectory, "runs.json");
            var beforeIdenticalAppend = File.ReadAllBytes(runsPath);
            Check(store.Append(completedRun), "PracticeStore treats a repeated completed session id as already saved");
            Check(File.ReadAllBytes(runsPath).SequenceEqual(beforeIdenticalAppend), "An identical idempotent append does not rewrite history");
            Check(Encoding.UTF8.GetString(beforeIdenticalAppend).Contains("\"finishedAt\": \"2023-11-14T22:14:20Z\"", StringComparison.Ordinal), "History is written in the macOS file format");
            Check(Directory.GetFiles(storeDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(new[] { "in-progress.json", "runs.json" }), "Atomic writes leave no temporary files behind");

            var revisedRun = completedRun with { FinishedAt = RecallNow.AddSeconds(120), Answers = new[] { RepeatedLow }, OverallScore = 0 };
            Check(store.Append(revisedRun), "PracticeStore upserts changed content for an existing session id");
            Check(Same(store.AllRuns(), revisedRun), "A same-id upsert replaces rather than duplicates the run");
            var reloadedStore = new PracticeStore(storeDirectory);
            Check(Same(reloadedStore.AllRuns(), revisedRun), "A same-id upsert survives disk reload");
            Check(reloadedStore.InProgressSession() == Snapshot, "PracticeStore reloads the separate resumable snapshot");
            Check(Same(reloadedStore.ProgressInputs("study-book"), new PracticeProgressInput(revisedRun.FinishedAt, 0, "study-book")) && reloadedStore.ProgressInputs("aws").Count == 0, "Progress inputs come from stored runs, filtered by pack");
            Check(reloadedStore.ClearInProgressSession(), "PracticeStore reports successful snapshot cleanup");
            Check(reloadedStore.InProgressSession() is null, "Clearing a completed/cancelled session removes resumable state");
            Check(Same(new PracticeStore(storeDirectory).AllRuns(), revisedRun), "Clearing resumable state never clears history");

            var failedUpsertDirectory = Path.Combine(root, "upsert-failure");
            var failedUpsertStore = new PracticeStore(failedUpsertDirectory);
            Check(failedUpsertStore.Append(completedRun), "Upsert rollback fixture starts with a persisted run");
            Directory.Delete(failedUpsertDirectory, recursive: true);
            File.WriteAllText(failedUpsertDirectory, "not a directory");
            Check(!failedUpsertStore.Append(revisedRun), "PracticeStore reports a failed same-id upsert");
            Check(Same(failedUpsertStore.AllRuns(), completedRun), "A failed same-id upsert restores the previous in-memory run");

            var malformedStoreDirectory = Path.Combine(root, "malformed");
            Directory.CreateDirectory(malformedStoreDirectory);
            var malformedRunsPath = Path.Combine(malformedStoreDirectory, "runs.json");
            var malformedRunsData = Encoding.UTF8.GetBytes("{ definitely-not-valid-json");
            File.WriteAllBytes(malformedRunsPath, malformedRunsData);
            var malformedStore = new PracticeStore(malformedStoreDirectory);
            Check(malformedStore.AllRuns().Count == 0, "Malformed history is not exposed as valid runs");
            Check(!malformedStore.Append(completedRun), "PracticeStore fails closed after malformed history cannot load");
            Check(File.ReadAllBytes(malformedRunsPath).SequenceEqual(malformedRunsData), "A failed-closed append preserves the malformed history file byte-for-byte");

            var malformedSnapshotDirectory = Path.Combine(root, "malformed-snapshot");
            Directory.CreateDirectory(malformedSnapshotDirectory);
            var malformedSnapshotPath = Path.Combine(malformedSnapshotDirectory, "in-progress.json");
            var malformedSnapshotData = Encoding.UTF8.GetBytes("[ definitely-not-a-session");
            File.WriteAllBytes(malformedSnapshotPath, malformedSnapshotData);
            var recoveredSnapshotStore = new PracticeStore(malformedSnapshotDirectory);
            Check(recoveredSnapshotStore.InProgressSession() is null, "Malformed snapshot is not exposed as a resumable session");
            var recoveryFiles = Directory.GetFiles(malformedSnapshotDirectory)
                .Where(path => Path.GetFileName(path).StartsWith("in-progress-recovery-", StringComparison.Ordinal) && Path.GetExtension(path) == ".json")
                .ToList();
            Check(recoveryFiles.Count == 1, "Malformed snapshot is quarantined under one unique adjacent recovery name");
            Check(recoveryFiles.Count == 1 && File.ReadAllBytes(recoveryFiles[0]).SequenceEqual(malformedSnapshotData), "Snapshot quarantine preserves the malformed bytes exactly");
            Check(!File.Exists(malformedSnapshotPath), "Quarantine frees the canonical snapshot path");
            Check(recoveredSnapshotStore.SaveInProgressSession(Snapshot), "A new snapshot can be saved after malformed data is quarantined");
            Check(new PracticeStore(malformedSnapshotDirectory).InProgressSession() == Snapshot, "The replacement snapshot reloads while quarantined recovery data remains adjacent");
            Check(recoveryFiles.Count == 1 && File.ReadAllBytes(recoveryFiles[0]).SequenceEqual(malformedSnapshotData), "Saving a replacement never modifies quarantined recovery data");

            var blockedStorePath = Path.Combine(root, "blocked");
            File.WriteAllText(blockedStorePath, "not a directory");
            var blockedStore = new PracticeStore(blockedStorePath);
            Check(!blockedStore.SaveInProgressSession(Snapshot), "PracticeStore reports snapshot write failure");
            Check(!blockedStore.Append(completedRun), "PracticeStore reports run write failure");
            Check(blockedStore.AllRuns().Count == 0, "Failed run persistence rolls back the in-memory append");
            Check(blockedStore.InProgressSession() is null, "A failed snapshot write is not kept in memory");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
