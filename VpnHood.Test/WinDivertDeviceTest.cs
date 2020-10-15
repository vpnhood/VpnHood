using VpnHood.Client;
using PacketDotNet;
using System.Linq;
using System.Net;

namespace VpnHood.Test
{
    public class WinDivertDeviceTest : WinDivertDevice
    {
        public const int ServerTimeToLife = 140;
        public const int ServerMinPort = 33000;
        public const int ServerMaxPort = 34000;
        private readonly IPAddress[] _testIpAddresses;

        public WinDivertDeviceTest(IPAddress[] testIpAddresses)
        {
            _testIpAddresses = testIpAddresses;
        }

        protected override void ProcessPacket(IPPacket ipPacket, SharpPcap.CaptureEventArgs e)
        {
            bool sendOut;

            // ignore non test ips
            if (!_testIpAddresses.Any(x => x.Equals(ipPacket.SourceAddress) || x.Equals(ipPacket.DestinationAddress)))
            {
                sendOut = true;
            }

            // let server outbound call go out: Tcp
            else if (ipPacket.Protocol == ProtocolType.Tcp)
            {
                var tcpPacket = ipPacket.Extract<TcpPacket>();
                sendOut = tcpPacket.SourcePort >= ServerMinPort && tcpPacket.SourcePort <= ServerMaxPort;
            }
            
            // let server outbound call go out: Udp
            else if (ipPacket.Protocol == ProtocolType.Udp)
            {
                var udpPacket = ipPacket.Extract<UdpPacket>();
                sendOut = udpPacket.SourcePort >= ServerMinPort && udpPacket.SourcePort <= ServerMaxPort;
            }
            
            // let server outbound call go out: Icmp
            else if (ipPacket.Protocol == ProtocolType.Icmp)
            {
                //var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
                sendOut = ipPacket.TimeToLive == (ServerTimeToLife - 1);
            }
            
            // drop direct packets for test addresses which client doesn't send to tunnel
            else
            {
                return;
            }

            // let packet go out
            if (sendOut) 
            {
                _device.SendPacket(e.Packet.GetPacket());
            }
            // Tunnel the packet
            else
            {
                base.ProcessPacket(ipPacket, e);
            }
        }
    }
}
