#!/usr/bin/env bash
# Install ytstream-proxy as a systemd service.
# Usage: sudo ./install-proxy.sh [username] [cookies_file] [ytdlp_path]
#   username     defaults to SUDO_USER or $USER
#   cookies_file defaults to value in plugin config XML, or empty
#   ytdlp_path   defaults to value in plugin config XML, or /usr/bin/yt-dlp
set -euo pipefail

INSTALL_DIR="/opt/ytstream-proxy"
SERVICE_FILE="/etc/systemd/system/ytstream-proxy.service"
CONFIG_ENV="$INSTALL_DIR/config.env"
PLUGIN_CONFIG="/var/lib/jellyfin/plugins/configurations/Jellyfin.Plugin.YouTubeFeed.xml"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

RUN_USER="${1:-${SUDO_USER:-$USER}}"

# Try to read config values from plugin XML if not supplied as arguments
_xml_value() {
    local tag="$1"
    grep -oP "(?<=<${tag}>)[^<]+" "$PLUGIN_CONFIG" 2>/dev/null || true
}

COOKIES_FILE="${2:-$(_xml_value CookiesFilePath)}"
YTDLP_PATH="${3:-$(_xml_value YtDlpPath)}"
YTDLP_PATH="${YTDLP_PATH:-/usr/bin/yt-dlp}"

echo ">>> Installing ytstream-proxy"
echo "    Install dir  : $INSTALL_DIR"
echo "    Run as user  : $RUN_USER"
echo "    yt-dlp       : $YTDLP_PATH"
echo "    Cookies file : ${COOKIES_FILE:-<none>}"

# 1. Copy proxy script
mkdir -p "$INSTALL_DIR"
cp "$SCRIPT_DIR/ytstream_proxy.py" "$INSTALL_DIR/ytstream_proxy.py"
chmod +x "$INSTALL_DIR/ytstream_proxy.py"

# 2. Write config.env — sourced by the service unit at startup
cat > "$CONFIG_ENV" <<EOF
YTDLP_PATH=${YTDLP_PATH}
COOKIES_FILE=${COOKIES_FILE}
FFMPEG_PATH=/usr/lib/jellyfin-ffmpeg/ffmpeg
EOF

# 3. Write service file (substitute actual username)
sed "s/YOUR_USERNAME/$RUN_USER/" "$SCRIPT_DIR/ytstream-proxy.service" > "$SERVICE_FILE"

# 4. Enable and start
systemctl daemon-reload
systemctl enable ytstream-proxy
systemctl restart ytstream-proxy

echo ""
echo "Done! Service status:"
systemctl status ytstream-proxy --no-pager
