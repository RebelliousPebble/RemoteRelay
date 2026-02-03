namespace RemoteRelay.Common;

[Serializable]
public class RelayConfig
{
    public string SourceName { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;
    public int RelayPin { get; set; }
    public bool ActiveLow { get; set; } = true;
    
    /// <summary>
    /// Optional TCP message to send when this route is activated.
    /// Sent to the endpoint configured via TcpMirrorAddress/TcpMirrorPort.
    /// </summary>
    public string? TcpMessage { get; set; }
}