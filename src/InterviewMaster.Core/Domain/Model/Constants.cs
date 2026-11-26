using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Whether content floats on the desktop, or sits on filled surfaces because the system asks for less
/// transparency.
public enum SurfaceTransparency
{
    Solid,
    Balanced
}

public static class SurfaceTransparencyExtensions
{
    public static string Title(this SurfaceTransparency transparency) => transparency switch
    {
        SurfaceTransparency.Solid => "Solid",
        _ => "Floating"
    };

    public static string RawValue(this SurfaceTransparency transparency) => transparency switch
    {
        SurfaceTransparency.Solid => "solid",
        _ => "balanced"
    };
}

public static class LayoutConstants
{
    public static class Typography
    {
        public const double Hero = 24;
        public const double H1 = 20;
        public const double H2 = 18;
        public const double Body = 15;
        public const double BodyLarge = 17;
        public const double Caption = 13;
        public const double CaptionSmall = 11;
        public const double Micro = 10;
        public const double Code = 14;
    }

    public static class Spacing
    {
        public const double Xs = 4;
        public const double Sm = 8;
        public const double Md = 12;
        public const double Lg = 20;
        public const double Xl = 30;
    }

    public static class CornerRadius
    {
        public const double Small = 6;
        public const double Medium = 10;
        public const double Large = 14;
        public const double Pill = 16;
    }

    public static class IconButton
    {
        public const double Size = 32;
        public const double IconSize = 16;
        public const double Spacing = 8;
    }

    public static class Toolbar
    {
        public const double Height = 40;
        public const double DropdownHeight = 26;
    }

    public static class Timeline
    {
        public const double MessageSpacing = 15;
        public const double MessagePadding = 10;
        public const double HorizontalInset = 20;
        public const double BadgeSize = 22;
        public const double BadgeGap = 8;
        public const double AnswerIndent = 20;
        public const double AccentBarWidth = 3;
    }

    public static class Animation
    {
        public const double Fast = 0.1;
        public const double Normal = 0.15;
        public const double Slow = 0.25;
        public const double Pulse = 0.5;
    }

    public static class Alpha
    {
        public const double ActiveBackground = 0.15;
        public const double InactiveBackground = 0.08;
        public const double HoverBackground = 0.12;
        public const double SubtleText = 0.5;
        public const double SecondaryText = 0.7;
    }
}

public static class AppConstants
{
    public static class Models
    {
        public const string AnthropicHaiku = "claude-haiku-4-5-20251001";

        /// xAI Grok used for classification + answer streaming (Haiku replacement).
        /// Non-reasoning variant prioritizes time-to-first-token for live interview cues.
        public const string XaiGrok = "grok-4.20-0309-non-reasoning";
        public const string GroqWhisper = "whisper-large-v3-turbo";
        public const string GroqFastClassification = "openai/gpt-oss-20b";

        /// 20B is faster to first/last token than 120B on cue-card answers
        /// without a quality drop on the voice bench.
        public const string GroqFastAnswer = "openai/gpt-oss-20b";
        public const string GroqChatModel = GroqFastAnswer;
    }

    public static class ApiUrls
    {
        public const string AnthropicMessages = "https://api.anthropic.com/v1/messages";
        public const string XaiChat = "https://api.x.ai/v1/chat/completions";
        public const string OpenAIChat = "https://api.openai.com/v1/chat/completions";
        public const string GroqTranscriptions = "https://api.groq.com/openai/v1/audio/transcriptions";
        public const string GroqChat = "https://api.groq.com/openai/v1/chat/completions";
    }

    /// Durations are in seconds, as `TimeInterval` is in the macOS sources.
    public static class Thresholds
    {
        public const double DedupeWindow = 5.0;
        public const double SimilarityThreshold = 0.5;
        public const double BufferTimeout = 10.0;
        public const double BufferedQuestionSilence = 2.5;
        public const double QuestionContinuationWindow = 5.0;
        public const double AnswerCooldown = 12.0;
        public const float SpeechThreshold = 0.5f;
        public const double SilenceTimeout = 0.65;

        /// Start STT as soon as silence looks real so transcription overlaps
        /// the remaining end-of-speech wait. Keep this below silenceTimeout.
        public const double SpeculativeSilenceTimeout = 0.25;
        public const double MinSpeechDuration = 0.5;
        public const double DirectHaikuFirstChunkTimeout = 1.1;

        /// GPT-5.5 with reasoning starts later (~1.4s median, ~1.7s p90); a short timeout wastes a full retry.
        public const double DirectGptFirstChunkTimeout = 4.0;
        public const int SlidingWindowSize = 6;
        public const int SummarizationThreshold = 10;
        public const int MaxConversationHistory = 50;
    }

    public static class MaxTokens
    {
        public const int Classification = 320;

        /// GPT-5 reasoning tokens count against max_completion_tokens.
        public const int OpenAIReasoningAllowance = 4000;
        public const int AnswerStream = 450;
        public const int Summarization = 150;
        public const int ImageAnalysis = 4096;
        public const int CodingSteps = 1800;
        public const int GroqAnswer = 280;
        public const int GroqClassification = 240;
        public const int FollowUp = 350;
        public const int MemoryContext = 200;
        public const int SessionAnalysis = 300;
    }

    public static class Limits
    {
        public const int GroqWhisperPromptBytes = 896;
    }
}

public enum SpeechTurnEmit
{
    SpeculativePreview,
    SpeechResumedAfterPreview,
    FinalSilence
}

public enum SpeechTurnAction
{
    PrefetchTranscriptionOnly,
    CancelInFlightWork,
    CommitAnswer
}

public static class SpeechTurnPolicy
{
    public static SpeechTurnAction Action(SpeechTurnEmit emit) => emit switch
    {
        SpeechTurnEmit.SpeculativePreview => SpeechTurnAction.PrefetchTranscriptionOnly,
        SpeechTurnEmit.SpeechResumedAfterPreview => SpeechTurnAction.CancelInFlightWork,
        _ => SpeechTurnAction.CommitAnswer
    };

    public static bool StartsAnswerCard(SpeechTurnAction action) => action == SpeechTurnAction.CommitAnswer;
}

public static class GroqRequestTuning
{
    public const string CueCardPrefill = "- ";

    public static (string Filename, string MimeType) TranscriptionUpload(ReadOnlySpan<byte> audioData)
    {
        if (audioData.Length >= 12)
        {
            var header = audioData.Slice(0, 12);
            if (header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8))
            {
                return ("audio.wav", "audio/wav");
            }
        }
        if (audioData.Length >= 8 && audioData.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return ("audio.m4a", "audio/mp4");
        }
        return ("audio.wav", "audio/wav");
    }

    public static Dictionary<string, object> ChatReasoningFields(string model)
    {
        if (model.HasPrefix("openai/gpt-oss"))
        {
            return new Dictionary<string, object>
            {
                ["reasoning_effort"] = "low",
                ["include_reasoning"] = false
            };
        }
        if (model.HasPrefix("qwen/"))
        {
            return new Dictionary<string, object> { ["reasoning_effort"] = "none" };
        }
        return new Dictionary<string, object>();
    }

    public static List<Dictionary<string, string>> ChatMessages(string userPrompt) => new()
    {
        new Dictionary<string, string> { ["role"] = "user", ["content"] = userPrompt },
        new Dictionary<string, string> { ["role"] = "assistant", ["content"] = CueCardPrefill }
    };

    public static string VisibleCueCardPrefix(string firstChunk)
    {
        var trimmed = firstChunk.Trim();
        return trimmed.HasPrefix("-") ? "" : CueCardPrefill;
    }
}
