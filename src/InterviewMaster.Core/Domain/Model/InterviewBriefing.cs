using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Live conversation briefing shown on the Timeline (digest + resume talking points).
/// Cue cards are a separate path; this type never opens one.
public sealed record InterviewBriefing(string Thread, string Digest, ValueList<string> TalkingPoints);

/// `Briefing` is null for "no change".
public readonly record struct BriefingDecision(InterviewBriefing? Briefing)
{
    public static readonly BriefingDecision NoChange = new((InterviewBriefing?)null);

    public static BriefingDecision Update(InterviewBriefing briefing) => new(briefing);

    public bool IsUpdate => Briefing is not null;
}

/// <param name="EndYear">Null for a current role.</param>
public sealed record ResumeRole(string Heading, int StartYear, int? EndYear, ValueList<string> EmployerKeywords);

public static class Briefing
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// Briefing never opens a cue card. Questions still use the existing card gate.
    public static bool BriefingOpensCueCard(BriefingDecision decision) => false;

    public static BriefingDecision NextInterviewBriefing(string utterance, bool isQuestion, string resumeText, InterviewBriefing? prior)
    {
        var trimmed = utterance.Trim();
        if (trimmed.Length < 8) return BriefingDecision.NoChange;

        var thread = DetectBriefingThread(trimmed);
        if (thread == "unknown")
        {
            thread = prior?.Thread ?? "unknown";
        }
        if (thread == "unknown") return BriefingDecision.NoChange;

        var next = new InterviewBriefing(
            thread,
            BriefingDigest(trimmed, thread),
            TalkingPointsFromResume(resumeText, thread));
        if (next == prior) return BriefingDecision.NoChange;
        return BriefingDecision.Update(next);
    }

    public static string FormatBriefingContent(InterviewBriefing briefing)
    {
        var lines = new List<string> { briefing.Digest };
        if (briefing.TalkingPoints.IsEmpty)
        {
            lines.Add("No matching resume points for this thread.");
        }
        else
        {
            lines.AddRange(briefing.TalkingPoints.Select(point => $"- [CV] {point}"));
        }
        return string.Join("\n", lines);
    }

    private static readonly Regex FencedCode = new(@"```[\s\S]*?```", Options);
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]+\)", Options);
    private static readonly Regex MarkdownImage = new(@"!\[[^\]]*\]\([^)]+\)", Options);
    private static readonly Regex MarkdownMarks = new(@"[*_`#>]+", Options);
    private static readonly Regex BlankLineRun = new(@"\n{3,}", Options);
    private static readonly Regex WhitespaceRun = new(@"\s+", Options);

    public static string ExtractResumeText(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        text = FencedCode.Replace(text, " ");
        text = MarkdownLink.Replace(text, "$1");
        text = MarkdownImage.Replace(text, " ");
        text = MarkdownMarks.Replace(text, " ");
        var lines = text.SplitKeepingEmpty('\n').Select(line =>
        {
            var trimmed = line.TrimInline();
            while (trimmed.HasPrefix("- ") || trimmed.HasPrefix("* "))
            {
                trimmed = trimmed.DropFirst(2).TrimInline();
            }
            return trimmed;
        });
        return BlankLineRun.Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    private static readonly string[] PastMarkers =
    {
        "did you", "have you", "have you used", "how have you",
        "your experience", "your background", "your last",
        "you used", "you do with", "you did",
        "walk me through your", "tell me about yourself",
        "in your last", "in your role",
        "side project", "personal project", "hobby project", "outside of work", "in your free time",
        "your projects", "any projects"
    };

    // "When would you use X?" or "You've got sales data..." are scenarios, not requests for a resume story.
    // "you have a " keeps its trailing space: "do you have any side projects" is not a scenario.
    private static readonly string[] HypotheticalMarkers =
    {
        "would you", "could you", "should you", "imagine", "suppose", "let's say", "you've got", "you have a ", "you're given"
    };

    private static readonly string[] PresentMarkers = { "you use", "you've", "you have" };

    public static bool IsExperienceCueCardQuestion(string text, string topic)
    {
        if (topic.Lowercased() == "personal") return true;
        var normalized = NormalizedBriefingText(text);
        if (normalized.ContainsAny(PastMarkers)) return true;
        if (normalized.ContainsAny(HypotheticalMarkers)) return false;
        return normalized.ContainsAny(PresentMarkers);
    }

    /// `recentContext` is the recent conversation; a follow-up like "What models did you use?"
    /// inherits the employer the interviewer named just before.
    public static string ResumeGroundingInstruction(string resumeText, string questionText, string topic, string recentContext = "")
    {
        var acrossRoles = AsksAcrossResumeRoles(questionText);
        var isExperience = IsExperienceCueCardQuestion(questionText, topic) || acrossRoles;
        var askedRole = acrossRoles
            ? null
            : ResumeRoleMentioned(questionText, resumeText)
              ?? (isExperience ? ResumeRoleMostRecentlyMentioned(recentContext, resumeText) : null);
        if (!isExperience && askedRole is null) return "";
        var trimmed = resumeText.Trim();
        if (trimmed.Length == 0)
        {
            return "Do not invent personal experience, jobs, or tools the candidate has not listed.\n"
                + "Do not tell a first-person story about shipping this work.";
        }
        var instruction =
            "CANDIDATE RESUME (only source of personal experience):\n"
            + trimmed + "\n"
            + "Use ONLY this resume for personal/experience answers.\n"
            + "Do not invent jobs, tools, or projects that are not listed.\n"
            + "If the asked tool or project is not in the resume, do not tell a first-person story about using it.\n"
            + "Never invent numbers, percentages, dataset sizes, labels, or outcomes that the resume does not state. "
            + "When the resume is vague, describe the approach (\"I would\", \"the method was\") instead of made-up specifics.\n"
            + "Keep tools and methods true to the dates of the role being described; never put newer technology into an older job.";
        if (askedRole is not null)
        {
            instruction += "\n" + ResumeEraInstruction(askedRole, questionText);
        }
        if (acrossRoles)
        {
            instruction +=
                "\nACROSS ROLES: the interviewer wants the candidate's other or all companies, not one job.\n"
                + "One bullet per company from the resume that is relevant to the topic being discussed "
                + "(keep the topic from the previous question, e.g. machine learning or AI), with the years.\n"
                + "Skip companies the interviewer excluded (\"other than\", \"besides\") and companies with no relevant resume evidence.";
        }
        return instruction;
    }

    private static readonly HashSet<string> RoleNouns = new()
    {
        "companies", "company", "roles", "jobs", "employers", "positions", "places"
    };

    private static readonly HashSet<string> SpanWords = new()
    {
        "other", "others", "another", "every", "all", "each", "besides", "else", "previous", "rest"
    };

    /// "Other companies", "every company", "besides Honeywell": a request spanning resume roles.
    public static bool AsksAcrossResumeRoles(string text)
    {
        var tokens = new HashSet<string>(ResumeWordTokens(text));
        if (tokens.Contains("besides")) return true;
        return tokens.Overlaps(RoleNouns) && tokens.Overlaps(SpanWords);
    }

    private static readonly Regex RoleLine = new(
        @"^(.+?)\s+[-–—]\s+(.+?)\s*\((\d{4})(?:\s*[-–—]\s*(\d{4}|current|present))?[^)]*\)\s*$",
        Options | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> GenericEmployerWords = new()
    {
        "group", "inc", "ltd", "gmbh", "corp", "company", "the", "and", "aerospace", "technologies", "systems", "solutions", "ai"
    };

    /// Roles are resume lines like "Software Engineer - Honeywell Aerospace (2016 - 2018)".
    public static List<ResumeRole> ResumeRoles(string resumeText)
    {
        var roles = new List<ResumeRole>();
        foreach (var rawLine in ExtractResumeText(resumeText).SplitOmittingEmpty('\n'))
        {
            var line = rawLine.TrimInline();
            var match = RoleLine.Match(line);
            if (!match.Success || !int.TryParse(match.Groups[3].Value, out var startYear)) continue;
            int? endYear = startYear;
            if (match.Groups[4].Success)
            {
                endYear = int.TryParse(match.Groups[4].Value, out var parsed) ? parsed : null;
            }
            var keywords = ResumeWordTokens(match.Groups[2].Value)
                .Where(word => word.Length >= 2 && !GenericEmployerWords.Contains(word))
                .ToList();
            if (keywords.Count == 0) continue;
            roles.Add(new ResumeRole(line, startYear, endYear, keywords));
        }
        return roles;
    }

    public static ResumeRole? ResumeRoleMentioned(string questionText, string resumeText)
    {
        var words = new HashSet<string>(ResumeWordTokens(questionText));
        return ResumeRoles(resumeText).FirstOrDefault(role => role.EmployerKeywords.Any(words.Contains));
    }

    public static ResumeRole? ResumeRoleMostRecentlyMentioned(string text, string resumeText)
    {
        var tokens = ResumeWordTokens(text);
        var roles = ResumeRoles(resumeText);
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            var role = roles.FirstOrDefault(candidate => candidate.EmployerKeywords.Contains(tokens[index]));
            if (role is not null) return role;
        }
        return null;
    }

    private static List<string> ResumeWordTokens(string text) => text.Lowercased().LetterNumberWords();

    private static readonly HashSet<string> TodayWords = new()
    {
        "today", "now", "nowadays", "modern", "differently", "redo"
    };

    public static string ResumeEraInstruction(ResumeRole role, string questionText = "")
    {
        if (role.EndYear is not int endYear)
        {
            return $"ASKED ROLE: {role.Heading}. This is a current role; modern tools are fine if the resume lists them.";
        }
        var period = $"{role.StartYear}-{endYear}";
        var asksAboutToday = ResumeWordTokens(questionText).Any(TodayWords.Contains);
        if (asksAboutToday)
        {
            return $"ASKED ROLE: {role.Heading} ({period}).\n"
                + $"The question asks how the candidate would approach it today. Keep what was actually done true to {endYear}-era tools, "
                + "then clearly label modern improvements with \"Today I would...\".";
        }
        return $"ASKED ROLE: {role.Heading}.\n"
            + $"ERA ({period}): describe only tools, libraries, and methods that existed by {endYear}.\n"
            + $"Do not mention anything released after {endYear}. Release years for reference: XGBoost 2014, TensorFlow and Keras 2015, "
            + "LightGBM and YOLO 2016, PyTorch and Transformers 2017, CatBoost 2017, BERT 2018, Playwright 2020, GPT-3 2020, LoRA 2021, "
            + "ChatGPT and LangChain 2022, RAG went mainstream in 2023, LangGraph 2024.\n"
            + "Before 2020, speak in that era's terms: pandas, NumPy, scikit-learn, Keras/TensorFlow 1.x, classic neural nets (MLP, LSTM), Hadoop/Spark, Jenkins.";
    }

    // MARK: - Internals

    private static readonly (string Thread, string[] Keywords)[] BriefingThreadKeywords =
    {
        ("rag", new[]
        {
            "graph rag", "hybrid search", "multi hop", "multi-hop",
            "chunking", "chunk", "embedding", "embeddings", "rerank", "reranking",
            "citation", "citations", "retrieval", "vector db", "vector database",
            "rag"
        }),
        ("agents", new[]
        {
            "langgraph", "langchain", "multi agent", "multi-agent",
            "tool calling", "hitl", "human in the loop",
            "react", "agents", "agent"
        }),
        ("evals", new[]
        {
            "faithfulness", "grounding", "mrr", "precision", "recall",
            "evals", "eval"
        }),
        ("production", new[]
        {
            "fastapi", "pydantic", "langsmith", "mlflow", "serverless",
            "kubernetes", "aks", "k8s", "timeouts", "retries", "production"
        }),
        ("personal", new[]
        {
            "tell me about yourself", "your background", "your experience",
            "last project", "introduce yourself"
        })
    };

    private static string NormalizedBriefingText(string text) =>
        WhitespaceRun.Replace(text.Lowercased().Replace("’", "'").Replace("-", " "), " ").Trim();

    private static bool BriefingTextContainsKeyword(string haystack, string keyword)
    {
        var needle = keyword.Lowercased();
        if (needle.Contains(' '))
        {
            return haystack.Contains(needle, StringComparison.Ordinal);
        }
        return Regex.IsMatch(haystack, $"(^|[^a-z0-9]){Regex.Escape(needle)}($|[^a-z0-9])", RegexOptions.CultureInvariant);
    }

    public static string DetectBriefingThread(string utterance)
    {
        var normalized = NormalizedBriefingText(utterance);
        var bestThread = "unknown";
        var bestScore = 0;
        foreach (var entry in BriefingThreadKeywords)
        {
            var score = entry.Keywords.Count(keyword => BriefingTextContainsKeyword(normalized, keyword));
            if (score > bestScore)
            {
                bestScore = score;
                bestThread = entry.Thread;
            }
        }
        return bestScore > 0 ? bestThread : "unknown";
    }

    private static readonly Dictionary<string, string> ThreadLabels = new()
    {
        ["rag"] = "RAG / retrieval",
        ["agents"] = "Agents / orchestration",
        ["evals"] = "Evals / grounding",
        ["production"] = "Production / serving",
        ["personal"] = "Background / experience"
    };

    public static string BriefingDigest(string utterance, string thread)
    {
        var label = ThreadLabels.TryGetValue(thread, out var known) ? known : thread;
        var trimmed = utterance.Trim();
        var snippet = trimmed.Length > 140 ? trimmed.Prefix(137) + "..." : trimmed;
        return $"{label}: {snippet}";
    }

    public static ValueList<string> TalkingPointsFromResume(string resumeText, string thread)
    {
        var extracted = ExtractResumeText(resumeText);
        if (extracted.Length == 0) return ValueList<string>.Empty;
        var keywords = BriefingThreadKeywords.FirstOrDefault(entry => entry.Thread == thread).Keywords;
        if (keywords is null || keywords.Length == 0) return ValueList<string>.Empty;

        var points = new List<string>();
        foreach (var chunk in ResumeChunks(extracted))
        {
            var normalized = NormalizedBriefingText(chunk);
            if (!keywords.Any(keyword => BriefingTextContainsKeyword(normalized, keyword))) continue;
            if (IsNegatedResumeChunk(normalized)) continue;
            var point = CompactResumePoint(chunk);
            if (point.Length == 0 || points.Contains(point)) continue;
            points.Add(point);
            if (points.Count == 4) break;
        }
        return points;
    }

    private static List<string> ResumeChunks(string text)
    {
        var chunks = new List<string>();
        foreach (var rawLine in text.SplitKeepingEmpty('\n'))
        {
            var line = rawLine.TrimInline();
            if (line.Length < 12) continue;
            if (line.Length > 180)
            {
                foreach (var sentence in line.SplitOmittingEmpty('.'))
                {
                    var piece = sentence.TrimInline();
                    if (piece.Length >= 12) chunks.Add(piece);
                }
            }
            else
            {
                chunks.Add(line);
            }
        }
        return chunks;
    }

    private static bool IsNegatedResumeChunk(string normalized) =>
        normalized.HasPrefix("no ") ||
        normalized.HasPrefix("not ") ||
        normalized.HasPrefix("none ") ||
        normalized.Contains("no experience", StringComparison.Ordinal) ||
        normalized.Contains("not used", StringComparison.Ordinal) ||
        normalized.Contains("haven't used", StringComparison.Ordinal) ||
        normalized.Contains("have not used", StringComparison.Ordinal);

    private static string CompactResumePoint(string chunk)
    {
        var point = chunk.Trim();
        if (point.HasSuffix(".")) point = point.DropLast();
        if (point.Length > 110)
        {
            point = point.Prefix(107).TrimInline() + "...";
        }
        return point;
    }
}
