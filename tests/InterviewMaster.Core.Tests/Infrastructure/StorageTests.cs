using System.Text.Json;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

public sealed class StorageTests : SwiftStyleTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "interview-master-storage-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests()
    {
        Directory.CreateDirectory(root);
    }

    public override void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        finally
        {
            base.Dispose();
        }
    }

    [Fact]
    public void SettingsFilePersistsPreferences()
    {
        var path = Path.Combine(root, "nested", "settings.json");
        var store = new JsonFileSettingsStore(path);
        Check(store.GetString("missing") is null && !store.GetBool("missing") && store.GetDouble("missing") is null, "Absent keys read as unset");
        store.Set("InterviewMaster.Role", "anything");
        store.Set("InterviewMaster.OpenAIPriority", true);
        store.Set("InterviewMaster.BackgroundTransparency", 0.35);
        var reloaded = new JsonFileSettingsStore(path);
        Check(reloaded.GetString("InterviewMaster.Role") == "anything", "Strings survive a restart");
        Check(reloaded.GetBool("InterviewMaster.OpenAIPriority"), "Booleans survive a restart");
        Check(reloaded.GetDouble("InterviewMaster.BackgroundTransparency") == 0.35, "Numbers survive a restart");
        Check(reloaded.GetString("InterviewMaster.OpenAIPriority") is null, "A boolean is not a string");
        reloaded.Remove("InterviewMaster.Role");
        Check(new JsonFileSettingsStore(path).GetString("InterviewMaster.Role") is null, "Removed keys stay removed");

        var settings = new AppSettings(new JsonFileSettingsStore(path)) { Role = InterviewRole.FrontendDeveloper, Frameworks = "React, Vite" };
        var again = new AppSettings(new JsonFileSettingsStore(path));
        Check(again.Role == InterviewRole.FrontendDeveloper && again.Frameworks == "React, Vite", "App settings persist through the settings file");
        Check(settings.BackgroundTransparency == 0.35, "App settings read numbers from the settings file");

        File.WriteAllText(path, "{ not json");
        Check(new JsonFileSettingsStore(path).GetString("InterviewMaster.Frameworks") is null, "A damaged settings file falls back to defaults");
    }

    [Fact]
    public void KeyFileAndEnvironmentSupplyApiKeys()
    {
        var path = Path.Combine(root, ".interview-master-keys");
        File.WriteAllText(path, "# comment\n\n  GROQ_API_KEY = file-groq  \nXAI_API_KEY=\nANTHROPIC_API_KEY=a=b\r\nOPENAI_API_KEY=file-openai\nnot a key line\n");
        var environment = new Dictionary<string, string?> { ["OPENAI_API_KEY"] = "  env-openai\n", ["XAI_API_KEY"] = "   " };
        var manager = new ApiKeyManager(path, name => environment.GetValueOrDefault(name));
        Check(manager.GetKey(ApiKeyType.Groq) == "file-groq", "Keys and values are trimmed");
        Check(manager.GetKey(ApiKeyType.Anthropic) == "a=b", "Only the first equals sign separates key from value");
        Check(manager.GetKey(ApiKeyType.Xai) is null && !manager.HasKey(ApiKeyType.Xai), "An empty value is no key");
        Check(manager.GetKey(ApiKeyType.OpenAI) == "env-openai", "The environment overrides the file");
        Check(manager.SetKey("typed", ApiKeyType.Xai) && manager.HasKey(ApiKeyType.Xai), "A key can be set for the session");
        Check(manager.DeleteKey(ApiKeyType.Xai) && !manager.HasKey(ApiKeyType.Xai), "A key can be removed for the session");
        File.WriteAllText(path, "GROQ_API_KEY=second\n");
        manager.Reload();
        Check(manager.GetKey(ApiKeyType.Groq) == "second" && manager.GetKey(ApiKeyType.Anthropic) is null, "Reloading replaces the loaded keys");

        var missing = new ApiKeyManager(Path.Combine(root, "no-such-file"), _ => null);
        Check(ApiKeyTypes.AllCases.All(type => !missing.HasKey(type)), "Without a key file or environment there are no keys");
        Check(ApiKeyTypes.AllCases.Select(type => type.RawValue()).SequenceEqual(new[] { "ANTHROPIC_API_KEY", "GROQ_API_KEY", "XAI_API_KEY", "OPENAI_API_KEY" }), "Key names match the macOS key file");
    }

    [Fact]
    public void MemoryStoreKeepsSessionsProfileAndKnowledge()
    {
        var directory = Path.Combine(root, "memory");
        var store = new MemoryStore(directory);
        var date = new DateTime(2026, 9, 29, 12, 34, 56, DateTimeKind.Utc);
        for (var index = 1; index <= 4; index++)
        {
            store.AddSessionSummary(new SessionSummary
            {
                Id = $"s{index}",
                Date = date,
                Topics = new List<string> { "RAG", $"topic{index}" },
                QuestionsAsked = index,
                KeyInsights = new List<string> { $"insight {index}a", $"insight {index}b", $"insight {index}c" },
                SourceFile = $"session-{index}.md"
            });
        }
        store.UpdateUserProfile(new[] { "RAG pipelines", "rag PIPELINES", "Testing" }, new[] { "System design" });
        store.UpdateUserProfile(null, new[] { "system DESIGN", "RAG evaluation" });
        store.AddKnowledge("rag", "Chunk by heading", new[] { "chunking" });
        store.AddKnowledge("caching", "Cache the prefix", new[] { "RAG" });
        store.AddKnowledge("other", "Unrelated", new[] { "misc" });

        var reloaded = new MemoryStore(directory);
        Check(reloaded.GetSessionSummaries().Count == 4 && reloaded.HasSessionForFile("session-2.md") && !reloaded.HasSessionForFile("nope.md"), "Sessions survive a restart");
        Check(reloaded.GetUserProfile().Strengths.SequenceEqual(new[] { "RAG pipelines", "Testing" }), "Strengths are not repeated in another letter case");
        Check(reloaded.GetUserProfile().Weaknesses.SequenceEqual(new[] { "System design", "RAG evaluation" }), "Weaknesses accumulate without repeats");
        var found = reloaded.SearchByTopics(new[] { "rag" });
        Check(found.Sessions.Select(session => session.Id).SequenceEqual(new[] { "s2", "s3", "s4" }), "Search returns the latest three matching sessions");
        Check(found.Knowledge.Select(entry => entry.Content).SequenceEqual(new[] { "Chunk by heading", "Cache the prefix" }), "Knowledge matches by topic or keyword");
        Check(reloaded.SessionsDirectory() == Path.Combine(directory, "sessions"), "Sessions are saved beside the memory file");

        var json = File.ReadAllText(Path.Combine(directory, "memory.json"));
        Check(json.Contains("\"sessionSummaries\"", StringComparison.Ordinal) && json.Contains("\"keyInsights\"", StringComparison.Ordinal) && json.Contains("\"sourceFile\": \"session-1.md\"", StringComparison.Ordinal), "Memory is written with the macOS key names");
        Check(json.Contains("\"date\": \"2026-09-29T12:34:56Z\"", StringComparison.Ordinal), "Memory dates are whole-second UTC ISO 8601");

        var retrieved = new MemoryRetrievalUseCase(reloaded).Retrieve(new[] { "rag" });
        Check(
            retrieved == "MEMORY (from past sessions):\n• You've been asked about rag 3 times before\n• insight 4a\n• insight 4b\n• Strength: RAG pipelines\n• Area to improve: RAG evaluation\n• Your note: \"Chunk by heading\"\n• Your note: \"Cache the prefix\"\n",
            $"Memory context lists past sessions, strengths, gaps and notes: {retrieved}");
        Check(new MemoryRetrievalUseCase(reloaded).Retrieve(Array.Empty<string>()) is null, "No topics, no memory context");
        Check(new MemoryRetrievalUseCase(new MemoryStore(Path.Combine(root, "empty"))).Retrieve(new[] { "rag" }) is null, "Nothing remembered, no memory context");
    }

    [Fact]
    public void AnswerDatabaseShipsWithTheApp()
    {
        var database = new QADatabase();
        Check(database.Topics.Count == 54, $"The shipped answer database has 54 topics (got {database.Topics.Count})");
        Check(database.HasTopic("hashMap") && database.GetAnswer("HASHMAP") == database.GetAnswer("hashMap"), "Topics match in any letter case");
        Check(database.GetAnswer("no-such-topic") is null && !database.HasTopic("no-such-topic"), "Unknown topics fall through to the model");
        Check(new QADatabase(new[] { Path.Combine(root, "missing.json") }).Topics.Count == 0, "Without the file every answer comes from the model");
        var repositoryCopy = File.ReadAllBytes(Path.Combine(RepoPaths.Root, "Resources", "qa_database.json"));
        Check(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "qa_database.json")).SequenceEqual(repositoryCopy), "The answer database is copied beside the app unchanged");
    }

    private sealed class FakePdf : IPdfTextExtractor
    {
        public IReadOnlyList<string>? PageTexts(string path) => path.Contains("broken", StringComparison.Ordinal) ? null : new[] { "Senior Engineer at Acme", "", "2019 - 2024" };
    }

    [Fact]
    public void ResumeStoreAttachesAndClearsTheLiveResume()
    {
        var settings = new InMemorySettingsStore();
        var store = new ResumeStore(settings);
        var changes = 0;
        store.Changed += () => changes++;
        Check(!store.HasResume && store.ExtractedText == "" && store.FileName == "", "No resume to begin with");

        var markdown = Path.Combine(root, "cv.md");
        File.WriteAllText(markdown, "# Jane\n\nSenior Engineer at Acme, 2019 - 2024\n");
        store.Attach(markdown);
        Check(store.HasResume && store.ExtractedText == Briefing.ExtractResumeText(File.ReadAllText(markdown)), "The attached resume is stored as cleaned text");
        Check(store.FileName == "cv.md" && store.SourcePath == markdown && changes == 1, "The file name and path are kept and listeners are told");
        Check(new ResumeStore(settings).ExtractedText == store.ExtractedText, "The resume is kept in the preferences");

        var empty = Path.Combine(root, "empty.md");
        File.WriteAllText(empty, "   \n");
        var message = Assert.Throws<ResumeStoreException>(() => store.Attach(empty)).Message;
        Check(message == "Could not read any text from that file." && store.FileName == "cv.md", "An empty file is refused and the old resume stays");

        var pdf = Path.Combine(root, "cv.pdf");
        File.WriteAllText(pdf, "%PDF");
        Check(Assert.Throws<ResumeStoreException>(() => store.Attach(pdf)).Message == "Could not open the PDF.", "Without a PDF reader a PDF is refused");
        store.PdfTextExtractor = new FakePdf();
        store.Attach(pdf);
        Check(store.ExtractedText == Briefing.ExtractResumeText("Senior Engineer at Acme\n2019 - 2024") && store.FileName == "cv.pdf", "PDF pages are joined and cleaned like Markdown");
        Check(Assert.Throws<ResumeStoreException>(() => store.Attach(Path.Combine(root, "broken.pdf"))).Message == "Could not open the PDF.", "An unreadable PDF is refused");

        store.Clear();
        Check(!store.HasResume && store.FileName == "" && store.SourcePath == "" && changes == 3, "Clearing removes the resume and tells listeners");
    }

    [Fact]
    public void DebugLogIsClearedOnStartAndAppended()
    {
        var path = Path.Combine(root, "interview_debug.log");
        File.WriteAllText(path, "old run\n");
        var logger = new DebugLogger(path);
        var printed = new List<string>();
        var previousSink = Log.Sink;
        Log.Sink = line => { lock (printed) printed.Add(line); };
        try
        {
            logger.Clear();
            logger.Log("first line");
            logger.Log(LogCategory.Transcription, "second line");
            logger.Flush();
        }
        finally
        {
            Log.Sink = previousSink;
        }
        var lines = File.ReadAllLines(path);
        Check(lines.Length == 3 && lines[0].EndsWith("=== Interview Master Started ===", StringComparison.Ordinal), "Starting clears the previous run");
        Check(System.Text.RegularExpressions.Regex.IsMatch(lines[1], @"^\[\d\d:\d\d:\d\d\.\d\d\d\] \[StorageTests:\d+\] first line$"), $"Lines carry the time and the calling file: {lines[1]}");
        Check(lines[2].EndsWith("] 📝 TRANSCRIBE: second line", StringComparison.Ordinal), "Categories prefix the message");
        lock (printed) Check(printed.Contains("📝 first line"), "Lines also reach the console");

        var blocked = new DebugLogger(Path.Combine(root, "no-such-folder", "interview_debug.log"));
        blocked.Clear();
        blocked.Log("nowhere to write");
        blocked.Flush();
        Check(!Directory.Exists(Path.Combine(root, "no-such-folder")), "A log that cannot be written is skipped quietly");
    }

    [Fact]
    public void ExportWritesMarkdownAndJson()
    {
        var settings = new AppSettings(new InMemorySettingsStore()) { Role = InterviewRole.FrontendDeveloper, Frameworks = "React" };
        var clock = new DateTime(2026, 11, 26, 16, 30, 0, DateTimeKind.Utc);
        var useCase = new ExportInterviewUseCase(() => clock, settings);
        var asked = new DateTime(2026, 11, 26, 16, 0, 0, DateTimeKind.Utc);
        var messages = new[]
        {
            new InterviewMessage(MessageType.Answer, "- Closures keep their scope.", topic: "closures", responseLatencyMs: 1250, turnSequence: 1) with { Timestamp = asked.AddSeconds(2) },
            new InterviewMessage(MessageType.Status, "Waiting for audio", turnSequence: 1) with { Timestamp = asked },
            new InterviewMessage(MessageType.Question, "What is a closure?", topic: "closures", audioSource: CaptureDevice.SystemAudio, turnSequence: 1) with { Timestamp = asked.AddSeconds(1) },
            new InterviewMessage(MessageType.CodingTask, "```js\nconst a = 1;\n```", topic: "unknown", turnSequence: 2) with { Timestamp = asked.AddSeconds(3) }
        };

        var json = useCase.Execute(messages, ExportFormat.Json);
        var decoded = JsonSerializer.Deserialize<ExportData>(json, AppJson.Compact);
        Assert.NotNull(decoded);
        Check(decoded.Messages.Select(message => message.Type).SequenceEqual(new[] { "question", "answer", "codingTask" }), "Export orders a turn's question before its answer and drops status lines");
        Check(decoded.Messages[0].AudioSource == "system" && decoded.Messages[1].AudioSource is null, "Export retains immutable capture provenance");
        Check(json.Contains("\"audioSource\"", StringComparison.Ordinal) && !json.Contains("captureDevice", StringComparison.Ordinal), "Export keeps the existing field name");
        Check(decoded.Messages[1].LatencyMs == 1250 && decoded.Messages[0].Timestamp == "2026-11-26T16:00:01Z", "Export keeps latency and ISO 8601 times");
        Check(decoded.Version == "1.0" && decoded.ExportDate == "2026-11-26T16:30:00Z" && decoded.Settings.Role == settings.Role.DisplayName() && decoded.Settings.Frameworks == "React", "Export records the version, date and settings");
        Check(json.IndexOf("\"exportDate\"", StringComparison.Ordinal) < json.IndexOf("\"messages\"", StringComparison.Ordinal) && json.IndexOf("\"settings\"", StringComparison.Ordinal) < json.IndexOf("\"version\"", StringComparison.Ordinal), "Export keys are in sorted order, as on macOS");
        Check(!json.Contains("null", StringComparison.Ordinal), "Absent optionals are left out");

        var markdown = useCase.Execute(messages, ExportFormat.Markdown);
        Check(markdown.StartsWith("# Interview Transcript\n\n**Date:** ", StringComparison.Ordinal) && markdown.EndsWith("---\n\n*Exported from Interview Master*\n", StringComparison.Ordinal), "Markdown has the transcript frame");
        Check(markdown.Contains($"**Role:** {settings.Role.DisplayName()}\n", StringComparison.Ordinal) && markdown.Contains("**Frameworks:** React\n", StringComparison.Ordinal), "Markdown lists the settings");
        Check(markdown.Contains($"## {TopicTitle.Display("closures")}\n\n### 🎙️ Interviewer <small>(", StringComparison.Ordinal) && markdown.Contains("> What is a closure?\n\n### 💡 Suggested Answer <small>(1.2s)</small>\n\n- Closures keep their scope.\n\n", StringComparison.Ordinal), "Markdown groups a topic's question and answer");
        Check(markdown.Contains("### 💻 Coding Solution\n\n```js\nconst a = 1;\n```\n\n", StringComparison.Ordinal) && !markdown.Contains("Waiting for audio", StringComparison.Ordinal), "Markdown keeps code and drops status lines");

        var earlier = new[]
        {
            new InterviewMessage(MessageType.Question, "Write me a reverse function in Python.", turnSequence: 1) with { Timestamp = asked.AddMinutes(-10) },
            new InterviewMessage(MessageType.Answer, "- Slice it.", turnSequence: 1) with { Timestamp = asked.AddMinutes(-10) }
        };
        var latest = new[]
        {
            new InterviewMessage(MessageType.Question, "REST, SQL, K", turnSequence: 1) with { Timestamp = asked },
            new InterviewMessage(MessageType.Answer, "- REST is HTTP APIs over resources.", turnSequence: 1) with { Timestamp = asked }
        };
        var kept = ExportInterviewUseCase.Messages(earlier.Concat(latest), asked);
        Check(kept.Select(message => message.Content).SequenceEqual(latest.Select(message => message.Content)), "Only the latest interview is saved");
        Check(ExportInterviewUseCase.Messages(earlier.Concat(latest), null).Count == 4, "Before any interview everything is kept");
        var keptJson = useCase.Execute(kept, ExportFormat.Json);
        Check(keptJson.Contains("REST, SQL, K", StringComparison.Ordinal) && !keptJson.Contains("reverse function in Python", StringComparison.Ordinal), "The saved file has no question from the earlier interview");
    }
}
