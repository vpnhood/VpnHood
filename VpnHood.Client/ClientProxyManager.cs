using PacketDotNet;
using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VpnHood.Client.Device;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client
{
    internal class ClientProxyManager : ProxyManager
    {
        private readonly IPacketCapture _packetCapture;
        private readonly SocketFactory _socketFactory;

        public ClientProxyManager(IPacketCapture packetCapture, SocketFactory socketFactory)
        {
            _packetCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        }

        // PacketCapture can not protect Ping so PingProxy does not work
        protected override Ping CreatePing()
        {
            throw new NotSupportedException($"{nameof(CreatePing)} is not supported by {nameof(ClientProxyManager)}!");
        }

        protected override UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            var udpClient = _socketFactory.CreateUdpClient(addressFamily);
            _packetCapture.ProtectSocket(udpClient.Client);
            return udpClient;
        }

        protected override void SendReceivedPacket(IPPacket ipPacket)
        {
            _packetCapture.SendPacketToInbound(ipPacket);
        }
    }
}