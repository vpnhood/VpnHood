using System.Net;
using PacketDotNet;

namespace VpnHood.Tunneling;

public interface IPacketProxyReceiver : IPacketReceiver
{
    public void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint);
    public void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, 
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint);

}