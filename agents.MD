# RemoteRelay Project Documentation

## Function
RemoteRelay is a remotely controlled relay switcher designed primarily for radio studios to switch active outputs seamlessly. It consists of a **Server** application running on a Raspberry Pi (controlling physical relays via a HAT) and a **Client** application (Avalonia UI) that connects to the server to view and control the relay state. The client has two UI modes, a single and a multi output mode. In single output, the output is represented by a single button. In multi output mode, the output is represented by a list of buttons.

## Structure
The solution is organized into three main projects:

- **RemoteRelay.sln**: The Visual Studio solution file.
- **RemoteRelay.Common**: A shared class library containing common data models and configuration classes used by both the server and client.
- **RemoteRelay.Server**: An ASP.NET Core application that runs on the Raspberry Pi. It hosts a SignalR hub for real-time communication and manages the GPIO pins for relay control.
- **RemoteRelay (Client)**: An Avalonia UI application that connects to the server. It provides a touch-friendly interface for users to switch relays.

## Key Components

### Server (`RemoteRelay.Server`)
- **`RelayHub.cs`**: The SignalR Hub that handles communication with clients. It receives switch commands and broadcasts state updates.
- **`SwitcherState.cs`**: Manages the current state of the switcher and coordinates relay operations.
- **`Relay.cs`**: Handles the low-level GPIO interactions to control the physical relays.
- **`config.json`**: Configuration file for defining relay pins, sources, and options.
- **`Program.cs`**: The entry point, configuring the web host and services.

### Client (`RemoteRelay`)
- **`SwitcherClient.cs`**: Manages the SignalR connection to the server, handling connection lifecycle and message dispatching.
- **`MainWindowViewModel.cs`**: The main ViewModel for the UI, handling user interactions and binding to the view.
- **`ServerDetails.json`**: Configuration file specifying the server's address.
- **`App.axaml` / `MainWindow.axaml`**: The Avalonia UI definitions.

The SingleOutput folder contains the single output logic and UI, and the MultiOutput folder contains the multi output logic and UI.

### Common (`RemoteRelay.Common`)
- **`AppSettings.cs`**: Shared configuration models.
- **`RelayConfig.cs`**: Definitions for relay configuration objects.

## Build Instructions

The project uses the .NET SDK (targeting .NET 9.0).

### Prerequisites
- .NET 9.0 SDK
- `makeself` (for creating the installer on Linux)

### Building and Publishing

You can build the projects individually using the `dotnet` CLI. The project includes a helper script `create_makeself_installer.sh` for generating a self-extracting installer for Linux ARM64.

#### Manual Build (Linux ARM64)

**Client:**
```bash
dotnet publish "RemoteRelay/RemoteRelay.csproj" --configuration "Release" /p:PublishProfile=FolderProfile -o "./publish/RemoteRelay-Client-linux-arm64"
```

**Server:**
```bash
dotnet publish "RemoteRelay.Server/RemoteRelay.Server.csproj" --configuration "Release" /p:PublishProfile=FolderProfile -o "./publish/RemoteRelay-Server-linux-arm64"
```

#### Creating the Installer
Run the `create_makeself_installer.sh` script from the repository root (requires a Linux environment or WSL):
```bash
./create_makeself_installer.sh
```
This script will:
1. Clean previous builds.
2. Restore dependencies.
3. Build and publish both Client and Server for `linux-arm64`.
4. Package them into a self-extracting script using `makeself`.

### Installation
The project includes an `install.sh` script which is wrapped by the `makeself` installer but can also be used manually if the `server_files` and `client_files` directories are prepared.
