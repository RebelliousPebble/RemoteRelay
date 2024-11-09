using System;

namespace RemoteRelay;

[Serializable]
public class RelayConfig
{
    public string SourceName { get; set; }
    public string OutputName { get; set; }
    public int RelayPin { get; set; }
}