using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

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
        return AppModel.IsPremiumFeatureAllowed(AppFeature.SplitDomain)
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
        AppModel.State.TcpProxyUsageReason == TcpProxyUsageReason.ServerRequiredOff ? Strings.Current.DomainFilterServerNoCloak : null;

    protected override async Task<(string Excludes, string Includes, string Blocks)> Load(CancellationToken cancellationToken)
    {
        var domains = await AppModel.Api.App.GetSplitDomains(cancellationToken);
        return (domains.Excludes, domains.Includes, domains.Blocks);
    }

    protected override Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken)
    {
        return AppModel.Api.App.SetSplitDomains(new SplitDomains { Excludes = excludes, Includes = includes, Blocks = blocks }, cancellationToken);
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseDomainFormat();
    }
}
