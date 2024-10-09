using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace RemoteRelay;

public class MainWindowViewModel : INotifyPropertyChanged
{
    
    private AppSettings _settings;
    private SwitcherServer _server;
    private SwitcherClient _client;
    public bool multiOutput;
    public event PropertyChangedEventHandler PropertyChanged;
    
    public bool MultiOutput
    {
        get => multiOutput;
        set 
        {
            multiOutput = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MultiOutput)));
        }
    }
    
    public MainWindowViewModel()
    {
        //Load settings from config.json
        JsonSerializer.Deserialize(File.ReadAllText("config.json"), typeof(AppSettings));
        if (_settings.IsServer)
        {
            _server = new SwitcherServer(_settings.Port, _settings);
            _settings.ServerName = Dns.GetHostName();
            _server.Start();
        }

        // Connect to the server (regardless of if a server or not)
        _client = new SwitcherClient(_settings.ServerName, _settings.Port);
        _client.Connect();
        multiOutput = _settings.Outputs > 1;
    }
}