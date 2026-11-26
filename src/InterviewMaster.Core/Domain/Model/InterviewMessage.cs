using System.Globalization;

namespace InterviewMaster.Domain;

/// Device that captured an interviewer utterance, independent of speaker identity.
public enum CaptureDevice
{
    Microphone,
    SystemAudio
}

public static class CaptureDevices
{
    public static readonly IReadOnlyList<CaptureDevice> AllCases = new[] { CaptureDevice.Microphone, CaptureDevice.SystemAudio };

    public static string RawValue(this CaptureDevice device) => device switch
    {
        CaptureDevice.Microphone => "microphone",
        _ => "system"
    };

    public static CaptureDevice? FromRawValue(string? rawValue) => rawValue switch
    {
        "microphone" => CaptureDevice.Microphone,
        "system" => CaptureDevice.SystemAudio,
        _ => null
    };

    public static string DisplayName(this CaptureDevice device) => device switch
    {
        CaptureDevice.Microphone => "Microphone",
        _ => "System audio"
    };
}

public enum MessageType
{
    /// Interviewer question
    Question,
    /// Generated AI answer
    Answer,
    /// User's spoken response (detected as "answer")
    UserResponse,
    /// Follow-up answer
    FollowUp,
    /// System status message
    Status,
    /// Screenshot thumbnail in timeline
    Screenshot,
    /// Pinned coding task solution (full width, larger)
    CodingTask,
    /// In-place conversation digest + resume talking points
    Briefing
}

/// Represents a message in the interview timeline. A value in Swift: copy it with `with` before changing
/// a copy that must not affect the original.
public sealed record InterviewMessage
{
    public Guid Id { get; init; }
    public DateTime Timestamp { get; init; }
    public MessageType Type { get; init; }
    public string Content { get; init; }
    public string? Topic { get; init; }
    public Guid? TurnId { get; init; }
    public int? TurnSequence { get; init; }

    /// For screenshot type messages
    public Guid? ScreenshotId { get; set; }

    /// Device that captured the audio
    public CaptureDevice? AudioSource { get; set; }

    /// For userResponse - collapsed by default
    public bool IsCollapsed { get; set; }

    /// Time from question end to completed answer
    public int? ResponseLatencyMs { get; set; }

    public InterviewMessage(
        MessageType type, string content, string? topic = null, Guid? screenshotId = null,
        CaptureDevice? audioSource = null, int? responseLatencyMs = null, Guid? turnId = null, int? turnSequence = null)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.Now;
        Type = type;
        Content = content;
        Topic = topic;
        TurnId = turnId;
        TurnSequence = turnSequence;
        ScreenshotId = screenshotId;
        AudioSource = audioSource;
        IsCollapsed = type == MessageType.UserResponse;
        ResponseLatencyMs = responseLatencyMs;
    }

    public bool IsQuestion => Type == MessageType.Question;

    public bool IsAnswer => Type is MessageType.Answer or MessageType.FollowUp;

    public string DisplayTime => Timestamp.ToString("T", CultureInfo.CurrentCulture);

    public string? DisplayLatency => ResponseLatencyMs is { } ms ? FormatLatency(ms) : null;

    /// "850ms" under a second, "2.4s" above. "F1" rounds as `%.1f` does; a custom "0.0" format does not.
    public static string FormatLatency(int ms) =>
        ms >= 1000
            ? (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "s"
            : $"{ms}ms";
}
