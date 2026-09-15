using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Dtos;

namespace VpnHood.AppLib.AvaloniaUI.Views;

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
        get => App.UserSettings.SplitTunneling.UseIpViaDevice;
        set => App.UserSettings.SplitTunneling.UseIpViaDevice = value;
    }

    protected override (string Excludes, string Includes, string Blocks) Load()
    {
        var ips = App.SettingsService.SplitIpViaDeviceSettings.Get();
        return (ips.Excludes, ips.Includes, "");
    }

    protected override void Save(string excludes, string includes, string blocks)
    {
        App.SettingsService.SplitIpViaDeviceSettings.Set(new SplitIpsViaDevice { Excludes = excludes, Includes = includes });
    }

    protected override void ConfigureInput(SplitListInput input)
    {
        input.UseIpFormat(hasBlocks: false);
    }
}
