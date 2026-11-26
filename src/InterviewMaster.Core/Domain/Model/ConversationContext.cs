using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Tracks conversation history for follow-up detection and context-aware responses
public sealed class ConversationContext
{
    public sealed record Utterance(string Text, Speaker Speaker, string? Topic, DateTime Timestamp, int? Sequence);

    public enum Speaker
    {
        Interviewer,
        Interviewee,
        Unknown
    }

    /// Message format for multi-turn API calls
    /// <param name="Role">"user" or "assistant"</param>
    public sealed record MultiTurnMessage(string Role, string Content);

    private const int MaxHistory = AppConstants.Thresholds.MaxConversationHistory;
    private const int SlidingWindowSize = AppConstants.Thresholds.SlidingWindowSize;
    private const int SummarizationThreshold = AppConstants.Thresholds.SummarizationThreshold;

    private readonly object historyGate = new();
    private List<Utterance> history = new();
    private string? currentTopic;
    private int? currentTopicSequence;

    // Multi-turn conversation support
    private string? conversationSummary;

    public string? CurrentTopic
    {
        get { lock (historyGate) return currentTopic; }
    }

    public string? ConversationSummary
    {
        get { lock (historyGate) return conversationSummary; }
    }

    public static string RawValue(Speaker speaker) => speaker switch
    {
        Speaker.Interviewer => "interviewer",
        Speaker.Interviewee => "interviewee",
        _ => "unknown"
    };

    /// Classify speaker based on heuristics
    public Speaker ClassifySpeaker(string text, bool isQuestion = false)
    {
        var trimmed = text.Trim();
        var wordCount = trimmed.SplitOmittingEmpty(' ').Count;
        var lowercased = trimmed.Lowercased();

        // If LLM already classified as question, it's the interviewer
        if (isQuestion) return Speaker.Interviewer;

        // Question indicators
        var hasQuestionMark = trimmed.Contains('?');
        var startsWithQuestion = lowercased.HasPrefix("what")
            || lowercased.HasPrefix("how")
            || lowercased.HasPrefix("why")
            || lowercased.HasPrefix("can you")
            || lowercased.HasPrefix("could you")
            || lowercased.HasPrefix("tell me")
            || lowercased.HasPrefix("explain")
            || lowercased.HasPrefix("describe")
            || lowercased.HasPrefix("walk me")
            || lowercased.HasPrefix("so ")
            || lowercased.HasPrefix("let's assume");

        var isFollowUp = lowercased.ContainsAny(new[]
        {
            "tell me more", "dig deeper", "elaborate", "can you expand", "more details", "give me an example"
        });

        // Interview greetings/transitions - likely interviewer
        var isInterviewerPhrase = lowercased.ContainsAny(new[]
        {
            "welcome to", "good evening", "good morning", "shall proceed", "gone through your resume", "let me ask"
        });

        // Short questions = interviewer
        if (wordCount < 25 && (hasQuestionMark || startsWithQuestion || isFollowUp || isInterviewerPhrase))
        {
            return Speaker.Interviewer;
        }

        // Long explanations = interviewee
        if (wordCount > 30) return Speaker.Interviewee;

        // Medium length with technical terms might be interviewee answering
        if (wordCount > 15) return Speaker.Interviewee;

        return Speaker.Unknown;
    }

    /// Check if this is a follow-up request
    public bool IsFollowUp(string text) => text.Lowercased().ContainsAny(new[]
    {
        "tell me more", "dig deeper", "elaborate", "expand on",
        "more details", "give me an example", "can you explain",
        "what else", "go deeper", "more about", "continue"
    });

    /// Add utterance to history
    public void AddUtterance(string text, string? topic, bool isQuestion = false, int? sequence = null)
    {
        var speaker = ClassifySpeaker(text, isQuestion);
        var utterance = new Utterance(text, speaker, topic, DateTime.Now, sequence);

        lock (historyGate)
        {
            var insertionIndex = sequence is { } wanted
                ? history.FindIndex(item => (item.Sequence ?? int.MinValue) > wanted)
                : -1;
            if (insertionIndex >= 0)
            {
                history.Insert(insertionIndex, utterance);
            }
            else
            {
                history.Add(utterance);
            }

            var topicLower = topic?.Lowercased();
            if (topic is not null && topicLower != "unknown" && topicLower != "followup" && topicLower != "answer")
            {
                if (sequence is { } current)
                {
                    if (currentTopicSequence is null || current >= currentTopicSequence.Value)
                    {
                        currentTopic = topic;
                        currentTopicSequence = current;
                    }
                }
                else
                {
                    currentTopic = topic;
                }
            }

            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }
        }

        Log.Print($"📝 [{RawValue(speaker)}] {text.Prefix(50)}... | Topic: {topic ?? "none"}");
    }

    /// Get recent context for LLM (last 5 utterances). Topic labels are left out on purpose: they are
    /// guesses, and a wrong one ("ragcag" beside a classifier question) pulled later answers off course.
    public string GetContextForLLM()
    {
        lock (historyGate)
        {
            if (history.Count == 0) return "No previous conversation.";

            return string.Join("\n", history.Skip(Math.Max(0, history.Count - 5)).Select(utterance =>
            {
                var speaker = utterance.Speaker switch
                {
                    Speaker.Interviewer => "Interviewer",
                    Speaker.Interviewee => "Candidate",
                    _ => "Other"
                };
                var text = utterance.Text.Length > 260 ? utterance.Text.Prefix(260) + "…" : utterance.Text;
                return $"{speaker}: {text}";
            }));
        }
    }

    /// Get full conversation history for comprehensive context
    public string GetFullConversation()
    {
        lock (historyGate)
        {
            if (history.Count == 0) return "";

            return string.Join("\n", history.Select(utterance =>
            {
                var role = utterance.Speaker == Speaker.Interviewer ? "Q" : "A";
                return $"{role}: {utterance.Text}";
            }));
        }
    }

    /// Get conversation summary (topics discussed), in the order they first came up.
    public string GetTopicsSummary()
    {
        lock (historyGate)
        {
            var unique = history
                .Select(utterance => utterance.Topic)
                .OfType<string>()
                .Where(topic => topic.Lowercased() is not ("unknown" or "followup" or "answer"))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return unique.Count == 0 ? "" : $"Topics discussed: {string.Join(", ", unique)}";
        }
    }

    /// The interviewer's most recent question, for a request that points back at it ("show me that in code").
    public string? LastInterviewerQuestion
    {
        get { lock (historyGate) return history.LastOrDefault(item => item.Speaker == Speaker.Interviewer)?.Text; }
    }

    /// Get the last topic discussed (for follow-ups)
    public string? LastTopic
    {
        get { lock (historyGate) return currentTopic; }
    }

    public void Clear()
    {
        lock (historyGate)
        {
            history.Clear();
            currentTopic = null;
            currentTopicSequence = null;
            conversationSummary = null;
        }
    }

    // MARK: - Multi-Turn Conversation Support

    /// Check if summarization is needed (history exceeds threshold)
    public bool NeedsSummarization
    {
        get { lock (historyGate) return conversationSummary is null && history.Count > SummarizationThreshold; }
    }

    /// Get messages older than sliding window (for summarization)
    public List<Utterance> GetMessagesForSummarization()
    {
        lock (historyGate)
        {
            if (history.Count <= SlidingWindowSize) return new List<Utterance>();
            return history.Take(history.Count - SlidingWindowSize).ToList();
        }
    }

    /// Get formatted text of old messages for summarization API call
    public string GetTextForSummarization() =>
        string.Join("\n", GetMessagesForSummarization().Select(utterance =>
        {
            var role = utterance.Speaker == Speaker.Interviewer ? "Interviewer" : "Candidate";
            return $"{role}: {utterance.Text}";
        }));

    /// Set the conversation summary (called after LLM summarizes)
    public void SetSummary(string summary)
    {
        int count;
        lock (historyGate)
        {
            conversationSummary = summary;
            if (history.Count > SlidingWindowSize)
            {
                history = history.Skip(history.Count - SlidingWindowSize).ToList();
            }
            count = history.Count;
        }
        Log.Print($"📋 Conversation summarized. Keeping last {count} messages.");
    }

    /// Build multi-turn messages array for API call
    /// Structure: [Summary context] + [Recent sliding window] + [Current utterance]
    public List<MultiTurnMessage> BuildMultiTurnMessages(string currentUtterance, string? pinnedSolution = null)
    {
        lock (historyGate)
        {
            var messages = new List<MultiTurnMessage>();

            if (conversationSummary is { } summary)
            {
                var contextContent = $"Previous interview context: {summary}";
                if (pinnedSolution is not null)
                {
                    contextContent += $"\n\nCurrent code solution being discussed:\n{pinnedSolution}";
                }
                messages.Add(new MultiTurnMessage("user", contextContent));
                messages.Add(new MultiTurnMessage("assistant", "I understand the context. I'll help with the interview questions."));
            }
            else if (pinnedSolution is not null)
            {
                messages.Add(new MultiTurnMessage("user", $"Current code solution being discussed:\n{pinnedSolution}"));
                messages.Add(new MultiTurnMessage("assistant", "I see the code. I'll help with questions about it."));
            }

            var recentHistory = conversationSummary is not null
                ? history
                : history.Skip(Math.Max(0, history.Count - SlidingWindowSize)).ToList();

            foreach (var utterance in recentHistory)
            {
                var role = utterance.Speaker == Speaker.Interviewer ? "user" : "assistant";
                messages.Add(new MultiTurnMessage(role, utterance.Text));
            }

            messages.Add(new MultiTurnMessage("user", currentUtterance));

            return messages;
        }
    }

    /// Convert multi-turn messages to API format
    public List<Dictionary<string, string>> MessagesToAPIFormat(IEnumerable<MultiTurnMessage> messages)
    {
        // Ensure alternating roles (API requirement)
        var result = new List<Dictionary<string, string>>();
        string? lastRole = null;

        foreach (var message in messages)
        {
            // If same role as last, merge content
            if (message.Role == lastRole && result.Count > 0)
            {
                var last = result[^1];
                last["content"] = (last.TryGetValue("content", out var existing) ? existing : "") + "\n\n" + message.Content;
            }
            else
            {
                result.Add(new Dictionary<string, string> { ["role"] = message.Role, ["content"] = message.Content });
                lastRole = message.Role;
            }
        }

        // Ensure starts with "user" role
        if (result.Count == 0 || result[0]["role"] != "user")
        {
            result.Insert(0, new Dictionary<string, string> { ["role"] = "user", ["content"] = "(Interview in progress)" });
        }

        return result;
    }
}
