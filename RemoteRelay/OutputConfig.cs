using System;
using System.Collections.Generic;
using System.Device.Gpio;

namespace RemoteRelay;

[Serializable]
public class OutputConfig
{
    public string? DisplayName { get; set; }
    public Dictionary<string, int> OutputPins { get; set; }
    public int? GpiButtonInputPin { get; set; }
}