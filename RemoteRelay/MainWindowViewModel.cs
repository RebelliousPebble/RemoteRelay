using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    public MainWindowViewModel()
    {
        Debug.WriteLine(Guid.NewGuid());
        
        //Load settings from config.json
        _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));
        _settings.ServerName = Dns.GetHostName();
        
        SwitcherServer.Instance(_settings.Port, _settings).Start();
        
        OperationViewModel = _settings.Outputs.Count == 1
            ? new MultiOutputViewModel()
            : new SingleOutputViewModel(_settings);
        // Get all unique inputs
        OperationViewModel = new SingleOutputViewModel(_settings);
    }

    public ViewModelBase OperationViewModel { get; set; }
}