using System.Linq;
using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Test
{
    internal class TestPacketCapture : WinDivertPacketCapture
    {
        private readonly TestDeviceOptions _deviceOptions;
        private IPAddress[]? _dnsServers;

        public TestPacketCapture(TestDeviceOptions deviceOptions)
        {
            _deviceOptions = deviceOptions;
        }

        public override bool IsDnsServersSupported => _deviceOptions.IsDnsServerSupported;

        public override IPAddress[]? DnsServers
        {
            get => IsDnsServersSupported ? _dnsServers : base.DnsServers;
            set
            {
                if (IsDnsServersSupported)
                    _dnsServers = value;
                else
                    base.DnsServers = value;
            }
        }

        public override bool CanSendPacketToOutbound => _deviceOptions.CanSendPacketToOutbound;
        public override bool CanProtectSocket => !_deviceOptions.CanSendPacketToOutbound;

        protected override void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
        {
            bool ignore = false;

            ignore |= 
                ipPacket.Extract<UdpPacket>()?.DestinationPort == 53 &&
                _deviceOptions.CaptureDnsAddresses != null &&
                _deviceOptions.CaptureDnsAddresses.All(x => !x.Equals(ipPacket.DestinationAddress));

            ignore |= TestNetProtector.IsProtectedPacket(ipPacket);
            
            // ignore protected packets
            if (ignore)
                SendPacketToOutbound(ipPacket);
            else
                base.ProcessPacketReceivedFromInbound(ipPacket);
        }

        public override void ProtectSocket(Socket socket)
        {
            if (CanProtectSocket)
                TestNetProtector.ProtectSocket(socket);
            else
                base.ProtectSocket(socket);
        }
    }
}