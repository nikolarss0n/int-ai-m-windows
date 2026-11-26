#!/usr/bin/env bash
# Builds the Windows app into dist/InterviewMaster-win-x64: one InterviewMaster.exe with .NET inside,
# plus the Resources folder it reads. Runs on Windows, macOS or Linux with the .NET 8 SDK.
set -euo pipefail
cd "$(dirname "$0")/.."
out="dist/InterviewMaster-win-x64"
dotnet publish src/InterviewMaster.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o "$out" --nologo
cp interview-master-keys.example "$out/"
echo "Published to $out"
