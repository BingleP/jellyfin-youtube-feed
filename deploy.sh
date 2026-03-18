#!/usr/bin/env bash
# Build and install the YouTube Feed plugin for Jellyfin, including the stream proxy.
# Usage: sudo ./deploy.sh
set -euo pipefail

PLUGIN_NAME="YouTube Feed"
PLUGIN_VERSION="1.0.0.0"
PLUGIN_DIR="/var/lib/jellyfin/plugins/${PLUGIN_NAME}_${PLUGIN_VERSION}"
BUILD_DIR="$(dirname "$0")/bin/Release/net9.0"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo ">>> Building plugin..."
dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.YouTubeFeed.csproj" \
    -c Release \
    --nologo \
    -o "$BUILD_DIR"

echo ">>> Creating plugin directory: $PLUGIN_DIR"
mkdir -p "$PLUGIN_DIR"

echo ">>> Copying DLL..."
cp "$BUILD_DIR/Jellyfin.Plugin.YouTubeFeed.dll" "$PLUGIN_DIR/"

echo ">>> Writing meta.json..."
cat > "$PLUGIN_DIR/meta.json" <<EOF
{
  "category": "General",
  "description": "Browse your YouTube recommended feed in Jellyfin",
  "guid": "4a5b6c7d-8e9f-0a1b-2c3d-4e5f6a7b8c9d",
  "name": "YouTube Feed",
  "overview": "Watch your YouTube recommended feed via yt-dlp",
  "owner": "local",
  "targetAbi": "10.11.0.0",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z)",
  "version": "1.0.0.0",
  "status": "Active",
  "autoUpdate": false
}
EOF

echo ">>> Setting plugin permissions..."
chown -R jellyfin:jellyfin "$PLUGIN_DIR"

echo ">>> Installing ytstream-proxy..."
bash "$SCRIPT_DIR/proxy/install-proxy.sh" "${SUDO_USER:-$USER}"

echo ""
echo "Done! Restart Jellyfin to load the plugin:"
echo "  sudo systemctl restart jellyfin"
echo ""
echo "Then in Jellyfin: Dashboard → Channels → YouTube Feed"
