using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Microsoft.AspNetCore.SignalR.Client;
using RemoteRelay.Common;

namespace RemoteRelay;

public class SwitcherClient
{
   private static SwitcherClient _instance;
   private static readonly object _lock = new();
   private readonly HubConnection _connection;
   private AppSettings? _settings;

   public Subject<Dictionary<string, string>> _stateChanged = new();

   private SwitcherClient(Uri hubUri)
   {
      _connection = new HubConnectionBuilder()
         .WithAutomaticReconnect()
         .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
         .WithUrl(hubUri)
         .Build();

      _connection.On<Dictionary<string, string>>("SystemState", state => { _stateChanged.OnNext(state); });

      _connection.On<AppSettings>("Configuration", settings => _settings = settings);

      _connection.StartAsync();
   }

   public static SwitcherClient Instance
   {
      get
      {
         if (_instance == null)
            throw new InvalidOperationException("Instance not initialized. Call InitializeInstance first.");
         return _instance;
      }
   }

   public AppSettings Settings
   {
      get
      {
         if (_settings == null)
            throw new InvalidOperationException("Settings not gathered from server. Call RequestSettings first.");
         return _settings.Value;
      }
   }

   public static void InitializeInstance(Uri hubUri)
   {
      lock (_lock)
      {
         if (_instance == null) _instance = new SwitcherClient(hubUri);
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
      _connection.SendAsync("GetConfiguration");
   }
}