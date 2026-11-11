using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.PacketTransports;

namespace VpnHood.Net.VpnAdapters.Abstractions;

/// <summary>
/// Represents a VPN adapter: a virtual network interface that the host's traffic is routed into, whose packets
/// a VPN reads and writes as an <see cref="IPacketTransport"/>.
/// </summary>
/// <remarks>
/// An adapter is made once and started and stopped many times. <see cref="TunVpnAdapter"/> is the base class of
/// VpnHood's adapters, one per platform.
/// </remarks>
public interface IVpnAdapter : IPacketTransport
{
    /// <summary>
    /// Occurs when the adapter is disposed.
    /// </summary>
    event EventHandler? Disposed;

    /// <summary>
    /// Occurs when the adapter finds new primary addresses without a restart, as when the OS rotates a temporary
    /// IPv6 address within its network.
    /// </summary>
    /// <remarks>
    /// It is raised on the thread that saw the network change, only when the addresses found differ from the last
    /// ones; not for the start's own discovery, nor when a stop clears them.
    /// </remarks>
    event EventHandler? PrimaryAdapterIpChanged;

    /// <summary>
    /// Occurs when the host moved to other networks and the adapter took their primary addresses without a
    /// restart: what was connected from the old networks may be dead, though an OS may keep it open, as Linux does.
    /// </summary>
    /// <remarks>
    /// It is raised on the thread that saw the network change. The primary addresses may stay the same.
    /// </remarks>
    event EventHandler? PrimaryNetworkChanged;

    /// <summary>
    /// Occurs when the adapter stopped on its own and stays down: by repeated I/O errors, by losing its interface,
    /// or by a restart that could not start it again. Its owner must act, a client by ending its session.
    /// </summary>
    /// <remarks>
    /// It is raised on a thread-pool task, so the adapter may have changed by the time a handler runs: one already
    /// queued may arrive after a stop or disposal. It is not raised with <see cref="VpnAdapterSettings.AutoRestart"/>,
    /// where the adapter retries instead. The owner's stop or disposal does not raise it, nor does a failed owner's
    /// start, whose caller gets the exception.
    /// </remarks>
    event EventHandler<Exception>? Failed;

    /// <summary>
    /// Occurs when the adapter is up, with the options it runs with: after the owner's start and after each
    /// restart, which may make the interface anew. What points at the interface from outside, such as a kill
    /// switch's block, must follow it.
    /// </summary>
    /// <remarks>
    /// It is raised on the starting thread, before the start completes; during a restart, under the adapter's restart
    /// lock, so a handler must not stop or dispose the adapter before returning.
    /// </remarks>
    event EventHandler<VpnAdapterOptions>? Started;

    /// <summary>
    /// Gets a value indicating whether the adapter is started.
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// Determines whether the adapter's NAT translates an IP version. A server forwards a version the adapter
    /// does not translate by its own forwarders.
    /// </summary>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns><see langword="true"/> if the adapter translates it; otherwise, <see langword="false"/>.</returns>
    bool IsNatSupported(IpVersion ipVersion);

    /// <summary>
    /// Gets a value indicating whether the adapter can keep a socket's traffic out of the tunnel.
    /// </summary>
    bool CanProtectSocket { get; }

    /// <summary>
    /// Keeps a socket's traffic out of the tunnel, so it reaches the network directly. Call it before the
    /// socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <returns>
    /// <see langword="true"/> if the socket is protected; <see langword="false"/> if it cannot be, for example
    /// when the host has no primary address of the socket's IP version.
    /// </returns>
    bool ProtectSocket(Socket socket);

    /// <summary>
    /// Keeps a socket's traffic to a given address out of the tunnel, so it reaches the network directly. Call it
    /// before the socket is bound or connected.
    /// </summary>
    /// <param name="socket">The socket, not bound yet.</param>
    /// <param name="ipAddress">The remote address the socket will reach.</param>
    /// <returns>
    /// <see langword="true"/> if the socket is protected; <see langword="false"/> if it cannot be, or need not be.
    /// </returns>
    bool ProtectSocket(Socket socket, IPAddress ipAddress);

    /// <summary>
    /// Gets the DNS servers of the network the tunnel runs over, which a protected socket reaches outside the
    /// tunnel; not the tunnel's own.
    /// </summary>
    /// <returns>The DNS servers; none where they are unknown.</returns>
    IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers();

    /// <summary>
    /// Starts the adapter: makes its interface with its addresses, DNS servers, routes, NAT and app filter, opens
    /// it and starts reading packets.
    /// </summary>
    /// <remarks>
    /// Await one start before the next. From the start on, the adapter keeps itself started until
    /// <see cref="Stop"/>, disposal, or a failure it does not recover from (without
    /// <see cref="VpnAdapterSettings.AutoRestart"/>), which raises <see cref="Failed"/>.
    /// </remarks>
    /// <param name="options">The options this run uses, which each restart uses again.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. It completes when the adapter is up.</returns>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled, or a stop or disposal came during the start.</exception>
    Task Start(VpnAdapterOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the adapter and removes its interface, with its routes and DNS servers. A restart under way ends
    /// first, and a pending retry is dropped, even when the adapter is already stopped.
    /// </summary>
    /// <remarks>
    /// The adapter counts as stopped even when closing or removing the interface fails: the error reaches the
    /// caller.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The adapter is disposed.</exception>
    void Stop();

    /// <summary>
    /// Gets the host's primary address of an IP version: the one it reaches the internet from, outside the
    /// tunnel.
    /// </summary>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns>The address, or <see langword="null"/> when the host has none of that version.</returns>
    IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion);

    /// <summary>
    /// Determines whether the host's network, outside the tunnel, supports an IP version.
    /// </summary>
    /// <remarks>
    /// It tells whether the primary interface has an address of that version, for routing and split-tunneling
    /// decisions and to know whether the server can be reached with it. It says nothing of what the tunnel
    /// itself carries.
    /// </remarks>
    /// <param name="ipVersion">The IP version.</param>
    /// <returns><see langword="true"/> if the host's network supports it; otherwise, <see langword="false"/>.</returns>
    bool IsIpVersionSupported(IpVersion ipVersion);

    /// <summary>
    /// Looks at the host's network again for its primary addresses, as a change of the host's addresses makes the
    /// adapter do, and raises <see cref="PrimaryAdapterIpChanged"/> when they differ from the last ones.
    /// </summary>
    /// <remarks>
    /// For a caller that needs <see cref="GetPrimaryAdapterAddress"/> and <see cref="IsIpVersionSupported"/> to
    /// tell the network as it is now: the OS may move the host's traffic to another network without an address
    /// change. An adapter that is stopped, starting or restarting is left alone: its start finds the addresses.
    /// </remarks>
    void RefreshPrimaryAdapterIps();
}
