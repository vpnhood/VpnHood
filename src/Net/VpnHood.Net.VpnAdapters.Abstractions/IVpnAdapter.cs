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
    /// The host moved to other networks and the adapter took their primary IPs, without a restart: what was
    /// connected from the old ones is dead, though an OS may keep it open (Linux does).
    /// </summary>
    event EventHandler? PrimaryNetworkChanged;
    /// <summary>
    /// The adapter stopped on its own, by I/O errors or a restart that could not start it again, and
    /// stays down, as nothing else starts it: its owner must act, a client by ending its session. Not
    /// raised with AutoRestart, where the adapter retries instead.
    /// </summary>
    event EventHandler<Exception>? Failed;
    /// <summary>
    /// The adapter is up, with these options: the owner's start and each restart, which may make the
    /// interface anew. What is pointed at the interface from outside, a kill switch's block, follows it.
    /// </summary>
    event EventHandler<VpnAdapterOptions>? Started;
    bool IsStarted { get; }
    /// <summary>
    /// Whether the adapter's NAT translates this IP version; a server sends one it does not by its forwarders.
    /// </summary>
    bool IsNatSupported(IpVersion ipVersion);
    bool CanProtectSocket { get; }
    bool ProtectSocket(Socket socket);
    bool ProtectSocket(Socket socket, IPAddress ipAddress);
    /// <summary>
    /// The DNS servers of the network the tunnel runs over, which a protected socket reaches outside the tunnel;
    /// not the tunnel's own. None where they are unknown.
    /// </summary>
    IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers();
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