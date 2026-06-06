using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.PacketTransports;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public interface IVpnAdapter : IPacketTransport
{
    event EventHandler? Disposed;
    event EventHandler? PrimaryAdapterIpChanged;
    bool IsStarted { get; }
    bool IsNatSupported { get; }
    bool CanProtectSocket { get; }
    bool ProtectSocket(Socket socket);
    bool ProtectSocket(Socket socket, IPAddress ipAddress);
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);
    void Stop();
    IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion);
    /// <summary>
    /// Checks if the physical device network/primary adapter supports the specified IP version.
    /// </summary>
    /// <remarks>
    /// This is used to determine if the primary physical interface has connectivity/IP configuration
    /// for the given IP version. It is typically used for routing and split-tunneling decisions,
    /// and to check if the channel to the server can be established using this IP version.
    /// It does NOT represent the capabilities of the virtual VPN adapter inside the tunnel.
    /// </remarks>
    bool IsIpVersionSupported(IpVersion ipVersion);
}