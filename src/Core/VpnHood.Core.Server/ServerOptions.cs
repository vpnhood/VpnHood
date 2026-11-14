using Ga4.Trackers;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.SystemInformation;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Tunneling;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Server;

/// <summary>Represents the options of a <see cref="VpnHoodServer"/>.</summary>
public class ServerOptions
{
    internal ISocketFactory SocketFactory { get; init; } = new SystemSocketFactory();

    /// <summary>
    /// QUIC listener provider (e.g. MsQuicServer). Null disables QUIC; if QUIC endpoints are then
    /// configured the server reports them as unsupported on this platform.
    /// </summary>
    public IQuicServer? QuicServer { get; init; }
    /// <summary>
    /// Gets the tracker the server reports its anonymous events to, as its start and its clients' versions, or
    /// <see langword="null"/>.
    /// </summary>
    public ITracker? Tracker { get; init; }

    /// <summary>
    /// Gets what tells the server of its machine, or <see langword="null"/> for a basic one, of what .NET knows.
    /// </summary>
    public ISystemInfoProvider? SystemInfoProvider { get; init; }
    internal NetFilter NetFilter { get; init; } = new();

    // What the sessions may not reach unless the local network is included. A test's servers stand in for public
    // hosts at addresses that are not public, so its list leaves those out.
    internal IReadOnlyList<IpNetwork> NonPublicNetworks { get; init; } = IpNetwork.NonPublicNetworks;

    /// <summary>
    /// Gets what manages the machine's network, as the server's configuration asks, or <see langword="null"/> for none.
    /// </summary>
    public INetConfigurationProvider? NetConfigurationProvider { get; init; }

    /// <summary>
    /// Gets what manages the machine's swap memory, as the server's configuration asks, or <see langword="null"/> for
    /// none.
    /// </summary>
    public ISwapMemoryProvider? SwapMemoryProvider { get; init; }

    /// <summary>
    /// Gets the VPN adapter that takes the sessions' TCP packets out, with its NAT, or <see langword="null"/> for none,
    /// where the sessions that may forward TCP connections do so instead.
    /// </summary>
    public IVpnAdapter? VpnAdapter { get; init; }
    internal bool AutoDisposeAccessManager { get; init; } = true;
    internal TimeSpan ConfigureInterval { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>
    /// Gets the server's folder for its own files, as the sessions it recovers after a restart and its last
    /// configuration. The default is the current directory.
    /// </summary>
    public string StoragePath { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>
    /// Gets the folder whose files the server serves under <c>/downloads/</c>, or <see langword="null"/> for none.
    /// </summary>
    public string? DownloadsPath { get; init; }
    internal bool PublicIpDiscovery { get; init; } = true;
    /// <summary>
    /// Gets a configuration of the server's own, or <see langword="null"/>: each setting it sets takes the place of the
    /// access manager's, but for the certificates and the configuration code, which stay the access manager's.
    /// </summary>
    public ServerConfig? Config { get; init; }
    internal TimeSpan DeadSessionTimeout { get; init; } = TimeSpan.FromMinutes(5);
    internal TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(1);
    internal TimeSpan ServerHostDisposeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Gets the IPv4 network the server gives its clients' virtual addresses from. The default is
    /// <see cref="ServerTransportDefaults.VirtualIpNetworkV4"/>.
    /// </summary>
    public IpNetwork VirtualIpNetworkV4 { get; init; } = ServerTransportDefaults.VirtualIpNetworkV4;

    /// <summary>
    /// Gets the IPv6 network the server gives its clients' virtual addresses from. The default is
    /// <see cref="ServerTransportDefaults.VirtualIpNetworkV6"/>.
    /// </summary>
    public IpNetwork VirtualIpNetworkV6 { get; init; } = ServerTransportDefaults.VirtualIpNetworkV6;
    internal bool Http01ChallengeThrottling { get; init; } = true;
    internal TimeSpan ConfigErrorStrikeDuration { get; init; } = TimeSpan.FromDays(7);
    internal TimeSpan ConfigErrorRetryInterval { get; init; } = TimeSpan.FromMinutes(30); //todo: consider 6 hour
}