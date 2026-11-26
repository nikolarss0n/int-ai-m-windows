"""Writes the canned server replies both client harnesses are run against. Usage: python3 scenarios.py scenarios.json"""
import json
import pathlib
import sys

def openai_sse(chunks, noise=True):
    lines = []
    if noise:
        lines += [": keep-alive", "data: not json", 'data: {"choices":[]}', 'data: {"choices":[{"delta":{"role":"assistant","content":""}}]}']
    for c in chunks:
        lines.append("data: " + json.dumps({"choices": [{"delta": {"content": c}}]}, ensure_ascii=False))
    lines.append("data: [DONE]")
    lines.append('data: ' + json.dumps({"choices": [{"delta": {"content": "AFTER DONE"}}]}))
    return "\n\n".join(lines) + "\n\n"

def anthropic_sse(chunks, noise=True):
    lines = []
    if noise:
        lines += ["event: message_start", 'data: {"type":"message_start","message":{}}', 'data: {"type":"ping"}', "data: {broken"]
    for c in chunks:
        lines.append("event: content_block_delta")
        lines.append("data: " + json.dumps({"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": c}}, ensure_ascii=False))
    lines.append('data: {"type":"message_stop"}')
    return "\n".join(lines) + "\n"

cases = {
    "header-answer": ["STAT", "US:question|TOPIC:oop\n", "---\n", "- Use the same method, ", "each object handles it.\n- Second ", "bullet."],
    "drift": ["- Closures keep ", "their scope.\n- Use them ", "for callbacks."],
    "statement": ["STATUS:statement|TOPIC:none\n---\n"],
    "filler": ["STATUS:filler|TOPIC:none"],
    "header-only": ["STATUS:question|TOPIC:hashMap"],
    "empty": [],
    "no-separator": ["STATUS:question|TOPIC:java\n- Answer without separator.\n- More."],
    "one-chunk": ["STATUS:question|TOPIC:unknown\n---\n- One chunk answer with emoji 🙂 and кирилица."],
    "incomplete": ["STATUS:incomplete|TOPIC:unknown\n---\n"],
    "spaced-header": ["STATUS: question | TOPIC: systemDesign\n", "---\n- Spaced header."],
    "late-separator": ["STATUS:question|TOPIC:api\n", "-", "--", "\n", "- ", "Late."],
    "tiny-chunks": list("STATUS:question|TOPIC:oop\n---\n- Tiny chunks 🙂 end."),
}
sse = [{"name": n, "openai": openai_sse(c), "anthropic": anthropic_sse(c)} for n, c in cases.items()]

configs = [
    ["software_engineer", "python", "", "en", "en", "low", False],
    ["senior_qa_engineer", "typescript", "", "en", "en", "low", False],
    ["qa_automation_engineer", "java", "Selenium, TestNG", "en", "en", "low", False],
    ["senior_ai_engineer", "python", "LangChain, FastAPI", "bg", "bg", "medium", True],
    ["backend_developer", "go", "Gin, gRPC", "en", "de", "low", False],
    ["senior_frontend_developer", "javascript", "React", "en", "en", "high", False],
    ["sdet", "csharp", "Playwright", "en", "en", "low", False],
    ["ml_engineer", "rust", "", "ru", "en", "low", False],
    ["data_engineer", "kotlin", "Spark", "en", "en", "low", False],
    ["devops_engineer", "cpp", "", "en", "en", "low", False],
    ["fullstack_developer", "swift", "", "en", "es", "low", True],
    ["qa_engineer", "python", "Pytest, Selenium", "en", "en", "low", False],
]
cv = "# Jane Doe\n\nSenior QA Engineer, 8 years.\n\n## Experience\n\n### Acme Corp (2019 - 2024)\n- Built Playwright test framework for checkout, cut flaky tests by 40%.\n- Led RAG evaluation for the support chatbot with LangChain.\n\n### Globex (2016 - 2019)\n- Selenium and REST Assured API testing in Java.\n\n## Skills\nPlaywright, TypeScript, Python, AWS, Docker\n"
texts = [
    "What is a closure?", "Tell me more", "tell me more.", "Can you elaborate?", "Go deeper on that", "give me an example please",
    "What is RAG?", "Compare RAG and CAG", "retrieval augmented generation vs cache augmented generation", "storage", "brag about it",
    "Какво е хешмап?", "хеш код", "Разкажи за ООП", "оп, какво е това", "полиморфизъм", "и наследяване и капсулация",
    "object-oriented design", "Great answer", "I see", "um", "hash table collisions", "hashCode and equals",
    "So you didn't use forecasting", "okay, what about the deployment", "Let's move on", "more about that", "expand on this",
    "What else?", "continue", "Continue - please", "more_details", "tell me more about playwright", "", "RAG.", "CAG?",
    "it’s fine", "What’s the difference between a thread and a process", "Rack or CAC", "context augmented generation",
    "Thanks.  Anything   else?", "Could you elaborate ...", "ОП ", "за оп, и още", "IS IT A RAG-CAG THING", "garage door",
]
classifications = [
    "STATUS:question|TOPIC:oop", "STATUS: Question | TOPIC: hashMap", "status:statement|topic:none", "STATUS:filler",
    "TOPIC:rag-cag|STATUS:incomplete", "- just an answer", "", "STATUS:question|TOPIC:\n---\n- x", "STATUS:question|TOPIC:unknown-thing|X",
    "STATUS:question", "STATUS:|TOPIC:", "prefix STATUS:answer|TOPIC:f1Score trailing",
]
error_bodies = [
    ['{"error":{"message":"Invalid key"}}', 401], ['{"error":"bad"}', 400], ["", 503], ["plain text failure", 500],
    ["x" * 400, 502], ['{"error":{"code":1}}', 400], ["[1,2]", 400], ['{"error":{"message":"Unicode грешка 🙂"}}\n', 429],
]
analysis = [
    "TOPICS:\noop\n- systemDesign\n\nSTRENGTHS:\n- clear\nWEAKNESSES:\n\tvague on scaling\nINSIGHTS:\nGood depth.\nNeeds examples.",
    "preamble line\ntopics\n- a\nInsights: \n - b\n-c\n- ",
    "", "no sections at all\njust text",
]
whisper = [
    ", ".join("term%d" % i for i in range(300)), ", ".join("термин%d" % i for i in range(120)), "x" * 1000,
    "👩‍👩‍👧‍👦" * 60, "short, list", "no-comma-" * 120, ("é" * 447) + ", tail" * 30, "a," + "б" * 600,
]
answer_prompts = [
    {"topic": "oop", "q": "What is polymorphism?", "cv": False, "context": None, "followUp": True, "inherited": False, "pinned": None},
    {"topic": "rag", "q": "What is Rack system?", "cv": True, "context": None, "followUp": True, "inherited": False, "pinned": None},
    {"topic": "solid", "q": "Explain SOLID", "cv": True, "context": "Q: What is OOP?\nA: - Objects bundle data and behaviour.", "followUp": True, "inherited": True, "pinned": None},
    {"topic": "followUp", "q": "Tell me more", "cv": False, "context": "Q: What is RAG?\nA: - RAG retrieves context.", "followUp": False, "inherited": False, "pinned": "function add(a, b) {\n  return a + b;\n}"},
    {"topic": "unknown", "q": "So you didn't use forecasting", "cv": True, "context": "Q: Tell me about Acme.\nA: - I built the Playwright framework.", "followUp": True, "inherited": False, "pinned": None},
    {"topic": "ragCag", "q": "Rack or CAC?", "cv": False, "context": None, "followUp": True, "inherited": True, "pinned": None},
    {"topic": "", "q": "Tell me about yourself", "cv": True, "context": None, "followUp": True, "inherited": False, "pinned": None},
    {"topic": "SOLID", "q": "x" * 30, "cv": False, "context": "", "followUp": True, "inherited": False, "pinned": ""},
]
classify_variants = [
    {"name": "plain", "t": "What is polymorphism?", "buffer": "", "lastTopic": None, "cv": False, "history": []},
    {"name": "buffered", "t": "and how does it compare to CAG", "buffer": "So tell me about RAG", "lastTopic": "rag", "cv": True,
     "history": [["user", "What is RAG?"], ["assistant", "- RAG retrieves context."], ["user", "and how does it compare"]]},
    {"name": "assistant-last", "t": "Tell me about your time at Acme", "buffer": "", "lastTopic": "personal", "cv": True,
     "history": [["user", "Hello"], ["assistant", "- Hi."]]},
]
chat = lambda content: json.dumps({"choices": [{"message": {"role": "assistant", "content": content}}]}, ensure_ascii=False)
groq_classify_replies = ["question,arrayList", "STATUS: question, TOPIC: none", "incomplete,none", "question", "answer:unknown", " Question , Unknown ", ",,", ""]
# A reply with "}" before "{" is left out: the macOS parser crashes on it (the port returns no score).
judge_replies = [
    '{"score":0.82,"feedback":"Solid.","strengths":["clear"],"gaps":["no example"]}',
    '```json\n{"score": 1.7, "feedback": "Over."}\n```', 'no json here', '{"score":"high","feedback":"x"}',
    '{"score":-3,"feedback":"neg","strengths":null,"gaps":[]}', '{"score":0.5,"feedback":"x","strengths":[1]}',
]
sequences = {
    "stream": [
        {"name": "401-json", "responses": [[401, '{"error":{"message":"Invalid key"}}']]},
        {"name": "400-plain", "responses": [[400, "bad request body"]]},
        {"name": "429-string", "responses": [[429, '{"error":"slow down"}']]},
        {"name": "500-500", "responses": [[500, "boom one"], [500, '{"error":{"message":"boom two"}}']]},
        {"name": "503-then-ok", "responses": [[503, ""], [200, None]]},
        {"name": "404-empty", "responses": [[404, ""]]},
    ],
    "message": [
        {"name": "ok", "responses": [[200, None]]},
        {"name": "400", "responses": [[400, '{"error":{"message":"Bad prompt"}}']]},
        {"name": "404-plain", "responses": [[404, "nope"]]},
        {"name": "500-then-ok", "responses": [[500, "{}"], [200, None]]},
        {"name": "malformed", "responses": [[200, "{}"]]},
        {"name": "null-content", "responses": [[200, "NULLCONTENT"]]},
    ],
    "groqAnswer": [
        {"name": "ok-no-dash", "responses": [[200, chat("Polymorphism lets objects differ.")]]},
        {"name": "ok-dash", "responses": [[200, chat("  - Already a bullet.\n")]]},
        {"name": "error-body-500", "responses": [[500, '{"error":{"message":"boom"}}']]},
        {"name": "empty-choices", "responses": [[200, '{"choices":[]}']]},
        {"name": "no-choices", "responses": [[200, "{}"]]},
        {"name": "404", "responses": [[404, "missing"]]},
        {"name": "null-content-then-ok", "responses": [[200, '{"choices":[{"message":{"content":null}}]}'], [200, chat("- Recovered.")]]},
    ],
    "groqStream": [
        {"name": "ok", "responses": [[200, openai_sse(["Poly", "morphism ", "explained."])]]},
        {"name": "ok-dash", "responses": [[200, openai_sse([" - Dash first.", " More."])]]},
        {"name": "403", "responses": [[403, "forbidden"]]},
        {"name": "500-500", "responses": [[500, "x"], [502, "y"]]},
        {"name": "500-then-ok", "responses": [[500, "x"], [200, openai_sse(["- After retry."])]]},
        {"name": "empty", "responses": [[200, "data: [DONE]\n\n"]]},
    ],
    "groqTranscribe": [
        {"name": "ok", "responses": [[200, '{"text":" What is polymorphism? "}']]},
        {"name": "401", "responses": [[401, '{"error":{"message":"Invalid API Key"}}']]},
        {"name": "400-empty", "responses": [[400, ""]]},
        {"name": "500-then-ok", "responses": [[500, "x" * 400], [200, '{"text":"ok"}']]},
        {"name": "decode-then-ok", "responses": [[200, '{"nope":1}'], [200, '{"text":"second"}']]},
    ],
    "practiceChat": [
        {"name": "ok", "responses": [[200, chat(" - Hint one.\n- Hint two. ")]]},
        {"name": "null", "responses": [[200, '{"choices":[{"message":{"content":null}}]}']]},
        {"name": "none", "responses": [[200, "{}"]]},
        {"name": "500", "responses": [[500, "practice " + "z" * 300]]},
        {"name": "401-empty", "responses": [[401, ""]]},
    ],
}
data = {"sse": sse, "configs": configs, "cv": cv, "texts": texts, "classifications": classifications, "errorBodies": error_bodies,
        "analysis": analysis, "whisper": whisper, "answerPrompts": answer_prompts, "classifyVariants": classify_variants,
        "groqClassifyReplies": groq_classify_replies, "judgeReplies": judge_replies, "sequences": sequences,
        "groqClassifyBodies": [chat(reply) for reply in groq_classify_replies], "judgeBodies": [chat(reply) for reply in judge_replies],
        "openaiMessage": chat("  Hello from the model.\n"), "anthropicMessage": json.dumps({"content": [{"type": "text", "text": "  Hello from Claude.\n"}, {"type": "text", "text": "second"}]}),
        "nullContentOpenAI": '{"choices":[{"message":{"content":null}}]}', "nullContentAnthropic": '{"content":[]}',
        "analysisReply": analysis[0], "defaultSse": {"openai": openai_sse(cases["header-answer"], noise=False), "anthropic": anthropic_sse(cases["header-answer"], noise=False)}}
pathlib.Path(sys.argv[1]).write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
print("scenarios", len(sse), "sse cases,", len(texts), "texts")
