"""Fills the prompts and topic lists of the two API clients from the macOS sources.

Usage: python3 tools/parity/generate/gen_clients.py /path/to/int-ai-m
"""
import pathlib
import re
import sys

MAC = pathlib.Path(sys.argv[1])
HERE = pathlib.Path(__file__).resolve().parent
WIN = HERE.parents[2] / "src/InterviewMaster.Core/Infrastructure"
src = (MAC / "Infrastructure/API/AnthropicClient.swift").read_text(encoding="utf-8")
groq = (MAC / "Infrastructure/Speech/GroqInterviewClient.swift").read_text(encoding="utf-8")

def between(start, end, text):
    i = text.index(start) + len(start)
    return text[i:text.index(end, i)]

# Anthropic system prompt: Swift literal has no indentation; C# raw string content sits at 8 spaces.
prompt = between('private static let classificationSystemPrompt = """\n', '\n"""', src)
assert prompt.count("\\") == 1 and "\\(AnswerPromptBlocks.candidateVoice)" in prompt
assert "{" not in prompt and "}" not in prompt and '"""' not in prompt
prompt = prompt.replace("\\(AnswerPromptBlocks.candidateVoice)", "{{AnswerPromptBlocks.CandidateVoice}}")
prompt_cs = "\n".join(("        " + line) if line else "" for line in prompt.split("\n"))

def one(pattern):
    m = re.search(pattern, src)
    assert m, pattern
    return m.group(1)

common, qa, ai = one(r'let common = "(.*)"'), one(r'let qaTopics = "(.*)"'), one(r'let aiTopics = "(.*)"')
langs = dict(re.findall(r'case \.(\w+):\n\s+langTopics = "(.*)"', src))
assert len(langs) == 10, langs
cs_lang = {"java": "Java", "python": "Python", "javascript": "JavaScript", "typescript": "TypeScript", "go": "Go",
           "csharp": "CSharp", "cpp": "Cpp", "rust": "Rust", "kotlin": "Kotlin", "swift": "Swift"}
lang_cases = "\n".join(f'            ProgrammingLanguage.{cs_lang[k]} => "{v}",' for k, v in langs.items() if k != "swift")
lang_cases += f'\n            _ => "{langs["swift"]}"'

def strings(block):
    return re.findall(r'"((?:[^"\\]|\\.)*)"', block)

def array_lines(start, text):
    body = between(start, "]", text)
    return "\n".join("        " + l.strip() for l in body.strip().split("\n"))

markers = array_lines("let markers = [", src)
for block in (markers, common, qa, ai):
    assert "\\" not in block

# The shared follow-up lists were copied by hand into ApiHttp.cs: prove they match both Swift files.
api_http = (WIN / "API/ApiHttp.cs").read_text(encoding="utf-8")
for name, start in (("TopicMarkers", "let topicMarkers = ["), ("ExactFollowUps", "let exactFollowUps: Set<String> = [")):
    a, g = strings(between(start, "]", src)), strings(between(start, "]", groq))
    cs_block = between(name, "};", api_http)
    assert a == g == strings(cs_block), name
for phrase in ("give me an example", "more about that", "expand on this"):
    assert phrase in src and phrase in groq and phrase in api_http

out = (HERE / "AnthropicClient.template.cs").read_text(encoding="utf-8")
out = (out.replace("@@PROMPT@@", prompt_cs).replace("@@COMMON@@", common).replace("@@QA@@", qa).replace("@@AI@@", ai)
       .replace("@@LANGS@@", lang_cases).replace("@@MARKERS@@", markers))
assert "@@" not in out
(WIN / "API/AnthropicClient.cs").write_text(out, encoding="utf-8")
print("AnthropicClient.cs", len(out.splitlines()), "lines")

# Groq classify prompt: both literals are indented 8 spaces, so the lines carry over unchanged.
classify = between('        let prompt = """\n        Classify this utterance.', '\n        """', groq)
classify = "        Classify this utterance." + classify
assert classify.count("\\(") == 2 and classify.count("\\") == 2, classify.count("\\")
assert "{" not in classify and "}" not in classify and '"""' not in classify
classify = classify.replace("\\(combinedText)", "{{combinedText}}").replace("\\(lastTopicNote)", "{{lastTopicNote}}")
for line in classify.split("\n"):
    assert line == "" or line.startswith("        "), repr(line)
out = (HERE / "GroqInterviewClient.template.cs").read_text(encoding="utf-8").replace("@@CLASSIFY_PROMPT@@", classify)
assert "@@" not in out
(WIN / "Speech").mkdir(exist_ok=True)
(WIN / "Speech/GroqInterviewClient.cs").write_text(out, encoding="utf-8")
print("GroqInterviewClient.cs", len(out.splitlines()), "lines")
