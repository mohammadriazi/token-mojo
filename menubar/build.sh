#!/usr/bin/env bash
# Builds CCTokMon.app (menu bar only, no Dock icon) into ~/Applications.
set -euo pipefail
cd "$(dirname "$0")"

APP="${1:-$HOME/Applications/CCTokMon.app}"
[[ "$APP" == *.app ]] || { echo "Target must end in .app: $APP" >&2; exit 1; }

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
swiftc -O -o "$APP/Contents/MacOS/CCTokMon" CCTokMonBar.swift
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key><string>com.cctokmon.menubar</string>
    <key>CFBundleName</key><string>CCTokMon</string>
    <key>CFBundleExecutable</key><string>CCTokMon</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>0.1.0</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
</dict>
</plist>
PLIST
codesign --force --sign - "$APP"
echo "Built $APP"
echo "Run: open \"$APP\""
