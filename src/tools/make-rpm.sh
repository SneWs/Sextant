#!/bin/bash
# Packs a published linux-x64 self-contained build into an .rpm for Fedora.
# Same layout as make-deb.sh: /opt/Sextant, a /usr/bin/Sextant symlink,
# the desktop file, and the icon. rpmbuild comes from the rpm package on
# Ubuntu runners and ships with Fedora.
#
# Usage: make-rpm.sh <publish-dir> <version> <output-file>
# An rpm Version cannot hold a hyphen, so 0.1.3-beta splits into
# Version 0.1.3 and Release beta.
set -euo pipefail

publish_dir="${1:?Usage: make-rpm.sh <publish-dir> <version> <output-file>}"
version="${2:?Usage: make-rpm.sh <publish-dir> <version> <output-file>}"
output="${3:?Usage: make-rpm.sh <publish-dir> <version> <output-file>}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
publish_dir="$(realpath "$publish_dir")"

if [[ ! -x "$publish_dir/Sextant" ]]; then
  echo "No executable Sextant in $publish_dir. Publish linux-x64 first." >&2
  exit 1
fi

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  echo "Expected a version such as 0.1.3 or 0.1.3-beta. Got: $version" >&2
  exit 1
fi

ver="${version%%-*}"
rel="1"
if [[ "$version" == *-* ]]; then
  rel="${version#*-}"
fi
# An rpm Release token allows letters, digits, dot, and underscore only.
rel="$(printf '%s' "$rel" | tr -c 'A-Za-z0-9._' '_')"

desktop="$publish_dir/sextant.desktop"
[[ -f "$desktop" ]] || desktop="$repo_root/src/sextant.desktop"
icon="$publish_dir/sextant.png"
[[ -f "$icon" ]] || icon="$repo_root/src/Assets/sextant.png"

top="$(mktemp -d)"
trap 'rm -rf "$top"' EXIT
mkdir -p "$top"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

cat > "$top/SPECS/sextant.spec" << EOF
Name: sextant
Version: $ver
Release: $rel
Summary: A Git client for large repositories
License: Sextant License
URL: https://github.com/SneWs/Sextant
BuildArch: x86_64
# The self-contained runtime links the system OpenSSL and zlib.
Requires: openssl-libs, zlib

%description
Sextant is a desktop Git client for large repositories, built with Avalonia.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/opt/Sextant
mkdir -p %{buildroot}/usr/bin
cp -a "$publish_dir"/. %{buildroot}/opt/Sextant/
ln -sf /opt/Sextant/Sextant %{buildroot}/usr/bin/Sextant
install -m 0644 -D "$desktop" %{buildroot}/usr/share/applications/sextant.desktop
install -m 0644 -D "$icon" %{buildroot}/usr/share/icons/hicolor/256x256/apps/sextant.png

%files
/opt/Sextant
/usr/bin/Sextant
/usr/share/applications/sextant.desktop
/usr/share/icons/hicolor/256x256/apps/sextant.png

%changelog
* $(date '+%a %b %d %Y') Marcus Grenängen <marcus@grenangen.se> - $ver-$rel
- Packaged from the CI publish.
EOF

rpmbuild --define "_topdir $top" -bb "$top/SPECS/sextant.spec" --target x86_64

built="$(find "$top/RPMS" -name 'sextant-*.rpm' -type f | head -1)"
if [[ -z "$built" ]]; then
  echo "rpmbuild produced no rpm." >&2
  exit 1
fi

mkdir -p "$(dirname "$output")"
rm -f "$output"
cp "$built" "$output"
echo "Built $output"
