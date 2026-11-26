import Foundation

func esc(_ s: String) -> String {
    var out = String.UnicodeScalarView()
    for u in s.unicodeScalars {
        switch u {
        case "\\": out.append(contentsOf: "\\\\".unicodeScalars)
        case "\n": out.append(contentsOf: "\\n".unicodeScalars)
        case "\r": out.append(contentsOf: "\\r".unicodeScalars)
        case "\t": out.append(contentsOf: "\\t".unicodeScalars)
        default: out.append(u)
        }
    }
    return String(out)
}

enum Out {
    static let lock = NSLock()
    static var lines: [String] = []
    static func emit(_ key: String, _ value: String) {
        lock.lock(); lines.append(key + "\t" + esc(value)); lock.unlock()
    }
}
func emit(_ key: String, _ value: String) { Out.emit(key, value) }

final class Stub: URLProtocol {
    static let lock = NSLock()
    static var queue: [(Int, Data)] = []
    static var record = true
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        var body = request.httpBody ?? Data()
        if let stream = request.httpBodyStream {
            stream.open()
            var buffer = [UInt8](repeating: 0, count: 65536)
            while true {
                let count = stream.read(&buffer, maxLength: buffer.count)
                if count <= 0 { break }
                body.append(buffer, count: count)
            }
            stream.close()
        }
        Stub.lock.lock()
        let response = Stub.queue.count > 1 ? Stub.queue.removeFirst() : Stub.queue[0]
        let record = Stub.record
        Stub.lock.unlock()
        if record {
            var headers: [String: String] = [:]
            for (key, value) in request.allHTTPHeaderFields ?? [:] { headers[key.lowercased()] = value }
            var boundary: String?
            if let type = headers["content-type"], let range = type.range(of: "boundary=") { boundary = String(type[range.upperBound...]) }
            var headerText = ["authorization", "x-api-key", "anthropic-version", "anthropic-beta", "content-type"]
                .compactMap { key in headers[key].map { "\(key)=\($0)" } }.joined(separator: "; ")
            var text = String(decoding: body, as: UTF8.self)
            if let boundary {
                headerText = headerText.replacingOccurrences(of: boundary, with: "BOUNDARY")
                text = text.replacingOccurrences(of: boundary, with: "BOUNDARY")
            }
            emit("REQ", "\(request.httpMethod ?? "") \(request.url!.absoluteString) [\(headerText)]")
            emit(boundary != nil ? "BODYTEXT" : (body.isEmpty ? "BODYNONE" : "BODYJSON"), text)
        }
        client?.urlProtocol(self, didReceive: HTTPURLResponse(url: request.url!, statusCode: response.0, httpVersion: nil,
            headerFields: ["Content-Type": "text/event-stream"])!, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: response.1)
        client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

func stubSession() -> URLSession {
    let config = URLSessionConfiguration.ephemeral
    config.protocolClasses = [Stub.self]
    return URLSession(configuration: config)
}

func describe(_ error: Error) -> String {
    let ns = error as NSError
    if ns.domain.hasPrefix("NS") || ns.domain.hasPrefix("Swift.") { return "other" }
    return "\(ns.domain)|\(ns.code)|\(ns.localizedDescription)"
}

func outcome(_ result: Result<Void, Error>) -> String {
    switch result {
    case .success: return "success"
    case .failure(let error): return "failure " + describe(error)
    }
}

func data(_ text: String) -> Data { Data(text.utf8) }

@main struct Harness {
    static func main() async {
        let raw = try! Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
        let json = try! JSONSerialization.jsonObject(with: raw) as! [String: Any]
        let texts = json["texts"] as! [String]
        let cv = json["cv"] as! String
        let configs = json["configs"] as! [[Any]]
        let sse = json["sse"] as! [[String: String]]
        let defaultSse = json["defaultSse"] as! [String: String]
        let answerPrompts = json["answerPrompts"] as! [[String: Any]]
        let classifyVariants = json["classifyVariants"] as! [[String: Any]]
        let sequences = json["sequences"] as! [String: [[String: Any]]]
        let providers: [(String, InterviewLLMProvider)] = [("xai", .xai), ("openai", .openai), ("anthropic", .anthropic)]

        func apply(_ c: [Any]) {
            let s = AppSettings.shared
            s.role = InterviewRole(rawValue: c[0] as! String)!
            s.programmingLanguage = ProgrammingLanguage(rawValue: c[1] as! String)!
            s.frameworks = c[2] as! String
            s.listeningLanguage = SpeakingLanguage(rawValue: c[3] as! String)!
            s.responseLanguage = SpeakingLanguage(rawValue: c[4] as! String)!
            UserDefaults.standard.set(c[5] as! String, forKey: "InterviewMaster.OpenAIReasoningEffort")
            s.openAIPriority = c[6] as! Bool
        }
        func anthropic(_ provider: InterviewLLMProvider) -> AnthropicClient {
            AnthropicClient(apiKey: "test-only", provider: provider, session: stubSession())
        }
        func groqClient() -> GroqInterviewClient {
            let client = GroqInterviewClient(apiKey: "test-only")
            client.session = stubSession()
            return client
        }
        func practiceClient() -> PracticeGroqClient {
            let client = PracticeGroqClient(apiKey: "test-only")
            client.session = stubSession()
            return client
        }
        func wire(_ provider: InterviewLLMProvider) -> String { provider == .anthropic ? "anthropic" : "openai" }
        func responses(_ sequence: [String: Any], fallback: String, nullContent: String = "") -> [(Int, Data)] {
            (sequence["responses"] as! [[Any]]).map { item in
                let body = item[1] as? String
                return (item[0] as! Int, data(body == nil ? fallback : (body == "NULLCONTENT" ? nullContent : body!)))
            }
        }
        func classify(_ client: AnthropicClient, _ v: [String: Any]) async {
            let history = (v["history"] as! [[String]]).map { ["role": $0[0], "content": $0[1]] }
            let result = await client.classifyAndStreamAnswer(
                transcription: v["t"] as! String, buffer: v["buffer"] as! String, lastTopic: v["lastTopic"] as? String,
                userBackground: (v["cv"] as! Bool) ? cv : nil, multiTurnMessages: history,
                onClassification: { emit("CLS", "\($0.status)|\($0.topic ?? "nil")") }, onAnswerChunk: { emit("CHUNK", $0) })
            emit("RESULT", outcome(result))
        }
        func answerPrompt(_ groq: GroqInterviewClient, _ p: [String: Any]) -> String {
            groq.answerPrompt(topic: p["topic"] as! String, transcription: p["q"] as! String, userBackground: (p["cv"] as! Bool) ? cv : nil,
                context: p["context"] as? String, contextIsFollowUp: p["followUp"] as! Bool, topicInherited: p["inherited"] as! Bool,
                pinnedSolution: p["pinned"] as? String)
        }

        // PURE
        apply(configs[0])
        let pure = anthropic(.xai)
        let groqPure = groqClient()
        let practicePure = practiceClient()
        emit("SYSTEM_PROMPT", AnthropicClient.classificationSystemPrompt)
        for text in texts {
            emit("TEXT", text)
            emit("MARKER", "\(pure.hasTechnicalQuestionMarker(text))")
            emit("VAGUE_A", "\(pure.isVagueFollowUpPrompt(text.lowercased()))")
            emit("VAGUE_G", "\(groqPure.isVagueFollowUpPrompt(text))")
            let pairs: [(String, String?)] = [("question", "unknown"), ("statement", nil), ("filler", "unknown"), ("incomplete", "java"), ("question", "Unknown")]
            for (status, topic) in pairs {
                let c = pure.adjustedClassification(AnthropicClient.UtteranceClassification(status: status, topic: topic), for: text)
                emit("ADJ", "\(c.status)|\(c.topic ?? "nil")")
            }
        }
        for text in json["classifications"] as! [String] {
            let c = pure.parseClassification(text)
            emit("PARSE", "\(c.status)|\(c.topic ?? "nil")")
        }
        for item in json["errorBodies"] as! [[Any]] {
            emit("ERRMSG", pure.extractErrorMessage(from: item[0] as! String, statusCode: item[1] as! Int))
        }
        for text in json["analysis"] as! [String] {
            let r = pure.parseAnalysisResponse(text)
            emit("ANALYSIS", [r.topics, r.strengths, r.weaknesses, r.insights].map { $0.joined(separator: "¦") }.joined(separator: "§"))
        }
        for text in json["whisper"] as! [String] { emit("WHISPER", groqPure.cappedWhisperPrompt(text)) }
        for topic in ["solid", "rag", "CAG", "ragCag", "oop", ""] { emit("GUIDANCE", groqPure.topicSpecificAnswerGuidance(for: topic)) }
        for reply in json["judgeReplies"] as! [String] {
            if let p = practicePure.parseJudgeJSON(reply) {
                emit("JUDGE", String(format: "%.6f", p.score) + "|" + p.feedback + "|" + p.strengths.joined(separator: "¦") + "|" + p.gaps.joined(separator: "¦"))
            } else { emit("JUDGE", "nil") }
        }
        for text in [String(repeating: "a", count: 250), "  short  ", String(repeating: "б", count: 200), String(repeating: "б", count: 201)] {
            emit("CLIP", practicePure.clipForOrpheus(text))
        }

        // CONFIGS
        for (index, config) in configs.enumerated() {
            apply(config)
            emit("CONFIG", "\(index)")
            emit("TOPICS", AnthropicClient.getTopicsForSettings())
            let groq = groqClient()
            for p in answerPrompts { emit("ANSWER_PROMPT", answerPrompt(groq, p)) }
            for (name, provider) in providers {
                for v in classifyVariants {
                    Stub.queue = [(200, data(defaultSse[wire(provider)]!))]
                    emit("CALL", "classify \(name) \(v["name"] as! String)")
                    await classify(anthropic(provider), v)
                }
                let client = anthropic(provider)
                Stub.queue = [(200, data(defaultSse[wire(provider)]!))]
                emit("CALL", "streamText \(name)")
                emit("RESULT", outcome(await client.streamTextMessage(prompt: "Explain closures briefly.", maxTokens: 120) { emit("CHUNK", $0) }))
                emit("CALL", "images \(name)")
                emit("RESULT", outcome(await client.sendMessageStream(images: ["QUJD", "REVG"], prompt: "Solve this.", prefill: "## Solution\n") { emit("CHUNK", $0) }))
                emit("RESULT", outcome(await client.sendMessageStream(images: [], prompt: "No images.") { emit("CHUNK", $0) }))
                Stub.queue = [(200, data(json[provider == .anthropic ? "anthropicMessage" : "openaiMessage"] as! String))]
                emit("CALL", "sendMessage \(name)")
                do { emit("TEXT", try await client.sendMessage(prompt: "Say hi", maxTokens: 50).text) } catch { emit("ERROR", describe(error)) }
                do { emit("TEXT", try await client.summarizeConversation(conversationText: "Q: What is OOP?\nA: - Objects")) } catch { emit("ERROR", describe(error)) }
                do { emit("TEXT", try await client.summarizeConversation(conversationText: "")) } catch { emit("ERROR", describe(error)) }
                do {
                    let r = try await client.analyzeSession(transcript: "Interviewer [oop]: What is OOP?\nAnswer [oop]: - Objects")
                    emit("TEXT", [r.topics, r.strengths, r.weaknesses, r.insights].map { $0.joined(separator: "¦") }.joined(separator: "§"))
                } catch { emit("ERROR", describe(error)) }
            }
            emit("CALL", "groq")
            Stub.queue = [(200, data(json["openaiMessage"] as! String))]
            do { emit("TEXT", try await groq.generateAnswer(for: "oop", transcription: "What is polymorphism?", userBackground: cv).answer) } catch { emit("ERROR", describe(error)) }
            do { emit("TEXT", try await groq.generateFollowUpAnswer(for: "rag", transcription: "Tell me more", context: "Q: What is RAG?\nA: - RAG retrieves.", userBackground: cv).answer) } catch { emit("ERROR", describe(error)) }
            do { emit("TEXT", try await groq.generateFollowUpAnswer(for: "oop", transcription: "Go deeper", context: "").answer) } catch { emit("ERROR", describe(error)) }
            do {
                let c = try await groq.classifyUtterance("ray list?", buffer: "What is", lastTopic: "arrayList").classification
                emit("CLS", "\(c.status)|\(c.topic ?? "nil")")
                let d = try await groq.classifyUtterance("Tell me more", buffer: "", lastTopic: nil).classification
                emit("CLS", "\(d.status)|\(d.topic ?? "nil")")
            } catch { emit("ERROR", describe(error)) }
            Stub.queue = [(200, data(defaultSse["openai"]!))]
            let p = answerPrompts[3]
            emit("RESULT", outcome(await groq.streamAnswer(topic: p["topic"] as! String, transcription: p["q"] as! String, userBackground: cv,
                context: p["context"] as? String, contextIsFollowUp: false, topicInherited: true, pinnedSolution: p["pinned"] as? String) { emit("CHUNK", $0) }))
            Stub.queue = [(200, data("{\"text\":\" spoken words \"}"))]
            do { emit("TEXT", try await groq.transcribe(audioData: data("RIFF0000WAVEfmt audio-bytes")).text) } catch { emit("ERROR", describe(error)) }
            do { emit("TEXT", try await groq.transcribe(audioData: data("0000ftypM4A audio"), filename: "clip.m4a").text) } catch { emit("ERROR", describe(error)) }
        }

        // PRACTICE
        apply(configs[0])
        let practice = practiceClient()
        emit("CALL", "practice")
        Stub.queue = [(200, data(json["openaiMessage"] as! String))]
        do { emit("TEXT", try await practice.helpCueCard(question: "What is a closure?")) } catch { emit("ERROR", describe(error)) }
        for (body, rubric) in zip(json["judgeBodies"] as! [String], ["Mentions scope.\nGives an example.", "", "  ", "r", "r", "r"]) {
            Stub.queue = [(200, data(body))]
            do {
                let r = try await practice.judgeAnswer(question: "What is a closure?", answer: "A function that keeps its scope, for example a counter.", rubric: rubric)
                emit("JUDGE", String(format: "%.6f", r.score) + "|" + r.feedback + "|" + r.strengths.joined(separator: "¦") + "|" + r.gaps.joined(separator: "¦"))
            } catch { emit("ERROR", describe(error)) }
        }
        Stub.queue = [(200, data("WAVDATA"))]
        do { emit("TEXT", "\(try await practice.synthesizeSpeech(text: String(repeating: "word ", count: 80)).count)") } catch { emit("ERROR", describe(error)) }
        Stub.queue = [(200, data("{\"text\":\" hi there \"}"))]
        do { emit("TEXT", try await practice.transcribe(audioData: data("RIFF0000WAVEfmt practice"))) } catch { emit("ERROR", describe(error)) }

        // STREAM PARSING
        Stub.record = false
        for (name, provider) in [providers[0], providers[2]] {
            for item in sse {
                for transcription in ["What is polymorphism?", "Great answer", "Compare RAG and CAG", "Tell me more"] {
                    Stub.queue = [(200, data(item[wire(provider)]!))]
                    emit("CALL", "parse \(name) \(item["name"]!) \(transcription)")
                    await classify(anthropic(provider), ["t": transcription, "buffer": "", "cv": false, "history": [[String]]()])
                }
            }
        }

        // HTTP SEQUENCES
        for (name, provider) in [providers[0], providers[2]] {
            for sequence in sequences["stream"]! {
                Stub.queue = responses(sequence, fallback: defaultSse[wire(provider)]!)
                emit("CALL", "sequence stream \(name) \(sequence["name"] as! String)")
                await classify(anthropic(provider), classifyVariants[0])
                Stub.queue = responses(sequence, fallback: defaultSse[wire(provider)]!)
                emit("RESULT", outcome(await anthropic(provider).streamTextMessage(prompt: "p") { emit("CHUNK", $0) }))
                Stub.queue = responses(sequence, fallback: defaultSse[wire(provider)]!)
                emit("RESULT", outcome(await anthropic(provider).sendMessageStream(images: ["QUJD"], prompt: "p") { emit("CHUNK", $0) }))
            }
            for sequence in sequences["message"]! {
                Stub.queue = responses(sequence, fallback: json[provider == .anthropic ? "anthropicMessage" : "openaiMessage"] as! String,
                    nullContent: json[provider == .anthropic ? "nullContentAnthropic" : "nullContentOpenAI"] as! String)
                emit("CALL", "sequence message \(name) \(sequence["name"] as! String)")
                do { emit("TEXT", try await anthropic(provider).sendMessage(prompt: "p").text) } catch { emit("ERROR", describe(error)) }
            }
        }
        for sequence in sequences["groqAnswer"]! {
            emit("CALL", "sequence groqAnswer \(sequence["name"] as! String)")
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", try await groqClient().generateAnswer(for: "oop", transcription: "What is OOP?").answer) } catch { emit("ERROR", describe(error)) }
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", try await groqClient().generateFollowUpAnswer(for: "oop", transcription: "More", context: "c").answer) } catch { emit("ERROR", describe(error)) }
        }
        for sequence in sequences["groqStream"]! {
            emit("CALL", "sequence groqStream \(sequence["name"] as! String)")
            Stub.queue = responses(sequence, fallback: "")
            emit("RESULT", outcome(await groqClient().streamAnswer(topic: "oop", transcription: "What is OOP?") { emit("CHUNK", $0) }))
        }
        for sequence in sequences["groqTranscribe"]! {
            emit("CALL", "sequence groqTranscribe \(sequence["name"] as! String)")
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", try await groqClient().transcribe(audioData: data("RIFF0000WAVEfmt x")).text) } catch { emit("ERROR", describe(error)) }
        }
        for body in json["groqClassifyBodies"] as! [String] {
            for text in ["Tell me more", "What is ray list?"] {
                Stub.queue = [(200, data(body))]
                do {
                    let c = try await groqClient().classifyUtterance(text, buffer: "", lastTopic: nil).classification
                    emit("CLS", "\(c.status)|\(c.topic ?? "nil")")
                } catch { emit("ERROR", describe(error)) }
            }
        }
        for sequence in sequences["practiceChat"]! {
            emit("CALL", "sequence practice \(sequence["name"] as! String)")
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", try await practiceClient().helpCueCard(question: "q")) } catch { emit("ERROR", describe(error)) }
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", "\(try await practiceClient().synthesizeSpeech(text: "hello").count)") } catch { emit("ERROR", describe(error)) }
            Stub.queue = responses(sequence, fallback: "")
            do { emit("TEXT", try await practiceClient().transcribe(audioData: data("RIFF0000WAVEfmt x"))) } catch { emit("ERROR", describe(error)) }
        }

        try! Out.lines.joined(separator: "\n").appending("\n").write(toFile: CommandLine.arguments[2], atomically: true, encoding: .utf8)
    }
}
