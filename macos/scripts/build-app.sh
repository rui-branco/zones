#!/usr/bin/env bash
# Builds Zones.app into macos/dist: a release build, the icon, Info.plist,
# and an ad-hoc signature. Starts from a clean bundle every time, so it can
# be run again and again, from any directory.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Zones.app"
ICON="$ROOT/.build/icon/AppIcon.icns"

cd "$ROOT"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/Zones"

swift scripts/make-icon.swift "$ICON"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Zones"
cp "$ICON" "$APP/Contents/Resources/AppIcon.icns"

# LSUIElement: a menu bar app, no Dock icon and no app menu.
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleIdentifier</key>
	<string>com.ruibranco.zones</string>
	<key>CFBundleName</key>
	<string>Zones</string>
	<key>CFBundleDisplayName</key>
	<string>Zones</string>
	<key>CFBundleExecutable</key>
	<string>Zones</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>0.1.0</string>
	<key>CFBundleVersion</key>
	<string>1</string>
	<key>CFBundleIconFile</key>
	<string>AppIcon</string>
	<key>LSUIElement</key>
	<true/>
	<key>LSMinimumSystemVersion</key>
	<string>13.0</string>
	<key>NSHumanReadableCopyright</key>
	<string>© 2026 Rui Branco</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP"
echo "Built $APP"
