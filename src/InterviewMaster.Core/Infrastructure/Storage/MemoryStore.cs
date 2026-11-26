using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

public sealed class MemoryStore
{
    private static MemoryStore? shared;

    public static MemoryStore Shared
    {
        get => shared ??= new MemoryStore();
        set => shared = value;
    }

    private readonly object gate = new();
    private readonly string filePath;
    private readonly string sessionsDirectory;
    private InterviewMemory memory = new();

    /// An injectable directory keeps the store verifiable without touching the user's real memory.
    public MemoryStore(string? directory = null)
    {
        var folder = directory ?? AppPaths.DocumentsRoot;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        filePath = Path.Combine(folder, "memory.json");
        sessionsDirectory = Path.Combine(folder, "sessions");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(filePath))
        {
            Log.Print("🧠 No memory file found, starting fresh");
            return;
        }
        try
        {
            memory = JsonSerializer.Deserialize<InterviewMemory>(File.ReadAllBytes(filePath), AppJson.Indented) ?? new InterviewMemory();
            Log.Print($"🧠 Loaded memory: {memory.SessionSummaries.Count} sessions, {memory.KnowledgeEntries.Count} knowledge entries");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Print($"⚠️ Failed to load memory: {error.Message}");
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllBytes(filePath, JsonSerializer.SerializeToUtf8Bytes(memory, AppJson.Indented));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Print($"⚠️ Failed to save memory: {error.Message}");
        }
    }

    public void AddSessionSummary(SessionSummary summary)
    {
        lock (gate)
        {
            memory.SessionSummaries.Add(summary);
            Save();
        }
        Log.Print($"🧠 Saved session summary: {summary.Id} ({summary.Topics.Count} topics)");
    }

    public List<SessionSummary> GetSessionSummaries()
    {
        lock (gate) return memory.SessionSummaries.ToList();
    }

    public bool HasSessionForFile(string filename)
    {
        lock (gate) return memory.SessionSummaries.Any(summary => summary.SourceFile == filename);
    }

    public UserProfile GetUserProfile()
    {
        lock (gate) return Copy(memory.UserProfile);
    }

    public void UpdateUserProfile(IEnumerable<string>? strengths, IEnumerable<string>? weaknesses)
    {
        lock (gate)
        {
            AddMissing(memory.UserProfile.Strengths, strengths);
            AddMissing(memory.UserProfile.Weaknesses, weaknesses);
            memory.UserProfile.LastUpdated = DateTime.UtcNow;
            Save();
        }
    }

    public void AddKnowledge(string topic, string content, IEnumerable<string> keywords)
    {
        var entry = new KnowledgeEntry
        {
            Id = Guid.NewGuid().ToString().ToUpperInvariant(),
            Topic = topic,
            Content = content,
            Keywords = keywords.ToList(),
            CreatedDate = DateTime.UtcNow,
            LastUsedDate = null
        };
        lock (gate)
        {
            memory.KnowledgeEntries.Add(entry);
            Save();
        }
    }

    public (List<SessionSummary> Sessions, List<KnowledgeEntry> Knowledge, UserProfile Profile) SearchByTopics(IEnumerable<string> topics)
    {
        lock (gate)
        {
            var topicsLower = topics.Select(topic => topic.Lowercased()).ToList();
            var matchingSessions = memory.SessionSummaries
                .Where(session => session.Topics.Any(topic => topicsLower.Contains(topic.Lowercased())))
                .TakeLast(3)
                .ToList();
            var matchingKnowledge = memory.KnowledgeEntries
                .Where(entry => topicsLower.Contains(entry.Topic.Lowercased())
                    || entry.Keywords.Any(keyword => topicsLower.Contains(keyword.Lowercased())))
                .ToList();
            return (matchingSessions, matchingKnowledge, Copy(memory.UserProfile));
        }
    }

    public string SessionsDirectory() => sessionsDirectory;

    private static void AddMissing(List<string> existing, IEnumerable<string>? additions)
    {
        if (additions is null) return;
        foreach (var addition in additions)
        {
            var lower = addition.Lowercased();
            if (!existing.Any(item => item.Lowercased() == lower)) existing.Add(addition);
        }
    }

    private static UserProfile Copy(UserProfile profile) => new()
    {
        Strengths = profile.Strengths.ToList(),
        Weaknesses = profile.Weaknesses.ToList(),
        Experience = profile.Experience.ToList(),
        LastUpdated = profile.LastUpdated
    };
}
