#!/usr/bin/env python3
"""Compares the Windows UI driver's states with the macOS driver's, state by state.

Usage: compare_states.py <mac-out> <windows-out> [state-number ...]

Both drivers write NN-name.txt per state: a few frames, a line of dashes, then every visible text.
The view trees differ, so the words are compared as sets and the frames after converting the Mac's
bottom-left coordinates. Exit code 1 when anything differs.
"""
import re
import sys
from pathlib import Path

# Words that are meant to differ: icon-only buttons the Mac dump names "Button", the Mac's fixed
# title on the mute button (its label lives in the tooltip), and paths in the sample notices.
IGNORED = {
    "Button",
    "Audio starts with the interview",
    "Audio is not being captured", "Listening to the interviewer", "Muted", "Connecting audio",
}
PLATFORM = [
    (re.compile(r"⌘⌥"), "Ctrl+Alt+"), (re.compile(r"⌘⇧"), "Ctrl+Shift+"), (re.compile(r"⌘↩"), "Ctrl+Enter"),
    (re.compile(r"⌘"), "Ctrl+"), (re.compile(r"Show in Finder"), "Show in Explorer"),
]


def entries(text, kinds):
    """The dump's entries; a line without a [kind] prefix carries on the one before."""
    result = []
    keep = False
    for line in text.split("\n"):
        match = re.match(r"\[(label|btn|text|icon|clipped)\] ?(.*)", line)
        if match:
            keep = match.group(1) in kinds
            if keep:
                result.append(match.group(2))
        elif result and keep:
            result[-1] += "\n" + line
    return result


def words(body, kinds=("label", "btn", "text")):
    found = set()
    for entry in entries(body, kinds):
        entry = entry.replace(" (hidden)", "")
        for part in re.split(r"⏎|\n", entry):
            part = re.sub(r"\s+", " ", part).strip()
            part = re.sub(r"^[•▸] ?", "", part)
            part = re.sub(r"(…|\.\.\.)$", "", part)
            for pattern, replacement in PLATFORM:
                part = pattern.sub(replacement, part)
            # The two drivers wait a little differently, so the timer is a second apart.
            part = re.sub(r"^\d+:\d\d$", "<elapsed>", part)
            # The Mac swaps a divider's dashes for a drawn line; the port keeps them and rules over them.
            part = re.sub(r"^─{3,}$", "---", part)
            if part and part not in IGNORED:
                found.add(part)
    return found


def frames(head, mac):
    result = {}
    for line in head.split("\n"):
        match = re.match(r"(\w+) (-?\d+),(-?\d+) (\d+)x(\d+) hidden=(\w+)", line)
        if not match:
            continue
        name, x, y, w, h = match.group(1), *map(int, match.group(2, 3, 4, 5))
        hidden = match.group(6).lower() == "true"
        if mac:
            x, y = x + 12, 500 - (12 + y + h)
        result[name] = (x, y, w, h, hidden)
    return result


def compare(mac_file, win_file):
    mac_head, _, mac_body = mac_file.read_text().partition("\n---\n")
    win_head, _, win_body = win_file.read_text().partition("\n---\n")
    problems = []
    mac_words, win_words = words(mac_body), words(win_body)
    # A wrapped line reads as two on one side and one on the other: compare the letters too.
    # Buttons that are only an icon have no words on the Mac dump, and the Mac's CV label holds the
    # points it cuts off: both may match, neither counts.
    icons = words(win_body, kinds=("icon", "clipped"))
    missing, extra = mac_words - win_words - icons, win_words - mac_words
    if missing or extra:
        joined_win, joined_mac = re.sub(r"\s+", "", " ".join(win_words)), re.sub(r"\s+", "", " ".join(mac_words))
        missing = {word for word in missing if re.sub(r"\s+", "", word) not in joined_win}
        extra = {word for word in extra if re.sub(r"\s+", "", word) not in joined_mac}
    for word in sorted(missing):
        problems.append(f"  only on Mac:     {word[:110]}")
    for word in sorted(extra):
        problems.append(f"  only on Windows: {word[:110]}")
    mac_frames, win_frames = frames(mac_head, True), frames(win_head, False)
    for name in ("burstStrip", "answerScroll", "codePanel"):
        if name not in mac_frames or name not in win_frames:
            continue
        mac_frame, win_frame = mac_frames[name], win_frames[name]
        if mac_frame[4] != win_frame[4]:
            problems.append(f"  {name}: hidden Mac={mac_frame[4]} Windows={win_frame[4]}")
        elif not mac_frame[4] and any(abs(a - b) > 1 for a, b in zip(mac_frame[:4], win_frame[:4])):
            problems.append(f"  {name}: Mac {mac_frame[:4]} Windows {win_frame[:4]}")
    return problems


def main():
    mac, win = Path(sys.argv[1]), Path(sys.argv[2])
    wanted = set(sys.argv[3:])
    failed = 0
    states = sorted(mac.glob("[0-9][0-9]-*.txt"))
    states = [state for state in states if not state.name.endswith(".style.txt")]
    for mac_file in states:
        if wanted and mac_file.name[:2] not in wanted:
            continue
        win_file = win / mac_file.name
        if not win_file.exists():
            print(f"{mac_file.stem}: no Windows state")
            failed += 1
            continue
        problems = compare(mac_file, win_file)
        if problems:
            failed += 1
            print(mac_file.stem)
            print("\n".join(problems))
    print(f"{len(states) - failed} of {len(states)} states match" if not wanted else f"{failed} differ")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
