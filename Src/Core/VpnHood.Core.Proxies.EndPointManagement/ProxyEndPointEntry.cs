using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement;

internal class ProxyEndPointEntry(ProxyEndPointInfo endPointInfo) 
{
    private readonly object _lock = new();
    public ProxyEndPointInfo Info => endPointInfo;
    public ProxyEndPointStatus Status => endPointInfo.Status;
    public ProxyEndPoint EndPoint => endPointInfo.EndPoint;

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
            Status.SucceededCount++;
            Status.Latency = latency;
            Status.LastSucceeded = FastDateTime.UtcNow;
            Status.ErrorMessage = null;

            var isSlow = fastestLatency != null && latency > GetSlowThreshold(fastestLatency.Value);
            if (isSlow) {
                Status.Penalty++;

                VhLogger.Instance.LogDebug("Proxy server responded slowly. {ProxyServer}, ResponseTime: {ResponseTime}, PenaltyRate: {Penalty}",
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
            Status.Penalty++;
            Status.Penalty++;
            Status.FailedCount++;
            Status.LastFailed = FastDateTime.UtcNow;
            Status.ErrorMessage = exception?.Message;
            UpdatePosition(currentQueuePos);

            VhLogger.Instance.LogDebug("Failed to connect to proxy server. {ProxyServer}, FailedCount: {FailedCount}, Penalty: {Penalty}",
                VhLogger.FormatHostName(EndPoint.Host), Status.FailedCount, Status.Penalty);
        }
    }
}