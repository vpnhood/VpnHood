using PacketDotNet;
using System.Linq;
using System.Net;
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

namespace VpnHood.Test
{
    class TestPacketCapture : WinDivertPacketCapture
    {
        private readonly TestDeviceOptions _deviceOptions;
        private IPAddress[] dnsServers;

        public TestPacketCapture(TestDeviceOptions deviceOptions)
        {
            _deviceOptions = deviceOptions;
            IncludeNetworks = deviceOptions.TestIpAddresses.Select(x => new IpNetwork(x)).ToArray();
        }

        protected override void ProcessPacket(IPPacket ipPacket)
        {
            if (TestNetProtector.IsProtectedPacket(ipPacket))
                SendPacketToOutbound(ipPacket);
            else
                base.ProcessPacket(ipPacket);
        }

        public override bool IsDnsServersSupported => _deviceOptions.IsDnsServerSupported;
        public override IPAddress[] DnsServers
        {
            get => IsDnsServersSupported ? dnsServers : base.DnsServers;
            set
            {
                if (IsDnsServersSupported)
                    dnsServers = value;
                else
                    base.DnsServers = value;
            }
        }
        public override bool CanSendPacketToOutbound => _deviceOptions.CanSendPacketToOutbound;
        public override bool CanProtectSocket => !_deviceOptions.CanSendPacketToOutbound;
        public override void ProtectSocket(System.Net.Sockets.Socket socket)
        {
            if (CanProtectSocket)
                TestNetProtector.ProtectSocket(socket);
            else
                base.ProtectSocket(socket);
        }
    }
}
