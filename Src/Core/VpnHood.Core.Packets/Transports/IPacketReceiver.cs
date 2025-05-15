namespace VpnHood.Core.Packets.Transports;

public interface IPacketReceiver : IDisposable
{
    DateTime LastReceivedTime { get; }
    event EventHandler<PacketReceivedEventArgs>? PacketReceived;
}