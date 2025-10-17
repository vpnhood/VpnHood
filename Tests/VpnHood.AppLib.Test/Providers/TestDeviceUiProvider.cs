using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestDeviceUiProvider : NullDeviceUiProvider
{
    public PrivateDns? SystemPrivateDns { get; set; }
    public DeviceProxySettings? DeviceProxySettings { get; set; }

    public override bool IsProxySettingsSupported => true;
    public override DeviceProxySettings? GetProxySettings() => DeviceProxySettings;

    public override PrivateDns? GetSystemPrivateDns() => SystemPrivateDns;
    public override SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;
}