using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Linq;

namespace RemoteRelay;

public class SwitcherState
{
    private GpioController _gpiController;
    private List<Source> _sources = new();
    private int outputCount = 0;

    public SwitcherState(AppSettings settings)
    {
        if (IsGpiEnvironment())
        {
            _gpiController = new GpioController(PinNumberingScheme.Board, new LibGpiodDriver());
        }
        else
        {
            _gpiController = new GpioController(PinNumberingScheme.Board, new MockGpioDriver());
        }

        foreach (var source in settings.Sources)
        {
            var newSource = new Source(source);
            foreach (var output in settings.Routes.Where(x => x.SourceName == source).Select(x => x.OutputName).Distinct())
            {
                newSource.AddOutputPin(ref _gpiController, output, settings.Routes.First(x => x.SourceName == source && x.OutputName == output).RelayPin);
            }
            _sources.Add(newSource);
        }
        outputCount = settings.Outputs.Count;
        
        // Set default source
        if (settings.DefaultSource != null)
        {
            SwitchSource(settings.DefaultSource, settings.Routes.First(x => x.SourceName == settings.DefaultSource).OutputName);
        }
    }

    public void SwitchSource(string source, string output)
    {
        if (outputCount == 1)
        {
            foreach (var x in _sources)
            {
                if(x._sourceName == source)
                {
                    x.EnableOutput(output);
                }
                else
                {
                    x.DisableOutput();
                }
            }
        }
        else
        {
            foreach (var x in _sources)
            {
                if(x._sourceName == source)
                {
                    x.EnableOutput(output);
                }
            } 
        }

    }
    
    // first value is index of source, second value is output index (-1 is not outputting)
    public Dictionary<string, string> GetSystemState()
    {
        var state = new Dictionary<string, string>();
        foreach(var x in _sources)
        {
            state.Add(x._sourceName, x.GetCurrentRoute());
        }

        return state;
    }

    private bool IsGpiEnvironment()
    {
        // Check if we are running on a Raspberry Pi or other unix-like environment
        return Environment.OSVersion.Platform == PlatformID.Unix;
    }
}
