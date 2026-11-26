#!/bin/bash
# Compiles the macOS API clients with the parity harness. Usage: build-swift.sh <mac repo> <build dir>
set -euo pipefail
MAC="$1"
OUT="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
# Request builders and parsers are private in the app; the harness has to call them and swap the URL session.
for file in Infrastructure/API/AnthropicClient.swift Infrastructure/Speech/GroqInterviewClient.swift Infrastructure/Practice/PracticeGroqClient.swift; do
    sed -E 's/private (static )?func /\1func /; s/private static let /static let /; s/private (let|var) session:/var session:/' "$MAC/$file" > "$OUT/$(basename "$file")"
done
cd "$MAC"
swiftc -O -parse-as-library -target arm64-apple-macosx14.0 \
    "$HERE/swift/main.swift" "$OUT/AnthropicClient.swift" "$OUT/GroqInterviewClient.swift" "$OUT/PracticeGroqClient.swift" \
    Domain/Model/AppSettings.swift Domain/Model/Constants.swift Domain/Model/ConversationContext.swift \
    Domain/Model/InterviewMessage.swift Domain/Model/InterviewBriefing.swift Domain/Model/InterviewMemory.swift \
    Domain/Model/CodingInterviewSteps.swift Domain/Model/ConversationUnderstanding.swift \
    Domain/Model/ClassifierStreamParser.swift Domain/Model/TopicTitle.swift Domain/Model/FeedbackNotice.swift \
    Domain/Practice/PracticeLogic.swift Infrastructure/API/JevDecisionClient.swift Infrastructure/DebugLogger.swift \
    -o "$OUT/parity_clients"
