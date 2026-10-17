#!/usr/bin/env bash
# Builds the release Zones.app (RELEASE=1: signed with Zones' own
# certificate, or not at all) and zips it as macos/dist/Zones-mac.zip, with
# Zones.app at its root: the asset the updater downloads. Prints the zip's
# path, then its SHA-256 as the line SHA256SUMS.txt has for it. Starts from
# a clean bundle and zip every time, so it can be run again and again, from
# any directory.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/dist/Zones.app"
ZIP="$ROOT/dist/Zones-mac.zip"

RELEASE=1 bash "$ROOT/scripts/build-app.sh"

rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "Packaged $ZIP"
echo "$(shasum -a 256 "$ZIP" | cut -d ' ' -f 1)  $(basename "$ZIP")"
