using System.Globalization;
using System.Text.RegularExpressions;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

/// Local question detection: decides from the words alone whether an utterance is a question,
/// which topic it names, and whether it can be answered before the model classifies it.
public partial class VoiceInterviewProcessor
{
    private const RegexOptions Invariant = RegexOptions.CultureInvariant;
    private const RegexOptions IgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly char[] NewlineCharacters = { '\n', '\r', '\u000B', '\u000C', '\u0085', (char)0x2028, (char)0x2029 };

    private static bool Has(string text, string value) => text.Contains(value, StringComparison.Ordinal);

    private static bool HasAny(string text, IEnumerable<string> values) => values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static string F0(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    // Deduplication Helpers

    /// Jaccard similarity between two strings (word-level)
    internal static double StringSimilarity(string a, string b)
    {
        var wordsA = a.Lowercased().SplitOmittingEmpty(' ').ToHashSet(StringComparer.Ordinal);
        var wordsB = b.Lowercased().SplitOmittingEmpty(' ').ToHashSet(StringComparer.Ordinal);
        if (wordsA.Count == 0 && wordsB.Count == 0) return 0;
        var intersection = wordsA.Count(wordsB.Contains);
        var union = wordsA.Count + wordsB.Count - intersection;
        return union > 0 ? (double)intersection / union : 0;
    }

    /// Check if transcription is duplicate (similar text within time window)
    private bool IsDuplicateTranscription(string text, CaptureDevice source, QuestionSignal signal)
    {
        lock (processingStateLock)
        {
            var now = DateTime.UtcNow;
            // Clean old entries
            recentTranscriptions.RemoveAll(entry => (now - entry.Timestamp).TotalSeconds > DedupeWindow);
            var currentTechnicalTokens = TechnicalQuestionTokens(NormalizedQuestionText(text));

            // Check similarity with recent transcriptions
            foreach (var recent in recentTranscriptions)
            {
                var similarity = StringSimilarity(text, recent.Text);
                if (similarity <= SimilarityThreshold) continue;

                var recentTechnicalTokens = TechnicalQuestionTokens(NormalizedQuestionText(recent.Text));
                if (signal.ProtectsFromSkip
                    && currentTechnicalTokens.Count > 0
                    && recentTechnicalTokens.Count > 0
                    && !currentTechnicalTokens.SetEquals(recentTechnicalTokens))
                {
                    Log.Print($"🔄 DEDUPE: Keeping similar question because topic changed ({string.Join(",", recentTechnicalTokens.Order(StringComparer.Ordinal))} → {string.Join(",", currentTechnicalTokens.Order(StringComparer.Ordinal))})");
                    continue;
                }

                // Don't dedupe if new text is significantly longer (it's more complete, not a duplicate)
                var lengthRatio = (double)text.Length / Math.Max(recent.Text.Length, 1);
                if (lengthRatio > 1.3)
                {
                    Log.Print($"🔄 DEDUPE: Keeping longer transcription ({F0(similarity * 100)}% similar but {F0((lengthRatio - 1) * 100)}% longer)");
                    continue;
                }
                Log.Print($"🔄 DEDUPE: Skipping similar transcription ({F0(similarity * 100)}% match with '{recent.Text.Prefix(30)}')");
                return true;
            }

            recentTranscriptions.Add((text, now, source));
            return false;
        }
    }

    /// Check for question markers in text
    public bool CheckForQuestionMarkers(string text) =>
        QuestionSignalFor(text, Delegate?.ConversationContext.LastTopic).ProtectsFromSkip;

    private static readonly string[] DirectQuestionPatterns =
    {
@@directQuestionPatterns#1@@
    };

    private static readonly string[] RequestPatterns =
    {
@@requestPatterns#1@@
    };

    private static readonly string[] ComparisonPatterns =
    {
@@comparisonPatterns#1@@
    };

    private static readonly string[] FollowUpPatterns =
    {
@@followUpPatterns#1@@
    };

    internal static QuestionSignal QuestionSignalFor(string text, string? lastTopic)
    {
        var normalized = NormalizedQuestionText(text);
        var words = normalized.SplitOmittingEmpty(' ');
        var wordCount = words.Count;
        var score = 0;
        var reasons = new List<string>();

        void Add(int points, string reason)
        {
            score += points;
            reasons.Add(reason);
        }

        var isCandidateStatement =
            normalized.HasPrefix("i ")
            || normalized.HasPrefix("i'm ")
            || normalized.HasPrefix("ive ")
            || normalized.HasPrefix("i've ")
            || normalized.HasPrefix("we ")
            || normalized.HasPrefix("my ")
            || Has(normalized, "i have ")
            || Has(normalized, "i worked ")
            || Has(normalized, "we used ")
            || Has(normalized, "we implemented ")
            || ConversationUnderstanding.LooksLikeCandidateNarrative(normalized);

        var bareIncomplete = IsBareIncompletePrompt(normalized);

        if (Has(normalized, "?")) Add(3, "question_mark");

        // "which"/"who"/"whose" ask a question only near the start ("Which database have you used?").
        // Later in a sentence they are relative pronouns ("...a project, which was about agents").
        var hasLeadingWhWord = ConversationUnderstanding.HasQuestioningWhWord(words);
        if (hasLeadingWhWord || HasAny(normalized, DirectQuestionPatterns)) Add(3, "direct_question");

        if (HasAny(normalized, RequestPatterns)) Add(3, "request");

        // Whole-word matching: as substrings these fired inside plain English ("two ", "answer ",
        // "wanna", "it was"), which made candidate statements look like interviewer questions.
        if (ConversationUnderstanding.HasForeignQuestionMarker(normalized)) Add(3, "question_language_marker");

        if (HasAny(normalized, ComparisonPatterns)) Add(2, "comparison");

        var hasFollowUpPhrase = !isCandidateStatement && HasAny(normalized, FollowUpPatterns);
        var technicalTokens = TechnicalQuestionTokens(normalized);
        var isShortTechnicalPrompt = wordCount <= 7 && technicalTokens.Count > 0 && !isCandidateStatement;
        var isContextualShortFollowUp = lastTopic is not null && wordCount <= 6 && (hasFollowUpPhrase || isShortTechnicalPrompt);
        var isFollowUp = hasFollowUpPhrase || isContextualShortFollowUp;

        if (isFollowUp) Add(lastTopic is null ? 2 : 3, "follow_up");

        if (technicalTokens.Count > 0)
        {
            Add(1, "technical_term");
            if (isShortTechnicalPrompt) Add(2, "short_technical_prompt");
        }

        var topicHint = IsVagueFollowUpPrompt(normalized, technicalTokens)
            ? "followUp"
            : BestTopicHint(normalized, technicalTokens);
        return new QuestionSignal(score, reasons, topicHint, isFollowUp, bareIncomplete);
    }

    internal static string NormalizedQuestionText(string text) =>
        Regex.Replace(text.Lowercased().Replace("’", "'").Replace("-", " ").Replace("_", " "), @"\s+", " ").Trim();

    internal static string RepairNoisyTechnicalTranscript(string text, bool? favorsAIAcronyms = null)
    {
        if (!(favorsAIAcronyms ?? AppSettings.Shared.Role.IsAIOrData())) return text;

        var normalized = NormalizedQuestionText(text);
        bool HasWord(string word) => Regex.IsMatch(normalized, $"(^|[^a-z0-9]){word}($|[^a-z0-9])", Invariant);

        var hasRack = HasWord("rack");
        var hasRag = HasWord("rag");
        var hasCac = HasWord("cac");
        var hasCag = HasWord("cag");
        var rackLooksLikeRag = hasRack && (hasCac || hasCag || Regex.IsMatch(normalized, @"\brack\s+(system|pipeline|architecture)\b", Invariant));
        var cacLooksLikeCag = hasCac && (hasRack || hasRag || Has(normalized, "cache augmented") || Has(normalized, "context augmented"));

        if (!rackLooksLikeRag && !cacLooksLikeCag) return text;

        var repaired = text;
        if (rackLooksLikeRag) repaired = Regex.Replace(repaired, @"\brack\b", "RAG", IgnoreCase);
        if (cacLooksLikeCag) repaired = Regex.Replace(repaired, @"\bcac\b", "CAG", IgnoreCase);
        return repaired;
    }

    private static readonly HashSet<string> IncompleteStems = new(StringComparer.Ordinal)
    {
@@incompleteStems#1@@
    };

    private static readonly string[] IncompleteComparisonSuffixes =
    {
@@incompleteComparisonSuffixes#1@@
    };

    private static readonly char[] PromptPunctuation = { ' ', '.', ',', '!', '?', ':', ';' };

    internal static bool IsBareIncompletePrompt(string normalized)
    {
        var cleaned = normalized.Trim(PromptPunctuation);
        if (IncompleteStems.Contains(cleaned)) return true;
        return IncompleteComparisonSuffixes.Any(cleaned.HasSuffix);
    }

    private static readonly (string Needle, string Token)[] TokenMap =
    {
@@tokenMap#1@@
    };

    // Short needles are matched as whole words: as substrings they
    // fire as substrings of unrelated words (e.g. "har" in "share", "orm" in
    // "performance", "dom" in "random", "aws" in "flaws", "git" in "legitimate").
    private static readonly HashSet<string> WordBoundaryNeedles = new(StringComparer.Ordinal)
    {
@@wordBoundaryNeedles#1@@
    };

    internal static HashSet<string> TechnicalQuestionTokens(string normalized)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (needle, token) in TokenMap)
        {
            var found = WordBoundaryNeedles.Contains(needle)
                ? Regex.IsMatch(normalized, "(^|[^a-z0-9])" + needle + "($|[^a-z0-9])", Invariant)
                : Has(normalized, needle);
            if (found) result.Add(token);
        }
        return result;
    }

    internal static string? BestTopicHint(string normalized, HashSet<string> technicalTokens)
    {
        if (Has(normalized, "хешмап") || Has(normalized, "хеш мап") || Has(normalized, "хеш таблиц")
            || Has(normalized, "hash map") || Has(normalized, "hashmap") || Has(normalized, "hash table"))
        {
            return "hashMap";
        }
        if (Has(normalized, "хеш код") || Has(normalized, "hash code") || Has(normalized, "hashcode"))
        {
            return "hashCode";
        }
        if (Has(normalized, "ооп") || Has(normalized, "обектно") || Has(normalized, "object oriented") || Has(normalized, "object-oriented")
            || Has(normalized, "полиморф") || Has(normalized, "наследяване") || Has(normalized, "капсулация"))
        {
            return "oop";
        }
        if (technicalTokens.Contains("rag") && technicalTokens.Contains("cag"))
        {
            return "ragCag";
        }
        if (Has(normalized, "introduce yourself")
            || Has(normalized, "please introduce")
            || Has(normalized, "your introduction")
            || Has(normalized, "quick intro")
            || Has(normalized, "brief intro")
            || Has(normalized, "about yourself")
            || Has(normalized, "your background")
            || Has(normalized, "your experience")
            || Has(normalized, "your project")
            || Has(normalized, "your projects")
            || Has(normalized, "recent project")
            || Has(normalized, "last project")
            || Has(normalized, "project you worked on")
            || Has(normalized, "current role")
            || Has(normalized, "last role")
            || Has(normalized, "side project")
            || Has(normalized, "personal project")
            || Has(normalized, "outside of work"))
        {
            return "personal";
        }
        // "array list"/"arraylist" matches both the bare "array" token and the more
        // specific arrayList needle; prefer arrayList so it is never shadowed by the
        // alphabetically-earlier "array" in the fallback below.
        if (Has(normalized, "array list") || Has(normalized, "arraylist"))
        {
            return "arrayList";
        }
        return technicalTokens.Count == 0 ? null : technicalTokens.Min(StringComparer.Ordinal);
    }

    private static readonly HashSet<string> ExactFollowUps = new(StringComparer.Ordinal)
    {
@@exactFollowUps#1@@
    };

    internal static bool IsVagueFollowUpPrompt(string normalized, HashSet<string> technicalTokens)
    {
        if (technicalTokens.Count > 0) return false;

        var cleaned = normalized.Trim(PromptPunctuation);
        if (ExactFollowUps.Contains(cleaned)) return true;

        return Has(cleaned, "give me an example") || Has(cleaned, "more about that") || Has(cleaned, "expand on this");
    }

    private static readonly string[] ContentMarkers =
    {
@@contentMarkers#1@@
    };

    private static readonly string[] InterviewRequestPhrases =
    {
@@interviewRequestPhrases#1@@
    };

    private static readonly HashSet<string> ExactSocialPrompts = new(StringComparer.Ordinal)
    {
@@exactSocialPrompts#1@@
    };

    private static readonly string[] SetupCheckPhrases =
    {
@@setupCheckPhrases#1@@
    };

    private static readonly string[] ReadySetupPhrases =
    {
@@readySetupPhrases#1@@
    };

    private static readonly string[] ReciprocalSocialPrompts =
    {
@@reciprocalSocialPrompts#1@@
    };

    private static readonly string[] ReciprocalSocialReplyMarkers =
    {
@@socialReplyMarkers#1@@
    };

    private static readonly string[] ContentfulHowAreYouPhrases =
    {
@@contentfulHowAreYouPhrases#1@@
    };

    private static readonly string[] SocialLeadIns =
    {
@@socialLeadIns#1@@
    };

    private static readonly string[] SocialWellnessPhrases =
    {
@@socialWellnessPhrases#1@@
    };

    private static readonly string[] SocialReplyMarkers =
    {
@@socialReplyMarkers#2@@
    };

    internal static bool ShouldSkipAsSocialPleasantry(string trimmed)
    {
        var normalized = NormalizedQuestionText(trimmed);
        var cleaned = Regex.Replace(Regex.Replace(normalized, @"[^a-z0-9\s']+", " ", Invariant), @"\s+", " ", Invariant).Trim();
        if (cleaned.Length == 0) return false;

        if (TechnicalQuestionTokens(normalized).Count > 0 || HasAny(cleaned, ContentMarkers)) return false;

        if (HasAny(cleaned, InterviewRequestPhrases)) return false;

        if (ExactSocialPrompts.Contains(cleaned)) return true;

        var wordCount = cleaned.SplitOmittingEmpty(' ').Count;
        if (wordCount <= 12 && HasAny(cleaned, SetupCheckPhrases)) return true;

        if (wordCount <= 8 && HasAny(cleaned, ReadySetupPhrases)) return true;

        if (wordCount <= 10 && ReciprocalSocialPrompts.Any(prompt => cleaned == prompt || cleaned.HasSuffix(" " + prompt)))
        {
            return wordCount <= 4 || HasAny(cleaned, ReciprocalSocialReplyMarkers);
        }

        var hasSocialHowQuestion = Has(cleaned, "how are you") || Has(cleaned, "how have you been");
        if (hasSocialHowQuestion)
        {
            if (HasAny(cleaned, ContentfulHowAreYouPhrases)) return false;

            return HasAny(cleaned, SocialReplyMarkers)
                || SocialLeadIns.Any(leadIn => cleaned.HasPrefix(leadIn) || Has(cleaned, " " + leadIn))
                || (wordCount <= 12 && HasAny(cleaned, SocialWellnessPhrases));
        }

        return false;
    }

    private static readonly string[] FirstPersonStarts =
    {
@@firstPersonStarts#1@@
    };

    private static readonly string[] AnswerPhrases =
    {
@@answerPhrases#1@@
    };

    private static readonly string[] InterviewerRequestPhrases =
    {
@@interviewerRequestPhrases#1@@
    };

    private static readonly string[] CandidateAbilityLeadIns =
    {
@@candidateAbilityLeadIns#1@@
    };

    private static readonly string[] CandidateNarrationMarkers =
    {
@@candidateNarrationMarkers#1@@
    };

    private static readonly string[] InterviewerReasonMarkers =
    {
@@interviewerReasonMarkers#1@@
    };

    internal static bool ShouldVetoQuestionAsCandidateStatement(string text, QuestionSignal signal)
    {
        var normalized = NormalizedQuestionText(text);
        var wordCount = normalized.SplitOmittingEmpty(' ').Count;
        if (wordCount < 5) return false;

        var looksLikeCandidateStatement = FirstPersonStarts.Any(normalized.HasPrefix)
            || HasAny(normalized, AnswerPhrases)
            || ConversationUnderstanding.LooksLikeCandidateNarrative(normalized);
        if (!looksLikeCandidateStatement) return false;

        if (HasAny(normalized, InterviewerRequestPhrases)) return false;
        // A question mark next to interviewer wording is a real question, even with an answer after it.
        if (Has(normalized, "?") && ConversationUnderstanding.HasInterviewerFrame(normalized)) return false;

        if (CandidateAbilityLeadIns.FirstOrDefault(normalized.HasPrefix) is { } leadIn)
        {
            if (leadIn.HasPrefix("let me "))
            {
                if (HasAny(normalized, CandidateNarrationMarkers)) return true;
            }
            else
            {
                return true;
            }
        }

        if (signal.ProtectsFromSkip && signal.Reasons.Any(reason => InterviewerReasonMarkers.Contains(reason)))
        {
            return false;
        }

        return true;
    }

    internal static bool ShouldTreatLocalSignalAsClearQuestion(string text, QuestionSignal signal) =>
        signal.ProtectsFromSkip && !ShouldVetoQuestionAsCandidateStatement(text, signal);

    /// Topics that must always defer to the authoritative model path because the
    /// status is ambiguous or a good answer needs multi-turn conversation context.
    private static readonly HashSet<string> ProvisionalDeferredTopics = new(StringComparer.Ordinal)
    {
@@provisionalDeferredTopics#1@@
    };

    /// Concrete-enough topics that still need the candidate's background to answer
    /// well (personal / experience / project openers). These are eligible for the
    /// fast path only when a background is available; otherwise they defer so the
    /// model path runs and we never invent experience the candidate does not have.
    private static readonly HashSet<string> BackgroundDependentTopics = new(StringComparer.Ordinal)
    {
@@backgroundDependentTopics#1@@
    };

    /// When the local signal alone is an unambiguous, complete, concrete-topic
    /// interviewer question, return the topic to answer immediately with the fast
    /// direct answer stream — putting useful text on the card without waiting for the
    /// slower authoritative classification. Returns null to defer to the model path
    /// (ambiguous status, incomplete utterance, candidate statement, a topic that
    /// needs conversation context, or a personal question with no background).
    public string? ProvisionalAnswerTopic(string text, string? lastTopic, bool hasBackground = false) =>
        ProvisionalAnswerPlanFor(text, lastTopic, hasBackground)?.Topic;

    /// A fast-path answer topic plus where it came from. An inherited topic is only a hint about
    /// the previous turn; the answer prompt must not present it as the subject of this question.
    public sealed record ProvisionalAnswerPlan(string Topic, bool InheritedFromPriorTurn);

    /// Topic used when a clear question names a subject local detection cannot map. It is
    /// answered from its own words instead of being forced onto the previous topic.
    public const string OwnWordsTopic = "unknown";

    public ProvisionalAnswerPlan? ProvisionalAnswerPlanFor(string text, string? lastTopic, bool hasBackground = false) =>
        ProvisionalPlan(text, lastTopic, hasBackground);

    internal static ProvisionalAnswerPlan? ProvisionalPlan(string text, string? lastTopic, bool hasBackground = false)
    {
        var signal = QuestionSignalFor(text, lastTopic);
        if (!ShouldTreatLocalSignalAsClearQuestion(text, signal)) return null;
        if (signal.IsBareIncomplete || IsLocallyIncomplete(text)) return null;

        var topic = CorrectedTopic(text, signal.TopicHint);
        if (topic.Lowercased() == "unknown" && signal.TopicHint is { } hint)
        {
            topic = hint;
        }
        var topicLower = topic.Lowercased();

        // Topics with no concrete subject of their own — vague follow-ups ("tell me more",
        // "can you elaborate", "go deeper") AND clear questions that local detection cannot
        // map to a named topic — carry no new topic. Two different things hide in that group:
        //  - prompts that lean on the previous turn ("where do we start with?", "have you used
        //    any of these?"): with a concrete prior topic they are answered immediately on it,
        //    which saves the visible classify-then-answer round-trip (~580ms+);
        //  - concrete new questions whose subject local detection simply does not know ("how do
        //    you evaluate a classifier?"): forcing the prior topic on those produced answers
        //    about the previous subject (a RAG definition for a classifier question), so they
        //    are answered from their own words.
        // The fast model always receives the verbatim question. Orphans (no usable prior topic)
        // still defer to the model path so we never answer a fresh topicless question from stale
        // context.
        if (ProvisionalDeferredTopics.Contains(topicLower))
        {
            if (lastTopic is not { } priorTopic
                || ProvisionalDeferredTopics.Contains(priorTopic.Lowercased())
                || BackgroundDependentTopics.Contains(priorTopic.Lowercased()))
            {
                return null;
            }
            if (ConversationUnderstanding.RefersBackToConversation(text))
            {
                return new ProvisionalAnswerPlan(priorTopic, true);
            }
            return new ProvisionalAnswerPlan(OwnWordsTopic, false);
        }

        if (BackgroundDependentTopics.Contains(topicLower))
        {
            return hasBackground ? new ProvisionalAnswerPlan(topic, false) : null;
        }
        return new ProvisionalAnswerPlan(topic, false);
    }

    internal static string CorrectedTopic(string text, string? classifiedTopic)
    {
        var normalized = text.Lowercased();
        var topic = string.IsNullOrEmpty(classifiedTopic) ? "unknown" : classifiedTopic;
        var topicLower = topic.Lowercased();

        if (Has(normalized, "хешмап") || Has(normalized, "хеш мап") || Has(normalized, "хеш таблиц")
            || Has(normalized, "hash map") || Has(normalized, "hashmap") || Has(normalized, "hash table"))
        {
            return "hashMap";
        }

        if (Has(normalized, "хеш код") || Has(normalized, "hash code") || Has(normalized, "hashcode"))
        {
            return "hashCode";
        }

        if (Has(normalized, "ооп") || Has(normalized, "обектно") || Has(normalized, "object oriented") || Has(normalized, "object-oriented")
            || Has(normalized, " полиморф") || Has(normalized, " наследяване") || Has(normalized, " капсулация")
            || normalized.HasPrefix("оп,") || normalized.HasPrefix("оп ") || Has(normalized, " оп,") || Has(normalized, " оп "))
        {
            return "oop";
        }

        if (topicLower is "unknown" or "none")
        {
            var normalizedForSignal = NormalizedQuestionText(text);
            var localTokens = TechnicalQuestionTokens(normalizedForSignal);
            if (IsVagueFollowUpPrompt(normalizedForSignal, localTokens)) return "followUp";
            if (BestTopicHint(normalizedForSignal, localTokens) is { } topicHint) return topicHint;
        }

        return topic;
    }

    /// Remove common model section labels so the final answer reads like something the candidate can say.
    internal static string ConversationalDisplayAnswer(string answer)
    {
        var trimmed = answer.Trim();
        if (trimmed.Length == 0) return trimmed;
        if (Has(trimmed, "```")) return trimmed;

        var cueItems = new List<string>();
        foreach (var rawLine in trimmed.Split(NewlineCharacters))
        {
            var cleaned = CleanConversationalLine(rawLine);
            if (cleaned.Length == 0) continue;
            cueItems.AddRange(CueFragments(StripCueMarker(cleaned)));
        }

        var uniqueItems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cueItems)
        {
            var compact = CompactCueLine(item);
            if (compact.Length == 0) continue;
            if (!seen.Add(compact.Lowercased())) continue;
            uniqueItems.Add(compact);
            if (uniqueItems.Count == 5) break;
        }

        if (uniqueItems.Count == 0) return trimmed;
        return string.Join("\n", uniqueItems.Select(item => $"- {item}"));
    }

    private static readonly string[] DiscardableIntroPrefixes =
    {
@@discardableIntroPrefixes#1@@
    };

    private static readonly string[] AnswerLeadInPrefixes =
    {
@@answerLeadInPrefixes#1@@
    };

    private static readonly string[] SoftLeadInPatterns =
    {
@@softLeadInPatterns#1@@
    };

    private static readonly string[] HeadingPrefixes =
    {
@@headingPrefixes#1@@
    };

    private static bool StartsWithNumberedMarker(string text) =>
        text.Length > 3 && char.IsNumber(text[0]) && text.Substring(1).HasPrefix(". ");

    internal static string CleanConversationalLine(string line)
    {
        var cleaned = line.Trim();
        if (cleaned.Length == 0) return "";

        var marker = "";
        if (cleaned.HasPrefix("- ") || cleaned.HasPrefix("* "))
        {
            marker = cleaned.Prefix(2);
            cleaned = cleaned.DropFirst(2).TrimInline();
        }
        else if (StartsWithNumberedMarker(cleaned))
        {
            marker = "- ";
            cleaned = cleaned.DropFirst(3).TrimInline();
        }

        cleaned = StripInlineMarkdownForCueCard(cleaned);
        var withoutBold = cleaned;
        var lowerWithoutBold = withoutBold.Lowercased();
        if (DiscardableIntroPrefixes.Any(lowerWithoutBold.HasPrefix)
            && (Has(lowerWithoutBold, "answer") || Has(lowerWithoutBold, "bullet") || Has(lowerWithoutBold, "cue")))
        {
            return InlineCueTail(withoutBold) ?? "";
        }

        foreach (var prefix in AnswerLeadInPrefixes)
        {
            if (!lowerWithoutBold.HasPrefix(prefix)) continue;
            cleaned = withoutBold.DropFirst(prefix.Length).TrimInline();
            break;
        }

        foreach (var pattern in SoftLeadInPatterns)
        {
            if (!Regex.IsMatch(cleaned, pattern, IgnoreCase)) continue;
            cleaned = Regex.Replace(cleaned, pattern, "", IgnoreCase).TrimInline();
            break;
        }

        foreach (var prefix in HeadingPrefixes)
        {
            if (!withoutBold.Lowercased().HasPrefix(prefix)) continue;
            cleaned = withoutBold.DropFirst(prefix.Length).TrimInline();
            break;
        }

        if (cleaned.Length == 0) return "";
        return marker + cleaned;
    }

    private static readonly string[] MarkerPatterns =
    {
@@markerPatterns#1@@
    };

    internal static string? InlineCueTail(string text)
    {
        foreach (var pattern in MarkerPatterns)
        {
            var match = Regex.Match(text, pattern, Invariant);
            if (!match.Success) continue;
            var start = match.Index;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            var tail = text.Substring(start).Trim();
            return tail.Length == 0 ? null : tail;
        }

        return null;
    }

    internal static string StripInlineMarkdownForCueCard(string text)
    {
        var cleaned = text;
        cleaned = Regex.Replace(cleaned, @"\*\*([A-Za-z])\*\*([A-Za-z]+)", "$1 - $1$2", Invariant);
        cleaned = Regex.Replace(cleaned, @"\b([A-Za-z])\*\*\s*", "$1 - ", Invariant);
        cleaned = Regex.Replace(cleaned, @"\*\*([^*]+)\*\*", "$1", Invariant);
        cleaned = cleaned.Replace("**", "");
        cleaned = cleaned.Replace("__", "");
        cleaned = Regex.Replace(cleaned, @"(?<!\*)\*([^*\n]+)\*(?!\*)", "$1", Invariant);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ", Invariant);
        return cleaned.Trim();
    }

    internal static string StripCueMarker(string line)
    {
        var text = line.Trim();
        if (text.HasPrefix("- ") || text.HasPrefix("* "))
        {
            text = text.DropFirst(2).TrimInline();
        }
        else if (StartsWithNumberedMarker(text))
        {
            text = text.DropFirst(3).TrimInline();
        }
        return text;
    }

    internal static List<string> CueFragments(string text)
    {
        const string marker = "\u001E";
        const string acronymLock = "\u001F";
        var lockedAcronyms = Regex.Replace(text, @"\b([A-Za-z]) - ", "${1}" + acronymLock, Invariant);
        var prepared = Regex.Replace(lockedAcronyms, @"\s+[-*]\s+", marker, Invariant);
        prepared = Regex.Replace(prepared, @"\s+\d+\.\s+", marker, Invariant);
        prepared = Regex.Replace(prepared, @"\s+/\s+", marker, Invariant);
        prepared = prepared.Replace("—", ". ").Replace(" - ", ". ").Replace(acronymLock, " - ");
        prepared = Regex.Replace(prepared, @"\s+", " ", Invariant).Trim();
        if (prepared.Length == 0) return new List<string>();

        var separated = Regex.Replace(prepared, @";\s+", marker, Invariant);
        separated = Regex.Replace(separated, @"(?<=[.!?])\s+", marker, Invariant);
        return separated
            .Split(marker)
            .Select(part => part.Trim())
            .SelectMany(SplitLongCommaCueFragment)
            .Where(part => part.Length > 0)
            .ToList();
    }

    internal static List<string> SplitLongCommaCueFragment(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.Length <= 140) return new List<string> { cleaned };

        const string marker = "\u001E";
        const string splitPattern = @",\s+(?=(?:and\s+)?(?:i|we|then|also|use|set|keep|review|report|focus|make|start|build|write|run|debug|validate|separate)\b)";
        var parts = Regex.Replace(cleaned, splitPattern, marker, IgnoreCase)
            .Split(marker)
            .Select(part =>
            {
                var trimmed = part.Trim();
                return trimmed.Lowercased().HasPrefix("and ") ? trimmed.DropFirst(4).Trim() : trimmed;
            })
            .Where(part => part.Length > 0)
            .ToList();

        return parts.Count >= 2 ? parts : new List<string> { cleaned };
    }

    private static readonly string[] CompactSeparators =
    {
@@separators#1@@
    };

    internal static string CompactCueLine(string text)
    {
        var cleaned = Regex.Replace(text, @"\s+", " ", Invariant).TrimWhere(c => char.IsWhiteSpace(c) || c is '-' or '*');
        if (cleaned.Length == 0) return "";

        const int maxCharacters = 120;
        if (cleaned.Length <= maxCharacters) return cleaned;

        var head = cleaned.Substring(0, maxCharacters);
        foreach (var separator in CompactSeparators)
        {
            var at = head.LastIndexOf(separator, StringComparison.Ordinal);
            if (at < 55) continue;
            var shortenedAtSeparator = head.Substring(0, at).Trim();
            return shortenedAtSeparator.HasSuffix(".") ? shortenedAtSeparator : $"{shortenedAtSeparator}.";
        }

        var shortened = head.Trim();
        return shortened.HasSuffix(".") ? shortened : $"{shortened}...";
    }

    private static readonly string[] WhisperHallucinations =
    {
@@whisperHallucinations#1@@
    };

    /// Check if text is a Whisper hallucination
    internal static bool IsWhisperHallucination(string trimmed)
    {
        var lowerTrimmed = trimmed.Lowercased().Replace("!", "").Replace(".", "").Replace(",", "").TrimInline();

        return trimmed.Length < 30 && WhisperHallucinations.Contains(lowerTrimmed);
    }

    private static readonly string[] GreetingStarts =
    {
@@greetingStarts#1@@
    };

    private static readonly string[] FillerPatterns =
    {
@@fillerPatterns#1@@
    };

    private static readonly string[] QuestionWords =
    {
@@questionWords#1@@
    };

    /// Check if text should be skipped as filler or greeting
    internal static bool ShouldSkipAsFillerOrGreeting(string trimmed)
    {
        var normalizedText = Regex.Replace(trimmed.Lowercased().Trim(), "[.!?,']", "", Invariant);

        var isGreeting = GreetingStarts.Any(normalizedText.HasPrefix);

        // "Okay, so you didn't use forecasting" carries a real check-in after the filler; let the classifier decide.
        var isFiller = FillerPatterns.Any(pattern =>
            normalizedText.HasPrefix(pattern) && normalizedText.DropFirst(pattern.Length).SplitOmittingEmpty(' ').Count < 4);

        var hasQuestionWord = HasAny(normalizedText, QuestionWords);

        if ((isGreeting || isFiller) && normalizedText.Length < 50 && !hasQuestionWord)
        {
            Log.Print($"⚡ PROCESS: LOCAL SKIP - Greeting/filler: '{trimmed}'");
            return true;
        }

        return false;
    }

    private static readonly string[] IncompleteEndings =
    {
@@incompleteEndings#1@@
    };

    /// Check if text is locally incomplete (ends with common incomplete patterns)
    internal static bool IsLocallyIncomplete(string trimmed)
    {
        var textForCheck = trimmed.Lowercased().TrimInline();
        var endsIncomplete = IncompleteEndings.Any(textForCheck.HasSuffix);
        var hasQuestionMark = Has(textForCheck, "?");

        return (endsIncomplete && !hasQuestionMark) || ConversationUnderstanding.TrailsOff(trimmed);
    }
}
