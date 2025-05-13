using VpnHood.Core.Packets.VhPackets;

namespace VpnHood.Core.Tunneling;

public static class PacketSenderQueuedExtensions
{
    public static void SendPacketsQueued(this IPacketSenderQueued sender, IList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            sender.SendPacketQueued(ipPacket);
    }
}