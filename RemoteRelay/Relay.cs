using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;

namespace RemoteRelay;

public class Source
{
    private List<GpioPin> _relayOutputPins;
    private GpioPin? _gpiButtonInputPin;

    public Source()
    {
        _relayOutputPins = new List<GpioPin>();
    }
    
    public void AddOutputPin(GpioController controller, int pinNumber)
    {
        var pin = controller.OpenPin(pinNumber, PinMode.Output);
        _relayOutputPins.Add(pin);
    }
    
    public void AddInputPin(GpioController controller, int pinNumber)
    {
        _gpiButtonInputPin = controller.OpenPin(pinNumber, PinMode.Input);
        _gpiButtonInputPin.ValueChanged += GpiButtonInputPinOnValueChanged;
    }

    public void EnableOutput(int output = 0)
    {
        if(output < _relayOutputPins.Count)
        {
            _relayOutputPins[output].Write(PinValue.High);
        }
        // write all other output pins low
        for (int i = 0; i < _relayOutputPins.Count; i++)
        {
            if (i != output)
            {
                _relayOutputPins[i].Write(PinValue.Low);
            }
        }
    }

    public void DisableOutput()
    {
        // write all output pins low
        foreach (var pin in _relayOutputPins)
        {
            pin.Write(PinValue.Low);
        }
    }
    //-1 = not routed, 0+ = output routed to.
    public int GetStatus()
    {
        for (int i = 0; i < _relayOutputPins.Count; i++)
        {
            if (_relayOutputPins[i].Read() == PinValue.High)
            {
                return i;
            }
        }
        return -1;
    }
    
    private void GpiButtonInputPinOnValueChanged(object sender, PinValueChangedEventArgs pinvaluechangedeventargs)
    {
        if(_relayOutputPins.Count == 1)
        {
            _relayOutputPins[0].Write(pinvaluechangedeventargs.ChangeType == PinEventTypes.Rising ? PinValue.High : PinValue.Low);
        }
    }
}