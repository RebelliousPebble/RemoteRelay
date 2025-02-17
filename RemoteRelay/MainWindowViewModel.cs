using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
   public MainWindowViewModel()
   {
      Debug.WriteLine(Guid.NewGuid());

      // Load ServerDetails.json
      var serverInfo = JsonSerializer.Deserialize<ServerDetails>(File.ReadAllText("ServerDetails.json"));

      SwitcherClient.InitializeInstance(new Uri($"http://{serverInfo.Host}:{serverInfo.Port}/relay"));
      SwitcherClient.Instance.RequestSettings();

      Thread.Sleep(2000);

      OperationViewModel = SwitcherClient.Instance.Settings.Outputs.Count == 1
         ? new MultiOutputViewModel()
         : new SingleOutputViewModel(SwitcherClient.Instance.Settings);
      // Get all unique inputs
      // Get all unique inputs
      OperationViewModel = new SingleOutputViewModel(SwitcherClient.Instance.Settings);

      SwitcherClient.Instance.RequestStatus();
   }

   public ViewModelBase OperationViewModel { get; set; }
}