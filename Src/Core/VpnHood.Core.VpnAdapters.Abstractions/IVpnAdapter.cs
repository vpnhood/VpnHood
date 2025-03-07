using PacketDotNet;
using System.Net.Sockets;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsDnsServerSupported { get; }
    bool IsNatSupported { get; }
    bool CanSendPacketToOutbound { get; }
    bool CanProtectClient { get; }
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    TcpClient CreateProtectedTcpClient(AddressFamily addressFamily);
    UdpClient CreateProtectedUdpClient(AddressFamily addressFamily);
    void SendPacketToInbound(IPPacket ipPacket);
    void SendPacketToInbound(IList<IPPacket> ipPackets);
    void SendPacketToOutbound(IPPacket ipPacket);
    void SendPacketToOutbound(IList<IPPacket> ipPackets);
    void SendPacket(IPPacket ipPacket);
}