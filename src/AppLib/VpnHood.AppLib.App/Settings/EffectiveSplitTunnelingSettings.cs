using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Settings;

// The resolved view of SplitTunnelingSettings and the only shape consumers may act on: holding one
// of these proves BOTH gates have been applied — the super toggle and the premium plan — so neither
// can be forgotten nor applied twice. Immutable on purpose — it is a verdict, not a place to store
// anything. Built exclusively by SplitTunnelingSettings.ToEffective(IPremiumFeatureChecker).
// AppMode/Apps and UseLocalNetwork survive a disabled toggle: neither can expose the public IP of
// the traffic that stays in the tunnel, so neither is sacrificed for the guarantee.
public class EffectiveSplitTunnelingSettings
{
    public required bool Enabled { get; init; }
    public required SplitAppMode AppMode { get; init; }
    public required string[] Apps { get; init; }
    public required SplitCountryMode CountryMode { get; init; }
    public required string[] Countries { get; init; }
    public required bool UseIpViaApp { get; init; }
    public required bool UseIpViaDevice { get; init; }
    public required bool UseDomain { get; init; }
    public required bool UseLocalNetwork { get; init; }
    public required SplitDnsMode DnsMode { get; init; }
    public required SplitUnsupportedIpMode UnsupportedIpMode { get; init; }
    public required SplitUnsupportedIpMode UnsupportedIpV6Mode { get; init; }
}
