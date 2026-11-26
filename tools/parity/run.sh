#!/bin/bash
# Runs every parity check: the macOS Swift sources and the C# port are fed the same inputs and
# their outputs are diffed. Needs a Mac with Xcode's swiftc, the .NET 8 SDK and python3.
#
# Usage: tools/parity/run.sh /path/to/int-ai-m [work dir]
set -euo pipefail
MAC="$(cd "$1" && pwd)"
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${2:-$(mktemp -d)}"
mkdir -p "$WORK"
WORK="$(cd "$WORK" && pwd)"
failed=0

build_cs() {
    dotnet build "$HERE/$1/cs" -nologo -v q -o "$WORK/$1-cs" > "$WORK/$1-cs-build.log" 2>&1 || { tail -20 "$WORK/$1-cs-build.log"; exit 1; }
}

# Both harnesses write a debug log to the folder they run in, so they run inside the work dir.
in_work() { (mkdir -p "$WORK/run" && cd "$WORK/run" && "$@" > /dev/null 2>&1); }

echo "== practice: question bank, scoring, scheduling, saved sessions"
"$HERE/practice/build-swift.sh" "$MAC" "$WORK/practice-swift"
build_cs practice
in_work "$WORK/practice-swift/parity_practice" write "$MAC/Resources/practice" "$WORK/practice-out-swift"
in_work dotnet "$WORK/practice-cs/InterviewMaster.Core.Tests.dll" write "$MAC/Resources/practice" "$WORK/practice-out-cs"
if cmp -s "$WORK/practice-out-swift/dump.txt" "$WORK/practice-out-cs/dump.txt"; then
    echo "identical: $(wc -l < "$WORK/practice-out-swift/dump.txt" | tr -d ' ') lines"
else
    echo "DIFFERENT: diff $WORK/practice-out-swift/dump.txt $WORK/practice-out-cs/dump.txt"; failed=1
fi
# Each side must read the files the other one saved.
(cd "$WORK/run" && "$WORK/practice-swift/parity_practice" read "$MAC/Resources/practice" "$WORK/practice-out-cs/store" 2> /dev/null | sed 's/^/swift reads C# files: /')
(cd "$WORK/run" && dotnet "$WORK/practice-cs/InterviewMaster.Core.Tests.dll" read "$MAC/Resources/practice" "$WORK/practice-out-swift/store" 2> /dev/null | sed 's/^/C# reads swift files: /')

echo "== clients: request bodies, stream parsing, errors"
python3 "$HERE/clients/scenarios.py" "$WORK/scenarios.json" > /dev/null
"$HERE/clients/build-swift.sh" "$MAC" "$WORK/clients-swift"
build_cs clients
in_work "$WORK/clients-swift/parity_clients" "$WORK/scenarios.json" "$WORK/clients-swift.txt"
in_work dotnet "$WORK/clients-cs/InterviewMaster.Core.Tests.dll" "$WORK/scenarios.json" "$WORK/clients-cs.txt"
python3 "$HERE/clients/compare.py" "$WORK/clients-swift.txt" "$WORK/clients-cs.txt" || failed=1

echo "== processor: question detection, topics, answer cleanup, prompts"
python3 "$HERE/processor/corpus.py" "$MAC" "$WORK/corpus.json"
"$HERE/processor/build-swift.sh" "$MAC" "$WORK/processor-swift"
build_cs processor
in_work "$WORK/processor-swift/parity_processor" "$WORK/corpus.json" "$WORK/processor-swift.txt"
in_work dotnet "$WORK/processor-cs/InterviewMaster.Core.Tests.dll" "$WORK/corpus.json" "$WORK/processor-cs.txt"
python3 "$HERE/processor/compare.py" "$WORK/processor-swift.txt" "$WORK/processor-cs.txt" || failed=1

echo "work dir: $WORK"
exit $failed
