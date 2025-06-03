using System;
using System.Diagnostics;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks; // Added for Task
using System.Reactive.Linq; // Added for Rx
using Avalonia.Threading; // Added for AvaloniaScheduler
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
   private const int RetryIntervalSeconds = 5;
   private IDisposable? _retrySubscription;
   private int _retryCountdown;

   private string _serverStatusMessage;
   public string ServerStatusMessage
   {
      get => _serverStatusMessage;
      set => this.RaiseAndSetIfChanged(ref _serverStatusMessage, value);
   }

   private ViewModelBase _operationViewModel;
   public ViewModelBase OperationViewModel
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
       _retrySubscription?.Dispose(); // Dispose any existing subscription

       _retryCountdown = RetryIntervalSeconds;
       UpdateServerStatusMessageForRetry();

       _retrySubscription = Observable.Interval(TimeSpan.FromSeconds(1), AvaloniaScheduler.Instance)
           .Subscribe(async _ =>
           {
               if (_retryCountdown > 0)
               {
                   _retryCountdown--;
                   UpdateServerStatusMessageForRetry();
               }

               if (_retryCountdown <= 0)
               {
                   _retrySubscription?.Dispose();
                   _retrySubscription = null;

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
           });
   }

   private async Task OnConnected()
   {
      _retrySubscription?.Dispose();
      _retrySubscription = null;

      ServerStatusMessage = $"Connected to {SwitcherClient.Instance.ServerUri}. Fetching settings...";
      SwitcherClient.Instance.RequestSettings();
      var settings = await SwitcherClient.Instance.GetSettingsAsync();

      if (settings != null)
      {
         // Corrected logic for OperationViewModel initialization using the local 'settings' variable
         OperationViewModel = settings.Outputs.Count > 1
             ? new MultiOutputViewModel() // Assuming MultiOutputViewModel takes no params or different ones
             : new SingleOutputViewModel(settings);

         SwitcherClient.Instance.RequestStatus();
      }
      else
      {
         ServerStatusMessage = "Failed to retrieve valid settings from server.";
         OperationViewModel = null; // Ensure OpViewModel is null if settings are null
      }
   }
}