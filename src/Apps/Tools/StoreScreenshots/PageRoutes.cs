using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

namespace VpnHood.App.StoreScreenshots;

// The web UI's routes, as the Avalonia UI's pages: the screenshot config names a screen by the
// route the SPA showed it at (e2e/store/project.mjs), and this is the one place that says which
// view that is here. The home is the page the host starts on, so its route opens nothing.
internal static class PageRoutes
{
    private static readonly Dictionary<string, Func<MainView, IPage>> Pages = new(StringComparer.OrdinalIgnoreCase) {
        ["/servers"] = host => new LocationsView(host.ViewModel, host),
        ["/servers/extend-session"] = host => new ExtendSessionView(host),
        ["/protocols"] = host => new ProtocolsView(host),
        ["/protocols/cloak-mode"] = FeaturePages.CloakMode,
        ["/split-tunneling"] = host => new SplitTunnelingView(host),
        ["/split-tunneling/split-apps"] = host => new SplitAppsView(host),
        ["/split-tunneling/split-countries"] = host => new SplitCountriesView(host),
        ["/split-tunneling/split-dns"] = host => new SplitDnsView(host),
        ["/split-tunneling/split-ipv6"] = host => new SplitIpv6View(host),
        ["/split-tunneling/split-local-network"] = host => new SplitLocalNetworkView(host),
        ["/dns"] = DnsView.Create,
        ["/dns/private-dns"] = FeaturePages.PrivateDns,
        ["/settings"] = host => new SettingsView(host),
        ["/settings/language"] = host => new LanguageView(host),
        ["/settings/notifications"] = FeaturePages.Notifications,
        ["/settings/quick-launch"] = FeaturePages.QuickLaunch,
        ["/settings/kill-switch"] = FeaturePages.KillSwitch,
        ["/settings/always-on"] = FeaturePages.AlwaysOn,
        ["/settings/proxies"] = host => new ProxiesView(host),
        ["/settings/privacy"] = host => new PrivacyView(host),
        ["/statistics"] = host => new StatisticsView(host),
        ["/privacy-policy"] = host => new PrivacyPolicyView(host),
        ["/purchase-subscription"] = host => new PurchaseSubscriptionView(host, null),
        ["/user/account"] = host => new AccountView(host)
    };

    public static bool IsHome(string route) => route.TrimEnd('/') == "";

    public static IPage Open(string route, MainView host)
    {
        var key = route.TrimEnd('/');
        if (!Pages.TryGetValue(key, out var open))
            throw new ArgumentException($"No page is known for route '{route}'. Known: /, {string.Join(", ", Pages.Keys.Order())}.");
        var page = open(host);
        host.Navigate(page);
        return page;
    }
}
