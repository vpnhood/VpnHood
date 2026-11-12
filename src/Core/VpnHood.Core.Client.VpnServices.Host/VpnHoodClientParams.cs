using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Represents what the host prepared for a single connection, handed to <see cref="VpnHoodClientFactory.Create"/>.
/// </summary>
/// <remarks>
/// The adapter arrives ready-made, as the null capture is the host's call; everything path- or store-backed stays raw
/// in <see cref="ServiceOptions"/>, so the factory, default or derived, resolves it.
/// </remarks>
public class VpnHoodClientParams
{
    /// <summary>Gets the options the app saved for the service.</summary>
    public required VpnServiceOptions ServiceOptions { get; init; }

    /// <summary>
    /// Gets the VPN adapter, ready-made, which the client disposes with itself where
    /// <see cref="ClientOptions.AutoDisposeVpnAdapter"/> says so, the default.
    /// </summary>
    public required IVpnAdapter VpnAdapter { get; init; }

    /// <summary>Gets the factory of the client's sockets.</summary>
    public required IClientSocketFactory SocketFactory { get; init; }

    /// <summary>Gets the VPN service's config folder, on this device only.</summary>
    public required string ConfigFolder { get; init; }

    /// <summary>
    /// Gets the proxies' folder, which travels with a backup (<see cref="VpnServiceFolders"/>).
    /// </summary>
    public required string DataFolder { get; init; }

    /// <summary>
    /// Gets what the server token's host name gave, known already after a server's move, which the connect takes rather
    /// than asking the name, or <see langword="null"/>.
    /// </summary>
    public HostNameAddresses? KnownHostAddresses { get; init; }
}
