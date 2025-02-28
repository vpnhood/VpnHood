using PacketDotNet;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceivedFromInbound;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsDnsServersSupported { get; }
    bool CanProtectSocket { get; }
    bool CanSendPacketToOutbound { get; }
    Task StartCapture(VpnAdapterOptions adapterOptions, CancellationToken cancellationToken);
    Task StopCapture(CancellationToken cancellationToken);
    void ProtectSocket(System.Net.Sockets.Socket socket);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> packets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
}