#!/bin/bash
set -e

# RemoteRelay Updater Script
# Checks for updates on GitHub and installs them over the current installation.

GITHUB_REPO="RebelliousPebble/RemoteRelay"
INSTALL_DIR_BASE="/home/$SUDO_USER/RemoteRelay" # Approximation, refined below
if [ -z "$SUDO_USER" ]; then
  echo "Error: This script must be run as root (sudo)." >&2
  exit 1
fi
USER_HOME=$(eval echo ~$SUDO_USER)
BASE_INSTALL_DIR="$USER_HOME/RemoteRelay"
SERVER_INSTALL_DIR="$BASE_INSTALL_DIR/server"
CLIENT_INSTALL_DIR="$BASE_INSTALL_DIR/client"

echo "Checking for updates..."

# Get latest release info
if ! command -v jq >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
    echo "Error: curl and jq are required. Please install them."
    exit 1
fi

LATEST_RELEASE_JSON=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases/latest")
LATEST_VERSION_TAG=$(echo "$LATEST_RELEASE_JSON" | jq -r .tag_name)
LATEST_VERSION=${LATEST_VERSION_TAG#v} # Remove 'v' prefix if present

if [ "$LATEST_VERSION_TAG" == "null" ]; then
    echo "Error: Failed to fetch latest release from GitHub."
    echo "API Response: $LATEST_RELEASE_JSON"
    exit 1
fi

# Get local version
LOCAL_VERSION="0.0.0"
if [ -f "$SERVER_INSTALL_DIR/RemoteRelay.Server" ]; then
   LOCAL_VERSION=$("$SERVER_INSTALL_DIR/RemoteRelay.Server" --version 2>/dev/null || echo "0.0.0")
elif [ -f "$CLIENT_INSTALL_DIR/RemoteRelay" ]; then
   LOCAL_VERSION=$("$CLIENT_INSTALL_DIR/RemoteRelay" --version 2>/dev/null || echo "0.0.0")
fi

# Simple version comparison
# Function to convert version string to comparable number (e.g., 1.2.3 -> 1002003)
version_to_int() {
  echo "$@" | awk -F. '{ printf("%d%03d%03d\n", $1,$2,$3); }';
}

echo "Current Version: $LOCAL_VERSION"
echo "Latest Version:  $LATEST_VERSION"

if [ $(version_to_int "$LATEST_VERSION") -le $(version_to_int "$LOCAL_VERSION") ]; then
    echo "RemoteRelay is up to date."
    exit 0
fi

echo "Update available!"
echo "Downloading installer..."

# Find asset URL for linux-arm64
ASSET_URL=$(echo "$LATEST_RELEASE_JSON" | jq -r '.assets[] | select(.name | contains("arm64") and contains(".sh")) | .browser_download_url' | head -n 1)

if [ -z "$ASSET_URL" ] || [ "$ASSET_URL" == "null" ]; then
    echo "Error: Could not find suitable installer asset (arm64 .sh) in release."
    exit 1
fi

TEMP_INSTALLER="/tmp/remoterelay_update_installer.sh"
curl -L -o "$TEMP_INSTALLER" "$ASSET_URL"
chmod +x "$TEMP_INSTALLER"

echo "Running installer..."
# Run the installer. check if we need to auto-confirm
# Since install.sh asks questions, we might want to automate it or let the user interact.
# The user asked: "install it over the top of the old install with all settings intact"
# The existing install.sh detects existing installation and asks "Update server component? [Y/n]"
# We can pipe "Y\nY\n" or similar if we want fully automated, or just let the user interact.
# But for a true "auto-update", we should probably try to automate.
# However, `install.sh` uses `read -r -p` which reads from stdin.
# We can try to assume Yes for everything if we want, or just exec it.
# Let's exec it so the user sees the installer UI. The user triggered the update script likely manually or via UI (if we hook it up later).
# The prompt says "can we add in an update script...".
# "Installs it over the top... if server/client were both installed, update both".

# To automate: we could construct the inputs based on what is installed.
# But `install.sh` defaults to Y for updates if installed.
# So pressing Enter is enough.

# Let's just run it. The user will see the prompts.
"$TEMP_INSTALLER"

# Cleanup
rm -f "$TEMP_INSTALLER"
echo "Update complete."
