﻿using PacketDotNet;

namespace VpnHood.Core.Client.Device;

public sealed class PacketReceivedEventArgs(IList<IPPacket> ipPackets, IVpnAdapter vpnAdapter) : EventArgs
{
    public IList<IPPacket> IpPackets { get; } = ipPackets;
    public IVpnAdapter VpnAdapter { get; } = vpnAdapter;
}