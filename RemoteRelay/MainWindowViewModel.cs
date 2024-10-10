using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
    
    private AppSettings _settings;
    private SwitcherServer? _server;
    private SwitcherClient _client;
    public ViewModelBase OperationViewModel { get; set; }
    public MainWindowViewModel()
    {
        //Load settings from config.json
        //_settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));
        //if (_settings.IsServer)
        //{
        //    _server = new SwitcherServer(_settings.Port, _settings);
         //   _settings.ServerName = Dns.GetHostName();
          //  _server.Start();
        //}

        // Connect to the server (regardless of if a server or not)
        //_client = new SwitcherClient(_settings.ServerName, _settings.Port);
        //_client.Connect();
        //OperationViewModel = _settings.Outputs.Count == 1 ? new MultiOutputViewModel(_settings) : new SingleOutputViewModel(_settings);
        // Get all unique inputs
        OperationViewModel = new SingleOutputViewModel(_settings);
    }
}