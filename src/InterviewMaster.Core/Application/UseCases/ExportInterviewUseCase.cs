using System.Globalization;
using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

public enum ExportFormat
{
    Markdown,
    Json
}

// Properties are declared in key order: the macOS encoder sorts keys, and exports should read the same.
public sealed record ExportData(string ExportDate, IReadOnlyList<ExportData.ExportMessage> Messages, ExportData.ExportSettings Settings, string Version)
{
    public sealed record ExportSettings(
        string Frameworks,
        string ListeningLanguage,
        string ProgrammingLanguage,
        string ResponseLanguage,
        string Role,
        string SpeakingLanguage);

    public sealed record ExportMessage(string? AudioSource, string Content, int? LatencyMs, string Timestamp, string? Topic, string Type);
}

public sealed class ExportInterviewUseCase
{
    private readonly Func<DateTime> now;
    private readonly AppSettings? explicitSettings;

    public ExportInterviewUseCase(Func<DateTime>? now = null, AppSettings? settings = null)
    {
        this.now = now ?? (() => DateTime.Now);
        explicitSettings = settings;
    }

    private AppSettings Settings => explicitSettings ?? AppSettings.Shared;

    /// The messages of the interview that started at `startedAt`. The timeline keeps earlier interviews,
    /// and their turn numbers start again at 1, so mixing them would interleave two conversations.
    public static List<InterviewMessage> Messages(IEnumerable<InterviewMessage> messages, DateTime? startedAt) =>
        startedAt is { } start ? messages.Where(message => message.Timestamp >= start).ToList() : messages.ToList();

    public string Execute(IEnumerable<InterviewMessage> messages, ExportFormat format)
    {
        var exportable = messages
            .Where(message => message.Type is MessageType.Question or MessageType.Answer or MessageType.FollowUp or MessageType.CodingTask or MessageType.Briefing)
            .OrderBy(message => message, Comparer<InterviewMessage>.Create(Compare))
            .ToList();
        return format == ExportFormat.Markdown ? FormatMarkdown(exportable) : FormatJson(exportable);
    }

    private string FormatMarkdown(List<InterviewMessage> messages)
    {
        var settings = Settings;
        var date = now();
        var culture = CultureInfo.CurrentCulture;
        var markdown = "# Interview Transcript\n\n";
        markdown += $"**Date:** {date.ToString("MMMM d, yyyy", culture)} at {date.ToString("t", culture)}\n";
        markdown += $"**Role:** {settings.Role.DisplayName()}\n";
        markdown += $"**Programming Language:** {settings.ProgrammingLanguage.DisplayName()}\n";
        markdown += $"**Listening Language:** {settings.ListeningLanguage.DisplayName()}\n";
        markdown += $"**Response Language:** {settings.ResponseLanguage.DisplayName()}\n";
        if (settings.Frameworks.Length > 0)
        {
            markdown += $"**Frameworks:** {settings.Frameworks}\n";
        }
        markdown += "\n";
        markdown += "**Legend:**\n";
        markdown += "- 🎙️ **Interviewer** - Questions from the interviewer\n";
        markdown += "- 💡 **Suggested Answer** - AI-generated answer hints\n\n";
        markdown += "---\n\n";

        string? currentTopic = null;
        foreach (var message in messages)
        {
            if (message.Topic is { } topic && topic != currentTopic && topic != "unknown")
            {
                markdown += $"## {TopicTitle.Display(topic)}\n\n";
                currentTopic = topic;
            }

            switch (message.Type)
            {
                case MessageType.Question:
                    markdown += $"### 🎙️ Interviewer <small>({message.DisplayTime})</small>\n\n";
                    markdown += $"> {message.Content}\n\n";
                    break;
                case MessageType.Answer or MessageType.FollowUp:
                    var header = "### 💡 Suggested Answer";
                    if (message.DisplayLatency is { } latency) header += $" <small>({latency})</small>";
                    markdown += $"{header}\n\n";
                    markdown += $"{message.Content}\n\n";
                    break;
                case MessageType.CodingTask:
                    markdown += "### 💻 Coding Solution\n\n";
                    markdown += $"{message.Content}\n\n";
                    break;
                case MessageType.Briefing:
                    markdown += "### Briefing\n\n";
                    markdown += $"{message.Content}\n\n";
                    break;
            }
        }

        markdown += "---\n\n*Exported from Interview Master*\n";
        return markdown;
    }

    private string FormatJson(List<InterviewMessage> messages)
    {
        var settings = Settings;
        var data = new ExportData(
            ExportDate: Iso8601(now()),
            Messages: messages.Select(message => new ExportData.ExportMessage(
                AudioSource: message.AudioSource?.RawValue(),
                Content: message.Content,
                LatencyMs: message.ResponseLatencyMs,
                Timestamp: Iso8601(message.Timestamp),
                Topic: message.Topic,
                Type: TypeString(message.Type))).ToList(),
            Settings: new ExportData.ExportSettings(
                Frameworks: settings.Frameworks,
                ListeningLanguage: settings.ListeningLanguage.DisplayName(),
                ProgrammingLanguage: settings.ProgrammingLanguage.DisplayName(),
                ResponseLanguage: settings.ResponseLanguage.DisplayName(),
                Role: settings.Role.DisplayName(),
                SpeakingLanguage: settings.ResponseLanguage.DisplayName()),
            Version: "1.0");
        return JsonSerializer.Serialize(data, AppJson.Indented);
    }

    private static string Iso8601(DateTime date) =>
        (date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Local) : date)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string TypeString(MessageType type) => type switch
    {
        MessageType.Question => "question",
        MessageType.Answer => "answer",
        MessageType.FollowUp => "followUp",
        MessageType.CodingTask => "codingTask",
        MessageType.UserResponse => "userResponse",
        MessageType.Status => "status",
        MessageType.Screenshot => "screenshot",
        _ => "briefing"
    };

    private static int Compare(InterviewMessage left, InterviewMessage right)
    {
        if (left.TurnSequence is { } leftSequence && right.TurnSequence is { } rightSequence)
        {
            return leftSequence != rightSequence ? leftSequence.CompareTo(rightSequence) : TypeOrder(left.Type).CompareTo(TypeOrder(right.Type));
        }
        return left.Timestamp.CompareTo(right.Timestamp);
    }

    private static int TypeOrder(MessageType type) => type switch
    {
        MessageType.Question => 0,
        MessageType.Answer or MessageType.FollowUp => 1,
        _ => 2
    };
}
