using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;

namespace VpnHood.Net.Http3;

internal static class Http3RequestMetrics
{
    private static readonly Meter Meter = new("VpnHood.Net.Http3");
    private static readonly UpDownCounter<long> QueuedRequests = Meter.CreateUpDownCounter<long>(
        "http3.queued_requests", description: "HTTP/3 application dispatches waiting to start.");

    public static void RequestQueued(IPEndPoint endPoint, long change)
    {
        if (!QueuedRequests.Enabled)
            return;
        var tags = new TagList {
            { "server.address", endPoint.Address.ToString() },
            { "server.port", endPoint.Port },
            { "network.protocol.version", "3" }
        };
        QueuedRequests.Add(change, tags);
    }
}
