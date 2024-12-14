using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client;

public class ClientSocketFactory(
    IPacketCapture packetCapture,
    ISocketFactory socketFactory)
    : ISocketFactory
{
    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = socketFactory.CreateTcpClient(addressFamily);

        // config for client
        socketFactory.SetKeepAlive(tcpClient.Client, true);
        VhUtil.ConfigTcpClient(tcpClient, null, null);

        // auto protect
        if (packetCapture.CanProtectSocket)
            packetCapture.ProtectSocket(tcpClient.Client);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var ret = socketFactory.CreateUdpClient(addressFamily);
        if (packetCapture.CanProtectSocket)
            packetCapture.ProtectSocket(ret.Client);
        return ret;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        socketFactory.SetKeepAlive(socket, enable);
    }

    public bool CanDetectInProcessPacket => packetCapture.CanDetectInProcessPacket;

    public bool IsInProcessPacket(PacketDotNet.ProtocolType protocol, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
    {
        return packetCapture.IsInProcessPacket(protocol, localEndPoint, remoteEndPoint);
    }
}