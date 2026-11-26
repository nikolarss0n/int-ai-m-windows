using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using static InterviewMaster.Tests.PracticeFixtures;

namespace InterviewMaster.Tests;

// Ports the bank half of `Tests/test_practice_bank.swift`: the shipped question files, loaded through
// the production loader. The files are the macOS ones, copied byte for byte.
public sealed class PracticeBankTests : SwiftStyleTests
{
    private static readonly string[] SpokenStarters = { "what ", "what's ", "whats ", "how ", "why ", "when ", "explain ", "describe " };

    private static readonly HashSet<string> ExpectedStudyBookTopicIDs = new(StringComparer.Ordinal)
    {
        "agentic-aws", "agentic-security", "agents", "api-engineering", "capital-markets",
        "cloud-infra", "data-ingestion", "data-quality", "data-science-stack",
        "deep-learning-frameworks", "design-patterns", "docker-ci", "drills", "dsa-sql",
        "evals", "evals-ci", "fastapi", "feature-engineering", "genai-challenges",
        "gradient-boosting", "graphrag", "java-collections", "java-concurrency", "java-core",
        "jd-gap", "langgraph", "llm-internals", "llmops", "markov-chains", "mcp",
        "ml-fundamentals", "mlops-tooling", "model-adaptation", "model-serving", "optimization",
        "pyspark-data", "python-concurrency", "python-idioms", "python-oop", "python-performance",
        "rag", "sa-craft", "security", "semantic-layer", "senior-python-interview", "spring",
        "transformers", "ways-of-working"
    };

    private static PracticeBankFile ViaLoad => Bank.Value;

    private static PracticeTopicPack Study => Practice.StudyBookPack(ViaLoad)!;

    private static List<string> AvailableGroups => Practice.BankGroups(ViaLoad).Select(group => group.Id).ToList();

    private static List<PracticeQuestion> PoolFor(PracticeRole role) =>
        Practice.QuestionPool(ViaLoad, Practice.GroupIDs(role, AvailableGroups).ToHashSet(StringComparer.Ordinal));

    private static List<PracticeQuestion> PoolFor(string groupId) =>
        Practice.QuestionPool(ViaLoad, new HashSet<string>(StringComparer.Ordinal) { groupId });

    private static List<PracticeQuestion> AIPool => PoolFor(PracticeRole.AIEngineer);

    private static bool HasHelp(PracticeQuestion question) => question.Answer.Trim().Length > 0 || question.Rubric.Trim().Length > 0;

    private static bool IsSpokenInterviewPrompt(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.EndsWith('…') || trimmed.EndsWith("...", StringComparison.Ordinal)) return false;
        if (trimmed.Contains('?')) return true;
        var lower = trimmed.ToLowerInvariant();
        return SpokenStarters.Any(starter => lower.StartsWith(starter, StringComparison.Ordinal));
    }

    private static List<(string Tab, string Question, string Answer)> SourceInterviewQuestions(string path)
    {
        var items = new List<(string, string, string)>();
        if (!File.Exists(path)) return items;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("tabs", out var tabs)) return items;
        foreach (var tab in tabs.EnumerateArray())
        {
            var name = tab.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            if (!tab.TryGetProperty("items", out var tabItems)) continue;
            foreach (var item in tabItems.EnumerateArray())
            {
                var question = (item.TryGetProperty("question", out var questionValue) ? questionValue.GetString() ?? "" : "").Trim();
                if (question.Length == 0) continue;
                var answer = item.TryGetProperty("answer", out var answerValue) ? answerValue.GetString() ?? "" : "";
                items.Add((name, question, answer));
            }
        }
        return items;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;

    private static bool LoadedTextCoversSource(string source, IEnumerable<string> loadedTexts)
    {
        var normalized = Practice.NormalizeQuestionText(source);
        var firstLine = Practice.NormalizeQuestionText(FirstLine(source));
        foreach (var text in loadedTexts)
        {
            var loaded = Practice.NormalizeQuestionText(text);
            if (loaded == normalized || loaded.Contains(firstLine, StringComparison.Ordinal) || normalized.Contains(loaded, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string HelpBody(string id)
    {
        var question = Study.Questions.FirstOrDefault(item => item.Id == id);
        if (question is null) return "";
        var answer = question.Answer.Trim();
        return answer.Length > 0 ? answer : question.Rubric.Trim();
    }

    private static string FirstParagraph(string text)
    {
        var trimmed = text.Trim();
        var split = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
        return split >= 0 ? trimmed[..split] : trimmed;
    }

    private static bool StartsWithKeyParagraph(string text, string key)
    {
        var first = FirstParagraph(text).Trim().Trim('.', '!', '?');
        var bareKey = key.Trim('.', '!', '?', ' ', '\t');
        return string.Equals(first, bareKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Precedes(List<long> left, List<long> right)
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            if (left[index] != right[index]) return left[index] < right[index];
        }
        return left.Count < right.Count;
    }

    [Fact]
    public void ShippedPracticeBankLoads()
    {
        var bankPath = Path.Combine(RepoPaths.PracticeDirectory, "bank.json");
        Check(File.Exists(bankPath), "Practice bank file exists at Resources/practice/bank.json");
        var loaded = PracticeBankLoader.LoadFile(bankPath);
        Check(loaded.Packs.Any(pack => pack.Id == "study-book"), "bank.json still decodes a study-book pack");
        Check(!ViaLoad.Packs.IsEmpty, "PracticeBankLoader.Load() finds the bank shipped beside the assembly");
        Check(ViaLoad.Packs.Any(pack => pack.Id == "study-book"), "Loaded bank includes the study-book pack, not only built-in extras");

        var fromRepo = PracticeBankLoader.Load(new[] { RepoPaths.PracticeDirectory });
        Check(fromRepo == ViaLoad, "The bank beside the assembly is the bank in the repository");
        Check(ViaLoad.Packs.Count == 18 && ViaLoad.Packs.Sum(pack => pack.Questions.Count) == 1671, "All 18 packs and 1,671 questions load");
        Check(ViaLoad.Positions.Count == 16, "All 16 positions load");

        var missing = PracticeBankLoader.Load(new[] { Path.Combine(RepoPaths.Root, "no-such-directory") });
        Check(missing.Packs.Select(pack => pack.Id).SequenceEqual(new[] { "aws", "models", "angular" }), "Without bank files the built-in packs are used");
        Check(missing.Positions.Count == 3 && missing.Positions.All(position => position.GroupIds.IsEmpty), "Built-in packs become positions covering every group");
    }

    [Fact]
    public void StudyBookPackAndTopicChips()
    {
        var study = Study;
        var builtinExtraIDs = new[] { "aws", "models", "angular" };
        var extraCount = ViaLoad.Packs.Where(pack => builtinExtraIDs.Contains(pack.Id)).Sum(pack => pack.Questions.Count);
        Check(study.Questions.Count == 102, $"Study-book pack still has 102 questions (got {study.Questions.Count})");
        Check(study.Questions.Count > extraCount, "Study-book pack is larger than AWS+Models+Angular combined");
        Check(ViaLoad.Packs.Any(pack => pack.Id == "interview-guide"), "Directory load also merges interview-guide.json");
        Check(study.Questions.All(HasHelp), "Every study-book question has a stored answer or rubric for Help");

        var studyTopicIDs = study.Questions.Select(question => question.TopicId).ToHashSet(StringComparer.Ordinal);
        Check(studyTopicIDs.SetEquals(ExpectedStudyBookTopicIDs), "Every study-book topicId from the bank still exists");
        var notSpoken = study.Questions.Where(question => !IsSpokenInterviewPrompt(question.Text)).Select(question => question.Id).ToList();
        Check(notSpoken.Count == 0, $"Study-book prompts are spoken interview questions, not quiz stems. Failed: {string.Join(", ", notSpoken)}");

        var chipTitles = Practice.BankGroups(ViaLoad).ToDictionary(group => group.Id, group => group.Title);
        Check(chipTitles.GetValueOrDefault("interview-prep") == "Whiteboard & DSA", "Interview-prep bucket is labeled as Whiteboard & DSA");
        Check(chipTitles.GetValueOrDefault("classical-ml") == "Tabular ML", "Classical ML is labeled as Tabular ML");
        Check(chipTitles.GetValueOrDefault("genai-and-llm") == "RAG & agents", "GenAI & LLM is labeled as RAG & agents");
        Check(chipTitles.GetValueOrDefault("ml-in-production") == "MLOps", "ML in production is labeled as MLOps");
        Check(chipTitles.GetValueOrDefault("foundations") == "LLM internals", "Foundations is labeled as LLM internals");
        Check(chipTitles.GetValueOrDefault("agentic-sme-s-and-p") == "Agents & security", "S&P SME bucket is labeled as Agents & security");
        Check(chipTitles.GetValueOrDefault("python-engineering") == "Python", "Python engineering is labeled Python");
        Check(chipTitles.Values.All(title => !title.ContainsIgnoringCase("interview prep")), "No chip is named Interview prep — the whole tab is interview prep");
        Check(study.Questions.All(question => chipTitles.ContainsKey(question.GroupId)), "Group ids are unchanged so role filters still work");

        Check(study.Questions.Count(question => question.Id.Contains("-quiz-", StringComparison.Ordinal)) == 88, "Coverage keeps 88 study-book quizzes");
        Check(study.Questions.Count(question => question.Id.Contains("-drill-", StringComparison.Ordinal)) == 14, "Coverage keeps 14 study-book drills");

        var autoregressive = HelpBody("llm-internals-quiz-0");
        var attention = HelpBody("transformers-quiz-0");
        var approval = HelpBody("agents-quiz-0");
        var crossEntropy = HelpBody("ml-fundamentals-quiz-0");
        var recallAtK = HelpBody("rag-quiz-0");
        var tdd = HelpBody("ways-of-working-quiz-0");
        Check(autoregressive.ContainsIgnoringCase("autoregressive"), "LLM Help still names autoregressive generation");
        Check(autoregressive.ContainsIgnoringCase("one token at a time"), "LLM Help still names one-token generation");
        Check(!StartsWithKeyParagraph(autoregressive, "One token at a time"), "LLM Help does not start with the leftover MCQ key paragraph");
        Check(attention.ContainsIgnoringCase("attention"), "Attention Help still names attention scores");
        Check(!StartsWithKeyParagraph(attention, "Attention scores"), "Attention Help does not start with the leftover MCQ key paragraph");
        Check(approval.ContainsIgnoringCase("approval"), "Agent Help still names human approval");
        Check(!StartsWithKeyParagraph(approval, "Require human approval"), "Agent Help does not start with the leftover MCQ key paragraph");
        Check(crossEntropy.ContainsIgnoringCase("cross-entropy"), "Loss Help still names cross-entropy");
        Check(!StartsWithKeyParagraph(crossEntropy, "Cross-entropy"), "Loss Help does not start with the leftover MCQ key paragraph");
        Check(recallAtK.Contains("Recall@k", StringComparison.Ordinal), "Retrieval Help still names Recall@k");
        Check(!StartsWithKeyParagraph(recallAtK, "Recall@k"), "Retrieval Help does not start with the leftover MCQ key paragraph");
        var leftoverKeyLeads = study.Questions.Where(question =>
        {
            var body = HelpBody(question.Id);
            var first = FirstParagraph(body);
            return body != first && first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 8;
        }).Select(question => question.Id).ToList();
        Check(leftoverKeyLeads.Count == 0, $"No study-book Help starts with a leftover MCQ key paragraph. Still: {string.Join(", ", leftoverKeyLeads)}");
        Check(tdd.Contains("Red → Green → Refactor", StringComparison.Ordinal), "TDD slogan stays an inline Red → Green → Refactor chain");
        Check(!tdd.Contains("Walk through it in this order", StringComparison.Ordinal), "TDD is not split into a fake drill list");
        Check(HelpBody("drills-drill-0").Contains("hybrid retrieve", StringComparison.Ordinal), "RAG drill Help keeps the original steps");

        var fullPool = Practice.QuestionPool(ViaLoad);
        Check(fullPool.Any(question => question.PackId == "study-book"), "Selectable pool includes study-book questions from the loaded file");
        Check(fullPool.Count == ViaLoad.Packs.Sum(pack => pack.Questions.Count), "Unfiltered pool is the merged loaded bank");
        var pythonPool = PoolFor("python-engineering");
        Check(pythonPool.Count > 0, "Role/topic filter still returns questions from the loaded bank");
        Check(pythonPool.All(question => question.GroupId == "python-engineering"), "Pool filter keeps only selected Python groups");
        Check(pythonPool.Any(question => question.PackId == "study-book"), "Python topic still includes study-book questions");
        Check(pythonPool.Any(question => question.Id == "ai-eng-video-6"), "Python topic includes the GIL interview question");
    }

    [Fact]
    public void InterviewGuidePackFromTheExportedQuestions()
    {
        var sourcePath = Path.Combine(RepoPaths.PracticeDirectory, "sources", "interview-questions-qa.json");
        var guidePath = Path.Combine(RepoPaths.PracticeDirectory, "interview-guide.json");
        Check(File.Exists(sourcePath), "Copied interview-questions-qa.json is next to the bank");
        Check(File.Exists(guidePath), "interview-guide.json is a top-level practice bank file");

        var guide = ViaLoad.Packs.FirstOrDefault(pack => pack.Id == "interview-guide");
        Assert.NotNull(guide);
        var sourceItems = SourceInterviewQuestions(sourcePath);
        Check(sourceItems.Count == 183, $"Source export has 183 questions (got {sourceItems.Count})");
        Check(guide.Questions.Count == sourceItems.Count, $"Imported pack keeps every source question ({guide.Questions.Count} vs {sourceItems.Count})");
        Check(guide.Groups.Select(group => group.Id).SequenceEqual(new[]
        {
            "jr-javascript", "mid-javascript", "senior-javascript", "typescript",
            "networking", "json", "qa", "oop", "coding-tasks", "logical-tasks", "devops"
        }), "Interview-guide groups follow the export tabs");

        var keepGuideTabs = new HashSet<string>(StringComparer.Ordinal)
        {
            "Jr Javascript", "Mid Javascript", "Senior Javascript", "TypeScript",
            "Networking", "JSON", "QA", "OOP"
        };
        var guideTexts = guide.Questions.Select(question => question.Text).ToList();
        var missingSpoken = sourceItems
            .Where(item => keepGuideTabs.Contains(item.Tab) && !LoadedTextCoversSource(item.Question, guideTexts))
            .Select(item => FirstLine(item.Question))
            .ToList();
        Check(missingSpoken.Count == 0, $"Guide items that already had real questions keep their wording. Missing: {string.Join(" | ", missingSpoken)}");

        var leftoverNotSpoken = guide.Questions
            .Where(question => question.GroupId is "coding-tasks" or "devops")
            .Where(question => !IsSpokenInterviewPrompt(question.Text))
            .Select(question => question.Id)
            .ToList();
        Check(leftoverNotSpoken.Count == 0, $"Leftover coding-task and DevOps titles are spoken interview prompts. Failed: {string.Join(", ", leftoverNotSpoken)}");
        Check(guide.Questions.Any(question => question.Id == "coding-tasks-2" && question.Text.Contains("sum of an array", StringComparison.Ordinal)), "Sum of array is now a spoken coding prompt");
        Check(!guide.Questions.Any(question => question.Text is "Sum of array" or "findDuplicates" or "ALGO EXPERT"), "Title-only coding names are gone");

        var jsQuestion = guide.Questions.FirstOrDefault(question => question.Text == "What is JavaScript?");
        Check(jsQuestion is not null, "Junior JS keeps 'What is JavaScript?' wording");
        Check(jsQuestion?.GroupId == "jr-javascript", "What is JavaScript? is in jr-javascript");
        var jsAnswer = jsQuestion?.Answer ?? "";
        Check(jsAnswer.Contains("JavaScript is a high-level, dynamic programming language", StringComparison.Ordinal), "JS answer keeps the original explanation");
        Check(!jsAnswer.ToLowerInvariant().Contains("core explanation:", StringComparison.Ordinal), "Help text drops the 'Core Explanation:' label only");

        var dataTypes = guide.Questions.FirstOrDefault(question => question.Text == "Explain Data Types.");
        Check(dataTypes is not null, "Heading-only prompts become 'Explain Data Types.' without rewriting the topic");
        Check((dataTypes?.Answer ?? "").Contains("Primitive Types", StringComparison.Ordinal), "Data Types answer stays the original body");

        var puzzle = guide.Questions.FirstOrDefault(question => question.Text.Contains("Missionaries and Cannibals", StringComparison.Ordinal));
        Check(puzzle is not null, "Logic puzzles are imported");
        Check(puzzle?.Text.Contains('\n') == true, "Logic puzzles keep the original title and problem on separate lines");
        Check(puzzle?.Text.Contains("Explain", StringComparison.Ordinal) == false, "Logic puzzles are not rewritten as Explain-prompts");

        var linux = guide.Questions.FirstOrDefault(question => question.Id == "devops-2");
        Check(linux is not null && IsSpokenInterviewPrompt(linux.Text), "DevOps Linux heading is a spoken interview prompt");
        Check((linux?.Answer ?? "").ContainsIgnoringCase("linux"), "DevOps Linux Help is no longer empty");

        var available = AvailableGroups;
        Check(Practice.GroupIDs(PracticeRole.Frontend, available).Contains("jr-javascript"), "Frontend role includes Jr Javascript");
        Check(Practice.GroupIDs(PracticeRole.Frontend, available).Contains("typescript"), "Frontend role includes TypeScript");
        Check(Practice.GroupIDs(PracticeRole.FullStack, available).Contains("devops"), "Full stack includes DevOps");
        Check(Practice.GroupIDs(PracticeRole.QAAutomation, available).Contains("qa"), "QA automation includes the QA tab");
        Check(Practice.GroupIDs(PracticeRole.QAAutomation, available).Contains("coding-tasks"), "QA automation includes coding tasks");
        Check(PoolFor(PracticeRole.Frontend).Any(question => question.Text == "What is JavaScript?"), "Frontend pool can draw the interview-guide JS questions");
        Check(
            Practice.QuestionSourceLine(
                Question("jr-javascript-0", "interview-guide", "What is JavaScript?", groupId: "jr-javascript", topicTitle: "1. Core JavaScript Fundamentals"),
                "Jr Javascript") == "Interview guide · Jr Javascript · 1. Core JavaScript Fundamentals",
            "Practice source line names the interview guide");
    }

    [Fact]
    public void PracticeModesAndTheAIEngineerRole()
    {
        Check(PracticeRunModes.AllCases.Select(mode => mode.RawValue()).SequenceEqual(new[] { "learn", "rehearse", "interview" }), "Practice exposes Active Recall, Voice Rehearsal, and Interview");
        Check(PracticeRunMode.Learn.Title() == "Active Recall", "The legacy Learn value is presented as Active Recall");
        Check(PracticeRunMode.Rehearse.Title() == "Voice Rehearsal", "Practice exposes the voice-first rehearsal rung");
        Check(Practice.HelpAllowed(PracticeRunMode.Learn), "Active Recall may reveal prepared learning help");
        Check(Practice.HelpAllowed(PracticeRunMode.Rehearse), "Voice Rehearsal may reveal prepared learning help");
        Check(!Practice.HelpAllowed(PracticeRunMode.Interview), "Interview mode reports Help disabled");
        Check(!Practice.ShowsMultipleChoice(PracticeRunMode.Learn), "Active Recall uses a typed response instead of multiple choice");
        Check(!Practice.ShowsMultipleChoice(PracticeRunMode.Rehearse), "Voice Rehearsal uses a spoken free response");
        Check(!Practice.ShowsMultipleChoice(PracticeRunMode.Interview), "Interview does not attach options");
        Check(Practice.ShowsSubmitButton(PracticeRunMode.Learn), "Active Recall has a reveal action after the typed response");
        Check(Practice.ShowsSubmitButton(PracticeRunMode.Interview), "Interview still submits a free-response answer");
        Check(Practice.RunMode("interview") == PracticeRunMode.Interview && Practice.RunMode("nonsense") == PracticeRunMode.Learn, "Unknown mode ids fall back to Active Recall");

        var aiGroups = Practice.GroupIDs(PracticeRole.AIEngineer, AvailableGroups);
        Check(!aiGroups.Contains("typescript"), "AI Engineer does not include TypeScript");
        Check(!aiGroups.Contains("jr-javascript"), "AI Engineer does not include junior JavaScript");
        Check(!aiGroups.Contains("oop"), "AI Engineer does not include the JS/Java OOP pack");
        var aiPool = AIPool;
        Check(aiPool.Count > 0, "AI Engineer still has a question pool");
        Check(aiPool.All(question => question.GroupId is not ("typescript" or "jr-javascript" or "mid-javascript" or "senior-javascript" or "oop")), "AI Engineer questions are not TypeScript/JS/OOP-pack items");
        var aiOrdered = Practice.InterviewOrderedSelection(aiPool, Math.Min(10, aiPool.Count), question => question.GroupId, shuffle: list => list);
        Check(aiOrdered.FirstOrDefault()?.GroupId == "python-engineering", "AI Engineer runs start with Python, not TypeScript");
    }

    [Fact]
    public void AuthoredCardDecks()
    {
        var claudePack = ViaLoad.Packs.FirstOrDefault(pack => pack.Id == "claude-architect");
        Assert.NotNull(claudePack);
        var availableGroups = AvailableGroups;
        var aiPool = AIPool;
        var claudeGroups = Practice.GroupIDs(PracticeRole.ClaudeArchitect, availableGroups).ToHashSet(StringComparer.Ordinal);
        var claudePool = Practice.QuestionPool(ViaLoad, claudeGroups);
        Check(claudePool.Count >= 120 && claudeGroups.Count == 5, "Claude-only selection contains at least the original 120 cards across five domains");
        Check(claudePool.All(question => question.PackId == "claude-architect" && question.Card?.IsValid == true), "Claude role never mixes in other packs or malformed cards");
        Check(!aiPool.Any(question => question.PackId == "claude-architect"), "General AI practice remains separate from Claude cards");
        var lessonCounts = claudePool.GroupBy(question => question.TopicId).ToList();
        Check(lessonCounts.Count == 30 && lessonCounts.All(lesson => lesson.Count() >= 4), "All 30 Claude lessons retain at least four cards");
        Check(claudePool.Select(question => question.Id).Distinct().Count() == claudePool.Count, "Claude card IDs are unique");
        var curriculum = Practice.CurriculumOrdered(Enumerable.Reverse(claudePool));
        Check(curriculum.Count == claudePool.Count && curriculum[0].TopicId == "claude-lesson-1-1" && curriculum[^1].TopicId == "claude-lesson-5-6", "A full Claude run walks the deck lesson by lesson");
        var curriculumKeys = curriculum.Select(Practice.CurriculumOrderKey).ToList();
        Check(curriculumKeys.Zip(curriculumKeys.Skip(1)).All(pair => !Precedes(pair.Second, pair.First)), "Curriculum order never goes backwards");
        var nine = curriculum.FindIndex(question => question.Id == "claude-3-1-9");
        var ten = curriculum.FindIndex(question => question.Id == "claude-3-1-10");
        Check(nine >= 0 && ten >= 0, "Lesson 3-1 has cards 9 and 10");
        Check(nine < ten, "Card numbers sort numerically, so 10 follows 9");
        Check(claudePool.All(question => question.Card!.Options.Select(option => option.Text).Distinct().Count() == 4), "Every Claude card has four distinct option texts");

        var decks = new[]
        {
            (Role: PracticeRole.AIEngineerCards, Pack: "ai-engineer-cards"),
            (Role: PracticeRole.MLCoreCards, Pack: "ml-core-cards"),
            (Role: PracticeRole.TypeScriptCards, Pack: "typescript-cards")
        };
        foreach (var (role, expectedPack) in decks)
        {
            var pack = ViaLoad.Packs.FirstOrDefault(item => item.Id == expectedPack);
            Assert.NotNull(pack);
            Check(role.CardPackID == expectedPack && PracticeRole.CardRole(expectedPack) == role, $"{expectedPack} is wired to its card role");
            var groups = Practice.GroupIDs(role, availableGroups).ToHashSet(StringComparer.Ordinal);
            Check(groups.Count > 0 && groups.SetEquals(pack.Groups.Select(group => group.Id)), $"{expectedPack} role exposes exactly the deck's groups");
            var pool = Practice.QuestionPool(ViaLoad, groups);
            Check(pool.Count > 0 && pool.All(question => question.PackId == expectedPack && question.Card?.IsValid == true), $"{expectedPack} pool is card-only and valid");
            Check(pool.All(question => question.Card!.Options.Select(option => option.Text).Distinct().Count() == 4), $"{expectedPack} cards have four distinct option texts");
            Check(pool.Select(question => question.Id).Distinct().Count() == pool.Count, $"{expectedPack} card IDs are unique");
            Check(pack.Questions.All(question => groups.Contains(question.GroupId)), $"{expectedPack} questions all belong to a declared group");
        }
        Check(!aiPool.Any(question => question.Card is not null), "Free-response AI Engineer practice never pulls in card decks");
        Check(!PoolFor(PracticeRole.Frontend).Any(question => question.Card is not null), "Frontend free-response practice never pulls in the TypeScript card deck");
        Check(
            PracticeRole.All.Where(role => role.CardPackID is null).All(role => !role.GroupIds.Any(id => id.StartsWith("cards-", StringComparison.Ordinal) || id.StartsWith("claude-", StringComparison.Ordinal))),
            "Free-response roles never list card groups");

        var firstClaude = claudePack.Questions[0];
        var firstCard = firstClaude.Card!;
        var correctID = firstCard.CorrectOptionID;
        var wrongID = firstCard.Options.First(option => option.Id != correctID).Id;
        Check(Practice.ScoreCard(firstClaude, correctID)?.FinalScore == 1, "Authored correct option scores one without a model call");
        Check(Practice.ScoreCard(firstClaude, wrongID)?.FinalScore == 0, "A valid distractor scores zero");
        Check(Practice.ScoreCard(firstClaude, "missing") is null, "Unknown option IDs cannot be scored");
        Check(Practice.ScoreCard(Study.Questions[0], correctID) is null, "Free-response questions cannot silently become multiple choice");
        Check(Practice.CardFeedback(firstClaude, null, PracticeRunMode.Learn) is null, "Study does not reveal before selection");
        Check(Practice.CardFeedback(firstClaude, correctID, PracticeRunMode.Interview) is null, "Interview withholds even a correct selection's answer and explanation");
        Check(Practice.CardFeedback(firstClaude, wrongID, PracticeRunMode.Interview) is null, "Interview withholds a wrong selection's correctness too");
        Check(Practice.CardFeedback(firstClaude, wrongID, PracticeRunMode.Learn)?.CorrectOptionID == correctID, "Study reveals the authored answer after a choice");

        var reorderedClaude = firstClaude with { Card = firstCard with { Options = firstCard.Options.Reverse().ToArray() } };
        Check(Practice.ScoreCard(reorderedClaude, correctID)?.FinalScore == 1, "Changing display order cannot change the answer key");
        var cardState = new PracticeSessionQuestionState(new PracticeQuestionReference(firstClaude.PackId, firstClaude.Id))
        {
            DraftResponse = wrongID,
            CardQuestion = reorderedClaude
        };
        var restored = JsonSerializer.Deserialize<PracticeSessionQuestionState>(JsonSerializer.SerializeToUtf8Bytes(cardState, PracticeJson.Options), PracticeJson.Options);
        Check(restored is not null && Practice.RestoreCardQuestion(restored) == reorderedClaude, "Resume preserves the exact wording, key and shuffled option order");
        Check(restored?.DraftResponse == wrongID, "Resume preserves the selected stable option ID");
        Check(Practice.RestoreCardQuestion(cardState with { Reference = new PracticeQuestionReference("other", firstClaude.Id) }) is null, "Resume rejects a card that mismatches its saved pack");
        Check(Practice.RestoreCardQuestion(cardState with { DraftResponse = "not-in-card" }) is null, "Resume rejects an unknown saved selection");

        var originalObject = JsonSerializer.SerializeToNode(firstClaude, PracticeJson.Options)!.AsObject();
        Check(originalObject["card"]?["correctOptionID"]?.GetValue<string>() == correctID && originalObject["card"]?["sourceURL"] is not null, "Cards are written with the macOS key names");
        foreach (var mutation in new[] { "duplicate", "missingKey", "tooFew" })
        {
            var mutated = originalObject.DeepClone().AsObject();
            var card = mutated["card"]!.AsObject();
            var options = card["options"]!.AsArray();
            if (mutation == "duplicate") options[1] = options[0]!.DeepClone();
            if (mutation == "tooFew") options.RemoveAt(options.Count - 1);
            if (mutation == "missingKey") card["correctOptionID"] = "not-an-option";
            var rejected = false;
            try
            {
                JsonSerializer.Deserialize<PracticeQuestion>(mutated, PracticeJson.Options);
            }
            catch (JsonException)
            {
                rejected = true;
            }
            Check(rejected, $"Malformed card rejected: {mutation}");
        }

        var record = Practice.ScoreCard(firstClaude, wrongID);
        var decoded = record is null
            ? null
            : JsonSerializer.Deserialize<PracticeScoredAnswer>(JsonSerializer.SerializeToUtf8Bytes(record, PracticeJson.Options), PracticeJson.Options);
        Check(record is not null && decoded == record && decoded?.CardResult?.CorrectOptionID == correctID, "Completed history preserves selection, authored answer and explanation");
    }

    [Fact]
    public void AIEngineerInterviewPacks()
    {
        var aiPool = AIPool;
        var videoPack = ViaLoad.Packs.FirstOrDefault(pack => pack.Id == "ai-engineer-interview");
        Assert.NotNull(videoPack);
        Check(videoPack.Questions.Count == 11, $"Video pack has all 11 questions (got {videoPack.Questions.Count})");
        var gil = videoPack.Questions.FirstOrDefault(question => question.Id == "ai-eng-video-6");
        Check(gil?.Text.Contains("Global Interpreter Lock", StringComparison.Ordinal) == true, "GIL is a spoken Practice question");
        Check(gil?.GroupId == "python-engineering", "GIL sits in the Python topic");
        Check(gil?.Answer.ContainsIgnoringCase("gil") == true, "GIL Help names the lock");
        Check(aiPool.Any(question => question.Id == "ai-eng-video-6"), "AI Engineer pool includes the GIL question");
        Check(aiPool.Any(question => question.Text.Contains("RLHF", StringComparison.Ordinal)), "AI Engineer pool includes RLHF");
        Check(aiPool.Any(question => question.Text.Contains("Generative AI and traditional programming", StringComparison.Ordinal)), "AI Engineer pool includes GenAI vs traditional programming");
        Check(
            Practice.QuestionSourceLine(
                Question("ai-eng-video-6", "ai-engineer-interview", "Explain concurrency, parallelism, and the Global Interpreter Lock (GIL) in Python.", groupId: "python-engineering", topicTitle: "Python concurrency & the GIL"),
                "Python") == "AI engineer interview · Python · Python concurrency & the GIL",
            "GIL source line names the video pack");

        var prepPack = ViaLoad.Packs.FirstOrDefault(pack => pack.Id == "ai-engineer-prep");
        Assert.NotNull(prepPack);
        Check(prepPack.Questions.Count == 5, $"Prep pack has 5 questions (got {prepPack.Questions.Count})");
        Check(prepPack.Questions.Any(question => question.Text.Contains("three software archetypes", StringComparison.Ordinal)), "Prep pack covers the three AI Engineer role types");
        Check(aiPool.Any(question => question.Id == "ai-eng-prep-1"), "AI Engineer pool includes the three-role question");
        Check(aiPool.Any(question => question.Id == "ai-eng-prep-2"), "AI Engineer pool includes the OSINT prep question");
        Check(aiPool.Any(question => question.Text.Contains("voice-based AI mock interviews", StringComparison.Ordinal)), "AI Engineer pool includes voice mock interviews");
        var rolesHelp = prepPack.Questions.FirstOrDefault(question => question.Id == "ai-eng-prep-1")?.Answer ?? "";
        Check(rolesHelp.Contains("LangGraph", StringComparison.Ordinal) && rolesHelp.Contains("PyTorch", StringComparison.Ordinal), "Role-types Help keeps the video stacks");
        Check(
            Practice.QuestionSourceLine(
                Question("ai-eng-prep-1", "ai-engineer-prep", "The title AI Engineer on a job board is generic. What three software archetypes usually sit behind it?", groupId: "genai-and-llm", topicTitle: "Types of AI Engineer roles"),
                "RAG & agents") == "AI interview prep · RAG & agents · Types of AI Engineer roles",
            "Prep source line names the cracking-the-interview pack");

        var interviewerPack = ViaLoad.Packs.FirstOrDefault(pack => pack.Id == "ai-engineer-interviewer");
        Assert.NotNull(interviewerPack);
        Check(interviewerPack.Questions.Count == 12, $"Interviewer pack has 12 questions (got {interviewerPack.Questions.Count})");
        Check(interviewerPack.Questions.Any(question => question.Text == "How do LLMs work?"), "Pack includes How do LLMs work?");
        Check(interviewerPack.Questions.Any(question => question.Text == "How do Transformers work?"), "Pack includes How do Transformers work?");
        Check(aiPool.Any(question => question.Id == "ai-eng-iv-7"), "AI Engineer pool includes the interviewer GIL question");
        Check(aiPool.Any(question => question.Text.Contains("AI engineering trilemma", StringComparison.Ordinal)), "AI Engineer pool includes the latency/cost/relevancy trilemma");
        var hyde = interviewerPack.Questions.FirstOrDefault(question => question.Id == "ai-eng-iv-10")?.Answer ?? "";
        Check(hyde.Contains("HyDE", StringComparison.Ordinal), "Embedding Help keeps HyDE from the video");
        Check(
            Practice.QuestionSourceLine(
                Question("ai-eng-iv-1", "ai-engineer-interviewer", "How do LLMs work?", groupId: "foundations", topicTitle: "LLM & transformer fundamentals"),
                "LLM internals") == "AI interviewer view · LLM internals · LLM & transformer fundamentals",
            "Interviewer source line names the pack");
    }

    [Fact]
    public void ResearchedInterviewPools()
    {
        var availableGroups = AvailableGroups;
        var aiGroups = Practice.GroupIDs(PracticeRole.AIEngineer, availableGroups);
        var aiPool = AIPool;
        var pythonPool = PoolFor("python-engineering");
        var typeScriptPool = PoolFor("typescript");
        Check(pythonPool.Count >= 100, $"Python pool has at least 100 questions (got {pythonPool.Count})");
        Check(typeScriptPool.Count >= 100, $"TypeScript pool has at least 100 questions (got {typeScriptPool.Count})");
        Check(pythonPool.All(HasHelp), "Every Python question has Help");
        Check(typeScriptPool.All(HasHelp), "Every TypeScript question has Help");
        var gilFact = pythonPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("global interpreter lock"));
        Check(gilFact is not null, "Python pool includes a GIL question");
        Check((gilFact?.Answer ?? "").ContainsIgnoringCase("gil"), "GIL Help still names the lock");
        var listTuple = pythonPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("list") && question.Text.ContainsIgnoringCase("tuple"));
        Check(listTuple is not null, "Python pool includes list vs tuple");
        Check((listTuple?.Answer ?? "").ContainsIgnoringCase("mutable"), "List-vs-tuple Help names mutability");
        var interfaceQuestion = typeScriptPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("interface") && question.Text.ContainsIgnoringCase("type"));
        Check(interfaceQuestion is not null, "TypeScript pool includes interface vs type");
        Check((interfaceQuestion?.Answer ?? "").ContainsIgnoringCase("interface"), "interface-vs-type Help keeps the fact");
        var generics = typeScriptPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("generic"));
        Check(generics is not null, "TypeScript pool includes generics");
        Check((generics?.Answer ?? "").ContainsIgnoringCase("type"), "Generics Help is non-empty and typed");
        Check(!aiGroups.Contains("typescript"), "AI Engineer still excludes TypeScript");
        Check(Practice.GroupIDs(PracticeRole.Frontend, availableGroups).Contains("typescript"), "Frontend still includes TypeScript");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableGroups).Contains("python-engineering"), "Full stack still includes Python");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableGroups).Contains("typescript"), "Full stack still includes TypeScript");
        Check(pythonPool.Any(question => question.PackId == "python-interview"), "Merged load includes the researched Python pack");
        Check(typeScriptPool.Any(question => question.PackId == "typescript-interview"), "Merged load includes the researched TypeScript pack");
        var python = aiPool.FirstOrDefault(question => question.Id == "python-oop-quiz-0");
        Check(python is not null, "AI Engineer pool still has python-oop-quiz-0");
        if (python is not null)
        {
            var pythonChoices = Practice.LearnChoices(python, aiPool, list => list);
            Check(pythonChoices.Count == 4, "Python Learn options still have four choices from the AI pool");
            Check(pythonChoices.All(choice => !choice.Text.ContainsIgnoringCase("typescript")), "Python Learn distractors are not TypeScript stems");
        }

        var oopPool = PoolFor("oop");
        var llmPool = PoolFor("genai-and-llm");
        var awsPool = PoolFor("aws");
        var devopsPool = PoolFor("devops");
        Check(oopPool.Count >= 100, $"OOP pool has at least 100 questions (got {oopPool.Count})");
        Check(llmPool.Count >= 100, $"LLM/embeddings pool has at least 100 questions (got {llmPool.Count})");
        Check(awsPool.Count >= 100, $"AWS pool has at least 100 questions (got {awsPool.Count})");
        Check(devopsPool.Count >= 100, $"DevOps pool has at least 100 questions (got {devopsPool.Count})");
        Check(oopPool.All(HasHelp), "Every OOP question has Help");
        Check(llmPool.All(HasHelp), "Every LLM question has Help");
        Check(awsPool.All(HasHelp), "Every AWS question has Help");
        Check(devopsPool.All(HasHelp), "Every DevOps question has Help");
        var solid = oopPool.FirstOrDefault(question => question.PackId == "oop-interview" && question.Text.ContainsIgnoringCase("single responsibility"));
        Check(solid is not null, "OOP pool includes Single Responsibility");
        Check((solid?.Answer ?? "").ContainsIgnoringCase("one reason"), "SRP Help names one reason to change");
        var embedding = llmPool.FirstOrDefault(question => question.PackId == "llm-interview" && question.Text.ContainsIgnoringCase("what is a text embedding"));
        Check(embedding is not null, "LLM pool includes embeddings");
        Check((embedding?.Answer ?? "").ContainsIgnoringCase("vector"), "Embedding Help names vectors");
        var pinecone = llmPool.FirstOrDefault(question => question.PackId == "llm-interview" && question.Text.ContainsIgnoringCase("pinecone"));
        Check(pinecone is not null, "LLM pool includes a hosted vector DB (Pinecone)");
        Check((pinecone?.Answer ?? "").ContainsIgnoringCase("vector"), "Pinecone Help stays on vector search");
        var iam = awsPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("iam") && question.Text.ContainsIgnoringCase("user") && question.Text.ContainsIgnoringCase("role"));
        Check(iam is not null, "AWS pool includes IAM user vs role");
        Check((iam?.Answer ?? "").ContainsIgnoringCase("role"), "IAM Help names roles");
        var cicd = devopsPool.FirstOrDefault(question => question.Text.ContainsIgnoringCase("ci") && question.Text.ContainsIgnoringCase("cd"));
        Check(cicd is not null, "DevOps pool includes CI vs CD");
        Check((cicd?.Answer ?? "").ContainsIgnoringCase("continuous"), "CI/CD Help names continuous delivery");
        Check(oopPool.Any(question => question.PackId == "oop-interview"), "Merged load includes the researched OOP pack");
        Check(llmPool.Any(question => question.PackId == "llm-interview"), "Merged load includes the researched LLM pack");
        Check(awsPool.Any(question => question.PackId == "aws-interview"), "Merged load includes the researched AWS pack");
        Check(devopsPool.Any(question => question.PackId == "devops-interview"), "Merged load includes the researched DevOps pack");
        var researchedPackIDs = new[] { "oop-interview", "llm-interview", "aws-interview", "devops-interview" };
        var notSpokenNew = ViaLoad.Packs.SelectMany(pack => pack.Questions)
            .Where(question => researchedPackIDs.Contains(question.PackId) && !IsSpokenInterviewPrompt(question.Text))
            .Select(question => question.Id)
            .ToList();
        Check(notSpokenNew.Count == 0, $"New OOP/LLM/AWS/DevOps prompts are spoken interview questions. Failed: {string.Join(", ", notSpokenNew)}");
        Check(Practice.GroupIDs(PracticeRole.Frontend, availableGroups).Contains("oop"), "Frontend still includes OOP");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableGroups).Contains("oop"), "Full stack includes OOP");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableGroups).Contains("aws"), "Full stack includes AWS");
        Check(Practice.GroupIDs(PracticeRole.FullStack, availableGroups).Contains("devops"), "Full stack includes DevOps");
        Check(aiGroups.Contains("genai-and-llm"), "AI Engineer includes RAG & agents");
        Check(aiGroups.Contains("aws"), "AI Engineer includes AWS");
        Check(aiGroups.Contains("devops"), "AI Engineer includes DevOps");
        Check(!aiGroups.Contains("oop"), "AI Engineer still excludes the JS/Java OOP pack");
        Check(aiPool.Any(question => question.PackId == "llm-interview"), "AI Engineer pool includes the LLM interview pack");
        Check(aiPool.Any(question => question.PackId == "aws-interview"), "AI Engineer pool includes the AWS interview pack");
        Check(aiPool.Any(question => question.PackId == "devops-interview"), "AI Engineer pool includes the DevOps interview pack");
        Check(!aiPool.Any(question => question.PackId == "oop-interview"), "AI Engineer pool does not include the OOP interview pack");
        if (solid is not null)
        {
            Check(Practice.QuestionSourceLine(solid, "OOP").StartsWith("OOP interview", StringComparison.Ordinal), "OOP source line names the OOP interview pack");
        }
        var vector = llmPool.FirstOrDefault(question => question.PackId == "llm-interview");
        if (vector is not null)
        {
            Check(Practice.QuestionSourceLine(vector, "RAG & agents").StartsWith("LLM interview", StringComparison.Ordinal), "LLM source line names the LLM interview pack");
        }
        var awsQuestion = awsPool.FirstOrDefault(question => question.PackId == "aws-interview");
        if (awsQuestion is not null)
        {
            Check(Practice.QuestionSourceLine(awsQuestion, "AWS").StartsWith("AWS interview", StringComparison.Ordinal), "AWS source line names the AWS interview pack");
        }
        var devopsQuestion = devopsPool.FirstOrDefault(question => question.PackId == "devops-interview");
        if (devopsQuestion is not null)
        {
            Check(Practice.QuestionSourceLine(devopsQuestion, "DevOps").StartsWith("DevOps interview", StringComparison.Ordinal), "DevOps source line names the DevOps interview pack");
        }
    }

    [Fact]
    public void LearnChoicesAndRevealedHelp()
    {
        var bankQuestions = ViaLoad.Packs.SelectMany(pack => pack.Questions).ToList();
        var llm = bankQuestions.FirstOrDefault(question => question.Id == "llm-internals-quiz-0");
        Assert.NotNull(llm);
        var choices = Practice.LearnChoices(llm, bankQuestions, list => list);
        Check(choices.Count == 4, $"Learn builds four options (got {choices.Count})");
        Check(choices.Select(choice => choice.Text).Distinct().Count() == 4, "Learn options are four distinct strings");
        Check(choices.Count(choice => choice.IsCorrect) == 1, "Exactly one option is the stored-answer stem");
        Check(choices.Count > 0 && choices[0].IsCorrect, "Unshuffled builder places the correct stem first");
        Check(choices.Count > 0 && choices[0].Text == Practice.AnswerStem(llm), "Correct option is the stem from the stored answer");
        Check(choices.Count > 0 && choices[0].Text.Length > 0, "Correct stem is non-empty");

        Check(Practice.RevealedHelp(PracticeRunMode.Learn, false, llm) is null, "Learn does not reveal Help as part of the option list");
        var revealed = Practice.RevealedHelp(PracticeRunMode.Learn, true, llm);
        Check(revealed is not null, "Learn reveals Help after a selection");
        Check(revealed == Practice.HelpText(llm), "Post-choice Help is the full stored bank answer");
        Check(revealed?.ContainsIgnoringCase("autoregressive") == true, "Full Help still names autoregressive generation");
        Check(choices.Where(choice => !choice.IsCorrect).All(choice => choice.Text != revealed), "Distractors are bank near-misses, not the full Help");
        Check(choices.Where(choice => choice.Text.ContainsIgnoringCase("autoregressive")).All(choice => choice.IsCorrect), "The sample fact is not used as a distractor");
        Check(Practice.RevealedHelp(PracticeRunMode.Interview, true, llm) is null, "Interview never reveals Help");

        var rag = bankQuestions.FirstOrDefault(question => question.Id == "rag-quiz-0");
        Assert.NotNull(rag);
        Check(Practice.LearnChoices(rag, bankQuestions, list => list).Count == 4, "RAG question also gets four Learn options");
        Check(Practice.RevealedHelp(PracticeRunMode.Learn, true, rag)?.Contains("Recall@k", StringComparison.Ordinal) == true, "RAG Help after a choice still names Recall@k");
        Check(Practice.RevealedHelp(PracticeRunMode.Learn, false, rag) is null, "RAG options are shown without Help");

        var headingQuestion = Question(
            "heading-closures", "interview-guide", "Explain Closures.", groupId: "mid-javascript",
            answer: "Core Concept:\nClosures are functions that maintain access to their outer scope's variables.");
        var headingStem = Practice.AnswerStem(headingQuestion);
        Check(headingStem.ContainsIgnoringCase("closures"), "Heading-prefixed answers yield the closures fact, not the label");
        Check(headingStem != "Core Concept:", "Correct stem is not the 'Core Concept:' heading");
        var headingBank = new[]
        {
            headingQuestion,
            Question("h1", "interview-guide", "Q1", groupId: "mid-javascript", answer: "An IIFE runs as soon as it is defined."),
            Question("h2", "interview-guide", "Q2", groupId: "mid-javascript", answer: "The event loop drains the microtask queue before macrotasks."),
            Question("h3", "interview-guide", "Q3", groupId: "mid-javascript", answer: "Debouncing waits for a pause before running the handler.")
        };
        var headingChoices = Practice.LearnChoices(headingQuestion, headingBank, list => list);
        var headingCorrect = headingChoices.FirstOrDefault(choice => choice.IsCorrect);
        Check(headingChoices.Count == 4, "Heading-prefixed Learn item still gets four options");
        Check(headingCorrect?.Text.ContainsIgnoringCase("closures") == true, "Correct option contains the closures fact");
        Check(headingCorrect?.Text != "Core Concept:", "Correct option is not 'Core Concept:'");
        var shuffled = Practice.LearnChoices(headingQuestion, headingBank);
        Check(shuffled.Count == 4 && shuffled.Count(choice => choice.IsCorrect) == 1, "The default shuffle keeps all four options and one answer");

        var closures = bankQuestions.FirstOrDefault(question => question.Id == "mid-javascript-0");
        Assert.NotNull(closures);
        var shippedStem = Practice.AnswerStem(closures);
        Check(shippedStem.ContainsIgnoringCase("closures"), "Shipped Explain Closures. stem is the fact");
        Check(shippedStem != "Core Concept:", "Shipped stem skips the Core Concept heading");
    }
}
