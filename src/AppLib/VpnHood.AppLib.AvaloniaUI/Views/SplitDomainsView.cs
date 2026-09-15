using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Dtos;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// The web UI's split-domains: the domain lists, behind their switch; the pitch while the feature is
// sold and not bought (Create).
public sealed class SplitDomainsView : SplitListView
{
    private SplitDomainsView(MainView host) : base(host)
    {
        Initialize();
    }

    public static IPage Create(MainView host)
    {
        return AppData.IsPremiumFeatureAllowed(AppFeature.SplitDomain)
            ? new SplitDomainsView(host)
            : FeaturePages.PremiumPitch(host, Strings.Current.SplitDomains, Strings.Current.SplitDomainsDesc, "split-ip.webp", AppFeature.SplitDomain);
    }

    protected override string Title => Strings.Current.SplitDomains;
    protected override string? SwitchDescription => null;

    protected override bool IsSwitchOn {
        get => App.UserSettings.SplitTunneling.UseDomain;
        set => App.UserSettings.SplitTunneling.UseDomain = value;
    }

    // the server that runs no cloak undoes a domain filter; the switch says so under itself
    protected override string? SwitchWarning =>
        App.State.TcpProxyUsageReason == TcpProxyUsageReason.ServerRequiredOff ? Strings.Current.DomainFilterServerNoCloak : null;

    protected override (string Excludes, string Includes, string Blocks) Load()
    {
        var domains = App.SettingsService.SplitDomainSettings.Get();
        return (domains.Excludes, domains.Includes, domains.Blocks);
    }

    protected override void Save(string excludes, string includes, string blocks)
    {
        App.SettingsService.SplitDomainSettings.Set(new SplitDomains { Excludes = excludes, Includes = includes, Blocks = blocks });
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseDomainFormat();
    }
}
