#!/bin/bash
# Local self-signed code-signing identity in its own keychain. Creating and using it needs no password and shows no dialog.
# A stable signature is what lets macOS keep the Screen Recording grant across reinstalls. Sourced by install.sh and build_app.sh.
SIGN_KC="$HOME/Library/Keychains/lumivane-signing.keychain-db"
SIGN_KC_PW="lumivane-local-signing"   # protects a throwaway key that only signs local builds

signing_hash() {   # prints the SHA-1 of the identity, if present
  [ -f "$SIGN_KC" ] || return 1
  security unlock-keychain -p "$SIGN_KC_PW" "$SIGN_KC" >/dev/null 2>&1 || true
  local o line; o="$(security find-identity -p codesigning "$SIGN_KC" 2>/dev/null || true)"
  while IFS= read -r line; do
    case "$line" in *"$SIGN_NAME"*) set -- $line; echo "$2"; return 0;; esac
  done <<< "$o"
  return 1
}

ensure_signing_identity() {
  if [ -n "$(signing_hash || true)" ]; then return 0; fi
  echo "Creating local signing identity \"$SIGN_NAME\"..."
  local d; d="$(mktemp -d)"
  cat > "$d/openssl.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $SIGN_NAME
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
CNF
  /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -config "$d/openssl.cnf" -keyout "$d/key.pem" -out "$d/cert.pem" 2>/dev/null
  /usr/bin/openssl pkcs12 -export -inkey "$d/key.pem" -in "$d/cert.pem" -out "$d/id.p12" -passout pass:signing 2>/dev/null
  [ -f "$SIGN_KC" ] || security create-keychain -p "$SIGN_KC_PW" "$SIGN_KC"
  security set-keychain-settings "$SIGN_KC"                       # never auto-lock
  security unlock-keychain -p "$SIGN_KC_PW" "$SIGN_KC"
  security import "$d/id.p12" -k "$SIGN_KC" -P signing -T /usr/bin/codesign >/dev/null
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$SIGN_KC_PW" "$SIGN_KC" >/dev/null 2>&1 || true
  rm -rf "$d"
  [ -n "$(signing_hash || true)" ] || { echo "Could not create the signing identity; using ad-hoc signing."; return 1; }
}

# sign_app <path> <bundle-id>: signs with the local identity, or ad-hoc if that fails
sign_app() {
  local h; h="$(signing_hash || true)"
  if [ -n "$h" ] && codesign --force --deep --sign "$h" --keychain "$SIGN_KC" --identifier "$2" "$1" 2>/dev/null; then
    echo "Signed with: $SIGN_NAME"
  else
    codesign --force --deep --sign - --identifier "$2" "$1"
    echo "Signed with: ad-hoc"
  fi
}
