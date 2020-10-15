using PacketDotNet;
using System;

namespace VpnHood.Server
{ 
    public class PingCompletedEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }

        public PingCompletedEventArgs(IPPacket ipPacket)
        {
            IpPacket = ipPacket;
        }
    }
}