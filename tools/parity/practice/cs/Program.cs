using System.Globalization;
using System.Text;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

const string sep = "\u0001";
const string sub = "\u0002";
static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
static string Bits(IEnumerable<bool> values) => string.Concat(values.Select(value => value ? "1" : "0"));
static int Utf8Compare(string a, string b)
{
    var x = Encoding.UTF8.GetBytes(a);
    var y = Encoding.UTF8.GetBytes(b);
    for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
    {
        if (x[i] != y[i]) return x[i].CompareTo(y[i]);
    }
    return x.Length.CompareTo(y.Length);
}
static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

var mode = args[0];
var bank = PracticeBankLoader.LoadDirectory(args[1]);
var target = args[2];
var all = bank.Packs.SelectMany(pack => pack.Questions).ToList();
var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime;

var response = new PracticeRecallResponse("i", "r");
var answerFull = new PracticeScoredAnswer
{
    QuestionId = "q/1", PackId = "study-book", Question = "What is \"RAG\"? <b>café</b>", Answer = "Line one\nLine two — ünïcode 🙂",
    UsedHelp = true, Mark = PracticeHelpMark.Yellow, RawScore = 0.65, FinalScore = 0.26, Feedback = "ok",
    Strengths = new[] { "s1" }, Gaps = new[] { "g1", "g2" }, RecallRating = PracticeRecallRating.GotIt,
    NextReviewAt = now.AddSeconds(259_200), RecallResponse = response, RecallConfidence = PracticeRecallConfidence.Very,
    CoverageOverrides = new[] { new PracticeCoverageOverride(1, true) }, ReviewedAt = now.AddSeconds(5),
    CardResult = new PracticeCardResult { SelectedOptionID = "a", CorrectOptionID = "b", CorrectAnswer = "B", Explanation = "because", Topic = "T", ContentVersion = 3 }
};
var answerPlain = new PracticeScoredAnswer
{
    QuestionId = "q/2", PackId = "study-book", Question = "Q", Answer = "A", UsedHelp = false, Mark = PracticeHelpMark.None,
    RawScore = 1, FinalScore = 1, Feedback = "", Strengths = Array.Empty<string>(), Gaps = Array.Empty<string>()
};
var run = new PracticeRunRecord
{
    Id = "RUN-1", PackId = "study-book", PackTitle = "Study book", StartedAt = now, FinishedAt = now.AddSeconds(60),
    Answers = new[] { answerFull, answerPlain }, OverallScore = 0.455, HelpedCount = 1, Mode = PracticeRunMode.Rehearse,
    TargetDate = now.AddSeconds(86_400 * 14)
};
var run2 = new PracticeRunRecord
{
    Id = "RUN-2", PackId = "aws", PackTitle = "AWS", StartedAt = now, FinishedAt = now.AddSeconds(120),
    Answers = Array.Empty<PracticeScoredAnswer>(), OverallScore = 0, HelpedCount = 0
};
var firstClaude = bank.Packs.First(pack => pack.Id == "claude-architect").Questions[0];
var state1 = new PracticeSessionQuestionState(new PracticeQuestionReference("study-book", "q/1"))
{
    DraftResponse = "draft", RecallResponse = response, Confidence = PracticeRecallConfidence.Unsure, HasRevealedKeyIdeas = true,
    CoverageOverrides = new[] { new PracticeCoverageOverride(0, false) }, UsedHelp = true, RepairDraft = "rd", IsRepairingGap = true, RepairIdeaIndex = 0
};
var state2 = new PracticeSessionQuestionState(new PracticeQuestionReference(firstClaude.PackId, firstClaude.Id))
{
    DraftResponse = firstClaude.Card!.CorrectOptionID, CardQuestion = firstClaude
};
var undo = new PracticeRatingUndoMetadata
{
    Question = state1.Reference, QuestionIndex = 0, AnswerCountBeforeRating = 0, QuestionCountBeforeRating = 2,
    PreviousQuestionState = state1, RatedAt = now.AddSeconds(1), RequeuedQuestionKeysBeforeRating = new[] { "k" }
};
var snapshot = new PracticeSessionSnapshot(
    packId: "study-book", packTitle: "Study book", mode: PracticeRunMode.Interview, questionStates: new[] { state1, state2 },
    startedAt: now, updatedAt: now.AddSeconds(30), id: "SNAP-1", roleId: "ai-engineer", groupIds: new[] { "a", "b" },
    currentQuestionIndex: 1, answers: new[] { answerFull }, requeuedQuestionKeys: new[] { Practice.RecallReviewKey("study-book", "q/1") },
    lastRatingUndo: undo, targetDate: now.AddSeconds(100));

if (mode == "read")
{
    var other = new PracticeStore(target);
    Console.WriteLine($"runs {other.AllRuns().SequenceEqual(new[] { run, run2 })} snapshot {other.InProgressSession() == snapshot}");
    return;
}

var lines = new List<string>();
lines.Add("packs" + sep + string.Join(",", bank.Packs.Select(pack => pack.Id)));
lines.Add("positions" + sep + string.Join("|", bank.Positions.Select(p => $"{p.Id}:{p.Title}:{p.PackId}:{string.Join("+", p.GroupIds)}")));
lines.Add("groups" + sep + string.Join("|", Practice.BankGroups(bank).Select(group => group.Id + ":" + group.Title)));
for (var index = 0; index < all.Count; index++)
{
    var q = all[index];
    var id = q.PackId + "/" + q.Id;
    var ideas = Practice.KeyIdeas(q);
    var ideas5 = Practice.KeyIdeas(q, 5);
    var previous = all[(index + all.Count - 1) % all.Count];
    lines.Add(id + sep + "ideas" + sep + string.Join(sub, ideas.Select(Esc)));
    lines.Add(id + sep + "ideas5" + sep + string.Join(sub, ideas5.Select(Esc)));
    lines.Add(id + sep + "stem" + sep + Esc(Practice.AnswerStem(q)));
    lines.Add(id + sep + "stem60" + sep + Esc(Practice.AnswerStem(q, 60)));
    lines.Add(id + sep + "norm" + sep + Esc(Practice.NormalizeQuestionText(q.Text)));
    lines.Add(id + sep + "key" + sep + string.Join(",", Practice.CurriculumOrderKey(q)));
    lines.Add(id + sep + "source" + sep + Esc(Practice.QuestionSourceLine(q, Practice.GroupDisplayTitle(q.GroupId, q.TopicTitle))));
    lines.Add(id + sep + "coverSelf" + sep + Bits(Practice.CoveredKeyIdeas(q.Answer, ideas5)));
    lines.Add(id + sep + "coverText" + sep + Bits(Practice.CoveredKeyIdeas(q.Text, ideas5)));
    lines.Add(id + sep + "coverPrev" + sep + Bits(Practice.CoveredKeyIdeas(previous.Answer, ideas5)));
    lines.Add(id + sep + "heur" + sep + F(Practice.HeuristicRawScore(q.Text, q.Answer), "F9") + "," + F(Practice.HeuristicRawScore(q.Text, previous.Text), "F9"));
    var tokens = Practice.Tokenize(q.Text).ToList();
    tokens.Sort(Utf8Compare);
    lines.Add(id + sep + "tokens" + sep + string.Join(",", tokens));
    lines.Add(id + sep + "heading" + sep + Bits(q.Answer.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(Practice.AnswerLineIsHeading)));
    lines.Add(id + sep + "help" + sep + Esc(Practice.HelpText(q) ?? "<nil>"));
    lines.Add(id + sep + "topicKey" + sep + Practice.TopicKey(q, new[] { q.GroupId }) + "|" + Practice.TopicKey(q, new[] { "a", "b" }));
    if (index % 5 == 0)
    {
        var choices = Practice.LearnChoices(q, all, list => list);
        lines.Add(id + sep + "learn" + sep + string.Join(sub, choices.Select(choice => (choice.IsCorrect ? "+" : "-") + Esc(choice.Text))));
    }
    if (q.Card is { } card && index % 7 == 0)
    {
        var scored = card.Options.Select(option =>
        {
            var result = Practice.ScoreCard(q, option.Id);
            return result is null ? "nil" : $"{(result.FinalScore == 1 ? 1 : 0)}:" + Esc(result.Feedback) + ":" + Esc(result.CardResult?.CorrectAnswer ?? "");
        });
        lines.Add(id + sep + "card" + sep + string.Join(sub, scored));
    }
}
lines.Add("curriculum" + sep + string.Join(",", Practice.CurriculumOrdered(Enumerable.Reverse(all)).Select(q => q.PackId + "/" + q.Id)));
lines.Add("balanced" + sep + string.Join(",", Practice.BalancedSelection(all, 100, q => q.GroupId, list => list).Select(q => q.Id)));
lines.Add("ordered" + sep + string.Join(",", Practice.InterviewOrderedSelection(all, 150, q => q.GroupId, shuffle: list => list).Select(q => q.Id)));
var pythonGroups = new[] { "python-engineering" };
var pythonPool = Practice.QuestionPool(bank, pythonGroups.ToHashSet());
lines.Add("orderedTopic" + sep + string.Join(",", Practice.InterviewOrderedSelection(pythonPool, 40, q => Practice.TopicKey(q, pythonGroups), shuffle: list => list).Select(q => q.Id)));

var latest = new Dictionary<string, PracticeScoredAnswer>();
var attempts = new Dictionary<string, List<PracticeScoredAnswer>>();
PracticeScoredAnswer Record(PracticeQuestion q, double score, PracticeRecallRating rating, DateTime due) => new()
{
    QuestionId = q.Id, PackId = q.PackId, Question = "Q", Answer = "A", UsedHelp = false, Mark = PracticeHelpMark.None, RawScore = score,
    FinalScore = score, Feedback = "", Strengths = Array.Empty<string>(), Gaps = Array.Empty<string>(), RecallRating = rating, NextReviewAt = due
};
for (var index = 0; index < all.Count; index++)
{
    var q = all[index];
    var key = Practice.RecallReviewKey(q.PackId, q.Id);
    switch (index % 4)
    {
        case 1:
        {
            var answer = Record(q, 0, PracticeRecallRating.Again, now.AddSeconds(-10 - index % 50));
            latest[key] = answer; attempts[key] = new() { answer };
            break;
        }
        case 2:
        {
            var answer = Record(q, 1, PracticeRecallRating.GotIt, now.AddSeconds(259_200 + index % 30));
            latest[key] = answer; attempts[key] = new() { answer, answer };
            break;
        }
        case 3:
        {
            var answer = Record(q, 0.5, PracticeRecallRating.Hard, now.AddSeconds(86_400));
            latest[key] = answer; attempts[key] = new() { answer };
            break;
        }
    }
}
foreach (var pack in bank.Packs)
{
    var plain = Practice.ContrastPairs(pack.Questions, now: now, limit: 40);
    lines.Add("contrast" + sep + pack.Id + sep + string.Join(",", plain.Select(pair => $"{pair.Topic.Id}={pair.First.Id}~{pair.Second.Id}")));
    var weighted = Practice.ContrastPairs(pack.Questions, latest, attempts, now, 40);
    lines.Add("contrastW" + sep + pack.Id + sep + string.Join(",", weighted.Select(pair => $"{pair.First.Id}~{pair.Second.Id}")));
}
foreach (var summary in Practice.TopicMasterySummaries(all, latest, attempts, now))
{
    lines.Add("mastery" + sep + $"{summary.Topic.Id}:{summary.Topic.Title}:{summary.NewCount}:{summary.LearningCount}:{summary.SolidCount}:{summary.DueCount}");
}
var plan = Practice.TodaysPlan(all, latest, attempts, now);
lines.Add("plan" + sep + $"{plan.DueCount}:{plan.LearningCount}:{plan.NewCount}:{plan.SolidCount}:{plan.RecommendedCount}:{plan.EstimatedMinutes}");
lines.Add("priority" + sep + string.Join(",", Practice.PrioritizedRecallQuestions(all, latest, now).Select(q => q.PackId + "/" + q.Id)));

var ratings = new[] { PracticeRecallRating.Again, PracticeRecallRating.Hard, PracticeRecallRating.GotIt };
var confidences = new PracticeRecallConfidence?[] { null, PracticeRecallConfidence.Unsure, PracticeRecallConfidence.Mostly, PracticeRecallConfidence.Very };
var targets = new double?[] { null, 300, 900, 172_800, 8_640_000, -86_400 };
foreach (var rating in ratings)
{
    foreach (var confidence in confidences)
    {
        for (var count = 0; count <= 8; count++)
        {
            var prior = Enumerable.Range(0, count).Select(j => new PracticeScoredAnswer
            {
                QuestionId = "p", PackId = "p", Question = "Q", Answer = "A", UsedHelp = j % 3 == 2, Mark = PracticeHelpMark.None, RawScore = 1,
                FinalScore = j % 2 == 0 ? 1.0 : 0.7, Feedback = "", Strengths = Array.Empty<string>(), Gaps = Array.Empty<string>(),
                RecallRating = PracticeRecallRating.GotIt, NextReviewAt = now
            }).ToList();
            var values = targets.Select(offset => F(Practice.AdaptiveReviewInterval(rating, prior, confidence, now, offset is { } seconds ? now.AddSeconds(seconds) : null), "F6"));
            lines.Add("adaptive" + sep + $"{rating.RawValue()}:{confidence?.RawValue() ?? "nil"}:{count}" + sep + string.Join(",", values));
        }
    }
}

Directory.CreateDirectory(target);
File.WriteAllText(Path.Combine(target, "dump.txt"), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
var store = new PracticeStore(Path.Combine(target, "store"));
Console.WriteLine($"write {store.Append(run)} {store.Append(run2)} {store.SaveInProgressSession(snapshot)} lines {lines.Count}");
