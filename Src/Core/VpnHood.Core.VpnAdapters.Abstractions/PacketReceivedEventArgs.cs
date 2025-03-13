using PacketDotNet;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public sealed class PacketReceivedEventArgs(IList<IPPacket> ipPackets) : EventArgs
{
    public IList<IPPacket> IpPackets { get; } = ipPackets;
}