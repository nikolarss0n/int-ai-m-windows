"""Diffs the two harness outputs. Usage: python3 compare.py swift.txt cs.txt [max differences shown]"""
import collections
import re
import sys


def topics_in_order(match):
    return "Topics discussed: " + ", ".join(sorted(match.group(1).split(", ")))


def load(path):
    # macOS lists the topics from a Set, so their order changes from run to run; compare them sorted.
    text = re.sub(r"Topics discussed: ([^\\\t\n]*)", topics_in_order, open(path, encoding="utf-8").read())
    return [tuple(line.split("\t", 1)) for line in text.split("\n")]


swift, cs = load(sys.argv[1]), load(sys.argv[2])
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 8
print("lines", len(swift), len(cs))
by_key = collections.Counter()
context, config, shown = "", "", 0
for index in range(max(len(swift), len(cs))):
    a = swift[index] if index < len(swift) else ("NONE", "")
    b = cs[index] if index < len(cs) else ("NONE", "")
    if a[0] == "CONFIG":
        config = a[1]
    if a[0] in ("T", "Q"):
        context = a[1]
    if a == b:
        continue
    by_key[a[0].split(":")[0]] += 1
    if shown < limit:
        shown += 1
        x, y = a[1] if len(a) > 1 else "", b[1] if len(b) > 1 else ""
        at = next((k for k in range(min(len(x), len(y))) if x[k] != y[k]), min(len(x), len(y)))
        print(f"--- line {index + 1} [{config}] {a[0]} / {b[0]} for {context[:100]!r}")
        print("  swift", repr(x[max(0, at - 70):at + 110]))
        print("  cs   ", repr(y[max(0, at - 70):at + 110]))
print("differences", sum(by_key.values()), dict(by_key.most_common()))
sys.exit(1 if by_key else 0)
