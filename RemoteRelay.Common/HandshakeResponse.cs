namespace RemoteRelay.Common;

public enum CompatibilityStatus
{
    Compatible,
    ClientOutdated,
    ServerOutdated, // Less likely to be detected by server, but good to have
    Incompatible
}

public class HandshakeResponse
{
    public CompatibilityStatus Status { get; set; }
    public string ServerVersion { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
