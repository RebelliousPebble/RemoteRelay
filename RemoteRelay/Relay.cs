using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace RemoteRelay;

public class Source
{
    private Dictionary<string, GpioPin> _relayOutputPins;
    private GpioPin? _gpiButtonInputPin;
    public string _sourceName { get; set; }

    public Source(string sourceName)
    {
        _relayOutputPins = new Dictionary<string, GpioPin>();
        _sourceName = sourceName;
    }
    
    public void AddOutputPin(ref GpioController controller, string outputName, int pinNumber)
    {
        var pin = controller.OpenPin(pinNumber, PinMode.Output);
        _relayOutputPins.Add(outputName, pin);
    }
    
    public void EnableOutput(string output = "")
    {
        if(_relayOutputPins.ContainsKey(output))
        {
            throw new ArgumentException("Output pin not found");
        }
        else
        {
            foreach (var pin in _relayOutputPins)
            {
                if (pin.Key == output)
                {
                    pin.Value.Write(PinValue.High);
                }
                else
                {
                    pin.Value.Write(PinValue.Low);
                }
            }
        }

    }

    public void DisableOutput()
    {
        // write all output pins low
        foreach (var pin in _relayOutputPins)
        {
            pin.Value.Write(PinValue.Low);
        }
    }
    
    public string GetCurrentRoute()
    {
        return _relayOutputPins.FirstOrDefault(x => x.Value.Read() == PinValue.High).Key;
    }
}