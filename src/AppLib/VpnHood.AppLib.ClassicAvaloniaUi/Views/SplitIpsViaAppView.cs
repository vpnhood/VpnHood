using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// The web UI's split-ips-via-app: the IP lists the app applies itself, behind their switch; the
// pitch while the feature is sold and not bought (Create).
public sealed class SplitIpsViaAppView : SplitListView
{
    private SplitIpsViaAppView(MainView host) : base(host)
    {
        Initialize();
    }

    public static IPage Create(MainView host)
    {
        return AppModel.IsPremiumFeatureAllowed(AppFeature.SplitIpViaApp)
            ? new SplitIpsViaAppView(host)
            : FeaturePages.PremiumPitch(host, Strings.Current.SplitIpsViaApp, Strings.Current.SplitIpsViaAppDesc, "split-ip.webp", AppFeature.SplitIpViaApp);
    }

    protected override string Title => Strings.Current.SplitIpsViaApp;
    protected override string? SwitchDescription => Strings.Current.SplitIpsViaAppShortDesc;

    protected override bool IsSwitchOn {
        get => Settings.SplitTunneling.UseIpViaApp;
        set => Settings.SplitTunneling.UseIpViaApp = value;
    }

    protected override async Task<(string Excludes, string Includes, string Blocks)> Load(CancellationToken cancellationToken)
    {
        var ips = await AppModel.Api.App.GetSplitIpsViaApp(cancellationToken);
        return (ips.Excludes, ips.Includes, ips.Blocks);
    }

    // no disconnect: the app applies the new list to a running session
    protected override Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken)
    {
        return AppModel.Api.App.SetSplitIpsViaApp(new SplitIpsViaApp { Excludes = excludes, Includes = includes, Blocks = blocks }, cancellationToken);
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseIpFormat(hasBlocks: true);
    }
}
