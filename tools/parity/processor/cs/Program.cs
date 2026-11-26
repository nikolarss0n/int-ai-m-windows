using System.Globalization;
using System.Text;
using System.Text.Json;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Support;

// The C# half of the processor parity check. It writes the same `key<TAB>value` lines as
// ../swift/main.swift, from the ported helpers and prompt builders.

var lines = new List<string>();

static string Esc(string text) =>
    text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

void Emit(string key, string value) => lines.Add(key + "\t" + Esc(value));
static string Opt(string? value) => value is null ? "nil" : "S:" + value;
static string Bit(bool value) => value ? "1" : "0";
static string Bits(params bool[] values) => string.Concat(values.Select(Bit));
static string Name(string? value) => value ?? "-";

static IEnumerable<string> Utf8Sorted(IEnumerable<string> values) =>
    values.OrderBy(value => Encoding.UTF8.GetBytes(value), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)));

const string sampleBackground = """
    Alex Example
    Senior Software Engineer

    EXPERIENCE
    Senior Software Engineer, Example Payments (2021 - Present)
    - Built payment APIs in Go and Kotlin, on Kubernetes and PostgreSQL.
    - Led a RAG prototype with Python and a vector database.

    QA Automation Engineer, Sample Labs (2016 - 2021)
    - Wrote Playwright and Selenium suites in TypeScript; cut flaky tests by half.

    SKILLS
    Go, Kotlin, Python, TypeScript, Playwright, Kubernetes, PostgreSQL
    """;
const string samplePinned = "def reverse(text):\n    return text[::-1]";
const string sampleMemory = "You've been asked about this topic 2 times before\nMention the retry budget.";

static ConversationContext FilledContext()
{
    var context = new ConversationContext();
    context.AddUtterance("What is RAG and CAG?", "ragCag", isQuestion: true, sequence: 1);
    context.AddUtterance("I built a retrieval pipeline at my last job.", null, isQuestion: false, sequence: 2);
    context.AddUtterance("So how do you evaluate a classifier?", "classification", isQuestion: true, sequence: 3);
    return context;
}

static string DescribeDecision(CodingTurnDecision? decision) => decision switch
{
    null => "nil",
    { Kind: CodingTurnKind.ArmSession } => "armSession",
    { Kind: CodingTurnKind.NeedsScreenContext } => "needsScreenContext",
    { Kind: CodingTurnKind.NewTask } value => "newTask:" + value.Text,
    { Kind: CodingTurnKind.RefineTask } value => "refineTask:" + value.Text,
    { } value => "changeCode:" + value.Text
};

static string DescribeSteps(IEnumerable<CodingInterviewStep> steps) =>
    string.Join("\u0002", steps.Select(step => $"{step.Number}\u0001{step.FileName}\u0001{step.Why}\u0001{step.Language}\u0001{step.Code}"));

using var corpus = JsonDocument.Parse(File.ReadAllText(args[0]));
var texts = corpus.RootElement.GetProperty("texts").EnumerateArray().Select(item => item.GetString()!).ToList();
var topics = corpus.RootElement.GetProperty("topics").EnumerateArray().Select(item => item.GetString()!).ToList();

var configurations = new (string Label, Dictionary<string, string> Settings)[]
{
    ("se", new() { ["InterviewMaster.Role"] = "software_engineer", ["InterviewMaster.ProgrammingLanguage"] = "swift", ["InterviewMaster.ListeningLanguage"] = "en", ["InterviewMaster.ResponseLanguage"] = "en" }),
    ("ai", new() { ["InterviewMaster.Role"] = "senior_ai_engineer", ["InterviewMaster.ProgrammingLanguage"] = "python", ["InterviewMaster.ListeningLanguage"] = "en", ["InterviewMaster.ResponseLanguage"] = "en" }),
    ("qa", new() { ["InterviewMaster.Role"] = "senior_qa_engineer", ["InterviewMaster.ProgrammingLanguage"] = "typescript", ["InterviewMaster.ListeningLanguage"] = "bg", ["InterviewMaster.ResponseLanguage"] = "bg" })
};
var lastTopics = new string?[] { null, "hashMap", "ragCag", "playwright" };
var classifiedTopics = new string?[] { null, "unknown", "hashMap", "general", "coding", "followUp" };
var now = DateTime.UnixEpoch.AddSeconds(1_700_000_000);
using var mainThread = new QueueMainThread();

foreach (var (label, settings) in configurations)
{
    AppSettings.Shared = new AppSettings(new InMemorySettingsStore(settings));
    Emit("CONFIG", label);
    var processor = new VoiceInterviewProcessor(mainThread);

    for (var index = 0; index < texts.Count; index++)
    {
        var text = texts[index];
        Emit("T", text);
        var normalized = VoiceInterviewProcessor.NormalizedQuestionText(text);
        Emit("norm", normalized);
        var tokens = VoiceInterviewProcessor.TechnicalQuestionTokens(normalized);
        Emit("tokens", string.Join(",", Utf8Sorted(tokens)));
        Emit("hint", Opt(VoiceInterviewProcessor.BestTopicHint(normalized, tokens)));
        Emit("flags", Bits(
            VoiceInterviewProcessor.IsVagueFollowUpPrompt(normalized, tokens),
            VoiceInterviewProcessor.IsBareIncompletePrompt(normalized),
            VoiceInterviewProcessor.ShouldSkipAsSocialPleasantry(text),
            VoiceInterviewProcessor.IsWhisperHallucination(text),
            VoiceInterviewProcessor.ShouldSkipAsFillerOrGreeting(text),
            VoiceInterviewProcessor.IsLocallyIncomplete(text),
            processor.CheckForQuestionMarkers(text)));
        Emit("repair1", VoiceInterviewProcessor.RepairNoisyTechnicalTranscript(text, true));
        Emit("repair0", VoiceInterviewProcessor.RepairNoisyTechnicalTranscript(text, false));
        Emit("repairD", VoiceInterviewProcessor.RepairNoisyTechnicalTranscript(text));
        foreach (var lastTopic in lastTopics)
        {
            var signal = VoiceInterviewProcessor.QuestionSignalFor(text, lastTopic);
            Emit("sig:" + Name(lastTopic), $"{signal.Score}|{string.Join(",", signal.Reasons)}|{Opt(signal.TopicHint)}|" + Bits(
                signal.IsFollowUp, signal.IsBareIncomplete, signal.ProtectsFromSkip,
                VoiceInterviewProcessor.ShouldVetoQuestionAsCandidateStatement(text, signal),
                VoiceInterviewProcessor.ShouldTreatLocalSignalAsClearQuestion(text, signal)));
            foreach (var hasBackground in new[] { false, true })
            {
                var plan = processor.ProvisionalAnswerPlanFor(text, lastTopic, hasBackground);
                Emit("plan:" + Name(lastTopic) + ":" + Bit(hasBackground), plan is null ? "nil" : plan.Topic + "|" + Bit(plan.InheritedFromPriorTurn));
            }
            Emit("topic:" + Name(lastTopic), Opt(processor.ProvisionalAnswerTopic(text, lastTopic)));
        }
        foreach (var classified in classifiedTopics)
        {
            Emit("corrected:" + Name(classified), VoiceInterviewProcessor.CorrectedTopic(text, classified));
        }
        Emit("display", VoiceInterviewProcessor.ConversationalDisplayAnswer(text));
        Emit("clean", VoiceInterviewProcessor.CleanConversationalLine(text));
        Emit("strip", VoiceInterviewProcessor.StripInlineMarkdownForCueCard(text));
        Emit("tail", Opt(VoiceInterviewProcessor.InlineCueTail(text)));
        Emit("marker", VoiceInterviewProcessor.StripCueMarker(text));
        Emit("fragments", string.Join("\u0002", VoiceInterviewProcessor.CueFragments(text)));
        Emit("split", string.Join("\u0002", VoiceInterviewProcessor.SplitLongCommaCueFragment(text)));
        Emit("compact", VoiceInterviewProcessor.CompactCueLine(text));
        Emit("sim", VoiceInterviewProcessor.StringSimilarity(text, texts[(index + 1) % texts.Count]).ToString("F6", CultureInfo.InvariantCulture));

        if (label == "se")
        {
            var understood = ConversationUnderstanding.Normalized(text);
            Emit("cu", understood + "|" + Bits(
                ConversationUnderstanding.HasForeignQuestionMarker(understood),
                ConversationUnderstanding.IsCandidateMetaUtterance(text),
                ConversationUnderstanding.HasInterviewerFrame(understood),
                ConversationUnderstanding.TrailsOff(text),
                ConversationUnderstanding.LooksGarbled(text),
                ConversationUnderstanding.LooksLikeCandidateNarrative(understood),
                ConversationUnderstanding.RefersBackToConversation(text),
                ConversationUnderstanding.FollowsPrevious(text, "unknown", "ragCag"),
                ConversationUnderstanding.FollowsPrevious(text, "hashMap", "ragCag")));
            Emit("held", Opt(ConversationUnderstanding.HeldQuestion(text)));
            Emit("continued", Opt(ConversationUnderstanding.ContinuedQuestion("What is a hash map?", text, 2, 8)));
            var followUp = CodingRequestDetector.FollowUp(text);
            Emit("coding", CodingRequestDetector.Normalized(text) + "|" + Bits(
                CodingRequestDetector.AsksHowToInLanguage(text),
                CodingRequestDetector.IsTaskRequest(text),
                CodingRequestDetector.IsDeicticRequest(text),
                CodingRequestDetector.AsksForCodeExample(text),
                CodingRequestDetector.CodeExampleNamesItsSubject(text),
                CodingRequestDetector.StartsCodingSession(text),
                CodingRequestDetector.LooksLikeTaskDetail(text)) + "|" + (followUp is null ? "nil" : followUp == CodingFollowUp.Change ? "change" : "example"));
            var armed = new CodingSessionState();
            armed.Arm(now);
            var working = new CodingSessionState();
            working.Begin("Write a function that reverses a string", Guid.NewGuid(), now);
            working.Store("STEP 1 — file: main.py\n```python\n" + samplePinned + "\n```");
            Emit("turn", string.Join("\u0002", new[]
            {
                DescribeDecision(CodingSteps.TurnDecision(text, new CodingSessionState(), now)),
                DescribeDecision(CodingSteps.TurnDecision(text, new CodingSessionState(), now, "What is polymorphism?")),
                DescribeDecision(CodingSteps.TurnDecision(text, armed, now.AddSeconds(5))),
                DescribeDecision(CodingSteps.TurnDecision(text, working, now.AddSeconds(5), "What is polymorphism?")),
                DescribeDecision(CodingSteps.TurnDecision(text, working, now.AddSeconds(700)))
            }));
            var parsed = CodingSteps.Parse(text);
            Emit("steps", DescribeSteps(parsed) + "|" + CodingSteps.Format(parsed) + "|" + string.Join("\u0002", CodingSteps.SimplicityViolations(parsed)));
            var progress = CodingSteps.ParseStreaming(text);
            Emit("stream", DescribeSteps(progress.Steps) + "|" + Bit(progress.LastStepIsOpen));
            var response = CodingSteps.SplitResponse(text);
            Emit("response", string.Join("\u0002", response.SayBullets) + "|" + response.StepsMarkdown + "|" + Opt(CodingSteps.FirstTalkingPoint(text))
                + "|" + CodingSteps.CardBullets(response, parsed.Count, true)
                + "|" + CodingSteps.CardBullets(response, parsed.Count, false));
            Emit("trimmed", CodingSteps.TrimmedCodeBlock(text));
        }
    }

    foreach (var topic in topics)
    {
        Emit("guidance:" + topic, VoiceInterviewProcessor.TopicSpecificAnswerGuidance(topic));
        foreach (var inherited in new[] { false, true })
        {
            var block = AnswerPromptBlocks.TopicBlock(topic, inherited);
            Emit("block:" + topic + ":" + Bit(inherited), block.Line + "|" + block.GarbleNote + "|" + Bit(block.AllowsTopicSpecificShape));
            Emit("direct:" + topic + ":" + Bit(inherited), VoiceInterviewProcessor.DirectHaikuAnswerPrompt(
                "Can you explain how that works in practice?", topic, inherited, sampleBackground, FilledContext(), null));
        }
    }

    var questions = texts.Where((text, index) => index % 9 == 0 && text.Length >= 8 && text.Length <= 240).ToList();
    for (var index = 0; index < questions.Count; index++)
    {
        var question = questions[index];
        Emit("Q", question);
        var hint = VoiceInterviewProcessor.QuestionSignalFor(question, null).TopicHint ?? "unknown";
        Emit("direct:a", VoiceInterviewProcessor.DirectHaikuAnswerPrompt(question, hint, false, "", new ConversationContext(), null));
        Emit("direct:b", VoiceInterviewProcessor.DirectHaikuAnswerPrompt(question, hint, true, sampleBackground, FilledContext(), samplePinned));
        Emit("direct:c", VoiceInterviewProcessor.DirectHaikuAnswerPrompt(question, "followUp", false, sampleBackground, FilledContext(), null));
        Emit("coding:a", CodingSteps.Prompt(AppSettings.Shared.ProgrammingLanguage.RawValue(), question));
        Emit("coding:b", CodingSteps.Prompt("Python", question, spoken: true));
        Emit("coding:c", CodingSteps.Prompt("TypeScript", "Reverse a string", spoken: true, currentCode: samplePinned, changeRequest: question));

        if (index % 4 != 0) continue;
        for (var variant = 0; variant < 3; variant++)
        {
            // The macOS side captures this prompt from the request it sends; here it is built by the
            // function that request uses.
            Emit($"manual:{variant}", VoiceInterviewProcessor.ManualAnswerPrompt(
                question,
                variant == 2 ? "ragCag" : hint,
                variant == 0 ? "" : sampleBackground,
                variant == 0 ? new ConversationContext() : FilledContext(),
                variant == 2 ? samplePinned : null,
                variant == 1 ? sampleMemory : null));
        }
    }
}

File.WriteAllText(args[1], string.Join("\n", lines), new UTF8Encoding(false));
