using Ga4.Trackers;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client;

internal class ClientUsageTracker : IJob, IAsyncDisposable
{
    private readonly AsyncLock _reportLock = new();
    private readonly ISessionStatus _sessionStatus;
    private readonly ITracker _tracker;
    private Traffic _lastTraffic = new();
    private int _lastRequestCount;
    private int _lastConnectionCount;
    private bool _disposed;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(25));

    public ClientUsageTracker(ISessionStatus sessionStatus, ITracker tracker)
    {
        _sessionStatus = sessionStatus;
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

        var traffic = _sessionStatus.SessionTraffic;
        var usage = traffic - _lastTraffic;
        var requestCount = _sessionStatus.ConnectorStat.RequestCount;
        var connectionCount = _sessionStatus.ConnectorStat.CreatedConnectionCount;

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
            if (_sessionStatus.SessionTraffic - _lastTraffic != new Traffic())
                await Report().VhConfigureAwait();
        }
        catch {
            // ignore
        }

        _disposed = true;
    }
}