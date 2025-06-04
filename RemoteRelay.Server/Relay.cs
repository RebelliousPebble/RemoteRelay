using System.Device.Gpio;
using RemoteRelay.Common;

namespace RemoteRelay.Server;

public class Source
{
   private readonly Dictionary<string, (GpioPin Pin, RelayConfig Config)> _relayOutputPins;
   private GpioPin? _gpiButtonInputPin;

   public Source(string sourceName)
   {
      _relayOutputPins = new Dictionary<string, (GpioPin Pin, RelayConfig Config)>();
      _sourceName = sourceName;
   }

   public string _sourceName { get; set; }

   public void AddOutputPin(ref GpioController controller, RelayConfig config)
   {
      var pin = controller.OpenPin(config.RelayPin, PinMode.Output);
      _relayOutputPins.Add(config.OutputName, (pin, config));
   }

   public void EnableOutput(string output = "")
   {
      if (_relayOutputPins.ContainsKey(output))
      {
         var targetPinConfig = _relayOutputPins[output].Config;
         foreach (var entry in _relayOutputPins)
         {
            var pin = entry.Value.Pin;
            var config = entry.Value.Config;
            if (entry.Key == output)
            {
               pin.Write(targetPinConfig.ActiveLow ? PinValue.Low : PinValue.High);
            }
            else
            {
               pin.Write(config.ActiveLow ? PinValue.High : PinValue.Low);
            }
         }
      }
   }

   public void DisableOutput()
   {
      foreach (var entry in _relayOutputPins)
      {
         var pin = entry.Value.Pin;
         var config = entry.Value.Config;
         pin.Write(config.ActiveLow ? PinValue.High : PinValue.Low);
      }
   }

   public string GetCurrentRoute()
   {
      return _relayOutputPins.FirstOrDefault(x => x.Value.Pin.Read() == (x.Value.Config.ActiveLow ? PinValue.Low : PinValue.High)).Key;
   }
}