#!/bin/bash
# Sign a stamped Sextant.app with a Developer ID Application certificate,
# notarize a zip of that bundle, and staple the ticket back onto the app.
#
# build-macos maps these environment variables from repository secrets.
# When APPLE_CERTIFICATE_BASE64 is empty, the ad-hoc signature from
# stamp-mac-app.sh is left in place and this script exits 0.
#
#   APPLE_CERTIFICATE_BASE64    Developer ID Application .p12, base64
#   APPLE_CERTIFICATE_PASSWORD  .p12 password
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

previous_default="$(security default-keychain | tr -d '"' | xargs)"
previous_list="$(security list-keychains -d user | tr -d '"')"
security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
security import "$p12" -k "$keychain" -P "$APPLE_CERTIFICATE_PASSWORD" -f pkcs12 -A -T /usr/bin/codesign
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
