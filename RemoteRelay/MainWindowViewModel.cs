using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using RemoteRelay.Common;
using RemoteRelay.MultiOutput;
using RemoteRelay.Setup;
using RemoteRelay.SingleOutput;

namespace RemoteRelay;

public class MainWindowViewModel : ViewModelBase
{
    private const int RetryIntervalSeconds = 5;
    private System.Timers.Timer? _retryTimer;
    private int _retryCountdown;
    private ClientConfig _clientConfig;
    private const string ConfigFileName = "ClientConfig.json";
    private AppSettings? _currentSettings;

    private string _serverStatusMessage = string.Empty;
    public string ServerStatusMessage
    {
        get => _serverStatusMessage;
        set => this.RaiseAndSetIfChanged(ref _serverStatusMessage, value);
    }

    private string _filterStatusMessage = string.Empty;
    public string FilterStatusMessage
    {
        get => _filterStatusMessage;
        set => this.RaiseAndSetIfChanged(ref _filterStatusMessage, value);
    }

    private string _updateMessage = string.Empty;
    public string UpdateMessage
    {
        get => _updateMessage;
        set => this.RaiseAndSetIfChanged(ref _updateMessage, value);
    }

    private ViewModelBase? _operationViewModel;
    public ViewModelBase? OperationViewModel
    {
        get => _operationViewModel;
        set
        {
            if (ReferenceEquals(_operationViewModel, value))
            {
                return;
            }

            if (_operationViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }

            this.RaiseAndSetIfChanged(ref _operationViewModel, value);
            this.RaisePropertyChanged(nameof(IsOperationViewReady));
        }
    }

    public bool IsOperationViewReady => _operationViewModel != null;

    private bool _showSetupButton;
    public bool ShowSetupButton
    {
        get => _showSetupButton;
        set => this.RaiseAndSetIfChanged(ref _showSetupButton, value);
    }

    public ICommand OpenSetupCommand { get; }

    public MainWindowViewModel()
    {
        Debug.WriteLine(Guid.NewGuid());

        LoadOrMigrateConfig();

        // Setup button is only shown when connected to localhost
        ShowSetupButton = _clientConfig.IsLocalhost;

        OpenSetupCommand = ReactiveCommand.Create(OpenSetup);

        var serverUri = new Uri($"http://{_clientConfig.Host}:{_clientConfig.Port}/relay");
        SwitcherClient.InitializeInstance(serverUri);

        SwitcherClient.Instance.SettingsUpdates
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(settings =>
            {
                _currentSettings = settings;
                // Only apply if we're not in setup mode
                if (OperationViewModel is not SetupViewModel)
                {
                    ApplySettings(settings);
                }
                ServerStatusMessage = $"Connected to {SwitcherClient.Instance.ServerUri} (settings refreshed at {DateTime.Now:T})";
            });

        SwitcherClient.Instance.CompatibilityUpdates
           .ObserveOn(RxApp.MainThreadScheduler)
           .Subscribe(status =>
           {
               switch (status)
               {
                   case CompatibilityStatus.ClientOutdated:
                       UpdateMessage = "Client Update Available - Please Run Update";
                       break;
                   case CompatibilityStatus.ServerOutdated:
                       UpdateMessage = "Server Version Unknown/Outdated";
                       break;
                   default:
                       UpdateMessage = string.Empty;
                       break;
               }
           });

        // Subscribe to connection state changes
        SwitcherClient.Instance._connectionStateChanged.Subscribe(isConnected =>
        {
            if (!isConnected)
            {
                OperationViewModel = null;
                StartRetryTimer();
            }
        });

        _ = InitializeConnectionAsync();
    }

    private void OpenSetup()
    {
        if (_currentSettings.HasValue)
        {
            OperationViewModel = new SetupViewModel(_currentSettings.Value, CloseSetup);
        }
    }

    private void CloseSetup()
    {
        if (_currentSettings.HasValue)
        {
            ApplySettings(_currentSettings.Value);
        }
    }

    private void LoadOrMigrateConfig()
    {
        if (File.Exists(ConfigFileName))
        {
            try
            {
                var json = File.ReadAllText(ConfigFileName);
                _clientConfig = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config: {ex.Message}");
                _clientConfig = new ClientConfig();
            }
        }
        else if (File.Exists("ServerDetails.json"))
        {
            try
            {
                // Migrate from ServerDetails
                var serverInfo = JsonSerializer.Deserialize<ServerDetails>(File.ReadAllText("ServerDetails.json"));
                _clientConfig = new ClientConfig
                {
                    Host = serverInfo.Host,
                    Port = serverInfo.Port
                };
                SaveConfig();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error migrating config: {ex.Message}");
                _clientConfig = new ClientConfig();
            }
        }
        else
        {
            _clientConfig = new ClientConfig();
            SaveConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_clientConfig, options);
            File.WriteAllText(ConfigFileName, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving config: {ex.Message}");
        }
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
            ApplySettings(settings.Value);
            ServerStatusMessage = $"Connected to {SwitcherClient.Instance.ServerUri}";

            SwitcherClient.Instance.RequestStatus();
        }
        else
        {
            ServerStatusMessage = "Failed to retrieve valid settings from server.";
            // OperationViewModel can be set to null since it's now nullable
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        // Auto-populate filters if empty
        bool configUpdated = false;
        if (_clientConfig.ShownInputs == null)
        {
            _clientConfig.ShownInputs = settings.Sources.ToList();
            configUpdated = true;
        }
        if (_clientConfig.ShownOutputs == null)
        {
            _clientConfig.ShownOutputs = settings.Outputs.ToList();
            configUpdated = true;
        }

        if (configUpdated)
        {
            SaveConfig();
        }

        // Filter routes
        var filteredRoutes = settings.Routes.Where(r =>
           (_clientConfig.ShownInputs?.Contains(r.SourceName) ?? true) &&
           (_clientConfig.ShownOutputs?.Contains(r.OutputName) ?? true)
        ).ToList();

        var filteredSettings = settings;
        filteredSettings.Routes = filteredRoutes;

        // Determine filter status
        bool isFiltered =
           (_clientConfig.ShownInputs != null && _clientConfig.ShownInputs.Count < settings.Sources.Count) ||
           (_clientConfig.ShownOutputs != null && _clientConfig.ShownOutputs.Count < settings.Outputs.Count);

        FilterStatusMessage = isFiltered ? "Filtered" : "All";

        OperationViewModel = filteredSettings.Outputs.Count > 1
           ? new MultiOutputViewModel(filteredSettings, FilterStatusMessage)
           : new SingleOutputViewModel(filteredSettings);
    }

    private class ServerDetails
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 33101;
    }
}