namespace InterviewMaster.Domain;

public static class BuiltInTemplates
{
    // MARK: - Behavioral

    public static readonly IReadOnlyList<InterviewTemplate> Behavioral = new InterviewTemplate[]
    {
        new InterviewTemplate(
            Id: "behavioral-general",
            Name: "General Behavioral",
            Description: "Common behavioral interview questions using STAR method",
            Category: TemplateCategory.Behavioral,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Tell me about yourself", Topic: "Introduction", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "2-3 minutes max", "Present → Past → Future structure", "Tailor to the role"
                }),
                new TemplateQuestion(Text: "Tell me about a time you had a conflict with a team member", Topic: "Conflict Resolution", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Use STAR: Situation, Task, Action, Result", "Focus on resolution, not blame", "Show empathy and communication skills"
                }),
                new TemplateQuestion(Text: "Describe a situation where you had to meet a tight deadline", Topic: "Time Management", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Explain prioritization strategy", "Mention trade-offs you made", "Highlight the outcome"
                }),
                new TemplateQuestion(Text: "What is your greatest technical achievement?", Topic: "Achievement", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Quantify the impact", "Explain technical challenges", "Show leadership or initiative"
                }),
                new TemplateQuestion(Text: "How do you handle disagreements about technical decisions?", Topic: "Decision Making", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Show data-driven approach", "Mention prototyping or proof of concept", "Emphasize team alignment"
                }),
                new TemplateQuestion(Text: "Tell me about a time you failed and what you learned", Topic: "Growth", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Be genuine about the failure", "Focus on lessons learned", "Show how you applied those lessons"
                })
            }
        )
    };

    // MARK: - System Design

    public static readonly IReadOnlyList<InterviewTemplate> SystemDesign = new InterviewTemplate[]
    {
        new InterviewTemplate(
            Id: "sysdesign-url-shortener",
            Name: "URL Shortener",
            Description: "Design a URL shortening service like bit.ly",
            Category: TemplateCategory.SystemDesign,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Design a URL shortener like bit.ly", Topic: "System Design", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Step 1: Clarify requirements (read vs write ratio, scale, analytics)",
                    "Step 2: API design (POST /shorten, GET /:code)",
                    "Step 3: Encoding strategy (base62, hash, counter)",
                    "Step 4: Storage (NoSQL for key-value, cache layer)",
                    "Step 5: Scale (horizontal scaling, CDN, rate limiting)"
                })
            }
        ),
        new InterviewTemplate(
            Id: "sysdesign-chat",
            Name: "Chat System",
            Description: "Design a real-time chat application like Slack",
            Category: TemplateCategory.SystemDesign,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Design a real-time chat system like Slack", Topic: "System Design", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Step 1: Requirements (1:1, group chat, channels, presence)",
                    "Step 2: Protocol choice (WebSocket for real-time, HTTP fallback)",
                    "Step 3: Message storage (partitioned by channel/time)",
                    "Step 4: Delivery guarantees (at-least-once, idempotency)",
                    "Step 5: Scale (connection servers, message queue, read replicas)"
                })
            }
        ),
        new InterviewTemplate(
            Id: "sysdesign-twitter",
            Name: "Twitter/X Feed",
            Description: "Design a social media feed like Twitter",
            Category: TemplateCategory.SystemDesign,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Design a Twitter-like social media feed", Topic: "System Design", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Step 1: Requirements (post, follow, timeline, search)",
                    "Step 2: Fan-out approaches (push vs pull vs hybrid)",
                    "Step 3: Timeline generation and caching",
                    "Step 4: Storage (posts, social graph, media)",
                    "Step 5: Scale (celebrity problem, sharding, CDN)"
                })
            }
        )
    };

    // MARK: - Test Automation

    public static readonly IReadOnlyList<InterviewTemplate> TestAutomation = new InterviewTemplate[]
    {
        new InterviewTemplate(
            Id: "qa-playwright",
            Name: "Playwright Automation",
            Description: "Modern E2E automation, locators, fixtures, and CI feedback",
            Category: TemplateCategory.TestAutomation,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "How do you design a stable Playwright test suite?", Topic: "testStrategy", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Risk-based coverage across smoke, regression, and critical paths",
                    "Role/test-id locators, fixtures, and isolated test data",
                    "Traces, screenshots, and retries used to diagnose, not hide flakes"
                }),
                new TemplateQuestion(Text: "How do you handle flaky E2E tests?", Topic: "flakyTests", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Classify app bug vs test timing vs environment/data issue",
                    "Use auto-waiting and deterministic assertions instead of sleeps",
                    "Keep failure artifacts: trace, video, logs, network"
                }),
                new TemplateQuestion(Text: "What is your Page Object Model strategy?", Topic: "pageObjects", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Hide page mechanics, not test intent",
                    "Prefer small page/component objects over one large class",
                    "Keep assertions in tests unless a domain helper is clearer"
                }),
                new TemplateQuestion(Text: "How do you test APIs and UI together?", Topic: "apiTesting", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "API tests for contracts and edge cases",
                    "UI E2E for user-critical flows",
                    "Use API setup/cleanup to keep UI tests fast"
                })
            }
        ),
        new InterviewTemplate(
            Id: "qa-playwright-deep-dive",
            Name: "Playwright Deep Dive",
            Description: "Playwright-specific APIs, debugging, isolation, and scaling",
            Category: TemplateCategory.TestAutomation,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Why are Playwright locators more stable than CSS or XPath?", Topic: "playwrightLocators", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "Use user-facing locators first: role, label, text",
                    "Locators auto-wait and retry assertions",
                    "Use test ids only for elements without semantic hooks"
                }),
                new TemplateQuestion(Text: "How do you avoid manual waits in Playwright?", Topic: "webFirstAssertions", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Use web-first assertions with expect(locator)",
                    "Wait on app state, URL, network result, or visible UI",
                    "Avoid waitForTimeout except as temporary debugging"
                }),
                new TemplateQuestion(Text: "How do you handle login and test isolation?", Topic: "storageState", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Use setup project or global setup to create storageState",
                    "Keep independent data per test or worker",
                    "Use browser contexts to isolate sessions"
                }),
                new TemplateQuestion(Text: "How do you mock network calls in Playwright?", Topic: "pageRoute", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Use page.route for targeted request interception",
                    "Mock external dependencies, not the behavior under test",
                    "Keep API contract coverage separate from mocked UI flows"
                }),
                new TemplateQuestion(Text: "How do you debug a failed Playwright test?", Topic: "traceViewer", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "Open trace first: actions, snapshots, console, network",
                    "Check screenshot/video and app logs",
                    "Classify as app bug, test bug, data issue, or environment issue"
                }),
                new TemplateQuestion(Text: "How do you scale Playwright in CI?", Topic: "cicd", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Shard by file or project and run workers in parallel",
                    "Use retries only to collect evidence and reduce noise",
                    "Separate fast smoke from full regression"
                })
            }
        ),
        new InterviewTemplate(
            Id: "qa-strategy",
            Name: "QA Strategy",
            Description: "Coverage decisions, test pyramid, CI, and release confidence",
            Category: TemplateCategory.TestAutomation,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "How do you decide what to automate?", Topic: "testStrategy", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Business risk, frequency, regression value, and stability",
                    "Automate repeatable checks; explore uncertain behavior manually",
                    "Prefer lower-level tests when they give the same confidence"
                }),
                new TemplateQuestion(Text: "Explain the test pyramid in practice", Topic: "testAutomation", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "Many unit tests, fewer integration tests, targeted E2E",
                    "Avoid an inverted pyramid of slow UI tests",
                    "Use contract/API tests to reduce E2E scope"
                }),
                new TemplateQuestion(Text: "What makes a good CI test pipeline?", Topic: "cicd", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Fast smoke gate first, deeper suites later",
                    "Parallel execution and clear failure artifacts",
                    "Quarantine policy with ownership and expiry"
                }),
                new TemplateQuestion(Text: "How would you test an LLM or chatbot feature?", Topic: "llmEvaluation", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Golden datasets and scenario-based evals",
                    "Deterministic checks where possible; rubric judging where not",
                    "Track regressions, safety failures, latency, and cost"
                })
            }
        )
    };

    // MARK: - Coding

    public static readonly IReadOnlyList<InterviewTemplate> Coding = new InterviewTemplate[]
    {
        new InterviewTemplate(
            Id: "coding-arrays-strings",
            Name: "Arrays & Strings",
            Description: "Common array and string manipulation problems",
            Category: TemplateCategory.Coding,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Two Sum: Find indices of two numbers that add up to target", Topic: "Arrays", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "Hash map for O(n) solution", "Store complement as key"
                }),
                new TemplateQuestion(Text: "Longest Substring Without Repeating Characters", Topic: "Strings", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Sliding window technique", "Hash set to track characters"
                }),
                new TemplateQuestion(Text: "Merge Intervals: Merge overlapping intervals", Topic: "Arrays", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Sort by start time", "Compare current end with next start"
                }),
                new TemplateQuestion(Text: "Trapping Rain Water", Topic: "Arrays", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "Two pointer approach", "Track max left and max right"
                })
            }
        ),
        new InterviewTemplate(
            Id: "coding-trees-graphs",
            Name: "Trees & Graphs",
            Description: "Tree traversal, graph search, and related problems",
            Category: TemplateCategory.Coding,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Validate Binary Search Tree", Topic: "Trees", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "In-order traversal should be sorted", "Pass min/max bounds recursively"
                }),
                new TemplateQuestion(Text: "Lowest Common Ancestor of a Binary Tree", Topic: "Trees", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Post-order traversal", "If both sides return non-null, current node is LCA"
                }),
                new TemplateQuestion(Text: "Number of Islands (grid BFS/DFS)", Topic: "Graphs", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "BFS or DFS from each unvisited land cell", "Mark visited cells"
                }),
                new TemplateQuestion(Text: "Course Schedule (topological sort)", Topic: "Graphs", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Build adjacency list", "Detect cycle with DFS or use Kahn's algorithm"
                })
            }
        ),
        new InterviewTemplate(
            Id: "coding-dp",
            Name: "Dynamic Programming",
            Description: "Classic DP problems from easy to hard",
            Category: TemplateCategory.Coding,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Climbing Stairs: How many ways to reach the top?", Topic: "DP", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "Similar to Fibonacci", "dp[i] = dp[i-1] + dp[i-2]"
                }),
                new TemplateQuestion(Text: "Longest Common Subsequence", Topic: "DP", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "2D DP table", "If chars match: dp[i][j] = dp[i-1][j-1] + 1"
                }),
                new TemplateQuestion(Text: "Coin Change: Minimum coins to make amount", Topic: "DP", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Bottom-up DP", "dp[amount] = min(dp[amount], dp[amount - coin] + 1)"
                }),
                new TemplateQuestion(Text: "Edit Distance between two strings", Topic: "DP", Difficulty: TemplateDifficulty.Hard, Hints: new[] {
                    "2D DP: insert, delete, replace operations", "dp[i][j] = min of three options"
                })
            }
        )
    };

    // MARK: - Language-Specific

    public static readonly IReadOnlyList<InterviewTemplate> LanguageSpecific = new InterviewTemplate[]
    {
        new InterviewTemplate(
            Id: "lang-python",
            Name: "Python Deep Dive",
            Description: "Python-specific interview questions",
            Category: TemplateCategory.LanguageSpecific,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Explain the GIL (Global Interpreter Lock) in Python", Topic: "Python Internals", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Mutex that protects access to Python objects", "Prevents true multithreading for CPU-bound tasks", "Use multiprocessing or asyncio instead"
                }),
                new TemplateQuestion(Text: "What are Python decorators and how do they work?", Topic: "Python", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Functions that modify other functions", "Use @syntax sugar", "Common: @property, @staticmethod, @cache"
                }),
                new TemplateQuestion(Text: "Explain list comprehension vs generator expression", Topic: "Python", Difficulty: TemplateDifficulty.Easy, Hints: new[] {
                    "List: [x for x in range(10)] - creates full list in memory", "Generator: (x for x in range(10)) - lazy evaluation"
                })
            }
        ),
        new InterviewTemplate(
            Id: "lang-javascript",
            Name: "JavaScript/TypeScript",
            Description: "JS/TS specific interview questions",
            Category: TemplateCategory.LanguageSpecific,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Explain the event loop in JavaScript", Topic: "JS Runtime", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Call stack → Microtask queue → Macrotask queue", "Promises go to microtask queue", "setTimeout goes to macrotask queue"
                }),
                new TemplateQuestion(Text: "What is closure and why is it useful?", Topic: "JavaScript", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Function that captures variables from outer scope", "Used for data privacy, factories, callbacks"
                }),
                new TemplateQuestion(Text: "Explain Promise.all vs Promise.allSettled vs Promise.race", Topic: "JavaScript", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "all: fails fast on first rejection", "allSettled: waits for all, returns status", "race: resolves/rejects with first settled"
                })
            }
        ),
        new InterviewTemplate(
            Id: "lang-java",
            Name: "Java Core",
            Description: "Core Java interview questions",
            Category: TemplateCategory.LanguageSpecific,
            Questions: new TemplateQuestion[] {
                new TemplateQuestion(Text: "Explain the difference between HashMap and ConcurrentHashMap", Topic: "Java Collections", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "HashMap: not thread-safe, allows null key", "ConcurrentHashMap: segment-level locking, no null keys"
                }),
                new TemplateQuestion(Text: "What is the Java Memory Model? Explain heap vs stack", Topic: "JVM", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "Stack: method frames, local variables, thread-local", "Heap: objects, shared across threads, GC managed"
                }),
                new TemplateQuestion(Text: "Explain Spring dependency injection and IoC", Topic: "Spring", Difficulty: TemplateDifficulty.Medium, Hints: new[] {
                    "IoC: framework controls object lifecycle", "DI: objects receive dependencies, don't create them", "@Autowired, @Component, @Service"
                })
            }
        )
    };

    public static List<InterviewTemplate> TemplatesForCategory(TemplateCategory category) =>
        All.Where(template => template.Category == category).ToList();

    public static IReadOnlyList<InterviewTemplate> All { get; } =
        Behavioral.Concat(SystemDesign).Concat(TestAutomation).Concat(Coding).Concat(LanguageSpecific).ToArray();
}
