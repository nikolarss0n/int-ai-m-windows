#!/bin/bash
# Compiles the macOS processor sources with the parity harness. Usage: build-swift.sh <mac repo> <build dir>
set -euo pipefail
MAC="$1"
OUT="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
# The helpers are private in the app; the harness has to call them.
sed -E 's/(^|[[:space:]])private (func|struct|let|var|static|final|class|enum) /\1\2 /g' \
    "$MAC/Application/VoiceInterviewProcessor.swift" > "$OUT/VoiceInterviewProcessor.swift"
cd "$MAC"
swiftc -O -parse-as-library -target arm64-apple-macosx14.0 \
    "$HERE/swift/main.swift" "$OUT/VoiceInterviewProcessor.swift" \
    Domain/Model/AppSettings.swift Domain/Model/Constants.swift Domain/Model/ConversationContext.swift \
    Domain/Model/InterviewMessage.swift Domain/Model/InterviewBriefing.swift \
    Infrastructure/API/JevDecisionClient.swift \
    Domain/Model/CodingInterviewSteps.swift Infrastructure/API/AnthropicClient.swift \
    Domain/Model/ConversationUnderstanding.swift Domain/Model/ClassifierStreamParser.swift Domain/Model/TopicTitle.swift Domain/Model/FeedbackNotice.swift \
    Infrastructure/Speech/GroqInterviewClient.swift \
    Infrastructure/DebugLogger.swift Presentation/Extensions/AppKitExtensions.swift \
    -framework Cocoa -o "$OUT/parity_processor"
