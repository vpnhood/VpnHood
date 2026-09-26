using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestDeviceUiProvider : NullDeviceUiProvider
{
    public PrivateDns? SystemPrivateDns { get; set; }
    public DeviceProxySettings? DeviceProxySettings { get; set; }

    public override bool IsProxySettingsSupported => true;
    public override DeviceProxySettings? GetProxySettings() => DeviceProxySettings;

    public override PrivateDns? GetPrivateDns() => SystemPrivateDns;
    public override SystemBarsInfo GetBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    // the UI the last settings request was carried out through: which window a request ran as
    public IUiContext? LastSettingsUiContext { get; private set; }
    public override void OpenSettings(IUiContext uiContext) => LastSettingsUiContext = uiContext;
}