import Cocoa

// Runs the macOS processor's own helpers and prompt builders over the corpus and writes one
// `key<TAB>value` line per result. The C# harness writes the same lines; compare.py diffs them.

/// Stands in for the real use case so the run never reads the user's saved interview memory.
struct MemoryRetrievalUseCase {
    static var context: String?
    func retrieve(forTopics topics: [String]) -> String? { MemoryRetrievalUseCase.context }
}

final class PromptClient: AnthropicClient {
    var prompts: [String] = []
    init() { super.init(apiKey: "parity-no-network") }
    override func warmupConnection() async {}
    override func cancelCurrentRequest() {}
    override func streamTextMessage(prompt: String, maxTokens: Int = 300,
                                    onChunk: @escaping (String) -> Void) async -> Result<Void, Error> {
        prompts.append(prompt)
        return .success(())
    }
}

final class PromptDelegate: VoiceInterviewProcessorDelegate {
    var userBackground = ""
    var pinnedSolution: String?
    var conversationContext = ConversationContext()
    func processorShowLoading(_ message: String, color: NSColor, turnID: UUID) {}
    func processorHideLoading(turnID: UUID) {}
    func processorDidReceiveQuestion(_ text: String, topic: String, messageType: InterviewMessage.MessageType, source: CaptureDevice, turnID: UUID, sequence: Int) {}
    func processorDidStartStreaming(messageType: InterviewMessage.MessageType, topic: String, latencyMs: Int?, turnID: UUID, sequence: Int) {}
    func processorDidReceiveAnswerChunk(_ fullContent: String, turnID: UUID) {}
    func processorDidFinishAnswer(_ fullAnswer: String, totalLatencyMs: Int?, turnID: UUID) {}
    func processorDidUpdateStatus(_ message: String) {}
    func processorDidUpdateBriefing(_ briefing: InterviewBriefing) {}
    func processorDidUpdateCodingTask(_ content: String) {}
}

var lines: [String] = []

func esc(_ text: String) -> String {
    var out = String.UnicodeScalarView()
    for scalar in text.unicodeScalars {
        switch scalar {
        case "\\": out.append(contentsOf: "\\\\".unicodeScalars)
        case "\n": out.append(contentsOf: "\\n".unicodeScalars)
        case "\r": out.append(contentsOf: "\\r".unicodeScalars)
        case "\t": out.append(contentsOf: "\\t".unicodeScalars)
        default: out.append(scalar)
        }
    }
    return String(out)
}

func emit(_ key: String, _ value: String) { lines.append(key + "\t" + esc(value)) }
func opt(_ value: String?) -> String { value.map { "S:" + $0 } ?? "nil" }
func bit(_ value: Bool) -> String { value ? "1" : "0" }
func bits(_ values: [Bool]) -> String { values.map(bit).joined() }
func name(_ value: String?) -> String { value ?? "-" }
func utf8Sorted(_ values: Set<String>) -> [String] {
    values.sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
}

let sampleBackground = """
Alex Example
Senior Software Engineer

EXPERIENCE
Senior Software Engineer, Example Payments (2021 - Present)
- Built payment APIs in Go and Kotlin, on Kubernetes and PostgreSQL.
- Led a RAG prototype with Python and a vector database.

QA Automation Engineer, Sample Labs (2016 - 2021)
- Wrote Playwright and Selenium suites in TypeScript; cut flaky tests by half.

SKILLS
Go, Kotlin, Python, TypeScript, Playwright, Kubernetes, PostgreSQL
"""
let samplePinned = "def reverse(text):\n    return text[::-1]"
let sampleMemory = "You've been asked about this topic 2 times before\nMention the retry budget."

func filledContext() -> ConversationContext {
    let context = ConversationContext()
    context.addUtterance(text: "What is RAG and CAG?", topic: "ragCag", isQuestion: true, sequence: 1)
    context.addUtterance(text: "I built a retrieval pipeline at my last job.", topic: nil, isQuestion: false, sequence: 2)
    context.addUtterance(text: "So how do you evaluate a classifier?", topic: "classification", isQuestion: true, sequence: 3)
    return context
}

func describe(_ decision: CodingTurnDecision?) -> String {
    guard let decision else { return "nil" }
    switch decision {
    case .armSession: return "armSession"
    case .needsScreenContext: return "needsScreenContext"
    case .newTask(let text): return "newTask:" + text
    case .refineTask(let text): return "refineTask:" + text
    case .changeCode(let text): return "changeCode:" + text
    }
}

func describe(_ steps: [CodingInterviewStep]) -> String {
    steps.map { "\($0.number)\u{1}\($0.fileName)\u{1}\($0.why)\u{1}\($0.language)\u{1}\($0.code)" }.joined(separator: "\u{2}")
}

@main
enum ProcessorParity {
    @MainActor static func main() async {
        let arguments = CommandLine.arguments
        let data = try! Data(contentsOf: URL(fileURLWithPath: arguments[1]))
        let corpus = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
        let texts = corpus["texts"] as! [String]
        let topics = corpus["topics"] as! [String]

        let configurations: [(String, [String: String])] = [
            ("se", ["InterviewMaster.Role": "software_engineer", "InterviewMaster.ProgrammingLanguage": "swift",
                    "InterviewMaster.ListeningLanguage": "en", "InterviewMaster.ResponseLanguage": "en"]),
            ("ai", ["InterviewMaster.Role": "senior_ai_engineer", "InterviewMaster.ProgrammingLanguage": "python",
                    "InterviewMaster.ListeningLanguage": "en", "InterviewMaster.ResponseLanguage": "en"]),
            ("qa", ["InterviewMaster.Role": "senior_qa_engineer", "InterviewMaster.ProgrammingLanguage": "typescript",
                    "InterviewMaster.ListeningLanguage": "bg", "InterviewMaster.ResponseLanguage": "bg"])
        ]
        let lastTopics: [String?] = [nil, "hashMap", "ragCag", "playwright"]
        let classifiedTopics: [String?] = [nil, "unknown", "hashMap", "general", "coding", "followUp"]
        let now = Date(timeIntervalSince1970: 1_700_000_000)

        for (label, settings) in configurations {
            UserDefaults.standard.setVolatileDomain(settings, forName: UserDefaults.argumentDomain)
            emit("CONFIG", label)
            let processor = VoiceInterviewProcessor()

            for (index, text) in texts.enumerated() {
                emit("T", text)
                let normalized = processor.normalizedQuestionText(text)
                emit("norm", normalized)
                let tokens = processor.technicalQuestionTokens(in: normalized)
                emit("tokens", utf8Sorted(tokens).joined(separator: ","))
                emit("hint", opt(processor.bestTopicHint(from: normalized, technicalTokens: tokens)))
                emit("flags", bits([
                    processor.isVagueFollowUpPrompt(normalized, technicalTokens: tokens),
                    processor.isBareIncompletePrompt(normalized),
                    processor.shouldSkipAsSocialPleasantry(text),
                    processor.isWhisperHallucination(text),
                    processor.shouldSkipAsFillerOrGreeting(text),
                    processor.isLocallyIncomplete(text),
                    processor.checkForQuestionMarkers(text)
                ]))
                emit("repair1", processor.repairNoisyTechnicalTranscript(text, favorsAIAcronyms: true))
                emit("repair0", processor.repairNoisyTechnicalTranscript(text, favorsAIAcronyms: false))
                emit("repairD", processor.repairNoisyTechnicalTranscript(text))
                for lastTopic in lastTopics {
                    let signal = processor.questionSignal(for: text, lastTopic: lastTopic)
                    emit("sig:" + name(lastTopic), "\(signal.score)|\(signal.reasons.joined(separator: ","))|\(opt(signal.topicHint))|" + bits([
                        signal.isFollowUp, signal.isBareIncomplete, signal.protectsFromSkip,
                        processor.shouldVetoQuestionAsCandidateStatement(text, signal: signal),
                        processor.shouldTreatLocalSignalAsClearQuestion(text, signal: signal)
                    ]))
                    for hasBackground in [false, true] {
                        let plan = processor.provisionalAnswerPlan(for: text, lastTopic: lastTopic, hasBackground: hasBackground)
                        emit("plan:" + name(lastTopic) + ":" + bit(hasBackground), plan.map { $0.topic + "|" + bit($0.inheritedFromPriorTurn) } ?? "nil")
                    }
                    emit("topic:" + name(lastTopic), opt(processor.provisionalAnswerTopic(for: text, lastTopic: lastTopic)))
                }
                for classified in classifiedTopics {
                    emit("corrected:" + name(classified), processor.correctedTopic(for: text, classifiedTopic: classified))
                }
                emit("display", processor.conversationalDisplayAnswer(text))
                emit("clean", processor.cleanConversationalLine(text))
                emit("strip", processor.stripInlineMarkdownForCueCard(text))
                emit("tail", opt(processor.inlineCueTail(in: text)))
                emit("marker", processor.stripCueMarker(from: text))
                emit("fragments", processor.cueFragments(from: text).joined(separator: "\u{2}"))
                emit("split", processor.splitLongCommaCueFragment(text).joined(separator: "\u{2}"))
                emit("compact", processor.compactCueLine(text))
                emit("sim", String(format: "%.6f", processor.stringSimilarity(text, texts[(index + 1) % texts.count])))

                if label == "se" {
                    let understood = ConversationUnderstanding.normalized(text)
                    emit("cu", understood + "|" + bits([
                        ConversationUnderstanding.hasForeignQuestionMarker(in: understood),
                        ConversationUnderstanding.isCandidateMetaUtterance(text),
                        ConversationUnderstanding.hasInterviewerFrame(understood),
                        ConversationUnderstanding.trailsOff(text),
                        ConversationUnderstanding.looksGarbled(text),
                        ConversationUnderstanding.looksLikeCandidateNarrative(understood),
                        ConversationUnderstanding.refersBackToConversation(text),
                        ConversationUnderstanding.followsPrevious(question: text, topic: "unknown", previousTopic: "ragCag"),
                        ConversationUnderstanding.followsPrevious(question: text, topic: "hashMap", previousTopic: "ragCag")
                    ]))
                    emit("held", opt(ConversationUnderstanding.heldQuestion(text)))
                    emit("continued", opt(ConversationUnderstanding.continuedQuestion(previous: "What is a hash map?", fragment: text, secondsSincePrevious: 2, window: 8)))
                    emit("coding", CodingRequestDetector.normalized(text) + "|" + bits([
                        CodingRequestDetector.asksHowToInLanguage(text),
                        CodingRequestDetector.isTaskRequest(text),
                        CodingRequestDetector.isDeicticRequest(text),
                        CodingRequestDetector.asksForCodeExample(text),
                        CodingRequestDetector.codeExampleNamesItsSubject(text),
                        CodingRequestDetector.startsCodingSession(text),
                        CodingRequestDetector.looksLikeTaskDetail(text)
                    ]) + "|" + (CodingRequestDetector.followUp(text).map { "\($0)" } ?? "nil"))
                    var armed = CodingSessionState()
                    armed.arm(at: now)
                    var working = CodingSessionState()
                    working.begin(task: "Write a function that reverses a string", cardTurnID: UUID(), at: now)
                    working.store(code: "STEP 1 — file: main.py\n```python\n" + samplePinned + "\n```")
                    emit("turn", [
                        describe(codingTurnDecision(text: text, session: CodingSessionState(), now: now)),
                        describe(codingTurnDecision(text: text, session: CodingSessionState(), now: now, previousQuestion: "What is polymorphism?")),
                        describe(codingTurnDecision(text: text, session: armed, now: now.addingTimeInterval(5))),
                        describe(codingTurnDecision(text: text, session: working, now: now.addingTimeInterval(5), previousQuestion: "What is polymorphism?")),
                        describe(codingTurnDecision(text: text, session: working, now: now.addingTimeInterval(700)))
                    ].joined(separator: "\u{2}"))
                    let parsed = parseCodingInterviewSteps(text)
                    emit("steps", describe(parsed) + "|" + formatCodingInterviewSteps(parsed) + "|" + codingStepsSimplicityViolations(parsed).joined(separator: "\u{2}"))
                    let progress = parseCodingInterviewStepsStreaming(text)
                    emit("stream", describe(progress.steps) + "|" + bit(progress.lastStepIsOpen))
                    let response = splitCodingResponse(text)
                    emit("response", response.sayBullets.joined(separator: "\u{2}") + "|" + response.stepsMarkdown + "|" + opt(firstTalkingPoint(text))
                         + "|" + codingCardBullets(for: response, stepCount: parsed.count, isComplete: true)
                         + "|" + codingCardBullets(for: response, stepCount: parsed.count, isComplete: false))
                    emit("trimmed", trimmedCodeBlock(text))
                }
            }

            for topic in topics {
                emit("guidance:" + topic, processor.topicSpecificAnswerGuidance(for: topic))
                for inherited in [false, true] {
                    let block = AnswerPromptBlocks.topic(topic, inherited: inherited)
                    emit("block:" + topic + ":" + bit(inherited), block.line + "|" + block.garbleNote + "|" + bit(block.allowsTopicSpecificShape))
                    emit("direct:" + topic + ":" + bit(inherited), processor.directHaikuAnswerPrompt(
                        text: "Can you explain how that works in practice?", topic: topic, topicInherited: inherited,
                        userBackground: sampleBackground, context: filledContext(), pinnedSolution: nil))
                }
            }

            let questions = texts.enumerated().filter { $0.offset % 9 == 0 && $0.element.utf16.count >= 8 && $0.element.utf16.count <= 240 }.map(\.element)
            let client = PromptClient()
            let delegate = PromptDelegate()
            processor.delegate = delegate
            processor.configure(groqClient: nil, anthropicClient: client)
            for (index, question) in questions.enumerated() {
                emit("Q", question)
                let hint = processor.questionSignal(for: question, lastTopic: nil).topicHint ?? "unknown"
                emit("direct:a", processor.directHaikuAnswerPrompt(
                    text: question, topic: hint, topicInherited: false, userBackground: "", context: ConversationContext(), pinnedSolution: nil))
                emit("direct:b", processor.directHaikuAnswerPrompt(
                    text: question, topic: hint, topicInherited: true, userBackground: sampleBackground, context: filledContext(), pinnedSolution: samplePinned))
                emit("direct:c", processor.directHaikuAnswerPrompt(
                    text: question, topic: "followUp", topicInherited: false, userBackground: sampleBackground, context: filledContext(), pinnedSolution: nil))
                emit("coding:a", codingInterviewStepsPrompt(language: AppSettings.shared.programmingLanguage.rawValue, task: question))
                emit("coding:b", codingInterviewStepsPrompt(language: "Python", task: question, spoken: true))
                emit("coding:c", codingInterviewStepsPrompt(language: "TypeScript", task: "Reverse a string", spoken: true, currentCode: samplePinned, changeRequest: question))

                if index % 4 == 0 {
                    for variant in 0..<3 {
                        delegate.userBackground = variant == 0 ? "" : sampleBackground
                        delegate.pinnedSolution = variant == 2 ? samplePinned : nil
                        delegate.conversationContext = variant == 0 ? ConversationContext() : filledContext()
                        MemoryRetrievalUseCase.context = variant == 1 ? sampleMemory : nil
                        let before = client.prompts.count
                        await processor.streamAnswerWithHaiku(question: question, topic: variant == 2 ? "ragCag" : hint, messageType: .question)
                        emit("manual:\(variant)", client.prompts.count == before + 1 ? client.prompts[before] : "MISSING")
                    }
                }
            }
        }

        try! lines.joined(separator: "\n").write(toFile: arguments[2], atomically: true, encoding: .utf8)
    }
}
