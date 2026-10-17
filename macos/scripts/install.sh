#!/usr/bin/env bash
# Builds Zones.app and installs it as ~/Applications/Zones.app, replacing the
# copy there, then opens it.
#
# The app is signed ad hoc, and an ad-hoc signature changes with every build:
# to macOS each reinstall is a different app, so it asks for Accessibility
# again. Turn Zones off and on (or remove it and add it back) in System
# Settings > Privacy & Security > Accessibility after each install.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$HOME/Applications/Zones.app"

# Quit the running copy, if any, so the new one can replace it. Only asked
# when one is running: told to quit an app it cannot find, AppleScript may
# stop to ask where it is.
if pgrep -xq Zones; then
    osascript -e 'quit app "Zones"' >/dev/null 2>&1 || true
    for _ in $(seq 1 50); do
        pgrep -xq Zones || break
        sleep 0.1
    done
fi

bash "$ROOT/scripts/build-app.sh"

mkdir -p "$HOME/Applications"
rm -rf "$DEST"
ditto "$ROOT/dist/Zones.app" "$DEST"
open "$DEST"
echo "Installed $DEST"
