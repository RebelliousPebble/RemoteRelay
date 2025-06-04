using System;

namespace RemoteRelay.Common;

[Serializable]
public class RelayConfig
{
    public string SourceName { get; set; }
    public string OutputName { get; set; }
    public int RelayPin { get; set; }
    public bool ActiveLow { get; set; } = true;
}