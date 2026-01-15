namespace RemoteRelay.Common;

/// <summary>
/// Response from the SaveConfiguration SignalR method.
/// </summary>
public class SaveConfigurationResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
