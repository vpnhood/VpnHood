using System;
using PacketDotNet;

namespace VpnHood.Client.Device;

public class PacketReceivedEventArgs : EventArgs
{
    public PacketReceivedEventArgs(IPPacket[] ipPackets, IPacketCapture packetCapture)
    {
        IpPackets = ipPackets ?? throw new ArgumentNullException(nameof(ipPackets));
        PacketCapture = packetCapture ?? throw new ArgumentNullException(nameof(packetCapture));
    }

    public IPPacket[] IpPackets { get; }
    public IPacketCapture PacketCapture { get; }
}