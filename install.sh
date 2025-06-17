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

# Welcome Message
echo "----------------------------------------------------"
echo " Welcome to the RemoteRelay Installation Script "
echo "----------------------------------------------------"
echo "Installing for user: $APP_USER"
echo "Installation base directory: $BASE_INSTALL_DIR"
echo

# No root check needed if installing to home directory for user-level services/autostart

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

  # User-level systemd service
  USER_SYSTEMD_DIR="$USER_HOME/.config/systemd/user"
  mkdir -p "$USER_SYSTEMD_DIR"
  SERVICE_FILE="$USER_SYSTEMD_DIR/remote-relay-server.service"

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

  echo "Creating systemd user service file: $SERVICE_FILE"
  # Start creating the service file
  cat << EOF > "$SERVICE_FILE"
[Unit]
Description=RemoteRelay Server (User Service)
After=network.target

[Service]
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
# Environment=DOTNET_ROOT=/usr/share/dotnet # May not be needed if .NET is in PATH or self-contained

[Install]
WantedBy=default.target # For user services
EOF

  echo "Reloading systemd user daemon..."
  command systemctl --user daemon-reload
  echo "Enabling remote-relay-server user service..."
  command systemctl --user enable remote-relay-server.service
  echo "Starting remote-relay-server user service..."
  command systemctl --user start remote-relay-server.service

  # Check service status
  if command systemctl --user is-active --quiet remote-relay-server.service; then
    echo "RemoteRelay Server user service is active and running."
  else
    echo "Warning: RemoteRelay Server user service failed to start. Check logs with 'journalctl --user -u remote-relay-server.service'"
  fi
  echo "Server Installation Complete."
  echo "To manage the server, use: systemctl --user [status|start|stop|restart] remote-relay-server.service"
  echo "User services require the user to be logged in and sometimes require 'linger' to be enabled for the user to run on boot without login:"
  echo "sudo loginctl enable-linger $APP_USER"
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

  # LXDE Autostart configuration for client (Kiosk Mode on Raspberry Pi)
  LXDE_AUTOSTART_DIR="$USER_HOME/.config/lxsession/LXDE-pi"
  AUTOSTART_FILE="$LXDE_AUTOSTART_DIR/autostart"
  CLIENT_EXEC_PATH="$CLIENT_INSTALL_DIR/RemoteRelay"

  echo "Configuring autostart for RemoteRelay Client..."
  mkdir -p "$LXDE_AUTOSTART_DIR"
  if [ $? -ne 0 ]; then
    echo "Warning: Could not create LXDE autostart directory: $LXDE_AUTOSTART_DIR"
    echo "Client may not autostart. Please check permissions or create it manually."
  else
    touch "$AUTOSTART_FILE" # Create the file if it doesn't exist
    if [ $? -ne 0 ]; then
      echo "Warning: Could not create LXDE autostart file: $AUTOSTART_FILE"
      echo "Client may not autostart. Please check permissions or create it manually."
    else
      echo "Disabling screen blanking and power saving for LXDE session..."
      CMD_NOBLANK="@xset s noblank"
      CMD_SOFF="@xset s off"
      CMD_NODPMS="@xset -dpms"

      if ! grep -qF "$CMD_NOBLANK" "$AUTOSTART_FILE"; then
        echo "Adding: $CMD_NOBLANK to $AUTOSTART_FILE"
        echo "$CMD_NOBLANK" >> "$AUTOSTART_FILE"
      else
        echo "Command to disable screen blanking already exists in $AUTOSTART_FILE."
      fi

      if ! grep -qF "$CMD_SOFF" "$AUTOSTART_FILE"; then
        echo "Adding: $CMD_SOFF to $AUTOSTART_FILE"
        echo "$CMD_SOFF" >> "$AUTOSTART_FILE"
      else
        echo "Command to turn off screen saver already exists in $AUTOSTART_FILE."
      fi

      if ! grep -qF "$CMD_NODPMS" "$AUTOSTART_FILE"; then
        echo "Adding: $CMD_NODPMS to $AUTOSTART_FILE"
        echo "$CMD_NODPMS" >> "$AUTOSTART_FILE"
      else
        echo "Command to disable DPMS already exists in $AUTOSTART_FILE."
      fi
      echo "Screen blanking and power saving settings configured in $AUTOSTART_FILE."

      # Check if the client command already exists
      if grep -qF "@\"$CLIENT_EXEC_PATH\"" "$AUTOSTART_FILE"; then
        echo "RemoteRelay Client autostart command already exists in $AUTOSTART_FILE."
      else
        echo "Adding RemoteRelay Client to LXDE autostart file: $AUTOSTART_FILE"
        echo "@\"$CLIENT_EXEC_PATH\"" >> "$AUTOSTART_FILE"
        if [ $? -ne 0 ]; then
            echo "Warning: Failed to add client to autostart file $AUTOSTART_FILE."
            echo "You may need to add '@\"$CLIENT_EXEC_PATH\"' to it manually."
        else
            echo "RemoteRelay Client added to autostart."
        fi
      fi
    fi
  fi

  echo "Client Installation Complete."
  echo "The RemoteRelay Client is configured to start automatically with the LXDE desktop session."
  echo "Screen blanking and power saving features for the session have also been disabled via autostart."
  echo "It will attempt to start when the $APP_USER user logs into the graphical desktop."
  echo "If the client does not start, or if screen blanking persists, check the $AUTOSTART_FILE file and ensure the commands and paths are correct."
  echo "You can also try running '$CLIENT_EXEC_PATH' manually from a terminal within the LXDE session to check for client errors."
  echo
}

# --- Perform Installations Based on User Choice ---
SUMMARY_MESSAGE="Installation Summary:\\n"

if [[ "$INSTALL_TYPE" == "S" || "$INSTALL_TYPE" == "B" ]]; then
  install_server
  SUMMARY_MESSAGE+="  - RemoteRelay Server installed to $SERVER_INSTALL_DIR and configured as a systemd user service.\\n"
fi

if [[ "$INSTALL_TYPE" == "C" || "$INSTALL_TYPE" == "B" ]]; then
  install_client
  SUMMARY_MESSAGE+="  - RemoteRelay Client installed to $CLIENT_INSTALL_DIR, configured with Server Address $SERVER_ADDRESS, and set up to autostart with the LXDE desktop session.\\n"
fi

# Completion Message
echo "----------------------------------------------------"
echo " RemoteRelay Installation Finished! "
echo "----------------------------------------------------"
echo -e "$SUMMARY_MESSAGE"
echo "Please check the output above for any warnings or errors."
echo "User services (server, if installed) can be managed with:"
echo "  systemctl --user [status|start|stop|restart] remote-relay-server.service"
echo "To enable the server service to start on boot (even without login), run:"
echo "  sudo loginctl enable-linger $APP_USER"
echo "The client (if installed) is configured to start automatically with the LXDE desktop session for user $APP_USER."
echo "If the server was also installed on this machine, ensure the server is running before the client attempts to connect."

exit 0
