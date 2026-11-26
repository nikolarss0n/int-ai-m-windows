using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Tests.Domain;

/// From `Tests/test_processor.swift` ("InterviewRole QA profile consolidation", "classifySpeaker",
/// "SpeechTurnPolicy", "GroqRequestTuning", "isFollowUp", "messagesToAPIFormat", "Sequenced conversation
/// context") and `Tests/test_audio_source.swift` (`testPureState`), plus checks of the settings port.
[Collection("SharedSettings")]
public class SettingsAndStateTests : SwiftStyleTests
{
    [Fact]
    public void QaProfileConsolidation()
    {
        Check(!InterviewRoles.SelectableCases.Contains(InterviewRole.Sdet), "Legacy SDET role is hidden from UI choices");
        Check(InterviewRole.Sdet.CanonicalRole() == InterviewRole.QAAutomationEngineer, "Legacy SDET maps to QA Automation / SDET");
        Check(InterviewRole.Sdet.DisplayName() == "QA Automation / SDET", "Legacy SDET uses combined display name");
        Check(InterviewRole.QAEngineer.IsQA(), "QA Engineer uses shared QA profile");
        Check(InterviewRole.QAAutomationEngineer.IsQA(), "QA Automation uses shared QA profile");
        Check(InterviewRole.SeniorQAEngineer.IsQA(), "Senior QA uses shared QA profile");
        Check(!InterviewRole.BackendDeveloper.IsQA(), "Non-QA role does not use QA profile");
        Check(InterviewRoles.AllCases.Count == 17 && InterviewRoles.SelectableCases.Count == 16, "Every role is listed, and all but SDET can be chosen");
        Check(InterviewRoles.AllCases.All(role => InterviewRoles.FromRawValue(role.RawValue()) == role), "Role raw values round-trip");
        Check(ProgrammingLanguages.AllCases.All(language => ProgrammingLanguages.FromRawValue(language.RawValue()) == language), "Programming language raw values round-trip");
        Check(SpeakingLanguages.AllCases.All(language => SpeakingLanguages.FromRawValue(language.RawValue()) == language), "Speaking language raw values round-trip");
        Check(InterviewRole.SeniorAIEngineer.IsSenior() && !InterviewRole.AIEngineer.IsSenior() && InterviewRole.MLEngineer.IsAIOrData(), "Seniority and AI roles are read from the role");
    }

    [Fact]
    public void SettingsDefaultsAndPersistence()
    {
        var store = new InMemorySettingsStore();
        var settings = new AppSettings(store);
        Check(settings.Role == InterviewRole.SeniorQAEngineer && settings.ProgrammingLanguage == ProgrammingLanguage.Python, "A new install starts as Senior QA in Python");
        Check(settings.CaptureDevice == CaptureDevice.SystemAudio && settings.AnswerModel == AnswerModel.Grok, "It listens to system audio and answers with Grok");
        Check(settings.BackgroundTransparency == 0.7 && settings.OpenAIAnswerModel == "gpt-5.5" && settings.OpenAIReasoningEffort == "low", "Background and OpenAI defaults match macOS");
        Check(store.GetString("InterviewMaster.ListeningLanguage") == "en" && store.GetString("InterviewMaster.ResponseLanguage") == "en", "The language split is written on first run");
        settings.BackgroundTransparency = 4;
        Check(settings.BackgroundTransparency == 1, "Transparency is clamped");
        settings.Role = InterviewRole.Sdet;
        Check(store.GetString("InterviewMaster.Role") == "qa_automation_engineer" && settings.Role == InterviewRole.QAAutomationEngineer, "SDET is stored as its canonical role");
        settings.ResponseLanguage = SpeakingLanguage.German;
        Check(store.GetString("InterviewMaster.SpeakingLanguage") == "de" && settings.SpeakingLanguage == SpeakingLanguage.German, "The response language also writes the legacy key");
        settings.Frameworks = " Playwright , , Pytest ";
        Check(settings.FrameworksList.SequenceEqual(new[] { "Playwright", "Pytest" }), "Frameworks split on commas and trim");
        settings.CaptureDevice = CaptureDevice.Microphone;
        Check(new AppSettings(store).CaptureDevice == CaptureDevice.Microphone, "A second reader sees what the first wrote");

        var legacy = new InMemorySettingsStore(new Dictionary<string, string>
        {
            ["InterviewMaster.TechStack"] = "qa_typescript",
            ["InterviewMaster.Language"] = "bg"
        });
        var migrated = new AppSettings(legacy);
        Check(migrated.Role == InterviewRole.SeniorQAEngineer && migrated.ProgrammingLanguage == ProgrammingLanguage.TypeScript, "A legacy QA stack migrates to role and language");
        Check(migrated.Frameworks == "Playwright, Jest, API Testing", "It brings its frameworks");
        Check(migrated.ListeningLanguage == SpeakingLanguage.Bulgarian && migrated.ResponseLanguage == SpeakingLanguage.Bulgarian, "The legacy language becomes both languages");
        Check(migrated.TechStack == LegacyTechStack.QaTypeScript, "The legacy view reads back");

        var sdet = new InMemorySettingsStore(new Dictionary<string, string> { ["InterviewMaster.Role"] = "sdet" });
        _ = new AppSettings(sdet);
        Check(sdet.GetString("InterviewMaster.Role") == "qa_automation_engineer", "A stored SDET role is normalized on start");
    }

    [Fact]
    public void SettingsPrompts()
    {
        var qa = new AppSettings(new InMemorySettingsStore());
        Check(qa.InterviewContext == "Position: Senior QA / SDET\nPrompt Profile: Shared QA/SDET Playwright\nQA Level: Senior QA / SDET\nProgramming Language: Python\nListening Language: English\nResponse Language: English\nTech Stack: Playwright, TypeScript, API Testing, CI", "The QA context reads as on macOS");
        Check(qa.IsPlaywrightFocused && qa.EffectiveFrameworks == "Playwright, TypeScript, API Testing, CI", "QA with no frameworks defaults to Playwright");
        var qaStyle = qa.AnswerStyleInstruction;
        Check(qaStyle.StartsWith("INTERVIEW ANSWER STYLE:\n- Answer as a spoken candidate answer, not as a textbook note.\n", StringComparison.Ordinal), "The style block opens with its heading");
        Check(qaStyle.Contains("- If the transcript is noisy, answer the likely topic confidently.\n\n" + AnswerPromptBlocks.CandidateVoice + "\nTEST AUTOMATION FOCUS:\n- Use the shared QA/SDET Playwright profile for every QA role.\n- QA EMPHASIS: quality strategy"), "The voice block is followed directly by the QA focus");
        Check(qaStyle.EndsWith("use trace/network artifacts to separate app bugs from test bugs.", StringComparison.Ordinal) && qaStyle.Contains("\nPLAYWRIGHT DEFAULTS:\n"), "Playwright defaults close the QA style");
        Check(!qaStyle.Contains("AI/ML FOCUS") && !qaStyle.Contains('\r'), "QA gets no AI block, and prompts keep Unix line endings");
        Check(qa.WhisperVocabulary.StartsWith("Python, Django, Flask", StringComparison.Ordinal) && qa.WhisperVocabulary.Contains("Playwright Test, getByRole") && qa.WhisperVocabulary.EndsWith("Big O, hash map, tree, graph", StringComparison.Ordinal), "Vocabulary is language, then role, then the common terms");
        Check(qa.LanguageInstruction == "OUTPUT LANGUAGE: English. Answer every candidate-facing bullet in English.", "English is one line");

        var ai = new AppSettings(new InMemorySettingsStore())
        {
            Role = InterviewRole.SeniorAIEngineer,
            ResponseLanguage = SpeakingLanguage.German,
            ListeningLanguage = SpeakingLanguage.German
        };
        Check(ai.AnswerStyleInstruction.Contains(AnswerPromptBlocks.CandidateVoice + "\nAI/ML FOCUS:\n- For RAG/CAG") && !ai.AnswerStyleInstruction.Contains("TEST AUTOMATION FOCUS"), "AI roles get the AI block only");
        Check(ai.LanguageInstruction == "OUTPUT LANGUAGE: German.\nAnswer every candidate-facing bullet in German, even if the transcript, previous cards, or examples are in another language.\nKeep code, commands, API names, framework names, and short technical identifiers in English.", "Another language is three lines");
        Check(ai.WhisperVocabulary.Contains("LLM, RAG, Retrieval-Augmented Generation") && ai.WhisperVocabulary.Contains("was ist, wie funktioniert") && ai.LanguageCode == "de", "AI and German vocabulary are added");
        Check(ai.InterviewContext == "Position: Senior AI Engineer\nProgramming Language: Python\nListening Language: German\nResponse Language: German", "A role with no stack has no stack line");

        var previous = AppSettings.Shared;
        try
        {
            AppSettings.Shared = ai;
            Check(AnswerModelOption.Current == AnswerModelOption.All[0], "Grok is the first menu choice");
            ai.AnswerModel = AnswerModel.Gpt;
            Check(AnswerModelOption.Current.Title == "GPT-5.5 (low reasoning)", "GPT defaults to low reasoning");
            ai.OpenAIPriority = true;
            Check(AnswerModelOption.Current.Priority, "Priority picks the priority choice");
            ai.OpenAIReasoningEffort = "high";
            Check(AnswerModelOption.Current == AnswerModelOption.All[2], "An effort with no menu entry shows as low reasoning");
            Check(AnalysisMode.Smart.Prompt().EndsWith("\nOUTPUT LANGUAGE: German.\nAnswer every candidate-facing bullet in German, even if the transcript, previous cards, or examples are in another language.\nKeep code, commands, API names, framework names, and short technical identifiers in English.", StringComparison.Ordinal) && AnalysisMode.Smart.Prefill().Length == 0, "Screenshot analysis asks for steps in the response language");
        }
        finally
        {
            AppSettings.Shared = previous;
        }
    }

    [Fact]
    public void ClassifySpeaker()
    {
        var context = new ConversationContext();
        Check(context.ClassifySpeaker("What is polymorphism?") == ConversationContext.Speaker.Interviewer, "Short question = interviewer");
        Check(context.ClassifySpeaker("anything", isQuestion: true) == ConversationContext.Speaker.Interviewer, "LLM classified question = interviewer");
        Check(context.ClassifySpeaker("Polymorphism is a concept in object-oriented programming that allows objects of different types to be treated as objects of a common base type. It enables you to write code that can work with objects of multiple classes through a single interface, promoting flexibility and extensibility in your software design.") == ConversationContext.Speaker.Interviewee, "Long answer = interviewee");
        Check(context.ClassifySpeaker("I have experience building distributed systems with microservices architecture using Docker and Kubernetes for container orchestration and service discovery") == ConversationContext.Speaker.Interviewee, "Medium-length technical answer (>15 words) = interviewee");
        Check(context.ClassifySpeaker("Tell me about your experience") == ConversationContext.Speaker.Interviewer, "Tell me = interviewer");
        Check(context.ClassifySpeaker("Welcome to the interview") == ConversationContext.Speaker.Interviewer, "Welcome phrase = interviewer");
        Check(context.ClassifySpeaker("ok") == ConversationContext.Speaker.Unknown, "Very short ambiguous = unknown");

        Check(context.IsFollowUp("Tell me more about that"), "Tell me more");
        Check(context.IsFollowUp("Can you elaborate?"), "Elaborate");
        Check(context.IsFollowUp("Give me an example"), "Give example");
        Check(!context.IsFollowUp("What is a binary tree?"), "New question");
        Check(context.IsFollowUp("Go deeper into that topic"), "Go deeper");
        Check(!context.IsFollowUp("I have experience with React"), "Statement");
    }

    [Fact]
    public void ConversationHistory()
    {
        var context = new ConversationContext();
        var result1 = context.MessagesToAPIFormat(new[]
        {
            new ConversationContext.MultiTurnMessage("user", "What is REST?"),
            new ConversationContext.MultiTurnMessage("assistant", "REST is..."),
            new ConversationContext.MultiTurnMessage("user", "Tell me more")
        });
        Check(result1.Count == 3, "3 alternating messages stay as 3");
        Check(result1[0]["role"] == "user", "First is user");
        var result2 = context.MessagesToAPIFormat(new[]
        {
            new ConversationContext.MultiTurnMessage("user", "Hello"),
            new ConversationContext.MultiTurnMessage("user", "What is REST?")
        });
        Check(result2.Count == 1, "Consecutive same-role merged to 1");
        Check(result2[0]["content"] == "Hello\n\nWhat is REST?", "Merged content has both parts");
        var result3 = context.MessagesToAPIFormat(new[]
        {
            new ConversationContext.MultiTurnMessage("assistant", "Welcome"),
            new ConversationContext.MultiTurnMessage("user", "Thanks")
        });
        Check(result3.Count == 3, "Prepends user message when starts with assistant");
        Check(result3[0]["role"] == "user", "Prepended message is user role");
        Check(result3[0]["content"] == "(Interview in progress)", "Prepended placeholder content");

        context.AddUtterance("What is RAG?", "newer-topic", isQuestion: true, sequence: 2);
        context.AddUtterance("And REST?", "older-topic-finished-late", isQuestion: true, sequence: 1);
        Check(context.LastTopic == "newer-topic", "Late completion from an older spoken turn cannot replace the newest topic");
        Check(context.GetFullConversation() == "Q: And REST?\nQ: What is RAG?", "A late turn is filed in spoken order");
        context.AddUtterance("And caching?", "newest-topic", isQuestion: true, sequence: 3);
        Check(context.LastTopic == "newest-topic" && context.LastInterviewerQuestion == "And caching?", "Higher speech sequence advances follow-up context");
        context.AddUtterance("ok", "unknown");
        Check(context.LastTopic == "newest-topic" && context.GetTopicsSummary() == "Topics discussed: older-topic-finished-late, newer-topic, newest-topic", "Placeholder topics are never the topic");
        Check(context.GetContextForLLM() == "Interviewer: And REST?\nInterviewer: What is RAG?\nInterviewer: And caching?\nOther: ok", "The model sees speakers, not topic guesses");

        var longContext = new ConversationContext();
        Check(longContext.GetContextForLLM() == "No previous conversation." && longContext.GetFullConversation().Length == 0, "An empty conversation says so");
        for (var index = 0; index < 11; index++) longContext.AddUtterance($"Question {index}?", "topic", isQuestion: true);
        Check(longContext.NeedsSummarization && longContext.GetMessagesForSummarization().Count == 5, "Past ten turns the older ones are summarized");
        Check(longContext.GetTextForSummarization().StartsWith("Interviewer: Question 0?\nInterviewer: Question 1?", StringComparison.Ordinal), "The summary text names the speaker");
        Check(longContext.BuildMultiTurnMessages("now").Count == 7, "Without a summary the window is the last six plus the new one");
        longContext.SetSummary("Talked about topics.");
        var summarized = longContext.BuildMultiTurnMessages("now", "print(1)");
        Check(!longContext.NeedsSummarization && summarized.Count == 9, "With a summary: context pair, six kept, the new one");
        Check(summarized[0].Content == "Previous interview context: Talked about topics.\n\nCurrent code solution being discussed:\nprint(1)" && summarized[1].Role == "assistant", "The summary and pinned code open the conversation");
        longContext.Clear();
        Check(longContext.LastTopic is null && longContext.ConversationSummary is null, "Clearing forgets topic and summary");
        var many = new ConversationContext();
        for (var index = 0; index < 60; index++) many.AddUtterance($"Question {index}?", null, isQuestion: true);
        Check(many.GetFullConversation().Split('\n').Length == AppConstants.Thresholds.MaxConversationHistory, "History is capped");
    }

    [Fact]
    public void SpeechTurnPolicyAndRequestTuning()
    {
        Check(SpeechTurnPolicy.Action(SpeechTurnEmit.SpeculativePreview) == SpeechTurnAction.PrefetchTranscriptionOnly, "0.25s silence overlaps STT only");
        Check(!SpeechTurnPolicy.StartsAnswerCard(SpeechTurnAction.PrefetchTranscriptionOnly), "Preview must not start an answer card");
        Check(SpeechTurnPolicy.Action(SpeechTurnEmit.SpeechResumedAfterPreview) == SpeechTurnAction.CancelInFlightWork, "Speech resume cancels in-flight preview work");
        Check(!SpeechTurnPolicy.StartsAnswerCard(SpeechTurnAction.CancelInFlightWork), "Cancel must not start an answer card");
        Check(SpeechTurnPolicy.Action(SpeechTurnEmit.FinalSilence) == SpeechTurnAction.CommitAnswer, "0.65s silence commits the answer");
        Check(SpeechTurnPolicy.StartsAnswerCard(SpeechTurnAction.CommitAnswer), "Final silence is the only emit that starts an answer card");

        var wavBytes = "RIFF"u8.ToArray().Concat(new byte[] { 16, 0, 0, 0 }).Concat("WAVE"u8.ToArray()).Concat(new byte[] { 0, 0, 0, 0 }).ToArray();
        Check(GroqRequestTuning.TranscriptionUpload(wavBytes) == ("audio.wav", "audio/wav"), "WAV payload uses .wav filename and audio/wav");
        var m4aBytes = new byte[] { 0, 0, 0, 24 }.Concat("ftyp"u8.ToArray()).Concat("M4A "u8.ToArray()).ToArray();
        Check(GroqRequestTuning.TranscriptionUpload(m4aBytes) == ("audio.m4a", "audio/mp4"), "M4A payload keeps .m4a filename and audio/mp4");

        var ossFields = GroqRequestTuning.ChatReasoningFields("openai/gpt-oss-20b");
        Check(ossFields["reasoning_effort"] as string == "low", "GPT-OSS stays on low reasoning");
        Check(ossFields["include_reasoning"] is false, "GPT-OSS hides reasoning so first visible token is answer text");
        Check(!ossFields.ContainsKey("reasoning_format"), "GPT-OSS does not send unsupported reasoning_format");
        Check(GroqRequestTuning.ChatReasoningFields("qwen/qwen3.6-27b")["reasoning_effort"] as string == "none", "Qwen disables reasoning for live cue cards");
        Check(GroqRequestTuning.ChatReasoningFields("llama-3.1-8b-instant").Count == 0, "Unknown models get no reasoning extras");

        var prefillMessages = GroqRequestTuning.ChatMessages("Q: What is SOLID?");
        Check(prefillMessages.Count == 2, "Fast-path chat uses user prompt plus cue-card prefill");
        Check(prefillMessages[0]["role"] == "user", "First chat message is the user prompt");
        Check(prefillMessages[1]["role"] == "assistant", "Second chat message prefills the assistant");
        Check(prefillMessages[1]["content"] == "- ", "Assistant prefill is a cue-card dash so the first token is answer text");
        Check(GroqRequestTuning.VisibleCueCardPrefix("- SOLID is").Length == 0, "Does not double the dash when the model already starts a bullet");
        Check(GroqRequestTuning.VisibleCueCardPrefix("SOLID is") == "- ", "Adds the cue-card dash when the first chunk has no bullet");
        Check(GroqRequestTuning.VisibleCueCardPrefix("  SOLID") == "- ", "Trims whitespace before deciding whether to add a dash");
    }

    [Fact]
    public void PushToTalkAndCapturePresentation()
    {
        var mute = new PushToTalkState();
        var first = mute.KeyDown(0, isRepeat: false)!.Value;
        Check(mute.IsMuted, "press mutes immediately");
        Check(mute.KeyDown(0.1, isRepeat: true) is null, "autorepeat ignored");
        mute.KeyUp(0.2);
        Check(mute.Latched && !mute.Expire(first), "short tap latches and watchdog cannot expire a released press");
        _ = mute.KeyDown(1, isRepeat: false);
        Check(!mute.IsMuted, "next press clears latch");
        mute.KeyUp(1.1);
        Check(!mute.IsMuted, "unlatch release cannot relatch");
        _ = mute.KeyDown(2, isRepeat: false);
        mute.KeyUp(2.5);
        Check(!mute.IsMuted, "hold release unmutes");
        var held = mute.KeyDown(4, isRepeat: false)!.Value;
        Check(mute.Expire(held) && !mute.IsMuted, "missed release watchdog clears held state");
        mute.ToggleLatch();
        Check(mute.IsMuted, "button latches");
        mute = new PushToTalkState();
        Check(!mute.KeyUp(5) && !mute.Expire(held), "old release/watchdog cannot affect new session");

        Check(AudioSourceSelection.CapturePresentation(CaptureDevice.Microphone, sessionActive: true, switching: false, muted: true, speaking: true) == CapturePresentation.Muted, "mute outranks speaking");
        Check(AudioSourceSelection.CapturePresentation(null, sessionActive: true, switching: false, muted: false, speaking: false) == CapturePresentation.Unavailable, "session alone does not imply listening");
        Check(AudioSourceSelection.CapturePresentation(null, sessionActive: true, switching: true, muted: false, speaking: false) == CapturePresentation.Switching, "a switch in progress says so");
        Check(AudioSourceSelection.CapturePresentation(CaptureDevice.SystemAudio, sessionActive: false, switching: false, muted: true, speaking: true) == CapturePresentation.Idle, "no session is idle whatever else is true");
        Check(CapturePresentation.Muted.Label.Contains("Ctrl+Shift+M"), "the mute label names its Windows shortcut");
        Check(CapturePresentation.Unavailable.Label.Contains("try again") && !CapturePresentation.Unavailable.Label.ToLowerInvariant().Contains("retry"), "one phrase for doing it again");
        Check(CapturePresentation.Listening(CaptureDevice.SystemAudio).Label == "Listening to System audio. Click or press Ctrl+Shift+M to mute.", "the source has one name, in sentence case, and the button says what a click does");
        var muteStates = new[] { CapturePresentation.Listening(CaptureDevice.Microphone), CapturePresentation.Speaking(CaptureDevice.Microphone), CapturePresentation.Muted };
        Check(new[] { "Listening", "Speech", "Muted" }.All(prefix => muteStates.Any(state => state.Label.StartsWith(prefix, StringComparison.Ordinal) && state.Label.ToLowerInvariant().Contains("mute"))), "every state of the mute button names the mute");
        Check(CapturePresentation.Speaking(CaptureDevice.Microphone) != CapturePresentation.Speaking(CaptureDevice.SystemAudio) && CapturePresentation.Speaking(CaptureDevice.Microphone).Symbol == "waveform", "states compare by kind and device");
        Check(AudioSourceSelection.ResolveStartupCaptureDevice(CaptureDevice.Microphone, microphoneAllowed: false) == CaptureDevice.SystemAudio, "revoked permission resolves actual startup");
        var leases = new MicrophoneReservations();
        var old = leases.Acquire(MicrophoneOwner.Practice)!;
        Check(leases.Acquire(MicrophoneOwner.Interview) is null, "a held microphone cannot be taken");
        leases.Release(old);
        var current = leases.Acquire(MicrophoneOwner.Interview)!;
        leases.Release(old);
        Check(leases.Owns(current), "stale release cannot free a new owner's lease");
        foreach (var device in CaptureDevices.AllCases)
        {
            Check(CaptureDevices.FromRawValue(device.RawValue()) == device, "Existing source raw value round-trips");
        }
    }

    [Fact]
    public void MessagesTemplatesAndScreenshots()
    {
        Check(InterviewMessage.FormatLatency(850) == "850ms" && InterviewMessage.FormatLatency(2400) == "2.4s" && InterviewMessage.FormatLatency(1000) == "1.0s", "Latency reads in ms under a second and seconds above");
        var spoken = new InterviewMessage(MessageType.UserResponse, "I built it");
        var answer = new InterviewMessage(MessageType.FollowUp, "- Use a cache", responseLatencyMs: 1250);
        Check(spoken.IsCollapsed && !answer.IsCollapsed && answer.IsAnswer && !answer.IsQuestion, "A spoken response starts collapsed; a follow-up is an answer");
        Check(answer.DisplayLatency == "1.2s" && InterviewMessage.FormatLatency(1750) == "1.8s" && InterviewMessage.FormatLatency(1450) == "1.4s" && InterviewMessage.FormatLatency(1050) == "1.1s", "Latency rounds exactly as the macOS format does, ties to even");
        Check(spoken.Id != answer.Id && spoken.DisplayLatency is null, "Each message has its own id");

        Check(BuiltInTemplates.All.Count == 13 && BuiltInTemplates.All.Sum(template => template.Questions.Count) == 44, "Every built-in template and question came across");
        Check(BuiltInTemplates.All.Select(template => template.Id).Distinct().Count() == 13, "Template ids are unique");
        Check(TemplateCategories.AllCases.All(category => BuiltInTemplates.TemplatesForCategory(category).Count > 0), "Every category has templates");
        Check(BuiltInTemplates.All[0].Questions[0].Text == "Tell me about yourself" && BuiltInTemplates.All[0].Questions[0].Hints.SequenceEqual(new[] { "2-3 minutes max", "Present → Past → Future structure", "Tailor to the role" }), "The first question keeps its hints");
        Check(TemplateCategory.TestAutomation.DisplayName() == "QA/SDET" && TemplateCategory.SystemDesign.RawValue() == "systemDesign", "Categories keep their names");

        var wide = new Screenshot(new byte[] { 1, 2, 3 }, 1600, 900);
        var tall = new Screenshot(Array.Empty<byte>(), 900, 1600, wide.Id);
        Check(wide.ThumbnailSize(160, 160) == (160, 90) && tall.ThumbnailSize(160, 160) == (90, 160), "A thumbnail keeps the screenshot's shape");
        Check(wide.ToBase64() == "AQID" && tall.ToBase64() is null && wide.Equals(tall), "Screenshots encode their PNG and compare by id");
        Check(Tab.Voice.KeyboardShortcut() == "Ctrl+3" && Tab.Practice.Title() == "Practice", "Tabs name their Windows shortcut");
    }
}
