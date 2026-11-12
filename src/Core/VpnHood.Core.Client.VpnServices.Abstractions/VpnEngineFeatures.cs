namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the optional parts of the contract a VPN engine has (<see cref="VpnEngineInfo.Features"/>); every engine
/// has the rest.
/// </summary>
/// <remarks>
/// A feature a report leaves out is one the engine lacks, and one a reader does not know it skips. The app asks an
/// engine for none it lacks, and an engine asked for one fails the connect rather than ignoring it, as a kill switch or
/// a split that silently did nothing would leak.
/// </remarks>
public class VpnEngineFeatures
{
    /// <summary>
    /// Gets a value indicating whether the engine splits the tunnel by app:
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.IncludeApps"/> and
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.ExcludeApps"/>.
    /// </summary>
    public bool SplitApps { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine has the kill switch's block:
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.UseKillSwitch"/>.
    /// </summary>
    public bool KillSwitch { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine connects through the person's proxies:
    /// <see cref="VpnServiceOptions.ProxyOptions"/>.
    /// </summary>
    public bool Proxies { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine speaks QUIC to the server:
    /// <see cref="VpnHood.Core.Common.Messaging.ChannelProtocol.Quic"/>, which an engine without it takes as TCP, as it
    /// takes a server's own lack of QUIC.
    /// </summary>
    public bool Quic { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine takes part in the ads a plan asks for: it waits in
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientState.WaitingForAd"/> and takes
    /// <see cref="Requests.ApiSetWaitForAdRequest"/>, <see cref="Requests.ApiSetAdOkRequest"/> and
    /// <see cref="Requests.ApiAdFailedRequest"/>.
    /// </summary>
    public bool Ads { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine takes a start the system or the app makes with no one asking:
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.AllowAlwaysOn"/> and
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.IsAlwaysOn"/>.
    /// </summary>
    public bool AlwaysOn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the engine finds the server's endpoints as told:
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.EndPointStrategy"/>.
    /// </summary>
    public bool EndPointStrategy { get; init; }

    /// <summary>
    /// Gets the kinds of tracker the engine makes (<see cref="Tracking.TrackerKinds"/>,
    /// <see cref="VpnHood.Core.Client.Abstractions.TrackerFactoryInfo.Kind"/>). The default is none.
    /// </summary>
    public IReadOnlyList<string> TrackerKinds { get; init; } = [];

    /// <summary>Gets every feature this library knows: its own engine's.</summary>
    public static VpnEngineFeatures All { get; } = new() {
        SplitApps = true,
        KillSwitch = true,
        Proxies = true,
        Quic = true,
        Ads = true,
        AlwaysOn = true,
        EndPointStrategy = true,
        TrackerKinds = [Tracking.TrackerKinds.Ga4, Tracking.TrackerKinds.DotNet]
    };
}
