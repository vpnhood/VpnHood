using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Settings;

public class SplitTunnelingSettings
{
    // The super toggle: is splitting allowed at all? Off is the one-tap guarantee that no traffic of
    // the tunneled apps escapes — every split below turns inert and both unsupported-ip modes act as
    // Block regardless of their stored values. Two splits are deliberately exempt because neither can
    // expose the public IP of the traffic this app protects: UseLocalNetwork (LAN traffic never
    // reaches the internet) and AppMode/Apps (an app kept out of the VPN is a per-app decision — the
    // user picked exactly which apps opt out, and the rest of the device is unaffected).
    public bool Enabled { get; set; } = true;
    public SplitAppMode AppMode { get; set; } = SplitAppMode.All;
    public string[] Apps { get; set; } = [];
    public SplitCountryMode CountryMode { get; set; } = SplitCountryMode.IncludeAll;
    public string[] Countries { get; set; } = [];
    public bool UseIpViaApp { get; set; }
    public bool UseIpViaDevice { get; set; }
    public bool UseDomain { get; set; }
    public bool UseLocalNetwork { get; set; }
    public SplitDnsMode DnsMode { get; set; } = SplitDnsMode.IncludeAll;

    // The fate of a destination the server does not route: Exclude bypasses it, Block fails closed.
    // Not shown in the UI (the toggle speaks for ordinary users), but kept as a real option so the
    // API can still choose Block while splitting is allowed.
    public SplitUnsupportedIpMode UnsupportedIpMode { get; set; } = SplitUnsupportedIpMode.Exclude;

    // The fate of IPv6 when the server cannot carry the family at all: Block keeps it inside (dead but
    // private), Exclude lets the OS route it natively (working but visible to WebRTC/STUN probes).
    public SplitUnsupportedIpMode UnsupportedIpV6Mode { get; set; } = SplitUnsupportedIpMode.Block;

    // The single place BOTH gates are applied — the super toggle and the plan — so no consumer can
    // apply one and forget the other. A disabled copy keeps only what cannot leak and turns both
    // unsupported-ip modes to Block; an allowed copy keeps only the splits the current plan can
    // actually apply, because the connect path skips the rest and an effective copy must not promise
    // what will not happen. Neither gate touches the stored values. The result depends on the plan,
    // so it is a snapshot of "now" — call it fresh, never cache it across a purchase or a profile
    // switch. The checker is required on purpose: an overload without it would restore the very hole
    // this closes.
    public EffectiveSplitTunnelingSettings ToEffective(IPremiumFeatureChecker premiumFeatureChecker)
    {
        if (!Enabled)
            return new EffectiveSplitTunnelingSettings {
                Enabled = false,
                AppMode = AppMode,
                Apps = Apps,
                CountryMode = SplitCountryMode.IncludeAll,
                Countries = [],
                UseIpViaApp = false,
                UseIpViaDevice = false,
                UseDomain = false,
                UseLocalNetwork = UseLocalNetwork,
                DnsMode = SplitDnsMode.IncludeAll,
                UnsupportedIpMode = SplitUnsupportedIpMode.Block,
                UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block
            };

        // AppMode/Apps, UseLocalNetwork, DnsMode and the unsupported-ip modes have no AppFeature of
        // their own, so no plan can withhold them
        var isCountryAllowed = premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitCountry);
        return new EffectiveSplitTunnelingSettings {
            Enabled = true,
            AppMode = AppMode,
            Apps = Apps,
            CountryMode = isCountryAllowed ? CountryMode : SplitCountryMode.IncludeAll,
            Countries = isCountryAllowed ? Countries : [],
            UseIpViaApp = UseIpViaApp && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaApp),
            UseIpViaDevice = UseIpViaDevice && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaDevice),
            UseDomain = UseDomain && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitDomain),
            UseLocalNetwork = UseLocalNetwork,
            DnsMode = DnsMode,
            UnsupportedIpMode = UnsupportedIpMode,
            UnsupportedIpV6Mode = UnsupportedIpV6Mode
        };
    }
}
