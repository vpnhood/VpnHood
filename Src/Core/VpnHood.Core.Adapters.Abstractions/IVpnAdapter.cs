using PacketDotNet;

namespace VpnHood.Core.Adapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceivedFromInbound;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsDnsServersSupported { get; }
    bool IsMtuSupported { get; }
    bool CanProtectSocket { get; }
    bool CanSendPacketToOutbound { get; }
    void StartCapture(VpnAdapterOptions adapterOptions);
    void StopCapture();
    void ProtectSocket(System.Net.Sockets.Socket socket);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> packets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
}