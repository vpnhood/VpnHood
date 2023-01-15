using System.Net;
using PacketDotNet;

namespace VpnHood.Tunneling;

public interface IPacketProxyReceiver : IPacketReceiver
{
    public void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint);
    public void OnNewLocalEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint? remoteEndPoint = null);

}