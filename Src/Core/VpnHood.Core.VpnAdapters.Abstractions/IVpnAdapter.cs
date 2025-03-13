using PacketDotNet;
using System.Net.Sockets;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsNatSupported { get; }
    bool CanProtectSocket { get; }
    void ProtectSocket(Socket socket);
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    void SendPacket(IPPacket ipPacket);
    void SendPackets(IList<IPPacket> ipPackets);
}