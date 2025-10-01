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

   public void AddOutputPin(GpioController controller, RelayConfig config)
   {
      var pin = controller.OpenPin(config.RelayPin, PinMode.Output);
      _relayOutputPins.Add(config.OutputName, (pin, config));
   }

   public void EnableOutput(string output = "")
   {
      Console.WriteLine($"EnableOutput called with output: '{output}'");
      Console.WriteLine($"Available outputs: {string.Join(", ", _relayOutputPins.Keys)}");
      
      if (_relayOutputPins.ContainsKey(output))
      {
         Console.WriteLine($"Found output '{output}' in relay pins");
         var targetPinConfig = _relayOutputPins[output].Config;
         foreach (var entry in _relayOutputPins)
         {
            var pin = entry.Value.Pin;
            var config = entry.Value.Config;
            if (entry.Key == output)
            {
               var writeValue = targetPinConfig.ActiveLow ? PinValue.Low : PinValue.High;
               Console.WriteLine($"Writing {writeValue} to pin {config.RelayPin} for active output '{entry.Key}'");
               pin.Write(writeValue);
               MockGpioDriver.UpdatePinState(config.RelayPin, writeValue);
            }
            else
            {
               var writeValue = config.ActiveLow ? PinValue.High : PinValue.Low;
               Console.WriteLine($"Writing {writeValue} to pin {config.RelayPin} for inactive output '{entry.Key}'");
               pin.Write(writeValue);
               MockGpioDriver.UpdatePinState(config.RelayPin, writeValue);
            }
         }
      }
      else
      {
         Console.WriteLine($"Output '{output}' NOT found in relay pins");
      }
   }

   public void DisableOutput()
   {
      Console.WriteLine($"DisableOutput called for source '{_sourceName}'");
      Console.WriteLine($"Available outputs: {string.Join(", ", _relayOutputPins.Keys)}");
      
      foreach (var entry in _relayOutputPins)
      {
         var pin = entry.Value.Pin;
         var config = entry.Value.Config;
         var writeValue = config.ActiveLow ? PinValue.High : PinValue.Low;
         Console.WriteLine($"Writing {writeValue} to pin {config.RelayPin} for disabled output '{entry.Key}'");
         pin.Write(writeValue);
         MockGpioDriver.UpdatePinState(config.RelayPin, writeValue);
      }
   }

   public string GetCurrentRoute()
   {
      var activeOutput = _relayOutputPins.FirstOrDefault(x => x.Value.Pin.Read() == (x.Value.Config.ActiveLow ? PinValue.Low : PinValue.High));
      return activeOutput.Key ?? string.Empty;
   }
}