using PacketDotNet;
using System;

namespace VpnHood.Server
{ 
    public class PacketReceivedEventArgs : EventArgs
    {
        public IPPacket IpPacket { get; }

        public PacketReceivedEventArgs(IPPacket ipPacket)
        {
            IpPacket = ipPacket;
        }
    }
}