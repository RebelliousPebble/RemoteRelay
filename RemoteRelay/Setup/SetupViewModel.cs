using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using RemoteRelay.Common;

namespace RemoteRelay.Setup;

/// <summary>
/// View model for the main setup configuration view.
/// </summary>
public class SetupViewModel : ViewModelBase
{
    private readonly Action _closeAction;
    private AppSettings _originalSettings;

    private int _serverPort = 33101;
    public int ServerPort
    {
        get => _serverPort;
        set => this.RaiseAndSetIfChanged(ref _serverPort, value);
    }

    private string _tcpMirrorAddress = string.Empty;
    public string TcpMirrorAddress
    {
        get => _tcpMirrorAddress;
        set => this.RaiseAndSetIfChanged(ref _tcpMirrorAddress, value);
    }

    private int? _tcpMirrorPort;
    public int? TcpMirrorPort
    {
        get => _tcpMirrorPort;
        set => this.RaiseAndSetIfChanged(ref _tcpMirrorPort, value);
    }

    private int _inactiveRelayPin;
    public int InactiveRelayPin
    {
        get => _inactiveRelayPin;
        set => this.RaiseAndSetIfChanged(ref _inactiveRelayPin, value);
    }

    private string _inactiveRelayState = "High";
    public string InactiveRelayState
    {
        get => _inactiveRelayState;
        set => this.RaiseAndSetIfChanged(ref _inactiveRelayState, value);
    }

    private bool _flashOnSelect = true;
    public bool FlashOnSelect
    {
        get => _flashOnSelect;
        set => this.RaiseAndSetIfChanged(ref _flashOnSelect, value);
    }

    private bool _showIpOnScreen = true;
    public bool ShowIpOnScreen
    {
        get => _showIpOnScreen;
        set => this.RaiseAndSetIfChanged(ref _showIpOnScreen, value);
    }

    private bool _logging = true;
    public bool Logging
    {
        get => _logging;
        set => this.RaiseAndSetIfChanged(ref _logging, value);
    }

    private string _logoFile = string.Empty;
    public string LogoFile
    {
        get => _logoFile;
        set => this.RaiseAndSetIfChanged(ref _logoFile, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private bool _isSaving;
    public bool IsSaving
    {
        get => _isSaving;
        set => this.RaiseAndSetIfChanged(ref _isSaving, value);
    }

    public ObservableCollection<InputConfigViewModel> Inputs { get; } = new();

    public ICommand AddInputCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand TestInactiveRelayOnCommand { get; }
    public ICommand TestInactiveRelayOffCommand { get; }

    public SetupViewModel(AppSettings settings, Action closeAction)
    {
        _closeAction = closeAction;
        _originalSettings = settings;

        LoadSettings(settings);

        AddInputCommand = ReactiveCommand.Create(AddInput);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        CloseCommand = ReactiveCommand.Create(() => _closeAction());
        TestInactiveRelayOnCommand = ReactiveCommand.CreateFromTask(() => 
            SwitcherClient.Instance.TestPinAsync(InactiveRelayPin, InactiveRelayState == "Low", true));
        TestInactiveRelayOffCommand = ReactiveCommand.CreateFromTask(() => 
            SwitcherClient.Instance.TestPinAsync(InactiveRelayPin, InactiveRelayState == "Low", false));
    }

    private void LoadSettings(AppSettings settings)
    {
        ServerPort = settings.ServerPort > 0 ? settings.ServerPort : 33101;
        TcpMirrorAddress = settings.TcpMirrorAddress ?? string.Empty;
        TcpMirrorPort = settings.TcpMirrorPort;
        FlashOnSelect = settings.FlashOnSelect;
        ShowIpOnScreen = settings.ShowIpOnScreen;
        Logging = settings.Logging;
        LogoFile = settings.LogoFile ?? string.Empty;

        if (settings.InactiveRelay != null)
        {
            InactiveRelayPin = settings.InactiveRelay.Pin;
            InactiveRelayState = settings.InactiveRelay.InactiveState;
        }

        // Group routes by source name to create input cards
        var sourceGroups = settings.Routes
            .GroupBy(r => r.SourceName)
            .OrderBy(g => g.Key);

        Inputs.Clear();
        foreach (var group in sourceGroups)
        {
            var inputVm = new InputConfigViewModel(group.Key, DeleteInput);
            
            // Add physical button config if exists
            if (settings.PhysicalSourceButtons?.TryGetValue(group.Key, out var buttonConfig) == true)
            {
                inputVm.PhysicalButtonPin = buttonConfig.PinNumber;
                inputVm.PhysicalButtonTrigger = buttonConfig.TriggerState;
            }

            // Add output routes
            foreach (var route in group)
            {
                var isDefault = settings.DefaultRoutes?.TryGetValue(route.SourceName, out var defaultOutput) == true 
                    && defaultOutput == route.OutputName;
                
                inputVm.OutputRoutes.Add(new RouteConfigViewModel(
                    route.OutputName,
                    route.RelayPin,
                    route.ActiveLow,
                    isDefault,
                    () => inputVm.RemoveRoute));
            }

            Inputs.Add(inputVm);
        }
    }

    private void AddInput()
    {
        var newName = $"Input {Inputs.Count + 1}";
        var inputVm = new InputConfigViewModel(newName, DeleteInput);
        Inputs.Add(inputVm);
    }

    private void DeleteInput(InputConfigViewModel input)
    {
        Inputs.Remove(input);
    }

    private async System.Threading.Tasks.Task SaveAsync()
    {
        IsSaving = true;
        StatusMessage = "Saving configuration...";

        try
        {
            var settings = BuildSettings();
            var response = await SwitcherClient.Instance.SaveConfigurationAsync(settings);

            if (response?.Success == true)
            {
                StatusMessage = "Configuration saved successfully!";
                // Request settings refresh
                SwitcherClient.Instance.RequestSettings();
            }
            else
            {
                StatusMessage = $"Save failed: {response?.Error ?? "Unknown error"}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private AppSettings BuildSettings()
    {
        var settings = new AppSettings
        {
            ServerPort = ServerPort,
            TcpMirrorAddress = string.IsNullOrWhiteSpace(TcpMirrorAddress) ? null : TcpMirrorAddress,
            TcpMirrorPort = TcpMirrorPort,
            FlashOnSelect = FlashOnSelect,
            ShowIpOnScreen = ShowIpOnScreen,
            Logging = Logging,
            LogoFile = LogoFile,
            Routes = new System.Collections.Generic.List<RelayConfig>(),
            DefaultRoutes = new System.Collections.Generic.Dictionary<string, string>(),
            PhysicalSourceButtons = new System.Collections.Generic.Dictionary<string, PhysicalButtonConfig>()
        };

        // Set inactive relay if pin is configured
        if (InactiveRelayPin > 0)
        {
            settings.InactiveRelay = new InactiveRelaySettings
            {
                Pin = InactiveRelayPin,
                InactiveState = InactiveRelayState
            };
        }

        // Build routes and other settings from inputs
        foreach (var input in Inputs)
        {
            // Add physical button config
            if (input.PhysicalButtonPin > 0)
            {
                settings.PhysicalSourceButtons[input.SourceName] = new PhysicalButtonConfig
                {
                    PinNumber = input.PhysicalButtonPin,
                    TriggerState = input.PhysicalButtonTrigger
                };
            }

            // Add routes
            foreach (var route in input.OutputRoutes)
            {
                settings.Routes.Add(new RelayConfig
                {
                    SourceName = input.SourceName,
                    OutputName = route.OutputName,
                    RelayPin = route.RelayPin,
                    ActiveLow = route.ActiveLow
                });

                // Add default route if marked
                if (route.IsDefaultRoute)
                {
                    settings.DefaultRoutes[input.SourceName] = route.OutputName;
                }
            }
        }

        return settings;
    }
}
