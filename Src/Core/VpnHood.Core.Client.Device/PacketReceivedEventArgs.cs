﻿using PacketDotNet;

namespace VpnHood.Core.Client.Device;

public sealed class PacketReceivedEventArgs(IList<IPPacket> ipPackets, IPacketCapture packetCapture) : EventArgs
{
    public IList<IPPacket> IpPackets { get; } = ipPackets;
    public IPacketCapture PacketCapture { get; } = packetCapture;
}