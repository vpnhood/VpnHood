using PacketDotNet;
using System.Net.Sockets;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsNatSupported { get; }
    bool CanProtectClient { get; }
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    TcpClient CreateProtectedTcpClient(AddressFamily addressFamily);
    UdpClient CreateProtectedUdpClient(AddressFamily addressFamily);
    void SendPacket(IPPacket ipPacket);
    void SendPackets(IList<IPPacket> ipPackets);
}