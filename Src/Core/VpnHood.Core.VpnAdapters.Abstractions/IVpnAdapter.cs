using System.Net;
using PacketDotNet;
using System.Net.Sockets;
using VpnHood.Core.Packets;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IDisposable
{
    event EventHandler<PacketReceivedEventArgs> PacketReceived;
    event EventHandler? Disposed;
    bool Started { get; }
    bool IsNatSupported { get; }
    bool CanProtectSocket { get; }
    bool ProtectSocket(Socket socket);
    bool ProtectSocket(Socket socket, IPAddress ipAddress);
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    void SendPacket(IPPacket ipPacket);
    void SendPackets(IList<IPPacket> ipPackets);
    IPAddress? GetPrimaryAdapterAddress(IPVersion ipVersion);
    bool IsIpVersionSupported(IPVersion ipVersion);
}