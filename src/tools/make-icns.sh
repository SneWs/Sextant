#!/bin/bash
# Build a .icns from the sextant mark. iconutil wants an iconset of exact sizes.
set -euo pipefail

png="$1"
out="$2"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
iconset="$tmp/sextant.iconset"
mkdir -p "$iconset" "$(dirname "$out")"

# macOS keeps the transparent margin the artwork carries, so a tile whose corners are cut
# back too far renders small inside the icon grid and the leftover canvas reads as a light
# border. Redraw the tile at the grid radius first; the mark itself is never rescaled.
# Falls back to the artwork as authored if the Swift toolchain is missing.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
normalize="$here/normalize-icon.swift"
if ! command -v swiftc >/dev/null 2>&1; then
  echo "make-icns: no swiftc, packing the artwork as authored." >&2
elif [[ ! -f "$normalize" ]]; then
  echo "make-icns: $(basename "$normalize") is missing, packing the artwork as authored." >&2
elif swiftc -O "$normalize" -o "$tmp/normalize-icon" 2>/dev/null \
  && "$tmp/normalize-icon" "$png" "$tmp/sextant-grid.png" 1024; then
  png="$tmp/sextant-grid.png"
else
  echo "make-icns: could not normalise onto the macOS icon grid, packing the artwork as authored." >&2
fi

write() {
  local size="$1"
  local name="$2"
  sips -z "$size" "$size" "$png" --out "$iconset/$name" >/dev/null
}

write 16 icon_16x16.png
write 32 icon_16x16@2x.png
write 32 icon_32x32.png
write 64 icon_32x32@2x.png
write 128 icon_128x128.png
write 256 icon_128x128@2x.png
write 256 icon_256x256.png
write 512 icon_256x256@2x.png
write 512 icon_512x512.png
write 1024 icon_512x512@2x.png

iconutil -c icns "$iconset" -o "$out"
