using VpnHood.AppLib.Settings;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Dtos;

// The EFFECTIVE split picture, computed from settings and the live session so the UI never holds
// business logic: list items bind their on/off display to these flags and their mode labels to the
// effective modes below (stored values survive in UserSettings), and the home screen shows a split
// badge on IsSplittingTraffic alone. When the super toggle is off every flag but IsLocalNetworkSplit
// is false, so the badge dies by itself.
public class SplitTunnelingState
{
    // echo of the super toggle, for enabling/disabling the split pages' items
    public required bool IsEnabled { get; init; }

    // drives the split badge: some PUBLIC traffic can travel outside the tunnel right now.
    // IsLocalNetworkSplit deliberately does not count — LAN traffic cannot expose the public IP.
    public required bool IsSplittingTraffic { get; init; }

    public required bool IsAppSplit { get; init; }
    public required bool IsCountrySplit { get; init; }
    public required bool IsIpViaAppSplit { get; init; }
    public required bool IsIpViaDeviceSplit { get; init; }
    public required bool IsDomainSplit { get; init; }
    public required bool IsLocalNetworkSplit { get; init; }

    // IPv6 is bypassing the tunnel: the server cannot carry it and the user chose Exclude over Block
    public required bool IsIpV6Split { get; init; }

    // the server's declarations leave public destinations out while splitting is allowed
    public required bool IsSplitByServer { get; init; }

    // label detail for the split pages' items: the EFFECTIVE modes, so a silenced item shows what
    // is actually in force — the toggle off shows Block/IncludeAll no matter what is stored
    public required SplitCountryMode CountryMode { get; init; }
    public required IReadOnlyList<string> Countries { get; init; }
    public required SplitDnsMode DnsMode { get; init; }
    public required SplitUnsupportedIpMode UnsupportedIpV6Mode { get; init; }
}
