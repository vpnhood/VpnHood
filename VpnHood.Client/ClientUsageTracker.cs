using Ga4.Trackers;
using VpnHood.Common.Jobs;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;

namespace VpnHood.Client;

internal class ClientUsageTracker : IJob, IAsyncDisposable
{
    private readonly AsyncLock _reportLock = new();
    private readonly VpnHoodClient.ClientStat _clientStat;
    private readonly ITracker _tracker;
    private Traffic _lastTraffic = new();
    private int _lastRequestCount;
    private int _lastConnectionCount;
    private bool _disposed;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(25));

    public ClientUsageTracker(VpnHoodClient.ClientStat clientStat, ITracker tracker)
    {
        _clientStat = clientStat;
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

        var traffic = _clientStat.SessionTraffic;
        var usage = traffic - _lastTraffic;
        var requestCount = _clientStat.ConnectorStat.RequestCount;
        var connectionCount = _clientStat.ConnectorStat.CreatedConnectionCount;

        var trackEvent = ClientTrackerBuilder.BuildUsage(usage, requestCount - _lastRequestCount, connectionCount - _lastConnectionCount);

        await _tracker.Track([trackEvent]).VhConfigureAwait();
        _lastTraffic = traffic;
        _lastRequestCount = requestCount;
        _lastConnectionCount = connectionCount;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Make sure no exception in dispose
            if (_clientStat.SessionTraffic - _lastTraffic != new Traffic())
                await Report().VhConfigureAwait();
        }
        catch
        {
            // ignore
        }

        _disposed = true;
    }
}