using PacketDotNet;

namespace VpnHood.Core.Server.Abstractions;

public interface ITunProvider : IDisposable
{
    event EventHandler<IPPacket> OnPacketReceived;
    Task SendPacket(IPPacket ipPacket);
}