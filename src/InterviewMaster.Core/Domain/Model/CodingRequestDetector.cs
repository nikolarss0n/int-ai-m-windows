using System.Text.RegularExpressions;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// What an interviewer asks for once code is already on the panel.
public enum CodingFollowUp
{
    /// Change the code that is there: another language, an improvement, another way, a constraint.
    Change,

    /// A fresh example of a technique ("show me sliding window"). It replaces the panel.
    Example
}

/// Detects "write this code" in an interviewer's words. Ported from `CodingRequestDetector` in
/// `Domain/Model/CodingInterviewSteps.swift`.
public static class CodingRequestDetector
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex WhitespaceRun = new(@"\s+", Options);

    public static string Normalized(string text) =>
        WhitespaceRun.Replace(text.Lowercased().Replace("’", "'").Replace("-", " "), " ").Trim();

    private const string Verbs = "write|implement|code|create|build|develop|scaffold|solve|complete";

    private const string Objects = "function|method|class|program|script|algorithm|endpoint|api|service|route|handler|query|regex|regular expression|loop|snippet|solution|code|unit tests?|test cases?|tests? for|module|component|hook|decorator|generator|parser|calculator|validator|cache|stack|queue|linked list|tree|trie|graph|heap|matrix|iterator|crud|fastapi|rest api|problem|exercise|challenge|task|binary search|search|sort|merge sort|quick sort|fibonacci|factorial|fizz ?buzz|palindrome|anagram|two sum|hash ?map|hash table|counter|scraper|crawler|app|application|pipeline|classifier|chatbot|agent|retriever|rate limiter|limiter|logger|context manager|state machine";

    /// A coding verb followed, within a few words, by something that can be written.
    private static readonly Regex VerbObject = new(
        @"\b(" + Verbs + @")\s+(me\s+|us\s+)?((a|an|the|some|this|that|your own|one|another)\s+)?([\w'/]+\s+){0,3}?(" + Objects + @")\b",
        Options);

    private static readonly Regex RequestFrame = new(
        @"\b(can|could|would|will)\s+you\s+(please\s+|just\s+|now\s+|also\s+)?(" + Verbs + @")\b|\bplease\s+(" + Verbs + @")\b|\b(let's|lets|let us)\s+(" + Verbs + @")\b|\bi\s+(want|need|would like|'d like|d like)\s+you\s+to\s+(" + Verbs + @")\b|\byou\s+(need|have|should|must|are going|'re going)\s+to\s+(" + Verbs + @")\b|\b(you|just)\s+(wanna|want to|gonna|going to)\s+(" + Verbs + @")\b|\byou('re| are)\s+(going to|gonna)\s+(" + Verbs + @")\b|^(so\s+|okay\s+|now\s+)*(wanna|want to|gonna)\s+(" + Verbs + @")\b|\byour task\b|\bgo ahead and\s+(" + Verbs + @")\b",
        Options);

    /// "How would you implement ..." asks how, not for code, even though "would you implement" also
    /// appears in polite requests. Question words are cut out before the request frame is tested.
    private static readonly Regex QuestionWordBeforeModal = new(
        @"\b(how|what|why|when|where|which)\s+(can|could|would|will|do|does|did|should)\s+you\b",
        Options);

    private static readonly Regex LeadingFiller = new(
        @"^(okay|ok|so|now|alright|all right|right|well|then|and|great|perfect|cool|good|nice|next)\b[,.]?\s*",
        Options);

    private static readonly Regex ImperativeStart = new(@"^(" + Verbs + @")\b", Options);

    private static readonly Regex FirstPersonStart = new(@"^(i|we|my|our)\s", Options);

    private static readonly Regex PastNarration = new(
        @"\b(i|we)\s+(have\s+|had\s+|just\s+|also\s+|usually\s+|always\s+|often\s+|recently\s+|really\s+)?(wrote|written|implemented|built|created|developed|coded|did|used|worked)\b",
        Options);

    private static readonly Regex ConceptualFrame = new(
        @"\b(what is|what's|what are|what does|how does|how do|why|difference between|explain|describe|tell me about|walk me through)\b",
        Options);

    /// Phrases that ask for code by themselves. The task itself is usually on the screen, so these are
    /// "deictic" requests (see `IsDeicticRequest`) unless the sentence adds detail.
    private static readonly string[] AlwaysCodingPhrases =
    {
        "write some code", "write the code", "write this code", "write that code",
        "show me the code", "type it out"
    };

    /// Object phrases that mean a coding task unless the sentence is conceptual.
    private static readonly string[] ObjectPhrases =
    {
        "write a fastapi", "fastapi service", "implement a fastapi", "build a fastapi", "create a fastapi",
        "code a fastapi", "write an endpoint", "implement an endpoint", "add an endpoint",
        "write a service", "implement a service", "build a service"
    };

    private static string StrippingLeadingFillers(string text)
    {
        var current = text;
        while (LeadingFiller.IsMatch(current))
        {
            var next = LeadingFiller.Replace(current, "");
            if (next == current) break;
            current = next;
        }
        return current;
    }

    private const string DoableVerbs = "reverse|sort|parse|read|write|merge|remove|find|count|check|convert|split|join|flatten|filter|swap|sum|loop|iterate|search|validate|format|print|open|fetch|call|create|declare|define|implement|make|build|generate|copy|delete|append|insert|replace|concatenate|compare|calculate|compute";

    /// "How to reverse a string in Python", or the same with its first words cut off by the microphone.
    private static readonly Regex HowToStart = new(
        @"^(?:(?:how to|how (?:do|would|can|could|should) (?:you|i|we))\s+)?(?:" + DoableVerbs + @")\b",
        Options);

    /// A small thing to do, in a named language: the answer is the code, not a talk about it.
    public static bool AsksHowToInLanguage(string text)
    {
        var normalized = Normalized(text);
        if (!HowToStart.IsMatch(StrippingLeadingFillers(normalized))) return false;
        return LanguageReference.IsMatch(normalized);
    }

    public static bool IsTaskRequest(string text)
    {
        var normalized = Normalized(text);
        if (normalized.Length == 0) return false;
        if (AsksHowToInLanguage(text)) return true;

        // "Write a function ..." is a request; "how would you write a function" is a cue-card question.
        var hasVerbObject = VerbObject.IsMatch(normalized);
        var withoutQuestionModals = QuestionWordBeforeModal.Replace(normalized, " ");
        if (hasVerbObject
            && (RequestFrame.IsMatch(withoutQuestionModals) || ImperativeStart.IsMatch(StrippingLeadingFillers(normalized))))
        {
            return true;
        }

        if (FirstPersonStart.IsMatch(normalized) || PastNarration.IsMatch(normalized) || ConceptualFrame.IsMatch(normalized))
        {
            return false;
        }
        if (normalized.ContainsAny(AlwaysCodingPhrases)) return true;
        return normalized.ContainsAny(ObjectPhrases);
    }

    private static readonly HashSet<string> GenericObjects = new()
    {
        "code", "solution", "problem", "exercise", "challenge", "task"
    };

    /// "Write this code" / "solve this problem": the task is on the interviewer's screen, not in
    /// their words. There is nothing to generate from, so the candidate is pointed at the
    /// screenshot shortcut instead of the model inventing a task.
    public static bool IsDeicticRequest(string text)
    {
        var normalized = Normalized(text);
        // "How to reverse a string in Python" names its task in full.
        if (!IsTaskRequest(text) || AsksHowToInLanguage(text) || DetailCues.IsMatch(normalized)) return false;
        foreach (Match match in VerbObject.Matches(normalized))
        {
            var written = match.Groups[match.Groups.Count - 1].Value;
            if (!GenericObjects.Contains(written)) return false;
        }
        return true;
    }

    // MARK: Follow-ups while code is on screen

    private const string Languages = @"python|javascript|typescript|java|kotlin|golang|rust|swift|c\+\+|c#|csharp|cpp|ruby|php|scala|sql|bash|node(?:\.?js)?|js|ts";

    /// "in TypeScript", "to Java", "using Rust" - never "for" ("we use TypeScript for the frontend").
    private static readonly Regex LanguageReference = new(
        @"\b(?:in|into|to|with|using)\s+(?:the\s+)?(?:" + Languages + @")(?![\w+#])|\bin\s+go\s*[?.!]*$",
        Options);

    /// A bare language answer to "what language?" or a rewrite request: "In TypeScript, please".
    private static readonly Regex LanguageOnly = new(
        @"^(?:(?:and|now|okay|ok|so)\s+)*(?:in\s+|with\s+|using\s+)?(?:" + Languages + @")(?:\s+(?:please|now|too|instead|as well))*[?.!]*$",
        Options);

    /// Verbs that ask for code to be produced or changed. Deliberately narrow: "do you have experience
    /// with TypeScript" must not read as a rewrite request.
    private static readonly Regex RewriteVerbs = new(
        @"\b(?:write|rewrite|convert|translate|port|switch|redo|implement|code|show|give|make|try)\b|\b(?:can|could|would|will)\s+you\s+(?:please\s+)?(?:do|use|see)\b|\bhow\s+(?:would|does|will)\s+(?:it|this|that)\s+look\b|\b(?:need|want|prefer)\s+(?:it|this)\b",
        Options);

    private static readonly Regex Referent = new(
        @"\b(?:it|this|that|these|the\s+(?:function|code|solution|loop|method|class|implementation|program|script)|your\s+(?:code|solution|function|approach|implementation))\b",
        Options);

    private static readonly Regex ImproveVerbs = new(
        @"\b(?:improve|optimi[sz]e|refactor|simplify|shorten|clean(?:\s+it)?\s+up|tidy|speed\s+(?:it\s+)?up)\b",
        Options);

    private static readonly Regex MakeBetter = new(
        @"\bmake\s+(?:it|this|that|the\s+\w+)\s+(?:a\s+bit\s+|a\s+little\s+|much\s+)?(?:faster|quicker|shorter|cleaner|simpler|better|smaller|more\s+\w+|less\s+\w+|recursive|iterative|in.?place|async|generic|thread.?safe|readable|efficient)\b",
        Options);

    private static readonly Regex AddOrHandle = new(
        @"\badd\s+(?:some\s+|a\s+|an\s+)?(?:error\s+handling|type\s+hints?|types?|tests?|unit\s+tests?|comments?|docstrings?|logging|validation|input\s+validation|main)\b|\bhandle\s+(?:the\s+)?(?:edge\s+cases?|empty|none|null|nil|errors?|exceptions?|unicode|invalid|negative|large|big|special)\b|\b(?:write|create|generate)\s+(?:a\s+|some\s+)?(?:unit\s+)?tests?\b",
        Options);

    private static readonly Regex Alternatives = new(
        @"\b(?:another|different|alternative|other|better|easier|simpler|shorter|cleaner|faster|quicker|nicer|smarter|more\s+(?:pythonic|idiomatic|efficient|readable|concise))\s+(?:way|approach|solution|method|implementation|version|technique|algorithm|option)\b|\bdifferently\b|\bwithout\s+(?:using\s+)?\w+",
        Options);

    private static readonly Regex Constraints = new(
        @"\b(?:use|using|try|with)\s+(?:a\s+|an\s+|the\s+)?(?:for\s+loop|while\s+loop|loop|recursion|recursive|stack|queue|two\s+pointers?|sliding\s+window|dict(?:ionary)?|hash\s?map|set|generator|regex|list\s+comprehension|built.?in|itertools|collections|numpy|pandas)\b",
        Options);

    private static readonly Regex ConstraintCue = new(
        @"\b(?:can|could|would|will)\s+you\b|\bhow\s+about\b|\bwhat\s+about\b|\btry\b|\blet's\b|\bnow\b|\binstead\b|^use\b",
        Options);

    private const string Techniques = @"sliding\s+window|two\s+pointers?|binary\s+search|recursion|memoi[sz]ation|dynamic\s+programming|backtracking|greedy|divide\s+and\s+conquer|bfs|dfs|breadth.?first|depth.?first|union.?find|trie|prefix\s+sum|kadane|bit\s+manipulation|monotonic\s+stack";

    private static readonly Regex TechniqueExample = new(
        @"\b(?:show|demonstrate|give|write|code|implement)\b[^.?!]{0,40}\b(?:" + Techniques + @")\b|\bhow\s+(?:(?:do|would|can|could|might)\s+)?(?:you|we|i|one)\s+(?:use|apply|implement|code|write|do)\b[^.?!]{0,30}\b(?:" + Techniques + @")\b|\b(?:" + Techniques + @")\s+(?:example|code|implementation|snippet|demo)\b|\bexamples?\s+of\s+(?:a\s+|an\s+|the\s+)?(?:" + Techniques + @")\b",
        Options);

    /// "How about using recursion?" and "what about a loop?" are suggestions, not questions about the code.
    private static readonly Regex QuestionStart = new(@"^(?:what|why|how|when|where|which|who)\b(?!\s+about\b)", Options);

    private static readonly Regex StrictExplain = new(
        @"\b(?:explain|describe|tell me about|walk me through|difference between|why)\b",
        Options);

    private static readonly Regex DefinitionFrame = new(@"\b(?:what is|what's|what are|what does)\b", Options);

    private static readonly Regex HypotheticalLook = new(@"^how\s+(?:would|does|will)\s+(?:it|this|that)\s+look", Options);

    private static readonly Regex PoliteRequest = new(@"\b(?:can|could|would|will)\s+you\b|\bplease\b|\blet's\b", Options);

    private static readonly Regex ImproveAtStart = new(
        @"^(?:improve|optimi[sz]e|refactor|simplify|shorten|clean|tidy|speed)\b",
        Options);

    /// Classifies an interviewer remark made while code is on screen, or null when it is an ordinary
    /// question (which the answer card handles, with the code as context).
    public static CodingFollowUp? FollowUp(string text)
    {
        var normalized = Normalized(text);
        var wordCount = normalized.SplitOmittingEmpty(' ').Count;
        if (wordCount < 2 || wordCount > 24) return null;
        if (FirstPersonStart.IsMatch(normalized) || PastNarration.IsMatch(normalized)
            || ConversationUnderstanding.LooksLikeCandidateNarrative(normalized)
            || StrictExplain.IsMatch(normalized))
        {
            return null;
        }
        var asksForAlternative = Alternatives.IsMatch(normalized);
        if (DefinitionFrame.IsMatch(normalized) && !asksForAlternative) return null;

        // A technique demo may be phrased as a how-question ("how do you use two pointers").
        if (TechniqueExample.IsMatch(normalized)) return CodingFollowUp.Example;

        var asksHow = QuestionStart.IsMatch(normalized);
        if (LanguageOnly.IsMatch(normalized)) return CodingFollowUp.Change;
        if (LanguageReference.IsMatch(normalized) && RewriteVerbs.IsMatch(normalized)
            && (!asksHow || HypotheticalLook.IsMatch(normalized)))
        {
            return CodingFollowUp.Change;
        }
        if (asksForAlternative) return CodingFollowUp.Change;
        if (asksHow) return null;

        // "How would you improve the database?" asks for an opinion; "improve it" asks for code.
        var startsWithImproveVerb = ImproveAtStart.IsMatch(StrippingLeadingFillers(normalized));
        if (ImproveVerbs.IsMatch(normalized)
            && (Referent.IsMatch(normalized) || startsWithImproveVerb || PoliteRequest.IsMatch(normalized)))
        {
            return CodingFollowUp.Change;
        }
        if (MakeBetter.IsMatch(normalized) || AddOrHandle.IsMatch(normalized)) return CodingFollowUp.Change;
        if (Constraints.IsMatch(normalized) && ConstraintCue.IsMatch(normalized)) return CodingFollowUp.Change;
        return null;
    }

    // MARK: A code example of something already said

    /// "Show me an example in code", "can you show that in code?", "give me a code snippet": the
    /// interviewer wants code for a concept, not an explanation of it.
    private static readonly Regex CodeExample = new(
        @"\b(?:show|give|write|share|demonstrate|sketch|put)\b[^.?!]{0,40}(?:\b(?:in|as|with)\s+(?:some\s+|a\s+little\s+|a\s+bit\s+of\s+|actual\s+|real\s+)?code\b|\bcode\s+(?:examples?|snippets?|samples?)\b|\bexamples?\s+(?:in|of)\s+code\b)",
        Options);

    /// Words that say how to show something, not what to show.
    private static readonly HashSet<string> ExampleFrameWords = new()
    {
        "okay", "ok", "so", "now", "alright", "right", "well", "then", "and", "also", "maybe", "yeah", "great", "good",
        "can", "could", "would", "will", "you", "please", "just", "me", "us", "quickly", "quick", "briefly",
        "show", "give", "write", "share", "demonstrate", "sketch", "put", "an", "a", "the", "some", "any", "one",
        "this", "that", "it", "these", "those", "how", "what", "works", "work", "looks", "look", "like",
        "example", "examples", "in", "as", "with", "of", "for", "to", "code", "snippet", "snippets", "sample", "samples",
        "little", "bit", "actual", "real", "simple", "small", "short", "kind", "sort"
    };

    public static bool AsksForCodeExample(string text)
    {
        var normalized = Normalized(text);
        if (normalized.Length == 0 || FirstPersonStart.IsMatch(normalized) || PastNarration.IsMatch(normalized)) return false;
        return CodeExample.IsMatch(normalized);
    }

    /// "Can you show me that in code?" names nothing: its subject is the question before it.
    /// "Cache, can you show me an example in code?" names its own.
    public static bool CodeExampleNamesItsSubject(string text)
    {
        var words = Normalized(text).SplitWhere(c => !char.IsLetter(c) && !char.IsNumber(c) && c != '\'');
        return words.Any(word => !ExampleFrameWords.Contains(word));
    }

    // MARK: Coding session

    private static readonly Regex SessionCues = new(
        @"\bnew (python |java |javascript |typescript |go |kotlin |swift )?(file|notebook|project)\b|\bopen (up )?(your|an|a|the) (editor|ide|terminal|notebook|repl|vs ?code|pycharm|intellij)\b|\bshare (your|the) screen\b|\bscreen shar(e|ing)\b|\bstart coding\b|\blet's code\b|\bpair program|\bcode together\b|\bpractical (task|exercise)\b|\blive coding\b|\bsome coding\b|\bcoding (exercise|challenge|task|problem|question|round|test|part|session)\b",
        Options);

    private static readonly Regex DetailCues = new(
        @"\b(input|output|return|returns|returning|parameter|parameters|param|argument|arguments|string|strings|list|lists|array|arrays|number|numbers|integer|integers|float|dictionary|dict|tuple|character|characters|char|chars|print|reverse|reversed|sort|sorted|count|sum|filter|unique|duplicate|duplicates|empty|edge case|edge cases|recursive|recursion|iterative|complexity|without|instead|given|takes|gets|accepts|receives|should|must|text|word|words|palindrome)\b",
        Options);

    /// Words that start or continue a coding exercise without yet naming the task.
    public static bool StartsCodingSession(string text)
    {
        var normalized = Normalized(text);
        if (PastNarration.IsMatch(normalized)) return false;
        return SessionCues.IsMatch(normalized);
    }

    /// A fragment that adds detail to a task already being spoken ("like text, return string").
    public static bool LooksLikeTaskDetail(string text)
    {
        var normalized = Normalized(text);
        // Real fragments are short. A long run of speech is conversation, not a clarification.
        var wordCount = normalized.SplitOmittingEmpty(' ').Count;
        if (wordCount < 3 || wordCount > 16 || normalized.Contains('?')) return false;
        if (FirstPersonStart.IsMatch(normalized)
            || ConversationUnderstanding.HasInterviewerFrame(normalized)
            || ConversationUnderstanding.LooksLikeCandidateNarrative(normalized))
        {
            return false;
        }
        return DetailCues.IsMatch(normalized);
    }
}
