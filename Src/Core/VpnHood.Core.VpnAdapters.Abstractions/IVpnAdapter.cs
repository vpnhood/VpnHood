using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IPacketTransport
{
    event EventHandler? Disposed;
    bool IsStarted { get; }
    bool IsNatSupported { get; }
    bool CanProtectSocket { get; }
    bool ProtectSocket(Socket socket);
    bool ProtectSocket(Socket socket, IPAddress ipAddress);
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion);
    bool IsIpVersionSupported(IpVersion ipVersion);
}