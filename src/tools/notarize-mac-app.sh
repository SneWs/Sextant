#!/bin/bash
# Sign a stamped Sextant.app with a Developer ID Application certificate,
# notarize a zip of that bundle, and staple the ticket back onto the app.
#
# build-macos maps these environment variables from repository secrets.
# When APPLE_CERTIFICATE_BASE64 is empty, the ad-hoc signature from
# stamp-mac-app.sh is left in place and this script exits 0.
#
#   APPLE_CERTIFICATE_BASE64    Developer ID Application .p12, base64
#   APPLE_CERTIFICATE_PASSWORD  .p12 password. A trailing newline is ignored.
#   APPLE_TEAM_ID               10-character Team ID
#   APPLE_API_KEY_ID            App Store Connect API Key ID
#   APPLE_API_ISSUER            App Store Connect Issuer ID
#   APPLE_API_KEY               .p8 private key contents
set -euo pipefail

app="${1:-}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
entitlements="$script_dir/entitlements.macos.plist"

blank() {
  local value="${1-}"
  [[ -z "${value//[[:space:]]/}" ]]
}

if blank "${APPLE_CERTIFICATE_BASE64-}"; then
  echo "APPLE_CERTIFICATE_BASE64 is not set. The published app stays ad-hoc signed."
  exit 0
fi

missing=()
for name in APPLE_CERTIFICATE_PASSWORD APPLE_TEAM_ID APPLE_API_KEY_ID APPLE_API_ISSUER APPLE_API_KEY; do
  if blank "${!name-}"; then
    missing+=("$name")
  fi
done
if [[ ${#missing[@]} -gt 0 ]]; then
  echo "notarize-mac-app: APPLE_CERTIFICATE_BASE64 is set, but these are not: ${missing[*]}" >&2
  exit 1
fi

if [[ ! "$APPLE_TEAM_ID" =~ ^[A-Z0-9]{10}$ ]]; then
  echo "notarize-mac-app: APPLE_TEAM_ID must be 10 letters or digits." >&2
  exit 1
fi

if [[ ! -d "$app" || ! -f "$app/Contents/MacOS/Sextant" ]]; then
  echo "notarize-mac-app: no Sextant.app at ${app:-<missing path>}" >&2
  exit 1
fi
if [[ ! -f "$entitlements" ]]; then
  echo "notarize-mac-app: missing $entitlements" >&2
  exit 1
fi
plutil -lint "$entitlements" >/dev/null

work="$(mktemp -d "${RUNNER_TEMP:-/tmp}/sextant-sign.XXXXXX")"
keychain="$work/signing.keychain-db"
p12="$work/developer-id.p12"
api_key="$work/AuthKey.p8"
zip="$work/Sextant.zip"
keychain_password="$(openssl rand -base64 32)"
previous_default=""
previous_list=""

cleanup() {
  if [[ -n "$previous_default" ]]; then
    security default-keychain -s "$previous_default" >/dev/null 2>&1 || true
  fi
  if [[ -n "$previous_list" ]]; then
    # shellcheck disable=SC2086
    security list-keychains -d user -s $previous_list >/dev/null 2>&1 || true
  fi
  if [[ -f "$keychain" ]]; then
    security delete-keychain "$keychain" >/dev/null 2>&1 || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT

printf '%s' "$APPLE_CERTIFICATE_BASE64" | tr -d '[:space:]' | base64 -D > "$p12"
if [[ ! -s "$p12" ]]; then
  echo "notarize-mac-app: APPLE_CERTIFICATE_BASE64 did not decode to a certificate." >&2
  exit 1
fi
printf '%s\n' "$APPLE_API_KEY" > "$api_key"
chmod 600 "$api_key" "$p12"
if ! grep -q "BEGIN PRIVATE KEY" "$api_key"; then
  echo "notarize-mac-app: APPLE_API_KEY is not a PEM private key (.p8 contents)." >&2
  exit 1
fi

# GitHub keeps a trailing newline when a secret is pasted or piped in with
# echo. security import then reports that the passphrase is wrong. A Keychain
# .p12 also uses RC2, and an OpenSSL 3 .p12 uses a MAC that older macOS
# import rejects with the same message even when the password is right.
password="${APPLE_CERTIFICATE_PASSWORD//$'\r'/}"
while [[ "$password" == [[:space:]]* ]]; do
  password="${password#?}"
done
while [[ "$password" == *[[:space:]] ]]; do
  password="${password%?}"
done

import_pkcs12() {
  security import "$1" -k "$keychain" -P "$2" -f pkcs12 -A -T /usr/bin/codesign
}

# Prints the PEM to stdout. Tries each openssl because a Keychain export is
# RC2, which OpenSSL 3 hides unless -legacy is set, and LibreSSL has no -legacy.
read_p12() {
  local bin="$1" src="$2" err="$3"
  if "$bin" pkcs12 -in "$src" -passin "file:$passfile" -nodes -out "$pem" 2>"$err"; then
    return 0
  fi
  local first
  first="$(cat "$err")"
  if "$bin" pkcs12 -legacy -in "$src" -passin "file:$passfile" -nodes -out "$pem" 2>"$err"; then
    return 0
  fi
  if grep -q "unknown option" "$err"; then
    printf '%s\n' "$first" > "$err"
  fi
  return 1
}

explain_p12_failure() {
  local bytes err
  bytes="$(wc -c < "$p12" | tr -d ' ')"
  echo "notarize-mac-app: the decoded certificate is ${bytes} bytes and was not imported." >&2
  if [[ -s "$work/openssl.err" ]]; then
    cat "$work/openssl.err" >&2
  fi
  if openssl x509 -inform DER -in "$p12" -noout >/dev/null 2>&1 || grep -q "BEGIN CERTIFICATE" "$p12"; then
    echo "APPLE_CERTIFICATE_BASE64 is a certificate (.cer), not a .p12. In Keychain Access, select the Developer ID Application certificate and its private key, export a .p12, and set the secret from: base64 < SigningCertificate.p12 | tr -d '\\n'" >&2
    return
  fi
  if grep -q "invalid password" "$work/openssl.err"; then
    echo "APPLE_CERTIFICATE_PASSWORD does not open that .p12. A line break or surrounding space is already ignored. Use the password from the .p12 export." >&2
    return
  fi
  echo "security import could not read this .p12, and OpenSSL could not either. Export it again from Keychain Access with the private key included." >&2
}

previous_default="$(security default-keychain | tr -d '"' | xargs)"
previous_list="$(security list-keychains -d user | tr -d '"')"
security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
passfile="$work/p12.pass"
pem="$work/identity.pem"
printf '%s' "$password" > "$passfile"
chmod 600 "$passfile"
if ! import_pkcs12 "$p12" "$password" 2>"$work/security.err"; then
  openssl_err="$work/openssl.err"
  : > "$openssl_err"
  opened=0
  while IFS= read -r bin; do
    [[ -n "$bin" && -x "$bin" ]] || continue
    if read_p12 "$bin" "$p12" "$openssl_err"; then
      opened=1
      break
    fi
  done < <(printf '%s\n' "$(command -v openssl)" /usr/bin/openssl /opt/homebrew/bin/openssl | awk 'NF && !seen[$0]++')
  if [[ "$opened" -ne 1 ]]; then
    cat "$work/security.err" >&2
    explain_p12_failure
    exit 1
  fi
  if ! grep -q "PRIVATE KEY" "$pem"; then
    echo "notarize-mac-app: the .p12 password worked, but the file has no private key. Export the certificate and its private key together." >&2
    exit 1
  fi
  echo "Rewriting the .p12 into the format macOS import accepts."
  legacy="$work/legacy.p12"
  legacy_pass="$(openssl rand -hex 16)"
  printf '%s' "$legacy_pass" > "$work/legacy.pass"
  chmod 600 "$pem" "$work/legacy.pass"
  exported=0
  while IFS= read -r bin; do
    [[ -n "$bin" && -x "$bin" ]] || continue
    if "$bin" pkcs12 -export -legacy -in "$pem" -out "$legacy" -passout "file:$work/legacy.pass" -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 2>"$work/export.err"; then
      exported=1
      break
    fi
    if "$bin" pkcs12 -export -in "$pem" -out "$legacy" -passout "file:$work/legacy.pass" -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 2>"$work/export.err"; then
      exported=1
      break
    fi
  done < <(printf '%s\n' "$(command -v openssl)" /usr/bin/openssl /opt/homebrew/bin/openssl | awk 'NF && !seen[$0]++')
  if [[ "$exported" -ne 1 ]]; then
    cat "$work/export.err" >&2
    echo "notarize-mac-app: could not rewrite the .p12 for macOS import." >&2
    exit 1
  fi
  import_pkcs12 "$legacy" "$legacy_pass"
fi
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$keychain_password" "$keychain" >/dev/null
security list-keychains -d user -s "$keychain" $previous_list >/dev/null
security default-keychain -s "$keychain"

identity="$(security find-identity -v -p codesigning "$keychain" | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -n 1)"
if [[ -z "$identity" ]]; then
  echo "notarize-mac-app: the .p12 has no Developer ID Application identity. Apple Development and Mac App Distribution certificates cannot sign this download." >&2
  exit 1
fi
if [[ "$identity" != *"($APPLE_TEAM_ID)" ]]; then
  echo "notarize-mac-app: certificate team does not match APPLE_TEAM_ID ($identity)." >&2
  exit 1
fi
echo "Signing with $identity"

# A non-deep sign treats every +x file under Contents/MacOS as nested code.
# Managed assemblies are PE images and cannot be signed, so clear that bit.
# Sign Mach-O files from the inside out, then seal the bundle. Entitlements
# stay on the app. Nested libraries do not get them.
while IFS= read -r -d '' f; do
  ft="$(file -b "$f")"
  if [[ "$ft" == *Mach-O* ]]; then
    chmod +x "$f"
  elif [[ -x "$f" ]]; then
    chmod a-x "$f"
  fi
done < <(find "$app" -type f -print0)

macho="$work/macho.txt"
while IFS= read -r -d '' f; do
  ft="$(file -b "$f")"
  if [[ "$ft" == *Mach-O* ]]; then
    printf '%s\n' "$f" >> "$macho"
  fi
done < <(find "$app" -type f -print0)

while IFS= read -r f; do
  [[ -z "$f" ]] && continue
  codesign --force --options runtime --timestamp --sign "$identity" "$f"
done < <(awk -F/ '{ printf "%d\t%s\n", NF, $0 }' "$macho" | sort -nr | cut -f2-)

codesign --force --options runtime --timestamp --entitlements "$entitlements" --sign "$identity" "$app"
codesign --verify --strict --deep "$app"

ditto -c -k --keepParent "$app" "$zip"
set +e
json="$(xcrun notarytool submit "$zip" \
  --key "$api_key" \
  --key-id "$APPLE_API_KEY_ID" \
  --issuer "$APPLE_API_ISSUER" \
  --wait \
  --output-format json)"
status=$?
set -e
printf '%s\n' "$json"
if [[ $status -ne 0 ]]; then
  id="$(printf '%s\n' "$json" | plutil -extract id raw -o - - 2>/dev/null || true)"
  if [[ -n "$id" ]]; then
    xcrun notarytool log "$id" \
      --key "$api_key" \
      --key-id "$APPLE_API_KEY_ID" \
      --issuer "$APPLE_API_ISSUER" || true
  fi
  exit "$status"
fi

xcrun stapler staple "$app"
xcrun stapler validate "$app"
echo "Developer ID signature and notarization ticket are in place."
