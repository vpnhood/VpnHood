using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management;

internal class ProxyEndPointEntry(ProxyEndPointInfo endPointInfo)
{
    private readonly Lock _lock = new();
    public ProxyEndPointInfo Info => endPointInfo;
    public ProxyEndPointStatus Status => endPointInfo.Status;
    public ProxyEndPoint EndPoint => endPointInfo.EndPoint;

    /// <summary>Set when the status or enabled state changed since the last store flush.</summary>
    public bool IsDirty { get; set; }

    // whether it worked as a proxy at its last use: it connected, or it answered that the server could not be reached
    public bool IsLastUsedAnswered {
        get {
            lock (_lock)
                return Status.IsLastUsedSucceeded || _isLastFailureServerUnreachable;
        }
    }

    private bool _isLastFailureServerUnreachable;

    // A copy of the endpoint and its status as they are, for a flush to write, which clears the flag: a change made
    // during the write marks the entry again, and the copy is never one half-way through a change
    public ProxyEndPointInfo TakeForFlush()
    {
        lock (_lock) {
            IsDirty = false;
            return new ProxyEndPointInfo {
                EndPoint = new ProxyEndPoint {
                    Protocol = EndPoint.Protocol,
                    Host = EndPoint.Host,
                    Port = EndPoint.Port,
                    Username = EndPoint.Username,
                    Password = EndPoint.Password,
                    IsEnabled = EndPoint.IsEnabled
                },
                Status = new ProxyEndPointStatus {
                    Penalty = Status.Penalty,
                    SucceededCount = Status.SucceededCount,
                    FailedCount = Status.FailedCount,
                    Latency = Status.Latency,
                    LastSucceeded = Status.LastSucceeded,
                    LastFailed = Status.LastFailed,
                    ErrorMessage = Status.ErrorMessage,
                    QueuePosition = Status.QueuePosition
                }
            };
        }
    }

    // a success taken back, as one whose connection failed after its connect
    public void TakeBackSuccess()
    {
        lock (_lock)
            Status.SucceededCount--;
    }

    public long GetSortValue(long currentRequestCount)
    {
        lock (_lock) {
            return Info.Status.QueuePosition - currentRequestCount;
        }
    }

    private static TimeSpan GetSlowThreshold(TimeSpan fastestLatency)
    {
        return fastestLatency * 2 + TimeSpan.FromSeconds(2);
    }

    public void RecordSuccess(TimeSpan latency, TimeSpan? fastestLatency, long currentQueuePos)
    {
        lock (_lock) {
            IsDirty = true;
            Status.SucceededCount++;
            Status.Latency = latency;
            Status.LastSucceeded = DateTime.UtcNow; // once per connection: a coarse second made a success and a failure tie
            Status.ErrorMessage = null;
            _isLastFailureServerUnreachable = false;

            var isSlow = fastestLatency != null && latency > GetSlowThreshold(fastestLatency.Value);
            if (isSlow) {
                Status.Penalty++;

                VhLogger.Instance.LogDebug(
                    "Proxy server responded slowly. {ProxyServer}, ResponseTime: {ResponseTime}, PenaltyRate: {Penalty}",
                    VhLogger.FormatHostName(EndPoint.Host), latency, Status.Penalty);
            }
            else {
                if (Status.Penalty > 0)
                    Status.Penalty--;
            }

            UpdatePosition(currentQueuePos);
        }
    }

    private void UpdatePosition(long currentQueuePos)
    {
        lock (_lock) {
            Info.Status.QueuePosition = currentQueuePos + Status.Penalty * 3 + 1;
        }
    }

    public void RecordFailed(Exception? exception, long currentQueuePos)
    {
        lock (_lock) {
            IsDirty = true;
            Status.Penalty++;
            Status.Penalty++;
            Status.FailedCount++;
            Status.LastFailed = DateTime.UtcNow;
            Status.ErrorMessage = exception?.Message;
            _isLastFailureServerUnreachable = exception != null && ProxyFailures.IsServerUnreachable(exception);
            UpdatePosition(currentQueuePos);

            VhLogger.Instance.LogDebug(
                "Failed to connect to proxy server. {ProxyServer}, FailedCount: {FailedCount}, Penalty: {Penalty}",
                VhLogger.FormatHostName(EndPoint.Host), Status.FailedCount, Status.Penalty);
        }
    }
}