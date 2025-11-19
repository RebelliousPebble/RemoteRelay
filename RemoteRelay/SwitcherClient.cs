using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks; // Added for TaskCompletionSource
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RemoteRelay.Common;

namespace RemoteRelay;

public class SwitcherClient
{
   private static SwitcherClient? _instance;
   private static readonly object _lock = new();
   private readonly object _requestLock = new();
   private readonly HubConnection _connection;
   private AppSettings? _settings;
   private Uri _hubUri;
   private TaskCompletionSource<AppSettings?> _settingsTcs;
   private readonly ReplaySubject<AppSettings> _settingsChanged = new(1);

   public Subject<Dictionary<string, string>> _stateChanged = new();
   public Subject<bool> _connectionStateChanged = new();

   private SwitcherClient(Uri hubUri)
   {
      _hubUri = hubUri;
      _settingsTcs = new TaskCompletionSource<AppSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
      _connection = new HubConnectionBuilder()
         .WithAutomaticReconnect()
         .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
         .WithUrl(hubUri)
         .AddJsonProtocol(options =>
         {
            options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
         })
         .Build();

      // Handle connection state changes
      _connection.Closed += OnConnectionClosed;
      _connection.Reconnecting += OnConnectionReconnecting;
      _connection.Reconnected += OnConnectionReconnected;

      _connection.On<Dictionary<string, string>>("SystemState", state => { _stateChanged.OnNext(state); });

      _connection.On<AppSettings>("Configuration", settings =>
      {
         _settings = settings;
         _settingsTcs.TrySetResult(settings); // Complete the TCS with the received settings
         _settingsChanged.OnNext(settings);
      });
   }

   public Uri ServerUri => _hubUri;
   public bool IsConnected => _connection.State == HubConnectionState.Connected;

   public static SwitcherClient Instance
   {
      get
      {
         if (_instance == null)
            throw new InvalidOperationException("Instance not initialized. Call InitializeInstance first.");
         return _instance;
      }
   }

   public AppSettings? Settings
   {
      get => _settings;
   }

   public IObservable<AppSettings> SettingsUpdates => _settingsChanged.AsObservable();

   public static void InitializeInstance(Uri hubUri)
   {
      lock (_lock)
      {
         // if (_instance == null) _instance = new SwitcherClient(hubUri);
         if (_instance == null)
            _instance = new SwitcherClient(hubUri);
      }
   }

   public async System.Threading.Tasks.Task<bool> ConnectAsync()
   {
      if (IsConnected)
         return true;

      try
      {
         await _connection.StartAsync();
         
         // Small delay to ensure connection is fully established
         await Task.Delay(100);
         
         return true;
      }
      catch (System.Net.Http.HttpRequestException)
      {
         return false;
      }
      catch (Exception)
      {
         return false;
      }
   }

   public void SwitchSource(string source, string output)
   {
      if (!IsConnected)
      {
         System.Diagnostics.Debug.WriteLine("Cannot switch source: connection is not active");
         return;
      }

      try
      {
         _connection.SendAsync("SwitchSource", source, output);
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"Error sending SwitchSource command: {ex.Message}");
      }
   }

   public void RequestStatus()
   {
      if (!IsConnected)
      {
         System.Diagnostics.Debug.WriteLine("Cannot request status: connection is not active");
         return;
      }

      try
      {
         _connection.SendAsync("GetSystemState");
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"Error sending GetSystemState command: {ex.Message}");
      }
   }

   public void RequestSettings()
   {
      if (!IsConnected)
      {
         System.Diagnostics.Debug.WriteLine("Cannot request settings: connection is not active");
         return;
      }

      try
      {
         // Reset TCS for cases where settings might be requested again
         lock (_requestLock)
         {
            if (_settingsTcs.Task.IsCompleted)
            {
               _settingsTcs = new TaskCompletionSource<AppSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
         }
         // _settings = null; // Removed to prevent null reference exceptions in UI binding
         _connection.SendAsync("GetConfiguration");
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"Error sending GetConfiguration command: {ex.Message}");
         _settingsTcs.TrySetException(ex);
      }
   }

   public Task<AppSettings?> GetSettingsAsync()
   {
      lock (_requestLock)
      {
         return _settingsTcs.Task;
      }
   }

   private Task OnConnectionClosed(Exception? exception)
   {
      System.Diagnostics.Debug.WriteLine($"Connection closed. Exception: {exception?.Message}");
      _connectionStateChanged.OnNext(false);
      return Task.CompletedTask;
   }

   private Task OnConnectionReconnecting(Exception? exception)
   {
      System.Diagnostics.Debug.WriteLine($"Connection reconnecting. Exception: {exception?.Message}");
      _connectionStateChanged.OnNext(false);
      return Task.CompletedTask;
   }

   private Task OnConnectionReconnected(string? connectionId)
   {
      System.Diagnostics.Debug.WriteLine($"Connection reconnected. Connection ID: {connectionId}");
      _connectionStateChanged.OnNext(true);
      
      // Re-request settings and status after reconnection
      try
      {
         RequestSettings();
         RequestStatus();
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"Error re-requesting data after reconnection: {ex.Message}");
      }
      
      return Task.CompletedTask;
   }
}