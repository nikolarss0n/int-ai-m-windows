using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// The words for a topic the classifier named in camelCase ("systemDesign", "llm", "ragCag"). The answer
/// card, the timeline pill and an export all show it.
public static class TopicTitle
{
    /// The card's title before any topic is known.
    public const string Unknown = "Live answer";

    /// Whole topics that read better as a phrase than as their words joined.
    private static readonly Dictionary<string, string> Phrases = new()
    {
        ["ragcag"] = "RAG and CAG",
        ["followup"] = "Follow-up",
        ["kmeans"] = "K-means",
        ["genai"] = "Generative AI",
        ["finetuning"] = "Fine-tuning",
        ["biasvariance"] = "Bias-variance",
        ["crossvalidation"] = "Cross-validation",
        ["precisionrecall"] = "Precision and recall",
        ["webfirstassertions"] = "Web-first assertions",
        ["autowaiting"] = "Auto-waiting",
        ["langchain"] = "LangChain",
        ["huggingface"] = "Hugging Face",
        ["playwrighttest"] = "Playwright Test"
    };

    /// Words that are names or acronyms, written the way they are written everywhere else.
    private static readonly Dictionary<string, string> Words = new()
    {
        ["ai"] = "AI", ["api"] = "API", ["aws"] = "AWS", ["cag"] = "CAG", ["cicd"] = "CI/CD", ["cpp"] = "C++", ["csharp"] = "C#",
        ["dbscan"] = "DBSCAN", ["devops"] = "DevOps", ["dotnet"] = ".NET", ["e2e"] = "E2E", ["fastapi"] = "FastAPI",
        ["graphql"] = "GraphQL", ["javascript"] = "JavaScript", ["jvm"] = "JVM", ["linq"] = "LINQ", ["llm"] = "LLM",
        ["ml"] = "ML", ["mlops"] = "MLOps", ["nlp"] = "NLP", ["nosql"] = "NoSQL", ["oop"] = "OOP", ["auc"] = "AUC", ["pr"] = "PR",
        ["rag"] = "RAG", ["rest"] = "REST", ["roc"] = "ROC", ["solid"] = "SOLID", ["sql"] = "SQL", ["stl"] = "STL",
        ["swiftui"] = "SwiftUI", ["typescript"] = "TypeScript", ["ui"] = "UI", ["uikit"] = "UIKit"
    };

    /// "systemDesign" becomes "System design", "llmEvaluation" "LLM evaluation", "csharp" "C#".
    public static string Display(string topic)
    {
        var trimmed = topic.Trim();
        if (trimmed.Length == 0 || trimmed.Lowercased() == "unknown") return Unknown;
        var key = trimmed.Lowercased().Where(c => char.IsLetter(c) || char.IsNumber(c));
        if (Phrases.TryGetValue(key, out var phrase)) return phrase;
        var parts = Split(trimmed);
        return string.Join(" ", parts.Select((part, index) =>
        {
            var lowered = part.Lowercased();
            if (Words.TryGetValue(lowered, out var name)) return name;
            return index == 0 ? lowered.Prefix(1).Uppercased() + lowered.DropFirst() : lowered;
        }));
    }

    /// The words of a camelCase or snake_case name. A run of capitals is one word ("genAI" is "gen", "AI"),
    /// and digits stay with the letters before them ("f1Score" is "f1", "Score").
    public static List<string> Split(string topic)
    {
        var parts = new List<string>();
        var current = "";
        for (var index = 0; index < topic.Length; index++)
        {
            var character = topic[index];
            if (character == '_' || character == '-' || character == ' ')
            {
                if (current.Length > 0) parts.Add(current);
                current = "";
                continue;
            }
            if (char.IsUpper(character) && current.Length > 0)
            {
                var previous = current[^1];
                var nextIsLower = index + 1 < topic.Length && char.IsLower(topic[index + 1]);
                var endsWord = char.IsLower(previous) || char.IsNumber(previous) || (char.IsUpper(previous) && nextIsLower);
                if (endsWord)
                {
                    parts.Add(current);
                    current = "";
                }
            }
            current += character;
        }
        if (current.Length > 0) parts.Add(current);
        return parts;
    }
}
