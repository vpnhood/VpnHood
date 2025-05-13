using System.Net;
using VpnHood.Core.Packets;

namespace VpnHood.Core.Tunneling;

public interface IPacketProxyCallbacks
{
    public void OnConnectionRequested(IpProtocol protocolType, IPEndPoint remoteEndPoint);

    public void OnConnectionEstablished(IpProtocol protocolType, IPEndPoint localEndPoint, 
        IPEndPoint remoteEndPoint, bool isNewLocalEndPoint, bool isNewRemoteEndPoint);
}