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

  echo "Creating systemd user service file: $SERVICE_FILE"
  cat << EOF > "$SERVICE_FILE"
[Unit]
Description=RemoteRelay Server (User Service)
After=network.target

[Service]
ExecStart=$SERVER_INSTALL_DIR/RemoteRelay.Server
WorkingDirectory=$SERVER_INSTALL_DIR
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

  # User-level systemd service for client
  USER_SYSTEMD_DIR="$USER_HOME/.config/systemd/user"
  mkdir -p "$USER_SYSTEMD_DIR" # Ensure directory exists
  CLIENT_SERVICE_FILE="$USER_SYSTEMD_DIR/remote-relay-client.service"

  echo "Creating systemd user service file for client: $CLIENT_SERVICE_FILE"

  CLIENT_AFTER_DIRECTIVE=""
  CLIENT_WANTED_BY_DIRECTIVE="graphical-session.target" # Ensures it starts with the graphical session

  if [[ "$INSTALL_TYPE" == "B" ]]; then # If installing Both server and client
    CLIENT_AFTER_DIRECTIVE="remote-relay-server.service $CLIENT_AFTER_DIRECTIVE"
  fi

  cat << EOF > "$CLIENT_SERVICE_FILE"
[Unit]
Description=RemoteRelay Client (User Service)
After=$CLIENT_AFTER_DIRECTIVE

[Service]
ExecStart=$CLIENT_INSTALL_DIR/RemoteRelay
WorkingDirectory=$CLIENT_INSTALL_DIR
Restart=always
Environment="DISPLAY=:0" 
Environment=XAUTHORITY=/home/pi/.Xauthority

[Install]
WantedBy=$CLIENT_WANTED_BY_DIRECTIVE
EOF

  echo "Reloading systemd user daemon..."
  command systemctl --user daemon-reload
  echo "Enabling remote-relay-client user service..."
  command systemctl --user enable remote-relay-client.service
  
  # Attempt to start the service. It might only fully work after next login if GUI elements are involved.
  echo "Attempting to start remote-relay-client user service..."
  command systemctl --user start remote-relay-client.service

  if command systemctl --user is-active --quiet remote-relay-client.service; then
    echo "RemoteRelay Client user service is active."
  else
    echo "Warning: RemoteRelay Client user service may not have started correctly yet (e.g. if graphical session not fully ready)."
    echo "It is enabled and should start automatically on your next login."
    echo "Check status with: journalctl --user -u remote-relay-client.service"
  fi

  echo "Client Installation Complete."
  echo "The RemoteRelay Client is configured as a systemd user service."
  echo "It will attempt to start automatically when you log in."
  echo "To manage the client service, use: systemctl --user [status|start|stop|restart] remote-relay-client.service"
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
  SUMMARY_MESSAGE+="  - RemoteRelay Client installed to $CLIENT_INSTALL_DIR, configured with Server Address $SERVER_ADDRESS, and set up as a systemd user service.\\n"
fi

# Completion Message
echo "----------------------------------------------------"
echo " RemoteRelay Installation Finished! "
echo "----------------------------------------------------"
echo -e "$SUMMARY_MESSAGE"
echo "Please check the output above for any warnings or errors."
echo "User services (both server and client if installed) can be managed with:"
echo "  systemctl --user [status|start|stop|restart] remote-relay-server.service"
echo "  systemctl --user [status|start|stop|restart] remote-relay-client.service"
echo "To enable services to start on boot (even without login, especially for the server), run:"
echo "  sudo loginctl enable-linger $APP_USER"
echo "The client service is configured to start with your graphical session."
echo "If both were installed on this machine, the client is set to start after the server."

exit 0
