using System.Text.Json;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Simple JSON-based Q&A database for instant answers.
public sealed class QADatabase
{
    private static QADatabase? shared;

    public static QADatabase Shared => shared ??= new QADatabase();

    public sealed record QAEntry(string Question, string Answer);

    private readonly Dictionary<string, QAEntry> database = new(StringComparer.Ordinal);

    public QADatabase(IEnumerable<string>? candidatePaths = null)
    {
        foreach (var path in candidatePaths ?? DefaultPaths())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, QAEntry>>(File.ReadAllBytes(path), AppJson.Compact);
                if (loaded is null) continue;
                foreach (var (topic, entry) in loaded) database[topic] = entry;
                Log.Print($"📚 QA Database loaded: {database.Count} topics from {path}");
                return;
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Print($"❌ Failed to decode QA database: {error.Message}");
            }
        }
        Log.Print("⚠️ QA Database not found, will use LLM for all answers");
    }

    /// Get answer for a topic. Returns null if not found (use LLM fallback).
    public string? GetAnswer(string topic)
    {
        if (database.TryGetValue(topic, out var exact)) return exact.Answer;
        var lowerTopic = topic.Lowercased();
        foreach (var (key, entry) in database)
        {
            if (key.Lowercased() == lowerTopic) return entry.Answer;
        }
        return null;
    }

    public bool HasTopic(string topic) => GetAnswer(topic) is not null;

    public List<string> Topics => database.Keys.Order(StringComparer.Ordinal).ToList();

    /// Beside the executable first, where the app ships it, then the working directory and its parent.
    private static List<string> DefaultPaths()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var paths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "qa_database.json"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "qa_database.json"),
            Path.Combine(workingDirectory, "Resources", "qa_database.json")
        };
        if (Path.GetDirectoryName(workingDirectory) is { } parent) paths.Add(Path.Combine(parent, "Resources", "qa_database.json"));
        return paths;
    }
}
