using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using RemoteRelay.Common;

namespace RemoteRelay
{
   public class SwitcherClient
   {
      private static SwitcherClient _instance;
      private static readonly object _lock = new object();
      private HubConnection _connection;
      private AppSettings _settings;

      public Subject<Dictionary<string, string>> _stateChanged = new();

      private SwitcherClient(AppSettings settings)
      {
         _settings = settings;
         var hubURI = new Uri($"http://{settings.ServerName}:{settings.ServerPort}/relay");

         _connection = new HubConnectionBuilder()
             .WithAutomaticReconnect()
             .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
             .WithUrl(hubURI)
             .Build();

         _connection.On<Dictionary<string, string>>("SystemState", (state) =>
         {
            _stateChanged.OnNext(state);
         });

         _connection.StartAsync();
      }

      public static SwitcherClient Instance
      {
         get
         {
            if (_instance == null)
            {
               throw new InvalidOperationException("Instance not initialized. Call InitializeInstance first.");
            }
            return _instance;
         }
      }

      public static void InitializeInstance(AppSettings settings)
      {
         lock (_lock)
         {
            if (_instance == null)
            {
               _instance = new SwitcherClient(settings);
            }
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
   }
}
