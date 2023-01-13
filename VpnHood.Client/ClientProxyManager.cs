using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using PacketDotNet;
using VpnHood.Client.Device;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client;

internal class ClientProxyManager : ProxyManager
{
    private readonly IPacketCapture _packetCapture;
        
    // PacketCapture can not protect Ping so PingProxy does not work
    protected override bool IsPingSupported => false; 

    public ClientProxyManager(IPacketCapture packetCapture, ISocketFactory socketFactory)
    : base(new ProtectedSocketFactory(packetCapture, socketFactory))
    {
        _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
    }

    protected override Task OnPacketReceived(IPPacket ipPacket)
    {
        _packetCapture.SendPacketToInbound(ipPacket);
        return Task.FromResult(0);
    }

    private class ProtectedSocketFactory : ISocketFactory
    {
        private readonly IPacketCapture _packetCapture;
        private readonly ISocketFactory _socketFactory;

        public ProtectedSocketFactory(IPacketCapture packetCapture, ISocketFactory socketFactory)
        {
            _packetCapture = packetCapture;
            _socketFactory = socketFactory;
        }

        public TcpClient CreateTcpClient(AddressFamily addressFamily)
        {
            var ret = _socketFactory.CreateTcpClient(addressFamily);
            _packetCapture.ProtectSocket(ret.Client);
            return ret;
        }

        public UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            var ret = _socketFactory.CreateUdpClient(addressFamily);
            _packetCapture.ProtectSocket(ret.Client);
            return ret;
        }

        public void SetKeepAlive(Socket socket, bool enable)
        {
            _socketFactory.SetKeepAlive(socket, enable);
        }
    }
}