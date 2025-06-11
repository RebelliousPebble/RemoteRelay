using System.Device.Gpio;

namespace RemoteRelay.Common;

public class PhysicalButtonConfig
{
    public int PinNumber { get; set; }

    private string _triggerState = "Low"; // Default value
    public string TriggerState
    {
        get => _triggerState;
        private set // Private setter for validation
        {
            if (string.Equals(value, "High", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase))
            {
                _triggerState = value;
            }
            else
            {
                throw new ArgumentException("TriggerState must be either \"High\" or \"Low\".");
            }
        }
    }

    public PinValue GetTriggerPinValue() => _triggerState.Equals("Low", StringComparison.OrdinalIgnoreCase) ? PinValue.Low : PinValue.High;

    public PinEventTypes GetTriggerEventType() => _triggerState.Equals("Low", StringComparison.OrdinalIgnoreCase) ? PinEventTypes.Falling : PinEventTypes.Rising;
}

public class InactiveRelaySettings
{
    public int Pin { get; set; }

    private string _inactiveState = "High"; // Default value
    public string InactiveState
    {
        get => _inactiveState;
        set
        {
            if (string.Equals(value, "High", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase))
            {
                _inactiveState = value;
            }
            else
            {
                throw new ArgumentException("InactiveState must be either \"High\" or \"Low\".");
            }
        }
    }

    public PinValue GetInactivePinValue() => _inactiveState.Equals("High", StringComparison.OrdinalIgnoreCase) ? PinValue.High : PinValue.Low;
    public PinValue GetActivePinValue() => _inactiveState.Equals("High", StringComparison.OrdinalIgnoreCase) ? PinValue.Low : PinValue.High;
}

[Serializable]
public struct AppSettings
{
    //Sources
    public List<RelayConfig> Routes { get; set; }
    public string? DefaultSource { get; set; }
    public Dictionary<string, PhysicalButtonConfig> PhysicalSourceButtons { get; set; }
    // Note: Sources and Outputs are expression-bodied members and don't need initialization here.

    //Communication
    public int ServerPort { get; set; }
    public string? TcpMirrorAddress { get; set; }
    public int? TcpMirrorPort { get; set; }

    //Options
    public InactiveRelaySettings? InactiveRelay { get; set; }
    public bool FlashOnSelect { get; set; }
    public bool ShowIpOnScreen { get; set; }
    public bool Logging { get; set; }
    public string LogoFile { get; set; }

    // Parameterless constructor for struct initialization
    public AppSettings()
    {
        PhysicalSourceButtons = new Dictionary<string, PhysicalButtonConfig>();
        Routes = new List<RelayConfig>();
        LogoFile = string.Empty;
        // DefaultSource, TcpMirrorAddress are nullable strings (default to null)
        // ServerPort, TcpMirrorPort are value types (default to 0 or null)
        // InactiveRelay is a nullable struct (defaults to null)
        // Booleans (FlashOnSelect, ShowIpOnScreen, Logging) default to false.
        // Sources and Outputs are computed properties.
    }

   public IReadOnlyCollection<string> Sources => Routes.Select(x => x.SourceName).Distinct().ToArray();
   public IReadOnlyCollection<string> Outputs => Routes.Select(x => x.OutputName).Distinct().ToArray();


   // Properties moved up to group them, constructor added above.
}