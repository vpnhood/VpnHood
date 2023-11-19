using System;
using PacketDotNet;

namespace VpnHood.Client.Device;

public class PacketReceivedEventArgs : EventArgs
{
    public IPPacket[] IpPackets { get; }
    public IPacketCapture PacketCapture { get; }

    public PacketReceivedEventArgs(IPPacket[] ipPackets, IPacketCapture packetCapture)
    {
        IpPackets = ipPackets;
        PacketCapture = packetCapture;
    }
}