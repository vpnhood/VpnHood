using System.Net;
using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public interface IPacketProxyReceiver : IPacketReceiver
{
    public void OnNewRemoteEndPoint(IpProtocol protocolType, IPEndPoint remoteEndPoint);

    public void OnNewEndPoint(IpProtocol protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint);
}