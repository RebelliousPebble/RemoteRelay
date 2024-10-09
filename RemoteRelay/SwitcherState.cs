using System.Collections.Generic;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;

namespace RemoteRelay;

public class SwitcherState
{
    private GpioController _gpiController;
    private List<Source> _sources = new();


    public SwitcherState(AppSettings settings)
    {
        _gpiController = new GpioController(PinNumberingScheme.Board, new LibGpiodDriver());

        foreach (var source in settings.Sources)
        {
            Source relay = new Source();
            foreach(var output in source.OutputPins)
            {
                relay.AddOutputPin(_gpiController, output.Value);
            }

            if (source.GpiButtonInputPin != null)
            {
                relay.AddInputPin(_gpiController, source.GpiButtonInputPin.Value);
            }

            _sources.Add(relay);
        }
        
    }

    public void SwitchSource(int source, int output = 0)
    {
        if (source < _sources.Count)
        {
            _sources[source].EnableOutput(output);
        }
        for (int i = 0; i < _sources.Count; i++)
        {
            if (i != source)
            {
                _sources[i].DisableOutput();
            }
        }
    }
    
    // first value is index of source, second value is output index (-1 is not outputting)
    public Dictionary<int, int> GetSystemState()
    {
        var state = new Dictionary<int, int>();
        for (int i = 0; i < _sources.Count; i++)
        {
            state[i] = _sources[i].GetStatus();
        }
        return state;
    }
}