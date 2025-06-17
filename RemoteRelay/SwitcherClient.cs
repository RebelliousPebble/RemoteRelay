using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks; // Added for TaskCompletionSource
using Microsoft.AspNetCore.SignalR.Client;
using RemoteRelay.Common;

namespace RemoteRelay;

public class SwitcherClient
{
   private static SwitcherClient? _instance;
   private static readonly object _lock = new();
   private readonly HubConnection _connection;
   private AppSettings? _settings;
   private Uri _hubUri;
   private bool _isConnected;
   private TaskCompletionSource<AppSettings?> _settingsTcs;

   public Subject<Dictionary<string, string>> _stateChanged = new();

   private SwitcherClient(Uri hubUri)
   {
      _hubUri = hubUri;
      _isConnected = false;
      _settingsTcs = new TaskCompletionSource<AppSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
      _connection = new HubConnectionBuilder()
         .WithAutomaticReconnect()
         .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
         .WithUrl(hubUri)
         .Build();

      _connection.On<Dictionary<string, string>>("SystemState", state => { _stateChanged.OnNext(state); });

      _connection.On<AppSettings>("Configuration", settings =>
      {
         _settings = settings;
         _settingsTcs.TrySetResult(settings); // Complete the TCS with the received settings
      });
   }

   public Uri ServerUri => _hubUri;
   public bool IsConnected => _isConnected;

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
      if (_isConnected)
         return true;

      try
      {
         await _connection.StartAsync();
         _isConnected = true;
         
         // Small delay to ensure connection is fully established
         await Task.Delay(100);
         
         return true;
      }
      catch (System.Net.Http.HttpRequestException)
      {
         _isConnected = false;
         return false;
      }
      catch (Exception)
      {
         _isConnected = false;
         return false;
      }
   }

   public void SwitchSource(string source, string output)
   {
      _connection.SendAsync("SwitchSource", source, output);
   }

   public void RequestStatus()
   {
      _connection.SendAsync("GetSystemState");
   }

   public void RequestSettings()
   {
      // Reset TCS for cases where settings might be requested again
      if (_settingsTcs.Task.IsCompleted)
      {
         _settingsTcs = new TaskCompletionSource<AppSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
      }
      _settings = null; // Clear previous settings before requesting new ones
      _connection.SendAsync("GetConfiguration");
   }

   public Task<AppSettings?> GetSettingsAsync()
   {
      return _settingsTcs.Task;
   }
}