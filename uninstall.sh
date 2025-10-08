#!/bin/bash

set -euo pipefail

SERVER_SERVICE_NAME="remote-relay-server.service"
SERVER_SERVICE_FILE="/etc/systemd/system/$SERVER_SERVICE_NAME"

if [ "$EUID" -eq 0 ]; then
    APP_USER="${SUDO_USER:-}"
    if [ -z "$APP_USER" ] || [ "$APP_USER" = "root" ]; then
        echo "Error: When running as root, sudo must preserve the invoking user (SUDO_USER)." >&2
        echo "Re-run using: sudo -E ./uninstall.sh" >&2
        exit 1
    fi
else
    APP_USER="$(whoami)"
fi

USER_HOME=$(eval echo ~$APP_USER)
if [ -z "$USER_HOME" ] || [ ! -d "$USER_HOME" ]; then
    echo "Error: Could not resolve home for $APP_USER." >&2
    exit 1
fi

BASE_INSTALL_DIR="$USER_HOME/RemoteRelay"
SERVER_INSTALL_DIR="$BASE_INSTALL_DIR/server"
CLIENT_INSTALL_DIR="$BASE_INSTALL_DIR/client"
LEGACY_BASE_DIR="$USER_HOME/.local/share/RemoteRelay"
AUTOSTART_DESKTOP="$USER_HOME/.config/autostart/remote-relay-client.desktop"
WAYFIRE_INI="$USER_HOME/.config/wayfire.ini"

SERVER_PRESENT=false
CLIENT_PRESENT=false
LEGACY_PRESENT=false
SERVICE_PRESENT=false

if [ -d "$SERVER_INSTALL_DIR" ] && [ -f "$SERVER_INSTALL_DIR/RemoteRelay.Server" ]; then
    SERVER_PRESENT=true
fi

if [ -d "$CLIENT_INSTALL_DIR" ] && [ -f "$CLIENT_INSTALL_DIR/RemoteRelay" ]; then
    CLIENT_PRESENT=true
fi

if [ -d "$LEGACY_BASE_DIR" ]; then
    LEGACY_PRESENT=true
fi

if [ -f "$SERVER_SERVICE_FILE" ]; then
    SERVICE_PRESENT=true
fi

if ! $SERVER_PRESENT && ! $CLIENT_PRESENT && ! $LEGACY_PRESENT && ! $SERVICE_PRESENT; then
    echo "No RemoteRelay installation artifacts found for user $APP_USER."
    exit 0
fi

echo "----------------------------------------------------"
echo "RemoteRelay Uninstaller"
echo "----------------------------------------------------"
echo "User: $APP_USER"
echo "Install root: $BASE_INSTALL_DIR"
echo

if $SERVER_PRESENT; then
    echo "- Server files located at $SERVER_INSTALL_DIR"
fi
if $CLIENT_PRESENT; then
    echo "- Client files located at $CLIENT_INSTALL_DIR"
fi
if $LEGACY_PRESENT; then
    echo "- Legacy installation detected at $LEGACY_BASE_DIR"
fi
if $SERVICE_PRESENT; then
    echo "- System service $SERVER_SERVICE_NAME present"
fi
echo

ask_yes_no() {
    local prompt="$1"
    local default="$2"
    local reply
    while true; do
        read -r -p "$prompt" reply || reply=""
        reply=${reply:-$default}
        case "${reply^^}" in
            Y|YES) return 0 ;;
            N|NO) return 1 ;;
        esac
        echo "Please answer y or n."
    done
}

REMOVE_SERVER=false
REMOVE_CLIENT=false

if $SERVER_PRESENT || $SERVICE_PRESENT; then
    if ask_yes_no "Remove the RemoteRelay server component? [Y/n] " "Y"; then
        REMOVE_SERVER=true
    fi
fi

if $CLIENT_PRESENT || [ -f "$AUTOSTART_DESKTOP" ]; then
    if ask_yes_no "Remove the RemoteRelay client component? [Y/n] " "Y"; then
        REMOVE_CLIENT=true
    fi
fi

if ! $REMOVE_SERVER && ! $REMOVE_CLIENT && ! $LEGACY_PRESENT; then
    echo "Nothing selected for removal. Exiting."
    exit 0
fi

if $REMOVE_SERVER && [ "$EUID" -ne 0 ]; then
    echo "Error: Removing the server requires sudo privileges." >&2
    echo "Re-run with: sudo -E ./uninstall.sh" >&2
    exit 1
fi

SUMMARY=()

remove_service() {
    if $REMOVE_SERVER && $SERVICE_PRESENT; then
        echo "Stopping $SERVER_SERVICE_NAME..."
        if systemctl is-active --quiet "$SERVER_SERVICE_NAME"; then
            systemctl stop "$SERVER_SERVICE_NAME"
        fi

        echo "Disabling $SERVER_SERVICE_NAME..."
        if systemctl is-enabled --quiet "$SERVER_SERVICE_NAME" 2>/dev/null; then
            systemctl disable "$SERVER_SERVICE_NAME"
        fi

        echo "Removing service file $SERVER_SERVICE_FILE"
        rm -f "$SERVER_SERVICE_FILE"
        systemctl daemon-reload
        SUMMARY+=("Removed systemd service $SERVER_SERVICE_NAME")
    fi
}

remove_server_files() {
    if $REMOVE_SERVER && $SERVER_PRESENT; then
        echo "Deleting server files at $SERVER_INSTALL_DIR"
        rm -rf "$SERVER_INSTALL_DIR"
        SUMMARY+=("Deleted server files from $SERVER_INSTALL_DIR")
    fi
}

remove_client_autostart() {
    if [ -f "$AUTOSTART_DESKTOP" ]; then
        echo "Removing desktop autostart entry $AUTOSTART_DESKTOP"
        rm -f "$AUTOSTART_DESKTOP"
        SUMMARY+=("Removed XDG autostart entry")
    fi
}

clean_wayfire_autostart() {
    local tmp="$WAYFIRE_INI.autostart.$$"
    awk '
        BEGIN { in_autostart = 0; }
        /^\s*\[autostart\]\s*$/ {
            in_autostart = 1;
            print;
            next;
        }
        /^\s*\[/ {
            in_autostart = 0;
            print;
            next;
        }
        {
            if (in_autostart && $0 ~ /^\s*remote-relay-client\s*=/) {
                next;
            }
            print;
        }
    ' "$WAYFIRE_INI" > "$tmp" && mv "$tmp" "$WAYFIRE_INI"
}

clean_wayfire_idle() {
    local tmp="$WAYFIRE_INI.idle.$$"
    awk '
        BEGIN { in_idle = 0; }
        /^\s*\[idle\]\s*$/ {
            in_idle = 1; print; next;
        }
        /^\s*\[/ {
            in_idle = 0; print; next;
        }
        {
            if (in_idle) {
                if ($0 ~ /^\s*(dpms_timeout|screensaver_timeout)\s*=\s*0\s*$/) {
                    next;
                }
            }
            print;
        }
    ' "$WAYFIRE_INI" > "$tmp" && mv "$tmp" "$WAYFIRE_INI"
}

remove_client_files() {
    if $REMOVE_CLIENT && $CLIENT_PRESENT; then
        echo "Deleting client files at $CLIENT_INSTALL_DIR"
        rm -rf "$CLIENT_INSTALL_DIR"
        SUMMARY+=("Deleted client files from $CLIENT_INSTALL_DIR")
    fi
}

remove_legacy_tree() {
    if $LEGACY_PRESENT && ask_yes_no "Remove legacy directory at $LEGACY_BASE_DIR? [Y/n] " "Y"; then
        rm -rf "$LEGACY_BASE_DIR"
        SUMMARY+=("Removed legacy directory $LEGACY_BASE_DIR")
    fi
}

remove_service
remove_server_files

if $REMOVE_CLIENT; then
    remove_client_autostart
    if [ -f "$WAYFIRE_INI" ]; then
        clean_wayfire_autostart
        if ask_yes_no "Remove Wayfire kiosk settings (screen blanking overrides)? [Y/n] " "Y"; then
            clean_wayfire_idle
            SUMMARY+=("Reverted Wayfire idle overrides")
        fi
        SUMMARY+=("Updated Wayfire autostart entries")
    fi
    remove_client_files
fi

remove_legacy_tree

if [ -d "$BASE_INSTALL_DIR" ] && [ -z "$(ls -A "$BASE_INSTALL_DIR" 2>/dev/null)" ]; then
    echo "Removing empty directory $BASE_INSTALL_DIR"
    rmdir "$BASE_INSTALL_DIR"
    SUMMARY+=("Removed empty $BASE_INSTALL_DIR")
fi

echo
echo "----------------------------------------------------"
echo "Uninstall complete"
echo "----------------------------------------------------"
for item in "${SUMMARY[@]}"; do
    echo "- $item"
done

if [ ${#SUMMARY[@]} -eq 0 ]; then
    echo "No changes were made."
fi

echo
echo "If any components remain, rerun this script with sudo as needed."

exit 0
