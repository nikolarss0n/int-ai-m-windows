using InterviewMaster.Support;

namespace InterviewMaster.Domain;

// MARK: - Interview Role

/// Role/position being interviewed for
public enum InterviewRole
{
    SeniorQAEngineer,
    QAEngineer,
    QAAutomationEngineer,
    Sdet,
    BackendDeveloper,
    SeniorBackendDeveloper,
    FrontendDeveloper,
    SeniorFrontendDeveloper,
    FullStackDeveloper,
    SeniorFullStackDeveloper,
    DevOpsEngineer,
    DataEngineer,
    MLEngineer,
    AIEngineer,
    SeniorAIEngineer,
    SoftwareEngineer,
    SeniorSoftwareEngineer
}

public static class InterviewRoles
{
    private static readonly (InterviewRole Role, string RawValue)[] RawValues =
    {
        (InterviewRole.SeniorQAEngineer, "senior_qa_engineer"),
        (InterviewRole.QAEngineer, "qa_engineer"),
        (InterviewRole.QAAutomationEngineer, "qa_automation_engineer"),
        (InterviewRole.Sdet, "sdet"),
        (InterviewRole.BackendDeveloper, "backend_developer"),
        (InterviewRole.SeniorBackendDeveloper, "senior_backend_developer"),
        (InterviewRole.FrontendDeveloper, "frontend_developer"),
        (InterviewRole.SeniorFrontendDeveloper, "senior_frontend_developer"),
        (InterviewRole.FullStackDeveloper, "fullstack_developer"),
        (InterviewRole.SeniorFullStackDeveloper, "senior_fullstack_developer"),
        (InterviewRole.DevOpsEngineer, "devops_engineer"),
        (InterviewRole.DataEngineer, "data_engineer"),
        (InterviewRole.MLEngineer, "ml_engineer"),
        (InterviewRole.AIEngineer, "ai_engineer"),
        (InterviewRole.SeniorAIEngineer, "senior_ai_engineer"),
        (InterviewRole.SoftwareEngineer, "software_engineer"),
        (InterviewRole.SeniorSoftwareEngineer, "senior_software_engineer")
    };

    public static readonly IReadOnlyList<InterviewRole> AllCases = RawValues.Select(pair => pair.Role).ToArray();

    public static readonly IReadOnlyList<InterviewRole> SelectableCases = new[]
    {
        InterviewRole.QAEngineer,
        InterviewRole.QAAutomationEngineer,
        InterviewRole.SeniorQAEngineer,
        InterviewRole.BackendDeveloper,
        InterviewRole.SeniorBackendDeveloper,
        InterviewRole.FrontendDeveloper,
        InterviewRole.SeniorFrontendDeveloper,
        InterviewRole.FullStackDeveloper,
        InterviewRole.SeniorFullStackDeveloper,
        InterviewRole.DevOpsEngineer,
        InterviewRole.DataEngineer,
        InterviewRole.MLEngineer,
        InterviewRole.AIEngineer,
        InterviewRole.SeniorAIEngineer,
        InterviewRole.SoftwareEngineer,
        InterviewRole.SeniorSoftwareEngineer
    };

    public static string RawValue(this InterviewRole role) => RawValues.First(pair => pair.Role == role).RawValue;

    public static InterviewRole? FromRawValue(string? rawValue)
    {
        foreach (var pair in RawValues)
        {
            if (pair.RawValue == rawValue) return pair.Role;
        }
        return null;
    }

    public static InterviewRole CanonicalRole(this InterviewRole role) =>
        role == InterviewRole.Sdet ? InterviewRole.QAAutomationEngineer : role;

    public static string DisplayName(this InterviewRole role) => role.CanonicalRole() switch
    {
        InterviewRole.SeniorQAEngineer => "Senior QA / SDET",
        InterviewRole.QAEngineer => "QA Engineer",
        InterviewRole.QAAutomationEngineer => "QA Automation / SDET",
        InterviewRole.Sdet => "QA Automation / SDET",
        InterviewRole.BackendDeveloper => "Backend Developer",
        InterviewRole.SeniorBackendDeveloper => "Senior Backend Developer",
        InterviewRole.FrontendDeveloper => "Frontend Developer",
        InterviewRole.SeniorFrontendDeveloper => "Senior Frontend Developer",
        InterviewRole.FullStackDeveloper => "Full Stack Developer",
        InterviewRole.SeniorFullStackDeveloper => "Senior Full Stack Developer",
        InterviewRole.DevOpsEngineer => "DevOps Engineer",
        InterviewRole.DataEngineer => "Data Engineer",
        InterviewRole.MLEngineer => "ML Engineer",
        InterviewRole.AIEngineer => "AI Engineer",
        InterviewRole.SeniorAIEngineer => "Senior AI Engineer",
        InterviewRole.SoftwareEngineer => "Software Engineer",
        _ => "Senior Software Engineer"
    };

    public static bool IsSenior(this InterviewRole role) =>
        role.CanonicalRole().RawValue().Contains("senior", StringComparison.Ordinal);

    public static bool IsQA(this InterviewRole role) => role.CanonicalRole()
        is InterviewRole.QAEngineer or InterviewRole.QAAutomationEngineer or InterviewRole.SeniorQAEngineer;

    public static bool IsAIOrData(this InterviewRole role) => role.CanonicalRole()
        is InterviewRole.DataEngineer or InterviewRole.MLEngineer or InterviewRole.AIEngineer or InterviewRole.SeniorAIEngineer;

    public static string QaProfileLabel(this InterviewRole role) => role.CanonicalRole() switch
    {
        InterviewRole.QAEngineer => "QA Engineer",
        InterviewRole.QAAutomationEngineer => "QA Automation / SDET",
        InterviewRole.SeniorQAEngineer => "Senior QA / SDET",
        _ => role.DisplayName()
    };

    public static string QaSeniorityInstruction(this InterviewRole role) => role.CanonicalRole() switch
    {
        InterviewRole.QAEngineer => "QA EMPHASIS: practical testing judgment, clear bug reporting, API/UI basics, and when manual exploration is better than automation.",
        InterviewRole.QAAutomationEngineer => "QA EMPHASIS: Playwright implementation, fixtures, locators, CI, API setup, mocking, and stable automation design.",
        InterviewRole.SeniorQAEngineer => "QA EMPHASIS: quality strategy, ownership, release risk, automation architecture, mentoring, observability, and maintainable CI feedback loops.",
        _ => ""
    };
}

// MARK: - Programming Language

/// Primary programming language for code solutions
public enum ProgrammingLanguage
{
    Python,
    TypeScript,
    JavaScript,
    Java,
    CSharp,
    Go,
    Rust,
    Cpp,
    Kotlin,
    Swift
}

public static class ProgrammingLanguages
{
    private static readonly (ProgrammingLanguage Language, string RawValue, string DisplayName)[] Table =
    {
        (ProgrammingLanguage.Python, "python", "Python"),
        (ProgrammingLanguage.TypeScript, "typescript", "TypeScript"),
        (ProgrammingLanguage.JavaScript, "javascript", "JavaScript"),
        (ProgrammingLanguage.Java, "java", "Java"),
        (ProgrammingLanguage.CSharp, "csharp", "C#"),
        (ProgrammingLanguage.Go, "go", "Go"),
        (ProgrammingLanguage.Rust, "rust", "Rust"),
        (ProgrammingLanguage.Cpp, "cpp", "C++"),
        (ProgrammingLanguage.Kotlin, "kotlin", "Kotlin"),
        (ProgrammingLanguage.Swift, "swift", "Swift")
    };

    public static readonly IReadOnlyList<ProgrammingLanguage> AllCases = Table.Select(row => row.Language).ToArray();

    public static string RawValue(this ProgrammingLanguage language) => Table.First(row => row.Language == language).RawValue;

    public static ProgrammingLanguage? FromRawValue(string? rawValue)
    {
        foreach (var row in Table)
        {
            if (row.RawValue == rawValue) return row.Language;
        }
        return null;
    }

    public static string DisplayName(this ProgrammingLanguage language) => Table.First(row => row.Language == language).DisplayName;

    /// Code block language identifier
    public static string CodeBlockLang(this ProgrammingLanguage language) => language.RawValue();
}

// MARK: - Speaking Language

/// Language for AI responses (code stays in English)
public enum SpeakingLanguage
{
    English,
    Bulgarian,
    German,
    Spanish,
    French,
    Italian,
    Portuguese,
    Russian,
    Chinese,
    Japanese,
    Korean,
    Dutch,
    Polish,
    Turkish,
    Hindi
}

public static class SpeakingLanguages
{
    private static readonly (SpeakingLanguage Language, string RawValue, string DisplayName)[] Table =
    {
        (SpeakingLanguage.English, "en", "English"),
        (SpeakingLanguage.Bulgarian, "bg", "Bulgarian"),
        (SpeakingLanguage.German, "de", "German"),
        (SpeakingLanguage.Spanish, "es", "Spanish"),
        (SpeakingLanguage.French, "fr", "French"),
        (SpeakingLanguage.Italian, "it", "Italian"),
        (SpeakingLanguage.Portuguese, "pt", "Portuguese"),
        (SpeakingLanguage.Russian, "ru", "Russian"),
        (SpeakingLanguage.Chinese, "zh", "Chinese"),
        (SpeakingLanguage.Japanese, "ja", "Japanese"),
        (SpeakingLanguage.Korean, "ko", "Korean"),
        (SpeakingLanguage.Dutch, "nl", "Dutch"),
        (SpeakingLanguage.Polish, "pl", "Polish"),
        (SpeakingLanguage.Turkish, "tr", "Turkish"),
        (SpeakingLanguage.Hindi, "hi", "Hindi")
    };

    public static readonly IReadOnlyList<SpeakingLanguage> AllCases = Table.Select(row => row.Language).ToArray();

    public static string RawValue(this SpeakingLanguage language) => Table.First(row => row.Language == language).RawValue;

    public static SpeakingLanguage? FromRawValue(string? rawValue)
    {
        foreach (var row in Table)
        {
            if (row.RawValue == rawValue) return row.Language;
        }
        return null;
    }

    public static string DisplayName(this SpeakingLanguage language) => Table.First(row => row.Language == language).DisplayName;
}

// MARK: - Answer Model

public enum AnswerModel
{
    Grok,
    Gpt
}

public static class AnswerModels
{
    public static readonly IReadOnlyList<AnswerModel> AllCases = new[] { AnswerModel.Grok, AnswerModel.Gpt };

    public static string RawValue(this AnswerModel model) => model == AnswerModel.Grok ? "grok" : "gpt";

    public static AnswerModel? FromRawValue(string? rawValue) => rawValue switch
    {
        "grok" => AnswerModel.Grok,
        "gpt" => AnswerModel.Gpt,
        _ => null
    };

    public static string DisplayName(this AnswerModel model) => model == AnswerModel.Grok ? "Grok (xAI)" : "GPT (OpenAI)";
}

// MARK: - App Settings

/// Global app settings, kept in the settings store under the keys the macOS app uses.
public sealed class AppSettings
{
    private static AppSettings? shared;

    public static AppSettings Shared
    {
        get => shared ??= new AppSettings();
        set => shared = value;
    }

    private const string RoleKey = "InterviewMaster.Role";
    private const string ProgrammingLanguageKey = "InterviewMaster.ProgrammingLanguage";
    private const string ListeningLanguageKey = "InterviewMaster.ListeningLanguage";
    private const string ResponseLanguageKey = "InterviewMaster.ResponseLanguage";
    private const string SpeakingLanguageKey = "InterviewMaster.SpeakingLanguage";
    private const string FrameworksKey = "InterviewMaster.Frameworks";
    private const string CaptureDeviceKey = "InterviewMaster.CaptureDevice";
    private const string AnswerModelKey = "InterviewMaster.AnswerModel";
    private const string OpenAIModelKey = "InterviewMaster.OpenAIModel";
    private const string OpenAIReasoningEffortKey = "InterviewMaster.OpenAIReasoningEffort";
    private const string OpenAIPriorityKey = "InterviewMaster.OpenAIPriority";
    private const string BackgroundTransparencyKey = "InterviewMaster.BackgroundTransparency";

    // Legacy keys for migration
    private const string LegacyLanguageKey = "InterviewMaster.Language";
    private const string LegacyTechStackKey = "InterviewMaster.TechStack";

    private readonly ISettingsStore? explicitStore;

    private ISettingsStore Store => explicitStore ?? SettingsStore.Standard;

    /// With no store given, reads and writes whatever `SettingsStore.Standard` is at the time.
    public AppSettings(ISettingsStore? store = null)
    {
        explicitStore = store;
        MigrateIfNeeded();
        MigrateLanguageSplitIfNeeded();
        NormalizeRoleIfNeeded();
    }

    public CaptureDevice CaptureDevice
    {
        get => CaptureDevices.FromRawValue(Store.GetString(CaptureDeviceKey)) ?? CaptureDevice.SystemAudio;
        set => Store.Set(CaptureDeviceKey, value.RawValue());
    }

    // MARK: - Role

    public InterviewRole Role
    {
        get => InterviewRoles.FromRawValue(Store.GetString(RoleKey))?.CanonicalRole() ?? InterviewRole.SeniorQAEngineer;
        set => Store.Set(RoleKey, value.CanonicalRole().RawValue());
    }

    // MARK: - Answer Model

    public AnswerModel AnswerModel
    {
        get => AnswerModels.FromRawValue(Store.GetString(AnswerModelKey) ?? "") ?? AnswerModel.Grok;
        set => Store.Set(AnswerModelKey, value.RawValue());
    }

    public string OpenAIAnswerModel => Store.GetString(OpenAIModelKey) ?? "gpt-5.5";

    /// none | minimal | low | medium | high. Higher is slower but more thorough.
    public string OpenAIReasoningEffort
    {
        get => Store.GetString(OpenAIReasoningEffortKey) ?? "low";
        set => Store.Set(OpenAIReasoningEffortKey, value);
    }

    public bool OpenAIPriority
    {
        get => Store.GetBool(OpenAIPriorityKey);
        set => Store.Set(OpenAIPriorityKey, value);
    }

    /// How see-through the window's background is, from 0 (dark and nearly solid) to 1 (nothing behind
    /// the text). It never touches the text.
    public double BackgroundTransparency
    {
        get => Store.GetDouble(BackgroundTransparencyKey) ?? 0.7;
        set => Store.Set(BackgroundTransparencyKey, Math.Min(1, Math.Max(0, value)));
    }

    // MARK: - Programming Language

    public ProgrammingLanguage ProgrammingLanguage
    {
        get => ProgrammingLanguages.FromRawValue(Store.GetString(ProgrammingLanguageKey)) ?? ProgrammingLanguage.Python;
        set => Store.Set(ProgrammingLanguageKey, value.RawValue());
    }

    // MARK: - Listening / Response Language

    public SpeakingLanguage ListeningLanguage
    {
        get => SpeakingLanguages.FromRawValue(Store.GetString(ListeningLanguageKey)) ?? SpeakingLanguage.English;
        set => Store.Set(ListeningLanguageKey, value.RawValue());
    }

    public SpeakingLanguage ResponseLanguage
    {
        get => SpeakingLanguages.FromRawValue(Store.GetString(ResponseLanguageKey)) ?? SpeakingLanguage.English;
        set
        {
            Store.Set(ResponseLanguageKey, value.RawValue());
            Store.Set(SpeakingLanguageKey, value.RawValue());
        }
    }

    /// Legacy name kept for existing call sites and stored sessions.
    public SpeakingLanguage SpeakingLanguage
    {
        get => ResponseLanguage;
        set => ResponseLanguage = value;
    }

    // MARK: - Frameworks/Tech Stack

    /// Comma-separated list of frameworks/tools (e.g., "Playwright, Pytest, AWS")
    public string Frameworks
    {
        get => Store.GetString(FrameworksKey) ?? "";
        set => Store.Set(FrameworksKey, value);
    }

    /// Frameworks as array
    public List<string> FrameworksList =>
        Frameworks.SplitOmittingEmpty(',').Select(item => item.TrimInline()).Where(item => item.Length > 0).ToList();

    public bool IsTestAutomationRole => Role.IsQA();

    public bool IsAIOrDataRole => Role.IsAIOrData();

    public string EffectiveFrameworks
    {
        get
        {
            var trimmed = Frameworks.Trim();
            if (trimmed.Length == 0 && IsTestAutomationRole)
            {
                return "Playwright, TypeScript, API Testing, CI";
            }
            return trimmed;
        }
    }

    public bool IsPlaywrightFocused
    {
        get
        {
            var lowerFrameworks = Frameworks.Trim().Lowercased();
            return IsTestAutomationRole && (lowerFrameworks.Length == 0 || lowerFrameworks.Contains("playwright", StringComparison.Ordinal));
        }
    }

    // MARK: - Convenience Properties

    /// Full context string for prompts
    public string InterviewContext
    {
        get
        {
            var context = $"Position: {Role.DisplayName()}\n";
            if (IsTestAutomationRole)
            {
                context += "Prompt Profile: Shared QA/SDET Playwright\n";
                context += $"QA Level: {Role.QaProfileLabel()}\n";
            }
            context += $"Programming Language: {ProgrammingLanguage.DisplayName()}\n";
            context += $"Listening Language: {ListeningLanguage.DisplayName()}\n";
            context += $"Response Language: {ResponseLanguage.DisplayName()}";
            var stack = EffectiveFrameworks;
            if (stack.Length > 0)
            {
                context += $"\nTech Stack: {stack}";
            }
            return context;
        }
    }

    /// How the answers should sound: the person's own way of speaking, for every prompt that writes one.
    public string CandidateVoiceInstruction => AnswerPromptBlocks.CandidateVoice;

    public string AnswerStyleInstruction
    {
        get
        {
            var instruction = $"""
                INTERVIEW ANSWER STYLE:
                - Answer as a spoken candidate answer, not as a textbook note.
                - Return cue-card bullets only: 3-5 lines, every line starts with "- ".
                - Each bullet should be short enough to read while speaking, ideally under 90 characters.
                - Plain text only. No markdown, no **bold**, no headings, no numbered lists.
                - For acronyms like SOLID, one bullet per letter as "S - Single Responsibility: ...".
                - No paragraphs. No long explanations. No markdown headings.
                - Only for "tell me about yourself" or overall-background questions, open with a years-of-experience headline. For project follow-ups, answer the specific point directly.
                - For "What is X?" concept questions, first bullet defines X plainly with the acronym expansion when useful.
                - No headings, markdown section labels, preambles, or filler.
                - Prefer concrete trade-offs, examples, and one senior signal over textbook lists.
                - If the transcript is noisy, answer the likely topic confidently.

                {CandidateVoiceInstruction}
                """;

            if (IsAIOrDataRole)
            {
                instruction += """

                    AI/ML FOCUS:
                    - For RAG/CAG, explain the retrieval flow: source data -> chunks/embeddings -> vector DB -> semantic search -> LLM answer with context.
                    - Mention documents, PDFs, web pages, images, video/audio transcripts, or internal knowledge when useful.
                    - Keep the tone concrete and human; avoid sterile phrases like "improves factuality" unless tied to retrieved context.
                    - For core ML questions, give the plain definition first, then the formula or intuition, when to use it, and one pitfall.
                    - Metrics: precision = TP/(TP+FP), recall = TP/(TP+FN), F1 = harmonic mean; prefer PR-AUC/F1 over accuracy on imbalanced data.
                    - Tie trade-offs to the business cost of false positives vs false negatives and to threshold choice.
                    - For overfitting, leakage, or tuning, mention train/validation/test splits, cross-validation, and fitting preprocessing on train only.
                    - For forecasting: check trend/seasonality, time-based split, simple baseline, then ARIMA/Prophet or XGBoost with lag/rolling features, backtest with MAE/RMSE.
                    - Only claim hands-on use of a model if the resume shows it; otherwise say how you would use and evaluate it.
                    """;
            }

            if (IsTestAutomationRole)
            {
                instruction += $"""

                    TEST AUTOMATION FOCUS:
                    - Use the shared QA/SDET Playwright profile for every QA role.
                    - {Role.QaSeniorityInstruction()}
                    - Default to Playwright unless the question names another automation framework.
                    - Mention locators, fixtures/test data, page objects, mocking, flake prevention, traces, or app-vs-test triage when relevant.
                    - For strategy questions, cover risk, coverage level, maintainability, and pipeline feedback speed.
                    """;
            }

            if (IsPlaywrightFocused)
            {
                instruction += """

                    PLAYWRIGHT DEFAULTS:
                    - Prefer web-first assertions like `await expect(locator).toBeVisible()` over manual waits.
                    - Prefer `getByRole`, `getByLabel`, and stable `data-testid` locators over CSS/XPath.
                    - Mention fixtures, `storageState`, browser contexts, `page.route`, trace viewer, projects, workers, retries, and sharding when relevant.
                    - For flaky tests: remove `waitForTimeout`, isolate data, check async UI state, and use trace/network artifacts to separate app bugs from test bugs.
                    """;
            }

            return instruction;
        }
    }

    /// Language instruction for LLM
    public string LanguageInstruction
    {
        get
        {
            if (ResponseLanguage == SpeakingLanguage.English)
            {
                return "OUTPUT LANGUAGE: English. Answer every candidate-facing bullet in English.";
            }
            var name = ResponseLanguage.DisplayName();
            return $"""
                OUTPUT LANGUAGE: {name}.
                Answer every candidate-facing bullet in {name}, even if the transcript, previous cards, or examples are in another language.
                Keep code, commands, API names, framework names, and short technical identifiers in English.
                """;
        }
    }

    /// Code block language for solutions
    public string CodeLanguage => ProgrammingLanguage.CodeBlockLang();

    /// Language code for Whisper API (e.g., "en", "bg", "de")
    public string LanguageCode => ListeningLanguage.RawValue();

    // MARK: - Legacy Compatibility

    /// Legacy property for backwards compatibility
    public SpeakingLanguage Language
    {
        get => ResponseLanguage;
        set => ResponseLanguage = value;
    }

    /// Legacy property - maps to programming language
    public LegacyTechStack TechStack
    {
        get => ProgrammingLanguage switch
        {
            ProgrammingLanguage.Python => IsTestAutomationRole ? LegacyTechStack.QaPython : LegacyTechStack.Python,
            ProgrammingLanguage.TypeScript => IsTestAutomationRole ? LegacyTechStack.QaTypeScript : LegacyTechStack.TypeScript,
            ProgrammingLanguage.JavaScript => LegacyTechStack.JavaScript,
            ProgrammingLanguage.Java => LegacyTechStack.Java,
            ProgrammingLanguage.CSharp => LegacyTechStack.CSharp,
            ProgrammingLanguage.Go => LegacyTechStack.Go,
            ProgrammingLanguage.Rust => LegacyTechStack.Rust,
            ProgrammingLanguage.Cpp => LegacyTechStack.Cpp,
            _ => LegacyTechStack.General
        };
        set
        {
            switch (value)
            {
                case LegacyTechStack.QaPython:
                    ProgrammingLanguage = ProgrammingLanguage.Python;
                    if (!Role.IsQA()) Role = InterviewRole.QAAutomationEngineer;
                    break;
                case LegacyTechStack.QaTypeScript:
                    ProgrammingLanguage = ProgrammingLanguage.TypeScript;
                    if (!Role.IsQA()) Role = InterviewRole.QAAutomationEngineer;
                    break;
                case LegacyTechStack.Python: ProgrammingLanguage = ProgrammingLanguage.Python; break;
                case LegacyTechStack.TypeScript: ProgrammingLanguage = ProgrammingLanguage.TypeScript; break;
                case LegacyTechStack.JavaScript: ProgrammingLanguage = ProgrammingLanguage.JavaScript; break;
                case LegacyTechStack.Java: ProgrammingLanguage = ProgrammingLanguage.Java; break;
                case LegacyTechStack.CSharp: ProgrammingLanguage = ProgrammingLanguage.CSharp; break;
                case LegacyTechStack.Go: ProgrammingLanguage = ProgrammingLanguage.Go; break;
                case LegacyTechStack.Rust: ProgrammingLanguage = ProgrammingLanguage.Rust; break;
                case LegacyTechStack.Cpp: ProgrammingLanguage = ProgrammingLanguage.Cpp; break;
                case LegacyTechStack.General: break;
            }
        }
    }

    public string LlmLanguageInstruction => LanguageInstruction;

    /// Vocabulary hints for Whisper based on role and tech stack
    public string WhisperVocabulary
    {
        get
        {
            const string common = "API, REST, SQL, NoSQL, Redis, Kafka, Docker, Kubernetes, microservices, CI/CD, Git, AWS, Azure, Big O, hash map, tree, graph";

            // Add language-specific vocabulary
            var vocab = ProgrammingLanguage switch
            {
                ProgrammingLanguage.Python => "Python, Django, Flask, FastAPI, dictionary, generator, decorator, async await, asyncio, pytest, Pydantic, ",
                ProgrammingLanguage.TypeScript or ProgrammingLanguage.JavaScript => "TypeScript, JavaScript, Node.js, React, Next.js, event loop, Promise, async await, npm, Jest, ",
                ProgrammingLanguage.Java => "Java, JVM, Spring Boot, Hibernate, JPA, Maven, Gradle, JUnit, HashMap, polymorphism, ",
                ProgrammingLanguage.CSharp => "C#, .NET, ASP.NET, Entity Framework, LINQ, async await, Task, NuGet, xUnit, ",
                ProgrammingLanguage.Go => "Go, Golang, goroutine, channel, defer, interface, struct, pointer, gin, GORM, ",
                ProgrammingLanguage.Rust => "Rust, ownership, borrowing, lifetime, trait, Option, Result, tokio, cargo, ",
                ProgrammingLanguage.Cpp => "C++, STL, vector, map, pointer, smart pointer, template, virtual function, CMake, ",
                ProgrammingLanguage.Kotlin => "Kotlin, coroutines, suspend, data class, sealed class, Spring, Android, ",
                _ => "Swift, SwiftUI, UIKit, optional, guard, protocol, extension, Combine, "
            };

            // Add role-specific vocabulary
            if (IsTestAutomationRole)
            {
                vocab += "Selenium, Playwright, Cypress, locator, test id, auto-waiting, trace viewer, E2E, API testing, mocking, fixtures, test data, POM, regression, flaky test, retries, CI pipeline, ";
            }

            if (IsPlaywrightFocused)
            {
                vocab += "Playwright Test, getByRole, getByLabel, locator, toBeVisible, toHaveText, web-first assertion, browser context, storageState, global setup, page.route, route.fulfill, HAR, test.step, workers, sharding, codegen, ";
            }

            if (IsAIOrDataRole)
            {
                vocab += "LLM, RAG, Retrieval-Augmented Generation, CAG, Cache-Augmented Generation, Context-Augmented Generation, embeddings, vector database, LangChain, fine-tuning, prompt engineering, transformer, backpropagation, PyTorch, TensorFlow, Hugging Face, MLOps, neural network, CNN, RNN, LSTM, ";
            }

            // Add custom frameworks
            if (Frameworks.Length > 0)
            {
                vocab += Frameworks + ", ";
            }

            if (LocalizedWhisperVocabulary.Length > 0)
            {
                vocab += LocalizedWhisperVocabulary + ", ";
            }

            return vocab + common;
        }
    }

    private string LocalizedWhisperVocabulary => ListeningLanguage switch
    {
        SpeakingLanguage.Bulgarian => "какво е, как работи, защо, обясни, разкажи, раскажи, разлика между, хеш мап, хешмапа, хеш таблица, хеш код, ООП, ОП, обектно-ориентирано, полиморфизъм, наследяване, капсулация, автоматизация на тестове, API тестове, регресия, нестабилен тест, мокване, фикстури, локатор",
        SpeakingLanguage.German => "was ist, wie funktioniert, warum, erklaere, erkläre, erzaehl, erzähl, unterschied zwischen, kannst du, koennen sie, können sie, was bedeutet, wann benutzt man, hash map, hash table, hashmap, objektorientiert, vererbung, kapselung, polymorphismus, testautomatisierung, API tests, regression, instabiler test, flaky test, mocking, fixtures, locator",
        _ => ""
    };

    // MARK: - Migration

    private void MigrateIfNeeded()
    {
        // Check if already migrated
        if (Store.GetString(RoleKey) is not null) return;

        // Migrate from legacy tech stack
        if (LegacyTechStacks.FromRawValue(Store.GetString(LegacyTechStackKey)) is { } legacy)
        {
            switch (legacy)
            {
                case LegacyTechStack.QaPython:
                    Role = InterviewRole.SeniorQAEngineer;
                    ProgrammingLanguage = ProgrammingLanguage.Python;
                    Frameworks = "Playwright, Pytest, API Testing";
                    break;
                case LegacyTechStack.QaTypeScript:
                    Role = InterviewRole.SeniorQAEngineer;
                    ProgrammingLanguage = ProgrammingLanguage.TypeScript;
                    Frameworks = "Playwright, Jest, API Testing";
                    break;
                case LegacyTechStack.Python:
                    Role = InterviewRole.BackendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.Python;
                    Frameworks = "Django, FastAPI";
                    break;
                case LegacyTechStack.TypeScript:
                    Role = InterviewRole.FrontendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.TypeScript;
                    Frameworks = "React, Next.js";
                    break;
                case LegacyTechStack.JavaScript:
                    Role = InterviewRole.FullStackDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.JavaScript;
                    Frameworks = "Node.js, Express, React";
                    break;
                case LegacyTechStack.Java:
                    Role = InterviewRole.BackendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.Java;
                    Frameworks = "Spring Boot, Hibernate";
                    break;
                case LegacyTechStack.CSharp:
                    Role = InterviewRole.BackendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.CSharp;
                    Frameworks = ".NET, Entity Framework";
                    break;
                case LegacyTechStack.Go:
                    Role = InterviewRole.BackendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.Go;
                    Frameworks = "Gin, GORM";
                    break;
                case LegacyTechStack.Rust:
                    Role = InterviewRole.BackendDeveloper;
                    ProgrammingLanguage = ProgrammingLanguage.Rust;
                    Frameworks = "Tokio, Actix";
                    break;
                case LegacyTechStack.Cpp:
                    Role = InterviewRole.SoftwareEngineer;
                    ProgrammingLanguage = ProgrammingLanguage.Cpp;
                    Frameworks = "STL, CMake";
                    break;
                case LegacyTechStack.General:
                    Role = InterviewRole.SoftwareEngineer;
                    ProgrammingLanguage = ProgrammingLanguage.Python;
                    break;
            }
            Log.Print("✅ Migrated legacy settings to new format");
        }

        // Migrate speaking language
        if (SpeakingLanguages.FromRawValue(Store.GetString(LegacyLanguageKey)) is { } language)
        {
            ListeningLanguage = language;
            ResponseLanguage = language;
        }
    }

    private void MigrateLanguageSplitIfNeeded()
    {
        var legacyLanguage = SpeakingLanguages.FromRawValue(Store.GetString(SpeakingLanguageKey))
            ?? SpeakingLanguages.FromRawValue(Store.GetString(LegacyLanguageKey))
            ?? SpeakingLanguage.English;

        if (Store.GetString(ListeningLanguageKey) is null)
        {
            Store.Set(ListeningLanguageKey, legacyLanguage.RawValue());
        }

        if (Store.GetString(ResponseLanguageKey) is null)
        {
            Store.Set(ResponseLanguageKey, legacyLanguage.RawValue());
        }
    }

    private void NormalizeRoleIfNeeded()
    {
        var code = Store.GetString(RoleKey);
        if (InterviewRoles.FromRawValue(code) is not { } storedRole) return;

        var canonical = storedRole.CanonicalRole();
        if (canonical.RawValue() != code)
        {
            Store.Set(RoleKey, canonical.RawValue());
        }
    }
}

// MARK: - Legacy Tech Stack (for backwards compatibility)

/// Legacy tech stack enum - kept for backwards compatibility
public enum LegacyTechStack
{
    Java,
    Python,
    JavaScript,
    TypeScript,
    Go,
    CSharp,
    Cpp,
    Rust,
    QaPython,
    QaTypeScript,
    General
}

public static class LegacyTechStacks
{
    private static readonly (LegacyTechStack Stack, string RawValue, string DisplayName)[] Table =
    {
        (LegacyTechStack.Java, "java", "Java/Spring"),
        (LegacyTechStack.Python, "python", "Python/Django"),
        (LegacyTechStack.JavaScript, "javascript", "JavaScript/Node"),
        (LegacyTechStack.TypeScript, "typescript", "TypeScript/React"),
        (LegacyTechStack.Go, "go", "Go/Golang"),
        (LegacyTechStack.CSharp, "csharp", "C#/.NET"),
        (LegacyTechStack.Cpp, "cpp", "C++"),
        (LegacyTechStack.Rust, "rust", "Rust"),
        (LegacyTechStack.QaPython, "qa_python", "QA/Python"),
        (LegacyTechStack.QaTypeScript, "qa_typescript", "QA/TypeScript"),
        (LegacyTechStack.General, "general", "General/Mixed")
    };

    public static readonly IReadOnlyList<LegacyTechStack> AllCases = Table.Select(row => row.Stack).ToArray();

    public static string RawValue(this LegacyTechStack stack) => Table.First(row => row.Stack == stack).RawValue;

    public static LegacyTechStack? FromRawValue(string? rawValue)
    {
        foreach (var row in Table)
        {
            if (row.RawValue == rawValue) return row.Stack;
        }
        return null;
    }

    public static string DisplayName(this LegacyTechStack stack) => Table.First(row => row.Stack == stack).DisplayName;
}

/// Menu choices for the live answer model.
public sealed record AnswerModelOption(string Title, AnswerModel Model, string? ReasoningEffort, bool Priority = false)
{
    public static readonly IReadOnlyList<AnswerModelOption> All = new[]
    {
        new AnswerModelOption("Grok 4.20 (fastest)", AnswerModel.Grok, null),
        new AnswerModelOption("GPT-5.5 (no reasoning)", AnswerModel.Gpt, "none"),
        new AnswerModelOption("GPT-5.5 (low reasoning)", AnswerModel.Gpt, "low"),
        new AnswerModelOption("GPT-5.5 (low reasoning, priority)", AnswerModel.Gpt, "low", Priority: true)
    };

    public static AnswerModelOption Current
    {
        get
        {
            var settings = AppSettings.Shared;
            if (settings.AnswerModel == AnswerModel.Grok) return All[0];
            return All.FirstOrDefault(option =>
                option.Model == AnswerModel.Gpt
                && option.ReasoningEffort == settings.OpenAIReasoningEffort
                && option.Priority == settings.OpenAIPriority) ?? All[2];
        }
    }
}
