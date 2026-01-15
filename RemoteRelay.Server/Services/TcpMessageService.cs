using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace RemoteRelay.Server.Services;

/// <summary>
/// Service for sending TCP messages to external systems when routes are switched.
/// </summary>
public class TcpMessageService
{
    private readonly ILogger<TcpMessageService> _logger;

    public TcpMessageService(ILogger<TcpMessageService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends a message to the specified TCP endpoint.
    /// Fire-and-forget: failures are logged but don't interrupt switching.
    /// </summary>
    /// <param name="host">The target host address</param>
    /// <param name="port">The target port</param>
    /// <param name="message">The message to send</param>
    public async Task SendMessageAsync(string host, int port, string message)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            _logger.LogDebug("Connecting to TCP endpoint {Host}:{Port}", host, port);
            await client.ConnectAsync(host, port, cts.Token);

            var data = Encoding.UTF8.GetBytes(message);
            await client.GetStream().WriteAsync(data, cts.Token);

            _logger.LogInformation("TCP message sent to {Host}:{Port}: {Message}", host, port, message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TCP connection to {Host}:{Port} timed out", host, port);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("TCP socket error connecting to {Host}:{Port}: {Error}", host, port, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send TCP message to {Host}:{Port}", host, port);
        }
    }
}
