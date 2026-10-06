#!/bin/bash
# Writes SEXTANT_VERSION for the build. A tag such as v0.1.3-beta selects the
# release workflow. The leading v is not part of the version that is imprinted.
set -euo pipefail

tag="${INPUT_VERSION:-}"
if [[ -z "$tag" && "${GITHUB_REF:-}" == refs/tags/* ]]; then
  tag="${GITHUB_REF_NAME:-}"
fi
if [[ -z "$tag" ]]; then
  tag="$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || true)"
fi

if [[ "$tag" =~ ^[vV]([0-9].*)$ ]]; then
  tag="${BASH_REMATCH[1]}"
fi

if [[ ! "$tag" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.+-]*)?$ ]]; then
  echo "Expected a version tag such as v0.1.3-beta. Got: ${tag:-none} (ref ${GITHUB_REF:-unknown})." >&2
  exit 1
fi

if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "SEXTANT_VERSION=$tag" >> "$GITHUB_ENV"
fi
echo "SEXTANT_VERSION=$tag"
