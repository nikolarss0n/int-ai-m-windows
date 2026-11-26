using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

// The first half of `Domain/Practice/PracticeLogic.swift`: the question bank, roles, selection and
// Active Recall review. `PracticeSessions.cs` holds the second half: scored answers, runs and sessions.
// Free functions `practiceXyz(...)` are `Practice.Xyz(...)` here.

/// JSON settings shared by the bank loader and the practice store. Keys match the macOS files.
public static class PracticeJson
{
    public static readonly JsonSerializerOptions Options = AppJson.Compact;

    /// What the store writes: indented, dates as whole-second UTC ISO 8601.
    public static readonly JsonSerializerOptions StoreOptions = AppJson.Indented;
}

public enum PracticeHelpMark
{
    None,
    Yellow
}

public static class PracticeScoring
{
    /// Scored interview answers that explicitly use prepared Help are worth 0.4×.
    /// Active Recall repairs are tracked as assisted learning, not grade penalties.
    public const double HelpMultiplier = 0.4;
}

public sealed record PracticeScoreResult(double RawScore, bool UsedHelp, PracticeHelpMark Mark, double FinalScore);

public sealed record PracticeCardOption
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string Rationale { get; init; }
}

public sealed record PracticeCardContent
{
    public required ValueList<PracticeCardOption> Options { get; init; }
    public required string CorrectOptionID { get; init; }
    public required string Explanation { get; init; }
    public required string SourceURL { get; init; }
    public required int ContentVersion { get; init; }

    [JsonIgnore]
    public bool IsValid =>
        Options.Count == 4
        && Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() == 4
        && Options.Select(option => option.Text.Trim().Lowercased()).Distinct(StringComparer.Ordinal).Count() == 4
        && Options.All(option => option.Id.Length > 0 && option.Text.Trim().Length > 0 && option.Rationale.Length > 0)
        && Options.Any(option => option.Id == CorrectOptionID)
        && Explanation.Length > 0 && ContentVersion > 0;
}

public sealed record PracticeCardResult
{
    public required string SelectedOptionID { get; init; }
    public required string CorrectOptionID { get; init; }
    public required string CorrectAnswer { get; init; }
    public required string Explanation { get; init; }
    public required string Topic { get; init; }
    public required int ContentVersion { get; init; }
}

public sealed record PracticeQuestion : IJsonOnDeserialized
{
    public required string Id { get; init; }
    public required string PackId { get; init; }
    public required string Text { get; init; }
    public string GroupId { get; init; } = "";
    public string TopicId { get; init; } = "";
    public string TopicTitle { get; init; } = "";
    public ValueList<string> Hints { get; init; } = ValueList<string>.Empty;
    public string Rubric { get; init; } = "";
    public string Answer { get; init; } = "";
    public PracticeCardContent? Card { get; init; }

    public void OnDeserialized()
    {
        if (Card is { IsValid: false })
        {
            throw new JsonException("A practice card needs four distinct options and one valid answer key.");
        }
    }
}

public sealed record PracticeGroup
{
    public required string Id { get; init; }
    public required string Title { get; init; }
}

public sealed record PracticePosition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string PackId { get; init; }

    /// Empty means every group in the pack.
    public required ValueList<string> GroupIds { get; init; }
}

public sealed record PracticeBankFile
{
    public required ValueList<PracticeTopicPack> Packs { get; init; }
    public ValueList<PracticePosition> Positions { get; init; } = ValueList<PracticePosition>.Empty;
}

public sealed record PracticeTopicPack
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Blurb { get; init; }
    public required ValueList<PracticeGroup> Groups { get; init; }
    public required ValueList<PracticeQuestion> Questions { get; init; }

    private static PracticeQuestion Question(string id, string packId, string text) => new() { Id = id, PackId = packId, Text = text };

    public static readonly PracticeTopicPack Aws = new()
    {
        Id = "aws",
        Title = "AWS",
        Blurb = "IAM, networking, storage, and compute trade-offs",
        Groups = new[] { new PracticeGroup { Id = "aws", Title = "AWS" } },
        Questions = new[]
        {
            Question("aws-iam", "aws", "What is the difference between an IAM user and an IAM role?"),
            Question("aws-s3", "aws", "When would you choose S3 Intelligent-Tiering over S3 Standard?"),
            Question("aws-sqs-sns", "aws", "How does SQS differ from SNS, and when would you use both?"),
            Question("aws-vpc", "aws", "How does VPC peering differ from Transit Gateway?"),
            Question("aws-lambda-ecs", "aws", "When would you run a workload on Lambda instead of ECS?"),
            Question("aws-dynamo", "aws", "Explain eventual consistency in DynamoDB and when it is acceptable."),
            Question("aws-multi-az", "aws", "How would you design a multi-AZ architecture for a public API on AWS?")
        }
    };

    public static readonly PracticeTopicPack Models = new()
    {
        Id = "models",
        Title = "Models",
        Blurb = "Transformers, sampling, RAG, and serving trade-offs",
        Groups = new[] { new PracticeGroup { Id = "models", Title = "Models" } },
        Questions = new[]
        {
            Question("models-encoder-decoder", "models", "What is the difference between a transformer encoder and a decoder?"),
            Question("models-sampling", "models", "Explain temperature versus top-p when sampling from an LLM."),
            Question("models-rag", "models", "What is RAG, and when would you use it instead of fine-tuning?"),
            Question("models-embeddings", "models", "How do embeddings enable semantic search?"),
            Question("models-context", "models", "What is a context window, and what breaks when you exceed it?"),
            Question("models-quant", "models", "What is weight quantization, and what do you trade away for speed?"),
            Question("models-eval", "models", "How would you evaluate whether a smaller model is good enough for a production assistant?")
        }
    };

    public static readonly PracticeTopicPack Angular = new()
    {
        Id = "angular",
        Title = "Angular",
        Blurb = "Components, change detection, forms, and routing",
        Groups = new[] { new PracticeGroup { Id = "angular", Title = "Angular" } },
        Questions = new[]
        {
            Question("angular-component-directive", "angular", "What is the difference between an Angular component and a directive?"),
            Question("angular-change-detection", "angular", "How does Angular change detection work, and what does OnPush change?"),
            Question("angular-signals", "angular", "What are Angular signals, and why were they added?"),
            Question("angular-state", "angular", "When would you share state with a service versus NgRx?"),
            Question("angular-forms", "angular", "What is the difference between template-driven and reactive forms?"),
            Question("angular-router", "angular", "How does the Angular router lazy-load a feature module?"),
            Question("angular-pipes", "angular", "When should a pipe be pure, and what goes wrong if it is not?")
        }
    };

    public static readonly IReadOnlyList<PracticeTopicPack> All = new[] { Aws, Models, Angular };
}

/// <param name="CardPackID">Set when the role practices an authored four-choice deck instead of free-response questions.</param>
public sealed record PracticeRole(string Id, string Title, ValueList<string> GroupIds, string? CardPackID = null)
{
    public static readonly PracticeRole ClaudeArchitect = new(
        "claude-architect", "Claude Architect",
        new[] { "claude-agents", "claude-tools", "claude-code", "claude-prompts", "claude-context" },
        CardPackID: "claude-architect");

    public static readonly PracticeRole AIEngineerCards = new(
        "ai-engineer-cards", "AI Engineer Cards",
        new[] { "cards-agents", "cards-coding", "cards-evaluation", "cards-fastapi-and-production", "cards-llm-foundations", "cards-python-oop", "cards-python-runtime", "cards-rag", "cards-submitted-project" },
        CardPackID: "ai-engineer-cards");

    public static readonly PracticeRole MLCoreCards = new(
        "ml-core-cards", "Core ML Cards",
        new[] { "cards-ml-metrics", "cards-ml-data", "cards-ml-models", "cards-ml-validation", "cards-ml-deep" },
        CardPackID: "ml-core-cards");

    public static readonly PracticeRole TypeScriptCards = new(
        "typescript-cards", "TypeScript Cards", new[] { "cards-typescript" }, CardPackID: "typescript-cards");

    public static readonly PracticeRole AIEngineer = new(
        "ai-engineer", "AI Engineer",
        new[]
        {
            "python-engineering", "foundations", "classical-ml", "genai-and-llm",
            "ml-in-production", "system-design", "models", "interview-prep", "agentic-sme-s-and-p",
            "aws", "devops"
        });

    public static readonly PracticeRole Frontend = new(
        "frontend", "Frontend engineer",
        new[]
        {
            "jr-javascript", "mid-javascript", "senior-javascript", "typescript",
            "angular", "oop", "json", "networking", "engineering-practice", "system-design"
        });

    public static readonly PracticeRole FullStack = new(
        "full-stack", "Full stack",
        new[]
        {
            "python-engineering", "jr-javascript", "mid-javascript", "senior-javascript",
            "typescript", "angular", "oop", "system-design", "engineering-practice",
            "aws", "networking", "json", "devops", "genai-and-llm", "interview-prep"
        });

    public static readonly PracticeRole QAAutomation = new(
        "qa-automation", "QA automation",
        new[]
        {
            "qa", "coding-tasks", "logical-tasks", "python-engineering",
            "engineering-practice", "java-and-jvm", "system-design", "interview-prep"
        });

    public static readonly IReadOnlyList<PracticeRole> All = new[]
    {
        AIEngineer, ClaudeArchitect, AIEngineerCards, MLCoreCards, TypeScriptCards, Frontend, FullStack, QAAutomation
    };

    public static PracticeRole? CardRole(string packId) => All.FirstOrDefault(role => role.CardPackID == packId);
}

public enum PracticeRunMode
{
    Learn,
    Rehearse,
    Interview
}

public static class PracticeRunModes
{
    public static readonly IReadOnlyList<PracticeRunMode> AllCases = new[] { PracticeRunMode.Learn, PracticeRunMode.Rehearse, PracticeRunMode.Interview };

    public static string RawValue(this PracticeRunMode mode) => mode switch
    {
        PracticeRunMode.Learn => "learn",
        PracticeRunMode.Rehearse => "rehearse",
        _ => "interview"
    };

    public static PracticeRunMode? FromRawValue(string? rawValue) => rawValue switch
    {
        "learn" => PracticeRunMode.Learn,
        "rehearse" => PracticeRunMode.Rehearse,
        "interview" => PracticeRunMode.Interview,
        _ => null
    };

    public static string Title(this PracticeRunMode mode) => mode switch
    {
        PracticeRunMode.Learn => "Active Recall",
        PracticeRunMode.Rehearse => "Voice Rehearsal",
        _ => "Interview"
    };
}

public enum PracticeRecallRating
{
    Again,
    Hard,
    GotIt
}

public static class PracticeRecallRatings
{
    public static readonly IReadOnlyList<PracticeRecallRating> AllCases = new[] { PracticeRecallRating.Again, PracticeRecallRating.Hard, PracticeRecallRating.GotIt };

    public static string RawValue(this PracticeRecallRating rating) => rating switch
    {
        PracticeRecallRating.Again => "again",
        PracticeRecallRating.Hard => "hard",
        _ => "gotIt"
    };

    public static string Title(this PracticeRecallRating rating) => rating switch
    {
        PracticeRecallRating.Again => "Again",
        PracticeRecallRating.Hard => "Hard",
        _ => "Got it"
    };

    public static string IntervalTitle(this PracticeRecallRating rating) => rating switch
    {
        PracticeRecallRating.Again => "in a few minutes",
        PracticeRecallRating.Hard => "tomorrow",
        _ => "in 3 days"
    };

    public static double FallbackScore(this PracticeRecallRating rating) => rating switch
    {
        PracticeRecallRating.Again => 0.25,
        PracticeRecallRating.Hard => 0.65,
        _ => 1.0
    };
}

/// The learner's confidence before revealing the prepared answer.
/// Kept separate from the post-reveal rating so confident mistakes remain visible.
public enum PracticeRecallConfidence
{
    Unsure,
    Mostly,
    Very
}

public static class PracticeRecallConfidences
{
    public static readonly IReadOnlyList<PracticeRecallConfidence> AllCases = new[] { PracticeRecallConfidence.Unsure, PracticeRecallConfidence.Mostly, PracticeRecallConfidence.Very };

    public static string RawValue(this PracticeRecallConfidence confidence) => confidence switch
    {
        PracticeRecallConfidence.Unsure => "unsure",
        PracticeRecallConfidence.Mostly => "mostly",
        _ => "very"
    };

    public static string Title(this PracticeRecallConfidence confidence) => confidence switch
    {
        PracticeRecallConfidence.Unsure => "Unsure",
        PracticeRecallConfidence.Mostly => "Mostly sure",
        _ => "Very sure"
    };

    internal static double ReviewIntervalMultiplier(this PracticeRecallConfidence confidence) => confidence switch
    {
        PracticeRecallConfidence.Unsure => 0.6,
        PracticeRecallConfidence.Mostly => 1.0,
        _ => 1.25
    };
}

/// Retains what the learner produced before feedback as well as any repair attempt.
/// `answer` on legacy score records remains supported; new recall records can attach this value.
public sealed record PracticeRecallResponse
{
    public required string Initial { get; init; }
    public string? Revised { get; init; }

    public PracticeRecallResponse() { }

    /// A revision that is only whitespace is no revision.
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PracticeRecallResponse(string initial, string? revised = null)
    {
        Initial = initial;
        Revised = revised?.Trim().Length > 0 ? revised : null;
    }

    [JsonIgnore]
    public string Current => Revised ?? Initial;

    [JsonIgnore]
    public bool WasRevised => Revised is not null;
}

/// A learner correction to the estimated coverage of one Key Idea.
public sealed record PracticeCoverageOverride
{
    public required int IdeaIndex { get; init; }
    public required bool IsCovered { get; init; }

    public PracticeCoverageOverride() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PracticeCoverageOverride(int ideaIndex, bool isCovered)
    {
        IdeaIndex = ideaIndex;
        IsCovered = isCovered;
    }
}

public sealed record PracticeRecallReview
{
    public ValueList<string> KeyIdeas { get; }
    public ValueList<bool> AutomaticCovered { get; }
    public ValueList<bool> Covered { get; }
    public ValueList<PracticeCoverageOverride> CoverageOverrides { get; }

    public PracticeRecallReview(ValueList<string> keyIdeas, ValueList<bool> covered)
    {
        KeyIdeas = keyIdeas;
        AutomaticCovered = covered;
        Covered = covered;
        CoverageOverrides = ValueList<PracticeCoverageOverride>.Empty;
    }

    public PracticeRecallReview(ValueList<string> keyIdeas, ValueList<bool> automaticCovered, ValueList<PracticeCoverageOverride> coverageOverrides)
    {
        KeyIdeas = keyIdeas;
        AutomaticCovered = automaticCovered;
        CoverageOverrides = coverageOverrides;
        Covered = Practice.ApplyCoverageOverrides(automaticCovered, coverageOverrides);
    }

    public int CoveredCount => Covered.Count(covered => covered);

    public double Score => KeyIdeas.IsEmpty ? 0 : (double)CoveredCount / KeyIdeas.Count;
}

public sealed record PracticeLearnChoice(string Text, bool IsCorrect);

public enum PracticeLearnOptionMark
{
    Unmarked,
    SelectedCorrect,
    SelectedWrong,
    RevealedCorrect
}

public sealed record PracticeProgressInput(DateTime FinishedAt, double Score, string PackId);

public sealed record PracticeProgressPoint(DateTime FinishedAt, double Score, string PackId);

public static partial class Practice
{
    public static PracticeScoreResult ApplyHelpPenalty(double rawScore, bool usedHelp)
    {
        var raw = Math.Min(1.0, Math.Max(0.0, rawScore));
        return usedHelp
            ? new PracticeScoreResult(raw, true, PracticeHelpMark.Yellow, raw * PracticeScoring.HelpMultiplier)
            : new PracticeScoreResult(raw, false, PracticeHelpMark.None, raw);
    }

    /// Chip labels: interview topics, not study-book chapter names.
    public static string GroupDisplayTitle(string id, string fallback = "") => id switch
    {
        "foundations" => "LLM internals",
        "classical-ml" => "Tabular ML",
        "ml-in-production" => "MLOps",
        "genai-and-llm" => "RAG & agents",
        "system-design" => "System design",
        "python-engineering" => "Python",
        "java-and-jvm" => "Java",
        "engineering-practice" => "Design patterns",
        "quant-and-domain" => "Time series",
        "agentic-sme-s-and-p" => "Agents & security",
        "interview-prep" => "Whiteboard & DSA",
        "jr-javascript" => "JavaScript · junior",
        "mid-javascript" => "JavaScript · mid",
        "senior-javascript" => "JavaScript · senior",
        "coding-tasks" => "Coding",
        "logical-tasks" => "Logic puzzles",
        _ => fallback.Length == 0 ? id : fallback
    };

    public static List<PracticeGroup> BankGroups(PracticeBankFile bank)
    {
        var groups = new List<PracticeGroup>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in bank.Packs)
        {
            IEnumerable<PracticeGroup> packGroups = pack.Groups.IsEmpty
                ? new[] { new PracticeGroup { Id = pack.Id, Title = pack.Title } }
                : pack.Groups;
            foreach (var group in packGroups)
            {
                if (!seen.Add(group.Id)) continue;
                groups.Add(new PracticeGroup { Id = group.Id, Title = GroupDisplayTitle(group.Id, group.Title) });
            }
        }
        return groups;
    }

    public static List<string> PackIDs() => PracticeTopicPack.All.Select(pack => pack.Id).ToList();

    public static PracticeTopicPack? Pack(string id) => PracticeTopicPack.All.FirstOrDefault(pack => pack.Id == id);

    public static IReadOnlyList<PracticeQuestion> QuestionsForPack(string id) =>
        Pack(id)?.Questions ?? (IReadOnlyList<PracticeQuestion>)Array.Empty<PracticeQuestion>();

    public static List<PracticeQuestion> QuestionsMatching(PracticeTopicPack pack, IReadOnlyCollection<string> groupIds)
    {
        if (groupIds.Count == 0) return pack.Questions.ToList();
        var allowed = new HashSet<string>(groupIds, StringComparer.Ordinal);
        return pack.Questions.Where(question => allowed.Contains(question.GroupId)).ToList();
    }

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex ListMarker = new(@"^\s*(?:[-*•]+|\d+[.)])\s+", RegexOptions.CultureInvariant);

    public static string NormalizeQuestionText(string text)
    {
        var s = HtmlTag.Replace(text, "");
        var entities = new (string Entity, string Replacement)[]
        {
            ("&nbsp;", " "), ("&amp;", "&"), ("&lt;", "<"), ("&gt;", ">"),
            ("&#39;", "'"), ("&quot;", "\"")
        };
        foreach (var (entity, replacement) in entities)
        {
            s = s.Replace(entity, replacement, StringComparison.Ordinal);
        }
        s = WhitespaceRun.Replace(s, " ");
        return s.Trim();
    }

    public static List<PracticeQuestion> QuestionPool(PracticeBankFile bank, IReadOnlySet<string>? groupIDs = null)
    {
        var questions = bank.Packs.SelectMany(pack => pack.Questions);
        if (groupIDs is null || groupIDs.Count == 0) return questions.ToList();
        return questions.Where(question => groupIDs.Contains(question.GroupId)).ToList();
    }

    public static PracticeTopicPack? StudyBookPack(PracticeBankFile bank) => bank.Packs.FirstOrDefault(pack => pack.Id == "study-book");

    public static string TopicKey(PracticeQuestion question, IReadOnlyCollection<string> groupIds)
    {
        if (groupIds.Count == 1)
        {
            return question.TopicId.Length == 0 ? question.GroupId : question.TopicId;
        }
        return question.GroupId.Length == 0 ? question.PackId : question.GroupId;
    }

    public static PracticeRole? Role(string id) => PracticeRole.All.FirstOrDefault(role => role.Id == id);

    public static List<string> GroupIDs(PracticeRole role, IEnumerable<string> available)
    {
        var allowed = new HashSet<string>(available, StringComparer.Ordinal);
        return role.GroupIds.Where(allowed.Contains).ToList();
    }

    /// Interview flow after skipping background: language fundamentals, then models, then applied AI.
    public static readonly IReadOnlyList<string> InterviewTopicOrder = new[]
    {
        "jr-javascript",
        "mid-javascript",
        "senior-javascript",
        "typescript",
        "python-engineering",
        "oop",
        "foundations",
        "classical-ml",
        "genai-and-llm",
        "ml-in-production",
        "system-design",
        "engineering-practice",
        "java-and-jvm",
        "quant-and-domain",
        "agentic-sme-s-and-p",
        "interview-prep",
        "qa",
        "coding-tasks",
        "logical-tasks",
        "networking",
        "json",
        "devops",
        "aws",
        "models",
        "angular"
    };

    internal static List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var copy = items.ToArray();
        Random.Shared.Shuffle(copy);
        return copy.ToList();
    }

    /// Round-robin across topics so a 100-question run is as even as the bank allows.
    public static List<PracticeQuestion> BalancedSelection(
        IReadOnlyList<PracticeQuestion> questions,
        int count,
        Func<PracticeQuestion, string> topicOf,
        Func<List<PracticeQuestion>, List<PracticeQuestion>>? shuffle = null)
    {
        shuffle ??= Shuffled;
        var want = Math.Min(Math.Max(0, count), questions.Count);
        if (want <= 0) return new List<PracticeQuestion>();

        var order = new List<string>();
        var buckets = new Dictionary<string, List<PracticeQuestion>>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            var topic = topicOf(question);
            if (!buckets.TryGetValue(topic, out var bucket))
            {
                order.Add(topic);
                bucket = new List<PracticeQuestion>();
                buckets[topic] = bucket;
            }
            bucket.Add(question);
        }
        foreach (var topic in order)
        {
            buckets[topic] = shuffle(buckets[topic]);
        }

        var nextIndex = order.ToDictionary(topic => topic, _ => 0, StringComparer.Ordinal);
        var selected = new List<PracticeQuestion>(want);
        var progressed = true;
        while (selected.Count < want && progressed)
        {
            progressed = false;
            foreach (var topic in order)
            {
                if (selected.Count >= want) break;
                var index = nextIndex[topic];
                var pool = buckets[topic];
                if (index < pool.Count)
                {
                    selected.Add(pool[index]);
                    nextIndex[topic] = index + 1;
                    progressed = true;
                }
            }
        }
        return selected;
    }

    /// Same even split, but questions stay in interview order: finish Python, then models, not mixed.
    public static List<PracticeQuestion> InterviewOrderedSelection(
        IReadOnlyList<PracticeQuestion> questions,
        int count,
        Func<PracticeQuestion, string> topicOf,
        IReadOnlyList<string>? topicOrder = null,
        Func<List<PracticeQuestion>, List<PracticeQuestion>>? shuffle = null)
    {
        var picked = BalancedSelection(questions, count, topicOf, shuffle);
        var buckets = new Dictionary<string, List<PracticeQuestion>>(StringComparer.Ordinal);
        var appearance = new List<string>();
        foreach (var question in picked)
        {
            var key = topicOf(question);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                appearance.Add(key);
                bucket = new List<PracticeQuestion>();
                buckets[key] = bucket;
            }
            bucket.Add(question);
        }
        var result = new List<PracticeQuestion>();
        var leftover = new HashSet<string>(appearance, StringComparer.Ordinal);
        foreach (var topic in topicOrder ?? InterviewTopicOrder)
        {
            if (!leftover.Remove(topic)) continue;
            result.AddRange(buckets[topic]);
        }
        foreach (var topic in appearance)
        {
            if (leftover.Contains(topic)) result.AddRange(buckets[topic]);
        }
        return result;
    }

    /// Curriculum order for authored card decks: lesson or topic numbers first, then the card number,
    /// compared numerically so card 10 follows card 9. Questions without numbers keep their relative order.
    public static List<long> CurriculumOrderKey(PracticeQuestion question)
    {
        static IEnumerable<long> Numbers(string text)
        {
            foreach (var part in text.SplitWhere(character => !char.IsNumber(character)))
            {
                if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) yield return number;
            }
        }

        var key = Numbers(question.TopicId).ToList();
        key.Add(long.MaxValue);
        key.AddRange(Numbers(question.Id));
        return key;
    }

    private static int CompareKeys(List<long> left, List<long> right)
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            if (left[index] != right[index]) return left[index] < right[index] ? -1 : 1;
        }
        return left.Count.CompareTo(right.Count);
    }

    public static List<PracticeQuestion> CurriculumOrdered(IEnumerable<PracticeQuestion> questions)
    {
        var keyed = questions.Select((question, offset) => (Question: question, Offset: offset, Key: CurriculumOrderKey(question))).ToList();
        keyed.Sort((lhs, rhs) =>
        {
            var byKey = CompareKeys(lhs.Key, rhs.Key);
            return byKey != 0 ? byKey : lhs.Offset.CompareTo(rhs.Offset);
        });
        return keyed.Select(item => item.Question).ToList();
    }

    public static string QuestionSourceLine(PracticeQuestion question, string groupTitle)
    {
        var origin = question.PackId switch
        {
            "study-book" => "Study book",
            "interview-guide" => "Interview guide",
            "ai-engineer-interview" => "AI engineer interview",
            "ai-engineer-prep" => "AI interview prep",
            "ai-engineer-interviewer" => "AI interviewer view",
            "python-interview" => "Python interview",
            "typescript-interview" => "TypeScript interview",
            "oop-interview" => "OOP interview",
            "llm-interview" => "LLM interview",
            "aws-interview" => "AWS interview",
            "devops-interview" => "DevOps interview",
            "aws" => "AWS pack",
            "models" => "Models pack",
            "angular" => "Angular pack",
            _ => question.PackId.Length == 0 ? "Practice" : question.PackId
        };
        var parts = new List<string> { origin };
        if (groupTitle.Length > 0) parts.Add(groupTitle);
        if (question.TopicTitle.Length > 0 && question.TopicTitle != groupTitle) parts.Add(question.TopicTitle);
        return string.Join(" · ", parts);
    }

    public static string? HelpText(PracticeQuestion question)
    {
        var answer = question.Answer.Trim();
        if (answer.Length > 0) return answer;
        var rubric = question.Rubric.Trim();
        return rubric.Length > 0 ? rubric : null;
    }

    public static PracticeRunMode RunMode(string id) => PracticeRunModes.FromRawValue(id) ?? PracticeRunMode.Learn;

    public static bool HelpAllowed(PracticeRunMode mode) => mode != PracticeRunMode.Interview;

    public static bool ShowsMultipleChoice(PracticeRunMode mode) => false;

    public static bool ShowsSubmitButton(PracticeRunMode mode) => true;

    /// Full Help is only the stored bank answer, and only after a Learn choice.
    public static string? RevealedHelp(PracticeRunMode mode, bool hasSelection, PracticeQuestion question) =>
        HelpAllowed(mode) && hasSelection ? HelpText(question) : null;

    /// Applies learner corrections in order. Invalid indices are ignored and the last
    /// correction for an idea wins, which makes repeated toggles deterministic.
    public static ValueList<bool> ApplyCoverageOverrides(IReadOnlyList<bool> automaticCoverage, IEnumerable<PracticeCoverageOverride> overrides)
    {
        var result = automaticCoverage.ToArray();
        foreach (var item in overrides)
        {
            if (item.IdeaIndex >= 0 && item.IdeaIndex < result.Length) result[item.IdeaIndex] = item.IsCovered;
        }
        return result;
    }

    public static DateTime NextReviewDate(PracticeRecallRating rating, DateTime? from = null)
    {
        double interval = rating switch
        {
            PracticeRecallRating.Again => 5 * 60,
            PracticeRecallRating.Hard => 24 * 60 * 60,
            _ => 3 * 24 * 60 * 60
        };
        return (from ?? DateTime.UtcNow).AddSeconds(interval);
    }

    /// Calculates a review interval that grows with demonstrated recall, is shortened
    /// by low pre-reveal confidence, and leaves another review opportunity before an
    /// interview/target date. The existing fixed scheduler above remains unchanged for
    /// compatibility with completed runs and current UI copy.
    public static double AdaptiveReviewInterval(
        PracticeRecallRating rating,
        IReadOnlyList<PracticeScoredAnswer> priorAttempts,
        PracticeRecallConfidence? confidence,
        DateTime? from = null,
        DateTime? targetDate = null)
    {
        const double day = 24 * 60 * 60;
        var date = from ?? DateTime.UtcNow;
        var unassistedAttempts = priorAttempts.Where(attempt => !attempt.UsedHelp).ToList();
        var successfulReviews = unassistedAttempts.Count(attempt =>
            attempt.RecallRating == PracticeRecallRating.GotIt && attempt.FinalScore >= 0.75 && !attempt.UsedHelp);
        var attemptBonus = 1.0 + Math.Min(unassistedAttempts.Count, 6.0) * 0.08;

        double interval;
        switch (rating)
        {
            case PracticeRecallRating.Again:
                interval = 5 * 60;
                break;
            case PracticeRecallRating.Hard:
                interval = day * Math.Min(2.0, 1.0 + successfulReviews * 0.15);
                break;
            default:
                var growth = Math.Pow(1.8, Math.Min(successfulReviews, 4));
                interval = 3 * day * growth * attemptBonus;
                break;
        }

        if (rating != PracticeRecallRating.Again && confidence is { } known)
        {
            interval *= known.ReviewIntervalMultiplier();
        }
        interval = Math.Min(interval, 45 * day);

        if (targetDate is { } target)
        {
            var remaining = (target - date).TotalSeconds;
            if (remaining > 0)
            {
                // Keep at least one more retrieval opportunity before the target when possible.
                var targetCap = remaining >= 10 * 60 ? Math.Max(5 * 60, remaining / 2) : remaining;
                interval = Math.Min(Math.Min(interval, targetCap), remaining);
            }
        }
        return Math.Max(0, interval);
    }

    public static DateTime AdaptiveNextReviewDate(
        PracticeRecallRating rating,
        IReadOnlyList<PracticeScoredAnswer> priorAttempts,
        PracticeRecallConfidence? confidence,
        DateTime? from = null,
        DateTime? targetDate = null)
    {
        var date = from ?? DateTime.UtcNow;
        return date.AddSeconds(AdaptiveReviewInterval(rating, priorAttempts, confidence, date, targetDate));
    }

    private static string CleanKeyIdea(string raw, int maxLength = 132)
    {
        var value = raw.Trim();
        value = ListMarker.Replace(value, "");
        value = WhitespaceRun.Replace(value, " ");
        value = value.Trim();
        if (value.Length == 0 || AnswerLineIsHeading(value)) return "";
        if (value.Length <= maxLength) return value;

        var prefix = value.Prefix(maxLength - 1);
        var boundary = prefix.LastIndexOf(' ');
        return boundary >= 0 ? prefix[..boundary].TrimInline() + "…" : prefix + "…";
    }

    /// Extracts short, local-first review points from the prepared answer or rubric.
    /// Structured bullets win; sentence and non-question hint fallbacks keep sparse banks useful.
    public static List<string> KeyIdeas(PracticeQuestion question, int limit = 3)
    {
        if (limit <= 0) return new List<string>();
        var body = HelpText(question) ?? "";
        var candidates = new List<string>();

        foreach (var line in body.SplitOmittingEmpty('\n'))
        {
            if (!ListMarker.IsMatch(line)) continue;
            var idea = CleanKeyIdea(line);
            if (idea.Length > 0) candidates.Add(idea);
        }

        if (candidates.Count < limit)
        {
            foreach (var part in body.Split('.', '!', '?', ';', '\n'))
            {
                var idea = CleanKeyIdea(part);
                if (idea.SplitWhere(char.IsWhiteSpace).Count >= 3) candidates.Add(idea);
            }
        }

        if (candidates.Count < limit)
        {
            foreach (var hint in question.Hints)
            {
                if (hint.Trim().HasSuffix("?")) continue;
                var idea = CleanKeyIdea(hint);
                if (idea.Length > 0) candidates.Add(idea);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var candidate in candidates)
        {
            var key = candidate.Lowercased().Where(character => char.IsLetter(character) || char.IsNumber(character));
            if (key.Length == 0 || !seen.Add(key)) continue;
            result.Add(candidate);
            if (result.Count == limit) break;
        }
        return result;
    }

    private static readonly HashSet<string> RecallStopWords = new(StringComparer.Ordinal)
    {
        "and", "are", "but", "can", "for", "from", "has", "have", "into", "its", "not",
        "that", "the", "their", "then", "this", "through", "use", "using", "was", "were",
        "when", "which", "with", "you", "your"
    };

    private static readonly string[] RecallSuffixes =
    {
        "ments", "ment", "ations", "ation", "ions", "tion", "ally", "ingly", "edly", "ity", "ing", "ed", "es", "ly", "s"
    };

    private static string? RecallTermKey(string word)
    {
        var value = word.Lowercased();
        if (value.Length <= 2 || RecallStopWords.Contains(value)) return null;
        foreach (var suffix in RecallSuffixes)
        {
            if (value.HasSuffix(suffix) && value.Length - suffix.Length >= 4)
            {
                value = value[..^suffix.Length];
                break;
            }
        }
        return value.Prefix(6);
    }

    private static HashSet<string> RecallTermKeys(string text) =>
        text.SplitWhere(character => !char.IsLetter(character))
            .Select(RecallTermKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> NegationMarkers = new(StringComparer.Ordinal)
    {
        "not", "no", "never", "none", "neither", "nor", "without", "cannot", "isnt", "arent", "wasnt", "werent",
        "doesnt", "dont", "didnt", "wont", "wouldnt", "shouldnt", "couldnt", "hasnt", "havent", "cant"
    };

    private static bool RecallIsNegated(string text) =>
        text.Lowercased()
            .Replace("’", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .SplitWhere(character => !char.IsLetter(character))
            .Any(NegationMarkers.Contains);

    /// Clauses that can carry their own polarity; a negated clause must not count as covering a positive idea.
    private static List<string> RecallClauses(string text) =>
        text.Split('.', '!', '?', ';', ':', ',', '\n')
            .SelectMany(part => part.Split(" but "))
            .SelectMany(part => part.Split(" whereas "))
            .Select(part => part.TrimInline())
            .Where(part => part.Length > 0)
            .ToList();

    private static bool RecallOverlapCovers(HashSet<string> answerTerms, HashSet<string> ideaTerms)
    {
        var overlap = answerTerms.Count(ideaTerms.Contains);
        var needed = ideaTerms.Count <= 3 ? 1 : 2;
        return overlap >= needed;
    }

    public static ValueList<bool> CoveredKeyIdeas(
        string answer,
        IReadOnlyList<string> keyIdeas,
        IEnumerable<PracticeCoverageOverride>? coverageOverrides = null)
    {
        var answerTerms = RecallTermKeys(answer);
        var answerNegated = RecallIsNegated(answer);
        var clauses = RecallClauses(answer).Select(clause => (Terms: RecallTermKeys(clause), Negated: RecallIsNegated(clause))).ToList();
        var automatic = keyIdeas.Select(idea =>
        {
            if (answerTerms.Count == 0) return false;
            var ideaTerms = RecallTermKeys(idea);
            if (ideaTerms.Count == 0) return false;
            var ideaNegated = RecallIsNegated(idea);
            if (!answerNegated && !ideaNegated) return RecallOverlapCovers(answerTerms, ideaTerms);
            return clauses.Any(clause => clause.Negated == ideaNegated && RecallOverlapCovers(clause.Terms, ideaTerms));
        }).ToArray();
        return ApplyCoverageOverrides(automatic, coverageOverrides ?? Array.Empty<PracticeCoverageOverride>());
    }

    public static PracticeRecallReview RecallReview(
        PracticeQuestion question,
        string answer,
        IEnumerable<PracticeCoverageOverride>? coverageOverrides = null)
    {
        var ideas = KeyIdeas(question);
        var automatic = CoveredKeyIdeas(answer, ideas);
        return new PracticeRecallReview(ideas, automatic, new ValueList<PracticeCoverageOverride>(coverageOverrides ?? Array.Empty<PracticeCoverageOverride>()));
    }

    public static PracticeLearnOptionMark LearnOptionMark(int index, int? selected, int? correct)
    {
        if (selected is not { } chosen || correct is not { } right) return PracticeLearnOptionMark.Unmarked;
        if (index == chosen)
        {
            return index == right ? PracticeLearnOptionMark.SelectedCorrect : PracticeLearnOptionMark.SelectedWrong;
        }
        return index == right ? PracticeLearnOptionMark.RevealedCorrect : PracticeLearnOptionMark.Unmarked;
    }

    public static bool AnswerLineIsHeading(string line)
    {
        var s = line.TrimInline();
        if (s.HasPrefix("- ")) s = s.DropFirst(2).TrimInline();
        if (!s.HasSuffix(":")) return false;
        if (s.Contains('.')) return false;
        return s.SplitWhere(char.IsWhiteSpace).Count <= 6;
    }

    public static string AnswerStem(PracticeQuestion question, int maxLength = 160)
    {
        var body = (HelpText(question) ?? "").Trim();
        if (body.Length == 0) return "";
        var lines = body.SplitOmittingEmpty('\n').Select(item => item.TrimInline()).Where(item => item.Length > 0).ToList();
        while (lines.Count > 1 && AnswerLineIsHeading(lines[0]))
        {
            lines.RemoveAt(0);
        }
        var line = lines.Count > 0 ? lines[0] : body;
        if (line.HasPrefix("- ")) line = line.DropFirst(2).TrimInline();
        if (AnswerLineIsHeading(line) && lines.Count > 1)
        {
            line = lines[1];
            if (line.HasPrefix("- ")) line = line.DropFirst(2).TrimInline();
        }
        if (line.Length > maxLength)
        {
            var sentenceEnd = line.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceEnd >= 0) line = line[..sentenceEnd] + ".";
        }
        if (line.Length > maxLength)
        {
            line = line.Prefix(maxLength - 1).TrimInline() + "…";
        }
        return line;
    }

    public static List<PracticeLearnChoice> LearnChoices(
        PracticeQuestion question,
        IReadOnlyList<PracticeQuestion> bank,
        Func<List<PracticeLearnChoice>, List<PracticeLearnChoice>>? shuffle = null)
    {
        var correct = AnswerStem(question);
        if (correct.Length == 0) return new List<PracticeLearnChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { correct.Lowercased() };
        var distractors = new List<string>();

        void Consider(IEnumerable<PracticeQuestion> questions)
        {
            foreach (var candidate in questions)
            {
                if (distractors.Count >= 3) return;
                if (candidate.Id == question.Id) continue;
                var stem = AnswerStem(candidate);
                var key = stem.Lowercased();
                if (stem.Length == 0 || seen.Contains(key)) continue;
                seen.Add(key);
                distractors.Add(stem);
            }
        }

        var others = bank.Where(item => item.Id != question.Id).ToList();
        if (question.TopicId.Length > 0) Consider(others.Where(item => item.TopicId == question.TopicId));
        if (question.GroupId.Length > 0) Consider(others.Where(item => item.GroupId == question.GroupId));
        Consider(others.Where(item => item.PackId == question.PackId));
        Consider(others);
        if (distractors.Count < 3) return new List<PracticeLearnChoice>();
        var choices = new List<PracticeLearnChoice> { new(correct, true) };
        choices.AddRange(distractors.Take(3).Select(text => new PracticeLearnChoice(text, false)));
        return (shuffle ?? Shuffled)(choices);
    }

    public static List<PracticeProgressPoint> ProgressSeries(IEnumerable<PracticeProgressInput> runs, string? packId = null) =>
        runs.Where(run => packId is null || run.PackId == packId)
            .OrderBy(run => run.FinishedAt)
            .Select(run => new PracticeProgressPoint(run.FinishedAt, run.Score, run.PackId))
            .ToList();
}
