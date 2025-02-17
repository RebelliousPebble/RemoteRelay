using System.Device.Gpio;

namespace RemoteRelay.Server;

public class Source
{
   private readonly Dictionary<string, GpioPin> _relayOutputPins;
   private GpioPin? _gpiButtonInputPin;

   public Source(string sourceName)
   {
      _relayOutputPins = new Dictionary<string, GpioPin>();
      _sourceName = sourceName;
   }

   public string _sourceName { get; set; }

   public void AddOutputPin(ref GpioController controller, string outputName, int pinNumber)
   {
      var pin = controller.OpenPin(pinNumber, PinMode.Output);
      _relayOutputPins.Add(outputName, pin);
   }

   public void EnableOutput(string output = "")
   {
      if (_relayOutputPins.ContainsKey(output))
         foreach (var pin in _relayOutputPins)
            pin.Value.Write(pin.Key == output ? PinValue.Low : PinValue.High);
   }

   public void DisableOutput()
   {
      // write all output pins low
      foreach (var pin in _relayOutputPins) pin.Value.Write(PinValue.High);
   }

   public string GetCurrentRoute()
   {
      return _relayOutputPins.FirstOrDefault(x => x.Value.Read() == PinValue.Low).Key;
   }
}