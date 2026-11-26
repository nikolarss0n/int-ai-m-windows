# Builds the Windows app into dist\InterviewMaster-win-x64: one InterviewMaster.exe with .NET inside,
# plus the Resources folder it reads. Needs the .NET 8 SDK.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$out = "dist\InterviewMaster-win-x64"
dotnet publish src\InterviewMaster.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item interview-master-keys.example $out
Write-Host "Published to $out"
