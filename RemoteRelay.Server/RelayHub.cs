using Microsoft.AspNetCore.SignalR;

namespace RemoteRelay.Server
{
   public class RelayHub : Hub
   {

      private readonly SwitcherState _switcherState;
      private readonly List<Source> _sources;

      public RelayHub(SwitcherState switcherState)
      {
         _switcherState = switcherState;
      }
      public async Task SwitchSource(string sourceName, string outputName)
      {
         var source = _sources.FirstOrDefault(x => x._sourceName == sourceName);
         if (source != null)
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


   }
}
