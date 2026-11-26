namespace InterviewMaster.Domain;

public sealed class InterviewMemory
{
    public UserProfile UserProfile { get; set; } = new();
    public List<SessionSummary> SessionSummaries { get; set; } = new();
    public List<KnowledgeEntry> KnowledgeEntries { get; set; } = new();
}

public sealed class UserProfile
{
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
    public List<string> Experience { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public sealed class SessionSummary
{
    public string Id { get; init; } = "";
    public DateTime Date { get; init; }
    public List<string> Topics { get; init; } = new();
    public int QuestionsAsked { get; init; }
    public List<string> KeyInsights { get; init; } = new();
    public string SourceFile { get; init; } = "";
}

public sealed class KnowledgeEntry
{
    public string Id { get; init; } = "";
    public string Topic { get; set; } = "";
    public string Content { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public DateTime CreatedDate { get; init; }
    public DateTime? LastUsedDate { get; set; }
}
