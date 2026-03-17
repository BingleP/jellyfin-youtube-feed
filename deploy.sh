#!/usr/bin/env bash
# Build and install the Invidious Channel plugin for Jellyfin.
# Usage: sudo ./deploy.sh
set -euo pipefail

PLUGIN_NAME="Invidious Channel"
PLUGIN_VERSION="1.0.0.0"
PLUGIN_DIR="/var/lib/jellyfin/plugins/${PLUGIN_NAME}_${PLUGIN_VERSION}"
BUILD_DIR="$(dirname "$0")/bin/Release/net9.0"

echo ">>> Building plugin..."
dotnet build "$(dirname "$0")/Jellyfin.Plugin.InvidiousChannel.csproj" \
    -c Release \
    --nologo \
    -o "$BUILD_DIR"

echo ">>> Creating plugin directory: $PLUGIN_DIR"
mkdir -p "$PLUGIN_DIR"

echo ">>> Copying DLL..."
cp "$BUILD_DIR/Jellyfin.Plugin.InvidiousChannel.dll" "$PLUGIN_DIR/"

echo ">>> Writing meta.json..."
cat > "$PLUGIN_DIR/meta.json" <<EOF
{
  "category": "General",
  "description": "Browse YouTube through your local Invidious instance",
  "guid": "4a5b6c7d-8e9f-0a1b-2c3d-4e5f6a7b8c9d",
  "name": "Invidious Channel",
  "overview": "Watch YouTube via Invidious",
  "owner": "local",
  "targetAbi": "10.11.0.0",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z)",
  "version": "1.0.0.0",
  "status": "Active",
  "autoUpdate": false
}
EOF

echo ">>> Setting permissions..."
chown -R jellyfin:jellyfin "$PLUGIN_DIR"

echo ""
echo "Done! Restart Jellyfin to load the plugin:"
echo "  sudo systemctl restart jellyfin"
echo ""
echo "Then in Jellyfin: Dashboard → Channels → Invidious"
