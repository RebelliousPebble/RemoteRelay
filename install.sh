#!/bin/bash

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)

if [ "$EUID" -ne 0 ]; then
  echo "Error: This installer must be run as root (sudo)." >&2
  exit 1
fi

APP_USER="${SUDO_USER:-}"
if [ -z "$APP_USER" ] || [ "$APP_USER" = "root" ]; then
  echo "Error: Unable to determine the non-root user running sudo." >&2
  echo "Re-run using: sudo -E ./install.sh" >&2
  exit 1
fi

USER_HOME=$(eval echo ~$APP_USER)
if [ -z "$USER_HOME" ] || [ ! -d "$USER_HOME" ]; then
  echo "Error: Could not resolve home directory for $APP_USER." >&2
  exit 1
fi

BASE_INSTALL_DIR="$USER_HOME/RemoteRelay"
SERVER_INSTALL_DIR="$BASE_INSTALL_DIR/server"
CLIENT_INSTALL_DIR="$BASE_INSTALL_DIR/client"
UNINSTALL_SCRIPT_SOURCE="uninstall.sh"
UNINSTALL_SCRIPT_DEST="$USER_HOME/RemoteRelay/uninstall.sh"
SERVER_FILES_SOURCE_DIR="server_files"
CLIENT_FILES_SOURCE_DIR="client_files"
LEGACY_BASE_DIR="$USER_HOME/.local/share/RemoteRelay"
SERVER_SERVICE_NAME="remote-relay-server.service"
SERVER_SERVICE_FILE="/etc/systemd/system/$SERVER_SERVICE_NAME"

SUMMARY=()

bold() { printf "\033[1m%s\033[0m" "$1"; }

prompt_yes_no() {
  local prompt="$1"
  local default="$2"
  local answer
  while true; do
    read -r -p "$prompt" answer || answer=""
    answer=${answer:-$default}
    case "${answer^^}" in
      Y|YES) return 0 ;;
      N|NO) return 1 ;;
    esac
    echo "Please answer yes or no (y/n)."
  done
}

ensure_dir_owned_by_user() {
  local dir="$1"
  mkdir -p "$dir"
  chown -R "$APP_USER:$APP_USER" "$dir"
}

preserve_file_if_exists() {
  local file_path="$1"
  local tmp_var="$2"
  if [ -f "$file_path" ]; then
    local tmp_backup
    tmp_backup=$(mktemp)
    cp "$file_path" "$tmp_backup"
    printf -v "$tmp_var" '%s' "$tmp_backup"
  else
    printf -v "$tmp_var" '%s' ""
  fi
}

restore_preserved_file() {
  local backup_path="$1"
  local dest_path="$2"
  if [ -n "$backup_path" ] && [ -f "$backup_path" ]; then
    mv "$backup_path" "$dest_path"
  fi
}

stop_server_if_running() {
  if systemctl is-active --quiet "$SERVER_SERVICE_NAME"; then
    echo "Stopping existing $SERVER_SERVICE_NAME..."
    systemctl stop "$SERVER_SERVICE_NAME"
    return 0
  fi
  return 1
}

start_server_service() {
  echo "Starting $SERVER_SERVICE_NAME..."
  systemctl start "$SERVER_SERVICE_NAME" || true
  if systemctl is-active --quiet "$SERVER_SERVICE_NAME"; then
    echo "Server service is running."
  else
    echo "Warning: Server service failed to start. Check with 'journalctl -u $SERVER_SERVICE_NAME'."
  fi
}

install_systemd_service_for_server() {
  local inactive_pin="$1"
  local inactive_state="$2"

  cat > "$SERVER_SERVICE_FILE" <<EOF
[Unit]
Description=RemoteRelay Server
After=network.target

[Service]
User=$APP_USER
Group=$APP_USER
WorkingDirectory=$SERVER_INSTALL_DIR
ExecStart=$SERVER_INSTALL_DIR/RemoteRelay.Server
Restart=always
RestartSec=5
EOF

  if [ -n "$inactive_pin" ] && [ -n "$inactive_state" ]; then
    echo "ExecStartPre=$SERVER_INSTALL_DIR/RemoteRelay.Server set-inactive-relay --pin $inactive_pin --state $inactive_state" >> "$SERVER_SERVICE_FILE"
    echo "ExecStopPost=$SERVER_INSTALL_DIR/RemoteRelay.Server set-inactive-relay --pin $inactive_pin --state $inactive_state" >> "$SERVER_SERVICE_FILE"
  fi

  cat >> "$SERVER_SERVICE_FILE" <<'EOF'

[Install]
WantedBy=multi-user.target
EOF

  chmod 644 "$SERVER_SERVICE_FILE"
  systemctl daemon-reload
  systemctl enable "$SERVER_SERVICE_NAME"
}

configure_wayfire_idle() {
  local wayfire_ini="$1"
  local tmp="$wayfire_ini.tmp.$$"

  awk '
    BEGIN {
      in_idle = 0; seen_idle = 0; wrote_dpms = 0; wrote_screen = 0;
    }
    /^\s*\[idle\]\s*$/ {
      if (in_idle) {
        if (!wrote_dpms) print "dpms_timeout = 0";
        if (!wrote_screen) print "screensaver_timeout = 0";
      }
      print; in_idle = 1; seen_idle = 1; wrote_dpms = 0; wrote_screen = 0; next;
    }
    /^\s*\[/ {
      if (in_idle) {
        if (!wrote_dpms) print "dpms_timeout = 0";
        if (!wrote_screen) print "screensaver_timeout = 0";
      }
      in_idle = 0;
      print; next;
    }
    {
      if (in_idle) {
        if ($0 ~ /^\s*dpms_timeout\s*=/) {
          print "dpms_timeout = 0"; wrote_dpms = 1; next;
        }
        if ($0 ~ /^\s*screensaver_timeout\s*=/) {
          print "screensaver_timeout = 0"; wrote_screen = 1; next;
        }
      }
      print;
    }
    END {
      if (in_idle) {
        if (!wrote_dpms) print "dpms_timeout = 0";
        if (!wrote_screen) print "screensaver_timeout = 0";
      } else if (!seen_idle) {
        print "[idle]";
        print "dpms_timeout = 0";
        print "screensaver_timeout = 0";
      }
    }
  ' "$wayfire_ini" > "$tmp" && mv "$tmp" "$wayfire_ini"
}

ensure_wayfire_autostart() {
  local wayfire_ini="$1"
  local command_line="$2"
  local tmp="$wayfire_ini.autostart.$$"

  awk -v entry="$command_line" '
    BEGIN { in_autostart = 0; seen_autostart = 0; wrote_entry = 0; key = "remote-relay-client"; }
    /^\s*\[autostart\]\s*$/ {
      if (in_autostart && !wrote_entry) print key " = " entry;
      print; in_autostart = 1; seen_autostart = 1; wrote_entry = 0; next;
    }
    /^\s*\[/ {
      if (in_autostart && !wrote_entry) print key " = " entry;
      in_autostart = 0;
      print; next;
    }
    {
      if (in_autostart) {
        if ($0 ~ "^\\s*" key "\\s*=") {
          if (!wrote_entry) print key " = " entry;
          wrote_entry = 1; next;
        }
      }
      print;
    }
    END {
      if (in_autostart && !wrote_entry) {
        print key " = " entry;
      } else if (!seen_autostart) {
        print "[autostart]";
        print key " = " entry;
      }
    }
  ' "$wayfire_ini" > "$tmp" && mv "$tmp" "$wayfire_ini"
}

configure_wayfire() {
  local client_exec="$1"
  local disable_idle="${2:-false}"
  local wayfire_ini="$USER_HOME/.config/wayfire.ini"
  ensure_dir_owned_by_user "$USER_HOME/.config"
  if [ ! -f "$wayfire_ini" ]; then
    touch "$wayfire_ini"
    chown "$APP_USER:$APP_USER" "$wayfire_ini"
  fi

  ensure_wayfire_autostart "$wayfire_ini" "$client_exec"
  if [ "$disable_idle" = "true" ]; then
    configure_wayfire_idle "$wayfire_ini"
  fi
  chown "$APP_USER:$APP_USER" "$wayfire_ini"
}

create_autostart_desktop() {
  local client_exec="$1"
  local enable_kiosk="$2"
  local desktop_path="$USER_HOME/.config/autostart/remote-relay-client.desktop"
  ensure_dir_owned_by_user "$USER_HOME/.config/autostart"

  local exec_line
  if [ "$enable_kiosk" = "true" ]; then
    local escaped_exec
    escaped_exec=$(printf '%s' "$client_exec" | sed 's/"/\\"/g')
    exec_line="sh -c 'xset s noblank && xset s off && xset -dpms && \"$escaped_exec\"'"
  else
    exec_line="\"$client_exec\""
  fi

  cat > "$desktop_path" <<EOF
[Desktop Entry]
Type=Application
Name=RemoteRelay Client
Exec=$exec_line
Path=$CLIENT_INSTALL_DIR
Terminal=false
X-GNOME-Autostart-enabled=true
EOF
  chmod 755 "$desktop_path"
  chown "$APP_USER:$APP_USER" "$desktop_path"
}

migrate_legacy_layout() {
  if [ ! -d "$LEGACY_BASE_DIR" ]; then
    return
  fi

  echo "Legacy installation detected at $LEGACY_BASE_DIR. Migrating to $BASE_INSTALL_DIR..."
  ensure_dir_owned_by_user "$BASE_INSTALL_DIR"

  if [ -d "$LEGACY_BASE_DIR/Server" ]; then
    ensure_dir_owned_by_user "$SERVER_INSTALL_DIR"
    cp -a "$LEGACY_BASE_DIR/Server/." "$SERVER_INSTALL_DIR/"
  fi

  if [ -d "$LEGACY_BASE_DIR/Client" ]; then
    ensure_dir_owned_by_user "$CLIENT_INSTALL_DIR"
    cp -a "$LEGACY_BASE_DIR/Client/." "$CLIENT_INSTALL_DIR/"
  fi

  chown -R "$APP_USER:$APP_USER" "$BASE_INSTALL_DIR"

  local legacy_backup="$LEGACY_BASE_DIR-backup-$(date +%s)"
  mv "$LEGACY_BASE_DIR" "$legacy_backup"
  echo "Legacy files copied. Original kept at $legacy_backup"
  SUMMARY+=("Migrated legacy installation to $BASE_INSTALL_DIR")
}

update_server_files() {
  local server_restarted=0
  if [ ! -d "$SERVER_FILES_SOURCE_DIR" ]; then
    echo "Error: server payload missing." >&2
    exit 1
  fi

  ensure_dir_owned_by_user "$SERVER_INSTALL_DIR"

  local backup_config backup_appsettings backup_devsettings
  preserve_file_if_exists "$SERVER_INSTALL_DIR/config.json" backup_config
  preserve_file_if_exists "$SERVER_INSTALL_DIR/appsettings.json" backup_appsettings
  preserve_file_if_exists "$SERVER_INSTALL_DIR/appsettings.Development.json" backup_devsettings

  if stop_server_if_running; then
    server_restarted=1
  fi

  find "$SERVER_INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -a "$SERVER_FILES_SOURCE_DIR/." "$SERVER_INSTALL_DIR/"
  chmod +x "$SERVER_INSTALL_DIR/RemoteRelay.Server"

  restore_preserved_file "$backup_config" "$SERVER_INSTALL_DIR/config.json"
  restore_preserved_file "$backup_appsettings" "$SERVER_INSTALL_DIR/appsettings.json"
  restore_preserved_file "$backup_devsettings" "$SERVER_INSTALL_DIR/appsettings.Development.json"

  chown -R "$APP_USER:$APP_USER" "$SERVER_INSTALL_DIR"

  local inactive_pin="" inactive_state=""
  if command -v jq >/dev/null 2>&1; then
    if [ -f "$SERVER_INSTALL_DIR/config.json" ]; then
      inactive_pin=$(jq -r '.InactiveRelay.Pin // empty' "$SERVER_INSTALL_DIR/config.json") || inactive_pin=""
      inactive_state=$(jq -r '.InactiveRelay.InactiveState // empty' "$SERVER_INSTALL_DIR/config.json") || inactive_state=""
      if [ -n "$inactive_state" ]; then
        inactive_state="$(echo "$inactive_state" | awk '{print toupper(substr($0,1,1)) tolower(substr($0,2))}')"
      fi
      if ! [[ "$inactive_pin" =~ ^[0-9]+$ ]]; then
        inactive_pin=""; inactive_state=""
      fi
      case "$inactive_state" in
        High|Low) ;;
        *) inactive_state="" ;;
      esac
    fi
  else
    echo "Note: jq not found. Skipping inactive relay helpers."
  fi

  install_systemd_service_for_server "$inactive_pin" "$inactive_state"

  if [ "$server_restarted" -eq 1 ]; then
    start_server_service
  else
    systemctl restart "$SERVER_SERVICE_NAME" >/dev/null 2>&1 || start_server_service
  fi

  SUMMARY+=("Server files installed at $SERVER_INSTALL_DIR")
}

update_client_files() {
  if [ ! -d "$CLIENT_FILES_SOURCE_DIR" ]; then
    echo "Error: client payload missing." >&2
    exit 1
  fi

  ensure_dir_owned_by_user "$CLIENT_INSTALL_DIR"

  local backup_server_details
  preserve_file_if_exists "$CLIENT_INSTALL_DIR/ServerDetails.json" backup_server_details

  find "$CLIENT_INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -a "$CLIENT_FILES_SOURCE_DIR/." "$CLIENT_INSTALL_DIR/"
  chmod +x "$CLIENT_INSTALL_DIR/RemoteRelay"

  restore_preserved_file "$backup_server_details" "$CLIENT_INSTALL_DIR/ServerDetails.json"
  chown -R "$APP_USER:$APP_USER" "$CLIENT_INSTALL_DIR"

  local client_exec="$CLIENT_INSTALL_DIR/RemoteRelay"
  local enable_kiosk="false"
  if prompt_yes_no "Disable screen blanking for the client display? [Y/n] " "Y"; then
    enable_kiosk="true"
  fi

  create_autostart_desktop "$client_exec" "$enable_kiosk"
  configure_wayfire "$client_exec" "$enable_kiosk"

  if [ "$enable_kiosk" = "true" ]; then
    echo "Kiosk mode enabled: desktop environments will keep the display awake before launching the client."
  else
    echo "Kiosk mode skipped: screen blanking settings unchanged."
  fi

  local client_summary="Client files installed at $CLIENT_INSTALL_DIR (autostart configured)"
  if [ "$enable_kiosk" = "true" ]; then
    client_summary+=" with kiosk screen blanking disabled"
  fi
  SUMMARY+=("$client_summary")
}

update_server_details_host() {
  local server_address="$1"
  local server_details="$CLIENT_INSTALL_DIR/ServerDetails.json"
  if [ -f "$server_details" ]; then
    local tmp
    tmp=$(mktemp)
    local escaped_address
    escaped_address=$(printf '%s' "$server_address" | sed -e 's/[\\/&]/\\&/g')
    if sed -E "s/(\"Host\"\s*:\s*\")([^\"]*)(\")/\\1$escaped_address\\3/" "$server_details" > "$tmp"; then
      mv "$tmp" "$server_details"
      chown "$APP_USER:$APP_USER" "$server_details"
      SUMMARY+=("Updated client host to $server_address")
    else
      echo "Warning: failed to update $server_details" >&2
      rm -f "$tmp"
    fi
  fi
}

copy_uninstall_script() {
  ensure_dir_owned_by_user "$BASE_INSTALL_DIR"
  if [ -f "$SCRIPT_DIR/$UNINSTALL_SCRIPT_SOURCE" ]; then
    cp "$SCRIPT_DIR/$UNINSTALL_SCRIPT_SOURCE" "$UNINSTALL_SCRIPT_DEST"
    chmod +x "$UNINSTALL_SCRIPT_DEST"
    chown "$APP_USER:$APP_USER" "$UNINSTALL_SCRIPT_DEST"
    SUMMARY+=("Uninstall script placed at $UNINSTALL_SCRIPT_DEST")
  else
    echo "Warning: uninstall script not found in installer payload." >&2
  fi
}

echo "----------------------------------------------------"
echo "$(bold "RemoteRelay Installer")"
echo "----------------------------------------------------"
echo "Target user: $APP_USER"
echo "Install root: $BASE_INSTALL_DIR"
echo

migrate_legacy_layout

SERVER_ALREADY_PRESENT=false
CLIENT_ALREADY_PRESENT=false
if [ -d "$SERVER_INSTALL_DIR" ] && [ -f "$SERVER_INSTALL_DIR/RemoteRelay.Server" ]; then
  SERVER_ALREADY_PRESENT=true
fi
if [ -d "$CLIENT_INSTALL_DIR" ] && [ -f "$CLIENT_INSTALL_DIR/RemoteRelay" ]; then
  CLIENT_ALREADY_PRESENT=true
fi

DO_INSTALL_SERVER=false
DO_INSTALL_CLIENT=false

if $SERVER_ALREADY_PRESENT || $CLIENT_ALREADY_PRESENT; then
  echo "Existing installation detected."
  if $SERVER_ALREADY_PRESENT; then
    if prompt_yes_no "Update server component? [Y/n] " "Y"; then
      DO_INSTALL_SERVER=true
    fi
  else
    if prompt_yes_no "Install server component? [y/N] " "N"; then
      DO_INSTALL_SERVER=true
    fi
  fi

  if $CLIENT_ALREADY_PRESENT; then
    if prompt_yes_no "Update client component? [Y/n] " "Y"; then
      DO_INSTALL_CLIENT=true
    fi
  else
    if prompt_yes_no "Install client component? [y/N] " "N"; then
      DO_INSTALL_CLIENT=true
    fi
  fi
else
  if prompt_yes_no "Install server component? [Y/n] " "Y"; then
    DO_INSTALL_SERVER=true
  fi

  if prompt_yes_no "Install client component? [Y/n] " "Y"; then
    DO_INSTALL_CLIENT=true
  fi

  if ! $DO_INSTALL_SERVER && ! $DO_INSTALL_CLIENT; then
    echo "Nothing selected. Exiting."
    exit 0
  fi
fi

SERVER_ADDRESS=""
if $DO_INSTALL_CLIENT; then
  local_default="localhost"
  read -r -p "Server address for clients [$local_default]: " SERVER_ADDRESS
  SERVER_ADDRESS=${SERVER_ADDRESS:-$local_default}
fi

if $DO_INSTALL_SERVER; then
  update_server_files
fi

if $DO_INSTALL_CLIENT; then
  update_client_files
  update_server_details_host "$SERVER_ADDRESS"
fi

copy_uninstall_script

chown -R "$APP_USER:$APP_USER" "$BASE_INSTALL_DIR"

echo
echo "----------------------------------------------------"
echo "Installation complete"
echo "----------------------------------------------------"
for line in "${SUMMARY[@]}"; do
  echo "- $line"
done

echo
echo "Server service: systemctl status $SERVER_SERVICE_NAME"
echo "Client binary: $CLIENT_INSTALL_DIR/RemoteRelay"
echo "Configuration lives in user-owned files at $BASE_INSTALL_DIR"

exit 0
