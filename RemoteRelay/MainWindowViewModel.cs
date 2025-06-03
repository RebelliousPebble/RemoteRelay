using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ReactiveUI;
using RemoteRelay.Common;
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
   private const int RetryIntervalSeconds = 5;
   private System.Timers.Timer? _retryTimer;
   private int _retryCountdown;

   private string _serverStatusMessage = string.Empty;
   public string ServerStatusMessage
   {
      get => _serverStatusMessage;
      set => this.RaiseAndSetIfChanged(ref _serverStatusMessage, value);
   }

   private ViewModelBase? _operationViewModel;
   public ViewModelBase? OperationViewModel
   {
      get => _operationViewModel;
      set
      {
         this.RaiseAndSetIfChanged(ref _operationViewModel, value);
         this.RaisePropertyChanged(nameof(IsOperationViewReady));
      }
   }

   public bool IsOperationViewReady => _operationViewModel != null;

   public MainWindowViewModel()
   {
      Debug.WriteLine(Guid.NewGuid());

      // Load ServerDetails.json
      var serverInfo = JsonSerializer.Deserialize<ServerDetails>(File.ReadAllText("ServerDetails.json"));
      var serverUri = new Uri($"http://{serverInfo.Host}:{serverInfo.Port}/relay");
      SwitcherClient.InitializeInstance(serverUri);

      _ = InitializeConnectionAsync();
   }

   private async Task InitializeConnectionAsync()
   {
      if (await SwitcherClient.Instance.ConnectAsync())
      {
         await OnConnected();
      }
      else
      {
         StartRetryTimer();
      }
   }

   private void UpdateServerStatusMessageForRetry()
   {
       ServerStatusMessage = $"Server offline. Trying to connect to {SwitcherClient.Instance.ServerUri}. Retrying in {_retryCountdown}s...";
   }

   private void StartRetryTimer()
   {
       _retryTimer?.Stop();
       _retryTimer?.Dispose();

       _retryCountdown = RetryIntervalSeconds;
       UpdateServerStatusMessageForRetry();

       _retryTimer = new System.Timers.Timer(1000); // 1 second interval
       _retryTimer.Elapsed += async (sender, e) =>
       {
           if (_retryCountdown > 0)
           {
               _retryCountdown--;
               UpdateServerStatusMessageForRetry();
           }

           if (_retryCountdown <= 0)
           {
               _retryTimer?.Stop();
               _retryTimer?.Dispose();
               _retryTimer = null;

               ServerStatusMessage = $"Server offline. Trying to connect to {SwitcherClient.Instance.ServerUri}. Retrying now...";

               bool connected = await SwitcherClient.Instance.ConnectAsync();
               if (connected)
               {
                   await OnConnected();
               }
               else
               {
                   StartRetryTimer();
               }
           }
       };
       _retryTimer.Start();
   }

   private async Task OnConnected()
   {
      _retryTimer?.Stop();
      _retryTimer?.Dispose();
      _retryTimer = null;

      ServerStatusMessage = $"Connected to {SwitcherClient.Instance.ServerUri}. Fetching settings...";
      SwitcherClient.Instance.RequestSettings();
      var settings = await SwitcherClient.Instance.GetSettingsAsync();

      if (settings != null)
      {
         // Corrected logic for OperationViewModel initialization using the local 'settings' variable
         OperationViewModel = settings.Value.Outputs.Count > 1
             ? new MultiOutputViewModel() // Assuming MultiOutputViewModel takes no params or different ones
             : new SingleOutputViewModel(settings.Value);

         SwitcherClient.Instance.RequestStatus();
      }
      else
      {
         ServerStatusMessage = "Failed to retrieve valid settings from server.";
         // OperationViewModel can be set to null since it's now nullable
      }
   }
}