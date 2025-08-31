using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Client.ProxyServers;

internal class ProxyServer(ProxyServerEndPoint endPoint)
{
    private readonly object _lock = new();
    private int _requestPosition;
    public ProxyServerEndPoint EndPoint => endPoint;
    public ProxyServerStatus Status = new() {
        IsActive = true, 
        Id = endPoint.GetId()
    };

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
            Status.LastUsedTime = DateTime.Now;

            var isSlow = fastestLatency != null && latency > GetSlowThreshold(fastestLatency.Value);
            if (isSlow) {
                Status.Penalty++;

                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Proxy server responded slowly. {ProxyServer}, ResponseTime: {ResponseTime}, PenaltyRate: {Penalty}",
                    VhLogger.FormatHostName(EndPoint.Host), latency, Status.Penalty);
            }
            else {
                if (Status.Penalty > 0)
                    Status.Penalty--;
            }
            UpdatePosition(currentRequestPos);
        }
    }

    private void UpdatePosition(int currentRequestPos)
    {
        _requestPosition = currentRequestPos + Status.Penalty * 3 + 1;
    }

    public void RecordFailed(Exception? exception, int currentRequestPos)
    {
        lock (_lock) {
            Status.Penalty++;
            Status.Penalty++;
            Status.FailedCount++;
            Status.ErrorMessage = exception?.Message;
            UpdatePosition(currentRequestPos);

            VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                "Failed to connect to proxy server. {ProxyServer}, FailedCount: {FailedCount}, Penalty: {Penalty}",
                VhLogger.FormatHostName(EndPoint.Host), Status.FailedCount, Status.Penalty);
        }
    }
}