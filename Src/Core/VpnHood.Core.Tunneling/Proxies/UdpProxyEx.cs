using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Collections;
namespace VpnHood.Core.Tunneling.Proxies;

internal class UdpProxyEx(UdpClient udpClient, TimeSpan udpTimeout, int queueCapacity, bool autoDisposePackets)
    : UdpProxy(udpClient, sourceEndPoint: null, queueCapacity, autoDisposePackets)
{
    public TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>> DestinationEndPointMap { get; } = new(udpTimeout);

    protected override IPEndPoint? GetSourceEndPoint(IPEndPoint remoteEndPoint)
    {
        return DestinationEndPointMap.TryGetValue(remoteEndPoint, out var sourceEndPoint) 
            ? sourceEndPoint.Value 
            : null;
    }

    protected override void DisposeManaged()
    {
        DestinationEndPointMap.Dispose();

        base.DisposeManaged();
    }
}