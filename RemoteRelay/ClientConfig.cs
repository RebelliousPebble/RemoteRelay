using System.Collections.Generic;

namespace RemoteRelay;

public class ClientConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 33101;
    public List<string>? ShownInputs { get; set; }
    public List<string>? ShownOutputs { get; set; }
}
