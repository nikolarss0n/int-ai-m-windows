# Parity checks against the macOS app

These tools prove that the C# port behaves like the macOS Swift code it was ported from. Each check compiles the real macOS sources with a small Swift harness, runs the C# port through the same inputs, and diffs the two outputs line by line.

## Run

Needs a Mac with Xcode's `swiftc`, the .NET 8 SDK, `python3`, and a checkout of the macOS repo.

```bash
tools/parity/run.sh /path/to/int-ai-m
```

It exits non-zero if any check differs. A full run takes about two minutes.

## What is compared

| Check | Inputs | Compared |
| --- | --- | --- |
| `practice/` | The shipped question bank | Key ideas, scoring, scheduling, ordering, and saved files. Each side also reads the files the other one saved. |
| `clients/` | Canned server replies from `clients/scenarios.py` | Request bodies, stream parsing, classification, error text, for Groq, xAI, Anthropic, and OpenAI. |
| `processor/` | Every string literal in the macOS processor tests (about 3,200), under three role and language settings | Question detection, topic choice, filler and hallucination filters, answer cleanup, coding-request detection, and every answer and coding prompt. |

Last run (2026-11-26, macOS sources at `7d64c16`): practice 25,940 lines, clients 4,048 lines, processor 414,786 lines, no differences.

## Known differences

- **Topic order in one prompt line.** macOS builds "Topics discussed: ..." from a set, so the order changes on every launch. The port lists topics in the order they came up. `processor/compare.py` sorts that line before comparing.
- **A malformed practice score.** macOS crashes when a scoring reply has `}` before `{`. The port treats it as no score. That reply is left out of the scenarios.
- **The manual-answer prompt** is captured from the request on macOS and built by the same function the request uses in C#.

## Generators

`generate/` copies long prompts and word lists from the Swift sources into the C# files, so they are never retyped:

```bash
python3 tools/parity/generate/gen_processor.py /path/to/int-ai-m
python3 tools/parity/generate/gen_clients.py /path/to/int-ai-m
```

Edit the `*.template.cs` files here, not the generated `VoiceInterviewProcessor.Signals.cs`, `AnthropicClient.cs`, or `GroqInterviewClient.cs`.
