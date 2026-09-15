using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Dtos;

namespace VpnHood.AppLib.AvaloniaUI.Views;

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
        return AppData.IsPremiumFeatureAllowed(AppFeature.SplitIpViaApp)
            ? new SplitIpsViaAppView(host)
            : FeaturePages.PremiumPitch(host, Strings.Current.SplitIpsViaApp, Strings.Current.SplitIpsViaAppDesc, "split-ip.webp", AppFeature.SplitIpViaApp);
    }

    protected override string Title => Strings.Current.SplitIpsViaApp;
    protected override string? SwitchDescription => Strings.Current.SplitIpsViaAppShortDesc;

    protected override bool IsSwitchOn {
        get => App.UserSettings.SplitTunneling.UseIpViaApp;
        set => App.UserSettings.SplitTunneling.UseIpViaApp = value;
    }

    protected override (string Excludes, string Includes, string Blocks) Load()
    {
        var ips = App.SettingsService.SplitIpViaAppSettings.Get();
        return (ips.Excludes, ips.Includes, ips.Blocks);
    }

    // no disconnect: the app applies the new list to a running session
    protected override void Save(string excludes, string includes, string blocks)
    {
        App.SettingsService.SplitIpViaAppSettings.Set(new SplitIpsViaApp { Excludes = excludes, Includes = includes, Blocks = blocks });
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseIpFormat(hasBlocks: true);
    }
}
