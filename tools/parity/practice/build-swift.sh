#!/bin/bash
# Compiles the macOS practice logic with the parity harness. Usage: build-swift.sh <mac repo> <build dir>
set -euo pipefail
MAC="$1"
OUT="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT/src"
cp "$HERE/swift/main.swift" "$OUT/src/main.swift"
cd "$MAC"
swiftc -O -target arm64-apple-macosx14.0 -D PRACTICE_STORE_TESTS \
    "$OUT/src/main.swift" Domain/Practice/PracticeLogic.swift \
    Infrastructure/Practice/PracticeBankLoader.swift Infrastructure/Practice/PracticeStore.swift \
    -o "$OUT/parity_practice"
