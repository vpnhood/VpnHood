namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The optional parts of the contract an engine has (VpnEngineInfo.Features); every engine has the rest. A feature
// a report leaves out is one the engine lacks, and one a reader does not know it skips. The app asks an engine for
// none it lacks, and an engine asked for one fails the connect rather than ignoring it, as a kill switch or a
// split that silently did nothing would leak.
public class VpnEngineFeatures
{
    // split tunneling by app: ClientOptions.IncludeApps and ExcludeApps
    public bool SplitApps { get; init; }

    // the kill switch's block: ClientOptions.UseKillSwitch
    public bool KillSwitch { get; init; }

    // a connection through the person's proxies: VpnServiceOptions.ProxyOptions
    public bool Proxies { get; init; }

    // QUIC to the server: ChannelProtocol.Quic, which an engine without it takes as TCP, as it takes a server's
    // own lack of QUIC
    public bool Quic { get; init; }

    // the ads a plan asks for: ClientState.WaitingForAd and the three ad requests
    public bool Ads { get; init; }

    // a start the system or the app makes with no one asking: ClientOptions.AllowAlwaysOn and IsAlwaysOn
    public bool AlwaysOn { get; init; }

    // how the server's endpoints are found: ClientOptions.EndPointStrategy
    public bool EndPointStrategy { get; init; }

    // the kinds of tracker it makes (Tracking.TrackerKinds, TrackerFactoryInfo.Kind)
    public IReadOnlyList<string> TrackerKinds { get; init; } = [];

    // every feature this library knows: its own engine's
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
