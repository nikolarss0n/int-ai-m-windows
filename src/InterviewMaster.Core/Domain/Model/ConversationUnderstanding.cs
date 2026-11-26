using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Pure helpers for reading a live interview transcript: who is probably speaking, and whether an
/// utterance stands on its own or leans on the previous turn. No UI or network.
public static class ConversationUnderstanding
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex WhitespaceRun = new(@"\s+", Options);

    public static string Normalized(string text) =>
        WhitespaceRun.Replace(
            text.Lowercased().Replace("’", "'").Replace("-", " ").Replace("_", " "),
            " ").Trim();

    // MARK: - Foreign-language question markers

    /// Cyrillic markers cannot collide with English, so plain substring matching is safe.
    private static readonly string[] CyrillicQuestionMarkers =
    {
        "какво", "как ", "защо", "кога", "къде", "кой", "коя", "кое", "кои",
        "разкажи", "раскажи", "обясни", "опиши"
    };

    /// Latin-script question words must match whole words. As substrings they fire inside English:
    /// "wo " in "two ", "wer " in "answer ", "wann" in "wanna".
    private static readonly Regex LatinQuestionWords = new(
        @"(^|[^\p{L}])(wie|warum|wann|wo|wer|welche|qu'est|pourquoi|quand|où|qui|qué|cómo|por qué|cuándo|dónde|quién)($|[^\p{L}])",
        Options);

    /// Foreign question words that are also ordinary English words ("it was", "add a comment").
    /// They only count as the first word of the utterance.
    private static readonly HashSet<string> LeadingOnlyQuestionWords = new() { "was", "comment" };

    public static bool HasForeignQuestionMarker(string normalized)
    {
        if (normalized.ContainsAny(CyrillicQuestionMarkers)) return true;
        if (LatinQuestionWords.IsMatch(normalized)) return true;
        var words = normalized.SplitOmittingEmpty(' ');
        return words.Count > 0 && LeadingOnlyQuestionWords.Contains(words[0]);
    }

    /// "Which database have you used?" asks a question; "...a project, which was about agents" does not.
    /// Only a wh-word among the first few words counts, and not when a pronoun subject follows it
    /// ("which I like", "who we hired"), which marks a relative clause.
    public static bool HasQuestioningWhWord(IReadOnlyList<string> words)
    {
        var cleaned = words.Take(5).Select(word => word.TrimPunctuation().Lowercased()).ToList();
        for (var index = 0; index < cleaned.Count; index++)
        {
            var word = cleaned[index];
            if (index >= 4 || (word != "which" && word != "who" && word != "whose")) continue;
            var next = index + 1 < cleaned.Count ? cleaned[index + 1] : "";
            var relativeStarts = new HashSet<string> { "i", "we", "he", "she", "they", "it", "you" };
            if (index >= 2)
            {
                relativeStarts.Add("was");
                relativeStarts.Add("were");
            }
            if (!relativeStarts.Contains(next)) return true;
        }
        return false;
    }

    // MARK: - Candidate speech

    private static readonly string[] CandidateStallPhrases =
    {
        "give me a second", "give me a moment", "give me a minute", "give me one second",
        "just a second", "just a moment", "one second", "one moment", "one sec",
        "hold on", "hang on", "bear with me", "wait a second", "wait a moment",
        "let me think", "let me share", "let me open", "let me create", "let me check",
        "let me see", "let me try", "let me look", "let me find", "let me just",
        "just let me", "sorry about that", "my bad"
    };

    private static readonly string[] CandidateCheckPhrases =
    {
        "does that make sense", "is that what you're looking for", "is that what you are looking for",
        "is that okay", "is this okay", "is that clear", "i hope that helps", "i hope that answers",
        "does that answer your question", "that's all i have", "that is all i have"
    };

    /// "Sorry, can you repeat?" is the candidate asking the interviewer, not a question to answer.
    private static readonly string[] RepeatRequestPhrases =
    {
        "can you repeat", "could you repeat", "can i repeat", "can you say that again",
        "could you say that again", "say that again", "i didn't catch that", "i didn't hear you", "come again"
    };

    private static readonly HashSet<string> RepeatFillerWords = new()
    {
        "sorry", "please", "okay", "ok", "uh", "um", "the", "that", "again", "question", "it", "what", "so", "oh"
    };

    /// Stalling and check-in phrases the candidate says while thinking or typing. They are never
    /// questions, so they must not open an answer card. A real question in the same breath wins.
    public static bool IsCandidateMetaUtterance(string text)
    {
        var normalized = Normalized(text);
        var cleaned = normalized.TrimChars(" .,!?:;");
        if (cleaned.Length == 0 || cleaned.SplitOmittingEmpty(' ').Count > 16) return false;
        if (cleaned == "sorry") return true;
        var repeatPhrase = RepeatRequestPhrases.FirstOrDefault(phrase => cleaned.Contains(phrase, StringComparison.Ordinal));
        if (repeatPhrase is not null)
        {
            var remainder = cleaned.Replace(repeatPhrase, " ")
                .LetterNumberWords()
                .Where(word => !RepeatFillerWords.Contains(word))
                .ToList();
            return remainder.Count <= 2;
        }
        var stallPhrase = CandidateStallPhrases.Concat(CandidateCheckPhrases)
            .FirstOrDefault(phrase => cleaned.Contains(phrase, StringComparison.Ordinal));
        if (stallPhrase is null) return false;
        // "give me a second" would otherwise trip the "give me ..." interviewer wording.
        return !HasInterviewerFrame(cleaned.Replace(stallPhrase, " "));
    }

    private static readonly Regex InterviewerFrame = new(
        @"\b(what|how|why|when|where|which|who)\s+(is|are|was|were|do|does|did|would|will|can|could|should|has|have|about|kind|type|else|exactly|if)\b|\b(why|how)\s+(this|that|your)\b|\b(can|could|would|will|do|did|have|are|is)\s+(you|your)\b|\b(would|could|can|will|should|is|are|was|were)\s+(it|this|that|there|we|i|they)\b|\b(tell|show|give|walk|talk)\s+(me|us)\b|\b(explain|describe|compare|elaborate)\b|\b(let's|lets|let us)\s+(start|begin|talk|discuss|go|move|proceed|jump)\b",
        Options);

    /// Wording that only an interviewer aims at the candidate ("how do you", "can you", "tell me").
    /// Deliberately generous: a miss here only means a candidate remark still gets an answer card,
    /// while a false hit on a real question would drop it.
    public static bool HasInterviewerFrame(string normalized) => InterviewerFrame.IsMatch(normalized);

    private static readonly Regex FirstPersonWord = new(
        @"\b(i|i'm|im|i've|ive|i'd|i'll|we|we're|we've|we'd|my|our|me|myself)\b",
        Options);

    private static readonly string[] AnswerOpeners =
    {
        "yeah", "yes", "yep", "well", "nope", "mostly", "basically", "actually", "honestly",
        "to be honest", "for me", "in my", "for example", "like for example", "something like",
        "so basically", "i think", "i guess", "i believe", "probably", "it depends", "depending on"
    };

    /// Transcription often puts a question mark after these ("...and what?"), so it does not finish them.
    private static readonly string[] TrailingQuestionWords =
    {
        "and what", "and how", "and why", "and which", "and where", "and when"
    };

    private static readonly string[] TrailingConnectives =
        TrailingQuestionWords.Concat(new[] { "let's say", "kind of", "sort of" }).ToArray();

    private const string TrailingPunctuation = ".,…? ";

    private static readonly HashSet<string> QuestionOpeners = new()
    {
        "what", "how", "why", "when", "where", "which", "who", "whose",
        "can", "could", "do", "does", "did", "is", "are", "should", "would"
    };

    /// Short names a real spoken list is made of, which the technical token table does not cover.
    private static readonly HashSet<string> FamiliarListWords = new()
    {
        "python", "java", "go", "rust", "swift", "kotlin", "ruby", "scala", "php", "perl", "c", "sql",
        "react", "vue", "node", "docker", "redis", "kafka", "aws", "azure", "gcp", "linux", "git"
    };

    private static readonly string[] FollowUpPhrases =
    {
        "tell me more", "dig deeper", "go deeper", "elaborate", "expand on", "more details", "more about",
        "what about", "how about", "what else", "anything else", "give me an example"
    };

    private static readonly string[] CodeReferencePhrases =
    {
        "did you", "you used", "you chose", "you wrote", "your solution", "your code", "this code", "that code",
        "the code", "this function", "the function", "version", "complexity"
    };

    /// Whether a question carries on from the one before it: the classifier called it a follow-up, it
    /// stays on the same topic, or it only makes sense after something ("what about…", "write it in…").
    public static bool FollowsPrevious(string question, string topic, string? previousTopic)
    {
        if (previousTopic is null) return false;
        var loweredTopic = topic.Lowercased();
        if (loweredTopic == "followup") return true;
        if (loweredTopic != "unknown" && loweredTopic != "answer" && loweredTopic == previousTopic.Lowercased()) return true;
        var lowered = question.Lowercased();
        // After code, a question about what was written ("why did you use slicing") is about that code.
        if (previousTopic.Lowercased() == "coding" && lowered.ContainsAny(CodeReferencePhrases)) return true;
        return lowered.ContainsAny(FollowUpPhrases) || RefersBackToConversation(question);
    }

    /// Speech that stops mid-thought: an ellipsis, or a connective with nothing after it.
    public static bool TrailsOff(string text)
    {
        var lowered = text.Lowercased().Trim();
        if (lowered.Length == 0) return false;
        var bare = lowered.TrimChars(TrailingPunctuation);
        if (lowered.HasSuffix("?")) return TrailingQuestionWords.Any(word => bare.HasSuffix(" " + word));
        if (lowered.HasSuffix("...") || lowered.HasSuffix("…")) return true;
        return TrailingConnectives.Any(word => bare.HasSuffix(" " + word));
    }

    /// A question that stops on a comma or trails off may go on, or may be all the interviewer says. It
    /// waits for more speech, and is answered without the unfinished tail if silence follows.
    public static string? HeldQuestion(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.HasSuffix(",") && !TrailsOff(trimmed)) return null;
        var question = trimmed.TrimChars(TrailingPunctuation);
        while (true)
        {
            var lowered = question.Lowercased();
            var tail = TrailingConnectives.FirstOrDefault(word => lowered.HasSuffix(" " + word));
            if (tail is null) break;
            question = question.DropLast(tail.Length + 1).TrimChars(TrailingPunctuation);
        }
        return question.Length == 0 ? null : question;
    }

    /// A short fragment right after a question carries that question on ("kind of, let's say,"), so it
    /// is heard as part of it. Anything that opens a question of its own stands alone.
    public static string? ContinuedQuestion(string? previous, string fragment, double? secondsSincePrevious, double window)
    {
        if (string.IsNullOrEmpty(previous) || secondsSincePrevious is null || secondsSincePrevious > window
            || fragment.Contains('?'))
        {
            return null;
        }
        var words = Normalized(fragment).SplitOmittingEmpty(' ');
        if (words.Count == 0 || words.Count > 6 || QuestionOpeners.Contains(words[0])) return null;
        return $"{previous} {fragment}";
    }

    /// Transcription that broke mumbled speech into a run of scraps: "Aki, di, sari, u, gontu, yus?" or
    /// "Aki, D, sorry, U, gone to, yas?". The caller rules out real lists by their topic.
    public static bool LooksGarbled(string text)
    {
        var cleaned = text.TrimWhere(c => char.IsWhiteSpace(c) || char.IsPunctuation(c));
        var pieces = cleaned.SplitOmittingEmpty(',').Select(piece => piece.TrimInline().Lowercased()).ToList();
        if (pieces.Count < 4) return false;
        var allScraps = pieces.All(piece =>
        {
            var words = piece.SplitOmittingEmpty(' ');
            return words.Count >= 1 && words.Count <= 2
                && words.All(word => word.Length <= 6 && word.All(c => char.IsAscii(c) && char.IsLetter(c)));
        });
        var allWords = pieces.SelectMany(piece => piece.SplitOmittingEmpty(' ')).ToList();
        return allScraps && QuestionOpeners.IsDisjoint(allWords) && FamiliarListWords.IsDisjoint(allWords);
    }

    /// A long, first-person, question-free run of speech is the candidate answering (or the
    /// interviewer setting a scenario), not something a cue card should answer.
    public static bool LooksLikeCandidateNarrative(string normalized)
    {
        var wordCount = normalized.SplitOmittingEmpty(' ').Count;
        if (wordCount < 12 || normalized.Contains('?') || HasInterviewerFrame(normalized)) return false;
        var firstPerson = FirstPersonWord.Matches(normalized).Count;
        var hasOpener = AnswerOpeners.Any(normalized.HasPrefix);
        return firstPerson >= 2 || (hasOpener && (firstPerson >= 1 || wordCount >= 16));
    }

    // MARK: - Back-references

    private static readonly string[] BackReferencePhrases =
    {
        "you mean", "you meant", "you said", "you mentioned", "we talked", "we discussed",
        "as we said", "as we discussed", "go on", "and then", "what next", "next step",
        "what do you mean", "which one", "which ones", "what else", "anything else",
        "what about", "how about"
    };

    private static readonly HashSet<string> AnaphoraWords = new()
    {
        "it", "this", "these", "those", "them", "they", "same", "above", "earlier",
        "mentioned", "again"
    };

    private static readonly Regex AnaphoricThat = new(
        @"\b(by|about|of|on|with|for|is|does|do|did|explain|mean|use|used)\s+that\b|\bthat$",
        Options);

    /// Words that carry no subject of their own: pronouns, auxiliaries, question words, request
    /// verbs, and discourse filler. A question made only of these depends on the previous turn.
    private static readonly HashSet<string> SubjectlessWords = new()
    {
        "i", "you", "we", "he", "she", "they", "it", "me", "us", "my", "your", "our", "their", "its",
        "is", "are", "was", "were", "be", "been", "being", "am", "do", "does", "did", "done",
        "have", "has", "had", "can", "could", "would", "will", "shall", "should", "may", "might", "must",
        "not", "no", "yes", "yeah", "yep", "ok", "okay", "so", "well", "anyway", "actually", "basically",
        "just", "really", "then", "now", "also", "still", "maybe", "please", "sorry", "thanks", "thank",
        "hi", "hello", "what", "how", "why", "when", "where", "which", "who", "whom", "whose", "whether",
        "if", "the", "and", "for", "with", "from", "about", "into", "over", "than", "but", "like", "that",
        "this", "these", "those", "there", "here", "some", "any", "all", "more", "other", "another",
        "get", "got", "go", "going", "gone", "come", "start", "starting", "begin", "next", "expect",
        "expecting", "approach", "handle", "deal", "think", "thinking", "see", "look", "say", "tell",
        "ask", "know", "want", "need", "make", "take", "put", "give", "work", "works", "working",
        "thing", "things", "stuff", "something", "anything", "everything", "way", "ways", "time",
        "explain", "describe", "elaborate", "expand", "details", "deeper", "detail", "example",
        "else", "same", "again", "mean", "meant", "kind", "sort", "let", "lets", "whats", "thats",
        "hows", "dont", "doesnt", "didnt", "cant", "wont", "isnt", "arent", "youre", "weve", "theres",
        "one", "ones", "much", "many", "very", "too", "out", "off", "under", "own"
    };

    /// True when the utterance has no subject of its own or points back at the last exchange
    /// ("Have you used any of these?", "Where do we start?", "Tell me more"). Such a prompt may
    /// inherit the previous topic; a concrete new question must be answered from its own words.
    public static bool RefersBackToConversation(string text)
    {
        var normalized = Normalized(text);
        if (normalized.Length == 0) return false;

        // Non-Latin scripts keep the earlier behaviour: the English word lists below cannot judge them.
        var letters = 0;
        var nonLatin = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (!IsLetterOrMark(rune)) continue;
            letters++;
            if (rune.Value > 0x024F) nonLatin++;
        }
        if (letters > 0 && (double)nonLatin / letters > 0.3) return true;

        if (normalized.ContainsAny(BackReferencePhrases)) return true;

        var words = normalized.Replace("'", "").LetterNumberWords();
        var contentWords = words.Where(word => word.Length >= 3 && !SubjectlessWords.Contains(word)).ToList();
        if (contentWords.Count == 0) return true;

        if (words.Count <= 8 && words.Any(AnaphoraWords.Contains)) return true;
        // A short fragment with no question wording ("Frameboards? Script, yeah. Well, used Spring")
        // is a clarification of what was just asked, not a new question.
        if (words.Count <= 10 && !HasInterviewerFrame(normalized)) return true;
        if (words.Count > 10) return false;
        var trimmed = normalized.TrimChars(" .,!?:;\"'");
        return AnaphoricThat.IsMatch(trimmed);
    }

    /// Swift `CharacterSet.letters`: general categories L* and M*.
    private static bool IsLetterOrMark(Rune rune)
    {
        if (Rune.IsLetter(rune)) return true;
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }
}

/// Prompt fragments shared by the fast answer paths, so a stale topic can never steer a new question.
public static class AnswerPromptBlocks
{
    /// Shared voice for interview prose; each caller keeps its own answer or coding format.
    public const string CandidateVoice = """
        CANDIDATE VOICE:
        - Point first. Short, direct sentences, everyday words, natural contractions.
        - One point at a time. Give the action, then the reason or one concrete example.
        - Sound like a colleague explaining their approach. No sales language or textbook padding.
        - Say "use", "check", "fix"; avoid "leverage", "facilitate", "robust solution".
        - For an approach, say "I'd check..." or "I'd use...". State definitions directly.
        - Say "I use" or "I did" only when the CV in this prompt supports it. Never invent experience.
        - Keep technical terms exact and limitations that change the answer; skip generic hedges.
        - No "I'd say", "I would answer it as", intros or closing summaries.
        - Example: "Keep the result in a cache so you don't fetch it again."
        """;

    /// <param name="Line">The "TOPIC: x" line, or nothing when there is no reliable topic.</param>
    /// <param name="GarbleNote">What the model should do when the transcript looks garbled.</param>
    /// <param name="AllowsTopicSpecificShape">Topic-specific answer shapes (SOLID letters, RAG flow) only fit a topic named in this question.</param>
    public readonly record struct Topic(string Line, string GarbleNote, bool AllowsTopicSpecificShape);

    private const string AnswerAsAsked = "The transcript may contain speech-to-text errors. Answer the question as asked.";

    public static Topic TopicBlock(string topic, bool inherited)
    {
        var lower = topic.Lowercased();
        if (lower.Length == 0 || lower == "unknown")
        {
            return new Topic("", AnswerAsAsked, false);
        }
        if (inherited)
        {
            return new Topic(
                $"PREVIOUS TOPIC (only if the question refers back to it): {topic}",
                AnswerAsAsked,
                false);
        }
        return new Topic(
            $"TOPIC: {topic}",
            "The transcript may contain speech-to-text errors. Trust TOPIC when words are garbled.",
            true);
    }

    /// `extendsPreviousAnswer` is for "tell me more"; otherwise the conversation only resolves references.
    public static string RecentConversation(string? context, bool extendsPreviousAnswer)
    {
        var trimmed = context?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "No previous conversation.") return "";
        var instruction = extendsPreviousAnswer
            ? "This is a follow-up on the same topic. Add new, deeper points or a concrete example; do NOT repeat what was already covered."
            : "Use the conversation only to work out what words like \"it\", \"that\" or \"these\" refer to. Answer the CURRENT question, not an earlier one.";
        return $"\n=== CONVERSATION SO FAR ===\n{trimmed}\n=== END CONVERSATION ===\n\n{instruction}\n";
    }

    /// The code the candidate is typing, so "why did you use a dict?" is answered about that code.
    public static string PinnedCode(string? solution, int limit = 1800)
    {
        var trimmed = solution?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";
        var body = trimmed.Length > limit ? trimmed.Prefix(limit) + "\n…" : trimmed;
        return $"\nCODE THE CANDIDATE IS WRITING (answer in this context when the question is about it):\n{body}\n";
    }
}
