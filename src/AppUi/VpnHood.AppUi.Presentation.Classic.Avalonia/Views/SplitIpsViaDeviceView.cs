using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;
using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

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
        return VhApp.IsPremiumFeatureAllowed(AppFeature.SplitIpViaDevice)
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
        var ips = await VhApp.Api.App.GetSplitIpsViaDevice(cancellationToken);
        return (ips.Excludes, ips.Includes, "");
    }

    protected override Task Save(string excludes, string includes, string blocks, CancellationToken cancellationToken)
    {
        return VhApp.Api.App.SetSplitIpsViaDevice(new SplitIpsViaDevice { Excludes = excludes, Includes = includes }, cancellationToken);
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseIpFormat(hasBlocks: false);
    }
}
