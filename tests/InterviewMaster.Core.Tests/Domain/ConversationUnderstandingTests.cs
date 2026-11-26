using System.Text;
using InterviewMaster.Domain;

namespace InterviewMaster.Tests.Domain;

/// From `Tests/test_processor.swift`: the "conversation understanding", "ClassifierStreamParser" and
/// "AnswerPromptBlocks" sections. Assertions that also need the processor live in the processor tests.
public class ConversationUnderstandingTests : SwiftStyleTests
{
    private static bool ForeignMarker(string text) =>
        ConversationUnderstanding.HasForeignQuestionMarker(ConversationUnderstanding.Normalized(text));

    private static List<string> Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public void ForeignQuestionMarkers()
    {
        Check(ForeignMarker("wie funktioniert Garbage Collection"), "German question word still counts");
        Check(ForeignMarker("Was ist ein HashMap?"), "Leading German 'was' still counts");
        Check(ForeignMarker("comment fonctionne le garbage collector"), "Leading French 'comment' still counts");
        Check(ForeignMarker("¿Qué es un hashmap?"), "Spanish question word behind punctuation counts");
        Check(ForeignMarker("Warum nutzt man Redis hier"), "German 'warum' counts");
        Check(ForeignMarker("Какво е ООП?"), "Cyrillic markers still match by substring");
        Check(!ForeignMarker("I have two projects in production"), "'two ' is not the German 'wo '");
        Check(!ForeignMarker("The candidate can answer the question"), "'answer ' is not the German 'wer '");
        Check(!ForeignMarker("So you wanna do it in Python, it's fine"), "'wanna' is not the German 'wann'");
        Check(!ForeignMarker("It was a hard migration and we shipped it"), "Mid-sentence English 'was' is not a question marker");
        Check(!ForeignMarker("Add a comment to the pull request"), "English 'comment' mid-sentence is not French");
        Check(ConversationUnderstanding.HasQuestioningWhWord(Words("which framework would you pick")), "Leading 'which framework' asks");
        Check(!ConversationUnderstanding.HasQuestioningWhWord(Words("something like which I like")), "'which I like' is a relative clause");
        Check(!ConversationUnderstanding.HasQuestioningWhWord(Words("the project which was about agents")), "A mid-sentence 'which was' is a relative clause");
    }

    internal static readonly string[] RealCandidateLines =
    {
        "Yeah, of course, for me, like I have 10 plus years experience in software engineering, now focusing more on AI engineering, but in the past I started working with LOMs, neural networks and so on",
        "I worked on a site project before I started here, which was about agent-to-agent communication.",
        "Mostly when you have to, let's say, get familiar with some kind of repository, Cloud Code is very helpful and you can go around and like asking questions about the repository",
        "Something like which I like it's like my hobby so I tried the quad code code code X, QN, the Chinese models"
    };

    internal static readonly string[] RealInterviewerLines =
    {
        "So how do you evaluate a classifier?",
        "Do you have any side projects that you are working on?",
        "Which JavaScript frameworks have you used?",
        "Okay, so let's say we deployed an AI model to production and it's not performing well. How do you make it work?",
        "Can you explain what unbalanced data means here when we are dealing with this now?",
        "How about Kubernetes? Do you know how you are connecting to the cluster and"
    };

    [Fact]
    public void CandidateSpeech()
    {
        Check(ConversationUnderstanding.LooksLikeCandidateNarrative(ConversationUnderstanding.Normalized(RealCandidateLines[0])), "First-person answer opener reads as narrative");
        Check(!ConversationUnderstanding.LooksLikeCandidateNarrative("i have five years of experience"), "Short statements are left to the existing first-person checks");
        Check(!ConversationUnderstanding.LooksLikeCandidateNarrative(ConversationUnderstanding.Normalized("So what languages are you comfortable working with? Most comfortably I am with Python and TypeScript, and in general I also started with Java")), "A question mark keeps a mixed turn a question");
        Check(!ConversationUnderstanding.LooksLikeCandidateNarrative(ConversationUnderstanding.Normalized("Tell me how we should handle retries in our pipeline because we run this every night and we have flaky vendors")), "Interviewer request wording is never narrative");
        Check(ConversationUnderstanding.IsCandidateMetaUtterance("Okay, just give me a second."), "Stalling is candidate meta");
        Check(ConversationUnderstanding.IsCandidateMetaUtterance("Give me a second, please."), "Stalling with please is candidate meta");
        Check(ConversationUnderstanding.IsCandidateMetaUtterance("Let me share my screen."), "Screen-share narration is candidate meta");
        Check(ConversationUnderstanding.IsCandidateMetaUtterance("Does that make sense?"), "Check-in is candidate meta");
        Check(!ConversationUnderstanding.IsCandidateMetaUtterance("Let me find the questions I prepared. Uh, are you familiar with MLOps tools?"), "A real question in the same breath is not meta");
        Check(!ConversationUnderstanding.IsCandidateMetaUtterance("Can you give me a second example of a race condition?"), "A request that mentions 'second' is not meta");
        Check(!ConversationUnderstanding.IsCandidateMetaUtterance("What is a hash map?"), "Plain question is not meta");
    }

    internal const string TrailingQuestion = "Okay, how to read a huge Python file with 10,000 rows and what...";

    [Fact]
    public void BackReferences()
    {
        var dependentPrompts = new[]
        {
            "Tell me more", "Can you elaborate?", "Go deeper", "What else?", "Anything else?", "More details", "Elaborate",
            "So, anyway, where do we start with?", "What should we be expecting?", "Where do we go from here?",
            "How would you approach this?", "So where do we start?", "Have you used any of these?",
            "So, it will split it?", "Which tools do you mean?", "and then", "Why?", "How so?", "What about it?",
            "Can you explain that?", "What do you mean by that?", "What is that?",
            "Frameboards? Script, Frameboards, yeah. Ah, okay. Well, used Spring,", "So you are using cloud code, what else?",
            "Dotté?"
        };
        foreach (var prompt in dependentPrompts)
        {
            Check(ConversationUnderstanding.RefersBackToConversation(prompt), $"Leans on the previous turn: {prompt}");
        }
        var independentPrompts = new[]
        {
            "How do you evaluate a classifier?", "So how do you evaluate a classifier?",
            "Do you have any side projects that you are working on?", "What is a hash map?",
            "Explain Bayesian inference", "How does the retriever rank chunks in your pipeline?",
            "Which JavaScript frameworks have you used?", "Why do we have two stage docker builds?"
        };
        foreach (var prompt in independentPrompts)
        {
            Check(!ConversationUnderstanding.RefersBackToConversation(prompt), $"Stands on its own: {prompt}");
        }
        Check(ConversationUnderstanding.RefersBackToConversation("Как се оценява класификатор?"), "Non-Latin scripts keep the earlier inherit-the-topic behaviour");
        Check(ConversationUnderstanding.HeldQuestion("Cotis, recursion,") == "Cotis, recursion", "A question stopping on a comma is answered without the comma");
        Check(ConversationUnderstanding.HeldQuestion("What is recursion?") is null, "A finished question does not wait for silence");
        Check(ConversationUnderstanding.HeldQuestion(",") is null, "A lone comma is not a question");
        Check(ConversationUnderstanding.TrailsOff(TrailingQuestion), "A question ending on 'and what...' trails off");
        Check(ConversationUnderstanding.TrailsOff("How do you read a file…"), "A typographic ellipsis trails off");
        Check(!ConversationUnderstanding.FollowsPrevious("Why slicing?", "coding", null), "The first question follows nothing");
        Check(ConversationUnderstanding.FollowsPrevious("Why did you use slicing?", "coding", "coding"), "The same topic again is a follow-up");
        Check(ConversationUnderstanding.FollowsPrevious("Go on", "followUp", "testing"), "The classifier's follow-up topic is a follow-up");
        Check(ConversationUnderstanding.FollowsPrevious("What about retries?", "networking", "testing"), "A question that leans on the last one is a follow-up");
        Check(!ConversationUnderstanding.FollowsPrevious("What is polymorphism?", "polymorphism", "coding"), "A new topic is not a follow-up");
        var followUps = new (string Text, bool Expected)[]
        {
            ("Okay, now can you write it in TypeScript?", true), ("Why did you use slicing instead of a loop?", true),
            ("Is there another way to reverse it?", true), ("How would you test a rate limiter?", false),
            ("Can you walk me through your last project?", false), ("Explain how the event loop schedules callbacks.", false),
            ("What is the time complexity of the two pointer version, and does it change with unicode input?", true)
        };
        foreach (var (text, expected) in followUps)
        {
            var got = ConversationUnderstanding.FollowsPrevious(text, "unknown", "coding");
            Check(got == expected, $"Follow-up {expected} expected for: {text}");
        }
        Check(ConversationUnderstanding.TrailsOff("How do you test it and how."), "A connective with a period trails off");
        Check(!ConversationUnderstanding.TrailsOff("How does it work...?"), "A question mark finishes the thought");
        Check(!ConversationUnderstanding.TrailsOff("What is recursion"), "A plain question does not trail off");
        Check(ConversationUnderstanding.TrailsOff("how to read a huge python file with 10,000 rows and what?"), "Transcription's question mark does not finish 'and what'");
        Check(ConversationUnderstanding.TrailsOff("Kind of, let's say."), "Transcription's period does not finish a filler tail");
        Check(!ConversationUnderstanding.TrailsOff("Is it a kind of?"), "A question mark does finish a filler tail");
        Check(ConversationUnderstanding.HeldQuestion("Okay, how to read a file and what? Kind of, let's say.") == "Okay, how to read a file", "Tails go across a question mark");
        Check(ConversationUnderstanding.HeldQuestion(TrailingQuestion) == "Okay, how to read a huge Python file with 10,000 rows", "The unfinished tail is dropped if silence follows");
        Check(ConversationUnderstanding.HeldQuestion($"{TrailingQuestion} kind of, let's say,") == "Okay, how to read a huge Python file with 10,000 rows", "Stacked tails all go");
        Check(ConversationUnderstanding.ContinuedQuestion("How to read a big file?", "kind of, let's say,", 0.4, 5) == "How to read a big file? kind of, let's say,", "A fragment right after a question joins it");
        Check(ConversationUnderstanding.ContinuedQuestion("How to read a big file?", "kind of, let's say,", 9, 5) is null, "A fragment long after stands alone");
        Check(ConversationUnderstanding.ContinuedQuestion("How to read a big file?", "what is the", 1, 5) is null, "A new question does not join the last one");
        Check(ConversationUnderstanding.ContinuedQuestion(null, "kind of,", 1, 5) is null, "No question, nothing to join");
        Check(ConversationUnderstanding.LooksGarbled("Aki, di, sari, u, gontu, yus?"), "The logged transcript is garbled");
        Check(ConversationUnderstanding.LooksGarbled("Aki, D, sorry, U, gone to, yas?"), "The same speech transcribed another way is garbled too");
        Check(!ConversationUnderstanding.LooksGarbled("What is recursion?"), "A question is not garbled");
        Check(!ConversationUnderstanding.LooksGarbled("So, in Python, what do you, use for, testing?"), "A chopped sentence with a question word is not garbled");
        Check(!ConversationUnderstanding.LooksGarbled("Okay, so, how, why?"), "Scraps with a question word are not garbled");
        Check(!ConversationUnderstanding.LooksGarbled("Red, green, refactor?"), "Three items are too few to call garbled");
        Check(!ConversationUnderstanding.LooksGarbled("Python, Java, Go, Rust?"), "A list of real technologies is not garbled");
        Check(FeedbackCopy.NoticeForStatus(FeedbackCopy.DidntCatchThat)?.Message == "Didn't catch that. Ask again.", "Garbled speech gets a notice that goes by itself");
        Check(FeedbackCopy.NoticeForStatus(FeedbackCopy.DidntCatchThat)?.Lasts is not null, "The notice does not stay");
    }

    private static (bool Monotonic, ClassifierStreamParser.Parse Final) ParseAllPrefixes(string full, bool headerless = false)
    {
        var runes = full.EnumerateRunes().ToList();
        var final = ClassifierStreamParser.ParseReply(full, headerless, streamEnded: true);
        var previous = "";
        var monotonic = true;
        for (var end = 0; end <= runes.Count; end++)
        {
            var builder = new StringBuilder();
            foreach (var rune in runes.Take(end)) builder.Append(rune.ToString());
            var parsed = ClassifierStreamParser.ParseReply(builder.ToString(), headerless, streamEnded: false);
            if (!parsed.Answer.StartsWith(previous, StringComparison.Ordinal)) monotonic = false;
            previous = parsed.Answer;
        }
        return (monotonic, final);
    }

    [Fact]
    public void ClassifierStream()
    {
        var standard = ParseAllPrefixes("STATUS:question|TOPIC:oop\n---\n- Polymorphism means one interface, many forms.\n- I use it for swappable strategies.");
        Check(standard.Monotonic, "Answer text only ever grows while streaming");
        Check(standard.Final.StatusLine == "STATUS:question|TOPIC:oop", "Header line is separated from the answer");
        Check(standard.Final.Answer == "- Polymorphism means one interface, many forms.\n- I use it for swappable strategies.", "Separator is removed from the answer");
        Check(!standard.Final.FormatDrift, "Well-formed reply is not drift");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:oop", streamEnded: false).StatusLine is null, "Header is not released before its line ends");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:oop\n", streamEnded: false).StatusLine is not null, "Header is released once its line ends");
        var noSeparator = ParseAllPrefixes("STATUS:question|TOPIC:mlops\n- one\n- two");
        Check(noSeparator.Monotonic && noSeparator.Final.Answer == "- one\n- two", "A missing --- separator no longer drops the answer");
        var blankInstead = ParseAllPrefixes("STATUS:question|TOPIC:mlops\n\n- one");
        Check(blankInstead.Final.Answer == "- one", "A blank line instead of --- is fine");
        var noHeader = ParseAllPrefixes("- Sure, here it is.\n- Second point.");
        Check(noHeader.Monotonic && noHeader.Final.FormatDrift && noHeader.Final.StatusLine is null, "A reply with no STATUS header is flagged as drift");
        Check(noHeader.Final.Answer == "- Sure, here it is.\n- Second point.", "Drift keeps the whole reply as the answer");
        var leadingBlank = ParseAllPrefixes("\nSTATUS:question|TOPIC:x\n---\n- a");
        Check(leadingBlank.Final.StatusLine == "STATUS:question|TOPIC:x" && leadingBlank.Final.Answer == "- a", "A leading blank line does not hide the header");
        var statement = ClassifierStreamParser.ParseReply("STATUS:statement|TOPIC:none\n", streamEnded: true);
        Check(statement.StatusLine == "STATUS:statement|TOPIC:none" && statement.Answer.Length == 0, "Non-question reply carries no answer");
        var headerOnly = ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:x", streamEnded: true);
        Check(headerOnly.StatusLine == "STATUS:question|TOPIC:x" && headerOnly.Answer.Length == 0, "A reply that ends after the header still reports its status");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:x\n-", streamEnded: false).Answer.Length == 0, "A lone dash may still become the separator, so it is held back");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:x\n--", streamEnded: false).Answer.Length == 0, "Two dashes are held back");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:x\n- a", streamEnded: false).Answer == "- a", "A bullet is not mistaken for the separator");
        Check(ClassifierStreamParser.ParseReply("STATUS:question|TOPIC:x\n-", streamEnded: true).Answer == "-", "A dash at the end of the stream is kept");
        var headerless = ParseAllPrefixes("\n- A\n- B", headerless: true);
        Check(headerless.Monotonic && headerless.Final.Answer == "- A\n- B" && headerless.Final.StatusLine is null, "Answer-only replies are the answer");
        Check(ClassifierStreamParser.ParseReply("S", streamEnded: false).Answer.Length == 0 && !ClassifierStreamParser.ParseReply("S", streamEnded: false).FormatDrift, "A first letter that could start STATUS waits");
        Check(ClassifierStreamParser.ParseReply("- Yes", streamEnded: false).FormatDrift, "A reply that clearly is not a header starts streaming at once");
    }

    [Fact]
    public void PromptBlocks()
    {
        var candidateVoice = AnswerPromptBlocks.CandidateVoice;
        var spokenPrompt = CodingSteps.Prompt("python", "write a function that gets a string and", spoken: true);
        var stepsPrompt = CodingSteps.Prompt("python", "Write a FastAPI health service");
        Check(candidateVoice.Length <= 792, "Shared voice stays within the previous 792-character prompt budget");
        Check(!candidateVoice.Contains('\r'), "The voice block has Unix line endings on every platform");
        Check(candidateVoice.Contains("CV in this prompt supports it") && candidateVoice.Contains("Never invent experience"), "Personal voice never authorizes invented work history");
        Check(candidateVoice.Contains("I'd check") && candidateVoice.Contains("State definitions directly"), "Hypothetical approaches may use first person without turning definitions into experience claims");
        Check(spokenPrompt.Contains(candidateVoice) && !stepsPrompt.Contains(candidateVoice), "Spoken coding gets the same voice while screenshot formatting stays unchanged");
        Check(!candidateVoice.Contains("3-5") && !candidateVoice.Contains("Every line starts"), "Shared prose voice does not override the SAY and STEP output contract");
        var namedTopic = AnswerPromptBlocks.TopicBlock("ragCag", inherited: false);
        Check(namedTopic.Line == "TOPIC: ragCag" && namedTopic.AllowsTopicSpecificShape, "A topic named in the question is trusted and may shape the answer");
        Check(namedTopic.GarbleNote.Contains("Trust TOPIC"), "A named topic wins over garbled words");
        var inheritedTopic = AnswerPromptBlocks.TopicBlock("ragCag", inherited: true);
        Check(inheritedTopic.Line.StartsWith("PREVIOUS TOPIC (only if the question refers back to it)", StringComparison.Ordinal), "An inherited topic is labelled as a hint");
        Check(!inheritedTopic.AllowsTopicSpecificShape && !inheritedTopic.GarbleNote.Contains("Trust TOPIC"), "An inherited topic never shapes the answer or overrides the words");
        var ownWords = AnswerPromptBlocks.TopicBlock("unknown", inherited: false);
        Check(ownWords.Line.Length == 0 && !ownWords.AllowsTopicSpecificShape && ownWords.GarbleNote.Contains("as asked"), "No topic means answer the question as asked");
        Check(AnswerPromptBlocks.TopicBlock("", inherited: false).Line.Length == 0, "An empty topic is treated as unknown");
        Check(AnswerPromptBlocks.RecentConversation(null, extendsPreviousAnswer: true).Length == 0, "No conversation adds nothing");
        Check(AnswerPromptBlocks.RecentConversation("No previous conversation.", extendsPreviousAnswer: false).Length == 0, "The empty-history placeholder adds nothing");
        var followUpBlock = AnswerPromptBlocks.RecentConversation("Interviewer: What is RAG?", extendsPreviousAnswer: true);
        Check(followUpBlock.Contains("Interviewer: What is RAG?") && followUpBlock.Contains("do NOT repeat"), "A follow-up extends the previous answer without repeating it");
        var referenceBlock = AnswerPromptBlocks.RecentConversation("Interviewer: What is RAG?", extendsPreviousAnswer: false);
        Check(referenceBlock.Contains("CURRENT question") && !referenceBlock.Contains("do NOT repeat"), "A back-reference only resolves what 'it' means");
        Check(AnswerPromptBlocks.PinnedCode(null).Length == 0 && AnswerPromptBlocks.PinnedCode("  \n").Length == 0, "No code adds nothing");
        Check(AnswerPromptBlocks.PinnedCode("def f(): pass").Contains("def f(): pass"), "Pinned code reaches the prompt");
        var longCode = string.Concat(Enumerable.Repeat("x = 1\n", 1000));
        Check(AnswerPromptBlocks.PinnedCode(longCode, limit: 100).Length < 300, "Pinned code is bounded so it cannot flood the prompt");
    }
}
