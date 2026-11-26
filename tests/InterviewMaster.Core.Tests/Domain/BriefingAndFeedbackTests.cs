using InterviewMaster.Domain;

namespace InterviewMaster.Tests.Domain;

/// From `Tests/test_processor.swift`: the "interview briefing reducer", "Resume role era grounding",
/// "screenshot analysis overlay", "WaitProgress", "DelayedActivity", "NoticeBoard", "FeedbackCopy" and
/// "TopicTitle" sections.
public class BriefingAndFeedbackTests : SwiftStyleTests
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string AlmaResumeMarkdown =
        "# Jane Doe\n## Experience\n"
        + "- Built a **production RAG** pipeline at AlmaMedia with chunking, embeddings, and citations.\n"
        + "- Shipped LangGraph agents with human-in-the-loop tool calling.\n"
        + "- FastAPI + Pydantic services with retries and LangSmith traces.";

    internal const string RagWalkthrough = "So for this skill check we usually walk through RAG end to end, including chunking a whole book, then embeddings and hybrid search.";

    [Fact]
    public void BriefingReducer()
    {
        var almaResume = Briefing.ExtractResumeText(AlmaResumeMarkdown);
        Check(almaResume.Contains("AlmaMedia"), "Markdown extract keeps the employer");
        Check(almaResume.Contains("production RAG"), "Markdown extract keeps the RAG story");
        Check(!almaResume.Contains("**"), "Markdown extract strips emphasis markers");
        Check(!almaResume.Contains("# Jane"), "Markdown extract strips heading marks");
        Check(Briefing.DetectBriefingThread(RagWalkthrough) == "rag", "Walkthrough of chunking a book is the RAG thread");

        var firstBriefing = Briefing.NextInterviewBriefing(RagWalkthrough, false, almaResume, null);
        Check(firstBriefing.Briefing is not null, "Statement about RAG/chunking a book updates the briefing");
        Check(!Briefing.BriefingOpensCueCard(firstBriefing), "Briefing update does not open a cue card");
        Check(firstBriefing.Briefing?.Thread == "rag", "Digest thread is RAG");
        Check(firstBriefing.Briefing?.Digest.Contains("chunking") == true, "Digest keeps the current RAG walkthrough");
        var ragPoints = firstBriefing.Briefing?.TalkingPoints.ToList() ?? new List<string>();
        Check(ragPoints.Any(point => point.ToLowerInvariant().Contains("almamedia") || point.ToLowerInvariant().Contains("rag")), "Talking points include the resume RAG story");
        Check(!ragPoints.Any(point => point.ToLowerInvariant().Contains("graph rag")), "Talking points omit Graph RAG when it is not in the resume");
        Check(Briefing.FormatBriefingContent(firstBriefing.Briefing!).Contains("[CV]"), "CV-sourced talking points are marked with a CV badge");
        Check(!Briefing.FormatBriefingContent(firstBriefing.Briefing!).ToLowerInvariant().Contains("graph rag"), "Formatted briefing does not invent Graph RAG");

        var secondBriefing = Briefing.NextInterviewBriefing(
            "Then we stay on retrieval: embeddings, hybrid search, rerank, and citations.", false, almaResume, firstBriefing.Briefing);
        Check(secondBriefing.Briefing is not null, "A later statement on the same thread updates the one briefing");
        Check(secondBriefing.Briefing?.Thread == "rag", "In-place update stays on the RAG thread");
        Check(secondBriefing.Briefing?.Digest != firstBriefing.Briefing?.Digest, "Digest text refreshes in place");
        Check(!Briefing.BriefingOpensCueCard(secondBriefing), "In-place briefing refresh still does not open a cue card");
        Check(Briefing.NextInterviewBriefing(RagWalkthrough, false, almaResume, firstBriefing.Briefing) == BriefingDecision.NoChange, "The same statement again changes nothing");

        const string langGraphQuestion = "What did you do with LangGraph?";
        Check(Briefing.IsExperienceCueCardQuestion(langGraphQuestion, "agents"), "Did-you-do-with is an experience cue card");
        var langGraphGrounding = Briefing.ResumeGroundingInstruction(almaResume, langGraphQuestion, "agents");
        Check(langGraphGrounding.Contains("LangGraph"), "Experience prompt may use the LangGraph resume fact");
        Check(langGraphGrounding.Contains("ONLY") || langGraphGrounding.Contains("only"), "Experience prompt constrains answers to the resume");
        Check(!langGraphGrounding.ToLowerInvariant().Contains("graph rag"), "Experience prompt does not add tools missing from the resume");
        Check(Briefing.ResumeGroundingInstruction(almaResume, "What is LangGraph?", "agents").Length == 0, "Conceptual what-is questions are not resume-story prompts");

        var missingThread = Briefing.NextInterviewBriefing(RagWalkthrough, false, "Staff engineer at Acme. Mentored a Python team. Strong communication.", null);
        Check(missingThread.Briefing?.TalkingPoints.IsEmpty == true, "Talking points stay empty when the RAG thread is absent from the resume");
        Check(missingThread.Briefing?.Digest.Length > 0, "Digest still tracks the live thread without inventing experience");
        Check(Briefing.FormatBriefingContent(missingThread.Briefing!).Contains("No matching resume points"), "Empty talking points are explicit, not a generic first-person story");
        Check(Briefing.NextInterviewBriefing("Sounds good", false, almaResume, null) == BriefingDecision.NoChange, "Filler does not create a briefing");
        Check(Briefing.IsExperienceCueCardQuestion("Do you have any side projects that you are working on?", "unknown"), "'do you have any' is an experience question, not a scenario");
        Check(!Briefing.IsExperienceCueCardQuestion("You have a table with 10 million rows. How would you index it?", "database"), "'you have a ' scenarios stay hypothetical");
    }

    private const string EraResume =
        "# Nikolay Prosenikov - AI Engineer\n"
        + "### AI Engineer - Indago AI (2025, 6 months)\n"
        + "Fine-tuned custom AI models with Claude, Gemini, and Grok.\n"
        + "### Senior Software Engineer - Alma (2025 - current)\n"
        + "RAG-powered search.\n"
        + "### Software Engineer - Honeywell Aerospace (2016 - 2018)\n"
        + "Python machine-learning models on aircraft-engine flight recorder data.\n"
        + "### UAT Coordinator (QA) and Process Specialist - HP (2012 - 2013)";

    [Fact]
    public void ResumeRoleEraGrounding()
    {
        var eraRoles = Briefing.ResumeRoles(EraResume);
        Check(eraRoles.Count == 4, "Every dated resume role is parsed");
        Check(eraRoles.FirstOrDefault(role => role.EmployerKeywords.Contains("honeywell"))?.EndYear == 2018, "Honeywell role ends in 2018");
        var alma = eraRoles.FirstOrDefault(role => role.EmployerKeywords.Contains("alma"));
        Check(alma is not null && alma.EndYear is null, "Current role has no end year");
        Check(eraRoles.FirstOrDefault(role => role.EmployerKeywords.Contains("indago"))?.EndYear == 2025, "Single-year role ends the year it started");
        Check(eraRoles.FirstOrDefault(role => role.EmployerKeywords.Contains("indago"))?.EmployerKeywords.Contains("ai") == false, "Generic 'AI' never identifies an employer");
        Check(eraRoles.Any(role => role.EmployerKeywords.SequenceEqual(new[] { "hp" })), "Short employer names like HP still parse");
        var honeywellGrounding = Briefing.ResumeGroundingInstruction(EraResume, "Tell me about the Honeywell project?", "unknown");
        Check(honeywellGrounding.Contains("CANDIDATE RESUME"), "Naming a past employer pulls in the resume even without experience markers");
        Check(honeywellGrounding.Contains("ERA (2016-2018)") && honeywellGrounding.Contains("existed by 2018"), "Past-role answers are pinned to that role's era");
        var almaGrounding = Briefing.ResumeGroundingInstruction(EraResume, "What did you build at Alma?", "personal");
        Check(almaGrounding.Contains("current role") && !almaGrounding.Contains("ERA ("), "Current roles allow modern tools");
        Check(!Briefing.IsExperienceCueCardQuestion("When would you use K-means versus DBSCAN?", "kMeans"), "Would-you scenarios are not resume stories");
        Check(!Briefing.IsExperienceCueCardQuestion("You've got historical sales data. How would you forecast next month's sales?", "forecasting"), "You've-got scenarios are not resume stories");
        Check(!Briefing.IsExperienceCueCardQuestion("Imagine a fraud dataset. How would you evaluate a model?", "metrics"), "Imagine scenarios are not resume stories");
        Check(Briefing.IsExperienceCueCardQuestion("Which vector database have you used in production?", "rag"), "Have-you questions still pull the resume");
        Check(Briefing.IsExperienceCueCardQuestion("What did you use at Allane, and why would you pick it again?", "unknown"), "Past markers win over hypothetical wording");
        Check(Briefing.IsExperienceCueCardQuestion("Do you use LangGraph day to day?", "agents"), "Present-tense usage questions still pull the resume");
        var inheritedGrounding = Briefing.ResumeGroundingInstruction(
            EraResume, "Okay, what kind of models did you use?", "unknown",
            "[interviewer]: Tell me about your experience in Honeywell.\n[candidate]: I built Python models.");
        Check(inheritedGrounding.Contains("ERA (2016-2018)"), "Follow-ups inherit the employer named in the previous question");
        var latestEmployer = Briefing.ResumeRoleMostRecentlyMentioned("We talked about Alma, then Honeywell.", EraResume);
        Check(latestEmployer?.EmployerKeywords.Contains("honeywell") == true, "The most recently named employer wins");
        Check(!Briefing.ResumeGroundingInstruction(EraResume, "What is F1 score?", "f1Score", "Tell me about Honeywell.").Contains("ERA ("), "Concept questions never inherit an employer");
        var otherCompanies = Briefing.ResumeGroundingInstruction(
            EraResume, "Okay, then tell me about your experience in other companies with machine learning.", "personal",
            "[interviewer]: Tell me about your experience in Honeywell.");
        Check(otherCompanies.Contains("ACROSS ROLES") && !otherCompanies.Contains("ASKED ROLE"), "Other companies never pins the previous employer");
        Check(Briefing.ResumeGroundingInstruction(EraResume, "but other companies?", "unknown").Contains("ACROSS ROLES"), "Short other-companies follow-up spans roles");
        Check(Briefing.ResumeGroundingInstruction(EraResume, "List every company where you did AI work", "unknown").Contains("ACROSS ROLES"), "Every-company requests span roles");
        Check(Briefing.ResumeGroundingInstruction(EraResume, "Besides Honeywell, where else did you do ML?", "unknown").Contains("ACROSS ROLES"), "Besides-X spans the other roles");
        Check(!Briefing.AsksAcrossResumeRoles("Tell me about the Honeywell project"), "A single-employer question is not across roles");
        var todayGrounding = Briefing.ResumeGroundingInstruction(EraResume, "If you did the Honeywell project today, what would you do differently?", "unknown");
        Check(todayGrounding.Contains("Today I would") && !todayGrounding.Contains("Do not mention anything released after"), "Asking about today allows labelled modern improvements");
        Check(Briefing.ResumeRoleMentioned("How do AI agents work?", EraResume) is null, "Generic AI questions do not match an employer");
        Check(Briefing.ResumeGroundingInstruction(EraResume, "What is F1 score?", "f1Score").Length == 0, "Concept questions stay resume-free");
    }

    [Fact]
    public void ScreenshotOverlay()
    {
        Check(ScreenshotPresentation.ShouldPresentScreenshotAnalysisOverlay(mainWindowVisible: true), "Live Timeline hides the gallery, so overlay shows even when the main window is visible");
        Check(ScreenshotPresentation.ShouldPresentScreenshotAnalysisOverlay(mainWindowVisible: false), "Overlay still shows in stealth when the main window is hidden");
        Check(ScreenshotPresentation.LiveScreenshotStatusText(0).Length == 0, "No shot status when nothing is captured");
        Check(ScreenshotPresentation.LiveScreenshotStatusText(1) == "1 screenshot · Ctrl+Enter analyze", "One captured screenshot is visible on the live profile line");
        Check(ScreenshotPresentation.LiveScreenshotStatusText(2).Contains("2 screenshots"), "Multiple screenshots keep a visible count, in the word every notice uses");
    }

    [Fact]
    public void WaitProgressWords()
    {
        Check(WaitProgress.Stage(0) == WaitStage.Brief && WaitProgress.Stage(3.9) == WaitStage.Brief, "A short wait names only what is happening");
        Check(WaitProgress.Stage(4) == WaitStage.Counting && WaitProgress.Stage(9.9) == WaitStage.Counting, "After four seconds the wait shows its length");
        Check(WaitProgress.Stage(10) == WaitStage.Stalled && WaitProgress.Stage(90) == WaitStage.Stalled, "After ten seconds it says it is taking longer");
        Check(WaitProgress.Format(7.9) == "7s" && WaitProgress.Format(59.9) == "59s", "Seconds are whole and never round up");
        Check(WaitProgress.Format(65) == "1m 05s" && WaitProgress.Format(600) == "10m 00s", "A minute or more is shown in minutes");
        Check(WaitProgress.Format(-3) == "0s", "A clock that ran backwards shows zero");
        const string separator = " · ";
        Check(WaitProgress.AnswerStatus(true, 30, separator) == "Queued", "A queued answer says queued, however long");
        Check(WaitProgress.AnswerStatus(false, 1, separator) == "Generating", "A fresh wait says what is happening");
        Check(WaitProgress.AnswerStatus(false, 6.2, separator) == "Generating · 6s", "A longer wait counts");
        Check(WaitProgress.AnswerStatus(false, 12, separator) == "Still generating · 12s", "A long wait says so");
        Check(WaitProgress.AnswerStatusShort(true, 30) == "Queued", "A narrow card still says queued");
        Check(WaitProgress.AnswerStatusShort(false, 1) == "Generating", "A narrow card says what is happening while the wait is short");
        Check(WaitProgress.AnswerStatusShort(false, 6.2) == "6s" && WaitProgress.AnswerStatusShort(false, 12) == "12s", "A narrow card keeps just the seconds once there are some");
        Check(WaitProgress.CodeStatus(0.5, separator) == "Writing", "Code that has just been asked for is being written");
        Check(WaitProgress.CodeStatus(5, separator) == "Writing · 5s", "Code that takes a while counts");
        Check(WaitProgress.CodeStatus(11, separator) == "Still writing · 11s", "Code that takes long says so");
    }

    [Fact]
    public void DelayedActivityRules()
    {
        var start = Epoch.AddSeconds(1_000);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var delayed = new DelayedActivity(showAfter: 0.2, expireAfter: 20);
        Check(!delayed.IsShowing(start) && delayed.IsEmpty && delayed.NextChange(start) is null, "Nothing waiting shows nothing");
        delayed.Begin(a, start);
        Check(!delayed.IsShowing(start.AddSeconds(0.1)), "A wait shorter than the delay shows no indicator");
        Check(delayed.IsShowing(start.AddSeconds(0.25)), "A wait that has lasted shows one");
        Check(delayed.NextChange(start) == start.AddSeconds(0.2), "The next change is when the indicator first shows");
        Check(delayed.NextChange(start.AddSeconds(0.25)) == start.AddSeconds(20), "Then when it expires");
        delayed.Begin(a, start.AddSeconds(5));
        Check(!delayed.IsShowing(start.AddSeconds(20.5)), "A repeated begin keeps the first start, so expiry is not pushed back");
        Check(delayed.NextChange(start.AddSeconds(21)) is null, "An expired wait has no further change");
        delayed.End(a);
        Check(delayed.IsEmpty && !delayed.IsShowing(start.AddSeconds(1)), "An ended wait shows nothing");
        delayed.Begin(a, start);
        delayed.Begin(b, start.AddSeconds(1));
        delayed.End(a);
        Check(delayed.IsShowing(start.AddSeconds(1.5)), "One wait ending leaves the other showing");
        delayed.RemoveAll();
        Check(delayed.IsEmpty && !delayed.IsShowing(start.AddSeconds(1.5)), "A reset clears every wait");
    }

    private static FeedbackNotice MakeNotice(string key, FeedbackKind kind, double? lasts = null) =>
        new(key, kind, key, Lasts: lasts);

    [Fact]
    public void NoticeBoardRules()
    {
        var start = Epoch.AddSeconds(5_000);
        var board = new NoticeBoard();
        Check(board.Current is null && board.Expiry is null, "An empty board shows nothing");
        Check(board.Show(MakeNotice("saved", FeedbackKind.Success, 3.5), start), "A confirmation shows on an empty board");
        Check(board.Expiry == start.AddSeconds(3.5), "It says when it will go");
        board.Expire(start.AddSeconds(3));
        Check(board.Current?.Key == "saved", "A confirmation stays for its time");
        board.Expire(start.AddSeconds(3.5));
        Check(board.Current is null, "Then it goes by itself");
        Check(board.Show(MakeNotice("pipeline", FeedbackKind.Error), start), "An error shows");
        board.Expire(start.AddSeconds(3_600));
        Check(board.Current?.Key == "pipeline", "An error stays until it is dismissed or resolved, however long");
        Check(!board.Show(MakeNotice("saved", FeedbackKind.Success, 3.5), start.AddSeconds(10)), "A confirmation does not cover an error");
        Check(board.Current?.Key == "pipeline", "The error is still there");
        Check(board.Show(MakeNotice("pipeline", FeedbackKind.Error), start.AddSeconds(11)), "The same thing again replaces itself");
        Check(board.Show(MakeNotice("save", FeedbackKind.Error), start.AddSeconds(12)), "An error may replace an error");
        board.Resolve("pipeline");
        Check(board.Current?.Key == "save", "Resolving something else leaves the notice alone");
        board.Resolve("save");
        Check(board.Current is null, "Resolving what it is about removes it");
        Check(board.Show(MakeNotice("cleared", FeedbackKind.Info, 8), start), "An info notice shows");
        Check(board.Show(MakeNotice("shot", FeedbackKind.Success, 3.5), start.AddSeconds(1)), "A confirmation replaces an info notice: the newer action is the one being answered");
        Check(board.Show(MakeNotice("cleared", FeedbackKind.Info, 8), start.AddSeconds(2)), "And an info notice replaces a confirmation, so an undo is never dropped");
        Check(board.Show(MakeNotice("mic", FeedbackKind.Warning), start.AddSeconds(3)), "A warning replaces both");
        Check(!board.Show(MakeNotice("shot", FeedbackKind.Success, 3.5), start.AddSeconds(4)), "A confirmation does not cover a warning");
        board.Dismiss();
        Check(board.Current is null && board.Expiry is null, "Dismissing clears it");
    }

    [Fact]
    public void FeedbackCopyWords()
    {
        var networkNotice = FeedbackCopy.NoticeForStatus("Error: Groq transcription network issue. Check connection and try again.");
        Check(networkNotice?.Kind == FeedbackKind.Error && networkNotice?.Message == "Couldn't transcribe. Check your connection.", "A network problem says what failed and the fix, on the line itself");
        Check(networkNotice?.Detail?.Contains("last question may not have been captured") == true, "The detail says what may have been lost");
        Check(networkNotice?.Lasts is null && networkNotice?.Key == "pipeline", "It stays until resolved");
        Check(FeedbackCopy.NoticeForStatus("Error: The request timed out.")?.Message == "Transcription timed out. Ask again.", "A timeout says so and what to do");
        Check(FeedbackCopy.NoticeForStatus("Error: Groq transcription failed: bad audio")?.Message == "Couldn't transcribe. Ask again.", "A transcription failure says so and what to do");
        Check(FeedbackCopy.NoticeForStatus("Error: something odd")?.Message == "Couldn't transcribe. Ask again.", "Every status error is about the question that was heard, so that is what it says");
        Check(FeedbackCopy.NoticeForStatus("Error: something odd")?.Detail == "something odd. The last question may not have been captured.", "An error with no known cause shows its own words on hover");
        Check(FeedbackCopy.NoticeForStatus("Error:")?.Detail == "The last question may not have been captured.", "No words still says what may have been lost");
        var rejectedKey = FeedbackCopy.NoticeForStatus("Error: Groq transcription failed: Groq API error (401): Invalid API Key");
        Check(rejectedKey?.Message == "Groq API key rejected" && rejectedKey?.ActionTitle == FeedbackCopy.OpenSettings, "A rejected key says so and offers the place to fix it, not \"could not transcribe\"");
        Check(FeedbackCopy.NoticeForStatus("Error: Groq API error (429): rate limit reached")?.Message == "Transcription is busy. Ask again in a moment.", "A busy service says so");
        Check(FeedbackCopy.NoticeForStatus("Error: Groq transcription network issue")?.ActionTitle is null, "A dropped connection has no button: the fix is outside the app");
        Check(FeedbackCopy.CauseOf("Error: The request timed out.") == FeedbackCopy.Cause.Timeout && FeedbackCopy.CauseOf("HTTP 401 unauthorized") == FeedbackCopy.Cause.KeyRejected, "The cause reads the same with or without the Error: prefix");
        Check(FeedbackCopy.CauseOf("Error: No answer text received") == FeedbackCopy.Cause.NoText && FeedbackCopy.CauseOf("odd") == FeedbackCopy.Cause.Other, "Every failure has a cause");
        foreach (var status in new[] { "Error: The request timed out.", "Error: Groq transcription network issue", "Error: HTTP 401", "Error: HTTP 429", "Error: odd", "Error:" })
        {
            var notice = FeedbackCopy.NoticeForStatus(status);
            var text = notice?.Message ?? "";
            Check(!text.ToLowerInvariant().Contains("something went wrong") && !text.Contains('!') && !text.ToLowerInvariant().Contains("oops"), $"An error names what failed, never a shrug: {text}");
            Check(notice?.ActionTitle is not null || text.Contains(". "), $"An error carries its fix on the line, as words or as a button: {text}");
        }
        var captureFailure = FeedbackCopy.ScreenshotFailed("No display found.");
        Check(captureFailure.Kind == FeedbackKind.Error && captureFailure.ActionTitle == FeedbackCopy.TryAgain && captureFailure.Lasts is null, "A failed screenshot stays with a button to try again");
        Check(captureFailure.Message == "Couldn't capture the screenshot" && captureFailure.Detail == "No display found.", "It says what failed, and why on hover");
        Check(FeedbackCopy.NoticeForStatus("Listening for questions...") is null && FeedbackCopy.NoticeForStatus("") is null, "Progress is not news");
        Check(FeedbackCopy.FailureReason("Error: No answer text received") == "The model sent back no text.", "An empty answer is named");
        Check(FeedbackCopy.FailureReason("Error: The request timed out.") == "The model took too long to answer.", "A timeout is named");
        Check(FeedbackCopy.FailureReason("Error: The Internet connection appears to be offline.") == "The network connection failed. Check it, then try again.", "A dead network is named, with the fix");
        Check(FeedbackCopy.FailureReason("Error: HTTP 401 unauthorized").Contains(@"%USERPROFILE%\.interview-master-keys"), "A rejected key points at both places a key can live");
        Check(FeedbackCopy.FailureReason("Error: HTTP 401 unauthorized").StartsWith("The API key was rejected", StringComparison.Ordinal), "A rejected key points at Settings");
        Check(FeedbackCopy.FailureReason("Error: HTTP 429 rate limit").StartsWith("The service is busy", StringComparison.Ordinal), "A rate limit says to wait");
        Check(FeedbackCopy.FailureReason("Error: Something specific broke. More detail follows.") == "Something specific broke.", "Anything else is its first sentence");
        Check(FeedbackCopy.FailureReason("Error: " + string.Concat(Enumerable.Repeat("long ", 40))).Length <= 90, "A long reason is cut to a line");
        Check(FeedbackCopy.FailureReason("Error:") == "The model did not answer.", "No reason still says something");
        var hold = FeedbackNotice.ConfirmationHold;
        Check(hold >= 3.0 && hold <= 5.0, "A confirmation stays three to five seconds");
        Check(FeedbackNotice.UndoHold >= 6 && FeedbackNotice.UndoHold > FeedbackNotice.ConfirmationHold, "A notice with a button stays long enough to reach it");
        Check(FeedbackNotice.Confirmation("a", "Saved").Lasts == FeedbackNotice.ConfirmationHold && FeedbackNotice.Confirmation("a", "Saved", actionTitle: "Show").Lasts == FeedbackNotice.UndoHold, "A confirmation with a button lasts longer");
        Check(FeedbackNotice.Error("a", "Failed").Lasts is null && FeedbackNotice.Info("a", "Cleared", actionTitle: "Undo").ActionTitle == "Undo", "An error stays; an info notice can carry an Undo");

        Check(FeedbackCopy.Cleared(0, false) is null && FeedbackCopy.Restored(0, false) is null, "Nothing cleared, nothing to say");
        Check(FeedbackCopy.Cleared(1, false) == "Cleared 1 screenshot", "One screenshot is singular");
        Check(FeedbackCopy.Cleared(3, false) == "Cleared 3 screenshots", "Several are plural");
        Check(FeedbackCopy.Cleared(0, true) == "Cleared the pinned code", "Pinned code alone");
        Check(FeedbackCopy.Cleared(2, true) == "Cleared 2 screenshots and the pinned code", "Both, in one sentence");
        Check(FeedbackCopy.Restored(2, true) == "Restored 2 screenshots and the pinned code", "Undo says what came back, in the words the clear used");
        Check(FeedbackCopy.Sentence("Disk is full") == "Disk is full." && FeedbackCopy.Sentence("Done!") == "Done!" && FeedbackCopy.Sentence("  ") == "", "A reason ends like a sentence");
        Check(FeedbackCopy.NothingToAnalyze.Message == "No screenshots yet. Press Ctrl+S to capture one.", "The empty-analyze notice names the Windows shortcut");
        Check(FeedbackCopy.SessionSaved("x").ActionTitle == "Show in Explorer", "A saved session opens in Explorer");
    }

    [Fact]
    public void TopicTitles()
    {
        var expectations = new (string Slug, string Words)[]
        {
            ("systemDesign", "System design"), ("system_design", "System design"), ("hashMap", "Hash map"), ("followUp", "Follow-up"),
            ("llm", "LLM"), ("rag", "RAG"), ("cag", "CAG"), ("ragCag", "RAG and CAG"), ("llmEvaluation", "LLM evaluation"),
            ("mlops", "MLOps"), ("vectorDatabase", "Vector database"), ("fineTuning", "Fine-tuning"), ("genAI", "Generative AI"),
            ("rocAuc", "ROC AUC"), ("prAuc", "PR AUC"), ("f1Score", "F1 score"), ("kMeans", "K-means"), ("dbscan", "DBSCAN"),
            ("e2eTesting", "E2E testing"), ("apiTesting", "API testing"), ("cicd", "CI/CD"), ("playwrightTest", "Playwright Test"),
            ("csharp", "C#"), ("cpp", "C++"), ("dotnet", ".NET"), ("jvm", "JVM"), ("oop", "OOP"), ("api", "API"), ("aws", "AWS"),
            ("javascript", "JavaScript"), ("typescript", "TypeScript"), ("swiftui", "SwiftUI"), ("langChain", "LangChain"),
            ("huggingFace", "Hugging Face"), ("crossValidation", "Cross-validation"), ("eventLoop", "Event loop"),
            ("algorithms", "Algorithms"), ("python", "Python"), ("coding", "Coding"), ("restAPI", "REST API"), ("APIGateway", "API gateway")
        };
        foreach (var (slug, words) in expectations)
        {
            var shown = TopicTitle.Display(slug);
            Check(shown == words, $"{slug} reads \"{words}\", not \"{shown}\"");
            Check(shown.Length > 0 && !char.IsLower(shown[0]), $"A topic title starts with a capital or a symbol: {shown}");
            Check(!shown.Contains("  ") && !shown.Contains('_') && shown == shown.Trim(), $"A topic title has clean spacing: {shown}");
        }
        Check(TopicTitle.Display("") == TopicTitle.Unknown && TopicTitle.Display("unknown") == "Live answer" && TopicTitle.Display("  ") == "Live answer", "No topic is the live answer");
        Check(TopicTitle.Split("genAI").SequenceEqual(new[] { "gen", "AI" }) && TopicTitle.Split("f1Score").SequenceEqual(new[] { "f1", "Score" }) && TopicTitle.Split("APIGateway").SequenceEqual(new[] { "API", "Gateway" }), "Capitals run together, digits stay with their word");
    }
}
