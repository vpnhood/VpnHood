using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Client.ProxyServers;

internal class ProxyServer
{
    private readonly object _lock = new();
    private int _requestPosition;
    public required ProxyServerEndPoint EndPoint { get; init; }
    public ProxyServerStatus Status = new();
    public int GetSortValue(int currentRequestCount)
    {
        lock (_lock) {
            return _requestPosition - currentRequestCount;
        }
    }

    private static TimeSpan GetSlowThreshold(TimeSpan fastestLatency)
    {
        return fastestLatency * 2 + TimeSpan.FromSeconds(2);
    }

    public void RecordSuccess(TimeSpan latency, TimeSpan? fastestLatency, int currentRequestPos)
    {
        lock (_lock) {
            Status.SucceededCount++;
            Status.Latency = latency;
            Status.ConnectionTime = DateTime.Now;
            
            var isSlow = fastestLatency != null && latency > GetSlowThreshold(fastestLatency.Value);
            if (isSlow) {
                Status.Penalty++;
                _requestPosition = currentRequestPos + Status.Penalty * 3;

                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Proxy server responded slowly. {ProxyServer}, ResponseTime: {ResponseTime}, PenaltyRate: {Penalty}",
                    VhLogger.FormatHostName(EndPoint.Host), latency, Status.Penalty);
            }
            else {
                if (Status.Penalty > 0)
                    Status.Penalty--;
            }
        }
    }

    public void RecordFailed(int currentRequestPos)
    {
        lock (_lock) {
            Status.Penalty++;
            Status.Penalty++;
            Status.FailedCount++;
            _requestPosition = currentRequestPos + Status.Penalty * 3;

            VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                "Failed to connect to proxy server. {ProxyServer}, FailedCount: {FailedCount}, Penalty: {Penalty}",
                VhLogger.FormatHostName(EndPoint.Host), Status.FailedCount, Status.Penalty);
        }
    }
}