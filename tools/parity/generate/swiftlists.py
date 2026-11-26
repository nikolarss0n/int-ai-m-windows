import re, pathlib

def scan_lists(text):
    """Finds `let name[: Type] = [ ... ]` array literals; returns [(name, body, start_offset)]."""
    out = []
    for m in re.finditer(r'\blet (\w+)(?:\s*:\s*[^=\n]+)?\s*=\s*\[', text):
        i = m.end()
        depth, n, body_start = 1, len(text), m.end()
        while i < n and depth > 0:
            c = text[i]
            if c == '#' and text.startswith('#"', i):
                j = text.index('"#', i + 2)
                i = j + 2
                continue
            if c == '"':
                i += 1
                while text[i] != '"':
                    i += 2 if text[i] == '\\' else 1
                i += 1
                continue
            if c == '/' and text.startswith('//', i):
                i = text.index('\n', i)
                continue
            if c == '[':
                depth += 1
            elif c == ']':
                depth -= 1
            i += 1
        out.append((m.group(1), text[body_start:i - 1], m.start()))
    return out

def to_csharp(body, indent):
    assert '\\(' not in body and '\\u{' not in body, body[:80]
    converted = body.replace('#"', '@"').replace('"#', '"')
    lines = [l.strip() for l in converted.strip().split("\n")]
    return "\n".join((indent + l) if l else "" for l in lines)
