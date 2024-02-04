using PacketDotNet;

namespace VpnHood.Client.Device;

public sealed class PacketReceivedEventArgs(IPPacket[] ipPackets, IPacketCapture packetCapture) : EventArgs
{
    public IPPacket[] IpPackets { get; } = ipPackets;
    public IPacketCapture PacketCapture { get; } = packetCapture;
}