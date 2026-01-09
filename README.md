# RemoteRelay
A remotely controlled relay switcher, primarily designed for switching the active output in a radio studio seamlessly. 

## Architecture
### Server
The server is designed to run on Raspberry Pi, and should work on the entire suite of devices from Pi 1 through to Pi 5 as long as there is a compatible relay HAT plugged in. For example, this https://www.waveshare.com/rpi-relay-board.htm works great on the Pi 5.
The server deals with all GPI operations and holds the state of the system. Client-Server communication is performed via SignalR across the network. Upon receiving a switch command, the system will switch the relay that is active on the HAT, before then updating all connected clients to show the new state
### Client
A platform-agnostic client built in Avalonia, that so far has been tested on Windows and Linux. This app connects to a running server, downloads the configuration file, and then renders the UI based on the system setup. 

![image](img/ui.png)

The client UI is designed for a pi connected to a touch screen, with the idea that a small touch screen can be installed in a studio for easy access and use.

## Easy Installation (Recommended)
For Raspberry Pi users, a self-extracting installer is available that simplifies the setup of the Server, Client, or Both.

1.  **Download the Installer:**
    Go to the [GitHub Releases page](https://github.com/YOUR_USERNAME/YOUR_REPOSITORY/releases) for this project.
    Download the `remote-relay-installer.sh` file from the latest release. You can use `wget` for this. For example:
    ```bash
    wget https://github.com/YOUR_USERNAME/YOUR_REPOSITORY/releases/latest/download/remote-relay-installer.sh
    ```
    (Replace `YOUR_USERNAME/YOUR_REPOSITORY` with the actual path to this repository if you are not the owner reading this).

2.  **Make it Executable:**
    ```bash
    chmod +x remote-relay-installer.sh
    ```

3.  **Run the Installer:**
    The installer requires superuser privileges to set up services and install files in system directories.
    ```bash
    sudo ./remote-relay-installer.sh
    ```
    The script will guide you through the installation process:
    *   You'll be asked if you want to install the **S**erver, **C**lient, or **B**oth.
    *   If you choose to install the Client (or Both), you'll be prompted for the Server's IP address.
    *   The installer will automatically copy the necessary files, set permissions, and configure autostart (systemd for the server, desktop autostart for the client).

## Setup
For the easiest setup on a Raspberry Pi, please see the 'Easy Installation (Recommended)' section above. The following instructions are for manual setup or for understanding the components.

### Server
- Image a Pi with the latest Raspberry Pi OS desktop image (for a server only system, Lite should be fine, but you can run the client and server on the same physical system)
- Build and Publish RemoteRelay.Server (there are publish tasks currently for linux-arm64)
- Copy the RemoteRelay.Server application to the Pi
- Modify the config.json file
	- Add correct definitions for the sources/options
	- Set the correct pin numbers (logical addressing is used)
- chmod +x the server binary
- Run the server application
- (Optional) Create a service for the server so it survives reboots (Handled by the easy installer)

### Client 
- Image a Pi with the latest Raspberry Pi OS desktop image 
- Build and Publish RemoteRelay
- Copy the published RemoteRelay application to the Pi
- Set the server details in ServerDetails.json
- chmod +x the client binary
- Run the client application
- (Optional) Set the application to launch when the pi boots up (Handled by the easy installer)

## To-Do
- Add support for sending a TCP message to a different service on switch (this could be for triggering a Hot Spare switch in Zetta, a Smart Transfer in Myriad, or for updating digital studio signage)
- Add support for multiple servers (Because one relay HAT isn't enough!)
- Add a pin lock so the system can only be used by those who know a code (ie presenters only)

## Inactive Relay Feature

This feature is designed to ensure that a specific relay (designated as the "inactive relay") is set to a predefined safe state when the RemoteRelay server application is not actively running. This is particularly useful to prevent unintended equipment states if the server application crashes, is stopped for maintenance, or before it has fully started.

The server application itself, when running, will typically set this relay to the *opposite* of its configured `InactiveState`, effectively making it an "active" indicator or ensuring a piece of equipment is explicitly on/off only when the server is managing the relays.

### Configuration

To use this feature, add the `InactiveRelay` section to your `RemoteRelay.Server/config.json` file:

```json
{
  // ... other settings ...
  "ServerPort": 33101,
  "InactiveRelay": {
    "Pin": 12,
    "InactiveState": "High"
  },
  "FlashOnSelect": true,
  // ... other settings ...
}
```

*   **`Pin`**: (Integer) The GPIO pin number (using logical/BCM numbering scheme) that will be controlled as the inactive relay.
*   **`InactiveState`**: (String) The desired state for this pin when the RemoteRelay server is *not* running. Accepts `"High"` or `"Low"`. The server application, upon startup, will attempt to set this pin to the opposite state (e.g., if `InactiveState` is "High", the server will set it to "Low" during its active operation if this pin isn't used for other routes).

### Systemd Integration (via `install.sh`)

The `install.sh` script enhances this feature by integrating it with the systemd service for `RemoteRelay.Server`.
*   **`ExecStartPre`**: Before the main server process starts, a command is run to set the inactive relay pin to its configured `InactiveState`.
*   **`ExecStopPost`**: After the main server process stops (or crashes), a command is run to set the inactive relay pin back to its `InactiveState`.

**Important:** For this systemd integration to work, the `jq` command-line JSON processor must be installed on the system where `install.sh` is run. If `jq` is not found, the `install.sh` script will print a warning, and this specific systemd enhancement (the `ExecStartPre` and `ExecStopPost` commands) for the Inactive Relay feature will be skipped, though the server will still manage the pin during its runtime if configured. You can typically install `jq` using `sudo apt install jq` on Debian/Ubuntu systems.

### Testing the Inactive Relay

To verify the Inactive Relay feature is working correctly, especially with systemd:

1.  **Initial State Check (Server Stopped):**
    *   Ensure the `RemoteRelay.Server` service is stopped: `systemctl --user stop remote-relay-server.service`.
    *   Manually check the physical state of the GPIO pin (e.g., with `raspi-gpio get <pin_number>` or a multimeter). It should match the `InactiveState` defined in `config.json` if the `ExecStopPost` command from a previous run or the `ExecStartPre` from the installer has set it.
2.  **Server Start:**
    *   Start the server: `systemctl --user start remote-relay-server.service`.
    *   The `ExecStartPre` command *should* have set the pin to `InactiveState` just before the main process. The main server process, once fully initialized, will then set it to the *active* state (opposite of `InactiveState`).
    *   Check the pin's physical state. It should now be the *opposite* of `InactiveState`.
3.  **Server Stop:**
    *   Stop the server: `systemctl --user stop remote-relay-server.service`.
    *   The `ExecStopPost` command should run, setting the pin back to `InactiveState`.
    *   Check the pin's physical state. It should match `InactiveState`.
4.  **Simulate Crash (Optional/Advanced):**
    *   If the server is running, find its process ID (PID) and kill it with `kill -9 <PID>`.
    *   Systemd should detect the crash and execute `ExecStopPost`, setting the pin to `InactiveState`. Check the physical state.
    *   Restart the service: `systemctl --user start remote-relay-server.service` to ensure it comes back up correctly.
