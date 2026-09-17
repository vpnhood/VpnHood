using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.DtoConverters;
using VpnHood.AppLib.Premium;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Settings;

// What the split actually does, as against what it is set to: a setting the build does not sell,
// or this person has not paid for, is not in effect, and the UI is told so rather than being
// silently ignored. The shape is the contract's; the resolution is the app's.
public static class SplitTunnelingSettingsExtensions
{
    public static EffectiveSplitTunnelingSettings ToEffective(this SplitTunnelingSettings settings,
        IPremiumFeatureChecker premiumFeatureChecker)
    {
        if (!settings.Enabled)
            return new EffectiveSplitTunnelingSettings {
                Enabled = false,
                AppMode = settings.AppMode,
                Apps = settings.Apps,
                CountryMode = SplitCountryMode.IncludeAll,
                Countries = [],
                UseIpViaApp = false,
                UseIpViaDevice = false,
                UseDomain = false,
                UseLocalNetwork = settings.UseLocalNetwork,
                DnsMode = SplitDnsMode.IncludeAll,
                UnroutedIpMode = SplitUnsupportedIpMode.Block,
                UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block
            };

        // AppMode/Apps, UseLocalNetwork, DnsMode and the unsupported-ip modes have no AppFeature of
        // their own, so no plan can withhold them
        var isCountryAllowed = premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitCountry);
        return new EffectiveSplitTunnelingSettings {
            Enabled = true,
            AppMode = settings.AppMode,
            Apps = settings.Apps,
            CountryMode = isCountryAllowed ? settings.CountryMode : SplitCountryMode.IncludeAll,
            Countries = isCountryAllowed ? settings.Countries : [],
            UseIpViaApp = settings.UseIpViaApp && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaApp),
            UseIpViaDevice = settings.UseIpViaDevice && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitIpViaDevice),
            UseDomain = settings.UseDomain && premiumFeatureChecker.IsPremiumFeatureAllowed(AppFeature.SplitDomain),
            UseLocalNetwork = settings.UseLocalNetwork,
            DnsMode = settings.DnsMode.ToEngine(),
            UnroutedIpMode = settings.UnroutedIpMode.ToEngine(),
            // the general mode is superior: its Block kills unsupported IPv6 too, so the effective
            // copy never says "bypass IPv6" while the general mode fails closed — the state and the
            // reconnect diff read the resolved truth
            UnsupportedIpV6Mode = settings.UnroutedIpMode is VpnHood.AppLib.Api.SplitTunneling.SplitUnsupportedIpMode.Block
                ? SplitUnsupportedIpMode.Block
                : settings.UnsupportedIpV6Mode.ToEngine()
        };
    }
}
