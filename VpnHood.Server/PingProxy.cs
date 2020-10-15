using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace VpnHood.Server
{
    public class PingProxy : IDisposable
    {
        private readonly int _timeout = 6000;
        private readonly Ping _ping;
        public event EventHandler<PingCompletedEventArgs> OnPingCompleted;

        public PingProxy()
        {
            _ping = new Ping();
            _ping.PingCompleted += Ping_PingCompleted;
        }

        private void Ping_PingCompleted(object sender, System.Net.NetworkInformation.PingCompletedEventArgs e)
        {
            var pingReply = e.Reply;
            var ipPacket = (IPv4Packet)e.UserState;
            if (pingReply?.Status != IPStatus.Success)
                return;

            // create the echoReply
            var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
            icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
            icmpPacket.Data = pingReply.Buffer;
            Util.UpdateICMPChecksum(icmpPacket);

            ipPacket.DestinationAddress = ipPacket.SourceAddress;
            ipPacket.SourceAddress = pingReply.Address;
            ipPacket.UpdateIPChecksum();
            ipPacket.UpdateCalculatedValues();

            OnPingCompleted?.Invoke(this, new PingCompletedEventArgs(ipPacket));
        }

        public void Send(IPv4Packet ipPacket)
        {
            // We should not use Task due its stack usage, this method is called by many session each many times!
            var icmpPacket = ipPacket.Extract<IcmpV4Packet>();
            var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, (ipPacket.FragmentFlags & 0x2) != 0);
            _ping.SendAsync(ipPacket.DestinationAddress, _timeout, icmpPacket.Data, pingOptions, ipPacket);
        }
        public void Dispose()
        {
            _ping.PingCompleted -= Ping_PingCompleted;
            _ping.Dispose();
        }
    }
}

