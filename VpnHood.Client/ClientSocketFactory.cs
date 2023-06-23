using System.Net.Sockets;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client;

public class ClientSocketFactory : ISocketFactory
{
    private readonly IPacketCapture _packetCapture;
    private readonly ISocketFactory _socketFactory;

    public ClientSocketFactory(IPacketCapture packetCapture, ISocketFactory socketFactory)
    {
        _packetCapture = packetCapture;
        _socketFactory = socketFactory;
    }

    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = _socketFactory.CreateTcpClient(addressFamily);

        // config for client
        _socketFactory.SetKeepAlive(tcpClient.Client, true);
        VhUtil.ConfigTcpClient(tcpClient, null, null);
        
        // auto protect
        if (_packetCapture.CanProtectSocket)
            _packetCapture.ProtectSocket(tcpClient.Client);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var ret = _socketFactory.CreateUdpClient(addressFamily);
        if (_packetCapture.CanProtectSocket)
            _packetCapture.ProtectSocket(ret.Client);
        return ret;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        _socketFactory.SetKeepAlive(socket, enable);
    }
}