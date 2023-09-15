#nullable enable
using System;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid;

public class AppProvider : IAppProvider
{
    public IDevice Device { get; } = new AndroidDevice();
    public bool IsLogToConsoleSupported => false;
    public Uri? AdditionalUiUrl => null;
    public Uri UpdateInfoUrl => new("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json");

}