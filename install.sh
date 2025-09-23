#!/bin/bash

# Determine user's home directory for installation
# Use SUDO_USER if set (script run with sudo), otherwise current user
APP_USER="${SUDO_USER:-$(whoami)}"
USER_HOME=$(eval echo ~$APP_USER)

if [ -z "$USER_HOME" ] || [ ! -d "$USER_HOME" ]; then
  echo "Error: Could not determine user's home directory. Exiting."
  exit 1
fi

# Define installation directories within the user's home
BASE_INSTALL_DIR="$USER_HOME/.local/share/RemoteRelay"
SERVER_INSTALL_DIR="$BASE_INSTALL_DIR/Server"
CLIENT_INSTALL_DIR="$BASE_INSTALL_DIR/Client"

# These are relative to the location of install.sh when extracted by makeself
SERVER_FILES_SOURCE_DIR="server_files"
CLIENT_FILES_SOURCE_DIR="client_files"
UNINSTALL_SCRIPT_SOURCE="uninstall.sh"

# Welcome Message
echo "----------------------------------------------------"
echo " Welcome to the RemoteRelay Installation Script "
echo "----------------------------------------------------"
echo "Installing for user: $APP_USER"
echo "Installation base directory: $BASE_INSTALL_DIR"
echo

# User Choices
INSTALL_TYPE=""
SERVER_ADDRESS="" # Changed from SERVER_IP to SERVER_ADDRESS

while [[ "$INSTALL_TYPE" != "S" && "$INSTALL_TYPE" != "C" && "$INSTALL_TYPE" != "B" ]]; do
  read -p "Choose installation type (S for Server, C for Client, B for Both): " INSTALL_TYPE
  INSTALL_TYPE=$(echo "$INSTALL_TYPE" | tr '[:lower:]' '[:upper:]') # Convert to uppercase
  if [[ "$INSTALL_TYPE" != "S" && "$INSTALL_TYPE" != "C" && "$INSTALL_TYPE" != "B" ]]; then
    echo "Invalid input. Please enter S, C, or B."
  fi
done

if [[ "$INSTALL_TYPE" == "C" || "$INSTALL_TYPE" == "B" ]]; then
  DEFAULT_ADDRESS="localhost" # Changed from 127.0.0.1
  if [[ "$INSTALL_TYPE" == "C" ]]; then
    read -p "Enter the Server's address (hostname or IP): " SERVER_ADDRESS
  else # Both Server and Client
    read -p "Enter the Server's address (hostname or IP, default: $DEFAULT_ADDRESS for local server): " USER_INPUT_ADDRESS
    SERVER_ADDRESS=${USER_INPUT_ADDRESS:-$DEFAULT_ADDRESS}
  fi
  if [ -z "$SERVER_ADDRESS" ]; then
    echo "Server address cannot be empty. Exiting."
    exit 1
  fi
fi

echo # Newline for better readability

# Check if server installation requires root privileges
if [[ "$INSTALL_TYPE" == "S" || "$INSTALL_TYPE" == "B" ]]; then
  if [ "$EUID" -ne 0 ]; then
    echo "Error: Server installation requires root privileges to install as a system service."
    echo "Please run this script with sudo when installing the server."
    exit 1
  fi
fi

# --- Server Installation ---
install_server() {
  echo "Starting Server Installation..."

  echo "Creating installation directory: $SERVER_INSTALL_DIR"
  mkdir -p "$SERVER_INSTALL_DIR"
  if [ ! -d "$SERVER_FILES_SOURCE_DIR" ]; then
    echo "Error: Server files source directory '$SERVER_FILES_SOURCE_DIR' not found. Make sure it's in the same directory as the installer."
    exit 1
  fi
  echo "Copying server files to $SERVER_INSTALL_DIR..."
  # Copy all contents from the source to the target
  cp -a "$SERVER_FILES_SOURCE_DIR/." "$SERVER_INSTALL_DIR/"
  chmod +x "$SERVER_INSTALL_DIR/RemoteRelay.Server"
  
  # Set ownership to the target user
  chown -R "$APP_USER:$APP_USER" "$SERVER_INSTALL_DIR"

  # System-level systemd service
  SYSTEM_SYSTEMD_DIR="/etc/systemd/system"
  SERVICE_FILE="$SYSTEM_SYSTEMD_DIR/remote-relay-server.service"

  # Check for jq
  if ! command -v jq &> /dev/null
  then
      echo "Warning: jq could not be found. Please install jq to enable InactiveRelay configuration in systemd service."
      echo "On Debian/Ubuntu: sudo apt install jq"
      echo "Skipping InactiveRelay configuration for systemd service."
      # Set INACTIVE_RELAY_PIN and INACTIVE_RELAY_STATE to empty so the rest of the script behaves as if no config found
      INACTIVE_RELAY_PIN=""
      INACTIVE_RELAY_STATE=""
  else
      CONFIG_FILE="$SERVER_INSTALL_DIR/config.json"
      if [ -f "$CONFIG_FILE" ]; then
          INACTIVE_RELAY_PIN=$(jq -r '.InactiveRelay.Pin // empty' "$CONFIG_FILE")
          INACTIVE_RELAY_STATE=$(jq -r '.InactiveRelay.InactiveState // empty' "$CONFIG_FILE")
      else
          echo "Warning: Configuration file $CONFIG_FILE not found. Skipping InactiveRelay configuration."
          INACTIVE_RELAY_PIN=""
          INACTIVE_RELAY_STATE=""
      fi
  fi

  EXEC_START_PRE=""
  EXEC_STOP_POST=""

  if [ -n "$INACTIVE_RELAY_PIN" ] && [ -n "$INACTIVE_RELAY_STATE" ]; then
      # Basic validation (more robust validation is in the C# app)
      if [[ "$INACTIVE_RELAY_PIN" =~ ^[0-9]+$ ]] && \
         ( [[ "$INACTIVE_RELAY_STATE" == "High" ]] || [[ "$INACTIVE_RELAY_STATE" == "Low" ]] || \
           [[ "$INACTIVE_RELAY_STATE" == "high" ]] || [[ "$INACTIVE_RELAY_STATE" == "low" ]] ); then # Added lowercase variants

          REMOTE_RELAY_EXEC="$SERVER_INSTALL_DIR/RemoteRelay.Server" # Path to the executable
          # Ensure state is capitalized for the command, C# app is case-insensitive but consistency is good
          CMD_STATE=$(echo "$INACTIVE_RELAY_STATE" | awk '{print toupper(substr($0,1,1))tolower(substr($0,2))}')

          EXEC_START_PRE="ExecStartPre=$REMOTE_RELAY_EXEC set-inactive-relay --pin $INACTIVE_RELAY_PIN --state $CMD_STATE"
          EXEC_STOP_POST="ExecStopPost=$REMOTE_RELAY_EXEC set-inactive-relay --pin $INACTIVE_RELAY_PIN --state $CMD_STATE"
          echo "Inactive Relay systemd integration: Configured for pin $INACTIVE_RELAY_PIN, state $CMD_STATE"
      else
          echo "Warning: InactiveRelay Pin ('$INACTIVE_RELAY_PIN') or State ('$INACTIVE_RELAY_STATE') in $CONFIG_FILE is invalid. Skipping systemd pre/post commands."
      fi
  else
      if command -v jq &> /dev/null && [ -f "$CONFIG_FILE" ]; then # Only show this if jq was found and config file existed
        echo "InactiveRelay settings not found or incomplete in $CONFIG_FILE. Skipping systemd pre/post commands."
      fi
  fi

  echo "Creating systemd system service file: $SERVICE_FILE"
  # Start creating the service file
  cat << EOF > "$SERVICE_FILE"
[Unit]
Description=RemoteRelay Server (System Service)
After=network.target

[Service]
User=$APP_USER
Group=$APP_USER
EOF

  # Conditionally add ExecStartPre
  if [ -n "$EXEC_START_PRE" ]; then
    echo "$EXEC_START_PRE" >> "$SERVICE_FILE"
  fi

  # Add mandatory lines
  cat << EOF >> "$SERVICE_FILE"
WorkingDirectory=$SERVER_INSTALL_DIR
ExecStart=$SERVER_INSTALL_DIR/RemoteRelay.Server
EOF

  # Conditionally add ExecStopPost
  if [ -n "$EXEC_STOP_POST" ]; then
    echo "$EXEC_STOP_POST" >> "$SERVICE_FILE"
  fi

  # Add the rest of the service file
  cat << EOF >> "$SERVICE_FILE"
Restart=always
RestartSec=5
# Environment=DOTNET_ROOT=/usr/share/dotnet # May not be needed if .NET is in PATH or self-contained

[Install]
WantedBy=multi-user.target
EOF

  echo "Reloading systemd daemon..."
  systemctl daemon-reload
  echo "Enabling remote-relay-server system service..."
  systemctl enable remote-relay-server.service
  echo "Starting remote-relay-server system service..."
  systemctl start remote-relay-server.service

  # Check service status
  if systemctl is-active --quiet remote-relay-server.service; then
    echo "RemoteRelay Server system service is active and running."
  else
    echo "Warning: RemoteRelay Server system service failed to start. Check logs with 'journalctl -u remote-relay-server.service'"
  fi
  echo "Server Installation Complete."
  echo "To manage the server, use: systemctl [status|start|stop|restart] remote-relay-server.service"
  echo "The server will now start automatically on system boot."
  echo
}

# --- Client Installation ---
install_client() {
  echo "Starting Client Installation..."

  echo "Creating installation directory: $CLIENT_INSTALL_DIR"
  mkdir -p "$CLIENT_INSTALL_DIR"
   if [ ! -d "$CLIENT_FILES_SOURCE_DIR" ]; then
    echo "Error: Client files source directory '$CLIENT_FILES_SOURCE_DIR' not found. Make sure it's in the same directory as the installer."
    exit 1
  fi
  echo "Copying client files to $CLIENT_INSTALL_DIR..."
  # Copy all contents from the source to the target
  cp -a "$CLIENT_FILES_SOURCE_DIR/." "$CLIENT_INSTALL_DIR/"
  chmod +x "$CLIENT_INSTALL_DIR/RemoteRelay"
  
  # Set ownership to the target user (important when running with sudo)
  chown -R "$APP_USER:$APP_USER" "$CLIENT_INSTALL_DIR"

  SERVER_DETAILS_FILE="$CLIENT_INSTALL_DIR/ServerDetails.json"
  if [ -f "$SERVER_DETAILS_FILE" ]; then
    echo "Updating Server Address in $SERVER_DETAILS_FILE to $SERVER_ADDRESS..."
    TMP_SERVER_DETAILS_FILE=$(mktemp)
    # This sed command is a bit more robust for various JSON value characters
    sed -E "s/(\"Host\":\s*\")[^\"]*(\")/\1${SERVER_ADDRESS//\\&/\\\\&}\2/" "$SERVER_DETAILS_FILE" > "$TMP_SERVER_DETAILS_FILE" && mv "$TMP_SERVER_DETAILS_FILE" "$SERVER_DETAILS_FILE"
    if [ $? -ne 0 ]; then
        echo "Warning: Failed to update Server Address in $SERVER_DETAILS_FILE using sed."
        echo "Please ensure $SERVER_DETAILS_FILE contains a line like: \"Host\": \"OLD_ADDRESS\""
        [ -f "$TMP_SERVER_DETAILS_FILE" ] && rm "$TMP_SERVER_DETAILS_FILE"
    fi
  else
    echo "Warning: $SERVER_DETAILS_FILE not found. Cannot update Server Address."
    echo "The application might look for this file. It should have been part of the published client files."
  fi

  CLIENT_EXEC_PATH="$CLIENT_INSTALL_DIR/RemoteRelay" # Ensure this is defined for the Exec line

  # XDG Autostart configuration
  XDG_AUTOSTART_DIR="$USER_HOME/.config/autostart"
  DESKTOP_FILE_NAME="remote-relay-client.desktop"
  DESKTOP_FILE_PATH="$XDG_AUTOSTART_DIR/$DESKTOP_FILE_NAME"

  echo "Creating XDG autostart directory: $XDG_AUTOSTART_DIR..."
  mkdir -p "$XDG_AUTOSTART_DIR"
  # Ensure proper ownership of the autostart directory
  chown "$APP_USER:$APP_USER" "$XDG_AUTOSTART_DIR" 2>/dev/null || true
  if [ $? -ne 0 ]; then
    echo "Warning: Could not create XDG autostart directory: $XDG_AUTOSTART_DIR"
    echo "Client may not autostart. Please check permissions or create it manually."
  else
    echo "Creating XDG autostart file: $DESKTOP_FILE_PATH..."
    # Using a subshell for variable expansion within heredoc to avoid issues with current shell context
    (
    cat << EOF > "$DESKTOP_FILE_PATH"
[Desktop Entry]
Type=Application
Name=RemoteRelay Client
Exec=$CLIENT_EXEC_PATH
Terminal=false
Path=$CLIENT_INSTALL_DIR
EOF
    )
    if [ $? -ne 0 ]; then
        echo "Warning: Failed to create .desktop file: $DESKTOP_FILE_PATH"
        echo "Client may not autostart. Please check file system permissions."
    else
        chmod 755 "$DESKTOP_FILE_PATH"
        chown "$APP_USER:$APP_USER" "$DESKTOP_FILE_PATH" 2>/dev/null || true
        echo "RemoteRelay Client XDG autostart entry created at $DESKTOP_FILE_PATH."

        # Ask user if they want kiosk mode (screen blanking disabled)
        echo
        read -p "Enable kiosk mode (disable screen blanking/power saving)? (y/N): " ENABLE_KIOSK
        if [[ "$ENABLE_KIOSK" =~ ^[Yy]$ ]]; then
            # Modify the Exec line to include xset commands while keeping the simple format
            XSET_COMMANDS="xset s noblank && xset s off && xset -dpms"
            # Check if .desktop file exists before modifying (it should, as we just created it)
            if [ -f "$DESKTOP_FILE_PATH" ]; then
                echo "Modifying Exec line in $DESKTOP_FILE_PATH to include screen blanking commands..."
                # Replace the Exec line to include xset commands
                TEMP_EXEC_LINE_CONTENT="sh -c '$XSET_COMMANDS && \"$CLIENT_EXEC_PATH\"'"
                sed -i "/^Exec=/c\Exec=${TEMP_EXEC_LINE_CONTENT}" "$DESKTOP_FILE_PATH"

                if [ $? -eq 0 ]; then
                    echo "Exec line updated successfully to include xset commands for kiosk mode."
                else
                    echo "Warning: Failed to update Exec line in $DESKTOP_FILE_PATH. Screen blanking may not be disabled."
                fi
            else
                echo "Warning: $DESKTOP_FILE_PATH not found for modification. This should not happen."
            fi
        else
            echo "Kiosk mode disabled. Screen blanking settings will remain default."
        fi
    fi
  fi

  echo "Client Installation Complete."
  echo "The RemoteRelay Client is now configured to start automatically with your desktop session using XDG autostart."
  if [[ "$ENABLE_KIOSK" =~ ^[Yy]$ ]]; then
    echo "Kiosk mode enabled: X11 screen blanking and power saving settings will be disabled at session start."
  fi
  echo "If the client does not start automatically, please check the file $DESKTOP_FILE_PATH"
  echo "and ensure your desktop environment supports XDG autostart."
  echo
  echo "Troubleshooting autostart issues:"
  echo "1. Test manually: $CLIENT_EXEC_PATH"
  echo "2. Test with working directory: cd '$CLIENT_INSTALL_DIR' && '$CLIENT_EXEC_PATH'"
  echo "3. Check autostart logs: journalctl --user-unit graphical-session.target"
  echo "4. Verify desktop file: desktop-file-validate '$DESKTOP_FILE_PATH'"
  echo

  # Attempt to configure Wayfire screen blanking (Wayland)
  WAYFIRE_INI="$USER_HOME/.config/wayfire.ini"
  echo # Add a newline for better separation of messages
  echo "--- Wayland/Wayfire Kiosk Configuration (Experimental) ---"

  if [ -f "$WAYFIRE_INI" ]; then # Check if it's a regular file
      if [ -s "$WAYFIRE_INI" ]; then # If it exists and is non-empty
          echo "Attempting to configure Wayfire screen blanking in $WAYFIRE_INI..."
          # AWK script content
          AWK_SCRIPT='
          BEGIN {
              in_idle_section = 0;
              dpms_key_processed_in_current_idle_section = 0;
              screensaver_key_processed_in_current_idle_section = 0;
              any_idle_section_found = 0;
              current_section_lines_buffer = "";
          }

          function flush_current_section_buffer() {
              if (in_idle_section) {
                  print current_section_lines_buffer;
                  if (!dpms_key_processed_in_current_idle_section) {
                      print "dpms_timeout = 0";
                  }
                  if (!screensaver_key_processed_in_current_idle_section) {
                      print "screensaver_timeout = 0";
                  }
              } else {
                  if (current_section_lines_buffer != "") {
                       print current_section_lines_buffer;
                  }
              }
              current_section_lines_buffer = "";
              in_idle_section = 0;
              dpms_key_processed_in_current_idle_section = 0;
              screensaver_key_processed_in_current_idle_section = 0;
          }

          /^\s*\[idle\]\s*$/ {
              flush_current_section_buffer();
              in_idle_section = 1;
              any_idle_section_found = 1;
              current_section_lines_buffer = $0;
              next;
          }

          /^\s*\[.*\]\s*$/ {
              flush_current_section_buffer();
              current_section_lines_buffer = $0;
              next;
          }

          {
              if (in_idle_section) {
                  if ($0 ~ /^\s*dpms_timeout\s*=/) {
                      current_section_lines_buffer = (current_section_lines_buffer == "" ? "" : current_section_lines_buffer ORS) "dpms_timeout = 0";
                      dpms_key_processed_in_current_idle_section = 1;
                      next;
                  }
                  if ($0 ~ /^\s*screensaver_timeout\s*=/) {
                      current_section_lines_buffer = (current_section_lines_buffer == "" ? "" : current_section_lines_buffer ORS) "screensaver_timeout = 0";
                      screensaver_key_processed_in_current_idle_section = 1;
                      next;
                  }
              }
              current_section_lines_buffer = (current_section_lines_buffer == "" ? $0 : current_section_lines_buffer ORS $0);
          }

          END {
              flush_current_section_buffer();
              if (!any_idle_section_found) {
                  print "[idle]";
                  print "dpms_timeout = 0";
                  print "screensaver_timeout = 0";
              }
          }
          '
          WAYFIRE_INI_TMP="$WAYFIRE_INI.tmp.$$"

          if awk "$AWK_SCRIPT" "$WAYFIRE_INI" > "$WAYFIRE_INI_TMP"; then
              if [ -s "$WAYFIRE_INI_TMP" ]; then
                  mv "$WAYFIRE_INI_TMP" "$WAYFIRE_INI"
                  echo "Wayfire configuration ($WAYFIRE_INI) attempt to disable screen blanking complete."
                  echo "Please verify $WAYFIRE_INI, especially the [idle] section, to ensure settings (dpms_timeout=0, screensaver_timeout=0) are correct."
              else
                  echo "Warning: Processing $WAYFIRE_INI with awk resulted in an empty file. Original file preserved."
                  rm -f "$WAYFIRE_INI_TMP"
              fi
          else
              echo "Warning: awk command failed while processing $WAYFIRE_INI. Original file preserved."
              rm -f "$WAYFIRE_INI_TMP"
          fi
      else # File exists but is empty
          echo "Wayfire configuration file $WAYFIRE_INI is empty. Appending default kiosk settings for [idle] section."
          {
              echo "[idle]"
              echo "dpms_timeout = 0"
              echo "screensaver_timeout = 0"
          } >> "$WAYFIRE_INI"
          echo "Wayfire configuration ($WAYFIRE_INI) updated with default kiosk settings for idle."
      fi
  else # File does not exist
      echo "Wayfire configuration file $WAYFIRE_INI not found. Skipping Wayland/Wayfire screen blanking setup."
  fi
  echo "--- End of Wayland/Wayfire Kiosk Configuration ---"
  echo
}

# --- Perform Installations Based on User Choice ---
SUMMARY_MESSAGE="Installation Summary:\\n"

# Copy uninstall script to user's home directory
UNINSTALL_SCRIPT_DEST="$USER_HOME/uninstall-remoteRelay.sh"
if [ -f "$UNINSTALL_SCRIPT_SOURCE" ]; then
  echo "Copying uninstall script to: $UNINSTALL_SCRIPT_DEST"
  cp "$UNINSTALL_SCRIPT_SOURCE" "$UNINSTALL_SCRIPT_DEST"
  chmod +x "$UNINSTALL_SCRIPT_DEST"
  chown "$APP_USER:$APP_USER" "$UNINSTALL_SCRIPT_DEST" 2>/dev/null || true
  echo "Uninstall script available at: $UNINSTALL_SCRIPT_DEST"
  echo
else
  echo "Warning: Uninstall script not found in installer package."
fi

if [[ "$INSTALL_TYPE" == "S" || "$INSTALL_TYPE" == "B" ]]; then
  install_server
  SUMMARY_MESSAGE+="  - RemoteRelay Server installed to $SERVER_INSTALL_DIR and configured as a systemd system service.\\n"
fi

if [[ "$INSTALL_TYPE" == "C" || "$INSTALL_TYPE" == "B" ]]; then
  install_client
  SUMMARY_MESSAGE+="  - RemoteRelay Client installed to $CLIENT_INSTALL_DIR, configured with Server Address $SERVER_ADDRESS, and set up for XDG autostart.\\n"
fi

chown -R "$APP_USER:$APP_USER" "$BASE_INSTALL_DIR"


# Completion Message
echo "----------------------------------------------------"
echo " RemoteRelay Installation Finished! "
echo "----------------------------------------------------"
echo -e "$SUMMARY_MESSAGE"
echo "Please check the output above for any warnings or errors."
echo "System services (server, if installed) can be managed with:"
echo "  systemctl [status|start|stop|restart] remote-relay-server.service"
echo "The server service (if installed) will start automatically on system boot."
echo "The client (if installed) is configured to start automatically with your desktop session via XDG autostart."
echo "If the server was also installed on this machine, ensure the server is running before the client attempts to connect."
echo
if [ -f "$UNINSTALL_SCRIPT_DEST" ]; then
  echo "To uninstall RemoteRelay, run: $UNINSTALL_SCRIPT_DEST"
  echo "Note: Use sudo when uninstalling server components."
fi

exit 0
