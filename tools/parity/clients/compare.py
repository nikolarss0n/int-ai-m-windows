import json, sys
def load(path):
    out = []
    for line in open(path, encoding="utf-8").read().split("\n"):
        if not line: continue
        key, _, value = line.partition("\t")
        if key == "BODYJSON":
            raw = value.replace("\\\\", "\0").replace("\\n", "\n").replace("\\r", "\r").replace("\\t", "\t").replace("\0", "\\")
            value = json.dumps(json.loads(raw), sort_keys=True, ensure_ascii=False)
        out.append((key, value))
    return out
a, b = load(sys.argv[1]), load(sys.argv[2])
print("lines", len(a), len(b))
diffs = 0
context = ""
for i in range(max(len(a), len(b))):
    x = a[i] if i < len(a) else None
    y = b[i] if i < len(b) else None
    if x and x[0] in ("CALL", "CONFIG", "TEXT"): context = "%s %s" % x
    if x != y:
        diffs += 1
        if diffs <= int(sys.argv[3]) if len(sys.argv) > 3 else 6:
            print("--- line", i + 1, "after", context[:90])
            xs, ys = (x or ("", ""))[1], (y or ("", ""))[1]
            j = next((k for k in range(min(len(xs), len(ys))) if xs[k] != ys[k]), min(len(xs), len(ys)))
            print(" swift", (x or ("NONE",))[0], repr(xs[max(0, j - 80):j + 120]))
            print(" cs   ", (y or ("NONE",))[0], repr(ys[max(0, j - 80):j + 120]))
print("differences", diffs)
