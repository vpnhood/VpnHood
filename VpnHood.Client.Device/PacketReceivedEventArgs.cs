using System;
using PacketDotNet;

namespace VpnHood.Client.Device;

public class PacketReceivedEventArgs : EventArgs
{

    public required IPPacket[] IpPackets { get; init; }
    public required IPacketCapture PacketCapture { get; init;}
}