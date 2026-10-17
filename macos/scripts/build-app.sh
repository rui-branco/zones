#!/usr/bin/env bash
# Builds Zones.app into macos/dist: a release build, the icon, Info.plist,
# and an ad-hoc signature. Starts from a clean bundle every time, so it can
# be run again and again, from any directory.
#
# The version is the repo's VERSION file, X.Y.Z, the one the Windows app and
# the release share. RELEASE=1 builds what is published: signed with Zones'
# own certificate (scripts/make-signing-cert.sh) or not at all.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Zones.app"
ICON="$ROOT/.build/icon/AppIcon.icns"
RELEASE="${RELEASE:-0}"

VERSION_FILE="$(cd "$ROOT/.." && pwd)/VERSION"
if [ ! -f "$VERSION_FILE" ]; then
    echo "ERROR: no $VERSION_FILE" >&2
    exit 1
fi
VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: $VERSION_FILE says '$VERSION', not a version X.Y.Z" >&2
    exit 1
fi

# A release is signed with Zones' own certificate or not built at all:
# checked before anything is built.
PEM_DIR="$HOME/.config/zones-signing"
RCODESIGN="${ZONES_RCODESIGN:-$HOME/.local/gesturewin-tools/rcodesign}"
if [ "$RELEASE" = "1" ]; then
    if [ ! -f "$PEM_DIR/key.pem" ] || [ ! -f "$PEM_DIR/cert.pem" ] || [ ! -x "$RCODESIGN" ]; then
        echo "ERROR: a release is signed with Zones' own certificate, which needs" >&2
        echo "       $PEM_DIR/key.pem and cert.pem (scripts/make-signing-cert.sh)" >&2
        echo "       and an executable $RCODESIGN (ZONES_RCODESIGN)." >&2
        echo "       One is missing: not signing the release any other way." >&2
        exit 1
    fi
fi

cd "$ROOT"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/Zones"

swift scripts/make-icon.swift "$ICON"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Zones"
cp "$ICON" "$APP/Contents/Resources/AppIcon.icns"

# LSUIElement: a menu bar app, no Dock icon and no app menu.
cat > "$APP/Contents/Info.plist" <<PLIST
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
	<string>$VERSION</string>
	<key>CFBundleVersion</key>
	<string>$VERSION</string>
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

# A release is signed with Zones' own certificate, by rcodesign, which needs
# no keychain and no trust in the certificate. Every release then has the
# same designated requirement, this identifier and this certificate, so macOS
# keeps the Accessibility grant across updates. Nothing else will do for a
# release: signed any other way, every Mac that updates would have to grant
# Accessibility again.
if [ "$RELEASE" = "1" ]; then
    "$RCODESIGN" sign --pem-file "$PEM_DIR/key.pem" --pem-file "$PEM_DIR/cert.pem" "$APP" >/dev/null
    codesign --verify --deep --strict "$APP"

    # Its designated requirement names this certificate, by the SHA-1 of its
    # DER: certificate root = H"<sha1>". An ad-hoc or any other signature
    # would not hold from one release to the next.
    CERT_SHA1="$(openssl x509 -in "$PEM_DIR/cert.pem" -noout -fingerprint -sha1 | sed 's/^.*=//; s/://g' | tr 'A-F' 'a-f')"
    REQUIREMENT="$(codesign -d -r- "$APP" 2>&1 || true)"
    case "$REQUIREMENT" in
        *"certificate root = H\"$CERT_SHA1\""*) ;;
        *)
            echo "ERROR: the release is not signed with Zones' own certificate ($CERT_SHA1). Its requirement:" >&2
            echo "$REQUIREMENT" >&2
            exit 1
            ;;
    esac
    echo "Signed with Zones' own certificate ($PEM_DIR)"
else
    codesign --force --deep --sign - "$APP"
fi
echo "Built $APP ($VERSION)"
