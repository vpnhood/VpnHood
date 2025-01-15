using PacketDotNet;

namespace VpnHood.Core.Server.Abstractions;

public interface ITunProvider
{
    event EventHandler<IPPacket> OnPacketReceived;
    void SendPacket(IPPacket ipPacket);
}