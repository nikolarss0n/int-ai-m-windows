using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

public sealed class SessionAnalysisUseCase
{
    private readonly MemoryStore? memoryStore;
    private readonly Func<DateTime> now;

    public SessionAnalysisUseCase(MemoryStore? memoryStore = null, Func<DateTime>? now = null)
    {
        this.memoryStore = memoryStore;
        this.now = now ?? (() => DateTime.UtcNow);
    }

    internal static string Transcript(IEnumerable<InterviewMessage> exportable) =>
        string.Join("\n", exportable.Select(message =>
        {
            var role = message.Type switch
            {
                MessageType.Question => "Interviewer",
                MessageType.Answer or MessageType.FollowUp => "Answer",
                MessageType.CodingTask => "Code Solution",
                _ => "Other"
            };
            var topic = message.Topic is { } value ? $" [{value}]" : "";
            return $"{role}{topic}: {message.Content}";
        }));

    /// Runs in the background, as on macOS; the returned task is there for callers that want to wait.
    public Task Analyze(IReadOnlyList<InterviewMessage> messages, string sourceFile, AnthropicClient anthropicClient)
    {
        var exportable = messages
            .Where(message => message.Type is MessageType.Question or MessageType.Answer or MessageType.FollowUp or MessageType.CodingTask)
            .ToList();
        if (exportable.Count < 2) return Task.CompletedTask;

        var transcript = Transcript(exportable);

        return Task.Run(async () =>
        {
            try
            {
                var result = await anthropicClient.AnalyzeSessionAsync(transcript).ConfigureAwait(false);
                var store = memoryStore ?? MemoryStore.Shared;
                store.AddSessionSummary(new SessionSummary
                {
                    Id = Guid.NewGuid().ToString().ToUpperInvariant(),
                    Date = now(),
                    Topics = result.Topics.ToList(),
                    QuestionsAsked = exportable.Count(message => message.Type == MessageType.Question),
                    KeyInsights = result.Insights.ToList(),
                    SourceFile = sourceFile
                });
                store.UpdateUserProfile(result.Strengths, result.Weaknesses);
                Log.Print($"🧠 Session analysis complete: {result.Topics.Count} topics, {result.Insights.Count} insights");
            }
            catch (Exception error)
            {
                Log.Print($"⚠️ Session analysis failed: {error.Message}");
            }
        });
    }
}
