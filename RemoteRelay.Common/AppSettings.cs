using System.Device.Gpio; // Assuming PinValue is here

namespace RemoteRelay.Common;

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
   public Dictionary<string, int> PhysicalSourceButtons { get; set; }
   public IReadOnlyCollection<string> Sources => Routes.Select(x => x.SourceName).Distinct().ToArray();
   public IReadOnlyCollection<string> Outputs => Routes.Select(x => x.OutputName).Distinct().ToArray();


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
}