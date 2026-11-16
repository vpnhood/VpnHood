using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Proxies.Management.Abstractions.Options;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the settings the app changes on a running connection (<see cref="Requests.ApiReconfigureRequest"/>),
/// each meaning what the option of its name means in <see cref="ClientOptions"/>.
/// </summary>
/// <remarks>
/// Each carries every setting the session takes live, so the latest is all there is to keep — but for the proxies'
/// reset (<c>ProxyOptions.ResetStates</c>), which the app sends once, and which a newer one that comes before the
/// session is up drops.
/// </remarks>
public class ClientReconfigureParams
{
    /// <summary>
    /// Gets or sets a value indicating whether the client drops QUIC (<see cref="ClientOptions.DropQuic"/>).
    /// </summary>
    public required bool DropQuic { get; set; }

    /// <summary>Gets or sets a value indicating whether the client drops UDP (<see cref="ClientOptions.DropUdp"/>).</summary>
    public required bool DropUdp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client forwards TCP (<see cref="ClientOptions.UseTcpForwarding"/>).
    /// </summary>
    public required bool UseTcpForwarding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the person lets their use be reported anonymously
    /// (<see cref="ClientOptions.AllowAnonymousTracker"/>); the service's trackers take it at once.
    /// </summary>
    public required bool AllowAnonymousTracker { get; set; }

    /// <summary>
    /// Gets or sets the protocol of the tunnel's packet channels (<see cref="ClientOptions.ChannelProtocol"/>).
    /// </summary>
    public required ChannelProtocol ChannelProtocol { get; set; }

    /// <summary>
    /// Gets or sets what happens to a destination the server does not route
    /// (<see cref="ClientOptions.UnroutedIpMode"/>).
    /// </summary>
    public required SplitUnsupportedIpMode UnroutedIpMode { get; set; }

    /// <summary>
    /// Gets or sets what happens to IPv6 when the server carries none (<see cref="ClientOptions.UnsupportedIpV6Mode"/>).
    /// </summary>
    public required SplitUnsupportedIpMode UnsupportedIpV6Mode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the kill switch is on (<see cref="ClientOptions.UseKillSwitch"/>). Where a
    /// connect of the service holds the tunnel, before the session is up too, the service puts its block up or takes it
    /// down at once. Otherwise it does nothing: where no connect holds the tunnel, as the block is then the app's; under
    /// a null capture; and on a device with no kill switch.
    /// </summary>
    public required bool UseKillSwitch { get; set; }

    /// <summary>
    /// Gets or sets the options of the proxies to connect through (<see cref="VpnServiceOptions.ProxyOptions"/>). The
    /// default is none.
    /// </summary>
    public required ProxyOptions ProxyOptions { get; set; } = new();
}