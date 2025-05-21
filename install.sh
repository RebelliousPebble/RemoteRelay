#!/bin/bash

# Define installation directories
SERVER_INSTALL_DIR="/opt/RemoteRelay.Server"
CLIENT_INSTALL_DIR="/opt/RemoteRelay.Client"
SERVER_FILES_DIR="server_files"
CLIENT_FILES_DIR="client_files"

# Welcome Message
echo "----------------------------------------------------"
echo " Welcome to the RemoteRelay Installation Script "
echo "----------------------------------------------------"
echo

# Check for root privileges
if [ "$(id -u)" -ne 0 ]; then
  echo "This script requires superuser privileges. Please run with sudo."
  exit 1
fi

# User Choices
INSTALL_TYPE=""
SERVER_IP=""

while [[ "$INSTALL_TYPE" != "S" && "$INSTALL_TYPE" != "C" && "$INSTALL_TYPE" != "B" ]]; do
  read -p "Choose installation type (S for Server, C for Client, B for Both): " INSTALL_TYPE
  INSTALL_TYPE=$(echo "$INSTALL_TYPE" | tr '[:lower:]' '[:upper:]') # Convert to uppercase
  if [[ "$INSTALL_TYPE" != "S" && "$INSTALL_TYPE" != "C" && "$INSTALL_TYPE" != "B" ]]; then
    echo "Invalid input. Please enter S, C, or B."
  fi
done

if [[ "$INSTALL_TYPE" == "C" || "$INSTALL_TYPE" == "B" ]]; then
  DEFAULT_IP="127.0.0.1"
  if [[ "$INSTALL_TYPE" == "C" ]]; then
    read -p "Enter the Server's IP address: " SERVER_IP
  else # Both Server and Client
    read -p "Enter the Server's IP address (default: $DEFAULT_IP for local server): " USER_INPUT_IP
    SERVER_IP=${USER_INPUT_IP:-$DEFAULT_IP}
  fi
  # Basic IP validation
  if ! [[ "$SERVER_IP" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Invalid IP address format. Exiting."
    exit 1
  fi
fi

echo # Newline for better readability

# --- Server Installation ---
install_server() {
  echo "Starting Server Installation..."

  echo "Creating installation directory: $SERVER_INSTALL_DIR"
  mkdir -p "$SERVER_INSTALL_DIR"
  if [ ! -d "$SERVER_FILES_DIR" ]; then
    echo "Error: Server files directory '$SERVER_FILES_DIR' not found. Make sure it's in the same directory as the installer."
    exit 1
  fi
  echo "Copying server files to $SERVER_INSTALL_DIR..."
  cp -r "$SERVER_FILES_DIR"/* "$SERVER_INSTALL_DIR/"
  chmod +x "$SERVER_INSTALL_DIR/RemoteRelay.Server"

  echo "Creating systemd service file: /etc/systemd/system/remote-relay-server.service"
  cat << EOF > /etc/systemd/system/remote-relay-server.service
[Unit]
Description=RemoteRelay Server
After=network.target

[Service]
ExecStart=$SERVER_INSTALL_DIR/RemoteRelay.Server
WorkingDirectory=$SERVER_INSTALL_DIR
Restart=always
User=pi
Environment=DOTNET_ROOT=/usr/share/dotnet # Adjust if .NET is installed elsewhere

[Install]
WantedBy=multi-user.target
EOF

  echo "Reloading systemd daemon..."
  systemctl daemon-reload
  echo "Enabling remote-relay-server service..."
  systemctl enable remote-relay-server.service
  echo "Starting remote-relay-server service..."
  systemctl start remote-relay-server.service

  # Check service status
  if systemctl is-active --quiet remote-relay-server.service; then
    echo "RemoteRelay Server service is active and running."
  else
    echo "Warning: RemoteRelay Server service failed to start. Check logs with 'journalctl -u remote-relay-server.service'"
  fi
  echo "Server Installation Complete."
  echo
}

# --- Client Installation ---
install_client() {
  echo "Starting Client Installation..."

  echo "Creating installation directory: $CLIENT_INSTALL_DIR"
  mkdir -p "$CLIENT_INSTALL_DIR"
   if [ ! -d "$CLIENT_FILES_DIR" ]; then
    echo "Error: Client files directory '$CLIENT_FILES_DIR' not found. Make sure it's in the same directory as the installer."
    exit 1
  fi
  echo "Copying client files to $CLIENT_INSTALL_DIR..."
  cp -r "$CLIENT_FILES_DIR"/* "$CLIENT_INSTALL_DIR/"
  chmod +x "$CLIENT_INSTALL_DIR/RemoteRelay"

  SERVER_DETAILS_FILE="$CLIENT_INSTALL_DIR/ServerDetails.json"
  if [ -f "$SERVER_DETAILS_FILE" ]; then
    echo "Updating Server IP in $SERVER_DETAILS_FILE to $SERVER_IP..."
    # Using sed as jq might not be universally available.
    # This assumes a simple structure like {"Host": "some_ip", ...}
    # Create a temporary file for sed output
    TMP_SERVER_DETAILS_FILE=$(mktemp)
    sed "s/\(\"Host\":\s*\)\"[^\"]*\"/\1\"$SERVER_IP\"/" "$SERVER_DETAILS_FILE" > "$TMP_SERVER_DETAILS_FILE" && mv "$TMP_SERVER_DETAILS_FILE" "$SERVER_DETAILS_FILE"
    if [ $? -ne 0 ]; then
        echo "Warning: Failed to update Server IP in $SERVER_DETAILS_FILE using sed."
        echo "Please ensure $SERVER_DETAILS_FILE contains a line like: \"Host\": \"OLD_IP_ADDRESS\""
        # Clean up temp file if it still exists
        [ -f "$TMP_SERVER_DETAILS_FILE" ] && rm "$TMP_SERVER_DETAILS_FILE"
    fi
  else
    echo "Warning: $SERVER_DETAILS_FILE not found. Cannot update Server IP."
  fi

  # Determine user's home directory for autostart
  APP_USER="${SUDO_USER:-pi}" # Use SUDO_USER if set, otherwise default to 'pi'
  USER_HOME=$(eval echo ~$APP_USER)
  AUTOSART_DIR="$USER_HOME/.config/autostart"

  echo "Creating autostart directory (if it doesn't exist): $AUTOSART_DIR"
  mkdir -p "$AUTOSART_DIR"
  # Ensure the APP_USER owns the .config directory and its contents if created by root
  chown -R $APP_USER:$APP_USER "$USER_HOME/.config"


  DESKTOP_FILE="$AUTOSART_DIR/remote-relay-client.desktop"
  echo "Creating autostart desktop file: $DESKTOP_FILE"
  cat << EOF > "$DESKTOP_FILE"
[Desktop Entry]
Name=RemoteRelay Client
Exec=$CLIENT_INSTALL_DIR/RemoteRelay
Path=$CLIENT_INSTALL_DIR/
Terminal=false
Type=Application
X-GNOME-Autostart-enabled=true
EOF
  # Set ownership of the desktop file to the user
  chown $APP_USER:$APP_USER "$DESKTOP_FILE"
  chmod 644 "$DESKTOP_FILE" # Standard permissions for .desktop files

  echo "Client autostart configured for user '$APP_USER'."
  echo "Client Installation Complete."
  echo
}

# --- Perform Installations Based on User Choice ---
SUMMARY_MESSAGE="Installation Summary:\n"

if [[ "$INSTALL_TYPE" == "S" || "$INSTALL_TYPE" == "B" ]]; then
  install_server
  SUMMARY_MESSAGE+="  - RemoteRelay Server installed to $SERVER_INSTALL_DIR and configured as a service.\n"
fi

if [[ "$INSTALL_TYPE" == "C" || "$INSTALL_TYPE" == "B" ]]; then
  install_client
  SUMMARY_MESSAGE+="  - RemoteRelay Client installed to $CLIENT_INSTALL_DIR, configured with Server IP $SERVER_IP, and set to autostart.\n"
fi

# Completion Message
echo "----------------------------------------------------"
echo " RemoteRelay Installation Finished! "
echo "----------------------------------------------------"
echo -e "$SUMMARY_MESSAGE"
echo "Please check the output above for any warnings or errors."
echo "If the server was installed, you can check its status with: sudo systemctl status remote-relay-server.service"
echo "If the client was installed, it will attempt to start on the next login of user '$APP_USER'."
echo "If both were installed on this machine, ensure the client is configured with IP $SERVER_IP."

exit 0
