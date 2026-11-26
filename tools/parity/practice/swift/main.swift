import Foundation

let sep = "\u{1}"
func esc(_ s: String) -> String {
    s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\n", with: "\\n").replacingOccurrences(of: "\r", with: "\\r")
}
func bits(_ values: [Bool]) -> String { values.map { $0 ? "1" : "0" }.joined() }
func utf8Sorted(_ values: Set<String>) -> [String] {
    values.sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }
}

let mode = CommandLine.arguments[1]
let practiceDir = URL(fileURLWithPath: CommandLine.arguments[2])
let target = URL(fileURLWithPath: CommandLine.arguments[3])
let bank = PracticeBankLoader.loadDirectory(practiceDir)
let all = bank.packs.flatMap(\.questions)
let now = Date(timeIntervalSince1970: 1_700_000_000)

let response = PracticeRecallResponse(initial: "i", revised: "r")
let answerFull = PracticeScoredAnswer(
    questionId: "q/1", packId: "study-book", question: "What is \"RAG\"? <b>café</b>", answer: "Line one\nLine two — ünïcode 🙂",
    usedHelp: true, mark: .yellow, rawScore: 0.65, finalScore: 0.26, feedback: "ok", strengths: ["s1"], gaps: ["g1", "g2"],
    recallRating: .gotIt, nextReviewAt: now.addingTimeInterval(259_200), recallResponse: response, recallConfidence: .very,
    coverageOverrides: [PracticeCoverageOverride(ideaIndex: 1, isCovered: true)], reviewedAt: now.addingTimeInterval(5),
    cardResult: PracticeCardResult(selectedOptionID: "a", correctOptionID: "b", correctAnswer: "B", explanation: "because", topic: "T", contentVersion: 3))
let answerPlain = PracticeScoredAnswer(
    questionId: "q/2", packId: "study-book", question: "Q", answer: "A", usedHelp: false, mark: .none, rawScore: 1, finalScore: 1,
    feedback: "", strengths: [], gaps: [])
let run = PracticeRunRecord(
    id: "RUN-1", packId: "study-book", packTitle: "Study book", startedAt: now, finishedAt: now.addingTimeInterval(60),
    answers: [answerFull, answerPlain], overallScore: 0.455, helpedCount: 1, mode: .rehearse, targetDate: now.addingTimeInterval(86_400 * 14))
let run2 = PracticeRunRecord(
    id: "RUN-2", packId: "aws", packTitle: "AWS", startedAt: now, finishedAt: now.addingTimeInterval(120), answers: [], overallScore: 0, helpedCount: 0)
let firstClaude = bank.packs.first { $0.id == "claude-architect" }!.questions[0]
let state1 = PracticeSessionQuestionState(
    reference: PracticeQuestionReference(packId: "study-book", questionId: "q/1"), draftResponse: "draft", recallResponse: response,
    confidence: .unsure, hasRevealedKeyIdeas: true, coverageOverrides: [PracticeCoverageOverride(ideaIndex: 0, isCovered: false)],
    usedHelp: true, repairDraft: "rd", isRepairingGap: true, repairIdeaIndex: 0)
let state2 = PracticeSessionQuestionState(
    reference: PracticeQuestionReference(packId: firstClaude.packId, questionId: firstClaude.id),
    draftResponse: firstClaude.card!.correctOptionID, cardQuestion: firstClaude)
let undo = PracticeRatingUndoMetadata(
    question: state1.reference, questionIndex: 0, answerCountBeforeRating: 0, questionCountBeforeRating: 2,
    previousQuestionState: state1, ratedAt: now.addingTimeInterval(1), requeuedQuestionKeysBeforeRating: ["k"])
let snapshot = PracticeSessionSnapshot(
    id: "SNAP-1", packId: "study-book", packTitle: "Study book", roleId: "ai-engineer", groupIds: ["a", "b"], mode: .interview,
    questionStates: [state1, state2], currentQuestionIndex: 1, startedAt: now, updatedAt: now.addingTimeInterval(30),
    answers: [answerFull], requeuedQuestionKeys: [practiceRecallReviewKey(packId: "study-book", questionId: "q/1")],
    lastRatingUndo: undo, targetDate: now.addingTimeInterval(100))

if mode == "read" {
    let store = PracticeStore(directoryURL: target)
    print("runs", store.allRuns() == [run, run2], "snapshot", store.inProgressSession() == snapshot)
    exit(0)
}

var lines: [String] = []
lines.append("packs" + sep + bank.packs.map(\.id).joined(separator: ","))
lines.append("positions" + sep + bank.positions.map { "\($0.id):\($0.title):\($0.packId):\($0.groupIds.joined(separator: "+"))" }.joined(separator: "|"))
lines.append("groups" + sep + practiceBankGroups(from: bank).map { $0.id + ":" + $0.title }.joined(separator: "|"))
for (index, q) in all.enumerated() {
    let id = q.packId + "/" + q.id
    let ideas = practiceKeyIdeas(for: q)
    let ideas5 = practiceKeyIdeas(for: q, limit: 5)
    let previous = all[(index + all.count - 1) % all.count]
    lines.append(id + sep + "ideas" + sep + ideas.map(esc).joined(separator: "\u{2}"))
    lines.append(id + sep + "ideas5" + sep + ideas5.map(esc).joined(separator: "\u{2}"))
    lines.append(id + sep + "stem" + sep + esc(practiceAnswerStem(for: q)))
    lines.append(id + sep + "stem60" + sep + esc(practiceAnswerStem(for: q, maxLength: 60)))
    lines.append(id + sep + "norm" + sep + esc(practiceNormalizeQuestionText(q.text)))
    lines.append(id + sep + "key" + sep + practiceCurriculumOrderKey(q).map { String($0) }.joined(separator: ","))
    lines.append(id + sep + "source" + sep + esc(practiceQuestionSourceLine(q, groupTitle: practiceGroupDisplayTitle(id: q.groupId, fallback: q.topicTitle))))
    lines.append(id + sep + "coverSelf" + sep + bits(practiceCoveredKeyIdeas(answer: q.answer, keyIdeas: ideas5)))
    lines.append(id + sep + "coverText" + sep + bits(practiceCoveredKeyIdeas(answer: q.text, keyIdeas: ideas5)))
    lines.append(id + sep + "coverPrev" + sep + bits(practiceCoveredKeyIdeas(answer: previous.answer, keyIdeas: ideas5)))
    lines.append(id + sep + "heur" + sep + String(format: "%.9f", heuristicPracticeRawScore(question: q.text, answer: q.answer)) + "," + String(format: "%.9f", heuristicPracticeRawScore(question: q.text, answer: previous.text)))
    lines.append(id + sep + "tokens" + sep + utf8Sorted(tokenizePractice(q.text)).joined(separator: ","))
    lines.append(id + sep + "heading" + sep + bits(q.answer.split(separator: "\n").prefix(3).map { practiceAnswerLineIsHeading(String($0)) }))
    lines.append(id + sep + "help" + sep + esc(practiceHelpText(for: q) ?? "<nil>"))
    lines.append(id + sep + "topicKey" + sep + practiceTopicKey(for: q, groupIds: [q.groupId]) + "|" + practiceTopicKey(for: q, groupIds: ["a", "b"]))
    if index % 5 == 0 {
        let choices = practiceLearnChoices(for: q, in: all, shuffle: { $0 })
        lines.append(id + sep + "learn" + sep + choices.map { ($0.isCorrect ? "+" : "-") + esc($0.text) }.joined(separator: "\u{2}"))
    }
    if let card = q.card, index % 7 == 0 {
        let scored = card.options.map { option -> String in
            guard let result = practiceScoreCard(q, selectedOptionID: option.id) else { return "nil" }
            return "\(result.finalScore == 1 ? 1 : 0):" + esc(result.feedback) + ":" + esc(result.cardResult?.correctAnswer ?? "")
        }
        lines.append(id + sep + "card" + sep + scored.joined(separator: "\u{2}"))
    }
}
lines.append("curriculum" + sep + practiceCurriculumOrdered(all.reversed()).map { $0.packId + "/" + $0.id }.joined(separator: ","))
lines.append("balanced" + sep + balancedPracticeSelection(questions: all, count: 100, topicOf: { $0.groupId }, shuffle: { $0 }).map(\.id).joined(separator: ","))
lines.append("ordered" + sep + interviewOrderedPracticeSelection(questions: all, count: 150, topicOf: { $0.groupId }, shuffle: { $0 }).map(\.id).joined(separator: ","))
let pythonPool = practiceQuestionPool(from: bank, groupIDs: ["python-engineering"])
lines.append("orderedTopic" + sep + interviewOrderedPracticeSelection(questions: pythonPool, count: 40, topicOf: { practiceTopicKey(for: $0, groupIds: ["python-engineering"]) }, shuffle: { $0 }).map(\.id).joined(separator: ","))

var latest: [String: PracticeScoredAnswer] = [:]
var attempts: [String: [PracticeScoredAnswer]] = [:]
func record(_ q: PracticeQuestion, _ score: Double, _ rating: PracticeRecallRating, _ due: Date) -> PracticeScoredAnswer {
    PracticeScoredAnswer(questionId: q.id, packId: q.packId, question: "Q", answer: "A", usedHelp: false, mark: .none, rawScore: score,
                         finalScore: score, feedback: "", strengths: [], gaps: [], recallRating: rating, nextReviewAt: due)
}
for (index, q) in all.enumerated() {
    let key = practiceRecallReviewKey(packId: q.packId, questionId: q.id)
    switch index % 4 {
    case 1:
        let answer = record(q, 0, .again, now.addingTimeInterval(-10 - Double(index % 50)))
        latest[key] = answer; attempts[key] = [answer]
    case 2:
        let answer = record(q, 1, .gotIt, now.addingTimeInterval(259_200 + Double(index % 30)))
        latest[key] = answer; attempts[key] = [answer, answer]
    case 3:
        let answer = record(q, 0.5, .hard, now.addingTimeInterval(86_400))
        latest[key] = answer; attempts[key] = [answer]
    default: break
    }
}
for pack in bank.packs {
    let plain = practiceContrastPairs(from: pack.questions, now: now, limit: 40)
    lines.append("contrast" + sep + pack.id + sep + plain.map { "\($0.topic.id)=\($0.first.id)~\($0.second.id)" }.joined(separator: ","))
    let weighted = practiceContrastPairs(from: pack.questions, latestAnswers: latest, attemptsByQuestion: attempts, now: now, limit: 40)
    lines.append("contrastW" + sep + pack.id + sep + weighted.map { "\($0.first.id)~\($0.second.id)" }.joined(separator: ","))
}
for summary in practiceTopicMasterySummaries(questions: all, latestAnswers: latest, attemptsByQuestion: attempts, now: now) {
    lines.append("mastery" + sep + "\(summary.topic.id):\(summary.topic.title):\(summary.newCount):\(summary.learningCount):\(summary.solidCount):\(summary.dueCount)")
}
let plan = practiceTodaysPlan(questions: all, latestAnswers: latest, attemptsByQuestion: attempts, now: now)
lines.append("plan" + sep + "\(plan.dueCount):\(plan.learningCount):\(plan.newCount):\(plan.solidCount):\(plan.recommendedCount):\(plan.estimatedMinutes)")
lines.append("priority" + sep + practicePrioritizedRecallQuestions(all, latestAnswers: latest, now: now).map { $0.packId + "/" + $0.id }.joined(separator: ","))

let ratings: [PracticeRecallRating] = [.again, .hard, .gotIt]
let confidences: [PracticeRecallConfidence?] = [nil, .unsure, .mostly, .very]
let targets: [Double?] = [nil, 300, 900, 172_800, 8_640_000, -86_400]
for rating in ratings {
    for confidence in confidences {
        for count in 0...8 {
            let prior = (0..<count).map { j -> PracticeScoredAnswer in
                PracticeScoredAnswer(questionId: "p", packId: "p", question: "Q", answer: "A", usedHelp: j % 3 == 2, mark: .none, rawScore: 1,
                                     finalScore: j % 2 == 0 ? 1.0 : 0.7, feedback: "", strengths: [], gaps: [], recallRating: .gotIt, nextReviewAt: now)
            }
            let values = targets.map { target in
                String(format: "%.6f", practiceAdaptiveReviewInterval(for: rating, priorAttempts: prior, confidence: confidence, from: now, targetDate: target.map { now.addingTimeInterval($0) }))
            }
            lines.append("adaptive" + sep + "\(rating.rawValue):\(confidence?.rawValue ?? "nil"):\(count)" + sep + values.joined(separator: ","))
        }
    }
}

try! FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
try! (lines.joined(separator: "\n") + "\n").write(to: target.appendingPathComponent("dump.txt"), atomically: true, encoding: .utf8)
let store = PracticeStore(directoryURL: target.appendingPathComponent("store"))
print("write", store.append(run), store.append(run2), store.saveInProgressSession(snapshot), "lines", lines.count)
