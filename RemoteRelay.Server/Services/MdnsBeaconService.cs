using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteRelay.Common;
using Makaretu.Dns;

namespace RemoteRelay.Server.Services;

/// <summary>
/// Background service that broadcasts mDNS presence.
/// Advertises _remoterelay._tcp.local.
/// </summary>
public class MdnsBeaconService : BackgroundService
{
    private readonly SwitcherState _switcherState;
    private readonly ILogger<MdnsBeaconService> _logger;
    private ServiceDiscovery? _serviceDiscovery;

    public MdnsBeaconService(
        SwitcherState switcherState,
        ILogger<MdnsBeaconService> logger)
    {
        _switcherState = switcherState;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _switcherState.GetSettings();
        var port = settings.ServerPort;

        _logger.LogInformation("Starting mDNS beacon for _remoterelay._tcp.local. on port {Port}", port);

        try
        {
            _serviceDiscovery = new ServiceDiscovery();

            // Advertise the service
            var profile = new ServiceProfile("RemoteRelay", "_remoterelay._tcp", (ushort)port);
            _serviceDiscovery.Advertise(profile);

            _logger.LogInformation("mDNS advertisement active.");

            // Keep reference alive
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start mDNS beacon");
        }

        // BackgroundService keeps running until pending task completes or cancellation
        // Since we are just holding the reference, we can just wait indefinitely
        var tcs = new TaskCompletionSource();
        stoppingToken.Register(() =>
        {
            tcs.TrySetResult();
            _serviceDiscovery?.Dispose();
        });

        return tcs.Task;
    }
}
