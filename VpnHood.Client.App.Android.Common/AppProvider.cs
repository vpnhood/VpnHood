using System;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Droid.Common;

public class AppProvider : IAppProvider
{
    public required IDevice Device { get; init; } 
    public bool IsLogToConsoleSupported => false;
    public Uri? AdditionalUiUrl => null;
    public Uri UpdateInfoUrl
    {
        get
        {
#if ANDROID_AAB
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json");
#else
            return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json");
#endif
        }
    }
}