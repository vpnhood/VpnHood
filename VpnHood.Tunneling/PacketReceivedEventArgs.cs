using PacketDotNet;
using System;

namespace VpnHood.Tunneling
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