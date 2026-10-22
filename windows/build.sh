#!/usr/bin/env bash
# Builds TokenMojo.exe (tray only, no taskbar window) into %LOCALAPPDATA%\Programs\TokenMojo.
# Runs from Git Bash on Windows, or cross-compiles from Linux/WSL with a target directory.
set -euo pipefail
cd "$(dirname "$0")"

if [[ $# -ge 1 ]]; then
    TARGET="$1"
elif [[ -n "${LOCALAPPDATA:-}" ]]; then
    TARGET="$(cygpath -u "$LOCALAPPDATA")/Programs/TokenMojo"
else
    echo "Usage: bash windows/build.sh <target-dir>   (LOCALAPPDATA is not set)" >&2
    exit 1
fi

command -v dotnet >/dev/null 2>&1 || { echo "The .NET 10 SDK is required: https://dotnet.microsoft.com/download" >&2; exit 1; }

dotnet test tests/TokenMojo.Core.Tests --nologo --verbosity quiet
dotnet publish src/TokenMojo --nologo --verbosity quiet \
    --configuration Release --runtime win-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    --output "$TARGET"
echo "Built $TARGET/TokenMojo.exe"
echo "Run: \"$TARGET/TokenMojo.exe\"  (quit a running TokenMojo before rebuilding)"
