using Ga4.Trackers;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client;

internal class ClientUsageTracker : IJob, IAsyncDisposable
{
    private readonly AsyncLock _reportLock = new();
    private readonly VpnHoodClient _vpnHoodClient;
    private readonly ITracker _tracker;
    private Traffic _lastTraffic = new();
    private int _lastRequestCount;
    private int _lastConnectionCount;
    private bool _disposed;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(25));

    public ClientUsageTracker(VpnHoodClient vpnHoodClient, ITracker tracker)
    {
        _vpnHoodClient = vpnHoodClient;
        _tracker = tracker;
        JobRunner.Default.Add(this);
    }
    
    public Task RunJob()
    {
        return Report();
    }

    public async Task Report()
    {
        using var lockAsync = await _reportLock.LockAsync().VhConfigureAwait();

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var clientStat = _vpnHoodClient.Stat;
        if (clientStat == null)
            return;

        var traffic = clientStat.SessionTraffic;
        var usage = traffic - _lastTraffic;
        var requestCount = clientStat.ConnectorStat.RequestCount;
        var connectionCount = clientStat.ConnectorStat.CreatedConnectionCount;

        var trackEvent = ClientTrackerBuilder.BuildUsage(usage, requestCount - _lastRequestCount,
            connectionCount - _lastConnectionCount);

        await _tracker.Track([trackEvent]).VhConfigureAwait();
        _lastTraffic = traffic;
        _lastRequestCount = requestCount;
        _lastConnectionCount = connectionCount;
    }

    public async ValueTask DisposeAsync()
    {
        try {
            // Make sure no exception in dispose
            var clientStat = _vpnHoodClient.Stat;
            if (clientStat!=null && clientStat.SessionTraffic - _lastTraffic != new Traffic())
                await Report().VhConfigureAwait();
        }
        catch {
            // ignore
        }

        _disposed = true;
    }
}