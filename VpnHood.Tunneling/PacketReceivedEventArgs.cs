using System;
using PacketDotNet;

namespace VpnHood.Tunneling;

public class PacketReceivedEventArgs : EventArgs
{
    public PacketReceivedEventArgs(IPPacket ipPacket)
    {
        IpPacket = ipPacket;
    }

    public IPPacket IpPacket { get; }
}