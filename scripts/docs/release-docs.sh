#!/usr/bin/env bash
# Publishes the exported docs as downloads on a GitHub release (the owner,
# 2026-09-24: the pack is not kept in the repository, where every export
# added 17 MB to every clone).
#
#   scripts/docs/export-docs.sh            first: writes docs/site/*.zip
#   scripts/docs/release-docs.sh           the standing "docs" release
#   scripts/docs/release-docs.sh v0.6.0    a version's release, once published
#
# Uploads docs/site/docs-pack.zip (the Docs space as a wiki pack) and
# docs/site/docs-site.zip (the static site for tesria.com/docs), replacing
# any earlier upload of the same name. The standing "docs" release is a
# pre-release that always holds the latest export, so it never shows as the
# latest version of Tesria. Needs the GitHub CLI, signed in with write access.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

TAG="${1:-docs}"
PACK=docs/site/docs-pack.zip
SITE=docs/site/docs-site.zip
for f in "$PACK" "$SITE"; do
  [ -f "$f" ] || { echo "No $f: run scripts/docs/export-docs.sh first." >&2; exit 1; }
done

if ! gh release view "$TAG" >/dev/null 2>&1; then
  if [ "$TAG" != docs ]; then
    echo "There is no release $TAG yet. Tag it and let the release workflow publish it first." >&2
    exit 1
  fi
  gh release create docs --target main --prerelease --title "Tesria docs (latest)" --notes \
"The latest export of Tesria's docs, replaced whenever they are exported again.

- **docs-pack.zip**: the Docs space as a wiki pack. In Tesria, choose Spaces, then Import a pack, to read the docs in your own instance.
- **docs-site.zip**: the same pages as a static website, as published at tesria.com/docs.

The docs for a released version are attached to that version's release."
fi

gh release upload "$TAG" "$PACK" "$SITE" --clobber
echo "uploaded to $(gh release view "$TAG" --json url --jq .url)"
