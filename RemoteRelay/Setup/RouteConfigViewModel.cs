using System;
using ReactiveUI;
using System.Windows.Input;

namespace RemoteRelay.Setup;

/// <summary>
/// View model for a single output route row in an input card.
/// </summary>
public class RouteConfigViewModel : ViewModelBase
{
    private readonly Func<Action<RouteConfigViewModel>> _getDeleteAction;

    private string _outputName = string.Empty;
    public string OutputName
    {
        get => _outputName;
        set => this.RaiseAndSetIfChanged(ref _outputName, value);
    }

    private int _relayPin;
    public int RelayPin
    {
        get => _relayPin;
        set => this.RaiseAndSetIfChanged(ref _relayPin, value);
    }

    private bool _activeLow = true;
    public bool ActiveLow
    {
        get => _activeLow;
        set => this.RaiseAndSetIfChanged(ref _activeLow, value);
    }

    private bool _isDefaultRoute;
    public bool IsDefaultRoute
    {
        get => _isDefaultRoute;
        set => this.RaiseAndSetIfChanged(ref _isDefaultRoute, value);
    }

    private bool _isRelayOn;
    public bool IsRelayOn
    {
        get => _isRelayOn;
        set
        {
            if (_isRelayOn != value)
            {
                this.RaiseAndSetIfChanged(ref _isRelayOn, value);
                // Send test command when toggle changes
                if (RelayPin > 0)
                {
                    _ = SwitcherClient.Instance.TestPinAsync(RelayPin, ActiveLow, value);
                }
            }
        }
    }

    public ICommand DeleteRouteCommand { get; }

    public RouteConfigViewModel(
        string outputName,
        int relayPin,
        bool activeLow,
        bool isDefaultRoute,
        Func<Action<RouteConfigViewModel>> getDeleteAction)
    {
        _outputName = outputName;
        _relayPin = relayPin;
        _activeLow = activeLow;
        _isDefaultRoute = isDefaultRoute;
        _getDeleteAction = getDeleteAction;

        DeleteRouteCommand = ReactiveCommand.Create(() => _getDeleteAction()(this));
    }
}

