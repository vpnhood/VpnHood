using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Net.VpnAdapters.Abstractions;

public interface IVpnAdapter : IPacketTransport
{
    event EventHandler? Disposed;
    event EventHandler? PrimaryAdapterIpChanged;
    /// <summary>
    /// The adapter stopped on its own, by I/O errors or a restart that could not start it again, and
    /// stays down, as nothing else starts it: its owner must act, a client by ending its session. Not
    /// raised with AutoRestart, where the adapter retries instead.
    /// </summary>
    event EventHandler<Exception>? Failed;
    bool IsStarted { get; }
    /// <summary>
    /// Whether the adapter's NAT translates this IP version; a server sends one it does not by its proxy.
    /// </summary>
    bool IsNatSupported(IpVersion ipVersion);
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