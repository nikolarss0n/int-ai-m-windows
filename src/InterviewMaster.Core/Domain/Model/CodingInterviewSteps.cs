using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

public sealed record CodingInterviewStep(int Number, string FileName, string Why, string Language, string Code);

/// What the interviewer has said about the current coding exercise. Speech arrives in fragments
/// ("write a function that gets a string and ... returns it reversed"), so the task is accumulated
/// across turns instead of being judged one utterance at a time.
public sealed record CodingSessionState
{
    /// Interviews talk about the code for a while before the next change; ten minutes of quiet ends the exercise.
    public const double FreshnessWindow = 600;
    public const int MaxTaskCharacters = 900;

    public DateTime? ArmedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public string Task { get; private set; } = "";

    public Guid? CardTurnId { get; private set; }

    /// The STEP blocks currently on the code panel. Follow-ups change this code instead of starting over.
    public string CurrentCode { get; private set; } = "";

    public bool IsFresh(DateTime now)
    {
        DateTime? last = null;
        if (ArmedAt is DateTime armed) last = armed;
        if (UpdatedAt is DateTime updated && (last is null || updated > last)) last = updated;
        return last is DateTime moment && (now - moment).TotalSeconds <= FreshnessWindow;
    }

    public void Arm(DateTime now) => ArmedAt = now;

    public void Begin(string task, Guid cardTurnId, DateTime now)
    {
        Task = Capped(task);
        CardTurnId = cardTurnId;
        UpdatedAt = now;
    }

    public void Refine(string task, DateTime now)
    {
        Task = Capped(task);
        UpdatedAt = now;
    }

    /// A spoken change to the code on screen ("write it in TypeScript") opens its own card.
    public void ApplyChange(string instruction, Guid cardTurnId, DateTime now)
    {
        Task = Capped(Task + " Then: " + instruction);
        CardTurnId = cardTurnId;
        UpdatedAt = now;
    }

    public void Store(string code) => CurrentCode = code;

    /// Keeps the exercise fresh without touching its task, for a retry of the request that built it.
    public void Touch(DateTime now) => UpdatedAt = now;

    public void Clear()
    {
        ArmedAt = null;
        UpdatedAt = null;
        Task = "";
        CardTurnId = null;
        CurrentCode = "";
    }

    /// A copy, where the Swift source relies on the struct being copied on assignment.
    public CodingSessionState Copy() => this with { };

    /// Keeps the most recent words when the spoken task grows past the cap.
    private static string Capped(string task)
    {
        var trimmed = task.Trim();
        return trimmed.Length > MaxTaskCharacters ? trimmed.Suffix(MaxTaskCharacters) : trimmed;
    }
}

/// What the code panel needs to know when code starts streaming in.
/// <param name="TurnId">The answer card this code belongs to. The panel keeps one version of the code per card.</param>
/// <param name="Request">What was asked, shown under the panel title.</param>
/// <param name="IsRefinement">A dictated detail that updates the version in place instead of adding one.</param>
/// <param name="AllowsChanges">Spoken exercises can be changed from the panel; a screenshot analysis cannot.</param>
public sealed record CodingTaskInfo(Guid TurnId, string Request, bool IsRefinement, bool AllowsChanges);

/// `Reason` is set when the code stopped early: a dropped connection or the length limit. The panel
/// then offers a retry.
public readonly record struct CodingTaskOutcome(bool IsComplete, string? Reason)
{
    public static readonly CodingTaskOutcome Complete = new(true, null);

    public static CodingTaskOutcome Incomplete(string reason) => new(false, reason);
}

/// One-click changes on the code panel. Each is worded like the request an interviewer would speak,
/// so a click and a spoken request take the same path.
public enum CodeQuickAction
{
    AnotherWay,
    Optimize,
    AddTests,
    OtherLanguage
}

public static class CodeQuickActions
{
    public static readonly IReadOnlyList<CodeQuickAction> AllCases = new[]
    {
        CodeQuickAction.AnotherWay, CodeQuickAction.Optimize, CodeQuickAction.AddTests, CodeQuickAction.OtherLanguage
    };

    /// The language a click on "Use <language>" switches to, given the language of the code on screen.
    public static string OtherLanguage(string? current) => current switch
    {
        "Python" => "TypeScript",
        "TypeScript" => "Python",
        "JavaScript" => "TypeScript",
        "Java" => "Python",
        "Kotlin" => "Java",
        "C#" => "Java",
        null => "TypeScript",
        _ => "Python"
    };

    public static string Title(this CodeQuickAction action, string? currentLanguage) => action switch
    {
        CodeQuickAction.AnotherWay => "Try another way",
        CodeQuickAction.Optimize => "Optimize",
        CodeQuickAction.AddTests => "Add tests",
        _ => $"Use {OtherLanguage(currentLanguage)}"
    };

    public static string Instruction(this CodeQuickAction action, string? currentLanguage) => action switch
    {
        CodeQuickAction.AnotherWay => "Show me another way to do it",
        CodeQuickAction.Optimize => "Optimize it",
        CodeQuickAction.AddTests => "Add tests for it",
        _ => $"Write it in {OtherLanguage(currentLanguage)}"
    };
}

public enum CodingTurnKind
{
    /// A coding exercise is starting ("let's start a new Python file") but no task is stated yet.
    ArmSession,

    /// "Write this code": the task is on the screen, not in the words. No model call.
    NeedsScreenContext,

    /// A fresh task. The text is what the interviewer just said.
    NewTask,

    /// More detail for the task in progress. The text is the whole accumulated task.
    RefineTask,

    /// A change to the code already on the panel. The text is what the interviewer just said.
    ChangeCode
}

public readonly record struct CodingTurnDecision(CodingTurnKind Kind, string Text = "")
{
    public static readonly CodingTurnDecision ArmSession = new(CodingTurnKind.ArmSession);
    public static readonly CodingTurnDecision NeedsScreenContext = new(CodingTurnKind.NeedsScreenContext);

    public static CodingTurnDecision NewTask(string text) => new(CodingTurnKind.NewTask, text);

    public static CodingTurnDecision RefineTask(string text) => new(CodingTurnKind.RefineTask, text);

    public static CodingTurnDecision ChangeCode(string text) => new(CodingTurnKind.ChangeCode, text);
}

/// Steps read from a reply that may still be streaming.
/// <param name="LastStepIsOpen">The last step has no closing fence yet, so its code is still growing.</param>
public sealed record CodingStepProgress(ValueList<CodingInterviewStep> Steps, bool LastStepIsOpen);

/// <param name="SayBullets">The talking points the candidate says before typing.</param>
/// <param name="StepsMarkdown">Everything from the first STEP onward.</param>
public sealed record CodingResponse(ValueList<string> SayBullets, string StepsMarkdown);

/// The free functions of `Domain/Model/CodingInterviewSteps.swift`.
public static class CodingSteps
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// True when the interviewer asks the candidate to write code ("write a function that reverses a
    /// string", "implement an LRU cache", "let's do some live coding"). Conceptual questions such as
    /// "what is FastAPI?" and candidate narration such as "I wrote a service" are not requests.
    public static bool IsImplementationCodingRequest(string text) => CodingRequestDetector.IsTaskRequest(text);

    /// `previousQuestion` is the interviewer's last question before this one: "show me that in code" is
    /// about it, so it becomes the task.
    public static CodingTurnDecision? TurnDecision(string text, CodingSessionState session, DateTime now, string? previousQuestion = null)
    {
        var hasCodeOnScreen = session.IsFresh(now) && session.CurrentCode.Length > 0;
        // "Write tests for it" and "write it in TypeScript" change the code that is there; they do not start a task.
        if (hasCodeOnScreen && CodingRequestDetector.FollowUp(text) == CodingFollowUp.Change)
        {
            return CodingTurnDecision.ChangeCode(text);
        }
        if (IsImplementationCodingRequest(text))
        {
            return CodingRequestDetector.IsDeicticRequest(text)
                ? CodingTurnDecision.NeedsScreenContext
                : CodingTurnDecision.NewTask(text);
        }
        if (CodingRequestDetector.AsksForCodeExample(text))
        {
            var subject = previousQuestion?.Trim() ?? "";
            if (CodingRequestDetector.CodeExampleNamesItsSubject(text) || subject.Length == 0)
            {
                return CodingTurnDecision.NewTask(text);
            }
            return CodingTurnDecision.NewTask($"{subject} {text}");
        }
        if (CodingRequestDetector.StartsCodingSession(text)) return CodingTurnDecision.ArmSession;
        if (!session.IsFresh(now)) return null;
        if (CodingRequestDetector.FollowUp(text) == CodingFollowUp.Example) return CodingTurnDecision.NewTask(text);
        if (!CodingRequestDetector.LooksLikeTaskDetail(text)) return null;
        if (session.Task.Length == 0) return CodingTurnDecision.NewTask(text);
        return CodingTurnDecision.RefineTask(session.Task + " " + text);
    }

    // MARK: - Prompt

    /// `spoken` is for a task taken from live speech: it is noisy and fragmentary, and the reply opens
    /// with a few lines the candidate can say aloud before typing. Screenshot analysis keeps the
    /// original prompt (`spoken == false`).
    ///
    /// `currentCode` is the STEP blocks already on the panel. With a `changeRequest` the model must apply
    /// that request to the code instead of starting over ("write it in TypeScript", "another way").
    /// Without one, the code is a draft written from an earlier, shorter version of the dictated task.
    public static string Prompt(string language, string task, bool spoken = false, string? currentCode = null, string? changeRequest = null)
    {
        var code = currentCode?.Trim() ?? "";
        var request = changeRequest?.Trim() ?? "";
        var isChange = spoken && code.Length > 0 && request.Length > 0;

        string taskBlock;
        if (isChange)
        {
            taskBlock = $"ORIGINAL TASK:\n{task}\n\nCURRENT CODE (already on the candidate's screen):\n{code}\n\n"
                + $"CHANGE REQUEST (raw speech-to-text of what the interviewer just said):\n{request}";
        }
        else if (spoken && code.Length > 0)
        {
            taskBlock = $"TASK:\n{task}\n\n"
                + $"CURRENT CODE (written from an earlier, shorter version of the task; update it to fit the full TASK):\n{code}";
        }
        else
        {
            taskBlock = $"TASK:\n{task}";
        }

        string speechNote;
        if (isChange)
        {
            speechNote = "\nApply the CHANGE REQUEST to the CURRENT CODE and output the FULL updated solution in the format below, replacing the current code. Keep what still fits and change only what the request needs. Keep the current language unless the request asks for another one; when it does, rewrite everything in that language and tag every code fence with it. If it asks for another way, use a genuinely different approach, not a cosmetic rewrite. If it asks to improve or optimize, make a real improvement and say what changed in SAY. The request is speech-to-text and may be garbled or partial: infer what the interviewer most likely wants. The simplicity rules below still apply unless the request says otherwise.\n";
        }
        else if (spoken)
        {
            speechNote = "\nThe TASK is raw speech-to-text from a live interview. It may be fragmented, may include the candidate's own words, and may mishear terms (\"PYTOM\" is Python). Infer the single most likely coding task. When details conflict, trust the latest. If the task is still underspecified, take the simplest reasonable reading and say the assumption in SAY. Use "
                + language
                + " unless the task names another language, and tag the code fences with the language actually used.\n";
        }
        else
        {
            speechNote = "";
        }

        var sayLine = isChange
            ? "- 2-3 short lines the candidate says out loud before typing: what changes and why, and the new time and space complexity if it changed."
            : "- 2-3 short lines the candidate says out loud before typing: restate the task, name one assumption, give time and space complexity.";
        var sayBlock = spoken ? $"SAY:\n{sayLine}\n\n" : "";
        var stepRule = spoken
            ? "Split into 1-6 steps the candidate can type in order, each at most about 6 lines and a whole unit: the imports, one function or one method. Never cut a function in the middle unless it is longer than 10 lines. Use ONE step when the whole solution is 6 lines or fewer."
            : "Split into 2-5 steps the candidate can type in order.";
        var preambleRule = spoken
            ? "- No preamble before SAY. No recap after the last step. SAY lines start with \"- \" and stay under 90 characters."
            : "- No preamble before STEP 1. No recap after the last step.";
        var voice = spoken ? $"For SAY and Why prose only:\n{AnswerPromptBlocks.CandidateVoice}\n" : "";

        return $"""
            You are a live coding interview copilot. The candidate must type this while speaking.

            {taskBlock}
            {speechNote}{voice}
            OUTPUT FORMAT — follow exactly:

            {sayBlock}STEP 1 — file: relative/path.ext
            Why: one short line of what this chunk does.
            ```{language}
            code to type now
            ```

            STEP 2 — file: relative/path.ext
            Why: one short line.
            ```{language}
            next chunk (may be the SAME file)
            ```

            RULES:
            - {stepRule}
            - Prefer small chunks even in the same file (imports, app setup, one route, a test).
            - Each Why is one sentence, under 80 characters.
            - Keep every code line under 80 characters, so it reads without scrolling sideways.
            {preambleRule}
            - Real runnable code. No TODO / pass placeholders unless the task is tiny.

            SIMPLEST CODE ONLY:
            - The easiest thing a mid-level engineer would type in 2 minutes. Boring on purpose.
            - One file when that is enough (usually main.py). Extra files only if they asked.
            - No classes unless required. No ABC/Protocol, DI containers, factories, middleware stacks, auth, Docker, or CI.
            - FastAPI: `FastAPI()`, a Pydantic model if there is a body, one route, `return` a dict. Skip Depends, routers, lifespan, background tasks, Redis, and vector DBs unless the task names them.
            - Python literals: True, False, None — never JSON true/false/null.
            - Prefer stdlib + FastAPI/Pydantic. No LangChain/LangGraph wrappers for a basic endpoint.
            - Short names. No type gymnastics. No comments except `#` when a line is not obvious.
            - Algorithm tasks: the obvious loop or dict. No clever one-liners.
            """;
    }

    // MARK: - Reading the reply

    public static string Format(IEnumerable<CodingInterviewStep> steps) =>
        string.Join("\n\n", steps.Select(step =>
            $"STEP {step.Number} — file: {step.FileName}\nWhy: {step.Why}\n```{step.Language}\n{step.Code}\n```"));

    private static readonly string[] ForbiddenTokens =
    {
        "depends", "apirouter", "langchain", "langgraph", "docker", "dockerfile"
    };

    /// Tokens that mean the snippet is too heavy for a live typed interview answer.
    public static List<string> SimplicityViolations(IEnumerable<CodingInterviewStep> steps)
    {
        var blob = string.Join("\n", steps.Select(step => $"{step.FileName} {step.Why} {step.Code}")).Lowercased();
        return ForbiddenTokens
            .Where(token => Regex.IsMatch(blob, $"(^|[^a-z0-9]){Regex.Escape(token)}($|[^a-z0-9])", RegexOptions.CultureInvariant))
            .ToList();
    }

    /// Drops blank lines around a code block. The first line keeps its indentation: a chunk that continues
    /// a class or function body starts indented, and pasting it flush left would break the file.
    public static string TrimmedCodeBlock(string code)
    {
        var lines = code.SplitKeepingEmpty('\n');
        while (lines.Count > 0 && lines[0].TrimInline().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].TrimInline().Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static readonly Regex StepBlock = new(
        @"STEP\s+(\d+)\s*[—\-]\s*file:\s*(\S+)\s*\nWhy:\s*(.+)\n```(\w+)?\n([\s\S]*?)```",
        Options);

    public static List<CodingInterviewStep> Parse(string markdown)
    {
        var steps = new List<CodingInterviewStep>();
        foreach (Match match in StepBlock.Matches(markdown))
        {
            var number = int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 0;
            var language = match.Groups[4].Success ? match.Groups[4].Value : "text";
            steps.Add(new CodingInterviewStep(
                number,
                match.Groups[2].Value,
                match.Groups[3].Value.Trim(),
                language,
                TrimmedCodeBlock(match.Groups[5].Value)));
        }
        return steps;
    }

    private static readonly Regex StepHeader = new(
        @"^\s*STEP\s+(\d+)\s*[—–:\-]?\s*(?:file:)?\s*(\S+)?",
        Options | RegexOptions.IgnoreCase);

    /// Swift `CharacterSet.newlines`.
    private static bool IsNewline(char c) => c is '\n' or '\r' or '\v' or '\f' or (char)0x85 or (char)0x2028 or (char)0x2029;

    /// Reads STEP blocks line by line so a half-streamed step shows up as soon as its code starts.
    public static CodingStepProgress ParseStreaming(string markdown)
    {
        var steps = new List<CodingInterviewStep>();
        CodingInterviewStep? current = null;
        var codeLines = new List<string>();
        var inFence = false;

        void FinishCurrent()
        {
            if (current is null) return;
            steps.Add(current with { Code = TrimmedCodeBlock(string.Join("\n", codeLines)) });
            current = null;
            codeLines = new List<string>();
        }

        foreach (var rawLine in markdown.SplitKeepingEmpty('\n'))
        {
            var line = rawLine.TrimWhere(IsNewline);
            if (inFence)
            {
                if (line.TrimInline() == "```")
                {
                    inFence = false;
                }
                else
                {
                    codeLines.Add(line);
                }
                continue;
            }
            var match = StepHeader.Match(line);
            if (match.Success)
            {
                FinishCurrent();
                var number = int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : steps.Count + 1;
                var fileName = match.Groups[2].Success ? match.Groups[2].Value : "main";
                current = new CodingInterviewStep(number, fileName, "", "text", "");
                continue;
            }
            if (current is null) continue;
            if (line.HasPrefix("```"))
            {
                inFence = true;
                var language = line.DropFirst(3).TrimInline();
                if (language.Length > 0) current = current with { Language = language };
                continue;
            }
            if (line.Lowercased().HasPrefix("why:"))
            {
                current = current with { Why = line.DropFirst(4).TrimInline() };
            }
        }
        var lastStepIsOpen = inFence || (current is not null && codeLines.Count == 0);
        FinishCurrent();
        return new CodingStepProgress(steps, lastStepIsOpen);
    }

    /// Display name for a code fence tag ("ts" becomes "TypeScript"), or null when the tag is unknown.
    public static string? LanguageDisplayName(string fence) => fence.Lowercased() switch
    {
        "python" or "py" => "Python",
        "typescript" or "ts" or "tsx" => "TypeScript",
        "javascript" or "js" or "jsx" or "node" or "nodejs" => "JavaScript",
        "java" => "Java",
        "kotlin" or "kt" => "Kotlin",
        "go" or "golang" => "Go",
        "rust" or "rs" => "Rust",
        "swift" => "Swift",
        "csharp" or "cs" or "c#" => "C#",
        "cpp" or "c++" => "C++",
        "c" => "C",
        "ruby" or "rb" => "Ruby",
        "php" => "PHP",
        "scala" => "Scala",
        "sql" => "SQL",
        "bash" or "sh" or "shell" or "zsh" => "Shell",
        _ => null
    };

    private static readonly Regex FirstStepLine = new(
        @"^[ \t]*STEP\s+\d+",
        Options | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly string[] BulletMarkers = { "- ", "* ", "• " };

    /// Splits a spoken-task reply into the SAY lines (shown on the answer card) and the STEP blocks
    /// (shown in the code panel).
    public static CodingResponse SplitResponse(string text)
    {
        var firstStep = FirstStepLine.Match(text);
        var sayText = firstStep.Success ? text.Substring(0, firstStep.Index) : text;
        var steps = firstStep.Success ? text.Substring(firstStep.Index) : "";

        var bullets = new List<string>();
        foreach (var rawLine in sayText.SplitKeepingEmpty('\n'))
        {
            var line = rawLine.TrimInline();
            var marker = BulletMarkers.FirstOrDefault(line.HasPrefix);
            if (marker is null) continue;
            var bullet = line.DropFirst(marker.Length).TrimInline();
            if (bullet.Length > 0) bullets.Add(bullet);
        }
        return new CodingResponse(bullets, steps);
    }

    /// The first thing to say from an answer, for the one line above the code.
    public static string? FirstTalkingPoint(string answer)
    {
        foreach (var rawLine in answer.SplitKeepingEmpty('\n'))
        {
            var line = rawLine.TrimInline();
            if (line.HasPrefix("- ")) line = line.DropFirst(2).TrimInline();
            if (line.Length > 0) return line;
        }
        return null;
    }

    /// The answer card text that goes with the code panel.
    public static string CardBullets(CodingResponse response, int stepCount, bool isComplete)
    {
        var lines = response.SayBullets.Select(bullet => $"- {bullet}").ToList();
        // With talking points on the card the open panel speaks for itself; an empty card still points to it.
        if (lines.Count > 0 || !isComplete || stepCount <= 0) return string.Join("\n", lines);
        lines.Add($"- Code is in the code panel ({stepCount} step{(stepCount == 1 ? "" : "s")}).");
        return string.Join("\n", lines);
    }
}
