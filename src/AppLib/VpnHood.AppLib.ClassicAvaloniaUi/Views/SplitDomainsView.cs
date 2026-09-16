using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using AppData = VpnHood.AppLib.AvaloniaUI.AppData;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.Proxies;
using VpnHood.AppLib.Contracts.SplitTunneling;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

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
        get => Settings.SplitTunneling.UseDomain;
        set => Settings.SplitTunneling.UseDomain = value;
    }

    // the server that runs no cloak undoes a domain filter; the switch says so under itself
    protected override string? SwitchWarning =>
        AppData.State.TcpProxyUsageReason == TcpProxyUsageReason.ServerRequiredOff ? Strings.Current.DomainFilterServerNoCloak : null;

    protected override async Task<(string Excludes, string Includes, string Blocks)> Load(CancellationToken cancellationToken)
    {
        var domains = await AppData.Api.App.GetSplitDomains(cancellationToken);
        return (domains.Excludes, domains.Includes, domains.Blocks);
    }

    protected override Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken)
    {
        return AppData.Api.App.SetSplitDomains(new SplitDomains { Excludes = excludes, Includes = includes, Blocks = blocks }, cancellationToken);
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseDomainFormat();
    }
}
