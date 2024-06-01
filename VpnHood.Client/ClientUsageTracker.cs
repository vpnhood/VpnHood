using Ga4.Ga4Tracking;
using VpnHood.Common.Jobs;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;

namespace VpnHood.Client;

internal class ClientUsageTracker : IJob, IAsyncDisposable
{
    private readonly AsyncLock _reportLock = new();
    private readonly VpnHoodClient.ClientStat _clientStat;
    private readonly Ga4Tracker _ga4Tracker;
    private Traffic _lastTraffic = new();
    private int _lastRequestCount;
    private int _lastConnectionCount;
    private bool _disposed;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(25));

    public ClientUsageTracker(VpnHoodClient.ClientStat clientStat, Version version, Ga4Tracker ga4Tracker)
    {
        _clientStat = clientStat;
        _ga4Tracker = ga4Tracker;
        JobRunner.Default.Add(this);

        var useProperties = new Dictionary<string, object> { { "client_version", version.ToString(3) } };
        _ = ga4Tracker.Track(new Ga4TagEvent { EventName = Ga4TagEvents.SessionStart }, useProperties);
    }

    public Task RunJob()
    {
        return Report();
    }

    public async Task Report()
    {
        using var lockAsync = await _reportLock.LockAsync().ConfigureAwait(false);

        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var traffic = _clientStat.SessionTraffic;
        var usage = traffic - _lastTraffic;
        var requestCount = _clientStat.ConnectorStat.RequestCount;
        var connectionCount = _clientStat.ConnectorStat.CreatedConnectionCount;

        var tagEvent = new Ga4TagEvent
        {
            EventName = "usage",
            Properties = new Dictionary<string, object>
            {
                {"traffic_total", Math.Round(usage.Total / 1_000_000d)},
                {"traffic_sent", Math.Round(usage.Sent / 1_000_000d)},
                {"traffic_received", Math.Round(usage.Received / 1_000_000d)},
                {"requests", requestCount - _lastRequestCount},
                {"connections", connectionCount - _lastConnectionCount}
            }
        };

        await _ga4Tracker.Track(tagEvent).ConfigureAwait(false);
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
                await Report().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        _disposed = true;
    }
}