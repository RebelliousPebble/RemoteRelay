using System;
using System.Collections.Generic;

namespace RemoteRelay;

public class ClientConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 33101;
    public List<string>? ShownInputs { get; set; }
    public List<string>? ShownOutputs { get; set; }

    /// <summary>
    /// Returns true if the host is localhost or 127.0.0.1
    /// </summary>
    public bool IsLocalhost => 
        Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
        Host == "127.0.0.1";
}
