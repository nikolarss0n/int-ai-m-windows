#!/usr/bin/env python3
"""Compares where the practice tab puts things on Windows with the macOS reference.

Usage: compare_practice_frames.py <mac-out> <windows-out> [--tolerance N] [state-number ...]

Both practice drivers write NN-name.frames.txt: `x,y WxH entry` per visible text or button, with the
origin at the top left of the tab, then the cards, scroll areas and pop-ups as `<Kind>`. Entries are
matched by their words; kinds by the nearest frame of the same kind. Exit code 1 on a difference.
"""
import re
import sys
from pathlib import Path

LINE = re.compile(r"(-?\d+),(-?\d+) (\d+)x(\d+) (.*)")


def read(path):
    entries, kinds = {}, {}
    for line in path.read_text().split("\n"):
        match = LINE.match(line)
        if not match:
            continue
        frame, name = tuple(map(int, match.group(1, 2, 3, 4))), match.group(5)
        if name.startswith("<"):
            kinds.setdefault(name, []).append(frame)
        else:
            entries.setdefault(re.sub(r"\s+", " ", name).strip(), []).append(frame)
    return entries, kinds


def far(a, b, tolerance):
    return any(abs(x - y) > tolerance for x, y in zip(a, b))


def centred(a, b, tolerance):
    """A label that fills its box on one side and sits in the middle of it on the other."""
    return all(abs((p[i] + p[i + 2] / 2) - (q[i] + q[i + 2] / 2)) <= tolerance for p, q in ((a, b),) for i in (0, 1)) \
        and b[2] <= a[2] + tolerance and b[3] <= a[3] + tolerance


def compare(mac_file, win_file, tolerance):
    (mac, mac_kinds), (win, win_kinds) = read(mac_file), read(win_file)
    buttons = {name[6:] for name in mac if name.startswith("[btn] ")}
    problems = []
    for name, frames in mac.items():
        kind, _, words = name.partition(" ")
        # A button's label sits inside it on the Mac and is the button itself on Windows.
        if kind == "[label]" and any(words == button or button.startswith(words) for button in buttons):
            continue
        # A text view is as tall as its text, whatever its scroll area shows; the scroll areas are compared.
        if kind == "[text]":
            continue
        other = win.get(name) or win.get(name.replace("[label]", "[text]", 1))
        if not other:
            problems.append(f"  not on Windows: {name[:90]}")
            continue
        for frame, theirs in zip(frames, other):
            if far(frame, theirs, tolerance) and not centred(frame, theirs, tolerance):
                problems.append(f"  {name[:70]}: Mac {frame} Windows {theirs}")
    for kind, frames in mac_kinds.items():
        theirs = list(win_kinds.get(kind, []))
        for frame in frames:
            if not theirs:
                problems.append(f"  no {kind} on Windows for Mac {frame}")
                continue
            nearest = min(theirs, key=lambda other: sum(abs(a - b) for a, b in zip(frame, other)))
            theirs.remove(nearest)
            if far(frame, nearest, tolerance):
                problems.append(f"  {kind}: Mac {frame} Windows {nearest}")
    return problems


def main():
    arguments = sys.argv[1:]
    tolerance = 2
    if "--tolerance" in arguments:
        at = arguments.index("--tolerance")
        tolerance = int(arguments[at + 1])
        del arguments[at:at + 2]
    mac, win = Path(arguments[0]), Path(arguments[1])
    wanted = set(arguments[2:])
    failed = checked = 0
    for mac_file in sorted(mac.glob("[0-9][0-9]-*.frames.txt")):
        if wanted and mac_file.name[:2] not in wanted:
            continue
        checked += 1
        win_file = win / mac_file.name
        if not win_file.exists():
            print(f"{mac_file.name}: no Windows state")
            failed += 1
            continue
        problems = compare(mac_file, win_file, tolerance)
        if problems:
            failed += 1
            print(mac_file.name.removesuffix(".frames.txt"))
            print("\n".join(problems))
    print(f"{checked - failed} of {checked} states match")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
