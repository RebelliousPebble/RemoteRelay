using System.IO;
using System.Net;
using System.Text.Json;
using RemoteRelay.MultiOutput;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
    private readonly SwitcherServer? _server;

    private readonly AppSettings _settings;

    public MainWindowViewModel()
    {
        //Load settings from config.json
        _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));
        _server = new SwitcherServer(_settings.Port, _settings);
        _settings.ServerName = Dns.GetHostName();
        _server.Start();


        OperationViewModel = _settings.Outputs.Count == 1
            ? new MultiOutputViewModel()
            : new SingleOutputViewModel(_settings);
        // Get all unique inputs
        OperationViewModel = new SingleOutputViewModel(_settings);
    }

    public ViewModelBase OperationViewModel { get; set; }
}