using Microsoft.AspNetCore.SignalR;
using RemoteRelay.Common;

namespace RemoteRelay.Server;

public class RelayHub : Hub
{
    private readonly SwitcherState _switcherState;

    public RelayHub(SwitcherState switcherState)
    {
        _switcherState = switcherState;
    }

    public override async Task OnConnectedAsync()
    {
        // Send current system state to newly connected client
        var state = _switcherState.GetSystemState();
        await Clients.Caller.SendAsync("SystemState", state);
        await base.OnConnectedAsync();
    }

    public async Task SwitchSource(string sourceName, string outputName)
    {
        if (sourceName != null)
        {
            _switcherState.SwitchSource(sourceName, outputName);
            //Sends the SystemState nessage to all clients
            await GetSystemState();
        }
    }

    public async Task GetSystemState()
    {
        var state = _switcherState.GetSystemState();
        await Clients.All.SendAsync("SystemState", state);
    }

    public async Task GetConfiguration()
    {
        await Clients.Caller.SendAsync("Configuration", _switcherState.GetSettings());
    }

    public async Task<HandshakeResponse> Handshake(string clientVersion)
    {
        var serverVersionStr = VersionHelper.GetVersion();
        var response = new HandshakeResponse
        {
            ServerVersion = serverVersionStr,
            Status = CompatibilityStatus.Compatible
        };

        if (System.Version.TryParse(clientVersion, out var cVer) &&
            System.Version.TryParse(serverVersionStr, out var sVer))
        {
            if (cVer < sVer)
            {
                response.Status = CompatibilityStatus.ClientOutdated;
                response.Message = "Client is outdated. Please update.";
            }
            else if (cVer > sVer)
            {
                response.Status = CompatibilityStatus.ServerOutdated; // Optional handling
            }
        }
        else
        {
            // Fallback if parsing fails - assume compatible or warn? 
            // For now, let's assume compatible if we can't curb version, or maybe warning.
            // Actually, let's leave it as Compatible but maybe log it?
        }

        return response;
    }
}