#!/usr/bin/env bash
# Makes Zones' own code-signing identity, once: a self-signed certificate,
# CN "Zones Code Signing", and its RSA key, in ~/.config/zones-signing
# (key.pem, cert.pem, both mode 600). build-app.sh signs releases with it
# (RELEASE=1), so every release has the same designated requirement and
# macOS keeps a Mac's Accessibility grant for Zones across updates.
#
# Keep the two files, and keep them private: a new key is a new identity,
# and every Mac that updates would have to grant Accessibility again. Does
# nothing when they are already there. Can be run from any directory.
set -euo pipefail

DIR="$HOME/.config/zones-signing"
KEY="$DIR/key.pem"
CERT="$DIR/cert.pem"

if [ -f "$KEY" ] && [ -f "$CERT" ]; then
    echo "Already made: $DIR"
    exit 0
fi
if [ -e "$KEY" ] || [ -e "$CERT" ]; then
    echo "ERROR: only one of key.pem and cert.pem is in $DIR. Not replacing it:" >&2
    echo "       move it away first if a new identity is really wanted." >&2
    exit 1
fi

umask 077
mkdir -p "$DIR"
chmod 700 "$DIR"

# Made beside where they go, and moved into place only once both are.
CONF="$DIR/openssl.cnf.tmp"
trap 'rm -f "$CONF" "$KEY.tmp" "$CERT.tmp"' EXIT
cat > "$CONF" <<'CNF'
[req]
distinguished_name = subject
x509_extensions = codesigning
prompt = no

[subject]
CN = Zones Code Signing

[codesigning]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
subjectKeyIdentifier = hash
CNF

# Ten years.
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3650 \
    -config "$CONF" -keyout "$KEY.tmp" -out "$CERT.tmp"
chmod 600 "$KEY.tmp" "$CERT.tmp"
mv "$KEY.tmp" "$KEY"
mv "$CERT.tmp" "$CERT"

echo "Made $DIR"
openssl x509 -in "$CERT" -noout -subject -enddate -fingerprint -sha1
