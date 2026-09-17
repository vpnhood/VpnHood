using VpnHood.AppLib.Contracts.Sessions;
using VpnHood.AppLib.Contracts.SplitTunneling;
using CoreClient = VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.DtoConverters;

// The split vocabulary in both directions. These are saved settings as well as wire values, so the
// member names on each side must stay identical - the switches below are exhaustive with no default
// arm so that a mode added to either side breaks this file rather than being silently mapped.
public static class SplitDtoConverters
{
    public static SplitDnsMode ToAppDto(this CoreClient.SplitDnsMode mode)
    {
        return mode switch {
            CoreClient.SplitDnsMode.IncludeAll => SplitDnsMode.IncludeAll,
            CoreClient.SplitDnsMode.DefaultRoute => SplitDnsMode.DefaultRoute
        };
    }

    public static CoreClient.SplitDnsMode ToEngine(this SplitDnsMode mode)
    {
        return mode switch {
            SplitDnsMode.IncludeAll => CoreClient.SplitDnsMode.IncludeAll,
            SplitDnsMode.DefaultRoute => CoreClient.SplitDnsMode.DefaultRoute
        };
    }

    public static SplitUnsupportedIpMode ToAppDto(this CoreClient.SplitUnsupportedIpMode mode)
    {
        return mode switch {
            CoreClient.SplitUnsupportedIpMode.Exclude => SplitUnsupportedIpMode.Exclude,
            CoreClient.SplitUnsupportedIpMode.Block => SplitUnsupportedIpMode.Block
        };
    }

    public static CoreClient.SplitUnsupportedIpMode ToEngine(this SplitUnsupportedIpMode mode)
    {
        return mode switch {
            SplitUnsupportedIpMode.Exclude => CoreClient.SplitUnsupportedIpMode.Exclude,
            SplitUnsupportedIpMode.Block => CoreClient.SplitUnsupportedIpMode.Block
        };
    }

    public static DnsSelection ToAppDto(this CoreClient.DnsSelection selection)
    {
        return selection switch {
            CoreClient.DnsSelection.UserDns => DnsSelection.UserDns,
            CoreClient.DnsSelection.ServerDns => DnsSelection.ServerDns,
            CoreClient.DnsSelection.GoogleDns => DnsSelection.GoogleDns
        };
    }

    public static DnsConfig ToAppDto(this CoreClient.DnsConfig dnsConfig)
    {
        return new DnsConfig {
            DnsSelection = dnsConfig.DnsSelection.ToAppDto(),
            IsIncludedInVpn = dnsConfig.IsIncludedInVpn,
            IsUserSuppressed = dnsConfig.IsUserSuppressed,
            DnsServers = dnsConfig.DnsServers
        };
    }
}
