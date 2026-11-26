using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

public sealed class MemoryRetrievalUseCase
{
    private const int MaxTokenBudget = AppConstants.MaxTokens.MemoryContext;

    private readonly MemoryStore? explicitStore;

    public MemoryRetrievalUseCase(MemoryStore? store = null)
    {
        explicitStore = store;
    }

    public string? Retrieve(IReadOnlyList<string> topics)
    {
        if (topics.Count == 0) return null;

        var (sessions, knowledge, profile) = (explicitStore ?? MemoryStore.Shared).SearchByTopics(topics);
        var parts = new List<string>();

        // Past session insights
        if (sessions.Count > 0)
        {
            var count = sessions.Count;
            parts.Add($"You've been asked about {topics[0]} {count} time{(count == 1 ? "" : "s")} before");
            parts.AddRange(sessions[^1].KeyInsights.Take(2));
        }

        // Relevant strengths/weaknesses
        var topicsLower = topics.Select(topic => topic.Lowercased()).Distinct(StringComparer.Ordinal).ToList();
        var relevantStrengths = profile.Strengths.Where(strength => topicsLower.Any(topic => strength.Lowercased().Contains(topic, StringComparison.Ordinal))).ToList();
        var relevantWeaknesses = profile.Weaknesses.Where(weakness => topicsLower.Any(topic => weakness.Lowercased().Contains(topic, StringComparison.Ordinal))).ToList();
        if (relevantStrengths.Count > 0) parts.Add($"Strength: {string.Join(", ", relevantStrengths)}");
        if (relevantWeaknesses.Count > 0) parts.Add($"Area to improve: {string.Join(", ", relevantWeaknesses)}");

        // Knowledge notes
        foreach (var entry in knowledge.Take(2))
        {
            parts.Add($"Your note: \"{entry.Content}\"");
        }

        if (parts.Count == 0) return null;

        // Enforce token budget (~4 chars per token approximation)
        var result = "MEMORY (from past sessions):\n";
        var characterCount = result.Length;
        foreach (var part in parts)
        {
            var line = $"• {part}\n";
            if (characterCount + line.Length > MaxTokenBudget * 4) break;
            result += line;
            characterCount += line.Length;
        }
        return result;
    }
}
