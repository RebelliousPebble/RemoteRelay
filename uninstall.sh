#!/bin/bash

# RemoteRelay Uninstall Script
# This script removes RemoteRelay Server and/or Client installations

# Determine user's home directory
# Use SUDO_USER if set (script run with sudo), otherwise current user
APP_USER="${SUDO_USER:-$(whoami)}"
USER_HOME=$(eval echo ~$APP_USER)

if [ -z "$USER_HOME" ] || [ ! -d "$USER_HOME" ]; then
  echo "Error: Could not determine user's home directory. Exiting."
  exit 1
fi

# Define installation directories
BASE_INSTALL_DIR="$USER_HOME/.local/share/RemoteRelay"
SERVER_INSTALL_DIR="$BASE_INSTALL_DIR/Server"
CLIENT_INSTALL_DIR="$BASE_INSTALL_DIR/Client"

# System service file
SYSTEM_SERVICE_FILE="/etc/systemd/system/remote-relay-server.service"

# Client autostart file
XDG_AUTOSTART_DIR="$USER_HOME/.config/autostart"
DESKTOP_FILE_PATH="$XDG_AUTOSTART_DIR/remote-relay-client.desktop"

# Wayfire configuration
WAYFIRE_INI="$USER_HOME/.config/wayfire.ini"

# Welcome Message
echo "----------------------------------------------------"
echo "    RemoteRelay Uninstall Script    "
echo "----------------------------------------------------"
echo "User: $APP_USER"
echo "Installation directory: $BASE_INSTALL_DIR"
echo

# Check what's installed
SERVER_INSTALLED=false
CLIENT_INSTALLED=false
SYSTEM_SERVICE_EXISTS=false

if [ -d "$SERVER_INSTALL_DIR" ] && [ -f "$SERVER_INSTALL_DIR/RemoteRelay.Server" ]; then
    SERVER_INSTALLED=true
    echo "✓ Server installation found"
fi

if [ -d "$CLIENT_INSTALL_DIR" ] && [ -f "$CLIENT_INSTALL_DIR/RemoteRelay" ]; then
    CLIENT_INSTALLED=true
    echo "✓ Client installation found"
fi

if [ -f "$SYSTEM_SERVICE_FILE" ]; then
    SYSTEM_SERVICE_EXISTS=true
    echo "✓ System service found"
fi

if [ -f "$DESKTOP_FILE_PATH" ]; then
    echo "✓ Client autostart entry found"
fi

if [ "$SERVER_INSTALLED" = false ] && [ "$CLIENT_INSTALLED" = false ] && [ "$SYSTEM_SERVICE_EXISTS" = false ]; then
    echo "No RemoteRelay installations found."
    exit 0
fi

echo

# Ask what to uninstall
UNINSTALL_TYPE=""
if [ "$SERVER_INSTALLED" = true ] && [ "$CLIENT_INSTALLED" = true ]; then
    while [[ "$UNINSTALL_TYPE" != "S" && "$UNINSTALL_TYPE" != "C" && "$UNINSTALL_TYPE" != "B" ]]; do
        read -p "What would you like to uninstall? (S for Server, C for Client, B for Both): " UNINSTALL_TYPE
        UNINSTALL_TYPE=$(echo "$UNINSTALL_TYPE" | tr '[:lower:]' '[:upper:]')
        if [[ "$UNINSTALL_TYPE" != "S" && "$UNINSTALL_TYPE" != "C" && "$UNINSTALL_TYPE" != "B" ]]; then
            echo "Invalid input. Please enter S, C, or B."
        fi
    done
elif [ "$SERVER_INSTALLED" = true ]; then
    echo "Only server installation found."
    read -p "Uninstall server? (y/N): " CONFIRM
    if [[ "$CONFIRM" =~ ^[Yy]$ ]]; then
        UNINSTALL_TYPE="S"
    else
        echo "Uninstall cancelled."
        exit 0
    fi
elif [ "$CLIENT_INSTALLED" = true ]; then
    echo "Only client installation found."
    read -p "Uninstall client? (y/N): " CONFIRM
    if [[ "$CONFIRM" =~ ^[Yy]$ ]]; then
        UNINSTALL_TYPE="C"
    else
        echo "Uninstall cancelled."
        exit 0
    fi
elif [ "$SYSTEM_SERVICE_EXISTS" = true ]; then
    echo "Only system service found (no installation files)."
    read -p "Remove system service? (y/N): " CONFIRM
    if [[ "$CONFIRM" =~ ^[Yy]$ ]]; then
        UNINSTALL_TYPE="S"
    else
        echo "Uninstall cancelled."
        exit 0
    fi
fi

echo

# Check for root privileges if server uninstall is requested
if [[ "$UNINSTALL_TYPE" == "S" || "$UNINSTALL_TYPE" == "B" ]]; then
    if [ "$EUID" -ne 0 ] && [ "$SYSTEM_SERVICE_EXISTS" = true ]; then
        echo "Error: Root privileges required to remove system service."
        echo "Please run this script with sudo to uninstall the server."
        exit 1
    fi
fi

# --- Server Uninstall ---
uninstall_server() {
    echo "Uninstalling RemoteRelay Server..."
    
    # Stop and disable system service
    if [ "$SYSTEM_SERVICE_EXISTS" = true ]; then
        echo "Stopping remote-relay-server service..."
        if systemctl is-active --quiet remote-relay-server.service; then
            systemctl stop remote-relay-server.service
        fi
        
        echo "Disabling remote-relay-server service..."
        if systemctl is-enabled --quiet remote-relay-server.service 2>/dev/null; then
            systemctl disable remote-relay-server.service
        fi
        
        echo "Removing service file: $SYSTEM_SERVICE_FILE"
        rm -f "$SYSTEM_SERVICE_FILE"
        
        echo "Reloading systemd daemon..."
        systemctl daemon-reload
    fi
    
    # Remove server files
    if [ -d "$SERVER_INSTALL_DIR" ]; then
        echo "Removing server files from: $SERVER_INSTALL_DIR"
        rm -rf "$SERVER_INSTALL_DIR"
    fi
    
    echo "Server uninstall complete."
}

# --- Client Uninstall ---
uninstall_client() {
    echo "Uninstalling RemoteRelay Client..."
    
    # Remove autostart entry
    if [ -f "$DESKTOP_FILE_PATH" ]; then
        echo "Removing autostart entry: $DESKTOP_FILE_PATH"
        rm -f "$DESKTOP_FILE_PATH"
    fi
    
    # Remove client files
    if [ -d "$CLIENT_INSTALL_DIR" ]; then
        echo "Removing client files from: $CLIENT_INSTALL_DIR"
        rm -rf "$CLIENT_INSTALL_DIR"
    fi
    
    # Ask about Wayfire configuration cleanup
    if [ -f "$WAYFIRE_INI" ]; then
        echo
        read -p "Remove RemoteRelay kiosk settings from Wayfire configuration? (y/N): " WAYFIRE_CLEANUP
        if [[ "$WAYFIRE_CLEANUP" =~ ^[Yy]$ ]]; then
            echo "Attempting to remove kiosk settings from $WAYFIRE_INI..."
            
            # Create AWK script to remove or reset idle section
            AWK_SCRIPT='
            BEGIN {
                in_idle_section = 0;
                skip_section = 0;
                current_section_lines_buffer = "";
            }
            
            function flush_current_section_buffer() {
                if (!skip_section && current_section_lines_buffer != "") {
                    print current_section_lines_buffer;
                }
                current_section_lines_buffer = "";
                in_idle_section = 0;
                skip_section = 0;
            }
            
            /^\s*\[idle\]\s*$/ {
                flush_current_section_buffer();
                in_idle_section = 1;
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
                    # Skip lines that set timeouts to 0 (our kiosk settings)
                    if ($0 ~ /^\s*(dpms_timeout|screensaver_timeout)\s*=\s*0\s*$/) {
                        next;
                    }
                    # If this is the only content in the idle section, mark for removal
                    if (current_section_lines_buffer == "[idle]" && 
                        ($0 ~ /^\s*(dpms_timeout|screensaver_timeout)\s*=\s*0/ || $0 ~ /^\s*$/)) {
                        skip_section = 1;
                        next;
                    }
                }
                current_section_lines_buffer = (current_section_lines_buffer == "" ? $0 : current_section_lines_buffer ORS $0);
            }
            
            END {
                flush_current_section_buffer();
            }
            '
            
            WAYFIRE_INI_TMP="$WAYFIRE_INI.tmp.$$"
            if awk "$AWK_SCRIPT" "$WAYFIRE_INI" > "$WAYFIRE_INI_TMP"; then
                if [ -s "$WAYFIRE_INI_TMP" ]; then
                    mv "$WAYFIRE_INI_TMP" "$WAYFIRE_INI"
                    echo "Wayfire configuration cleanup complete."
                else
                    echo "Warning: Wayfire configuration cleanup resulted in empty file. Original preserved."
                    rm -f "$WAYFIRE_INI_TMP"
                fi
            else
                echo "Warning: Failed to clean up Wayfire configuration. Original preserved."
                rm -f "$WAYFIRE_INI_TMP"
            fi
        fi
    fi
    
    echo "Client uninstall complete."
}

# --- Perform Uninstall Based on User Choice ---
if [[ "$UNINSTALL_TYPE" == "S" || "$UNINSTALL_TYPE" == "B" ]]; then
    uninstall_server
    echo
fi

if [[ "$UNINSTALL_TYPE" == "C" || "$UNINSTALL_TYPE" == "B" ]]; then
    uninstall_client
    echo
fi

# Clean up base directory if empty
if [ -d "$BASE_INSTALL_DIR" ]; then
    if [ -z "$(ls -A "$BASE_INSTALL_DIR" 2>/dev/null)" ]; then
        echo "Removing empty base directory: $BASE_INSTALL_DIR"
        rmdir "$BASE_INSTALL_DIR"
        
        # Also try to remove parent .local/share if it becomes empty
        PARENT_DIR="$(dirname "$BASE_INSTALL_DIR")"
        if [ -d "$PARENT_DIR" ] && [ -z "$(ls -A "$PARENT_DIR" 2>/dev/null)" ]; then
            rmdir "$PARENT_DIR" 2>/dev/null || true
        fi
    fi
fi

# Final message
echo "----------------------------------------------------"
echo "         Uninstall Complete!         "
echo "----------------------------------------------------"

case "$UNINSTALL_TYPE" in
    "S")
        echo "RemoteRelay Server has been uninstalled."
        echo "- System service removed and disabled"
        echo "- Server files deleted"
        ;;
    "C")
        echo "RemoteRelay Client has been uninstalled."
        echo "- Autostart entry removed"
        echo "- Client files deleted"
        ;;
    "B")
        echo "RemoteRelay Server and Client have been uninstalled."
        echo "- System service removed and disabled"
        echo "- Autostart entry removed"
        echo "- All application files deleted"
        ;;
esac

echo
echo "Thank you for using RemoteRelay!"

exit 0
