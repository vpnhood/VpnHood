using PacketDotNet;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceivedFromInbound;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsDnsServerSupported { get; }
    bool IsNatSupported { get; }
    bool CanProtectSocket { get; }
    bool CanSendPacketToOutbound { get; }
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    void ProtectSocket(System.Net.Sockets.Socket socket);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> ipPackets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
}