using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using AppData = VpnHood.AppLib.AvaloniaUI.AppData;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.SplitTunneling;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// The web UI's split-ips-via-device: the IP lists handed to the device's own routing, behind their
// switch - no block list, the device cannot drop; the pitch while the feature is sold and not
// bought (Create).
public sealed class SplitIpsViaDeviceView : SplitListView
{
    private SplitIpsViaDeviceView(MainView host) : base(host)
    {
        Initialize();
    }

    public static IPage Create(MainView host)
    {
        return AppData.IsPremiumFeatureAllowed(AppFeature.SplitIpViaDevice)
            ? new SplitIpsViaDeviceView(host)
            : FeaturePages.PremiumPitch(host, Strings.Current.SplitIpsViaDevice, Strings.Current.SplitIpsViaDeviceDesc, "split-ip.webp", AppFeature.SplitIpViaDevice);
    }

    protected override string Title => Strings.Current.SplitIpsViaDevice;
    protected override string? SwitchDescription => Strings.Current.SplitIpsViaDeviceShortDesc;

    protected override bool IsSwitchOn {
        get => Settings.SplitTunneling.UseIpViaDevice;
        set => Settings.SplitTunneling.UseIpViaDevice = value;
    }

    protected override async Task<(string Excludes, string Includes, string Blocks)> Load(CancellationToken cancellationToken)
    {
        var ips = await AppData.Api.App.GetSplitIpsViaDevice(cancellationToken);
        return (ips.Excludes, ips.Includes, "");
    }

    protected override Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken)
    {
        return AppData.Api.App.SetSplitIpsViaDevice(new SplitIpsViaDevice { Excludes = excludes, Includes = includes }, cancellationToken);
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseIpFormat(hasBlocks: false);
    }
}
