"""Builds the input corpus for the processor parity check from the macOS tests.

Usage: python3 corpus.py /path/to/int-ai-m corpus.json

Every string literal in the macOS processor tests becomes an input, so both ports are compared on
the phrases the app was tuned against, plus a few edge cases the tests do not name.
"""
import json
import pathlib
import re
import sys

mac = pathlib.Path(sys.argv[1])
sources = ["Tests/test_processor.swift", "Tests/test_capture_processor.swift", "Tests/test_jev.swift"]

ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "0": "\0", '"': '"', "'": "'", "\\": "\\"}


def unescape(raw):
    def one(match):
        if match.group(1):
            return chr(int(match.group(1), 16))
        return ESCAPES.get(match.group(2), match.group(2))

    return re.sub(r"\\(?:u\{([0-9A-Fa-f]+)\}|(.))", one, raw)


texts = []
for name in sources:
    path = mac / name
    if not path.exists():
        continue
    source = path.read_text(encoding="utf-8")

    def block(match):
        indent = match.group(2)
        lines = [line[len(indent):] if line.startswith(indent) else line for line in match.group(1).split("\n")]
        body = "\n".join(lines)
        if "\\(" not in body and not re.search(r"\\\s*$", body, flags=re.M):
            texts.append(unescape(body))
        return '""'

    source = re.sub(r'"""\n(.*?)\n([ \t]*)"""', block, source, flags=re.S)
    for match in re.finditer(r'"((?:[^"\\\n]|\\.)*)"', source):
        raw = match.group(1)
        if "\\(" not in raw:
            texts.append(unescape(raw))

texts += [
    "", " ", "?", "...", "What is the", "edge cases", "  What is a hash map?  ", "WHAT IS A HASH MAP",
    "what is RAG,", "Какво е хеш таблица?", "Was ist eine Hashtabelle?", "¿Qué es un mapa hash?",
    "- one\n- two\n- three\n- four\n- five\n- six\n- seven",
    "**Bold** `code` _under_ [link](https://example.com) and a very long line, with commas, that keeps going, and going, and going, until it is cut",
    "```swift\nlet x = 1\n```\nThen explain it.",
    "line one\r\nline two\r\n- bullet", "What is a cafe\u0301?", "\U0001F44D What is REST?", "Ｗhat is ＡＰＩ？",
    "what is İstanbul scaling", "STRASSE vs straße", "naïve bayes — how does it work…", "x" * 400,
]

seen, corpus = set(), []
for text in texts:
    if text not in seen and "\0" not in text and len(text) <= 1500:
        seen.add(text)
        corpus.append(text)

processor = (mac / "Application/VoiceInterviewProcessor.swift").read_text(encoding="utf-8")
topics = sorted(set(re.findall(r'"([a-z][A-Za-z0-9]{1,30})"', processor)))

pathlib.Path(sys.argv[2]).write_text(json.dumps({"texts": corpus, "topics": topics}, ensure_ascii=False), encoding="utf-8")
print(len(corpus), "texts,", len(topics), "topics")
