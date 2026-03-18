#!/usr/bin/env bash
# Build a release zip, update manifest.json, create a GitHub release, and push.
# Usage: ./release.sh <version> [changelog]
#   version   e.g. 1.0.1
#   changelog optional one-line description (defaults to "Release <version>")
set -euo pipefail

VERSION="${1:?Usage: ./release.sh <version> [changelog]}"
CHANGELOG="${2:-Release $VERSION}"
ZIP_NAME="jellyfin-youtube-feed_${VERSION}.0.zip"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/bin/Release/net9.0"
TMP_DIR="$(mktemp -d)"

echo ">>> Building plugin..."
dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.YouTubeFeed.csproj" -c Release --nologo -o "$BUILD_DIR"

echo ">>> Creating release zip: $ZIP_NAME"
cp "$BUILD_DIR/Jellyfin.Plugin.YouTubeFeed.dll" "$TMP_DIR/"
python3 -c "
import zipfile, os, sys
src = sys.argv[1]; out = sys.argv[2]
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.write(os.path.join(src, 'Jellyfin.Plugin.YouTubeFeed.dll'), 'Jellyfin.Plugin.YouTubeFeed.dll')
print('done')
" "$TMP_DIR" "$SCRIPT_DIR/$ZIP_NAME"

CHECKSUM="$(md5sum "$SCRIPT_DIR/$ZIP_NAME" | awk '{print $1}')"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z)"
SOURCE_URL="https://github.com/BingleP/jellyfin-youtube-feed/releases/download/v${VERSION}/${ZIP_NAME}"

echo ">>> Checksum: $CHECKSUM"

echo ">>> Updating manifest.json..."
python3 - "$SCRIPT_DIR/manifest.json" "$VERSION" "$CHANGELOG" "$CHECKSUM" "$SOURCE_URL" "$TIMESTAMP" <<'PYEOF'
import json, sys

manifest_path, version, changelog, checksum, source_url, timestamp = sys.argv[1:]

with open(manifest_path) as f:
    manifest = json.load(f)

new_entry = {
    "changelog": changelog,
    "checksum": checksum,
    "sourceUrl": source_url,
    "targetAbi": "10.11.0.0",
    "timestamp": timestamp,
    "version": f"{version}.0"
}

# Prepend so newest version is first
versions = manifest[0]["versions"]
versions.insert(0, new_entry)

with open(manifest_path, "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")

print(f"manifest.json updated — {len(versions)} version(s) listed")
PYEOF

echo ">>> Committing manifest.json..."
git add manifest.json
git commit -m "Release v${VERSION}

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
git push origin master

echo ">>> Creating GitHub release v${VERSION}..."
gh release create "v${VERSION}" "$SCRIPT_DIR/$ZIP_NAME" \
  --title "v${VERSION}" \
  --notes "$(cat <<EOF
## YouTube Feed Plugin v${VERSION}

${CHANGELOG}

**Repository URL for Jellyfin:**
\`https://raw.githubusercontent.com/BingleP/jellyfin-youtube-feed/master/manifest.json\`

See the [README](https://github.com/BingleP/jellyfin-youtube-feed#installation) for full setup instructions.
EOF
)"

echo ""
echo "Done! Release v${VERSION} is live."
echo "Plugin zip: $ZIP_NAME (checksum: $CHECKSUM)"
rm -rf "$TMP_DIR"
