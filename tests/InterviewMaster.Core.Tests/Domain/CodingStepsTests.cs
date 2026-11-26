using InterviewMaster.Domain;

namespace InterviewMaster.Tests.Domain;

/// From `Tests/test_processor.swift`: the "coding interview steps", "coding requests", "code panel"
/// and "Code file sections" / "Code line changes" sections.
public class CodingStepsTests : SwiftStyleTests
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string StepsPrompt = CodingSteps.Prompt("python", "Write a FastAPI health service");

    private static readonly string SampleSteps = CodingSteps.Format(new[]
    {
        new CodingInterviewStep(1, "main.py", "Create the FastAPI app.", "python", "from fastapi import FastAPI\napp = FastAPI()"),
        new CodingInterviewStep(2, "main.py", "Add a health route.", "python", "@app.get(\"/health\")\ndef health():\n    return {\"ok\": True}")
    });

    [Fact]
    public void CodingInterviewSteps()
    {
        Check(CodingSteps.IsImplementationCodingRequest("Can you write a FastAPI service that exposes a RAG query endpoint?"), "Write a FastAPI service is an implementation request");
        Check(CodingSteps.IsImplementationCodingRequest("Implement a FastAPI service with a health route"), "Implement a FastAPI service is an implementation request");
        Check(CodingSteps.IsImplementationCodingRequest("Implement an endpoint that accepts a question and top_k"), "Implement an endpoint is an implementation request");
        Check(!CodingSteps.IsImplementationCodingRequest("What is FastAPI?"), "What-is FastAPI is not an implementation request");
        Check(!CodingSteps.IsImplementationCodingRequest("How does FastAPI dependency injection work?"), "Conceptual FastAPI question is not an implementation request");
        Check(StepsPrompt.Contains("STEP 1"), "Coding prompt asks for numbered steps");
        Check(StepsPrompt.Contains("file:"), "Coding prompt asks for a file name per step");
        Check(StepsPrompt.Contains("Why:"), "Coding prompt asks for a concise why line");
        Check(StepsPrompt.Contains("same file") || StepsPrompt.Contains("SAME file"), "Coding prompt allows splitting the same file");
        Check(StepsPrompt.Contains("SIMPLEST CODE ONLY"), "Coding prompt demands the easiest typed solution");
        Check(StepsPrompt.Contains("Boring on purpose"), "Coding prompt forbids over-engineered solutions");
        Check(StepsPrompt.Contains("Skip Depends"), "FastAPI steps stay on the boring happy path");
        Check(StepsPrompt.Contains("True, False, None"), "Python steps must use Python literals, not JSON true/false");
        Check(!StepsPrompt.Contains('\r'), "The prompt has Unix line endings on every platform");
        Check(StepsPrompt.StartsWith("You are a live coding interview copilot. The candidate must type this while speaking.\n\nTASK:\nWrite a FastAPI health service\n\nOUTPUT FORMAT — follow exactly:\n\nSTEP 1 — file: relative/path.ext\n", StringComparison.Ordinal), "The screenshot prompt opens exactly as the macOS prompt does");

        var parsedSteps = CodingSteps.Parse(SampleSteps);
        Check(parsedSteps.Count == 2, "Parser keeps two steps even in the same file");
        Check(parsedSteps[0].FileName == "main.py" && parsedSteps[1].FileName == "main.py", "Same-file steps keep the file name");
        Check(parsedSteps[0].Code.Contains("FastAPI") && parsedSteps[1].Code.Contains("/health"), "Each step keeps its own snippet");
        Check(parsedSteps[0].Why.Contains("app") && parsedSteps[1].Why.Contains("health"), "Each step keeps a concise why");
        Check(parsedSteps.All(step => step.Why.Length <= 80), "Why lines stay glanceable");
        var joinedCode = string.Join("\n", parsedSteps.Select(step => step.Code)).ToLowerInvariant();
        Check(!joinedCode.Contains("depends"), "Simplest FastAPI fixture has no Depends");
        Check(!joinedCode.Contains("langchain"), "Simplest FastAPI fixture has no LangChain");
        Check(!joinedCode.Contains("docker"), "Simplest FastAPI fixture has no Docker");
        Check(CodingSteps.SimplicityViolations(parsedSteps).Count == 0, "Shipped simplicity checker accepts the boring FastAPI walkthrough");
        var heavySteps = new[]
        {
            new CodingInterviewStep(1, "app.py", "Wire a router.", "python",
                "from fastapi import FastAPI, Depends, APIRouter\nfrom langchain_core.prompts import ChatPromptTemplate")
        };
        var heavyHits = CodingSteps.SimplicityViolations(heavySteps);
        Check(heavyHits.Any(hit => hit.Contains("depends")), "Shipped checker flags Depends scaffolding");
        Check(heavyHits.Any(hit => hit.Contains("langchain")), "Shipped checker flags LangChain scaffolding");
    }

    [Fact]
    public void Detection()
    {
        var codingRequests = new[]
        {
            "Can you write a FastAPI service that exposes a RAG query endpoint?",
            "Write a function that reverses a string",
            "Okay, so you wanna do it in Python, it's fine. So, just wanna write a function, that gets a string and",
            "Now implement an LRU cache",
            "Can you code a binary search?",
            "Write a SQL query that finds duplicate emails",
            "Could you please create a class for a bank account",
            "Please write a script that counts words in a file",
            "I want you to write a function that checks for palindromes"
        };
        foreach (var request in codingRequests)
        {
            Check(CodingSteps.IsImplementationCodingRequest(request), $"Coding request: {request}");
        }
        var notCodingRequests = new[]
        {
            "What is FastAPI?", "How does FastAPI dependency injection work?",
            "I wrote a function that reverses a string", "We implemented a service for orders last year",
            "Do you write unit tests for your APIs?", "How would you implement an LRU cache?",
            "Explain how to write a good unit test", "What is a linked list?",
            "Tell me about the service you built", "Let's start a new Python file",
            "I did some live coding at my last job"
        };
        foreach (var text in notCodingRequests)
        {
            Check(!CodingSteps.IsImplementationCodingRequest(text), $"Not a coding request: {text}");
        }
        Check(CodingRequestDetector.StartsCodingSession("Okay, let's start a new Python file."), "New file starts a coding session");
        Check(CodingRequestDetector.StartsCodingSession("Can we share your screen and do some live coding?"), "Screen share plus live coding starts a session");
        Check(CodingRequestDetector.StartsCodingSession("We have a coding exercise for you"), "Announcing an exercise starts a session");
        Check(!CodingRequestDetector.StartsCodingSession("I did some live coding at my last job"), "Candidate history does not start a session");
        Check(CodingRequestDetector.IsDeicticRequest("Write this code"), "'Write this code' points at the screen");
        Check(CodingRequestDetector.IsDeicticRequest("Can you solve this problem?"), "'Solve this problem' points at the screen");
        Check(!CodingRequestDetector.IsDeicticRequest("Write a function that reverses a string"), "A named task is not deictic");
        Check(!CodingRequestDetector.IsDeicticRequest("Write some code to reverse a string"), "Detail words make it a real task");
    }

    [Fact]
    public void SessionAccumulation()
    {
        var sessionStart = Epoch.AddSeconds(1_000);
        var codingSession = new CodingSessionState();
        Check(CodingSteps.TurnDecision("Let's start a new Python file", codingSession, sessionStart) == CodingTurnDecision.ArmSession, "New file arms the session");
        codingSession.Arm(sessionStart);
        Check(CodingSteps.TurnDecision("Write this code", codingSession, sessionStart) == CodingTurnDecision.NeedsScreenContext, "Deictic request asks for the screen");
        const string firstRequest = "Okay, so you wanna do it in Python. So, just wanna write a function, that gets a string and";
        Check(CodingSteps.TurnDecision(firstRequest, codingSession, sessionStart) == CodingTurnDecision.NewTask(firstRequest), "A spoken task starts fresh");
        codingSession.Begin(firstRequest, Guid.NewGuid(), sessionStart.AddSeconds(5));
        const string detail = "like text, return string";
        Check(CodingSteps.TurnDecision(detail, codingSession, sessionStart.AddSeconds(60)) == CodingTurnDecision.RefineTask(firstRequest + " " + detail), "A fragment refines the task in progress");
        Check(CodingSteps.TurnDecision("Not sure if there was reverse here to be honest.", codingSession, sessionStart.AddSeconds(90)) is not null, "'reverse' is task detail");
        Check(CodingSteps.TurnDecision("that is doing what?", codingSession, sessionStart.AddSeconds(70)) is null, "A question is not task detail");
        Check(CodingSteps.TurnDecision("What is the difference between a list and a tuple?", codingSession, sessionStart.AddSeconds(70)) is null, "A concept question stays a cue card");
        Check(CodingSteps.TurnDecision("I usually return a list of strings from these helpers in my projects", codingSession, sessionStart.AddSeconds(70)) is null, "Candidate speech is never task detail");
        Check(CodingSteps.TurnDecision(detail, codingSession, sessionStart.AddSeconds(CodingSessionState.FreshnessWindow + 30)) is null, "A stale session no longer collects detail");
        var armedOnly = new CodingSessionState();
        armedOnly.Arm(sessionStart);
        Check(CodingSteps.TurnDecision("Given a string, return its reverse", armedOnly, sessionStart.AddSeconds(20)) == CodingTurnDecision.NewTask("Given a string, return its reverse"), "In an armed session the first detail becomes the task");
        Check(CodingSteps.TurnDecision("Given a string, return its reverse", new CodingSessionState(), sessionStart) is null, "Without a session, detail-like speech is not a coding task");
        var longSession = new CodingSessionState();
        longSession.Begin(string.Concat(Enumerable.Repeat("word ", 400)), Guid.NewGuid(), sessionStart);
        Check(longSession.Task.Length <= CodingSessionState.MaxTaskCharacters, "Accumulated task stays bounded");
    }

    [Fact]
    public void FollowUpsWhileCodeIsOnScreen()
    {
        var codeChanges = new[]
        {
            "Okay, now can you write it in TypeScript?", "Write it in Java", "Can you do it in Rust?", "In TypeScript please",
            "How would this look in Go?", "improve this reverse function and for example", "Can you optimize it?",
            "Make it faster", "Refactor this", "Add error handling", "Handle empty strings", "Add a unit test for it",
            "Write tests for it", "Is there another way in Python to reverse it?", "Show me a different approach",
            "Can you do it without slicing?", "How about using recursion?", "Use a loop instead",
            "What's another way to reverse it?"
        };
        foreach (var request in codeChanges)
        {
            Check(CodingRequestDetector.FollowUp(request) == CodingFollowUp.Change, $"Changes the code on screen: {request}");
        }
        var techniqueExamples = new[]
        {
            "Can you show me sliding window technique?", "Okay, how you use two pointers?", "Show me binary search",
            "Give me an example of memoization", "How would you use dynamic programming here?"
        };
        foreach (var request in techniqueExamples)
        {
            Check(CodingRequestDetector.FollowUp(request) == CodingFollowUp.Example, $"Asks for a technique example: {request}");
        }
        var plainQuestions = new[]
        {
            "What is polymorphism?", "Why did you use slicing?", "What is the time complexity?",
            "How would you improve the performance of the database?", "Explain how two pointers work",
            "How does sliding window work?", "I improved the function last week and used TypeScript",
            "We use TypeScript for the frontend", "What is the difference between Python and TypeScript?",
            "Do you have experience with TypeScript?", "Tell me about your experience with Python",
            "Can you explain it in Python?", "Walk me through the code", "Why would you use a dictionary here?",
            "What does this function return?"
        };
        foreach (var question in plainQuestions)
        {
            Check(CodingRequestDetector.FollowUp(question) is null, $"Stays an ordinary question: {question}");
        }

        var followUpStart = Epoch.AddSeconds(5_000);
        var liveSession = new CodingSessionState();
        liveSession.Begin("Write me a reverse function in Python.", Guid.NewGuid(), followUpStart);
        Check(CodingSteps.TurnDecision("Okay, now can you write it in TypeScript?", liveSession, followUpStart.AddSeconds(20)) is null, "Without code on the panel a language change has nothing to change");
        liveSession.Store("STEP 1 — file: main.py\nWhy: Slice.\n```python\ndef reverse(text):\n    return text[::-1]\n```");
        Check(CodingSteps.TurnDecision("Okay, now can you write it in TypeScript?", liveSession, followUpStart.AddSeconds(20)) == CodingTurnDecision.ChangeCode("Okay, now can you write it in TypeScript?"), "A language change applies to the code on screen");
        Check(CodingSteps.TurnDecision("Write tests for it", liveSession, followUpStart.AddSeconds(20)) == CodingTurnDecision.ChangeCode("Write tests for it"), "Tests for the code on screen change it instead of starting a task");
        Check(CodingSteps.TurnDecision("Is there another way in Python to reverse it?", liveSession, followUpStart.AddSeconds(45)) == CodingTurnDecision.ChangeCode("Is there another way in Python to reverse it?"), "Another way changes the code on screen");
        Check(CodingSteps.TurnDecision("Can you show me sliding window technique?", liveSession, followUpStart.AddSeconds(60)) == CodingTurnDecision.NewTask("Can you show me sliding window technique?"), "A technique example replaces the panel with a new task");
        Check(CodingSteps.TurnDecision("Write a function that checks for palindromes", liveSession, followUpStart.AddSeconds(60)) == CodingTurnDecision.NewTask("Write a function that checks for palindromes"), "A new spoken task is still a new task");
        Check(CodingSteps.TurnDecision("What is polymorphism?", liveSession, followUpStart.AddSeconds(60)) is null, "A concept question in the middle of the exercise stays a cue card");
        Check(CodingSteps.TurnDecision("Okay, now can you write it in TypeScript?", liveSession, followUpStart.AddSeconds(CodingSessionState.FreshnessWindow + 30)) is null, "An old exercise no longer collects changes");
        liveSession.ApplyChange("write it in TypeScript", Guid.NewGuid(), followUpStart.AddSeconds(30));
        Check(liveSession.Task.EndsWith("Then: write it in TypeScript", StringComparison.Ordinal) && liveSession.CurrentCode.Contains("def reverse"), "A change extends the task and keeps the code until new code replaces it");
        var taskBeforeTouch = liveSession.Task;
        liveSession.Touch(followUpStart.AddSeconds(400));
        Check(liveSession.Task == taskBeforeTouch && liveSession.IsFresh(followUpStart.AddSeconds(500)), "A retry keeps the task but keeps the exercise fresh");
        liveSession.Clear();
        Check(liveSession.CurrentCode.Length == 0 && liveSession.Task.Length == 0, "Clearing forgets the code");
        Check(liveSession == new CodingSessionState(), "A cleared session equals a new one");

        var changePrompt = CodingSteps.Prompt(
            "python", "Write me a reverse function in Python.", spoken: true,
            currentCode: "STEP 1 — file: main.py\nWhy: Slice.\n```python\ndef reverse(text):\n    return text[::-1]\n```",
            changeRequest: "Okay, now can you write it in TypeScript?");
        Check(changePrompt.Contains("CURRENT CODE (already on the candidate's screen)") && changePrompt.Contains("def reverse(text)"), "A change request shows the model the code on screen");
        Check(changePrompt.Contains("CHANGE REQUEST") && changePrompt.Contains("write it in TypeScript"), "A change request carries what was just said");
        Check(changePrompt.Contains("rewrite everything in that language") && changePrompt.Contains("genuinely different approach"), "Language changes and alternatives are spelled out");
        Check(changePrompt.Contains("what changes and why"), "The talking points describe the change");
        Check(changePrompt.Contains("STEP 1") && changePrompt.Contains("SIMPLEST CODE ONLY"), "A change keeps the stepped format");
        var dictationPrompt = CodingSteps.Prompt("python", "write a function that gets a string and returns it reversed", spoken: true, currentCode: "STEP 1 — file: main.py\nWhy: x\n```python\nx = 1\n```");
        Check(dictationPrompt.Contains("written from an earlier, shorter version of the task"), "Dictation refinements update the draft that is there");
        Check(!CodingSteps.Prompt("python", "x", spoken: true).Contains("CURRENT CODE"), "A first task has no code to show");
        Check(!CodingSteps.Prompt("python", "x", currentCode: "code", changeRequest: "in Java").Contains("CHANGE REQUEST"), "Screenshot analysis never gets change requests");
    }

    [Fact]
    public void QuickActions()
    {
        var followUpStart = Epoch.AddSeconds(5_000);
        foreach (var action in CodeQuickActions.AllCases)
        {
            foreach (var language in new string?[] { "Python", "TypeScript", "Java", null })
            {
                var instruction = action.Instruction(language);
                Check(CodingRequestDetector.FollowUp(instruction) == CodingFollowUp.Change, $"A click and a spoken request are the same thing: {instruction}");
                Check(action.Title(language).Length > 0, "Every action has a label");
            }
        }
        // From a live run: an easier way is another way, and "in code" asks for code.
        var codeOnPanel = new CodingSessionState();
        codeOnPanel.Begin("Write me a Python function to reverse a string.", Guid.NewGuid(), followUpStart);
        codeOnPanel.Store("STEP 1 — file: main.py\nWhy: Loop.\n```python\ndef reverse(text):\n    return ''.join(reversed(text))\n```");
        foreach (var easier in new[] { "Is there some easier way?", "Is there a simpler way?", "Is there an easier way to do it?", "Any cleaner approach?", "Is there a more pythonic way?" })
        {
            Check(CodingSteps.TurnDecision(easier, codeOnPanel, followUpStart.AddSeconds(20)) == CodingTurnDecision.ChangeCode(easier), $"With code on screen, this changes it: {easier}");
            Check(CodingSteps.TurnDecision(easier, new CodingSessionState(), followUpStart) is null, $"With no code, this is an ordinary question: {easier}");
        }
        foreach (var request in new[] { "Can you show me that in code?", "Show me an example in code.", "Could you give me a code example?", "Okay, can you show it in code?" })
        {
            Check(CodingRequestDetector.AsksForCodeExample(request) && !CodingRequestDetector.CodeExampleNamesItsSubject(request), $"Asks for code and names nothing: {request}");
            Check(CodingSteps.TurnDecision(request, new CodingSessionState(), followUpStart, "Can you explain how sliding window works?")
                  == CodingTurnDecision.NewTask($"Can you explain how sliding window works? {request}"), $"The question before it becomes the task: {request}");
            Check(CodingSteps.TurnDecision(request, new CodingSessionState(), followUpStart) == CodingTurnDecision.NewTask(request), $"With nothing before it, it is still a coding request: {request}");
        }
        Check(CodingSteps.TurnDecision("Cache, can you show me example in code?", new CodingSessionState(), followUpStart, "What is sliding window?")
              == CodingTurnDecision.NewTask("Cache, can you show me example in code?"), "A request that names its subject keeps it, not the question before");
        Check(CodingRequestDetector.CodeExampleNamesItsSubject("Can you show me an LRU cache in code?"), "An LRU cache is a subject");
        foreach (var notARequest in new[] { "I wrote an example in code for that.", "We keep the examples in code review.", "How do you keep code examples up to date?", "What is a code smell?", "Explain the difference between REST and GraphQL." })
        {
            Check(CodingSteps.TurnDecision(notARequest, new CodingSessionState(), followUpStart, "What is REST?") is null, $"Not a request for code: {notARequest}");
        }
        Check(CodeQuickAction.OtherLanguage.Title("Python") == "Use TypeScript", "Python offers TypeScript");
        Check(CodeQuickAction.OtherLanguage.Title("TypeScript") == "Use Python", "TypeScript offers Python");
        Check(CodeQuickActions.AllCases.Select(action => action.Title("Python")).SequenceEqual(new[] { "Try another way", "Optimize", "Add tests", "Use TypeScript" }), "Every quick change is a button that starts with a verb");
        Check(CodeQuickAction.OtherLanguage.Instruction(null) == "Write it in TypeScript", "Unknown language offers TypeScript");
        Check(CodeQuickAction.OtherLanguage.Title("TypeScript") != CodeQuickAction.OtherLanguage.Title("Python"), "The offered language is never the current one");
        Check(CodingTaskOutcome.Incomplete("x") != CodingTaskOutcome.Complete && CodingTaskOutcome.Complete == new CodingTaskOutcome(true, null), "Outcomes compare");
    }

    [Fact]
    public void LanguageLabel()
    {
        Check(CodingSteps.LanguageDisplayName("typescript") == "TypeScript" && CodingSteps.LanguageDisplayName("TS") == "TypeScript", "TypeScript fences are named");
        Check(CodingSteps.LanguageDisplayName("py") == "Python" && CodingSteps.LanguageDisplayName("golang") == "Go", "Short tags map to display names");
        Check(CodingSteps.LanguageDisplayName("c#") == "C#" && CodingSteps.LanguageDisplayName("c++") == "C++", "Symbol tags map to display names");
        Check(CodingSteps.LanguageDisplayName("text") is null && CodingSteps.LanguageDisplayName("") is null, "Unknown tags fall back to the app setting");
        var tsFence = CodingSteps.ParseStreaming("STEP 1 — file: reverse.ts\nWhy: x\n```typescript\nconst a = 1\n```");
        Check(tsFence.Steps.Count > 0 && CodingSteps.LanguageDisplayName(tsFence.Steps[0].Language) == "TypeScript", "A parsed step carries the language of its fence");
    }

    private const string SpokenReply =
        "SAY:\n- Restating: take a string, return it reversed.\n- Assumption: empty string returns empty.\n- O(n) time, O(n) space.\n\n"
        + "STEP 1 — file: main.py\nWhy: Slice the string backwards.\n```python\ndef reverse(text):\n    return text[::-1]\n```";

    [Fact]
    public void PromptAndReplyParsing()
    {
        var spokenPrompt = CodingSteps.Prompt("python", "write a function that gets a string and", spoken: true);
        Check(spokenPrompt.Contains("SAY:") && spokenPrompt.Contains("PYTOM"), "Spoken prompt asks for talking points and warns about noisy speech");
        Check(spokenPrompt.Contains("STEP 1") && spokenPrompt.Contains("SIMPLEST CODE ONLY"), "Spoken prompt keeps the stepped format and simplicity rules");
        Check(spokenPrompt.Contains("\n\nSAY:\n- 2-3 short lines the candidate says out loud before typing: restate the task, name one assumption, give time and space complexity.\n\nSTEP 1 — file: relative/path.ext\n"), "The SAY block sits directly above the first step, as in the macOS prompt");
        var howTo = new (string Text, bool Expected)[]
        {
            ("How to reverse string in Python.", true), ("how do you sort a list in TypeScript?", true),
            ("So, how would you merge two dictionaries in Python?", true), ("reverse a string in Python", true),
            ("How does garbage collection work in Python?", false), ("How would you approach testing in Python?", false),
            ("How do you manage dependencies in Python?", false), ("How would you sort a list?", false),
            ("I read the config in Python last year", false)
        };
        foreach (var (text, expected) in howTo)
        {
            Check(CodingRequestDetector.AsksHowToInLanguage(text) == expected, $"How-to code request {expected} expected for: {text}");
        }
        Check(CodingSteps.TurnDecision("How to reverse string in Python.", new CodingSessionState(), DateTime.UtcNow) == CodingTurnDecision.NewTask("How to reverse string in Python."), "A how-to in a named language starts a coding task, not a screenshot prompt");
        Check(spokenPrompt.Contains("1-6 steps") && spokenPrompt.Contains("at most about 6 lines"), "A tiny function may be one step; longer code comes in short steps");
        Check(!StepsPrompt.Contains("SAY:"), "Screenshot analysis prompt is unchanged");

        var split = CodingSteps.SplitResponse(SpokenReply);
        Check(split.SayBullets.Count == 3 && split.SayBullets[0].StartsWith("Restating", StringComparison.Ordinal), "SAY lines become the answer card");
        Check(split.StepsMarkdown.StartsWith("STEP 1", StringComparison.Ordinal), "Steps start at the first STEP");
        Check(CodingSteps.Parse(split.StepsMarkdown).Count == 1, "The steps part parses on its own");
        var sayOnly = CodingSteps.SplitResponse("SAY:\n- Restating the task.");
        Check(sayOnly.SayBullets.SequenceEqual(new[] { "Restating the task." }) && sayOnly.StepsMarkdown.Length == 0, "SAY streams before any step exists");
        Check(CodingSteps.SplitResponse("STEP 1 — file: a.py\nWhy: x\n```python\nx = 1\n```").SayBullets.Count == 0, "A reply without SAY has no talking points");
        Check(!CodingSteps.CardBullets(split, 1, isComplete: true).Contains("Code is in the panel"), "Talking points need no pointer to the open panel");
        var emptyResponse = new CodingResponse(Array.Empty<string>(), "");
        Check(CodingSteps.CardBullets(emptyResponse, 1, isComplete: true).EndsWith("- Code is in the code panel (1 step).", StringComparison.Ordinal), "An empty card still points at the panel");
        Check(CodingSteps.FirstTalkingPoint("- Use two pointers\n- It is O(n)") == "Use two pointers", "The line above the code is the first bullet");
        Check(CodingSteps.FirstTalkingPoint("\n  \n") is null, "An empty answer has no talking point");
        Check(!CodingSteps.CardBullets(split, 1, isComplete: false).Contains("panel"), "Streaming card does not promise the panel early");
        Check(CodingSteps.CardBullets(emptyResponse, 0, isComplete: false).Length == 0, "No text means the card keeps its spinner");
    }

    [Fact]
    public void StreamingParser()
    {
        var parsedSteps = CodingSteps.Parse(SampleSteps);
        const string partial = "STEP 1 — file: main.py\nWhy: Create the app.\n```python\nfrom fastapi import FastAPI\napp = Fast";
        var partialProgress = CodingSteps.ParseStreaming(partial);
        Check(partialProgress.Steps.Count == 1 && partialProgress.LastStepIsOpen, "A half-typed step is visible and marked open");
        Check(partialProgress.Steps[0].FileName == "main.py" && partialProgress.Steps[0].Why == "Create the app.", "Partial step keeps file and why");
        Check(partialProgress.Steps[0].Code.EndsWith("app = Fast", StringComparison.Ordinal), "Partial code shows what has streamed");
        var fullProgress = CodingSteps.ParseStreaming(SampleSteps);
        Check(fullProgress.Steps.SequenceEqual(parsedSteps) && !fullProgress.LastStepIsOpen, "Streaming parser agrees with the strict parser on a finished reply");
        var headerOnlyProgress = CodingSteps.ParseStreaming("STEP 1 — file: main.py\nWhy: Start");
        Check(headerOnlyProgress.Steps.Count == 1 && headerOnlyProgress.LastStepIsOpen && headerOnlyProgress.Steps[0].Code.Length == 0, "A step header alone is an open step");
        Check(CodingSteps.ParseStreaming("SAY:\n- hi").Steps.Count == 0, "SAY alone has no steps");
        var twoOpen = CodingSteps.ParseStreaming("STEP 1 — file: a.py\nWhy: one\n```python\nx = 1\n```\nSTEP 2 — file: a.py\nWhy: two\n```python\ny = 2");
        Check(twoOpen.Steps.Count == 2 && twoOpen.LastStepIsOpen && twoOpen.Steps[0].Code == "x = 1", "A closed step followed by an open one");
    }

    [Fact]
    public void CodeFileSections()
    {
        var indentedBlock = CodingSteps.ParseStreaming("STEP 1 — file: a.py\nWhy: x\n```python\n\n    def run(self):\n        pass\n\n```");
        Check(indentedBlock.Steps[0].Code == "    def run(self):\n        pass", "A chunk keeps its first line's indentation");
        var fileSteps = new[]
        {
            new CodingInterviewStep(1, "a.py", "one", "python", "x = 1\ny = 2"),
            new CodingInterviewStep(2, "a.py", "two", "python", "    z = 3"),
            new CodingInterviewStep(3, "b.py", "three", "python", "w = 4"),
            new CodingInterviewStep(4, "a.py", "four", "python", "v = 5")
        };
        var fileSections = CodeFiles.GroupCodeSteps(fileSteps);
        Check(fileSections.Select(section => section.FileName).SequenceEqual(new[] { "a.py", "b.py", "a.py" }), "Only neighbouring steps merge, so typing order survives");
        Check(fileSections[0].Code == "x = 1\ny = 2\n\n    z = 3", "Chunks join with a blank line and keep their indentation");
        Check(fileSections[0].Chunks[1].FirstLine == 3, "Line numbers count the blank line, so they match the pasted file");
        Check(fileSections[2].Chunks[0].FirstLine == 0, "A new section numbers from its own first line");
        var repeatedSteps = new[]
        {
            new CodingInterviewStep(1, "a.py", "", "python", "def add(a, b):\n    return a + b"),
            new CodingInterviewStep(2, "a.py", "", "python", "def add(a, b):\n    return a + b\nprint(add(1, 2))")
        };
        var repeatedChunks = CodeFiles.GroupCodeSteps(repeatedSteps)[0].Chunks;
        Check(!repeatedChunks[0].RepeatsEarlierCode && repeatedChunks[1].RepeatsEarlierCode, "Repeated code is flagged, not silently removed");
        Check(repeatedChunks[1].Code.Contains("def add"), "A flagged chunk keeps its code");
        var streamingStep = new[] { new CodingInterviewStep(1, "a.py", "w", "text", "") };
        Check(CodeFiles.GroupCodeSteps(streamingStep)[0].Code.Length == 0, "A step with no code yet makes an empty file");
    }

    [Fact]
    public void CodeLineChanges()
    {
        const string oldCode = "a = 1\nb = 2\nc = 3\nd = 4\ne = 5";
        Check(Equals(CodeFiles.LineChanges(oldCode, oldCode), new CodeLineChanges()), "Identical code has no marks");
        var editedLine = CodeFiles.LineChanges(oldCode, "a = 1\nb = 9\nc = 3\nd = 4\ne = 5");
        Check(editedLine is not null && editedLine.Changed.SetEquals(new[] { 1 }) && editedLine.RemovedBefore.Count == 0, "An edited line is changed, not removed");
        var addedLine = CodeFiles.LineChanges(oldCode, "a = 1\nb = 2\nnew = 0\nc = 3\nd = 4\ne = 5");
        Check(addedLine is not null && addedLine.Changed.SetEquals(new[] { 2 }), "An added line is marked");
        var removedLine = CodeFiles.LineChanges(oldCode, "a = 1\nb = 2\nd = 4\ne = 5");
        Check(removedLine is not null && removedLine.Changed.Count == 0 && removedLine.RemovedBefore.SetEquals(new[] { 2 }), "A removed line marks where it was");
        var removedAtEnd = CodeFiles.LineChanges(oldCode, "a = 1\nb = 2\nc = 3\nd = 4");
        Check(removedAtEnd is not null && removedAtEnd.RemovedBefore.SetEquals(new[] { 4 }), "Lines removed from the end mark one past the last line");
        Check(CodeFiles.LineChanges(oldCode, "p\nq\nr\ns") is null, "A rewrite has no marks");
    }
}
