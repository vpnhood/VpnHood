using System.Net;
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
    void SendPacket(IpPacket ipPacket);
    void SendPackets(IList<IpPacket> ipPackets);
    IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion);
    bool IsIpVersionSupported(IpVersion ipVersion);
}