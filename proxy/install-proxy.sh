#!/usr/bin/env bash
# Install ytstream-proxy as a systemd service.
# Usage: sudo ./install-proxy.sh [username]
#   username defaults to the current SUDO_USER or $USER
set -euo pipefail

INSTALL_DIR="/opt/ytstream-proxy"
SERVICE_FILE="/etc/systemd/system/ytstream-proxy.service"
RUN_USER="${1:-${SUDO_USER:-$USER}}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo ">>> Installing ytstream-proxy"
echo "    Install dir : $INSTALL_DIR"
echo "    Run as user : $RUN_USER"

# 1. Copy proxy script
mkdir -p "$INSTALL_DIR"
cp "$SCRIPT_DIR/ytstream_proxy.py" "$INSTALL_DIR/ytstream_proxy.py"
chmod +x "$INSTALL_DIR/ytstream_proxy.py"

# 2. Write service file (substitute actual username)
sed "s/YOUR_USERNAME/$RUN_USER/" "$SCRIPT_DIR/ytstream-proxy.service" > "$SERVICE_FILE"

# 3. Enable and start
systemctl daemon-reload
systemctl enable ytstream-proxy
systemctl restart ytstream-proxy

echo ""
echo "Done! Service status:"
systemctl status ytstream-proxy --no-pager
