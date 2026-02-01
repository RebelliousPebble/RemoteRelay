#!/bin/bash

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)

if [ "$EUID" -ne 0 ]; then
  echo "Error: This installer must be run as root (sudo)." >&2
  exit 1
fi

install_dependencies() {
  if command -v apt-get >/dev/null 2>&1; then
    echo "Checking dependencies..."
    if ! command -v curl >/dev/null 2>&1 || ! command -v jq >/dev/null 2>&1; then
       echo "Installing dependencies (curl, jq)..."
       apt-get update -qq && apt-get install -y -qq curl jq
    fi
  else
    echo "Warning: apt-get not found. Please ensure curl and jq are installed manually."
  fi
}

install_dependencies

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
    local backup_dir="$USER_HOME/.remoterelay-backup-$$"
    mkdir -p "$backup_dir"
    local filename=$(basename "$file_path")
    local backup_path="$backup_dir/$filename"
    if cp "$file_path" "$backup_path"; then
      echo "  Preserving existing config: $filename"
      printf -v "$tmp_var" '%s' "$backup_path"
    else
      echo "  Warning: Failed to preserve $filename" >&2
      printf -v "$tmp_var" '%s' ""
    fi
  else
    printf -v "$tmp_var" '%s' ""
  fi
}

restore_preserved_file() {
  local backup_path="$1"
  local dest_path="$2"
  if [ -n "$backup_path" ] && [ -f "$backup_path" ]; then
    local filename=$(basename "$dest_path")
    if cp "$backup_path" "$dest_path"; then
      echo "  Restored existing config: $filename"
      chown "$APP_USER:$APP_USER" "$dest_path"
    else
      echo "  Warning: Failed to restore $filename" >&2
    fi
  fi
}

cleanup_backup_dir() {
  local backup_dir="$USER_HOME/.remoterelay-backup-$$"
  if [ -d "$backup_dir" ]; then
    rm -rf "$backup_dir"
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

clean_wayfire_duplicates() {
  local wayfire_ini="$1"
  if [ ! -f "$wayfire_ini" ]; then
    return
  fi
  
  local tmp="$wayfire_ini.clean.$$"
  
  # Remove all existing remote-relay-client entries and duplicate idle settings
  awk '
    BEGIN { in_autostart = 0; in_idle = 0; }
    /^[ \t]*\[autostart\][ \t]*$/ {
      in_autostart = 1; in_idle = 0; print; next;
    }
    /^[ \t]*\[idle\][ \t]*$/ {
      in_idle = 1; in_autostart = 0; print; next;
    }
    /^[ \t]*\[/ {
      in_autostart = 0; in_idle = 0; print; next;
    }
    {
      if (in_autostart && $0 ~ /^[ \t]*remote-relay-client[ \t]*=/) {
        next;
      }
      if (in_idle && ($0 ~ /^[ \t]*dpms_timeout[ \t]*=/ || $0 ~ /^[ \t]*screensaver_timeout[ \t]*=/)) {
        next;
      }
      print;
    }
  ' "$wayfire_ini" > "$tmp"
  
  if [ -s "$tmp" ]; then
    mv "$tmp" "$wayfire_ini"
    chown "$APP_USER:$APP_USER" "$wayfire_ini"
    echo "Cleaned duplicate entries from wayfire.ini"
  else
    echo "Warning: Failed to clean wayfire.ini" >&2
    rm -f "$tmp"
  fi
}

configure_wayfire_idle() {
  local wayfire_ini="$1"
  local tmp="$wayfire_ini.tmp.$$"

  awk '
    BEGIN {
      in_idle = 0; seen_idle = 0; wrote_dpms = 0; wrote_screen = 0;
    }
    /^[ \t]*\[idle\][ \t]*$/ {
      if (in_idle) {
        if (!wrote_dpms) print "dpms_timeout = 0";
        if (!wrote_screen) print "screensaver_timeout = 0";
      }
      print; in_idle = 1; seen_idle = 1; wrote_dpms = 0; wrote_screen = 0; next;
    }
    /^[ \t]*\[/ {
      if (in_idle) {
        if (!wrote_dpms) print "dpms_timeout = 0";
        if (!wrote_screen) print "screensaver_timeout = 0";
      }
      in_idle = 0;
      print; next;
    }
    {
      if (in_idle) {
        if ($0 ~ /^[ \t]*dpms_timeout[ \t]*=/) {
          if (!wrote_dpms) {
            print "dpms_timeout = 0"; wrote_dpms = 1;
          }
          next;
        }
        if ($0 ~ /^[ \t]*screensaver_timeout[ \t]*=/) {
          if (!wrote_screen) {
            print "screensaver_timeout = 0"; wrote_screen = 1;
          }
          next;
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
    /^[ \t]*\[autostart\][ \t]*$/ {
      if (in_autostart && !wrote_entry) print key " = " entry;
      print; in_autostart = 1; seen_autostart = 1; wrote_entry = 0; next;
    }
    /^[ \t]*\[/ {
      if (in_autostart && !wrote_entry) print key " = " entry;
      in_autostart = 0;
      print; next;
    }
    {
      if (in_autostart) {
        if ($0 ~ "^[ \\t]*" key "[ \\t]*=") {
          if (!wrote_entry) {
            print key " = " entry;
            wrote_entry = 1;
          }
          next;
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
  local launcher_script="$1"
  local disable_idle="${2:-false}"
  local wayfire_ini="$USER_HOME/.config/wayfire.ini"
  ensure_dir_owned_by_user "$USER_HOME/.config"
  if [ ! -f "$wayfire_ini" ]; then
    touch "$wayfire_ini"
    chown "$APP_USER:$APP_USER" "$wayfire_ini"
  fi

  # Clean up any duplicate entries from previous installs
  clean_wayfire_duplicates "$wayfire_ini"

  # Build the command to run
  # Wayfire runs commands with sh, so just call the launcher script directly
  # Screen blanking is handled by [idle] section, not xset
  local autostart_cmd="$launcher_script"

  ensure_wayfire_autostart "$wayfire_ini" "$autostart_cmd"
  if [ "$disable_idle" = "true" ]; then
    configure_wayfire_idle "$wayfire_ini"
  fi
  chown "$APP_USER:$APP_USER" "$wayfire_ini"
  echo "Wayfire autostart configured in $wayfire_ini"
}

create_autostart_desktop() {
  local launcher_script="$1"
  local enable_kiosk="$2"
  local desktop_path="$USER_HOME/.config/autostart/remote-relay-client.desktop"
  
  # Note: This XDG autostart is for legacy X11 compatibility only.
  # Wayfire (default on modern RPi OS) uses wayfire.ini [autostart] instead.
  
  # Ensure directories exist with proper permissions
  ensure_dir_owned_by_user "$USER_HOME/.config"
  ensure_dir_owned_by_user "$USER_HOME/.config/autostart"

  local exec_line
  if [ "$enable_kiosk" = "true" ]; then
    # For XDG autostart, use simple format - no nested quotes
    exec_line="sh -c 'xset s noblank; xset s off; xset -dpms; exec $launcher_script'"
  else
    exec_line="$launcher_script"
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
  chmod 644 "$desktop_path"
  chown "$APP_USER:$APP_USER" "$desktop_path"

  echo "XDG autostart desktop entry created (for X11 compatibility)"
}

create_client_launcher() {
  local client_exec="$1"
  local launcher_script="$CLIENT_INSTALL_DIR/start-client.sh"
  
  cat > "$launcher_script" <<'EOF'
#!/bin/bash
# RemoteRelay Client Launcher Script
# Sets up environment and launches the client

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Change to the client directory
cd "$SCRIPT_DIR" || exit 1

# Check for ARMv8.1+ features (like lse) to handle older CPUs (e.g. Pi 3)
# If 'lse' is missing from cpuinfo, disable .NET use of LSE instructions/intrinsics
if [ -f /proc/cpuinfo ]; then
  if ! grep -q "lse" /proc/cpuinfo && ! grep -q "atomics" /proc/cpuinfo; then
     echo "Detected CPU missing LSE/Atomics (likely Pi 3). Disabling DOTNET_EnableHWIntrinsic and DOTNET_EnableLse."
     export DOTNET_EnableHWIntrinsic=0
     export DOTNET_EnableLse=0
  fi
fi

# Enable logging for debugging autostart issues
exec >> "$SCRIPT_DIR/client.log" 2>&1
echo "[$(date)] Starting RemoteRelay Client"
echo "Working directory: $(pwd)"
echo "Script directory: $SCRIPT_DIR"
echo "Display: $DISPLAY"
echo "Wayland display: $WAYLAND_DISPLAY"
echo "XDG_SESSION_TYPE: $XDG_SESSION_TYPE"

# Wait a bit for the desktop environment to fully initialize
sleep 3

# Launch the client
echo "[$(date)] Launching RemoteRelay binary"
exec "$SCRIPT_DIR/RemoteRelay"
EOF

  chmod +x "$launcher_script"
  chown "$APP_USER:$APP_USER" "$launcher_script"
  echo "$launcher_script"
}

create_desktop_shortcut() {
  local launcher_script="$1"
  local desktop_dir="$USER_HOME/Desktop"
  local desktop_shortcut="$desktop_dir/RemoteRelay.desktop"
  
  # Ensure Desktop directory exists
  if [ ! -d "$desktop_dir" ]; then
    ensure_dir_owned_by_user "$desktop_dir"
  fi

  cat > "$desktop_shortcut" <<EOF
[Desktop Entry]
Type=Application
Name=RemoteRelay Client
Comment=Launch RemoteRelay Client
Exec="$launcher_script"
Path=$CLIENT_INSTALL_DIR
Icon=applications-multimedia
Terminal=false
Categories=AudioVideo;Audio;
EOF
  chmod 755 "$desktop_shortcut"
  chown "$APP_USER:$APP_USER" "$desktop_shortcut"
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

  echo "Preserving existing server configuration files..."
  local backup_config backup_appsettings backup_devsettings
  preserve_file_if_exists "$SERVER_INSTALL_DIR/config.json" backup_config
  preserve_file_if_exists "$SERVER_INSTALL_DIR/appsettings.json" backup_appsettings
  preserve_file_if_exists "$SERVER_INSTALL_DIR/appsettings.Development.json" backup_devsettings

  if stop_server_if_running; then
    server_restarted=1
  fi

  echo "Installing new server files..."
  find "$SERVER_INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -a "$SERVER_FILES_SOURCE_DIR/." "$SERVER_INSTALL_DIR/"
  chmod +x "$SERVER_INSTALL_DIR/RemoteRelay.Server"

  echo "Restoring preserved configuration files..."
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

  echo "Preserving existing client configuration files..."
  local backup_server_details backup_client_config
  preserve_file_if_exists "$CLIENT_INSTALL_DIR/ServerDetails.json" backup_server_details
  preserve_file_if_exists "$CLIENT_INSTALL_DIR/ClientConfig.json" backup_client_config

  echo "Installing new client files..."
  find "$CLIENT_INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -a "$CLIENT_FILES_SOURCE_DIR/." "$CLIENT_INSTALL_DIR/"
  chmod +x "$CLIENT_INSTALL_DIR/RemoteRelay"

  echo "Restoring preserved configuration files..."
  restore_preserved_file "$backup_server_details" "$CLIENT_INSTALL_DIR/ServerDetails.json"
  restore_preserved_file "$backup_client_config" "$CLIENT_INSTALL_DIR/ClientConfig.json"
  chown -R "$APP_USER:$APP_USER" "$CLIENT_INSTALL_DIR"

  local enable_kiosk="false"
  if prompt_yes_no "Disable screen blanking for the client display? [Y/n] " "Y"; then
    enable_kiosk="true"
  fi

  echo "Creating launcher script..."
  local launcher_script
  launcher_script=$(create_client_launcher "$CLIENT_INSTALL_DIR/RemoteRelay")

  echo "Configuring autostart..."
  # Wayfire (Wayland) is the default on modern RPi OS - configure it first
  configure_wayfire "$launcher_script" "$enable_kiosk"
  # Also create XDG autostart for X11 compatibility (legacy)
  create_autostart_desktop "$launcher_script" "$enable_kiosk"
  create_desktop_shortcut "$launcher_script"

  if [ "$enable_kiosk" = "true" ]; then
    echo "Kiosk mode enabled: display will stay awake."
  else
    echo "Kiosk mode skipped: screen blanking settings unchanged."
  fi

  local client_summary="Client files installed at $CLIENT_INSTALL_DIR (autostart configured)"
  if [ "$enable_kiosk" = "true" ]; then
    client_summary+=" with kiosk screen blanking disabled"
  fi
  SUMMARY+=("$client_summary")
  SUMMARY+=("Desktop shortcut created at $USER_HOME/Desktop/RemoteRelay.desktop")
}

update_client_config_host() {
  local server_address="$1"
  local client_config="$CLIENT_INSTALL_DIR/ClientConfig.json"
  if [ -f "$client_config" ]; then
    local tmp
    tmp=$(mktemp)
    # Using jq if available is safer, but fallback to sed if needed?
    # The script checks for jq dependency at start now, so let's try jq first if possible,
    # but to be safe and consistent with the legacy sed approach (which preserves comments better sometimes, though json shouldn't have them):
    
    if command -v jq >/dev/null 2>&1; then
       # update valid json with jq
       jq --arg host "$server_address" '.Host = $host' "$client_config" > "$tmp" && mv "$tmp" "$client_config"
       chown "$APP_USER:$APP_USER" "$client_config"
       SUMMARY+=("Updated client host to $server_address")
    else
       # fallback to sed
        local escaped_address
        escaped_address=$(printf '%s' "$server_address" | sed -e 's/[\\/&]/\\&/g')
        if sed -E "s/(\"Host\"\s*:\s*\")([^\"]*)(\")/\\1$escaped_address\\3/" "$client_config" > "$tmp"; then
          mv "$tmp" "$client_config"
          chown "$APP_USER:$APP_USER" "$client_config"
          SUMMARY+=("Updated client host to $server_address")
        else
          echo "Warning: failed to update $client_config" >&2
          rm -f "$tmp"
        fi
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

copy_update_script() {
  ensure_dir_owned_by_user "$BASE_INSTALL_DIR"
  if [ -f "$SCRIPT_DIR/update.sh" ]; then
    cp "$SCRIPT_DIR/update.sh" "$BASE_INSTALL_DIR/update.sh"
    chmod +x "$BASE_INSTALL_DIR/update.sh"
    chown "$APP_USER:$APP_USER" "$BASE_INSTALL_DIR/update.sh"
    SUMMARY+=("Update script installed at $BASE_INSTALL_DIR/update.sh")
  else
    echo "Warning: update script not found in installer payload." >&2
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
  
  # Try to read existing config for default
  if command -v jq >/dev/null 2>&1 && [ -f "$CLIENT_INSTALL_DIR/ClientConfig.json" ]; then
      existing_host=$(jq -r '.Host // empty' "$CLIENT_INSTALL_DIR/ClientConfig.json")
      if [ -n "$existing_host" ]; then
          local_default="$existing_host"
      fi
  fi
  
  read -r -p "Server address for clients [$local_default]: " SERVER_ADDRESS
  SERVER_ADDRESS=${SERVER_ADDRESS:-$local_default}
fi

if $DO_INSTALL_SERVER; then
  update_server_files
fi

if $DO_INSTALL_CLIENT; then
  update_client_files
  update_client_config_host "$SERVER_ADDRESS"
fi

copy_uninstall_script
copy_update_script

chown -R "$APP_USER:$APP_USER" "$BASE_INSTALL_DIR"

cleanup_backup_dir

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
