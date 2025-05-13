using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Collections;
namespace VpnHood.Core.Tunneling;

internal class UdpProxyEx(UdpClient udpClient, TimeSpan udpTimeout, int queueCapacity, bool autoDisposeSentPackets)
    : UdpProxy(udpClient, sourceEndPoint: null, queueCapacity, autoDisposeSentPackets)
{
    public TimeoutDictionary<IPEndPoint, TimeoutItem<IPEndPoint>> DestinationEndPointMap { get; } = new(udpTimeout);

    protected override IPEndPoint? GetSourceEndPoint(IPEndPoint remoteEndPoint)
    {
        return DestinationEndPointMap.TryGetValue(remoteEndPoint, out var sourceEndPoint) 
            ? sourceEndPoint.Value 
            : null;
    }
}