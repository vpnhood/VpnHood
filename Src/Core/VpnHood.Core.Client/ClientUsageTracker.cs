using Ga4.Trackers;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client;

internal class ClientUsageTracker : IDisposable
{
    private readonly TimeSpan _reportInterval = TimeSpan.FromMinutes(25);
    private readonly AsyncLock _reportLock = new();
    private Traffic _lastTraffic = new();
    private int _lastRequestCount;
    private int _lastConnectionCount;
    private bool _disposed;
    private readonly ISessionStatus _sessionStatus;
    private readonly ITracker _tracker;
    private readonly Job _reportJob;

    public ClientUsageTracker(ISessionStatus sessionStatus, ITracker tracker)
    {
        _sessionStatus = sessionStatus;
        _tracker = tracker;
        _reportJob = new Job(Report, new JobOptions {
            Period = _reportInterval,
            MaxRetry = 2,
            Name = "ClientReporter"
        });
    }

    public async ValueTask Report(CancellationToken cancellationToken)
    {
        using var lockAsync = await _reportLock.LockAsync(TimeSpan.Zero, cancellationToken).Vhc();
        if (!lockAsync.Succeeded)
            return;

        var traffic = _sessionStatus.SessionTraffic;
        var usage = traffic - _lastTraffic;
        var requestCount = _sessionStatus.ConnectorStat.RequestCount;
        var connectionCount = _sessionStatus.ConnectorStat.CreatedConnectionCount;

        var trackEvent = ClientTrackerBuilder.BuildUsage(usage, requestCount - _lastRequestCount,
            connectionCount - _lastConnectionCount);

        await _tracker.Track([trackEvent], cancellationToken).Vhc();
        _lastTraffic = traffic;
        _lastRequestCount = requestCount;
        _lastConnectionCount = connectionCount;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _reportJob.Dispose();
        _disposed = true;
    }
}