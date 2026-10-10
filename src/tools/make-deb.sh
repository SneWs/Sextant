#!/bin/bash
# Packs a published Linux self-contained build into a .deb for Ubuntu
# and Debian. The app lives in /opt/Sextant, /usr/bin/Sextant is a symlink,
# and the desktop file and icon go where the menu systems look for them.
#
# Usage: make-deb.sh <publish-dir> <version> <output-file> [linux-x64|linux-arm64]
# The version is the imprint from resolve-version.sh, such as 0.1.3-beta.
# dpkg accepts that shape: upstream 0.1.3, revision beta.
set -euo pipefail

publish_dir="${1:?Usage: make-deb.sh <publish-dir> <version> <output-file>}"
version="${2:?Usage: make-deb.sh <publish-dir> <version> <output-file>}"
output="${3:?Usage: make-deb.sh <publish-dir> <version> <output-file>}"
runtime="${4:-linux-x64}"

case "$runtime" in
  linux-x64) architecture=amd64 ;;
  linux-arm64) architecture=arm64 ;;
  *)
    echo "Unsupported runtime: $runtime (expected linux-x64 or linux-arm64)." >&2
    exit 1
    ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ ! -x "$publish_dir/Sextant" ]]; then
  echo "No executable Sextant in $publish_dir. Publish $runtime first." >&2
  exit 1
fi

if [[ ! "$version" =~ ^[0-9] ]]; then
  echo "A dpkg Version must start with a digit. Got: $version" >&2
  exit 1
fi

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
pkg="$stage/pkg"

install -d "$pkg/DEBIAN" "$pkg/opt/Sextant" "$pkg/usr/bin" \
  "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/256x256/apps"

cp -a "$publish_dir/." "$pkg/opt/Sextant/"
ln -sf /opt/Sextant/Sextant "$pkg/usr/bin/Sextant"

# The publish folder carries both files. Fall back to the sources.
desktop="$publish_dir/sextant.desktop"
[[ -f "$desktop" ]] || desktop="$repo_root/src/sextant.desktop"
install -m 0644 "$desktop" "$pkg/usr/share/applications/sextant.desktop"

icon="$publish_dir/sextant.png"
[[ -f "$icon" ]] || icon="$repo_root/src/Assets/sextant.png"
install -m 0644 "$icon" "$pkg/usr/share/icons/hicolor/256x256/apps/sextant.png"

# .NET loads ICU for globalization, and Skia links Fontconfig.
# Both published architectures require glibc 2.38; ICU's runtime package name
# differs between Ubuntu 24.04/26.04 and Debian 13.
cat > "$pkg/DEBIAN/control" << EOF
Package: sextant
Version: $version
Section: vcs
Priority: optional
Architecture: $architecture
Maintainer: Marcus Grenängen <marcus@grenangen.se>
Homepage: https://github.com/SneWs/Sextant
Depends: libc6 (>= 2.38), libssl3 | libssl1.1, zlib1g, libstdc++6, libgcc-s1, libfontconfig1, libicu78 | libicu76 | libicu74
Description: A Git client for large repositories
 Sextant is a desktop Git client for large repositories,
 built with Avalonia.
EOF

mkdir -p "$(dirname "$output")"
rm -f "$output"
dpkg-deb --root-owner-group --build "$pkg" "$output"
echo "Built $output"
