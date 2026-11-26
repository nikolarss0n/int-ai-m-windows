"""Fills the word lists of VoiceInterviewProcessor.Signals.cs from the macOS source.

Usage: python3 tools/parity/generate/gen_processor.py /path/to/int-ai-m

The template holds the C# logic; every `@@name#n@@` line is replaced with the n-th Swift array
literal of that name, copied verbatim, so a list is never retyped by hand.
"""
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from swiftlists import scan_lists, to_csharp  # noqa: E402

mac = pathlib.Path(sys.argv[1])
out_dir = HERE.parents[2] / "src/InterviewMaster.Core/Application/Processor"
src = (mac / "Application/VoiceInterviewProcessor.swift").read_text(encoding="utf-8")

lists, seen = {}, {}
for name, body, _ in scan_lists(src):
    seen[name] = seen.get(name, 0) + 1
    lists[f"{name}#{seen[name]}"] = body

for template in sorted(HERE.glob("VoiceInterviewProcessor.*.template.cs")):
    used = set()

    def fill(match):
        used.add(match.group(2))
        return to_csharp(lists[match.group(2)], match.group(1))

    out = re.sub(r"^([ \t]*)@@([\w#]+)@@[ \t]*$", fill, template.read_text(encoding="utf-8"), flags=re.M)
    assert "@@" not in out, template
    target = out_dir / template.name.replace(".template", "")
    target.write_text(out, encoding="utf-8")
    print(target.name, len(out.splitlines()), "lines,", len(used), "lists")
