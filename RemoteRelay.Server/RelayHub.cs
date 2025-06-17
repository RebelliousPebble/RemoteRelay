using Microsoft.AspNetCore.SignalR;

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
}