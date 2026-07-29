using System.Net;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Test.Tests;

// The super toggle: off must AND every split away (except the harmless LAN one) and fail closed,
// while the stored values survive for the day the user re-enables splitting.
[TestClass]
public class SplitTunnelingSettingsTest
{
    private static SplitTunnelingSettings CreateFullySplitSettings(bool enabled)
    {
        return new SplitTunnelingSettings {
            Enabled = enabled,
            AppMode = SplitAppMode.Exclude,
            Apps = ["com.example.app"],
            CountryMode = SplitCountryMode.ExcludeMyCountry,
            Countries = ["us"],
            UseIpViaApp = true,
            UseIpViaDevice = true,
            UseDomain = true,
            UseLocalNetwork = true,
            DnsMode = SplitDnsMode.DefaultRoute,
            UnroutedIpMode = SplitUnsupportedIpMode.Exclude,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude
        };
    }

    [TestMethod]
    public void Disabled_toggle_fails_closed_and_silences_every_split_except_apps_and_local_network()
    {
        var settings = CreateFullySplitSettings(enabled: false);
        var effective = settings.ToEffective(PremiumFeatureChecker.AllowAll);

        Assert.AreEqual(SplitUnsupportedIpMode.Block, effective.UnroutedIpMode, "server misses fail closed");
        Assert.AreEqual(SplitUnsupportedIpMode.Block, effective.UnsupportedIpV6Mode, "IPv6 fails closed too");
        Assert.AreEqual(SplitCountryMode.IncludeAll, effective.CountryMode);
        Assert.IsFalse(effective.UseIpViaApp);
        Assert.IsFalse(effective.UseIpViaDevice);
        Assert.IsFalse(effective.UseDomain);
        Assert.AreEqual(SplitDnsMode.IncludeAll, effective.DnsMode);

        // the two exempt splits: neither can expose the IP of the traffic that stays in the tunnel
        Assert.IsTrue(effective.UseLocalNetwork, "LAN traffic never reaches the internet");
        Assert.AreEqual(SplitAppMode.Exclude, effective.AppMode, "a per-app opt-out survives the toggle");
        CollectionAssert.AreEqual(settings.Apps, effective.Apps);

        // the stored values must survive untouched for re-enabling
        Assert.AreEqual(SplitCountryMode.ExcludeMyCountry, settings.CountryMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, settings.UnroutedIpMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, settings.UnsupportedIpV6Mode);
    }

    [TestMethod]
    public void A_plan_that_withholds_a_split_resolves_it_away()
    {
        // the plan gate lives in ToEffective, so every consumer — connect mapping, split db
        // services, state, the tcp-proxy reason — reads a split the plan withholds as simply off
        var settings = CreateFullySplitSettings(enabled: true);
        var effective = settings.ToEffective(PremiumFeatureChecker.RefuseAll);

        Assert.IsFalse(effective.UseIpViaApp);
        Assert.IsFalse(effective.UseIpViaDevice);
        Assert.IsFalse(effective.UseDomain);
        Assert.AreEqual(SplitCountryMode.IncludeAll, effective.CountryMode);
        Assert.AreEqual(0, effective.Countries.Length);

        // features no plan can withhold (they have no AppFeature of their own) are untouched
        Assert.AreEqual(SplitAppMode.Exclude, effective.AppMode);
        Assert.IsTrue(effective.UseLocalNetwork);
        Assert.AreEqual(SplitDnsMode.DefaultRoute, effective.DnsMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, effective.UnroutedIpMode);

        // and the stored values survive for the day the plan allows them again
        Assert.IsTrue(settings.UseDomain);
        Assert.AreEqual(SplitCountryMode.ExcludeMyCountry, settings.CountryMode);
    }

    [TestMethod]
    public void Split_ip_via_device_or_app_raises_the_split_badge()
    {
        // the reported scenario: both IP splits on, nothing else — the home badge must appear
        var userSettings = new UserSettings {
            SplitTunneling = new SplitTunnelingSettings {
                Enabled = true,
                UseIpViaDevice = true,
                UseIpViaApp = true
            }
        };

        var state = StateHelper.GetSplitTunnelingState(userSettings, sessionInfo: null, PremiumFeatureChecker.AllowAll);
        Assert.IsTrue(state.IsIpViaDeviceSplit);
        Assert.IsTrue(state.IsIpViaAppSplit);
        Assert.IsTrue(state.IsSplittingTraffic, "the badge must appear for IP splits");

        // each one alone is enough
        userSettings.SplitTunneling.UseIpViaApp = false;
        Assert.IsTrue(StateHelper.GetSplitTunnelingState(userSettings, null, PremiumFeatureChecker.AllowAll).IsSplittingTraffic);
        userSettings.SplitTunneling.UseIpViaApp = true;
        userSettings.SplitTunneling.UseIpViaDevice = false;
        Assert.IsTrue(StateHelper.GetSplitTunnelingState(userSettings, null, PremiumFeatureChecker.AllowAll).IsSplittingTraffic);

        // ...but a plan that can not apply them reports no split, because the connect path skips them
        Assert.IsFalse(StateHelper.GetSplitTunnelingState(userSettings, null, PremiumFeatureChecker.RefuseAll).IsSplittingTraffic);
    }

    [TestMethod]
    public void Enabled_toggle_passes_everything_through_including_the_stored_modes()
    {
        var settings = CreateFullySplitSettings(enabled: true);
        var effective = settings.ToEffective(PremiumFeatureChecker.AllowAll);

        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, effective.UnroutedIpMode);
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, effective.UnsupportedIpV6Mode);
        Assert.AreEqual(SplitAppMode.Exclude, effective.AppMode);
        Assert.AreEqual(SplitDnsMode.DefaultRoute, effective.DnsMode);
        CollectionAssert.AreEqual(settings.Apps, effective.Apps);
    }

    // the plan's answer, without the app: allowed everywhere, or refused everywhere
    private sealed class PremiumFeatureChecker(bool isAllowed) : IPremiumFeatureChecker
    {
        public static readonly IPremiumFeatureChecker AllowAll = new PremiumFeatureChecker(true);
        public static readonly IPremiumFeatureChecker RefuseAll = new PremiumFeatureChecker(false);

        public bool IsPremiumFeatureAllowed(AppFeature feature) => isAllowed;
    }

    // a connected session that reveals what the server can and cannot carry
    private static SessionInfo CreateSessionInfo(bool isIpV6SupportedByServer, bool isTrafficSplitByServer = false)
    {
        return new SessionInfo {
            SessionId = "1",
            AccessInfo = null,
            CreatedTime = DateTime.UtcNow,
            IsUdpChannelSupported = true,
            IsQuicChannelSupported = false,
            IsTcpPacketSupported = true,
            IsTcpProxySupported = true,
            IsLocalNetworkAllowed = false,
            ServerLocationInfo = null,
            IsPremiumSession = false,
            SuppressedTo = SessionSuppressType.None,
            AdRequirement = AdRequirement.None,
            ChannelProtocols = [ChannelProtocol.Tcp],
            AccessKey = null,
            ClientCountry = null,
            DnsConfig = new DnsConfig {
                DnsSelection = DnsSelection.ServerDns,
                IsIncludedInVpn = true,
                IsUserSuppressed = false,
                DnsServers = [IPAddress.Parse("8.8.8.8")]
            },
            IsTrafficSplitByServer = isTrafficSplitByServer,
            IsIpV6SupportedByServer = isIpV6SupportedByServer,
            ServerVersion = new Version(1, 0, 0),
            ClientPublicIpAddress = IPAddress.Parse("1.2.3.4")
        };
    }

    [TestMethod]
    public void Bypassed_ipv6_on_a_v4_only_server_raises_the_split_badge()
    {
        // the user allows splitting and chose to let IPv6 out; the server cannot carry it, so IPv6
        // really is travelling outside the tunnel — the badge must say so even with no other split
        var userSettings = new UserSettings {
            SplitTunneling = new SplitTunnelingSettings {
                Enabled = true,
                UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude
            }
        };

        var state = StateHelper.GetSplitTunnelingState(userSettings,
            CreateSessionInfo(isIpV6SupportedByServer: false), PremiumFeatureChecker.AllowAll);
        Assert.IsTrue(state.IsIpV6Split);
        Assert.IsTrue(state.IsSplittingTraffic, "the badge must appear for bypassed IPv6 alone");

        // ...but not when the server carries IPv6 (nothing is unsupported to bypass)
        var v6ServerState = StateHelper.GetSplitTunnelingState(userSettings,
            CreateSessionInfo(isIpV6SupportedByServer: true), PremiumFeatureChecker.AllowAll);
        Assert.IsFalse(v6ServerState.IsIpV6Split);
        Assert.IsFalse(v6ServerState.IsSplittingTraffic);

        // ...nor when IPv6 is blocked rather than bypassed
        userSettings.SplitTunneling.UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block;
        var blockedState = StateHelper.GetSplitTunnelingState(userSettings,
            CreateSessionInfo(isIpV6SupportedByServer: false), PremiumFeatureChecker.AllowAll);
        Assert.IsFalse(blockedState.IsIpV6Split);
        Assert.IsFalse(blockedState.IsSplittingTraffic);

        // ...nor when the super toggle is off, which forces Block no matter what is stored
        userSettings.SplitTunneling.UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude;
        userSettings.SplitTunneling.Enabled = false;
        var disabledState = StateHelper.GetSplitTunnelingState(userSettings,
            CreateSessionInfo(isIpV6SupportedByServer: false), PremiumFeatureChecker.AllowAll);
        Assert.IsFalse(disabledState.IsIpV6Split);
        Assert.IsFalse(disabledState.IsSplittingTraffic);
    }

    [TestMethod]
    public void A_general_block_is_superior_and_covers_ipv6()
    {
        // UnroutedIpMode is the super flag of the pair: its Block must kill unsupported IPv6 too,
        // so ToEffective resolves the v6 mode to Block and every consumer — core options, state,
        // reconnect diff — reads that one truth instead of re-deriving the rule
        var settings = new SplitTunnelingSettings {
            Enabled = true,
            UnroutedIpMode = SplitUnsupportedIpMode.Block,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude
        };

        var effective = settings.ToEffective(PremiumFeatureChecker.AllowAll);
        Assert.AreEqual(SplitUnsupportedIpMode.Block, effective.UnsupportedIpV6Mode,
            "the general Block overrides the stored bypass");
        Assert.AreEqual(SplitUnsupportedIpMode.Exclude, settings.UnsupportedIpV6Mode,
            "the stored value survives for the day the general mode relaxes");

        // no bypass actually happens, so a v4-only server must not raise the v6 split badge
        var userSettings = new UserSettings { SplitTunneling = settings };
        var state = StateHelper.GetSplitTunnelingState(userSettings,
            CreateSessionInfo(isIpV6SupportedByServer: false), PremiumFeatureChecker.AllowAll);
        Assert.IsFalse(state.IsIpV6Split);
        Assert.IsFalse(state.IsSplittingTraffic);
    }

    [TestMethod]
    public void State_reports_no_splitting_while_the_toggle_is_off()
    {
        var userSettings = new UserSettings { SplitTunneling = CreateFullySplitSettings(enabled: false) };
        var state = StateHelper.GetSplitTunnelingState(userSettings, sessionInfo: null, PremiumFeatureChecker.AllowAll);

        Assert.IsFalse(state.IsEnabled);
        Assert.IsFalse(state.IsSplittingTraffic, "the badge must die by itself");
        Assert.IsFalse(state.IsCountrySplit);
        Assert.IsFalse(state.IsIpViaAppSplit);
        Assert.IsFalse(state.IsIpViaDeviceSplit);
        Assert.IsFalse(state.IsDomainSplit);
        Assert.IsFalse(state.IsIpV6Split);
        Assert.IsFalse(state.IsSplitByServer);

        // the exempt pair stays on for its pages, and neither ever joins the badge
        Assert.IsTrue(state.IsLocalNetworkSplit);
        Assert.IsTrue(state.IsAppSplit);
    }

    [TestMethod]
    public void State_reports_splitting_while_the_toggle_is_on_but_premium_gates_still_count()
    {
        var userSettings = new UserSettings { SplitTunneling = CreateFullySplitSettings(enabled: true) };

        var state = StateHelper.GetSplitTunnelingState(userSettings, sessionInfo: null, PremiumFeatureChecker.AllowAll);
        Assert.IsTrue(state.IsSplittingTraffic);
        Assert.IsTrue(state.IsAppSplit);
        Assert.IsTrue(state.IsDomainSplit);
        Assert.IsFalse(state.IsIpV6Split, "no session has revealed a v4-only server yet");

        // a free plan cannot actually apply the premium splits, so they must not be reported
        var gatedState = StateHelper.GetSplitTunnelingState(userSettings, sessionInfo: null, PremiumFeatureChecker.RefuseAll);
        Assert.IsFalse(gatedState.IsCountrySplit);
        Assert.IsFalse(gatedState.IsIpViaAppSplit);
        Assert.IsFalse(gatedState.IsIpViaDeviceSplit);
        Assert.IsFalse(gatedState.IsDomainSplit);
        Assert.IsTrue(gatedState.IsAppSplit, "the free per-app split still applies...");
        Assert.IsFalse(gatedState.IsSplittingTraffic, "...but it never raises the badge on its own");
    }
}
